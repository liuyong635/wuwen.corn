using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Services.Connection;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Services
{
    /// <summary>
    /// 机器人服务连接适配器 - 实现 IConnectableDevice 接口
    /// 用于将 IRobotService 集成到统一的连接监控系统
    /// </summary>
    public class RobotServiceConnectAdapter : IConnectableDevice
    {
        private readonly IRobotService _robotService;
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

        // 记录断线前的伺服状态（用于安全提示）
        private bool _servoEnabledBeforeDisconnect = false;

        #region IConnectableDevice 属性

        public string DeviceId => "Robot_Main";

        public string DeviceName => _robotService.Config?.DeviceName ?? "SCARA机器人";

        public DeviceType DeviceType => DeviceType.Robot;

        public bool IsConnected => _robotService.IsConnected;

        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

        #endregion

        #region 构造函数

        public RobotServiceConnectAdapter(IRobotService robotService, ILogService logService = null)
        {
            _robotService = robotService ?? throw new ArgumentNullException(nameof(robotService));
            _logService = logService;
            _connectionTimeoutMs = robotService.Config?.ConnectTimeout ?? 10000;

            // 订阅原始服务的连接状态变化事件
            _robotService.ConnectionStatusChanged += OnRobotConnectionStatusChanged;

            _logService?.Information("[机器人适配器] 已初始化 (IP: {IP}, 端口: {Port})",
                _robotService.Config?.IPAddress ?? "未配置",
                _robotService.Config?.Port ?? 0);
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
                _logService?.Debug("[机器人] 开始连接...");

                // 使用 Task.Run 避免死锁，并设置超时
                var connectTask = Task.Run(async () => await _robotService.ConnectAsync());

                // 等待连接完成或超时
                bool completed = connectTask.Wait(_connectionTimeoutMs + 2000); // 额外2秒缓冲

                if (!completed)
                {
                    _logService?.Warning("[机器人] 连接超时");
                    return false;
                }

                var result = connectTask.Result;

                if (result.success)
                {
                    _heartbeatFailCount = 0;
                    _logService?.Information("[机器人] 连接成功");
                }
                else
                {
                    _logService?.Warning("[机器人] 连接失败: {Message}", result.message);
                }

                return result.success;
            }
            catch (AggregateException ae)
            {
                var innerEx = ae.InnerException ?? ae;
                _logService?.Error(innerEx, "[机器人] 连接异常");
                return false;
            }
            catch (Exception ex)
            {
                _logService?.Error(ex, "[机器人] 连接异常");
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

                // 记录断线前的伺服状态
                _servoEnabledBeforeDisconnect = _robotService.IsServoEnabled;

                if (_servoEnabledBeforeDisconnect)
                {
                    _logService?.Warning("[机器人] ⚠️ 断开时伺服处于使能状态");
                }

                // 异步断开（DisconnectAsync 内部会关闭伺服）
                var disconnectTask = Task.Run(async () => await _robotService.DisconnectAsync());
                disconnectTask.Wait(5000); // 最多等待5秒

                _heartbeatFailCount = 0;
                _logService?.Information("[机器人] 已断开连接");
            }
            catch (Exception ex)
            {
                _logService?.Error(ex, "[机器人] 断开连接异常");
            }
        }

        /// <summary>
        /// 心跳检测 - 通过读取伺服状态寄存器验证连接
        /// </summary>
        public bool CheckConnection()
        {
            // 1. 检查基本连接状态
            if (!_robotService.IsConnected)
            {
                _logService?.Debug("[机器人心跳] 基本连接状态：未连接");
                _heartbeatFailCount = 0;
                return false;
            }

            _heartbeatStopwatch.Restart();

            try
            {
                // 2. 尝试读取伺服状态（轻量级心跳）
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
                    _logService?.Warning("[机器人心跳] 响应较慢: {Elapsed}ms", elapsedMs);
                }

                // 5. 处理心跳结果
                if (!heartbeatSuccess)
                {
                    _heartbeatFailCount++;
                    _failedHeartbeats++;
                    _logService?.Warning("[机器人心跳] 检测失败 (耗时: {Elapsed}ms, 失败次数: {FailCount}/{MaxCount})",
                        elapsedMs, _heartbeatFailCount, MAX_HEARTBEAT_FAIL_COUNT);

                    if (_heartbeatFailCount >= MAX_HEARTBEAT_FAIL_COUNT)
                    {
                        _logService?.Error("[机器人心跳] 连续失败，判定为断线");
                        OnConnectionStateChanged(false, "机器人心跳检测失败");
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
                    _logService?.Verbose("[机器人心跳] 统计 - 总次数: {Total}, 失败: {Failed}, 平均耗时: {Avg}ms, 最大耗时: {Max}ms",
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
                _logService?.Error(ex, "[机器人心跳] 检测异常 (失败次数: {FailCount}/{MaxCount})",
                    _heartbeatFailCount, MAX_HEARTBEAT_FAIL_COUNT);

                if (_heartbeatFailCount >= MAX_HEARTBEAT_FAIL_COUNT)
                {
                    OnConnectionStateChanged(false, "机器人心跳检测异常");
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
                MaxRetryCount = 5,                  // 机器人重连次数较少
                InitialRetryDelayMs = 5000,         // 首次等待较长（机器人启动可能较慢）
                MaxRetryDelayMs = 60000,            // 最大1分钟间隔
                Strategy = ReconnectionStrategy.LinearBackoff,  // 线性递增更平稳
                HeartbeatIntervalMs = 500,          // 500ms心跳间隔
                ConnectionTimeoutMs = 10000,        // 连接超时10秒
                AutoConnectOnStartup = false        // 机器人不自动连接（安全考虑）
            };
        }

        #endregion

        #region 私有方法 - 心跳实现

        /// <summary>
        /// 执行心跳检测 - 读取伺服状态
        /// </summary>
        private bool PerformHeartbeat()
        {
            try
            {
                // 使用读取伺服状态作为心跳（轻量级，只读1个寄存器）
                var task = Task.Run(async () => await _robotService.GetServoEnableAsync());

                // 设置心跳超时（比连接超时短）
                bool completed = task.Wait(3000);

                if (!completed)
                {
                    _logService?.Debug("[机器人心跳] 读取伺服状态超时");
                    return false;
                }

                // 读取成功即表示连接正常
                return true;
            }
            catch (InvalidOperationException)
            {
                // "请先连接机器人" 异常表示连接已断开
                return false;
            }
            catch (Exception ex)
            {
                _logService?.Debug("[机器人心跳] 读取异常: {Error}", ex.Message);
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
                // 读取坐标检查机器人响应
                var coordTask = Task.Run(async () => await _robotService.ReadCoordinatesAsync());

                if (coordTask.Wait(2000))
                {
                    var coords = coordTask.Result;
                    _logService?.Verbose("[机器人健康] 坐标: {Coords}", coords?.ToString() ?? "NULL");
                }
                else
                {
                    _logService?.Warning("[机器人健康] 读取坐标超时");
                }
            }
            catch (Exception ex)
            {
                _logService?.Debug("[机器人健康] 扩展检查异常: {Error}", ex.Message);
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
                    "[机器人心跳] 最终统计 - 总次数: {Total}, 失败: {Failed}, 成功率: {Rate:F2}%, 平均耗时: {Avg}ms, 最大耗时: {Max}ms",
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
            // 记录断线时的伺服状态
            if (!isConnected && _robotService.IsServoEnabled)
            {
                _servoEnabledBeforeDisconnect = true;
                _logService?.Warning("[机器人] ⚠️ 断线时伺服处于使能状态，请注意安全");
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
        private void OnRobotConnectionStatusChanged(object sender, ConnectionStatusChangedEventArgs e)
        {
            if (!e.IsConnected)
            {
                _logService?.Warning("[机器人] 连接已断开: {Message}", e.Message);
                _heartbeatFailCount = 0;
            }
            else
            {
                _logService?.Information("[机器人] 连接已恢复");

                // 重连成功后的安全提示
                if (_servoEnabledBeforeDisconnect)
                {
                    _logService?.Warning("[机器人] ⚠️ 重连成功，但断线前伺服处于使能状态，请手动确认机器人状态");
                    _servoEnabledBeforeDisconnect = false;
                }
            }

            // 转发事件到连接管理器
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(
                e.IsConnected,
                e.Message,
                null
            ));
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
        /// 断线前伺服是否使能（用于UI安全提示）
        /// </summary>
        public bool WasServoEnabledBeforeDisconnect => _servoEnabledBeforeDisconnect;

        #endregion
    }
}