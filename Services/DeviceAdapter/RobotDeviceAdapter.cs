using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Services;
using SeedCut.Services.Connection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Services.DeviceAdapter
{
    /// <summary>
    /// 机器人设备适配器 - 实现 IRobotDevice 接口
    /// 职责：
    /// 1. 实现 IDevice/IRobotDevice 接口，供 DeviceManager 统一管理
    /// 2. 通过 RobotServiceConnectAdapter 实现连接管理
    /// 3. 通过 IRobotService 进行实际机器人操作
    /// 4. ✅ 新增：处理TCP命令，设置FlagCondition触发Handler
    /// </summary>
    public class RobotDeviceAdapter : IRobotDevice
    {
        #region 私有字段

        private readonly RobotServiceConnectAdapter _adapter;
        private readonly IRobotService _robotService;
        private readonly ConnectionManager _connectionManager;
        private readonly ILogService _logService;

        private DeviceConnectionState _connectionState = DeviceConnectionState.Disconnected;
        private RobotState _robotState = RobotState.Unknown;
        private string _lastError;
        private bool _disposed;

        // 配置
        private readonly RobotDeviceConfig _deviceConfig;

        #endregion

        #region IDevice 属性

        public string DeviceId => "Robot";
        public string DeviceName => _adapter.DeviceName;
        public Framework.Core.DeviceType DeviceType => Framework.Core.DeviceType.Robot;

        public DeviceConnectionState ConnectionState
        {
            get => _connectionState;
            private set
            {
                if (_connectionState != value)
                {
                    var oldState = _connectionState;
                    _connectionState = value;
                    RaiseConnectionChanged(oldState, value);
                }
            }
        }

        public bool IsConnected => _adapter.IsConnected;
        public string LastError => _lastError;

        #endregion

        #region IRobotDevice 属性

        public RobotState RobotState
        {
            get => _robotState;
            private set
            {
                if (_robotState != value)
                {
                    var oldState = _robotState;
                    _robotState = value;
                    RaiseRobotStateChanged(oldState, value);
                }
            }
        }

        public RobotPosition CurrentPosition
        {
            get
            {
                var coords = _robotService.CurrentCoordinates;
                if (coords == null) return new RobotPosition();
                return new RobotPosition(coords.X, coords.Y, coords.Z, 0, 0, coords.C);
            }
        }

        public string CurrentProgram => string.Empty;
        public bool IsServoOn => _robotService.IsServoEnabled;
        public bool IsProgramRunning => RobotState == RobotState.Running;
        public bool HasAlarm => RobotState == RobotState.Alarm || RobotState == RobotState.Error;

        /// <summary>
        /// ✅ 新增：AR程序客户端是否已连接
        /// </summary>
        public bool IsArClientConnected => _robotService.IsArClientConnected;

        /// <summary>
        /// ✅ 新增：TCP服务器是否正在运行
        /// </summary>
        public bool IsTcpServerRunning => _robotService.IsTcpServerRunning;

        #endregion

        #region 扩展属性

        public bool IsReconnecting => _connectionManager?.IsReconnecting ?? false;
        public int CurrentRetryCount => _connectionManager?.CurrentRetryCount ?? 0;
        public RobotDeviceConfig DeviceConfig => _deviceConfig;
        public IRobotService Service => _robotService;

        #endregion

        #region 事件

        public event EventHandler<DeviceConnectionChangedEventArgs> ConnectionChanged;
        public event EventHandler<DeviceErrorEventArgs> ErrorOccurred;
        public event EventHandler<RobotStateChangedEventArgs> RobotStateChanged;
        public event EventHandler<string> CommandReceived;
        public event EventHandler<ReconnectionAttemptEventArgs> ReconnectionAttempt;

        #endregion

        #region 构造函数

        public RobotDeviceAdapter(
            RobotServiceConnectAdapter adapter,
            IRobotService robotService,
            ILogService logService,
            RobotDeviceConfig config = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _robotService = robotService ?? throw new ArgumentNullException(nameof(robotService));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _deviceConfig = config ?? new RobotDeviceConfig();

            _connectionManager = new ConnectionManager(_adapter, _logService);
            SubscribeEvents();

            _logService.Information("[{DeviceName}] 机器人设备已创建", DeviceName);
        }

        public RobotDeviceAdapter(
            RobotServiceConnectAdapter adapter,
            IRobotService robotService)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _robotService = robotService ?? throw new ArgumentNullException(nameof(robotService));
            _logService = null;
            _connectionManager = null;
            _deviceConfig = new RobotDeviceConfig();

            SubscribeBasicEvents();
        }

        #endregion

        #region IDevice 方法

        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            try
            {
                ConnectionState = DeviceConnectionState.Connecting;
                RobotState = RobotState.Unknown;

                bool result;
                if (_connectionManager != null)
                {
                    result = await _connectionManager.ConnectAsync();
                }
                else
                {
                    result = await Task.Run(() => _adapter.Connect(), ct);
                }

                ConnectionState = result
                    ? DeviceConnectionState.Connected
                    : DeviceConnectionState.Error;

                RobotState = result ? RobotState.Idle : RobotState.Error;

                return result;
            }
            catch (OperationCanceledException)
            {
                ConnectionState = DeviceConnectionState.Disconnected;
                RobotState = RobotState.Unknown;
                return false;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                ConnectionState = DeviceConnectionState.Error;
                RobotState = RobotState.Error;
                RaiseError(ex.Message);
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                // ★ 新增：先停止 TCP 服务器
                _robotService?.StopTcpServer();

                if (_robotService.IsConnected)
                {
                    try
                    {
                        if (_robotService.IsServoEnabled)
                        {
                            await _robotService.SetServoEnableAsync(false).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logService?.Warning("[{DeviceName}] 断开前关闭伺服失败: {Error}", DeviceName, ex.Message);
                    }
                }

                if (_connectionManager != null)
                {
                    _connectionManager.Disconnect();
                }
                else
                {
                    _adapter.Disconnect();
                }

                ConnectionState = DeviceConnectionState.Disconnected;
                RobotState = RobotState.Unknown;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _logService?.Error(ex, "[{DeviceName}] 断开连接异常", DeviceName);
                ConnectionState = DeviceConnectionState.Disconnected;
                RobotState = RobotState.Unknown;
            }
        }

        public async Task<bool> ResetAsync()
        {
            if (!IsConnected)
            {
                RaiseError("机器人未连接，无法重置");
                return false;
            }

            try
            {
                var result = await _robotService.ResetRobotAsync();
                if (result.success)
                {
                    RobotState = RobotState.Idle;
                    _lastError = null;
                    _logService?.Information("[{DeviceName}] 设备已重置", DeviceName);
                }
                else
                {
                    _lastError = result.message;
                    RaiseError(result.message);
                }
                return result.success;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                RaiseError(ex.Message);
                return false;
            }
        }

        public Task<bool> CheckHealthAsync(CancellationToken ct = default)
        {
            var isHealthy = _adapter.CheckConnection();
            return Task.FromResult(isHealthy);
        }

        #endregion

        #region IRobotDevice 方法 - 伺服控制

        public async Task<bool> EnableServoAsync(bool enable, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("机器人未连接");
                return false;
            }

            try
            {
                var result = await _robotService.SetServoEnableAsync(enable);
                if (result.success)
                {
                    _logService?.Information("[{DeviceName}] 伺服{State}", DeviceName, enable ? "已使能" : "已禁用");
                }
                else
                {
                    RaiseError(result.message);
                }
                return result.success;
            }
            catch (Exception ex)
            {
                RaiseError($"设置伺服失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ClearAlarmAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("机器人未连接");
                return false;
            }

            try
            {
                var result = await _robotService.ResetRobotAsync();
                if (result.success)
                {
                    RobotState = RobotState.Idle;
                    _logService?.Information("[{DeviceName}] 报警已清除", DeviceName);
                }
                else
                {
                    RaiseError(result.message);
                }
                return result.success;
            }
            catch (Exception ex)
            {
                RaiseError($"清除报警失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region IRobotDevice 方法 - 程序控制

        public async Task<bool> RunProgramAsync(string programName, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("机器人未连接");
                return false;
            }

            try
            {
                var result = await _robotService.RunARAsync();
                if (result.success)
                {
                    RobotState = RobotState.Running;
                }
                else
                {
                    RaiseError(result.message);
                }
                return result.success;
            }
            catch (Exception ex)
            {
                RaiseError($"运行程序失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RunProgramAndWaitAsync(string programName, TimeSpan timeout, CancellationToken ct = default)
        {
            var started = await RunProgramAsync(programName, ct);
            if (!started) return false;

            var deadline = DateTime.Now + timeout;
            while (DateTime.Now < deadline && !ct.IsCancellationRequested)
            {
                if (RobotState != RobotState.Running)
                    return true;
                await Task.Delay(100, ct);
            }

            return RobotState != RobotState.Running;
        }

        public async Task<bool> PauseAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("机器人未连接");
                return false;
            }

            try
            {
                var result = await _robotService.PauseARAsync();
                if (result.success)
                {
                    RobotState = RobotState.Paused;
                }
                else
                {
                    RaiseError(result.message);
                }
                return result.success;
            }
            catch (Exception ex)
            {
                RaiseError($"暂停失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ResumeAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("机器人未连接");
                return false;
            }

            try
            {
                var result = await _robotService.RunARAsync();
                if (result.success)
                {
                    RobotState = RobotState.Running;
                }
                else
                {
                    RaiseError(result.message);
                }
                return result.success;
            }
            catch (Exception ex)
            {
                RaiseError($"恢复失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> StopAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("机器人未连接");
                return false;
            }

            try
            {
                var result = await _robotService.StopARAsync();
                if (result.success)
                {
                    RobotState = RobotState.Stopped;
                }
                else
                {
                    RaiseError(result.message);
                }
                return result.success;
            }
            catch (Exception ex)
            {
                RaiseError($"停止失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> HomeAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("机器人未连接");
                return false;
            }

            if (!IsServoOn)
            {
                RaiseError("请先使能伺服");
                return false;
            }

            try
            {
                var result = await _robotService.ResetRobotAsync();
                if (result.success)
                {
                    _logService?.Information("[{DeviceName}] 回原点完成", DeviceName);
                }
                else
                {
                    RaiseError(result.message);
                }
                return result.success;
            }
            catch (Exception ex)
            {
                RaiseError($"回原点失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region IRobotDevice 方法 - IO操作

        public bool ReadInput(int index)
        {
            _logService?.Warning("[{DeviceName}] ReadInput 暂不支持", DeviceName);
            return false;
        }

        public bool ReadOutput(int index)
        {
            _logService?.Warning("[{DeviceName}] ReadOutput 暂不支持", DeviceName);
            return false;
        }

        public void WriteOutput(int index, bool value)
        {
            _logService?.Warning("[{DeviceName}] WriteOutput 暂不支持", DeviceName);
        }

        #endregion

        #region IRobotDevice 方法 - 变量操作

        public int ReadIntVariable(string name)
        {
            _logService?.Warning("[{DeviceName}] ReadIntVariable 暂不支持", DeviceName);
            return 0;
        }

        public void WriteIntVariable(string name, int value)
        {
            _logService?.Warning("[{DeviceName}] WriteIntVariable 暂不支持", DeviceName);
        }

        #endregion

        #region IRobotDevice 方法 - TCP通信扩展

        /// <summary>
        /// ✅ 启动TCP服务器，用于AR程序连接
        /// </summary>
        public async Task<bool> StartTcpServerAsync(int port, CancellationToken ct = default)
        {
            try
            {
                var result = await _robotService.StartTcpServerAsync(port);
                if (result.success)
                {
                    _logService?.Information("[{DeviceName}] TCP服务器已启动，端口: {Port}", DeviceName, port);
                }
                else
                {
                    _logService?.Warning("[{DeviceName}] TCP服务器启动失败: {Message}", DeviceName, result.message);
                }
                return result.success;
            }
            catch (Exception ex)
            {
                _logService?.Error(ex, "[{DeviceName}] 启动TCP服务器异常", DeviceName);
                return false;
            }
        }

        /// <summary>
        /// ✅ 停止TCP服务器
        /// </summary>
        public void StopTcpServer()
        {
            try
            {
                _robotService.StopTcpServer();
                _logService?.Information("[{DeviceName}] TCP服务器已停止", DeviceName);
            }
            catch (Exception ex)
            {
                _logService?.Error(ex, "[{DeviceName}] 停止TCP服务器异常", DeviceName);
            }
        }

        /// <summary>
        /// ✅ 发送命令给机器人AR程序
        /// </summary>
        public async Task SendCommandAsync(string command, CancellationToken ct = default)
        {
            if (!IsArClientConnected)
            {
                _logService?.Warning("[{DeviceName}] AR程序未连接，无法发送命令: {Command}", DeviceName, command);
                return;
            }

            try
            {
                var result = await _robotService.SendCommandAsync(command);
                if (result)
                {
                    _logService?.Debug("[{DeviceName}] 命令已发送: {Command}", DeviceName, command);
                }
                else
                {
                    _logService?.Warning("[{DeviceName}] 命令发送失败: {Command}", DeviceName, command);
                }
            }
            catch (Exception ex)
            {
                _logService?.Error(ex, "[{DeviceName}] 发送命令异常: {Command}", DeviceName, command);
                throw;
            }
        }

        /// <summary>
        /// ✅ 发送坐标数据给机器人AR程序
        /// </summary>
        public async Task<bool> SendCoordinatesAsync(List<VisionCoordinate> coordinates, CancellationToken ct = default)
        {
            if (!IsArClientConnected)
            {
                _logService?.Warning("[{DeviceName}] AR程序未连接，无法发送坐标", DeviceName);
                return false;
            }

            if (coordinates == null || coordinates.Count == 0)
            {
                _logService?.Warning("[{DeviceName}] 坐标列表为空", DeviceName);
                return false;
            }

            try
            {
                var result = await _robotService.SendCoordinatesAsync(coordinates);
                if (result)
                {
                    _logService?.Information("[{DeviceName}] 已发送 {Count} 个坐标", DeviceName, coordinates.Count);
                }
                return result;
            }
            catch (Exception ex)
            {
                _logService?.Error(ex, "[{DeviceName}] 发送坐标异常", DeviceName);
                return false;
            }
        }

        /// <summary>
        /// ★ 新增：停止AR程序并清理所有相关资源
        /// 
        /// 用于测试取消和生产停止场景
        /// 不断开Modbus连接（由DeviceManager管理）
        /// </summary>
        public async Task<bool> StopAndCleanupAsync(CancellationToken ct = default)
        {
            var allSuccess = true;

            _logService?.Information("[{DeviceName}] ========== 开始停止和清理 ==========", DeviceName);

            // 步骤1: 停止AR程序
            if (IsProgramRunning)
            {
                _logService?.Information("[{DeviceName}] [步骤1] 停止AR程序...", DeviceName);
                try
                {
                    var stopResult = await StopAsync(ct);
                    if (stopResult)
                    {
                        _logService?.Information("[{DeviceName}] [步骤1] AR程序已停止 ✓", DeviceName);
                    }
                    else
                    {
                        _logService?.Warning("[{DeviceName}] [步骤1] AR程序停止失败", DeviceName);
                        allSuccess = false;
                    }
                }
                catch (Exception ex)
                {
                    _logService?.Warning("[{DeviceName}] [步骤1] 停止AR程序异常: {Error}", DeviceName, ex.Message);
                    allSuccess = false;
                }
            }
            else
            {
                _logService?.Information("[{DeviceName}] [步骤1] AR程序未运行，跳过", DeviceName);
            }

            // 步骤2: 断开伺服使能（安全考虑）
            if (IsServoOn)
            {
                _logService?.Information("[{DeviceName}] [步骤2] 断开伺服使能...", DeviceName);
                try
                {
                    var servoResult = await EnableServoAsync(false, ct);
                    if (servoResult)
                    {
                        _logService?.Information("[{DeviceName}] [步骤2] 伺服已断开 ✓", DeviceName);
                    }
                    else
                    {
                        _logService?.Warning("[{DeviceName}] [步骤2] 断开伺服失败", DeviceName);
                        allSuccess = false;
                    }
                }
                catch (Exception ex)
                {
                    _logService?.Warning("[{DeviceName}] [步骤2] 断开伺服异常: {Error}", DeviceName, ex.Message);
                    allSuccess = false;
                }
            }
            else
            {
                _logService?.Information("[{DeviceName}] [步骤2] 伺服未使能，跳过", DeviceName);
            }

            // 步骤3: 停止TCP服务器
            _logService?.Information("[{DeviceName}] [步骤3] 停止TCP服务器...", DeviceName);
            try
            {
                StopTcpServer();
                _logService?.Information("[{DeviceName}] [步骤3] TCP服务器已停止 ✓", DeviceName);
            }
            catch (Exception ex)
            {
                _logService?.Warning("[{DeviceName}] [步骤3] 停止TCP服务器异常: {Error}", DeviceName, ex.Message);
                // TCP服务器停止失败不影响整体结果
            }

            // 步骤4: 清理所有Flag条件
            _logService?.Information("[{DeviceName}] [步骤4] 清理Flag条件...", DeviceName);
            try
            {
                ClearAllRobotFlags();
                _logService?.Information("[{DeviceName}] [步骤4] Flag条件已清理 ✓", DeviceName);
            }
            catch (Exception ex)
            {
                _logService?.Warning("[{DeviceName}] [步骤4] 清理Flag异常: {Error}", DeviceName, ex.Message);
            }

            // 更新机器人状态
            RobotState = RobotState.Idle;

            _logService?.Information("[{DeviceName}] ========== 停止和清理完成 ({Result}) ==========",
                DeviceName, allSuccess ? "全部成功" : "部分失败");

            return allSuccess;
        }

        /// <summary>
        /// ★ 新增：清理所有机器人相关的Flag条件
        /// 
        /// 清理范围：
        /// - Robot_*_Received 系列标志
        /// - Vision_NoPoints_Received
        /// - Robot_Command_Pending
        /// - 各Handler的Busy标志
        /// </summary>
        public void ClearAllRobotFlags()
        {
            // 清理机器人命令接收标志
            FlagCondition.SetFlag("Robot_Ready_Received", false);
            FlagCondition.SetFlag("Robot_AllowProcess_Received", false);
            FlagCondition.SetFlag("Robot_CheckMaterial_Received", false);
            FlagCondition.SetFlag("Robot_Clamp_Received", false);
            FlagCondition.SetFlag("Robot_Release_Received", false);
            FlagCondition.SetFlag("Robot_BatchComplete_Received", false);
            FlagCondition.SetFlag("Robot_BackDropReady_Received", false);
            FlagCondition.SetFlag("Robot_WaitInit_Received", false);
            FlagCondition.SetFlag("Robot_BatchNext_Received", false);

            // 清理视觉相关标志
            FlagCondition.SetFlag("Vision_NoPoints_Received", false);

            // 清理命令待处理标志
            FlagCondition.SetFlag("Robot_Command_Pending", false);

            // 清理Handler忙碌标志
            FlagCondition.SetFlag("RobotCommand_Busy", false);
            FlagCondition.SetFlag("DiskVision_Busy", false);
            FlagCondition.SetFlag("SystemStartup_Busy", false);

            // 清理系统启动请求标志
            FlagCondition.SetFlag("System_Start_Request", false);

            _logService?.Debug("[{DeviceName}] 所有机器人相关Flag已清理", DeviceName);
        }


        #endregion

        #region 事件订阅

        private void SubscribeEvents()
        {
            SubscribeBasicEvents();

            if (_connectionManager != null)
            {
                _connectionManager.ConnectionStateChanged += OnConnectionManagerStateChanged;
                _connectionManager.ReconnectionAttempt += OnReconnectionAttempt;
            }
        }

        private void SubscribeBasicEvents()
        {
            _adapter.ConnectionStateChanged += OnAdapterConnectionStateChanged;
            _robotService.ConnectionStatusChanged += OnRobotServiceConnectionChanged;
            _robotService.CoordinatesUpdated += OnRobotCoordinatesUpdated;

            // ✅ 新增：订阅AR程序命令事件
            _robotService.CommandReceived += OnRobotCommandReceived;
            _robotService.ArClientConnectionChanged += OnArClientConnectionChanged;
        }

        private void UnsubscribeEvents()
        {
            _adapter.ConnectionStateChanged -= OnAdapterConnectionStateChanged;
            _robotService.ConnectionStatusChanged -= OnRobotServiceConnectionChanged;
            _robotService.CoordinatesUpdated -= OnRobotCoordinatesUpdated;

            // ✅ 新增：取消订阅AR程序命令事件
            _robotService.CommandReceived -= OnRobotCommandReceived;
            _robotService.ArClientConnectionChanged -= OnArClientConnectionChanged;

            if (_connectionManager != null)
            {
                _connectionManager.ConnectionStateChanged -= OnConnectionManagerStateChanged;
                _connectionManager.ReconnectionAttempt -= OnReconnectionAttempt;
            }
        }

        #endregion

        #region 事件处理

        private void OnAdapterConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            if (_connectionManager == null)
            {
                ConnectionState = e.IsConnected
                    ? DeviceConnectionState.Connected
                    : DeviceConnectionState.Disconnected;

                RobotState = e.IsConnected ? RobotState.Idle : RobotState.Unknown;
            }
        }

        private void OnConnectionManagerStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            if (e.IsConnected)
            {
                ConnectionState = DeviceConnectionState.Connected;
                RobotState = RobotState.Idle;
            }
            else if (_connectionManager.IsReconnecting)
            {
                ConnectionState = DeviceConnectionState.Reconnecting;
                RobotState = RobotState.Unknown;
            }
            else
            {
                ConnectionState = DeviceConnectionState.Disconnected;
                RobotState = RobotState.Unknown;
            }
        }

        private void OnReconnectionAttempt(object sender, ReconnectionAttemptEventArgs e)
        {
            if (!e.Success && !e.IsMaxRetryReached)
            {
                ConnectionState = DeviceConnectionState.Reconnecting;
                RobotState = RobotState.Unknown;
            }
            else if (e.IsMaxRetryReached)
            {
                ConnectionState = DeviceConnectionState.Error;
                RobotState = RobotState.Error;
                _lastError = "达到最大重连次数";
            }

            ReconnectionAttempt?.Invoke(this, e);
        }

        private void OnRobotServiceConnectionChanged(object sender, ConnectionStatusChangedEventArgs e)
        {
            if (_connectionManager == null)
            {
                ConnectionState = e.IsConnected
                    ? DeviceConnectionState.Connected
                    : DeviceConnectionState.Disconnected;
            }
        }

        private void OnRobotCoordinatesUpdated(object sender, CoordinatesUpdatedEventArgs e)
        {
            _logService?.Verbose("[{DeviceName}] 坐标更新: {Coords}", DeviceName, e.Coordinates?.ToString());
        }

        /// <summary>
        /// ★ 重构：AR程序命令接收处理
        /// 
        /// 旧架构：为每种命令设独立Flag
        /// 新架构：统一用 RobotCommand.Parse() 解析，推入共享队列，
        ///         设 Robot_Command_Pending 触发 RobotActionHandler
        /// </summary>
        private void OnRobotCommandReceived(object sender, RobotCommandEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Command))
                return;

            _logService?.Information("[{DeviceName}] 收到AR命令: {Command}",
                DeviceName, e.Command);

            try
            {
                // 使用框架层的 RobotCommand.Parse 解析（支持所有新协议命令）
                var cmd = SeedCut.Framework.Services.Handlers.RobotCommand.Parse(e.Command);

                if (cmd.Type == SeedCut.Framework.Services.Handlers.RobotCommandType.Unknown)
                {
                    _logService?.Warning("[{DeviceName}] 未识别的命令: {Command}", DeviceName, e.Command);
                    return;
                }

                // 推入共享队列
                SeedCut.Framework.Services.Handlers.RobotCommand.Enqueue(cmd);

                // 设置 Flag 触发 RobotActionHandler
                FlagCondition.SetFlag("Robot_Command_Pending", true);

                _logService?.Debug("[{DeviceName}] 命令已入队: {Type} (优先级={Priority})",
                    DeviceName, cmd.Type, cmd.Priority);
            }
            catch (Exception ex)
            {
                _logService?.Error(ex, "[{DeviceName}] 处理命令异常: {Command}", DeviceName, e.Command);
            }

            // 触发外部事件（保留兼容性）
            CommandReceived?.Invoke(this, e.Command);
        }

        /// <summary>
        /// 解析并记录飞拍偏差数据
        /// 格式: "FLY_OFFSET;X;Y;C;STATUS"
        /// </summary>
        private void LogFlyOffsetData(string command)
        {
            try
            {
                var parts = command.Split(';');
                if (parts.Length >= 5)
                {
                    var x = parts[1];
                    var y = parts[2];
                    var c = parts[3];
                    var status = parts[4];

                    // 简单判断是否超限（用于日志级别）
                    bool isOverLimit = false;
                    if (double.TryParse(x, out var dx) &&
                        double.TryParse(y, out var dy) &&
                        double.TryParse(c, out var dc))
                    {
                        isOverLimit = Math.Abs(dx) > 5.0 || Math.Abs(dy) > 5.0 || Math.Abs(dc) > 10.0;
                    }

                    if (isOverLimit)
                    {
                        _logService?.Warning("[{DeviceName}] 飞拍偏差[超限]: X={X}mm, Y={Y}mm, C={C}°, 状态={Status}",
                            DeviceName, x, y, c, status);
                    }
                    else
                    {
                        _logService?.Information("[{DeviceName}] 飞拍偏差: X={X}mm, Y={Y}mm, C={C}°, 状态={Status}",
                            DeviceName, x, y, c, status);
                    }
                }
            }
            catch (Exception ex)
            {
                _logService?.Warning("[{DeviceName}] 解析飞拍偏差失败: {Error}", DeviceName, ex.Message);
            }
        }

        /// <summary>
        /// ✅ 新增：AR程序客户端连接状态变化处理
        /// </summary>
        private void OnArClientConnectionChanged(object sender, bool isConnected)
        {
            _logService?.Information("[{DeviceName}] AR程序客户端{Status}",
                DeviceName, isConnected ? "已连接" : "已断开");
        }

        #endregion

        #region 事件触发

        private void RaiseConnectionChanged(DeviceConnectionState oldState, DeviceConnectionState newState)
        {
            ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs
            {
                DeviceId = DeviceId,
                DeviceName = DeviceName,
                OldState = oldState,
                NewState = newState
            });
        }

        private void RaiseRobotStateChanged(RobotState oldState, RobotState newState)
        {
            RobotStateChanged?.Invoke(this, new RobotStateChangedEventArgs
            {
                OldState = oldState,
                NewState = newState,
                Message = $"状态从 {oldState} 变为 {newState}"
            });
        }

        private void RaiseError(string message)
        {
            _lastError = message;
            ErrorOccurred?.Invoke(this, new DeviceErrorEventArgs
            {
                DeviceId = DeviceId,
                DeviceName = DeviceName,
                ErrorMessage = message
            });

            _logService?.Error("[{DeviceName}] {Error}", DeviceName, message);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            if (IsProgramRunning)
            {
                try
                {
                    StopAsync().Wait(TimeSpan.FromSeconds(3));
                }
                catch { }
            }

            if (IsServoOn)
            {
                try
                {
                    EnableServoAsync(false).Wait(TimeSpan.FromSeconds(3));
                }
                catch { }
            }

            // ★ 新增：显式停止 TCP 服务器
            _robotService?.StopTcpServer();

            UnsubscribeEvents();
            _connectionManager?.Dispose();

            _disposed = true;

            _logService?.Information("[{DeviceName}] 机器人设备已释放", DeviceName);
        }

        #endregion
    }

    #region 配置类

    public class RobotDeviceConfig
    {
        public bool EnableSafetyCheck { get; set; } = true;
        public int ConnectionTimeoutMs { get; set; } = 10000;
        public int OperationTimeoutMs { get; set; } = 5000;
        public float WorkAreaMinX { get; set; } = -500;
        public float WorkAreaMaxX { get; set; } = 500;
        public float WorkAreaMinY { get; set; } = -500;
        public float WorkAreaMaxY { get; set; } = 500;
        public float WorkAreaMinZ { get; set; } = 0;
        public float WorkAreaMaxZ { get; set; } = 300;
    }

    #endregion
}