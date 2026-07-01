using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Services.Connection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Execution
{
    /// <summary>
    /// 执行控制器
    /// 
    /// 核心职责：
    /// 1. 管理系统运行状态（启动/停止/暂停/恢复/急停）
    /// 2. 提供统一的取消令牌管理
    /// 3. 处理设备断线事件
    /// 4. 支持Handler超时控制
    /// 
    /// 使用方式：
    /// 1. 创建实例并注入依赖
    /// 2. 订阅事件（StateChanged, HandlerTimeout等）
    /// 3. 调用 Start() 启动系统
    /// 4. Handler通过 ExecuteWithControlAsync 获得统一的控制
    /// </summary>
    public class ExecutionController : IDisposable
    {
        #region 私有字段

        private readonly IProductionContext _productionContext;
        private readonly ILogService _logService;
        private readonly ConnectionMonitorService _connectionMonitor;
        private readonly ExecutionControllerConfig _config;

        // 状态管理
        private ExecutionState _state = ExecutionState.Idle;
        private readonly object _stateLock = new object();

        // 取消令牌管理
        private CancellationTokenSource _globalCts;
        private readonly ManualResetEventSlim _pauseEvent = new ManualResetEventSlim(true);

        // 正在执行的Handler跟踪
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningHandlers
            = new ConcurrentDictionary<string, CancellationTokenSource>();

        // 设备→Handler依赖映射（用于断线时取消相关Handler）
        private readonly ConcurrentDictionary<string, HashSet<string>> _deviceHandlerMap
            = new ConcurrentDictionary<string, HashSet<string>>();

        private bool _disposed;

        #endregion

        #region 属性

        /// <summary>
        /// 当前状态
        /// </summary>
        public ExecutionState State
        {
            get { lock (_stateLock) { return _state; } }
            private set
            {
                ExecutionState oldState;
                lock (_stateLock)
                {
                    if (_state == value) return;
                    oldState = _state;
                    _state = value;
                }
                OnStateChanged(oldState, value, null);
            }
        }

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning => State == ExecutionState.Running;

        /// <summary>
        /// 是否暂停中
        /// </summary>
        public bool IsPaused => State == ExecutionState.Paused;

        /// <summary>
        /// 是否急停状态
        /// </summary>
        public bool IsEmergencyStopped => State == ExecutionState.EmergencyStopped;

        /// <summary>
        /// 配置
        /// </summary>
        public ExecutionControllerConfig Config => _config;

        /// <summary>
        /// 正在执行的Handler数量
        /// </summary>
        public int RunningHandlerCount => _runningHandlers.Count;

        #endregion

        #region 事件

        /// <summary>
        /// 状态变化事件
        /// </summary>
        public event EventHandler<ExecutionStateChangedEventArgs> StateChanged;

        /// <summary>
        /// Handler超时事件
        /// </summary>
        public event EventHandler<HandlerTimeoutEventArgs> HandlerTimeout;

        /// <summary>
        /// 设备断线导致Handler取消事件
        /// </summary>
        public event EventHandler<string> HandlerCancelledByDeviceDisconnect;

        #endregion

        #region 构造函数

        public ExecutionController(
            IProductionContext productionContext,
            ILogService logService,
            ConnectionMonitorService connectionMonitor = null,
            ExecutionControllerConfig config = null)
        {
            _productionContext = productionContext ?? throw new ArgumentNullException(nameof(productionContext));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _connectionMonitor = connectionMonitor;
            _config = config ?? new ExecutionControllerConfig();

            // 订阅设备连接状态变化
            if (_connectionMonitor != null)
            {
                _connectionMonitor.DeviceStatusChanged += OnDeviceStatusChanged;
            }

            // 订阅ProductionContext状态变化
            _productionContext.StateChanged += OnProductionStateChanged;

            _logService.Information("[ExecutionController] 已初始化");
        }

        #endregion

        #region 状态控制

        /// <summary>
        /// 启动系统
        /// </summary>
        public void Start()
        {
            lock (_stateLock)
            {
                if (_state == ExecutionState.Running)
                {
                    _logService.Warning("[ExecutionController] 系统已在运行中");
                    return;
                }

                if (_state == ExecutionState.EmergencyStopped)
                {
                    _logService.Warning("[ExecutionController] 急停状态下无法启动，请先复位");
                    return;
                }

                // 创建新的全局取消令牌
                _globalCts?.Dispose();
                _globalCts = new CancellationTokenSource();

                // 确保暂停事件处于释放状态
                _pauseEvent.Set();

                _state = ExecutionState.Running;
            }

            // 同步ProductionContext
            if (!_productionContext.IsRunning)
            {
                _productionContext.Start();
            }

            _logService.Information("[ExecutionController] 系统已启动");
            OnStateChanged(ExecutionState.Idle, ExecutionState.Running, "手动启动");
        }

        /// <summary>
        /// 停止系统（异步，等待当前任务完成）
        /// </summary>
        public async Task StopAsync()
        {
            lock (_stateLock)
            {
                if (_state == ExecutionState.Idle || _state == ExecutionState.Stopped)
                {
                    return;
                }

                _state = ExecutionState.Stopping;
            }

            _logService.Information("[ExecutionController] 正在停止系统，等待当前任务完成...");

            // 等待所有Handler完成
            var timeout = TimeSpan.FromSeconds(_config.StopWaitTimeoutSeconds);
            var waitResult = await WaitForAllHandlersAsync(timeout);

            if (!waitResult)
            {
                _logService.Warning("[ExecutionController] 等待任务完成超时，强制取消");
                CancelAllHandlers();
            }

            // 取消全局令牌
            _globalCts?.Cancel();

            lock (_stateLock)
            {
                _state = ExecutionState.Stopped;
            }

            // 同步ProductionContext
            if (_productionContext.IsRunning)
            {
                _productionContext.Stop();
            }

            _logService.Information("[ExecutionController] 系统已停止");
            OnStateChanged(ExecutionState.Stopping, ExecutionState.Stopped, "手动停止");
        }

        /// <summary>
        /// 暂停系统
        /// </summary>
        /// <param name="waitForCurrentTasks">是否等待当前任务完成</param>
        public async Task PauseAsync(bool waitForCurrentTasks = true)
        {
            lock (_stateLock)
            {
                if (_state != ExecutionState.Running)
                {
                    _logService.Warning("[ExecutionController] 非运行状态，无法暂停");
                    return;
                }

                _state = ExecutionState.Paused;
            }

            // 阻塞新任务
            _pauseEvent.Reset();

            _logService.Information("[ExecutionController] 系统已暂停" +
                (waitForCurrentTasks ? "，等待当前任务完成" : ""));

            if (waitForCurrentTasks)
            {
                var timeout = TimeSpan.FromSeconds(_config.PauseWaitTimeoutSeconds);
                await WaitForAllHandlersAsync(timeout);
            }

            // 同步ProductionContext
            if (!_productionContext.IsPaused)
            {
                _productionContext.Pause();
            }

            OnStateChanged(ExecutionState.Running, ExecutionState.Paused, "手动暂停");
        }

        /// <summary>
        /// 恢复运行
        /// </summary>
        public void Resume()
        {
            lock (_stateLock)
            {
                if (_state != ExecutionState.Paused)
                {
                    _logService.Warning("[ExecutionController] 非暂停状态，无法恢复");
                    return;
                }

                _state = ExecutionState.Running;
            }

            // 释放等待的任务
            _pauseEvent.Set();

            // 同步ProductionContext
            if (_productionContext.IsPaused)
            {
                _productionContext.Resume();
            }

            _logService.Information("[ExecutionController] 系统已恢复运行");
            OnStateChanged(ExecutionState.Paused, ExecutionState.Running, "手动恢复");
        }

        /// <summary>
        /// 急停
        /// </summary>
        public void EmergencyStop(string reason = "急停触发")
        {
            ExecutionState oldState;
            lock (_stateLock)
            {
                oldState = _state;
                _state = ExecutionState.EmergencyStopped;
            }

            _logService.Warning("[ExecutionController] ⚠️ 急停: {0}", reason);

            // 立即取消所有任务
            _globalCts?.Cancel();
            CancelAllHandlers();

            // 同步ProductionContext
            _productionContext.EmergencyStop();

            OnStateChanged(oldState, ExecutionState.EmergencyStopped, reason);
        }

        /// <summary>
        /// 复位急停
        /// </summary>
        public void ResetEmergencyStop()
        {
            lock (_stateLock)
            {
                if (_state != ExecutionState.EmergencyStopped)
                {
                    return;
                }

                _state = ExecutionState.Idle;
            }

            // 重建全局取消令牌
            _globalCts?.Dispose();
            _globalCts = new CancellationTokenSource();

            // 确保暂停事件处于释放状态
            _pauseEvent.Set();

            // 同步ProductionContext
            _productionContext.ResetEmergency();

            _logService.Information("[ExecutionController] 急停已复位");
            OnStateChanged(ExecutionState.EmergencyStopped, ExecutionState.Idle, "急停复位");
        }

        #endregion

        #region Handler执行控制

        /// <summary>
        /// 带控制的Handler执行包装
        /// 提供：暂停等待、超时控制、取消令牌
        /// </summary>
        /// <param name="handlerId">Handler ID</param>
        /// <param name="action">执行动作</param>
        /// <returns>执行结果</returns>
        public async Task<ValueTuple<bool, string, HandlerExecutionStatus>> ExecuteWithControlAsync(
            string handlerId,
            Func<CancellationToken, Task<ValueTuple<bool, string>>> action)
        {
            // 检查系统状态
            if (State == ExecutionState.EmergencyStopped)
            {
                return new ValueTuple<bool, string, HandlerExecutionStatus>(
                    false, "系统已急停", HandlerExecutionStatus.Cancelled);
            }

            if (State != ExecutionState.Running && State != ExecutionState.Paused)
            {
                return new ValueTuple<bool, string, HandlerExecutionStatus>(
                    false, "系统未运行", HandlerExecutionStatus.Cancelled);
            }

            // 等待暂停恢复
            if (!await WaitForResumeOrCancelAsync(handlerId))
            {
                return new ValueTuple<bool, string, HandlerExecutionStatus>(
                    false, "等待恢复时被取消", HandlerExecutionStatus.Cancelled);
            }

            // 创建Handler专用的取消令牌（链接全局令牌+超时）
            var timeout = _config.GetHandlerTimeout(handlerId);
            using (var handlerCts = CancellationTokenSource.CreateLinkedTokenSource(_globalCts.Token))
            {
                handlerCts.CancelAfter(timeout);

                // 注册到运行中Handler列表
                _runningHandlers[handlerId] = handlerCts;

                try
                {
                    var result = await action(handlerCts.Token);
                    return new ValueTuple<bool, string, HandlerExecutionStatus>(
                        result.Item1,
                        result.Item2,
                        result.Item1 ? HandlerExecutionStatus.Success : HandlerExecutionStatus.Failed);
                }
                catch (OperationCanceledException)
                {
                    // 判断是超时还是被取消
                    if (handlerCts.IsCancellationRequested && !_globalCts.IsCancellationRequested)
                    {
                        // 超时
                        _logService.Warning("[{0}] 执行超时 ({1}秒)", handlerId, timeout.TotalSeconds);
                        HandlerTimeout?.Invoke(this, new HandlerTimeoutEventArgs
                        {
                            HandlerId = handlerId,
                            ConfiguredTimeout = timeout
                        });
                        return new ValueTuple<bool, string, HandlerExecutionStatus>(
                            false, "执行超时", HandlerExecutionStatus.Timeout);
                    }
                    else
                    {
                        // 被取消（全局取消或急停）
                        return new ValueTuple<bool, string, HandlerExecutionStatus>(
                            false, "已取消", HandlerExecutionStatus.Cancelled);
                    }
                }
                catch (DeviceDisconnectedException ex)
                {
                    _logService.Warning("[{0}] 设备断线: {1}", handlerId, ex.DeviceName);
                    return new ValueTuple<bool, string, HandlerExecutionStatus>(
                        false, ex.Message, HandlerExecutionStatus.DeviceDisconnected);
                }
                finally
                {
                    // 从运行中列表移除
                    _runningHandlers.TryRemove(handlerId, out _);
                }
            }
        }

        /// <summary>
        /// 等待暂停恢复或被取消
        /// </summary>
        private async Task<bool> WaitForResumeOrCancelAsync(string handlerId)
        {
            while (State == ExecutionState.Paused)
            {
                // 检查全局取消
                if (_globalCts?.IsCancellationRequested == true)
                {
                    return false;
                }

                // 等待暂停事件被设置（恢复）
                if (_pauseEvent.Wait(100))
                {
                    break;
                }
            }

            return State == ExecutionState.Running;
        }

        /// <summary>
        /// 注册设备→Handler依赖关系
        /// </summary>
        public void RegisterDeviceDependency(string deviceId, string handlerId)
        {
            var handlers = _deviceHandlerMap.GetOrAdd(deviceId, _ => new HashSet<string>());
            lock (handlers)
            {
                handlers.Add(handlerId);
            }
        }

        /// <summary>
        /// 取消指定Handler
        /// </summary>
        public void CancelHandler(string handlerId)
        {
            if (_runningHandlers.TryGetValue(handlerId, out var cts))
            {
                cts.Cancel();
                _logService.Information("[{0}] Handler已取消", handlerId);
            }
        }

        /// <summary>
        /// 取消所有Handler
        /// </summary>
        public void CancelAllHandlers()
        {
            foreach (var kvp in _runningHandlers)
            {
                kvp.Value.Cancel();
            }
            _logService.Information("[ExecutionController] 已取消所有Handler");
        }

        /// <summary>
        /// 等待所有Handler完成
        /// </summary>
        private async Task<bool> WaitForAllHandlersAsync(TimeSpan timeout)
        {
            var startTime = DateTime.Now;
            while (_runningHandlers.Count > 0)
            {
                if (DateTime.Now - startTime > timeout)
                {
                    return false;
                }
                await Task.Delay(50);
            }
            return true;
        }

        /// <summary>
        /// 获取正在运行的Handler列表
        /// </summary>
        public IReadOnlyList<string> GetRunningHandlers()
        {
            return new List<string>(_runningHandlers.Keys);
        }

        #endregion

        #region 设备断线处理

        private void OnDeviceStatusChanged(object sender, DeviceConnectionStatusChangedEventArgs e)
        {
            if (e.IsConnected)
            {
                // 设备恢复连接
                OnDeviceReconnected(e.DeviceId, e.Status.DeviceName);
            }
            else
            {
                // 设备断线
                OnDeviceDisconnected(e.DeviceId, e.Status.DeviceName);
            }
        }

        private void OnDeviceDisconnected(string deviceId, string deviceName)
        {
            _logService.Warning("[ExecutionController] 设备断线: {0}", deviceName);

            if (!_config.CancelHandlerOnDeviceDisconnect)
            {
                return;
            }

            // 取消依赖该设备的Handler
            if (_deviceHandlerMap.TryGetValue(deviceId, out var handlers))
            {
                lock (handlers)
                {
                    foreach (var handlerId in handlers)
                    {
                        if (_runningHandlers.TryGetValue(handlerId, out var cts))
                        {
                            cts.Cancel();
                            _logService.Warning("[{0}] 因设备 [{1}] 断线被取消", handlerId, deviceName);
                            HandlerCancelledByDeviceDisconnect?.Invoke(this, handlerId);
                        }
                    }
                }
            }
        }

        private void OnDeviceReconnected(string deviceId, string deviceName)
        {
            _logService.Information("[ExecutionController] 设备恢复: {0}", deviceName);

            // 简单方案：设备恢复后，通知外部触发信号评估
            // 具体的重新评估由 HandlerExecutor 完成
            DeviceReconnected?.Invoke(this, deviceId);
        }

        /// <summary>
        /// 设备恢复连接事件
        /// </summary>
        public event EventHandler<string> DeviceReconnected;

        #endregion

        #region ProductionContext同步

        private void OnProductionStateChanged(object sender, ProductionStateChangedEventArgs e)
        {
            // ProductionContext状态变化时同步
            // 主要处理外部触发的急停
            if (_productionContext.IsEmergencyStopped && State != ExecutionState.EmergencyStopped)
            {
                EmergencyStop("ProductionContext急停触发");
            }
        }

        #endregion

        #region 事件触发

        private void OnStateChanged(ExecutionState oldState, ExecutionState newState, string reason)
        {
            StateChanged?.Invoke(this, new ExecutionStateChangedEventArgs
            {
                OldState = oldState,
                NewState = newState,
                Reason = reason
            });
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            // 取消订阅事件
            if (_connectionMonitor != null)
            {
                _connectionMonitor.DeviceStatusChanged -= OnDeviceStatusChanged;
            }
            _productionContext.StateChanged -= OnProductionStateChanged;

            // 取消所有任务
            _globalCts?.Cancel();
            _globalCts?.Dispose();

            // 释放暂停事件
            _pauseEvent.Dispose();

            // 清理运行中Handler
            foreach (var cts in _runningHandlers.Values)
            {
                cts.Dispose();
            }
            _runningHandlers.Clear();

            _disposed = true;
            _logService.Information("[ExecutionController] 已释放");
        }

        #endregion
    }
}