using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Services.Connection;
using System;
using System.Threading;
using System.Threading.Tasks;

// 使用别名解决命名空间冲突
using FrameworkDeviceType = SeedCut.Framework.Core.DeviceType;
using FrameworkVibratorState = SeedCut.Framework.Core.VibratorState;

namespace SeedCut.Services.DeviceAdapter
{
    /// <summary>
    /// 振动盘设备适配器 - 实现 IVibratorDevice 接口
    /// 职责：
    /// 1. 实现 IDevice/IVibratorDevice 接口，供 DeviceManager 统一管理
    /// 2. 通过 VibratorServiceConnectAdapter 实现连接管理
    /// 3. 通过 IVibratorService 进行实际振动盘操作
    /// </summary>
    public class VibratorDeviceAdapter : IVibratorDevice
    {
        #region 私有字段

        private readonly VibratorServiceConnectAdapter _adapter;
        private readonly IVibratorService _vibratorService;
        private readonly ConnectionManager _connectionManager;
        private readonly ILogService _logService;

        private DeviceConnectionState _connectionState = DeviceConnectionState.Disconnected;
        private FrameworkVibratorState _vibratorState = FrameworkVibratorState.Idle;
        private string _lastError;
        private bool _disposed;

        // 配置
        private readonly VibratorDeviceConfig _deviceConfig;

        #endregion

        #region IDevice 属性

        /// <summary>
        /// 设备唯一标识
        /// </summary>
        public string DeviceId => "Vibrator";

        /// <summary>
        /// 设备显示名称
        /// </summary>
        public string DeviceName => _adapter.DeviceName;

        /// <summary>
        /// 设备类型
        /// </summary>
        public FrameworkDeviceType DeviceType => FrameworkDeviceType.Vibrator;

        /// <summary>
        /// 连接状态
        /// </summary>
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

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _adapter.IsConnected;

        /// <summary>
        /// 最后错误信息
        /// </summary>
        public string LastError => _lastError;

        #endregion

        #region IVibratorDevice 属性

        /// <summary>
        /// 振动盘状态
        /// </summary>
        public FrameworkVibratorState VibratorState
        {
            get => _vibratorState;
            private set
            {
                if (_vibratorState != value)
                {
                    var oldState = _vibratorState;
                    _vibratorState = value;
                    RaiseStateChanged(oldState, value);
                }
            }
        }

        /// <summary>
        /// 光源A是否开启
        /// </summary>
        public bool IsLightAOn => _vibratorService.IsLightAOn;

        /// <summary>
        /// 光源B是否开启
        /// </summary>
        public bool IsLightBOn => _vibratorService.IsLightBOn;

        /// <summary>
        /// 是否正在进料（抖料）
        /// </summary>
        public bool IsFeeding => _vibratorService.IsFeeding;

        /// <summary>
        /// 是否正在振动
        /// </summary>
        public bool IsVibrating => _vibratorService.IsVibrating;

        /// <summary>
        /// 排料门是否打开
        /// </summary>
        public bool IsPourDoorOpen => _vibratorService.IsPourDoorOpen;

        #endregion

        #region 扩展属性

        /// <summary>
        /// 是否正在重连
        /// </summary>
        public bool IsReconnecting => _connectionManager?.IsReconnecting ?? false;

        /// <summary>
        /// 当前重试次数
        /// </summary>
        public int CurrentRetryCount => _connectionManager?.CurrentRetryCount ?? 0;

        /// <summary>
        /// 振动盘配置
        /// </summary>
        public VibratorDeviceConfig DeviceConfig => _deviceConfig;

        /// <summary>
        /// 获取底层 Service（用于高级操作或测试）
        /// </summary>
        public IVibratorService Service => _vibratorService;

        #endregion

        #region 事件

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event EventHandler<DeviceConnectionChangedEventArgs> ConnectionChanged;

        /// <summary>
        /// 错误发生事件
        /// </summary>
        public event EventHandler<DeviceErrorEventArgs> ErrorOccurred;

        /// <summary>
        /// 振动盘状态变化事件
        /// </summary>
        public event EventHandler<VibratorStateChangedEventArgs> StateChanged;

        /// <summary>
        /// 重连尝试事件
        /// </summary>
        public event EventHandler<ReconnectionAttemptEventArgs> ReconnectionAttempt;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建振动盘设备（带日志服务，支持连接管理）
        /// </summary>
        /// <param name="adapter">连接适配器（负责连接管理）</param>
        /// <param name="vibratorService">振动盘服务（负责实际操作）</param>
        /// <param name="logService">日志服务</param>
        /// <param name="config">设备配置（可选）</param>
        public VibratorDeviceAdapter(
            VibratorServiceConnectAdapter adapter,
            IVibratorService vibratorService,
            ILogService logService,
            VibratorDeviceConfig config = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _vibratorService = vibratorService ?? throw new ArgumentNullException(nameof(vibratorService));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _deviceConfig = config ?? CreateDefaultConfig();

            // 创建连接管理器（内含心跳检测和断线重连逻辑）
            _connectionManager = new ConnectionManager(_adapter, _logService);

            // 订阅事件
            SubscribeEvents();

            _logService.Information("[{DeviceName}] 振动盘设备已创建", DeviceName);
        }

        /// <summary>
        /// 创建振动盘设备（无日志服务，无连接管理）
        /// 用于简单测试场景
        /// </summary>
        /// <param name="adapter">连接适配器</param>
        /// <param name="vibratorService">振动盘服务</param>
        public VibratorDeviceAdapter(
            VibratorServiceConnectAdapter adapter,
            IVibratorService vibratorService)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _vibratorService = vibratorService ?? throw new ArgumentNullException(nameof(vibratorService));
            _logService = null;
            _connectionManager = null;
            _deviceConfig = CreateDefaultConfig();

            // 订阅基本事件（不包含重连事件）
            SubscribeBasicEvents();
        }

        /// <summary>
        /// 从服务配置创建默认设备配置
        /// </summary>
        private VibratorDeviceConfig CreateDefaultConfig()
        {
            var serviceConfig = _vibratorService.Config;
            if (serviceConfig != null)
            {
                return new VibratorDeviceConfig
                {
                    IPAddress = serviceConfig.IPAddress,
                    Port = serviceConfig.Port,
                    StationId = serviceConfig.StationId,
                    LightAAddress = serviceConfig.LightAAddress,
                    LightBAddress = serviceConfig.LightBAddress,
                    BoxFeedAddress = serviceConfig.BoxFeedAddress,
                    VibrationAddress = serviceConfig.VibrationAddress,
                    PourDoorAddress = serviceConfig.PourDoorAddress,
                    StateAddress = serviceConfig.StateAddress,
                    FeedDuration = serviceConfig.FeedDuration
                };
            }
            return new VibratorDeviceConfig();
        }

        #endregion

        #region IDevice 方法

        /// <summary>
        /// 异步连接设备
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            try
            {
                ConnectionState = DeviceConnectionState.Connecting;
                VibratorState = FrameworkVibratorState.Unknown;

                bool result;
                if (_connectionManager != null)
                {
                    // 使用连接管理器连接（带心跳和重连支持）
                    result = await _connectionManager.ConnectAsync();
                }
                else
                {
                    // 直接使用适配器连接（无心跳支持）
                    result = await Task.Run(() => _adapter.Connect(), ct);
                }

                ConnectionState = result
                    ? DeviceConnectionState.Connected
                    : DeviceConnectionState.Error;

                VibratorState = result ? FrameworkVibratorState.Idle : FrameworkVibratorState.Error;

                return result;
            }
            catch (OperationCanceledException)
            {
                ConnectionState = DeviceConnectionState.Disconnected;
                VibratorState = FrameworkVibratorState.Disconnected;
                return false;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                ConnectionState = DeviceConnectionState.Error;
                VibratorState = FrameworkVibratorState.Error;
                RaiseError(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 异步断开设备
        /// ✅ 修复：改进断开逻辑，避免状态检查导致的错误
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                // ✅ 修复：只有在设备实际连接时才尝试停止操作
                // 使用 _vibratorService.IsConnected 而不是 IsConnected 属性，
                // 因为后者可能已经被更新为 false
                if (_vibratorService.IsConnected)
                {
                    // 尝试停止振动和进料，但不让失败阻止断开
                    try
                    {
                        if (_vibratorService.IsVibrating)
                        {
                            await _vibratorService.StopVibrationAsync().ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logService?.Warning("[{DeviceName}] 断开前停止振动失败（已忽略）: {Error}", DeviceName, ex.Message);
                    }

                    try
                    {
                        if (_vibratorService.IsFeeding)
                        {
                            await _vibratorService.StopFeedAsync().ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logService?.Warning("[{DeviceName}] 断开前停止进料失败（已忽略）: {Error}", DeviceName, ex.Message);
                    }
                }

                // 执行断开操作
                if (_connectionManager != null)
                {
                    // 使用连接管理器断开（会停止心跳和重连）
                    _connectionManager.Disconnect();
                }
                else
                {
                    // 直接使用适配器断开
                    _adapter.Disconnect();
                }

                ConnectionState = DeviceConnectionState.Disconnected;
                VibratorState = FrameworkVibratorState.Disconnected;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _logService?.Error(ex, "[{DeviceName}] 断开连接异常", DeviceName);
                // ✅ 修复：即使出错也要更新状态
                ConnectionState = DeviceConnectionState.Disconnected;
                VibratorState = FrameworkVibratorState.Disconnected;
            }
        }

        /// <summary>
        /// 重置设备
        /// </summary>
        public async Task<bool> ResetAsync()
        {
            try
            {
                // 停止所有操作
                if (IsVibrating)
                {
                    await StopVibrationAsync();
                }
                if (IsFeeding)
                {
                    await StopFeedAsync();
                }

                // 关闭所有光源
                await SetLightAAsync(false);
                await SetLightBAsync(false);

                // 关闭排料门
                await SetPourDoorAsync(false);

                // 重置状态
                VibratorState = FrameworkVibratorState.Idle;
                _lastError = null;

                _logService?.Information("[{DeviceName}] 设备已重置", DeviceName);
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                RaiseError(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 健康检查
        /// </summary>
        public Task<bool> CheckHealthAsync(CancellationToken ct = default)
        {
            var isHealthy = _adapter.CheckConnection();
            return Task.FromResult(isHealthy);
        }

        #endregion

        #region 光源控制

        /// <summary>
        /// 设置光源A状态
        /// </summary>
        public async Task<bool> SetLightAAsync(bool turnOn, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            return await _vibratorService.SetLightAAsync(turnOn);
        }

        /// <summary>
        /// 设置光源B状态
        /// </summary>
        public async Task<bool> SetLightBAsync(bool turnOn, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            return await _vibratorService.SetLightBAsync(turnOn);
        }

        /// <summary>
        /// 切换光源A状态
        /// </summary>
        public async Task<bool> ToggleLightAAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            return await _vibratorService.ToggleLightAAsync();
        }

        /// <summary>
        /// 切换光源B状态
        /// </summary>
        public async Task<bool> ToggleLightBAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            return await _vibratorService.ToggleLightBAsync();
        }

        /// <summary>
        /// 读取光源A状态
        /// </summary>
        public async Task<bool> ReadLightAAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            return await _vibratorService.ReadLightAAsync();
        }

        /// <summary>
        /// 读取光源B状态
        /// </summary>
        public async Task<bool> ReadLightBAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            return await _vibratorService.ReadLightBAsync();
        }

        #endregion

        #region 进料控制（抖料）

        /// <summary>
        /// 开始进料（抖料）
        /// </summary>
        public async Task<bool> StartFeedAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            VibratorState = FrameworkVibratorState.Feeding;
            var result = await _vibratorService.StartFeedAsync();

            if (!result)
            {
                VibratorState = FrameworkVibratorState.Idle;
            }

            return result;
        }

        /// <summary>
        /// 停止进料
        /// </summary>
        public async Task<bool> StopFeedAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            var result = await _vibratorService.StopFeedAsync();

            if (result && !IsVibrating)
            {
                VibratorState = FrameworkVibratorState.Idle;
            }

            return result;
        }

        /// <summary>
        /// 执行一次进料周期
        /// </summary>
        public async Task<bool> FeedOnceAsync(int durationMs = 0, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            // 使用配置的默认时间或指定时间
            int feedDuration = durationMs > 0 ? durationMs : _deviceConfig.FeedDuration;

            VibratorState = FrameworkVibratorState.Feeding;

            // StartFeedAsync 内部已包含延时和停止逻辑
            var result = await _vibratorService.StartFeedAsync();

            if (!IsVibrating)
            {
                VibratorState = FrameworkVibratorState.Idle;
            }

            return result;
        }

        #endregion

        #region 振动控制

        /// <summary>
        /// 启动振动（组合1）
        /// </summary>
        public async Task<bool> StartVibrationAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            VibratorState = FrameworkVibratorState.Vibrating;
            var result = await _vibratorService.StartVibrationAsync();

            if (!result)
            {
                VibratorState = FrameworkVibratorState.Idle;
            }

            return result;
        }

        /// <summary>
        /// 启动振动（组合2）
        /// </summary>
        public async Task<bool> StartVibrationGroup2Async(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            VibratorState = FrameworkVibratorState.Vibrating;
            var result = await _vibratorService.StartVibrationGroup2Async();

            if (!result)
            {
                VibratorState = FrameworkVibratorState.Idle;
            }

            return result;
        }

        /// <summary>
        /// 停止振动
        /// </summary>
        public async Task<bool> StopVibrationAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            var result = await _vibratorService.StopVibrationAsync();

            if (result && !IsFeeding)
            {
                VibratorState = FrameworkVibratorState.Idle;
            }

            return result;
        }

        /// <summary>
        /// 设置振动频率（预留接口，需要硬件支持）
        /// </summary>
        public async Task<bool> SetVibrationFrequencyAsync(int frequency, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            // 注意：当前 VibratorService 不支持频率设置，此处预留接口
            _logService?.Warning("[{DeviceName}] 设置振动频率功能暂未实现", DeviceName);
            await Task.CompletedTask;
            return false;
        }

        /// <summary>
        /// 设置振动强度（预留接口，需要硬件支持）
        /// </summary>
        public async Task<bool> SetVibrationIntensityAsync(int intensity, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            // 注意：当前 VibratorService 不支持强度设置，此处预留接口
            _logService?.Warning("[{DeviceName}] 设置振动强度功能暂未实现", DeviceName);
            await Task.CompletedTask;
            return false;
        }

        #endregion

        #region 排料门控制

        /// <summary>
        /// 设置排料门状态
        /// </summary>
        public async Task<bool> SetPourDoorAsync(bool open, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            return await _vibratorService.SetPourDoorAsync(open);
        }

        /// <summary>
        /// 切换排料门状态
        /// </summary>
        public async Task<bool> TogglePourDoorAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            return await _vibratorService.TogglePourDoorAsync();
        }

        #endregion

        #region 状态读取

        /// <summary>
        /// 读取设备状态
        /// </summary>
        public async Task<ushort> ReadStateAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                return 0;
            }

            return await _vibratorService.ReadStateAsync();
        }

        /// <summary>
        /// 刷新设备状态
        /// </summary>
        public async Task RefreshStatusAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                return;
            }

            await _vibratorService.RefreshStatusAsync();

            // 根据服务状态更新设备状态
            UpdateVibratorState();
        }

        /// <summary>
        /// 根据服务状态更新设备状态
        /// </summary>
        private void UpdateVibratorState()
        {
            if (!IsConnected)
            {
                VibratorState = FrameworkVibratorState.Disconnected;
            }
            else if (IsVibrating)
            {
                VibratorState = FrameworkVibratorState.Vibrating;
            }
            else if (IsFeeding)
            {
                VibratorState = FrameworkVibratorState.Feeding;
            }
            else
            {
                VibratorState = FrameworkVibratorState.Idle;
            }
        }

        #endregion

        #region 复合操作

        /// <summary>
        /// 执行完整的进料流程（开光源->抖料->振动->关光源）
        /// </summary>
        public async Task<bool> ExecuteFeedCycleAsync(VibratorFeedCycleConfig config = null, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            config = config ?? new VibratorFeedCycleConfig();

            try
            {
                _logService?.Information("[{DeviceName}] 开始执行进料流程", DeviceName);

                // 1. 开启光源
                if (config.EnableLightA)
                {
                    await SetLightAAsync(true, ct);
                }
                if (config.EnableLightB)
                {
                    await SetLightBAsync(true, ct);
                }

                // 2. 执行抖料
                VibratorState = FrameworkVibratorState.Feeding;
                await _vibratorService.StartFeedAsync();

                // 3. 启动振动
                VibratorState = FrameworkVibratorState.Vibrating;
                if (config.UseVibrationGroup2)
                {
                    await _vibratorService.StartVibrationGroup2Async();
                }
                else
                {
                    await _vibratorService.StartVibrationAsync();
                }

                // 4. 等待振动时间
                await Task.Delay(config.VibrationDurationMs, ct);

                // 5. 停止振动
                await _vibratorService.StopVibrationAsync();

                // 6. 稳定延时
                if (config.StabilizationDelayMs > 0)
                {
                    await Task.Delay(config.StabilizationDelayMs, ct);
                }

                // 7. 关闭光源
                if (config.EnableLightA)
                {
                    await SetLightAAsync(false, ct);
                }
                if (config.EnableLightB)
                {
                    await SetLightBAsync(false, ct);
                }

                VibratorState = FrameworkVibratorState.Idle;
                _logService?.Information("[{DeviceName}] 进料流程完成", DeviceName);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logService?.Warning("[{DeviceName}] 进料流程被取消", DeviceName);
                await StopVibrationAsync();
                await StopFeedAsync();
                VibratorState = FrameworkVibratorState.Idle;
                return false;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                RaiseError($"进料流程失败: {ex.Message}");
                VibratorState = FrameworkVibratorState.Error;
                return false;
            }
        }

        /// <summary>
        /// 执行排料流程（停止振动->打开排料门->延时->关闭排料门）
        /// </summary>
        public async Task<bool> ExecutePourCycleAsync(int pourDurationMs = 2000, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("振动盘未连接");
                return false;
            }

            try
            {
                _logService?.Information("[{DeviceName}] 开始执行排料流程", DeviceName);

                // 1. 停止振动
                if (IsVibrating)
                {
                    await StopVibrationAsync(ct);
                }

                // 2. 打开排料门
                await SetPourDoorAsync(true, ct);

                // 3. 等待排料时间
                await Task.Delay(pourDurationMs, ct);

                // 4. 关闭排料门
                await SetPourDoorAsync(false, ct);

                _logService?.Information("[{DeviceName}] 排料流程完成", DeviceName);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logService?.Warning("[{DeviceName}] 排料流程被取消", DeviceName);
                await SetPourDoorAsync(false);
                return false;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                RaiseError($"排料流程失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 事件订阅

        private void SubscribeEvents()
        {
            // 订阅基本事件
            SubscribeBasicEvents();

            // 订阅连接管理器事件
            if (_connectionManager != null)
            {
                _connectionManager.ConnectionStateChanged += OnConnectionManagerStateChanged;
                _connectionManager.ReconnectionAttempt += OnReconnectionAttempt;
            }
        }

        private void SubscribeBasicEvents()
        {
            // 订阅适配器连接状态事件
            _adapter.ConnectionStateChanged += OnAdapterConnectionStateChanged;

            // 订阅振动盘服务事件
            _vibratorService.ConnectionChanged += OnVibratorServiceConnectionChanged;
            _vibratorService.StatusChanged += OnVibratorServiceStatusChanged;
            _vibratorService.ErrorOccurred += OnVibratorServiceError;
        }

        private void UnsubscribeEvents()
        {
            // 取消订阅适配器事件
            _adapter.ConnectionStateChanged -= OnAdapterConnectionStateChanged;

            // 取消订阅振动盘服务事件
            _vibratorService.ConnectionChanged -= OnVibratorServiceConnectionChanged;
            _vibratorService.StatusChanged -= OnVibratorServiceStatusChanged;
            _vibratorService.ErrorOccurred -= OnVibratorServiceError;

            // 取消订阅连接管理器事件
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
            // 仅当没有连接管理器时，直接响应适配器状态变化
            if (_connectionManager == null)
            {
                ConnectionState = e.IsConnected
                    ? DeviceConnectionState.Connected
                    : DeviceConnectionState.Disconnected;

                VibratorState = e.IsConnected ? FrameworkVibratorState.Idle : FrameworkVibratorState.Disconnected;
            }
        }

        private void OnConnectionManagerStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            // 通过连接管理器更新状态
            if (e.IsConnected)
            {
                ConnectionState = DeviceConnectionState.Connected;
                VibratorState = FrameworkVibratorState.Idle;
            }
            else if (_connectionManager.IsReconnecting)
            {
                ConnectionState = DeviceConnectionState.Reconnecting;
            }
            else
            {
                ConnectionState = DeviceConnectionState.Disconnected;
                VibratorState = FrameworkVibratorState.Disconnected;
            }
        }

        private void OnReconnectionAttempt(object sender, ReconnectionAttemptEventArgs e)
        {
            // 更新状态
            if (!e.Success && !e.IsMaxRetryReached)
            {
                ConnectionState = DeviceConnectionState.Reconnecting;
            }
            else if (e.IsMaxRetryReached)
            {
                ConnectionState = DeviceConnectionState.Error;
                VibratorState = FrameworkVibratorState.Error;
                _lastError = "达到最大重连次数";
            }

            // 转发事件
            ReconnectionAttempt?.Invoke(this, e);
        }

        private void OnVibratorServiceConnectionChanged(object sender, bool isConnected)
        {
            if (_connectionManager == null)
            {
                ConnectionState = isConnected
                    ? DeviceConnectionState.Connected
                    : DeviceConnectionState.Disconnected;
            }
        }

        private void OnVibratorServiceStatusChanged(object sender, string status)
        {
            _logService?.Debug("[{DeviceName}] 状态: {Status}", DeviceName, status);
        }

        private void OnVibratorServiceError(object sender, string error)
        {
            _lastError = error;
            RaiseError(error);
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

        private void RaiseStateChanged(FrameworkVibratorState oldState, FrameworkVibratorState newState)
        {
            StateChanged?.Invoke(this, new VibratorStateChangedEventArgs
            {
                OldState = oldState,
                NewState = newState,
                Message = $"状态变化: {oldState} -> {newState}"
            });
        }

        private void RaiseError(string message)
        {
            ErrorOccurred?.Invoke(this, new DeviceErrorEventArgs
            {
                DeviceId = DeviceId,
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

            // 停止所有操作
            try
            {
                if (IsVibrating)
                {
                    StopVibrationAsync().Wait(TimeSpan.FromSeconds(3));
                }
                if (IsFeeding)
                {
                    StopFeedAsync().Wait(TimeSpan.FromSeconds(3));
                }
            }
            catch { }

            // 取消订阅事件
            UnsubscribeEvents();

            // 释放连接管理器
            _connectionManager?.Dispose();

            // 注意：不要释放 _adapter 和 _vibratorService
            // 它们的生命周期由外部管理

            _disposed = true;

            _logService?.Information("[{DeviceName}] 振动盘设备已释放", DeviceName);
        }

        #endregion
    }
}