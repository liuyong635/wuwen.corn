using SeedCut.Models;
using SeedCut.Services;
using SeedCut.Services.HM;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// HM激光器调试ViewModel（扩展版）
    /// 
    /// 特性：
    /// 1. 支持所有激光参数的实时调整
    /// 2. 保存配置时同时更新文件和内存
    /// 3. 参数分组管理，界面清晰
    /// 4. 支持配置验证和重置
    /// </summary>
    public class HM_LaserDebugViewModel : INotifyPropertyChanged
    {
        #region 私有字段

        private readonly HM_LaserService _service;
        private bool _hasUnsavedChanges;

        #endregion

        #region 事件

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 配置已应用事件（通知其他组件配置已更新）
        /// </summary>
        public event EventHandler ConfigApplied;

        #endregion

        #region 连接相关属性

        private string _ipAddress;
        public string IpAddress
        {
            get => _ipAddress;
            set
            {
                if (SetProperty(ref _ipAddress, value))
                {
                    HasUnsavedChanges = true;
                }
            }
        }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    UpdateConnectionStatus();
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        private string _connectionStatus = "未连接";
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        private Brush _connectionIndicator = Brushes.Gray;
        public Brush ConnectionIndicator
        {
            get => _connectionIndicator;
            set => SetProperty(ref _connectionIndicator, value);
        }

        #endregion

        #region 振镜参数属性

        public uint MarkSpeed
        {
            get => _service.Config.MarkSpeed;
            set
            {
                if (_service.Config.MarkSpeed != value)
                {
                    _service.Config.MarkSpeed = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConfigSummary));
                    HasUnsavedChanges = true;
                }
            }
        }

        public uint JumpSpeed
        {
            get => _service.Config.JumpSpeed;
            set
            {
                if (_service.Config.JumpSpeed != value)
                {
                    _service.Config.JumpSpeed = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public uint MarkDelay
        {
            get => _service.Config.MarkDelay;
            set
            {
                if (_service.Config.MarkDelay != value)
                {
                    _service.Config.MarkDelay = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public uint JumpDelay
        {
            get => _service.Config.JumpDelay;
            set
            {
                if (_service.Config.JumpDelay != value)
                {
                    _service.Config.JumpDelay = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public uint PolygonDelay
        {
            get => _service.Config.PolygonDelay;
            set
            {
                if (_service.Config.PolygonDelay != value)
                {
                    _service.Config.PolygonDelay = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public uint MarkCount
        {
            get => _service.Config.MarkCount;
            set
            {
                if (_service.Config.MarkCount != value)
                {
                    _service.Config.MarkCount = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        #endregion

        #region 激光参数属性

        public float LaserPower
        {
            get => _service.Config.LaserPower;
            set
            {
                value = Math.Max(0, Math.Min(100, value)); // 限制0-100
                if (Math.Abs(_service.Config.LaserPower - value) > 0.001f)
                {
                    _service.Config.LaserPower = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConfigSummary));
                    HasUnsavedChanges = true;
                }
            }
        }

        public float Frequency
        {
            get => _service.Config.Frequency;
            set
            {
                if (Math.Abs(_service.Config.Frequency - value) > 0.001f)
                {
                    _service.Config.Frequency = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConfigSummary));
                    HasUnsavedChanges = true;
                }
            }
        }

        public float DutyCycle
        {
            get => _service.Config.DutyCycle;
            set
            {
                value = Math.Max(0, Math.Min(1, value)); // 限制0-1
                if (Math.Abs(_service.Config.DutyCycle - value) > 0.001f)
                {
                    _service.Config.DutyCycle = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DutyCyclePercent));
                    HasUnsavedChanges = true;
                }
            }
        }

        /// <summary>
        /// 占空比百分比显示（0-100）
        /// </summary>
        public float DutyCyclePercent
        {
            get => _service.Config.DutyCycle * 100;
            set
            {
                DutyCycle = value / 100f;
            }
        }

        public float LaserOnDelay
        {
            get => _service.Config.LaserOnDelay;
            set
            {
                if (Math.Abs(_service.Config.LaserOnDelay - value) > 0.001f)
                {
                    _service.Config.LaserOnDelay = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public float LaserOffDelay
        {
            get => _service.Config.LaserOffDelay;
            set
            {
                if (Math.Abs(_service.Config.LaserOffDelay - value) > 0.001f)
                {
                    _service.Config.LaserOffDelay = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public float FPKDelay
        {
            get => _service.Config.FPKDelay;
            set
            {
                if (Math.Abs(_service.Config.FPKDelay - value) > 0.001f)
                {
                    _service.Config.FPKDelay = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public float FPKLength
        {
            get => _service.Config.FPKLength;
            set
            {
                if (Math.Abs(_service.Config.FPKLength - value) > 0.001f)
                {
                    _service.Config.FPKLength = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public float StandbyFrequency
        {
            get => _service.Config.StandbyFrequency;
            set
            {
                if (Math.Abs(_service.Config.StandbyFrequency - value) > 0.001f)
                {
                    _service.Config.StandbyFrequency = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public float StandbyDutyCycle
        {
            get => _service.Config.StandbyDutyCycle;
            set
            {
                value = Math.Max(0, Math.Min(1, value));
                if (Math.Abs(_service.Config.StandbyDutyCycle - value) > 0.001f)
                {
                    _service.Config.StandbyDutyCycle = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        #endregion

        #region 工作区域属性

        public double WorkAreaMinX
        {
            get => _service.Config.WorkAreaMinX;
            set
            {
                if (Math.Abs(_service.Config.WorkAreaMinX - value) > 0.001)
                {
                    _service.Config.WorkAreaMinX = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConfigSummary));
                    HasUnsavedChanges = true;
                }
            }
        }

        public double WorkAreaMaxX
        {
            get => _service.Config.WorkAreaMaxX;
            set
            {
                if (Math.Abs(_service.Config.WorkAreaMaxX - value) > 0.001)
                {
                    _service.Config.WorkAreaMaxX = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConfigSummary));
                    HasUnsavedChanges = true;
                }
            }
        }

        public double WorkAreaMinY
        {
            get => _service.Config.WorkAreaMinY;
            set
            {
                if (Math.Abs(_service.Config.WorkAreaMinY - value) > 0.001)
                {
                    _service.Config.WorkAreaMinY = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConfigSummary));
                    HasUnsavedChanges = true;
                }
            }
        }

        public double WorkAreaMaxY
        {
            get => _service.Config.WorkAreaMaxY;
            set
            {
                if (Math.Abs(_service.Config.WorkAreaMaxY - value) > 0.001)
                {
                    _service.Config.WorkAreaMaxY = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConfigSummary));
                    HasUnsavedChanges = true;
                }
            }
        }

        public bool EnableCoordinateCheck
        {
            get => _service.Config.EnableCoordinateCheck;
            set
            {
                if (_service.Config.EnableCoordinateCheck != value)
                {
                    _service.Config.EnableCoordinateCheck = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        #endregion

        #region 高级参数属性

        public int Protocol
        {
            get => _service.Config.Protocol;
            set
            {
                if (_service.Config.Protocol != value)
                {
                    _service.Config.Protocol = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProtocolName));
                    HasUnsavedChanges = true;
                }
            }
        }

        public string ProtocolName
        {
            get
            {
                switch (Protocol)
                {
                    case 0: return "SPI";
                    case 1: return "XY2-100";
                    case 2: return "SL2";
                    default: return "未知";
                }
            }
        }

        public int Dimensional
        {
            get => _service.Config.Dimensional;
            set
            {
                if (_service.Config.Dimensional != value)
                {
                    _service.Config.Dimensional = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DimensionalName));
                    HasUnsavedChanges = true;
                }
            }
        }

        public string DimensionalName => Dimensional == 0 ? "2D" : "3D";

        public bool JumpToZeroAfterMark
        {
            get => _service.Config.JumpToZeroAfterMark;
            set
            {
                if (_service.Config.JumpToZeroAfterMark != value)
                {
                    _service.Config.JumpToZeroAfterMark = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public bool UseAnalogPower
        {
            get => _service.Config.UseAnalogPower;
            set
            {
                if (_service.Config.UseAnalogPower != value)
                {
                    _service.Config.UseAnalogPower = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public uint Waveform
        {
            get => _service.Config.Waveform;
            set
            {
                value = Math.Min(63, value); // 限制0-63
                if (_service.Config.Waveform != value)
                {
                    _service.Config.Waveform = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public bool EnableMOPA
        {
            get => _service.Config.EnableMOPA;
            set
            {
                if (_service.Config.EnableMOPA != value)
                {
                    _service.Config.EnableMOPA = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public uint MOPAPulseWidth
        {
            get => _service.Config.MOPAPulseWidth;
            set
            {
                if (_service.Config.MOPAPulseWidth != value)
                {
                    _service.Config.MOPAPulseWidth = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        #endregion

        #region 超时参数属性

        public int ConnectionTimeoutMs
        {
            get => _service.Config.ConnectionTimeoutMs;
            set
            {
                if (_service.Config.ConnectionTimeoutMs != value)
                {
                    _service.Config.ConnectionTimeoutMs = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public int DownloadTimeoutMs
        {
            get => _service.Config.DownloadTimeoutMs;
            set
            {
                if (_service.Config.DownloadTimeoutMs != value)
                {
                    _service.Config.DownloadTimeoutMs = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public int MarkTimeoutMs
        {
            get => _service.Config.MarkTimeoutMs;
            set
            {
                if (_service.Config.MarkTimeoutMs != value)
                {
                    _service.Config.MarkTimeoutMs = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        #endregion

        #region 状态属性

        private bool _isMarking;
        public bool IsMarking
        {
            get => _isMarking;
            set
            {
                if (SetProperty(ref _isMarking, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        private int _markProgress;
        public int MarkProgress
        {
            get => _markProgress;
            set => SetProperty(ref _markProgress, value);
        }

        private string _testCoordinates = "-10,0,10,0;0,-10,0,10";

        private string _areaCoordinates = "AddAreas[-5,-3,5,-3:5,-3,5,3:5,3,-5,3:-5,3,-5,-3]";
        /// <summary>
        /// 区域填充坐标 (格式: AddAreas[x1,y1,x2,y2:x3,y3,x4,y4:...])
        /// </summary>
        public string AreaCoordinates
        {
            get => _areaCoordinates;
            set => SetProperty(ref _areaCoordinates, value);
        }

        private float _fillLineSpacing = 0.1f;
        /// <summary>
        /// 填充线间距 (mm)
        /// </summary>
        public float FillLineSpacing
        {
            get => _service.Config.FillLineSpacing;
            set
            {
                if (Math.Abs(_service.Config.FillLineSpacing - value) > 0.0001f)
                {
                    _service.Config.FillLineSpacing = value;
                    OnPropertyChanged();
                    HasUnsavedChanges = true;
                }
            }
        }

        public string TestCoordinates
        {
            get => _testCoordinates;
            set => SetProperty(ref _testCoordinates, value);
        }

        private string _statusMessage = "就绪";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _workStatus = "未知";
        public string WorkStatus
        {
            get => _workStatus;
            set => SetProperty(ref _workStatus, value);
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => SetProperty(ref _hasUnsavedChanges, value);
        }

        public string ConfigSummary =>
            $"速度:{_service.Config.MarkSpeed}mm/s  功率:{_service.Config.LaserPower}%  " +
            $"频率:{_service.Config.Frequency}kHz  区域:{_service.Config.WorkAreaWidth}x{_service.Config.WorkAreaHeight}mm";

        public ObservableCollection<HM_LogEntry> Logs { get; }

        #endregion

        #region 命令

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand TestMarkCommand { get; }
        public ICommand StopMarkCommand { get; }
        public ICommand RedLightOnCommand { get; }
        public ICommand RedLightOffCommand { get; }
        public ICommand JumpToZeroCommand { get; }
        public ICommand RefreshStatusCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand ReloadConfigCommand { get; }
        public ICommand ResetToDefaultCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand ApplyConfigCommand { get; }
        public ICommand FillAreaCommand { get; }
        public ICommand PresetRectangleCommand { get; }
        public ICommand PresetTriangleCommand { get; }
        #endregion

        #region 构造函数

        public HM_LaserDebugViewModel(HM_LaserService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));

            _ipAddress = _service.Config.IpAddress;
            Logs = new ObservableCollection<HM_LogEntry>();

            // 订阅服务事件
            _service.ConnectionChanged += OnConnectionChanged;
            _service.ProgressChanged += OnProgressChanged;
            _service.MarkFinished += OnMarkFinished;
            _service.StatusChanged += OnStatusChanged;
            _service.ErrorOccurred += OnErrorOccurred;

            // 初始化命令
            ConnectCommand = new RelayCommand(
                async () => await ConnectAsync(),
                () => !IsConnected && !string.IsNullOrWhiteSpace(IpAddress)
            );

            DisconnectCommand = new RelayCommand(
                () => Disconnect(),
                () => IsConnected
            );

            TestMarkCommand = new RelayCommand(
                async () => await TestMarkAsync(),
                () => IsConnected && !IsMarking
            );

            StopMarkCommand = new RelayCommand(
                () => StopMark(),
                () => IsConnected && IsMarking
            );

            RedLightOnCommand = new RelayCommand(
                () => SetRedLight(true),
                () => IsConnected && !IsMarking
            );

            RedLightOffCommand = new RelayCommand(
                () => SetRedLight(false),
                () => IsConnected
            );

            JumpToZeroCommand = new RelayCommand(
                () => JumpToZero(),
                () => IsConnected && !IsMarking
            );

            RefreshStatusCommand = new RelayCommand(
                () => RefreshStatus(),
                () => IsConnected
            );

            SaveConfigCommand = new RelayCommand(
                () => SaveConfig(),
                () => true
            );

            ReloadConfigCommand = new RelayCommand(
                () => ReloadConfig(),
                () => true
            );

            ResetToDefaultCommand = new RelayCommand(
                () => ResetToDefault(),
                () => true
            );

            ClearLogsCommand = new RelayCommand(
                () => Logs.Clear(),
                () => true
            );

            ApplyConfigCommand = new RelayCommand(
                () => ApplyConfig(),
                () => HasUnsavedChanges
            );

            FillAreaCommand = new RelayCommand(
                async () => await FillAreaAsync(),
                () => IsConnected && !IsMarking
            );
            PresetRectangleCommand = new RelayCommand(() =>
            {
                // 10x6mm 矩形
                AreaCoordinates = "AddAreas[-5,-3,5,-3:5,-3,5,3:5,3,-5,3:-5,3,-5,-3]";
                AddLog("已加载预设: 矩形 10x6mm", LogLevel.Info);
            });

            PresetTriangleCommand = new RelayCommand(() =>
            {
                // 等边三角形 (底边10mm)
                AreaCoordinates = "AddAreas[-5,-2.887,5,-2.887:5,-2.887,0,5.774:0,5.774,-5,-2.887]";
                AddLog("已加载预设: 等边三角形", LogLevel.Info);
            });

            AddLog("ViewModel初始化完成", LogLevel.Info);
        }

        #endregion

        #region 命令实现

        private async System.Threading.Tasks.Task ConnectAsync()
        {
            try
            {
                // 先应用IP地址更改
                _service.Config.IpAddress = IpAddress;

                AddLog($"正在连接 {IpAddress}...", LogLevel.Info);
                StatusMessage = "正在连接...";

                var result = await _service.ConnectAsync();

                if (result)
                {
                    AddLog("连接成功", LogLevel.Success);
                    StatusMessage = "已连接";
                    RefreshStatus();
                }
                else
                {
                    AddLog("连接失败", LogLevel.Error);
                    StatusMessage = "连接失败";
                }
            }
            catch (Exception ex)
            {
                AddLog($"连接异常: {ex.Message}", LogLevel.Error);
                StatusMessage = "连接异常";
            }

            RaiseCommandsCanExecuteChanged();
        }

        private void Disconnect()
        {
            try
            {
                _service.Disconnect();
                AddLog("已断开连接", LogLevel.Info);
                StatusMessage = "已断开";
            }
            catch (Exception ex)
            {
                AddLog($"断开异常: {ex.Message}", LogLevel.Error);
            }

            RaiseCommandsCanExecuteChanged();
        }

        private async System.Threading.Tasks.Task TestMarkAsync()
        {
            if (string.IsNullOrWhiteSpace(TestCoordinates))
            {
                AddLog("请输入测试坐标", LogLevel.Warning);
                return;
            }

            try
            {
                IsMarking = true;
                MarkProgress = 0;
                AddLog($"开始测试打标: {(TestCoordinates.Length > 50 ? TestCoordinates.Substring(0, 50) + "..." : TestCoordinates)}", LogLevel.Info);
                StatusMessage = "打标中...";

                var command = $"AddLines[{TestCoordinates}]";
                var result = await _service.AddLinesAndMarkAsync(command);

                if (result)
                {
                    AddLog("打标完成", LogLevel.Success);
                    StatusMessage = "打标完成";
                }
                else
                {
                    AddLog("打标失败", LogLevel.Error);
                    StatusMessage = "打标失败";
                }
            }
            catch (Exception ex)
            {
                AddLog($"打标异常: {ex.Message}", LogLevel.Error);
                StatusMessage = "打标异常";
            }
            finally
            {
                IsMarking = false;
                RaiseCommandsCanExecuteChanged();
            }
        }


        /// <summary>
        /// 区域填充打标
        /// </summary>
        private async System.Threading.Tasks.Task FillAreaAsync()
        {
            if (string.IsNullOrWhiteSpace(AreaCoordinates))
            {
                AddLog("请输入区域坐标", LogLevel.Warning);
                return;
            }

            try
            {
                IsMarking = true;
                MarkProgress = 0;

                // 确保格式正确
                var coords = AreaCoordinates.Trim();
                if (!coords.StartsWith("AddAreas["))
                {
                    coords = "AddAreas[" + coords.TrimEnd(']') + "]";
                }

                AddLog($"开始区域填充: 线间距={FillLineSpacing}mm", LogLevel.Info);
                AddLog($"区域数据: {(coords.Length > 60 ? coords.Substring(0, 60) + "..." : coords)}", LogLevel.Info);
                StatusMessage = "区域填充中...";

                var result = await _service.FillAreaAndMarkAsync(coords);

                if (result)
                {
                    AddLog("区域填充完成", LogLevel.Success);
                    StatusMessage = "区域填充完成";
                }
                else
                {
                    AddLog("区域填充失败", LogLevel.Error);
                    StatusMessage = "区域填充失败";
                }
            }
            catch (Exception ex)
            {
                AddLog($"区域填充异常: {ex.Message}", LogLevel.Error);
                StatusMessage = "区域填充异常";
            }
            finally
            {
                IsMarking = false;
                RaiseCommandsCanExecuteChanged();
            }
        }

        private void StopMark()
        {
            try
            {
                _service.StopMark();
                IsMarking = false;
                AddLog("打标已停止", LogLevel.Warning);
                StatusMessage = "已停止";
            }
            catch (Exception ex)
            {
                AddLog($"停止异常: {ex.Message}", LogLevel.Error);
            }

            RaiseCommandsCanExecuteChanged();
        }

        private void SetRedLight(bool enable)
        {
            try
            {
                var result = _service.SetRedLight(enable);
                if (result)
                {
                    AddLog(enable ? "红光已开启" : "红光已关闭", LogLevel.Info);
                }
                else
                {
                    AddLog("红光控制失败", LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                AddLog($"红光控制异常: {ex.Message}", LogLevel.Error);
            }
        }

        private void JumpToZero()
        {
            try
            {
                var result = _service.JumpTo(0, 0, 0);
                AddLog(result ? "振镜已回零" : "振镜回零失败", result ? LogLevel.Info : LogLevel.Error);
            }
            catch (Exception ex)
            {
                AddLog($"振镜回零异常: {ex.Message}", LogLevel.Error);
            }
        }

        private void RefreshStatus()
        {
            try
            {
                var status = _service.GetWorkStatus();
                WorkStatus = GetWorkStatusText(status);
                AddLog($"工作状态: {WorkStatus}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                AddLog($"刷新状态异常: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 保存配置到文件并通知其他组件
        /// </summary>
        private void SaveConfig()
        {
            try
            {
                // 1. 验证配置
                if (!_service.Config.Validate(out string error))
                {
                    AddLog($"配置验证失败: {error}", LogLevel.Error);
                    StatusMessage = $"验证失败: {error}";
                    return;
                }

                // 2. 更新IP地址
                _service.Config.IpAddress = IpAddress;

                // 3. 保存到文件
                _service.Config.Save();

                // 4. 标记已保存
                HasUnsavedChanges = false;

                // 5. 触发配置已应用事件
                ConfigApplied?.Invoke(this, EventArgs.Empty);

                AddLog("配置已保存并应用", LogLevel.Success);
                StatusMessage = "配置已保存";
                OnPropertyChanged(nameof(ConfigSummary));
            }
            catch (Exception ex)
            {
                AddLog($"保存配置异常: {ex.Message}", LogLevel.Error);
                StatusMessage = "保存失败";
            }
        }

        /// <summary>
        /// 从文件重新加载配置
        /// </summary>
        private void ReloadConfig()
        {
            try
            {
                var newConfig = HM_LaserConfig.Load();

                // 复制所有属性到当前配置
                CopyConfigValues(newConfig, _service.Config);

                // 刷新UI
                RefreshAllProperties();
                HasUnsavedChanges = false;

                AddLog("配置已从文件重新加载", LogLevel.Success);
                StatusMessage = "配置已重新加载";
            }
            catch (Exception ex)
            {
                AddLog($"重新加载配置异常: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 重置为默认配置
        /// </summary>
        private void ResetToDefault()
        {
            try
            {
                var defaultConfig = HM_LaserConfig.CreateDefault();
                CopyConfigValues(defaultConfig, _service.Config);

                RefreshAllProperties();
                HasUnsavedChanges = true;

                AddLog("已重置为默认配置（未保存）", LogLevel.Warning);
                StatusMessage = "已重置为默认值";
            }
            catch (Exception ex)
            {
                AddLog($"重置配置异常: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 应用当前配置（不保存到文件）
        /// </summary>
        private void ApplyConfig()
        {
            try
            {
                if (!_service.Config.Validate(out string error))
                {
                    AddLog($"配置验证失败: {error}", LogLevel.Error);
                    return;
                }

                _service.Config.IpAddress = IpAddress;
                ConfigApplied?.Invoke(this, EventArgs.Empty);

                AddLog("配置已应用（未保存到文件）", LogLevel.Info);
                StatusMessage = "配置已应用";
            }
            catch (Exception ex)
            {
                AddLog($"应用配置异常: {ex.Message}", LogLevel.Error);
            }
        }

        #endregion

        #region 辅助方法

        private void CopyConfigValues(HM_LaserConfig source, HM_LaserConfig target)
        {
            // 连接配置
            target.IpAddress = source.IpAddress;
            target.ConnectionTimeoutMs = source.ConnectionTimeoutMs;
            target.DownloadTimeoutMs = source.DownloadTimeoutMs;
            target.MarkTimeoutMs = source.MarkTimeoutMs;

            // 振镜参数
            target.MarkSpeed = source.MarkSpeed;
            target.JumpSpeed = source.JumpSpeed;
            target.MarkDelay = source.MarkDelay;
            target.JumpDelay = source.JumpDelay;
            target.PolygonDelay = source.PolygonDelay;
            target.MarkCount = source.MarkCount;

            // 激光参数
            target.LaserOnDelay = source.LaserOnDelay;
            target.LaserOffDelay = source.LaserOffDelay;
            target.FPKDelay = source.FPKDelay;
            target.FPKLength = source.FPKLength;
            target.Frequency = source.Frequency;
            target.DutyCycle = source.DutyCycle;
            target.LaserPower = source.LaserPower;
            target.StandbyFrequency = source.StandbyFrequency;
            target.StandbyDutyCycle = source.StandbyDutyCycle;

            // 工作区域
            target.WorkAreaMinX = source.WorkAreaMinX;
            target.WorkAreaMaxX = source.WorkAreaMaxX;
            target.WorkAreaMinY = source.WorkAreaMinY;
            target.WorkAreaMaxY = source.WorkAreaMaxY;
            target.EnableCoordinateCheck = source.EnableCoordinateCheck;

            // 高级配置
            target.Protocol = source.Protocol;
            target.Dimensional = source.Dimensional;
            target.JumpToZeroAfterMark = source.JumpToZeroAfterMark;
            target.UseAnalogPower = source.UseAnalogPower;
            target.Waveform = source.Waveform;
            target.EnableMOPA = source.EnableMOPA;
            target.MOPAPulseWidth = source.MOPAPulseWidth;
            target.FillLineSpacing = source.FillLineSpacing;

        }

        private void RefreshAllProperties()
        {
            _ipAddress = _service.Config.IpAddress;

            // 通知所有属性变更
            OnPropertyChanged(nameof(IpAddress));
            OnPropertyChanged(nameof(MarkSpeed));
            OnPropertyChanged(nameof(JumpSpeed));
            OnPropertyChanged(nameof(MarkDelay));
            OnPropertyChanged(nameof(JumpDelay));
            OnPropertyChanged(nameof(PolygonDelay));
            OnPropertyChanged(nameof(MarkCount));
            OnPropertyChanged(nameof(LaserPower));
            OnPropertyChanged(nameof(Frequency));
            OnPropertyChanged(nameof(DutyCycle));
            OnPropertyChanged(nameof(DutyCyclePercent));
            OnPropertyChanged(nameof(LaserOnDelay));
            OnPropertyChanged(nameof(LaserOffDelay));
            OnPropertyChanged(nameof(FPKDelay));
            OnPropertyChanged(nameof(FPKLength));
            OnPropertyChanged(nameof(StandbyFrequency));
            OnPropertyChanged(nameof(StandbyDutyCycle));
            OnPropertyChanged(nameof(WorkAreaMinX));
            OnPropertyChanged(nameof(WorkAreaMaxX));
            OnPropertyChanged(nameof(WorkAreaMinY));
            OnPropertyChanged(nameof(WorkAreaMaxY));
            OnPropertyChanged(nameof(EnableCoordinateCheck));
            OnPropertyChanged(nameof(Protocol));
            OnPropertyChanged(nameof(ProtocolName));
            OnPropertyChanged(nameof(Dimensional));
            OnPropertyChanged(nameof(DimensionalName));
            OnPropertyChanged(nameof(JumpToZeroAfterMark));
            OnPropertyChanged(nameof(UseAnalogPower));
            OnPropertyChanged(nameof(Waveform));
            OnPropertyChanged(nameof(EnableMOPA));
            OnPropertyChanged(nameof(MOPAPulseWidth));
            OnPropertyChanged(nameof(ConnectionTimeoutMs));
            OnPropertyChanged(nameof(DownloadTimeoutMs));
            OnPropertyChanged(nameof(MarkTimeoutMs));
            OnPropertyChanged(nameof(ConfigSummary));
            OnPropertyChanged(nameof(FillLineSpacing));

        }

        private void UpdateConnectionStatus()
        {
            if (IsConnected)
            {
                ConnectionStatus = "已连接";
                ConnectionIndicator = Brushes.Green;
            }
            else
            {
                ConnectionStatus = "未连接";
                ConnectionIndicator = Brushes.Gray;
            }
        }

        private string GetWorkStatusText(HM_WorkStatus status)
        {
            switch (status)
            {
                case HM_WorkStatus.Ready: return "就绪";
                case HM_WorkStatus.Running: return "运行中";
                case HM_WorkStatus.Alarm: return "报警";
                default: return "未知";
            }
        }

        private void AddLog(string message, LogLevel level)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    Logs.Insert(0, new HM_LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Message = message,
                        Level = level
                    });

                    while (Logs.Count > 200)
                    {
                        Logs.RemoveAt(Logs.Count - 1);
                    }
                }
                catch { }
            }));
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (TestMarkCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StopMarkCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RedLightOnCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RedLightOffCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (JumpToZeroCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RefreshStatusCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ApplyConfigCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FillAreaCommand as RelayCommand)?.RaiseCanExecuteChanged();

        }

        #endregion

        #region 事件处理

        private void OnConnectionChanged(object sender, bool connected)
        {
            IsConnected = connected;
            AddLog(connected ? "设备已连接" : "设备已断开", connected ? LogLevel.Success : LogLevel.Warning);
        }

        private void OnProgressChanged(object sender, int progress)
        {
            MarkProgress = progress;
        }

        private void OnMarkFinished(object sender, HM_MarkFinishedEventArgs e)
        {
            IsMarking = false;
            MarkProgress = e.Success ? 100 : 0;
            AddLog($"打标完成，耗时: {e.ElapsedMs}ms", e.Success ? LogLevel.Success : LogLevel.Error);
        }

        private void OnStatusChanged(object sender, string status)
        {
            StatusMessage = status;
        }

        private void OnErrorOccurred(object sender, string error)
        {
            AddLog($"错误: {error}", LogLevel.Error);
            StatusMessage = error;
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

    #region 辅助类

    public class HM_LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public LogLevel Level { get; set; }

        public string TimeString => Timestamp.ToString("HH:mm:ss.fff");

        public Brush LevelBrush
        {
            get
            {
                switch (Level)
                {
                    case LogLevel.Success: return Brushes.Green;
                    case LogLevel.Warning: return Brushes.Orange;
                    case LogLevel.Error: return Brushes.Red;
                    default: return Brushes.Black;
                }
            }
        }
    }

    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    

    #endregion
}
