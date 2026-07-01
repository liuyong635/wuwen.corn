using SeedCut.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// 激光器调试ViewModel
    /// </summary>
    public class LaserDebugViewModel : INotifyPropertyChanged
    {
        private readonly ILaserService _laserService;

        public event PropertyChangedEventHandler PropertyChanged;

        #region 属性

        private bool _isServerStarted;
        public bool IsServerStarted
        {
            get => _isServerStarted;
            set => SetProperty(ref _isServerStarted, value);
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        private string _connectionStatus = "服务器未启动";
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        private Brush _connectionIndicatorBrush = Brushes.Gray;
        public Brush ConnectionIndicatorBrush
        {
            get => _connectionIndicatorBrush;
            set => SetProperty(ref _connectionIndicatorBrush, value);
        }

        private bool _isMarking;
        public bool IsMarking
        {
            get => _isMarking;
            set => SetProperty(ref _isMarking, value);
        }

        private int _markProgress;
        public int MarkProgress
        {
            get => _markProgress;
            set => SetProperty(ref _markProgress, value);
        }

        private int _queueLength;
        public int QueueLength
        {
            get => _queueLength;
            set => SetProperty(ref _queueLength, value);
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _customCommand = "Mark;";
        public string CustomCommand
        {
            get => _customCommand;
            set => SetProperty(ref _customCommand, value);
        }

        // 快速测试参数
        private string _testCoordinates = "-5,0,5,0;0,-5,0,5";
        public string TestCoordinates
        {
            get => _testCoordinates;
            set => SetProperty(ref _testCoordinates, value);
        }

        private string _hsdFilePath = "C:\\Users\\26671\\Desktop\\玉米01.hsd";
        public string HsdFilePath
        {
            get => _hsdFilePath;
            set => SetProperty(ref _hsdFilePath, value);
        }

        private int _layerId = 1;
        public int LayerId
        {
            get => _layerId;
            set => SetProperty(ref _layerId, value);
        }

        private int _laserPower = 50;
        public int LaserPower
        {
            get => _laserPower;
            set => SetProperty(ref _laserPower, value);
        }

        // 日志集合
        public ObservableCollection<LaserLogEntry> Logs { get; }

        // 配置信息显示
        public int ServerPort => _laserService.Config.Port;
        public string AutoSendMark => _laserService.Config.AutoSendMark ? "启用" : "禁用";

        #endregion

        #region 命令

        public ICommand StartServerCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand SendCommandCommand { get; }

        // 基础指令
        public ICommand MarkCommand { get; }
        public ICommand StopMarkCommand { get; }
        public ICommand GetProgressCommand { get; }

        // 测试指令
        public ICommand TestAddLinesCommand { get; }
        public ICommand TestAddAreasCommand { get; }
        public ICommand LoadHsdFileCommand { get; }
        public ICommand SetLaserPowerCommand { get; }

        // 队列管理
        public ICommand ClearQueueCommand { get; }
        public ICommand ClearLogsCommand { get; }

        #endregion

        public LaserDebugViewModel(ILaserService laserService)
        {
            _laserService = laserService ?? throw new ArgumentNullException(nameof(laserService));
            Logs = new ObservableCollection<LaserLogEntry>();

            // 订阅服务事件
            _laserService.ConnectionChanged += OnConnectionChanged;
            _laserService.ResponseReceived += OnResponseReceived;
            _laserService.MarkFinished += OnMarkFinished;
            _laserService.StatusChanged += OnStatusChanged;
            _laserService.ErrorOccurred += OnErrorOccurred;

            // 初始化命令
            StartServerCommand = new RelayCommand(
                async () => await StartServerAsync(),
                () => !IsServerStarted
            );

            DisconnectCommand = new RelayCommand(
                async () => await DisconnectAsync(),
                () => IsServerStarted
            );

            SendCommandCommand = new RelayCommand(
                async () => await SendCustomCommandAsync(),
                () => IsConnected && !string.IsNullOrWhiteSpace(CustomCommand)
            );

            MarkCommand = new RelayCommand(
                async () => await _laserService.MarkAsync(),
                () => IsConnected && !IsMarking
            );

            StopMarkCommand = new RelayCommand(
                async () => await _laserService.StopMarkAsync(),
                () => IsConnected && IsMarking
            );

            GetProgressCommand = new RelayCommand(
                async () => await _laserService.GetMarkProgressAsync(),
                () => IsConnected
            );

            TestAddLinesCommand = new RelayCommand(
                async () => await _laserService.AddLinesAsync(TestCoordinates),
                () => IsConnected && !string.IsNullOrWhiteSpace(TestCoordinates)
            );

            TestAddAreasCommand = new RelayCommand(
                async () => await _laserService.AddAreasAsync(TestCoordinates),
                () => IsConnected && !string.IsNullOrWhiteSpace(TestCoordinates)
            );

            LoadHsdFileCommand = new RelayCommand(
                async () => await _laserService.LoadHsdFileAsync(HsdFilePath),
                () => IsConnected && !string.IsNullOrWhiteSpace(HsdFilePath)
            );

            SetLaserPowerCommand = new RelayCommand(
                async () => await _laserService.SetLaserPowerAsync(LayerId, LaserPower),
                () => IsConnected
            );

            ClearQueueCommand = new RelayCommand(
                () => _laserService.ClearQueue(),
                () => IsConnected && QueueLength > 0
            );

            ClearLogsCommand = new RelayCommand(
                () => Logs.Clear()
            );

            UpdateConnectionStatus();
        }

        #region 命令方法

        private async System.Threading.Tasks.Task StartServerAsync()
        {
            var success = await _laserService.StartServerAsync();
            if (success)
            {
                IsServerStarted = true;
                AddLog("✓ TCP服务器已启动，等待激光器连接...", LogLevel.Info);
            }
            else
            {
                AddLog("✗ TCP服务器启动失败", LogLevel.Error);
            }

            RaiseCommandsCanExecuteChanged();
        }

        private async System.Threading.Tasks.Task DisconnectAsync()
        {
            await _laserService.DisconnectAsync();
            IsServerStarted = false;
            IsConnected = false;
            IsMarking = false;
            AddLog("✓ 已断开连接", LogLevel.Info);
            RaiseCommandsCanExecuteChanged();
        }

        private async System.Threading.Tasks.Task SendCustomCommandAsync()
        {
            await _laserService.SendCommandAsync(CustomCommand);
            AddLog($"→ {CustomCommand}", LogLevel.Info);
        }

        #endregion

        #region 事件处理

        private void OnConnectionChanged(object sender, bool isConnected)
        {
            IsConnected = isConnected;
            UpdateConnectionStatus();

            if (isConnected)
            {
                AddLog("✓ 激光器已连接", LogLevel.Success);
            }
            else
            {
                AddLog("✗ 激光器已断开", LogLevel.Warning);
                IsMarking = false;
                MarkProgress = 0;
            }

            RaiseCommandsCanExecuteChanged();
        }

        private void OnResponseReceived(object sender, LaserResponseEventArgs e)
        {
            AddLog($"← {e.Response}", LogLevel.Info);

            // 更新进度
            if (e.ResponseType == LaserResponseType.Progress)
            {
                if (int.TryParse(e.Response, out int progress))
                {
                    MarkProgress = progress;
                }
            }
        }

        private void OnMarkFinished(object sender, LaserMarkFinishedEventArgs e)
        {
            IsMarking = false;
            MarkProgress = e.Success ? 100 : 0;

            if (e.Success)
            {
                AddLog($"✓ 打标完成，耗时: {e.ElapsedMs}ms", LogLevel.Success);
            }
            else
            {
                AddLog($"✗ 打标失败: {e.Message}", LogLevel.Error);
            }

            RaiseCommandsCanExecuteChanged();
        }

        private void OnStatusChanged(object sender, string message)
        {
            StatusMessage = message;

            // 更新队列长度
            QueueLength = _laserService.QueueLength;

            // 更新打标状态
            IsMarking = _laserService.IsMarking;
            MarkProgress = _laserService.MarkProgress;

            RaiseCommandsCanExecuteChanged();
        }

        private void OnErrorOccurred(object sender, string message)
        {
            AddLog($"✗ 错误: {message}", LogLevel.Error);
        }

        private void UpdateConnectionStatus()
        {
            if (!IsServerStarted)
            {
                ConnectionStatus = "服务器未启动";
                ConnectionIndicatorBrush = Brushes.Gray;
            }
            else if (!IsConnected)
            {
                ConnectionStatus = $"等待连接（端口 {ServerPort}）";
                ConnectionIndicatorBrush = Brushes.Orange;
            }
            else
            {
                ConnectionStatus = "激光器已连接";
                ConnectionIndicatorBrush = Brushes.Green;
            }
        }

        private void AddLog(string message, LogLevel level)
        {
            // ✅ 修复：使用 BeginInvoke 替代 Invoke，避免跨线程死锁
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    Logs.Insert(0, new LaserLogEntry
                    {
                        Timestamp = DateTime.Now,
                        Message = message,
                        Level = level
                    });

                    // 限制日志数量
                    while (Logs.Count > 200)
                    {
                        Logs.RemoveAt(Logs.Count - 1);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[激光器] 添加日志异常: {ex.Message}");
                }
            }));
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            (StartServerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SendCommandCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MarkCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StopMarkCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (GetProgressCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (TestAddLinesCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (TestAddAreasCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (LoadHsdFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SetLaserPowerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ClearQueueCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        #endregion

        #region INotifyPropertyChanged

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }

    /// <summary>
    /// 激光日志条目
    /// </summary>
    public class LaserLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public LogLevel Level { get; set; }

        public string TimeString => Timestamp.ToString("HH:mm:ss.fff");
    }

    
}