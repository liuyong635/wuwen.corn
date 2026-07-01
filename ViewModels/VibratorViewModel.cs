using SeedCut.Services;
using SeedCut.Services.Connection;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// 振动盘 ViewModel - 使用 ConnectionMonitorService 管理连接
    /// 参考 MainPLCViewModel 的架构模式
    /// </summary>
    public class VibratorViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IVibratorService _vibratorService;
        private readonly ConnectionMonitorService _connectionMonitor;
        private const string _deviceId = "Vibrator_Main";  // 与适配器中的DeviceId一致

        public event PropertyChangedEventHandler PropertyChanged;

        #region 属性

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

        private bool _isReconnecting;
        public bool IsReconnecting
        {
            get => _isReconnecting;
            set => SetProperty(ref _isReconnecting, value);
        }

        private int _retryCount;
        public int RetryCount
        {
            get => _retryCount;
            set => SetProperty(ref _retryCount, value);
        }

        private string _connectionStatus = "设备未连接";
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
        }

        private Brush _connectionIndicatorBrush = Brushes.Orange;
        public Brush ConnectionIndicatorBrush
        {
            get => _connectionIndicatorBrush;
            set => SetProperty(ref _connectionIndicatorBrush, value);
        }

        private bool _isLightAOn;
        public bool IsLightAOn
        {
            get => _isLightAOn;
            set => SetProperty(ref _isLightAOn, value);
        }

        private bool _isLightBOn;
        public bool IsLightBOn
        {
            get => _isLightBOn;
            set => SetProperty(ref _isLightBOn, value);
        }

        private bool _isVibrating;
        public bool IsVibrating
        {
            get => _isVibrating;
            set => SetProperty(ref _isVibrating, value);
        }

        private bool _isFeeding;
        public bool IsFeeding
        {
            get => _isFeeding;
            set => SetProperty(ref _isFeeding, value);
        }

        private bool _isPourDoorOpen;
        public bool IsPourDoorOpen
        {
            get => _isPourDoorOpen;
            set => SetProperty(ref _isPourDoorOpen, value);
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        private string _statusMessage;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // 状态文本属性
        public string VibratorStateText => IsVibrating ? "运行中" : "停止";
        public string BoxStateText => IsFeeding ? "运行中" : "停止";
        public string LightStateText => (IsLightAOn || IsLightBOn) ? "开启" : "关闭";
        public Brush LightStateBrush => (IsLightAOn || IsLightBOn) ? Brushes.Green : Brushes.Orange;

        #endregion

        #region 命令

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand ToggleLightACommand { get; }
        public ICommand ToggleLightBCommand { get; }
        public ICommand FeedCommand { get; }
        public ICommand PourCommand { get; }
        public ICommand VibrateCommand { get; }
        public ICommand StopVibratorCommand { get; }
        public ICommand StopBoxCommand { get; }
        public ICommand VibrationGroup2Command { get; }

        #endregion

        /// <summary>
        /// 构造函数 - 注入 IVibratorService 和 ConnectionMonitorService
        /// </summary>
        public VibratorViewModel(IVibratorService vibratorService, ConnectionMonitorService connectionMonitor)
        {
            _vibratorService = vibratorService ?? throw new ArgumentNullException(nameof(vibratorService));
            _connectionMonitor = connectionMonitor ?? throw new ArgumentNullException(nameof(connectionMonitor));

            // ✅ 订阅 ConnectionMonitorService 的事件（类似 MainPLCViewModel）
            _connectionMonitor.DeviceStatusChanged += OnDeviceStatusChanged;
            _connectionMonitor.DeviceReconnecting += OnDeviceReconnecting;

            // 仍然订阅服务的状态变化事件（用于更新设备状态，如光源、振动等）
            _vibratorService.StatusChanged += OnStatusChanged;
            _vibratorService.ErrorOccurred += OnErrorOccurred;

            // 初始化命令 - 连接/断开通过 ConnectionMonitorService
            ConnectCommand = new RelayCommand(ExecuteConnect, CanExecuteConnect);
            DisconnectCommand = new RelayCommand(ExecuteDisconnect, CanExecuteDisconnect);

            // 控制命令仍然直接调用 IVibratorService
            ToggleLightACommand = new RelayCommand(async () => await _vibratorService.ToggleLightAAsync(),
                () => IsConnected && !IsBusy);
            ToggleLightBCommand = new RelayCommand(async () => await _vibratorService.ToggleLightBAsync(),
                () => IsConnected && !IsBusy);
            FeedCommand = new RelayCommand(async () => await _vibratorService.StartFeedAsync(),
                () => IsConnected && !IsBusy);
            PourCommand = new RelayCommand(async () => await _vibratorService.TogglePourDoorAsync(),
                () => IsConnected && !IsBusy);
            VibrateCommand = new RelayCommand(async () => await _vibratorService.StartVibrationAsync(),
                () => IsConnected && !IsBusy);
            StopVibratorCommand = new RelayCommand(async () => await _vibratorService.StopVibrationAsync(),
                () => IsConnected && IsVibrating && !IsBusy);
            StopBoxCommand = new RelayCommand(async () => await _vibratorService.StopFeedAsync(),
                () => IsConnected && IsFeeding && !IsBusy);
            VibrationGroup2Command = new RelayCommand(async () => await _vibratorService.StartVibrationGroup2Async(),
                () => IsConnected && !IsBusy);

            // 初始化连接状态
            UpdateConnectionStatus();

            System.Diagnostics.Debug.WriteLine($"✅ VibratorViewModel 构造完成，设备ID: {_deviceId}");
        }

        #region 命令实现

        /// <summary>
        /// 通过 ConnectionMonitorService 连接设备
        /// </summary>
        private async void ExecuteConnect()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "正在连接振动盘...";

            try
            {
                // ✅ 通过 ConnectionMonitorService 连接
                bool result = await _connectionMonitor.ConnectDeviceAsync(_deviceId);

                if (result)
                {
                    StatusMessage = "振动盘连接成功";
                }
                else
                {
                    StatusMessage = "振动盘连接失败";
                }

                UpdateConnectionStatus();
            }
            catch (Exception ex)
            {
                StatusMessage = $"连接异常: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"振动盘连接异常: {ex}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanExecuteConnect()
        {
            return !IsBusy && !IsConnected;
        }

        /// <summary>
        /// 通过 ConnectionMonitorService 断开设备
        /// </summary>
        private void ExecuteDisconnect()
        {
            if (IsBusy) return;

            try
            {
                // ✅ 通过 ConnectionMonitorService 断开
                _connectionMonitor.DisconnectDevice(_deviceId);

                StatusMessage = "振动盘已断开";
                UpdateConnectionStatus();
            }
            catch (Exception ex)
            {
                StatusMessage = $"断开异常: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"振动盘断开异常: {ex}");
            }
        }

        private bool CanExecuteDisconnect()
        {
            return !IsBusy && IsConnected;
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// ✅ 处理 ConnectionMonitorService 的设备状态变化事件
        /// ✅ 修复：使用 BeginInvoke 避免死锁
        /// </summary>
        private void OnDeviceStatusChanged(object sender, DeviceConnectionStatusChangedEventArgs e)
        {
            // 只处理振动盘设备的事件
            if (e.DeviceId != _deviceId) return;

            // ✅ 修复：使用 BeginInvoke 替代 Invoke，避免跨线程死锁
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    IsConnected = e.IsConnected;
                    IsReconnecting = e.Status.IsReconnecting;
                    RetryCount = e.Status.RetryCount;

                    UpdateConnectionStatus();

                    if (e.IsConnected)
                    {
                        StatusMessage = $"振动盘已连接 ({e.Status.GetStatusText()})";
                    }
                    else if (e.Status.IsReconnecting)
                    {
                        StatusMessage = $"振动盘重连中... (第{e.Status.RetryCount}次)";
                    }
                    else
                    {
                        StatusMessage = $"振动盘断开 ({e.Status.GetStatusText()})";
                    }

                    System.Diagnostics.Debug.WriteLine($"[振动盘] 状态变化: {e.Status.GetStatusText()}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[振动盘] 更新状态异常: {ex.Message}");
                }
            }));
        }

        /// <summary>
        /// ✅ 处理重连尝试事件
        /// ✅ 修复：使用 BeginInvoke 避免死锁
        /// </summary>
        private void OnDeviceReconnecting(object sender, ReconnectionAttemptEventArgs e)
        {
            if (e.DeviceName != "振动盘控制器") return;

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (e.Success)
                    {
                        StatusMessage = "振动盘重连成功";
                        IsReconnecting = false;
                    }
                    else if (e.IsMaxRetryReached)
                    {
                        StatusMessage = "振动盘重连失败，已达最大重试次数";
                        IsReconnecting = false;
                    }
                    else
                    {
                        StatusMessage = $"振动盘重连中... (第{e.RetryCount}次)";
                        RetryCount = e.RetryCount;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[振动盘] 更新重连状态异常: {ex.Message}");
                }
            }));
        }

        /// <summary>
        /// 处理服务状态变化（设备状态更新，如光源、振动等）
        /// ✅ 修复：使用 BeginInvoke 避免死锁
        /// </summary>
        private void OnStatusChanged(object sender, string message)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    StatusMessage = message;
                    UpdateDeviceStatus();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[振动盘] 更新服务状态异常: {ex.Message}");
                }
            }));
        }

        /// <summary>
        /// 处理错误事件
        /// ✅ 修复：使用 BeginInvoke 避免死锁
        /// </summary>
        private void OnErrorOccurred(object sender, string message)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    StatusMessage = $"错误: {message}";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[振动盘] 更新错误状态异常: {ex.Message}");
                }
            }));
        }

        /// <summary>
        /// 更新连接状态显示（包括指示灯颜色）
        /// </summary>
        private void UpdateConnectionStatus()
        {
            // ✅ 从 ConnectionMonitorService 获取实时状态
            var deviceStatus = _connectionMonitor.GetDeviceStatus(_deviceId);

            if (deviceStatus != null)
            {
                IsConnected = deviceStatus.IsConnected;
                IsReconnecting = deviceStatus.IsReconnecting;
                RetryCount = deviceStatus.RetryCount;

                if (deviceStatus.IsConnected)
                {
                    ConnectionStatus = "✓ 设备已连接";
                    ConnectionIndicatorBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // 绿色
                }
                else if (deviceStatus.IsReconnecting)
                {
                    ConnectionStatus = $"⟳ 重连中 (第{deviceStatus.RetryCount}次)";
                    ConnectionIndicatorBrush = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // 橙色
                }
                else
                {
                    ConnectionStatus = "✗ 设备未连接";
                    ConnectionIndicatorBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // 红色
                }
            }
            else
            {
                // 设备未注册到监控服务，使用默认状态
                IsConnected = false;
                ConnectionStatus = "✗ 设备未连接";
                ConnectionIndicatorBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // 红色
            }
        }

        /// <summary>
        /// 更新设备状态（光源、振动等）
        /// </summary>
        private void UpdateDeviceStatus()
        {
            IsLightAOn = _vibratorService.IsLightAOn;
            IsLightBOn = _vibratorService.IsLightBOn;
            IsVibrating = _vibratorService.IsVibrating;
            IsFeeding = _vibratorService.IsFeeding;
            IsPourDoorOpen = _vibratorService.IsPourDoorOpen;

            OnPropertyChanged(nameof(VibratorStateText));
            OnPropertyChanged(nameof(BoxStateText));
            OnPropertyChanged(nameof(LightStateText));
            OnPropertyChanged(nameof(LightStateBrush));

            RaiseCommandsCanExecuteChanged();
        }

        /// <summary>
        /// 触发所有命令的 CanExecute 重新评估
        /// </summary>
        private void RaiseCommandsCanExecuteChanged()
        {
            (ConnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DisconnectCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ToggleLightACommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ToggleLightBCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FeedCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PourCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (VibrateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StopVibratorCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StopBoxCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (VibrationGroup2Command as RelayCommand)?.RaiseCanExecuteChanged();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            // 取消订阅事件
            if (_connectionMonitor != null)
            {
                _connectionMonitor.DeviceStatusChanged -= OnDeviceStatusChanged;
                _connectionMonitor.DeviceReconnecting -= OnDeviceReconnecting;
            }

            if (_vibratorService != null)
            {
                _vibratorService.StatusChanged -= OnStatusChanged;
                _vibratorService.ErrorOccurred -= OnErrorOccurred;
            }
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


}