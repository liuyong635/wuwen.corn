using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Services.Connection;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SeedCut.Services
{
    /// <summary>
    /// 振动盘服务连接适配器 - 实现 IConnectableDevice 接口
    /// 用于将 IVibratorService 集成到统一的连接监控系统
    /// </summary>
    public class VibratorServiceConnectAdapter : IConnectableDevice
    {
        private readonly IVibratorService _vibratorService;
        private readonly ILogService _logService;

        // 连接超时配置
        private readonly int _connectionTimeoutMs;

        // 心跳检测配置
        private int _heartbeatFailCount = 0;
        private const int MAX_HEARTBEAT_FAIL_COUNT = 3;

        // 心跳性能监控
        private readonly Stopwatch _heartbeatStopwatch = new Stopwatch();
        private long _totalHeartbeats = 0;
        private long _failedHeartbeats = 0;
        private long _maxHeartbeatTime = 0;
        private long _avgHeartbeatTime = 0;

        // 扩展健康检查开关
        public bool EnableExtendedHealthCheck { get; set; } = false;

        // 记录断线前的设备状态（用于安全提示）
        private bool _wasVibrating = false;
        private bool _wasFeeding = false;

        #region IConnectableDevice 属性

        public string DeviceId => "Vibrator_Main";

        public string DeviceName => "振动盘控制器";

        public DeviceType DeviceType => DeviceType.Vibrator;

        public bool IsConnected => _vibratorService.IsConnected;

        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

        #endregion

        #region 构造函数

        public VibratorServiceConnectAdapter(IVibratorService vibratorService, ILogService logService = null)
        {
            _vibratorService = vibratorService ?? throw new ArgumentNullException(nameof(vibratorService));
            _logService = logService;
            _connectionTimeoutMs = 5000; // 默认5秒超时

            // 订阅原始服务的连接状态变化事件
            _vibratorService.ConnectionChanged += OnVibratorConnectionChanged;
            _vibratorService.ErrorOccurred += OnVibratorError;

            _logService?.Information("[振动盘适配器] 已初始化 (IP: {IP}, 端口: {Port})",
                _vibratorService.Config?.IPAddress ?? "未配置",
                _vibratorService.Config?.Port ?? 0);
        }

        #endregion

        #region IConnectableDevice 方法

        /// <summary>
        /// 同步连接方法 - 包装异步连接
        /// </summary>
        public bool Connect()
        {
            try
            {
                _logService?.Debug("[振动盘] 开始连接...");

                // 使用 Task.Run 避免死锁，并设置超时
                var connectTask = Task.Run(async () => await _vibratorService.ConnectAsync());

                // 等待连接完成或超时
                bool completed = connectTask.Wait(_connectionTimeoutMs + 2000); // 额外2秒缓冲

                if (!completed)
                {
                    _logService?.Warning("[振动盘] 连接超时");
                    return false;
                }

                bool success = connectTask.Result;

                if (success)
                {
                    _heartbeatFailCount = 0;
                    _logService?.Information("[振动盘] 连接成功");
                }
                else
                {
                    _logService?.Warning("[振动盘] 连接失败");
                }

                return success;
            }
            catch (AggregateException ae)
            {
                var innerEx = ae.InnerException ?? ae;
                _logService?.Error(innerEx, "[振动盘] 连接异常");
                return false;
            }
            catch (Exception ex)
            {
                _logService?.Error(ex, "[振动盘] 连接异常");
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            try
            {
                // 输出心跳统计
                PrintHeartbeatStatistics();

                // 记录断线前的设备状态
                _wasVibrating = _vibratorService.IsVibrating;
                _wasFeeding = _vibratorService.IsFeeding;

                if (_wasVibrating || _wasFeeding)
                {
                    _logService?.Warning("[振动盘] ⚠️ 断开时设备仍在运行 (振动: {Vibrating}, 抖料: {Feeding})",
                        _wasVibrating, _wasFeeding);
                }

                // 异步断开
                var disconnectTask = Task.Run(async () => await _vibratorService.DisconnectAsync());
                disconnectTask.Wait(5000); // 最多等待5秒

                _heartbeatFailCount = 0;
                _logService?.Information("[振动盘] 已断开连接");
            }
            catch (Exception ex)
            {
                _logService?.Error(ex, "[振动盘] 断开连接异常");
            }
        }

        /// <summary>
        /// 心跳检测 - 通过读取状态寄存器验证连接
        /// </summary>
        public bool CheckConnection()
        {
            // 1. 检查基本连接状态
            if (!_vibratorService.IsConnected)
            {
                _logService?.Debug("[振动盘心跳] 基本连接状态：未连接");
                _heartbeatFailCount = 0;
                return false;
            }

            _heartbeatStopwatch.Restart();

            try
            {
                // 2. 尝试读取状态寄存器（轻量级心跳）
                bool heartbeatSuccess = PerformHeartbeat();

                _heartbeatStopwatch.Stop();

                // 3. 记录性能统计
                long elapsedMs = _heartbeatStopwatch.ElapsedMilliseconds;
                _totalHeartbeats++;
                _avgHeartbeatTime = (_avgHeartbeatTime * (_totalHeartbeats - 1) + elapsedMs) / _totalHeartbeats;

                if (elapsedMs > _maxHeartbeatTime)
                {
                    _maxHeartbeatTime = elapsedMs;
                }

                // 4. 心跳耗时警告
                if (elapsedMs > 200)
                {
                    _logService?.Warning("[振动盘心跳] 响应较慢: {Elapsed}ms", elapsedMs);
                }

                // 5. 处理心跳结果
                if (!heartbeatSuccess)
                {
                    _heartbeatFailCount++;
                    _failedHeartbeats++;
                    _logService?.Warning("[振动盘心跳] 检测失败 (耗时: {Elapsed}ms, 失败次数: {FailCount}/{MaxCount})",
                        elapsedMs, _heartbeatFailCount, MAX_HEARTBEAT_FAIL_COUNT);

                    if (_heartbeatFailCount >= MAX_HEARTBEAT_FAIL_COUNT)
                    {
                        _logService?.Error("[振动盘心跳] 连续失败，判定为断线");
                        OnConnectionStateChanged(false, "振动盘心跳检测失败");
                        return false;
                    }

                    // 未达到阈值，暂时容忍
                    return true;
                }

                // 6. 心跳成功，重置失败计数
                _heartbeatFailCount = 0;

                // 7. 定期输出统计日志
                if (_totalHeartbeats % 100 == 0)
                {
                    _logService?.Verbose("[振动盘心跳] 统计 - 总次数: {Total}, 失败: {Failed}, 平均耗时: {Avg}ms, 最大耗时: {Max}ms",
                        _totalHeartbeats, _failedHeartbeats, _avgHeartbeatTime, _maxHeartbeatTime);
                }

                // 8. 扩展健康检查（可选）
                if (EnableExtendedHealthCheck)
                {
                    PerformExtendedHealthCheck();
                }

                return true;
            }
            catch (Exception ex)
            {
                _heartbeatFailCount++;
                _failedHeartbeats++;
                _logService?.Error(ex, "[振动盘心跳] 检测异常 (失败次数: {FailCount}/{MaxCount})",
                    _heartbeatFailCount, MAX_HEARTBEAT_FAIL_COUNT);

                if (_heartbeatFailCount >= MAX_HEARTBEAT_FAIL_COUNT)
                {
                    OnConnectionStateChanged(false, "振动盘心跳检测异常");
                    return false;
                }

                return true;
            }
        }

        /// <summary>
        /// 获取重连配置
        /// </summary>
        public ReconnectionConfig GetReconnectionConfig()
        {
            return new ReconnectionConfig
            {
                EnableAutoReconnect = true,
                MaxRetryCount = 8,                  // 振动盘重连次数适中
                InitialRetryDelayMs = 2000,         // 首次等待2秒
                MaxRetryDelayMs = 20000,            // 最大20秒间隔
                Strategy = ReconnectionStrategy.ExponentialBackoff,  // 指数退避
                HeartbeatIntervalMs = 500,          // 500ms心跳间隔
                ConnectionTimeoutMs = 5000,         // 连接超时5秒
                AutoConnectOnStartup = true         // 振动盘可以自动连接
            };
        }

        #endregion

        #region 私有方法 - 心跳实现

        /// <summary>
        /// 执行心跳检测 - 读取状态寄存器
        /// </summary>
        private bool PerformHeartbeat()
        {
            try
            {
                // 使用读取状态作为心跳（轻量级，只读1个寄存器）
                var task = Task.Run(async () => await _vibratorService.ReadStateAsync());

                // 设置心跳超时（比连接超时短）
                bool completed = task.Wait(3000);

                if (!completed)
                {
                    _logService?.Debug("[振动盘心跳] 读取状态超时");
                    return false;
                }

                // 读取成功即表示连接正常（返回值0也是有效的）
                return true;
            }
            catch (Exception ex)
            {
                _logService?.Debug("[振动盘心跳] 读取异常: {Error}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 扩展健康检查（可选）
        /// </summary>
        private void PerformExtendedHealthCheck()
        {
            try
            {
                // 读取光源状态
                var lightATask = Task.Run(async () => await _vibratorService.ReadLightAAsync());
                var lightBTask = Task.Run(async () => await _vibratorService.ReadLightBAsync());

                Task.WaitAll(new[] { lightATask, lightBTask }, 2000);

                _logService?.Verbose("[振动盘健康] 光源状态 - A: {LightA}, B: {LightB}",
                    _vibratorService.IsLightAOn ? "开" : "关",
                    _vibratorService.IsLightBOn ? "开" : "关");
            }
            catch (Exception ex)
            {
                _logService?.Debug("[振动盘健康] 扩展检查异常: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// 输出心跳统计信息
        /// </summary>
        private void PrintHeartbeatStatistics()
        {
            if (_totalHeartbeats > 0)
            {
                double successRate = (_totalHeartbeats - _failedHeartbeats) * 100.0 / _totalHeartbeats;
                _logService?.Information(
                    "[振动盘心跳] 最终统计 - 总次数: {Total}, 失败: {Failed}, 成功率: {Rate:F2}%, 平均耗时: {Avg}ms, 最大耗时: {Max}ms",
                    _totalHeartbeats, _failedHeartbeats, successRate, _avgHeartbeatTime, _maxHeartbeatTime);
            }
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 触发连接状态变化事件
        /// </summary>
        private void OnConnectionStateChanged(bool isConnected, string message)
        {
            // 记录断线时的设备状态
            if (!isConnected)
            {
                _wasVibrating = _vibratorService.IsVibrating;
                _wasFeeding = _vibratorService.IsFeeding;

                if (_wasVibrating || _wasFeeding)
                {
                    _logService?.Warning("[振动盘] ⚠️ 断线时设备仍在运行，请注意检查");
                }
            }

            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(
                isConnected,
                message,
                null
            ));
        }

        /// <summary>
        /// 原始服务连接状态变化处理
        /// </summary>
        private void OnVibratorConnectionChanged(object sender, bool isConnected)
        {
            if (!isConnected)
            {
                _logService?.Warning("[振动盘] 连接已断开");
                _heartbeatFailCount = 0;
            }
            else
            {
                _logService?.Information("[振动盘] 连接已恢复");

                // 重连成功后的安全提示
                if (_wasVibrating || _wasFeeding)
                {
                    _logService?.Warning("[振动盘] ⚠️ 重连成功，但断线前设备正在运行，请确认设备状态");
                    _wasVibrating = false;
                    _wasFeeding = false;
                }
            }

            // 转发事件到连接管理器
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(
                isConnected,
                isConnected ? "振动盘连接恢复" : "振动盘连接断开",
                null
            ));
        }

        /// <summary>
        /// 原始服务错误处理
        /// </summary>
        private void OnVibratorError(object sender, string errorMessage)
        {
            _logService?.Warning("[振动盘] 错误: {Error}", errorMessage);
        }

        #endregion

        #region 公共辅助方法

        /// <summary>
        /// 获取心跳统计信息
        /// </summary>
        public (long total, long failed, long avgMs, long maxMs) GetHeartbeatStatistics()
        {
            return (_totalHeartbeats, _failedHeartbeats, _avgHeartbeatTime, _maxHeartbeatTime);
        }

        /// <summary>
        /// 重置心跳统计
        /// </summary>
        public void ResetHeartbeatStatistics()
        {
            _totalHeartbeats = 0;
            _failedHeartbeats = 0;
            _avgHeartbeatTime = 0;
            _maxHeartbeatTime = 0;
            _heartbeatFailCount = 0;
        }

        /// <summary>
        /// 断线前是否在振动（用于UI安全提示）
        /// </summary>
        public bool WasVibratingBeforeDisconnect => _wasVibrating;

        /// <summary>
        /// 断线前是否在抖料（用于UI安全提示）
        /// </summary>
        public bool WasFeedingBeforeDisconnect => _wasFeeding;

        #endregion
    }
}