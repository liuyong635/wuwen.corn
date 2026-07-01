using SeedCut.Services;
using SeedCut.Services.Connection;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// 机器人 ViewModel - 集成 ConnectionMonitorService 实现断线重连
    /// </summary>
    public class RobotViewModel : INotifyPropertyChanged, IDisposable
    {
        #region 私有字段

        private readonly IRobotService _robotService;
        private readonly ConnectionMonitorService _connectionMonitor;
        private readonly string _robotDeviceId = "Robot_Main";
        private DispatcherTimer _statusTimer;

        private bool _isConnected;
        private bool _isReconnecting;
        private int _retryCount;
        private bool _isServoEnabled;
        private string _ipAddress = "192.168.0.123";
        private string _port = "502";
        private string _connectionStatusText = "未连接";
        private string _statusMessage = "就绪";
        private bool _isBusy;

        private double _coordinateX;
        private double _coordinateY;
        private double _coordinateZ;
        private double _coordinateC;

        #endregion

        #region 属性

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConnectionButtonText));

                    // 更新命令的可用状态
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// 是否正在重连
        /// </summary>
        public bool IsReconnecting
        {
            get => _isReconnecting;
            set
            {
                if (_isReconnecting != value)
                {
                    _isReconnecting = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ConnectionButtonText));
                }
            }
        }

        /// <summary>
        /// 重连次数
        /// </summary>
        public int RetryCount
        {
            get => _retryCount;
            set
            {
                if (_retryCount != value)
                {
                    _retryCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsServoEnabled
        {
            get => _isServoEnabled;
            set
            {
                if (_isServoEnabled != value)
                {
                    _isServoEnabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ServoButtonText));
                    OnPropertyChanged(nameof(ServoStatusText));

                    // 更新JOG命令的可用状态
                    CommandManager.InvalidateRequerySuggested();

                    System.Diagnostics.Debug.WriteLine($"✅ 伺服状态更新: {(value ? "已使能" : "未使能")}");
                }
            }
        }

        /// <summary>
        /// 是否繁忙（连接/断开操作中）
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy != value)
                {
                    _isBusy = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string IPAddress
        {
            get => _ipAddress;
            set
            {
                if (_ipAddress != value)
                {
                    _ipAddress = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Port
        {
            get => _port;
            set
            {
                if (_port != value)
                {
                    _port = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 连接按钮文本（根据状态动态变化）
        /// </summary>
        public string ConnectionButtonText
        {
            get
            {
                if (IsReconnecting)
                    return "重连中...";
                return IsConnected ? "断开" : "连接";
            }
        }

        /// <summary>
        /// 连接状态文本（显示详细状态）
        /// </summary>
        public string ConnectionStatusText
        {
            get => _connectionStatusText;
            set
            {
                if (_connectionStatusText != value)
                {
                    _connectionStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 机器人地址信息（显示用）
        /// </summary>
        public string RobotAddress => $"{_robotService.Config?.IPAddress}:{_robotService.Config?.Port}";

        public string ServoButtonText => IsServoEnabled ? "关闭伺服" : "打开伺服";

        public string ServoStatusText => IsServoEnabled ? "已使能" : "未使能";

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                    System.Diagnostics.Debug.WriteLine($"📝 状态: {value}");
                }
            }
        }

        public double CoordinateX
        {
            get => _coordinateX;
            set
            {
                if (Math.Abs(_coordinateX - value) > 0.001)
                {
                    _coordinateX = value;
                    OnPropertyChanged();
                }
            }
        }

        public double CoordinateY
        {
            get => _coordinateY;
            set
            {
                if (Math.Abs(_coordinateY - value) > 0.001)
                {
                    _coordinateY = value;
                    OnPropertyChanged();
                }
            }
        }

        public double CoordinateZ
        {
            get => _coordinateZ;
            set
            {
                if (Math.Abs(_coordinateZ - value) > 0.001)
                {
                    _coordinateZ = value;
                    OnPropertyChanged();
                }
            }
        }

        public double CoordinateC
        {
            get => _coordinateC;
            set
            {
                if (Math.Abs(_coordinateC - value) > 0.001)
                {
                    _coordinateC = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region 命令

        public ICommand ConnectCommand { get; }
        public ICommand ReadCoordinatesCommand { get; }
        public ICommand ToggleServoCommand { get; }
        public ICommand RunARCommand { get; }
        public ICommand PauseARCommand { get; }
        public ICommand StopARCommand { get; }
        public ICommand ResetRobotCommand { get; }

        // JOG 命令
        public ICommand JogXPlusCommand { get; }
        public ICommand JogXMinusCommand { get; }
        public ICommand JogYPlusCommand { get; }
        public ICommand JogYMinusCommand { get; }
        public ICommand JogZPlusCommand { get; }
        public ICommand JogZMinusCommand { get; }
        public ICommand JogCPlusCommand { get; }
        public ICommand JogCMinusCommand { get; }
        public ICommand StopJogCommand { get; }

        #endregion

        #region 构造函数

        /// <summary>
        /// 构造函数 - 注入 IRobotService 和 ConnectionMonitorService
        /// </summary>
        /// <param name="robotService">机器人服务（业务操作）</param>
        /// <param name="connectionMonitor">连接监控服务（连接管理、心跳、重连）</param>
        public RobotViewModel(IRobotService robotService, ConnectionMonitorService connectionMonitor)
        {
            _robotService = robotService ?? throw new ArgumentNullException(nameof(robotService));
            _connectionMonitor = connectionMonitor ?? throw new ArgumentNullException(nameof(connectionMonitor));

            // ✅ 订阅连接监控服务的事件（主要状态源）
            _connectionMonitor.DeviceStatusChanged += OnDeviceStatusChanged;
            _connectionMonitor.DeviceReconnecting += OnDeviceReconnecting;

            // ✅ 仍然订阅坐标更新事件（业务数据）
            _robotService.CoordinatesUpdated += OnCoordinatesUpdated;

            // 初始化命令
            ConnectCommand = new RelayCommand(OnConnect, CanExecuteConnect);
            ReadCoordinatesCommand = new RelayCommand(OnReadCoordinates, CanExecuteRobotCommand);
            ToggleServoCommand = new RelayCommand(OnToggleServo, CanExecuteRobotCommand);
            RunARCommand = new RelayCommand(OnRunAR, CanExecuteRobotCommand);
            PauseARCommand = new RelayCommand(OnPauseAR, CanExecuteRobotCommand);
            StopARCommand = new RelayCommand(OnStopAR, CanExecuteRobotCommand);
            ResetRobotCommand = new RelayCommand(OnResetRobot, CanExecuteRobotCommand);

            // JOG 命令
            JogXPlusCommand = new RelayCommand(() => OnJog(1), CanExecuteJogCommand);
            JogXMinusCommand = new RelayCommand(() => OnJog(2), CanExecuteJogCommand);
            JogYPlusCommand = new RelayCommand(() => OnJog(3), CanExecuteJogCommand);
            JogYMinusCommand = new RelayCommand(() => OnJog(4), CanExecuteJogCommand);
            JogZPlusCommand = new RelayCommand(() => OnJog(5), CanExecuteJogCommand);
            JogZMinusCommand = new RelayCommand(() => OnJog(6), CanExecuteJogCommand);
            JogCPlusCommand = new RelayCommand(() => OnJog(7), CanExecuteJogCommand);
            JogCMinusCommand = new RelayCommand(() => OnJog(8), CanExecuteJogCommand);
            StopJogCommand = new RelayCommand(OnStopJog, CanExecuteJogCommand);

            // 初始化定时器
            InitializeTimer();

            // 初始化连接状态显示
            UpdateConnectionStatus();

            System.Diagnostics.Debug.WriteLine("✅ RobotViewModel 初始化完成（已集成 ConnectionMonitorService）");
        }

        #endregion

        #region 定时器

        private void InitializeTimer()
        {
            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // 每500ms更新一次
            };
            _statusTimer.Tick += OnStatusTimerTick;
        }

        private async void OnStatusTimerTick(object sender, EventArgs e)
        {
            if (!IsConnected) return;

            try
            {
                // 定期读取坐标
                await _robotService.ReadCoordinatesAsync();

                // 定期读取伺服状态
                var servoEnabled = await _robotService.GetServoEnableAsync();
                IsServoEnabled = servoEnabled;
            }
            catch (Exception ex)
            {
                // 静默失败,避免日志刷屏
                System.Diagnostics.Debug.WriteLine($"⚠️ 状态更新异常: {ex.Message}");
            }
        }

        #endregion

        #region 连接状态管理

        /// <summary>
        /// 更新连接状态显示
        /// </summary>
        private void UpdateConnectionStatus()
        {
            var deviceStatus = _connectionMonitor.GetDeviceStatus(_robotDeviceId);

            if (deviceStatus != null)
            {
                IsConnected = deviceStatus.IsConnected;
                IsReconnecting = deviceStatus.IsReconnecting;
                RetryCount = deviceStatus.RetryCount;

                if (deviceStatus.IsConnected)
                {
                    ConnectionStatusText = $"✓ 已连接 - {RobotAddress}";
                }
                else if (deviceStatus.IsReconnecting)
                {
                    ConnectionStatusText = $"⟳ 重连中 (第{deviceStatus.RetryCount}次) - {RobotAddress}";
                }
                else
                {
                    ConnectionStatusText = $"✗ 未连接 - {RobotAddress}";
                }
            }
            else
            {
                // 设备未注册到监控服务
                IsConnected = false;
                IsReconnecting = false;
                ConnectionStatusText = $"✗ 未连接 - {RobotAddress}";
            }
        }

        /// <summary>
        /// 设备状态变化事件处理
        /// </summary>
        private void OnDeviceStatusChanged(object sender, DeviceConnectionStatusChangedEventArgs e)
        {
            if (e.DeviceId != _robotDeviceId)
                return;

            // ✅ 修复：使用 BeginInvoke 替代 Invoke，避免跨线程死锁
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    UpdateConnectionStatus();

                    if (e.IsConnected)
                    {
                        StatusMessage = $"机器人已连接 ({e.Status.GetStatusText()})";

                        // 连接成功后启动定时器
                        _statusTimer?.Start();
                        System.Diagnostics.Debug.WriteLine("✅ 状态轮询定时器已启动");

                        // 立即读取初始状态
                        _ = ReadInitialStatusAsync();
                    }
                    else
                    {
                        StatusMessage = $"机器人断开 ({e.Status.GetStatusText()})";

                        // 断开连接后停止定时器
                        _statusTimer?.Stop();
                        IsServoEnabled = false;
                        System.Diagnostics.Debug.WriteLine("⏹️ 状态轮询定时器已停止");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[机器人] 更新状态异常: {ex.Message}");
                }
            }));
        }

        /// <summary>
        /// 设备重连尝试事件处理
        /// ✅ 修复：使用 BeginInvoke 替代 Invoke，避免跨线程死锁
        /// </summary>
        private void OnDeviceReconnecting(object sender, ReconnectionAttemptEventArgs e)
        {
            if (e.DeviceName != _robotService.Config?.DeviceName)
                return;

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    RetryCount = e.RetryCount;
                    IsReconnecting = !e.Success && !e.IsMaxRetryReached;

                    if (e.Success)
                    {
                        StatusMessage = $"机器人重连成功 (尝试了 {e.RetryCount} 次)";
                    }
                    else if (e.IsMaxRetryReached)
                    {
                        StatusMessage = $"机器人重连失败，已达到最大重试次数 ({e.RetryCount})";
                        IsReconnecting = false;
                    }
                    else
                    {
                        StatusMessage = $"正在尝试第 {e.RetryCount} 次重连...";
                    }

                    UpdateConnectionStatus();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[机器人] 更新重连状态异常: {ex.Message}");
                }
            }));
        }

        #endregion

        #region 事件处理

        private void OnCoordinatesUpdated(object sender, CoordinatesUpdatedEventArgs e)
        {
            CoordinateX = e.Coordinates.X;
            CoordinateY = e.Coordinates.Y;
            CoordinateZ = e.Coordinates.Z;
            CoordinateC = e.Coordinates.C;
        }

        private async System.Threading.Tasks.Task ReadInitialStatusAsync()
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(200); // 等待连接稳定
                await _robotService.GetServoEnableAsync();
                await _robotService.ReadCoordinatesAsync();
                System.Diagnostics.Debug.WriteLine("✅ 初始状态读取完成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 读取初始状态失败: {ex.Message}");
            }
        }

        #endregion

        #region 命令实现

        /// <summary>
        /// 连接/断开操作 - 通过 ConnectionMonitorService
        /// </summary>
        private async void OnConnect()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (IsConnected)
                {
                    // ✅ 通过 ConnectionMonitorService 断开
                    StatusMessage = "正在断开连接...";
                    _connectionMonitor.DisconnectDevice(_robotDeviceId);
                    StatusMessage = "机器人已断开";
                }
                else
                {
                    // ✅ 通过 ConnectionMonitorService 连接
                    StatusMessage = "正在连接机器人...";
                    bool result = await _connectionMonitor.ConnectDeviceAsync(_robotDeviceId);

                    if (result)
                    {
                        StatusMessage = "机器人连接成功";
                    }
                    else
                    {
                        StatusMessage = "机器人连接失败";
                    }
                }

                UpdateConnectionStatus();
            }
            catch (Exception ex)
            {
                StatusMessage = $"连接操作失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"❌ 连接异常: {ex}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanExecuteConnect()
        {
            // 繁忙或重连中时禁用按钮
            return !IsBusy && !IsReconnecting;
        }

        private async void OnReadCoordinates()
        {
            try
            {
                await _robotService.ReadCoordinatesAsync();
                StatusMessage = "坐标读取成功";
            }
            catch (Exception ex)
            {
                StatusMessage = $"读取坐标失败: {ex.Message}";
            }
        }

        private async void OnToggleServo()
        {
            try
            {
                bool targetState = !IsServoEnabled;
                System.Diagnostics.Debug.WriteLine($"🔧 切换伺服: {IsServoEnabled} -> {targetState}");

                var result = await _robotService.SetServoEnableAsync(targetState);
                StatusMessage = result.message;

                if (result.success)
                {
                    // ✅ 关键:写入后等待并验证
                    await System.Threading.Tasks.Task.Delay(100);
                    var actualState = await _robotService.GetServoEnableAsync();
                    IsServoEnabled = actualState;

                    System.Diagnostics.Debug.WriteLine($"✅ 伺服状态验证: {actualState}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 设置伺服失败: {result.message}");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"设置伺服失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"❌ 伺服异常: {ex}");
            }
        }

        private async void OnRunAR()
        {
            try
            {
                var result = await _robotService.RunARAsync();
                StatusMessage = result.message;
            }
            catch (Exception ex)
            {
                StatusMessage = $"运行失败: {ex.Message}";
            }
        }

        private async void OnPauseAR()
        {
            try
            {
                var result = await _robotService.PauseARAsync();
                StatusMessage = result.message;
            }
            catch (Exception ex)
            {
                StatusMessage = $"暂停失败: {ex.Message}";
            }
        }

        private async void OnStopAR()
        {
            try
            {
                var result = await _robotService.StopARAsync();
                StatusMessage = result.message;
            }
            catch (Exception ex)
            {
                StatusMessage = $"停止失败: {ex.Message}";
            }
        }

        private async void OnResetRobot()
        {
            try
            {
                var result = await _robotService.ResetRobotAsync();
                StatusMessage = result.message;
            }
            catch (Exception ex)
            {
                StatusMessage = $"复位失败: {ex.Message}";
            }
        }

        private async void OnJog(int axis)
        {
            try
            {
                var result = await _robotService.JogAsync(axis);
                // StatusMessage = result.message; // JOG时不更新状态消息,避免刷屏
            }
            catch (Exception ex)
            {
                StatusMessage = $"JOG 失败: {ex.Message}";
            }
        }

        private async void OnStopJog()
        {
            try
            {
                var result = await _robotService.StopJogAsync();
                StatusMessage = result.message;
            }
            catch (Exception ex)
            {
                StatusMessage = $"停止 JOG 失败: {ex.Message}";
            }
        }

        #endregion

        #region 命令可用性判断

        private bool CanExecuteRobotCommand()
        {
            return IsConnected && !IsReconnecting;
        }

        private bool CanExecuteJogCommand()
        {
            return IsConnected && IsServoEnabled && !IsReconnecting;
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _statusTimer?.Stop();

            // ✅ 取消订阅连接监控服务事件
            if (_connectionMonitor != null)
            {
                _connectionMonitor.DeviceStatusChanged -= OnDeviceStatusChanged;
                _connectionMonitor.DeviceReconnecting -= OnDeviceReconnecting;
            }

            // ✅ 取消订阅业务事件
            if (_robotService != null)
            {
                _robotService.CoordinatesUpdated -= OnCoordinatesUpdated;
            }
        }

        /// <summary>
        /// 兼容旧代码的清理方法
        /// </summary>
        public void Cleanup()
        {
            Dispose();
        }

        #endregion
    }
}