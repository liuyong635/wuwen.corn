using SeedCut.Framework.Services.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Services.Connection
{
    /// <summary>
    /// 优化版连接管理器 - 修复心跳波动问题
    /// </summary>
    public class ConnectionManager : IDisposable
    {
        private readonly IConnectableDevice _device;
        private readonly ReconnectionConfig _config;
        private readonly ILogService _logService;

        private Timer _heartbeatTimer;
        private Timer _reconnectTimer;
        private readonly object _lockObject = new object();
        private int _currentRetryCount = 0;
        private bool _isReconnecting = false;
        private bool _disposed = false;
        private bool _manualDisconnect = false;

        // ✅ 新增：心跳专用线程，避免线程池调度延迟
        private Thread _heartbeatThread;
        private bool _heartbeatRunning = false;
        private ManualResetEventSlim _heartbeatEvent = new ManualResetEventSlim(false);

        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;
        public event EventHandler<ReconnectionAttemptEventArgs> ReconnectionAttempt;

        public bool IsReconnecting => _isReconnecting;
        public int CurrentRetryCount => _currentRetryCount;

        public ConnectionManager(IConnectableDevice device, ILogService logService, ReconnectionConfig config = null)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _config = device.GetReconnectionConfig() ?? config ?? new ReconnectionConfig();

            _device.ConnectionStateChanged += OnDeviceConnectionStateChanged;

            _logService.Information("[{DeviceName}] 连接管理器已初始化", _device.DeviceName);
        }

        #region 连接控制

        /// <summary>
        /// ✅ 优化：添加 ConfigureAwait(false) 和超时控制
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            lock (_lockObject)
            {
                _manualDisconnect = false;
            }

            try
            {
                // ✅ 使用 ConfigureAwait(false) 避免死锁
                // ✅ 添加超时控制，防止无限等待
                using (var cts = new CancellationTokenSource(_config.ConnectionTimeoutMs))
                {
                    var connectTask = Task.Run(() => _device.Connect());
                    var completedTask = await Task.WhenAny(connectTask, Task.Delay(_config.ConnectionTimeoutMs, cts.Token)).ConfigureAwait(false);

                    bool success;
                    if (completedTask == connectTask)
                    {
                        success = await connectTask.ConfigureAwait(false);
                        cts.Cancel(); // 取消延迟任务
                    }
                    else
                    {
                        _logService.Warning("[{DeviceName}] 连接超时 ({Timeout}ms)", _device.DeviceName, _config.ConnectionTimeoutMs);
                        success = false;
                    }

                    if (success)
                    {
                        _currentRetryCount = 0;
                        StartHeartbeat();
                        _logService.Information("[{DeviceName}] 连接成功", _device.DeviceName);
                    }
                    else
                    {
                        _logService.Warning("[{DeviceName}] 连接失败", _device.DeviceName);
                    }

                    return success;
                }
            }
            catch (Exception ex)
            {
                _logService.Error(ex, "[{DeviceName}] 连接异常", _device.DeviceName);
                return false;
            }
        }

        public void Disconnect()
        {
            lock (_lockObject)
            {
                _manualDisconnect = true;
            }

            StopHeartbeat();
            StopReconnect();
            _device.Disconnect();
            _logService.Information("[{DeviceName}] 已手动断开连接", _device.DeviceName);
        }

        #endregion

        #region 心跳检测 - 优化版

        /// <summary>
        /// ✅ 启动心跳检测 - 使用专用线程
        /// </summary>
        private void StartHeartbeat()
        {
            if (_heartbeatRunning)
                return;

            _heartbeatRunning = true;
            _heartbeatEvent.Reset();

            // 创建高优先级的专用心跳线程
            _heartbeatThread = new Thread(HeartbeatThreadProc)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,  // 提高优先级
                Name = $"Heartbeat-{_device.DeviceName}"
            };
            _heartbeatThread.Start();

            _logService.Debug("[{DeviceName}] 心跳检测已启动，间隔: {Interval}ms",
                _device.DeviceName, _config.HeartbeatIntervalMs);
        }

        /// <summary>
        /// ✅ 停止心跳检测
        /// </summary>
        private void StopHeartbeat()
        {
            _heartbeatRunning = false;
            _heartbeatEvent.Set();  // 唤醒线程以便退出

            if (_heartbeatThread != null && _heartbeatThread.IsAlive)
            {
                if (!_heartbeatThread.Join(500))  // 等待0.5秒
                {
                    _logService.Warning("[{DeviceName}] 心跳线程未能正常退出", _device.DeviceName);
                }
                _heartbeatThread = null;
            }

            _logService.Debug("[{DeviceName}] 心跳检测已停止", _device.DeviceName);
        }

        /// <summary>
        /// ✅ 心跳线程主循环 - 避免Task.Run开销
        /// </summary>
        private void HeartbeatThreadProc()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (_heartbeatRunning)
            {
                try
                {
                    // 跳过重连期间的心跳
                    if (_isReconnecting)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    // ✅ 直接调用，无嵌套Task.Run
                    var isConnected = _device.CheckConnection();

                    if (!isConnected && _device.IsConnected)
                    {
                        _logService.Warning("[{DeviceName}] 心跳检测失败，设备可能已断开",
                            _device.DeviceName);
                        OnConnectionLost();
                    }
                }
                catch (Exception ex)
                {
                    _logService.Error(ex, "[{DeviceName}] 心跳检测异常", _device.DeviceName);
                }

                // ✅ 精确定时：补偿执行耗时
                var elapsed = sw.ElapsedMilliseconds;
                var sleepTime = Math.Max(0, _config.HeartbeatIntervalMs - (int)elapsed);

                if (sleepTime > 0)
                {
                    // 使用事件等待，支持快速退出
                    _heartbeatEvent.Wait(sleepTime);
                }

                sw.Restart();
            }
        }

        #endregion

        #region 自动重连

        private void OnConnectionLost()
        {
            lock (_lockObject)
            {
                if (_manualDisconnect)
                {
                    _logService.Information("[{DeviceName}] 手动断开，跳过自动重连", _device.DeviceName);
                    return;
                }

                if (_isReconnecting)
                    return;

                if (!_config.EnableAutoReconnect)
                {
                    _logService.Warning("[{DeviceName}] 连接丢失，但自动重连已禁用", _device.DeviceName);
                    return;
                }

                _isReconnecting = true;
                _currentRetryCount = 0;
            }

            StopHeartbeat();
            _logService.Warning("[{DeviceName}] 检测到连接丢失，启动自动重连", _device.DeviceName);

            ScheduleReconnect(0);
        }

        private void ScheduleReconnect(int delayMs)
        {
            if (_reconnectTimer != null)
            {
                _reconnectTimer.Dispose();
                _reconnectTimer = null;
            }

            _reconnectTimer = new Timer(
                ReconnectCallback,
                null,
                delayMs,
                Timeout.Infinite
            );

            if (delayMs > 0)
            {
                _logService.Information("[{DeviceName}] 将在 {Delay}ms 后尝试重连",
                    _device.DeviceName, delayMs);
            }
        }

        private void ReconnectCallback(object state)
        {
            lock (_lockObject)
            {
                if (_manualDisconnect || !_config.EnableAutoReconnect)
                {
                    _isReconnecting = false;
                    return;
                }

                if (_config.MaxRetryCount > 0 && _currentRetryCount >= _config.MaxRetryCount)
                {
                    _logService.Error("[{DeviceName}] 已达到最大重试次数 ({MaxRetry})，停止重连",
                        _device.DeviceName, _config.MaxRetryCount);
                    _isReconnecting = false;

                    ReconnectionAttempt?.Invoke(this, new ReconnectionAttemptEventArgs
                    {
                        DeviceName = _device.DeviceName,
                        RetryCount = _currentRetryCount,
                        Success = false,
                        IsMaxRetryReached = true
                    });

                    return;
                }

                _currentRetryCount++;
            }

            _logService.Information("[{DeviceName}] 尝试重连 (第 {RetryCount} 次)",
                _device.DeviceName, _currentRetryCount);

            ReconnectionAttempt?.Invoke(this, new ReconnectionAttemptEventArgs
            {
                DeviceName = _device.DeviceName,
                RetryCount = _currentRetryCount,
                Success = false,
                IsMaxRetryReached = false
            });

            try
            {
                var success = _device.Connect();

                if (success)
                {
                    lock (_lockObject)
                    {
                        _isReconnecting = false;
                        _currentRetryCount = 0;
                    }

                    StartHeartbeat();
                    _logService.Information("[{DeviceName}] 重连成功 (尝试了 {RetryCount} 次)",
                        _device.DeviceName, _currentRetryCount);

                    ReconnectionAttempt?.Invoke(this, new ReconnectionAttemptEventArgs
                    {
                        DeviceName = _device.DeviceName,
                        RetryCount = _currentRetryCount,
                        Success = true,
                        IsMaxRetryReached = false
                    });
                }
                else
                {
                    var nextDelay = ReconnectionStrategyCalculator.CalculateNextDelay(_config, _currentRetryCount - 1);
                    _logService.Warning("[{DeviceName}] 重连失败，将在 {Delay}ms 后重试",
                        _device.DeviceName, nextDelay);
                    ScheduleReconnect(nextDelay);
                }
            }
            catch (Exception ex)
            {
                _logService.Error(ex, "[{DeviceName}] 重连过程发生异常", _device.DeviceName);
                var nextDelay = ReconnectionStrategyCalculator.CalculateNextDelay(_config, _currentRetryCount - 1);
                ScheduleReconnect(nextDelay);
            }
        }

        private void StopReconnect()
        {
            lock (_lockObject)
            {
                _isReconnecting = false;
                _currentRetryCount = 0;
            }

            if (_reconnectTimer != null)
            {
                _reconnectTimer.Dispose();
                _reconnectTimer = null;
                _logService.Debug("[{DeviceName}] 重连任务已停止", _device.DeviceName);
            }
        }

        #endregion

        #region 事件处理

        private void OnDeviceConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            ConnectionStateChanged?.Invoke(this, e);

            if (!e.IsConnected && !_manualDisconnect && _config.EnableAutoReconnect)
            {
                OnConnectionLost();
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            _manualDisconnect = true;

            StopHeartbeat();
            StopReconnect();

            _heartbeatEvent?.Dispose();

            if (_device != null)
            {
                _device.ConnectionStateChanged -= OnDeviceConnectionStateChanged;
            }

            _disposed = true;
        }

        #endregion
    }

    public class ReconnectionAttemptEventArgs : EventArgs
    {
        public string DeviceName { get; set; }
        public int RetryCount { get; set; }
        public bool Success { get; set; }
        public bool IsMaxRetryReached { get; set; }
        public DateTime AttemptTime { get; set; } = DateTime.Now;
    }
}