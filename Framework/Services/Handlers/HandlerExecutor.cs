using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Execution;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// 增强版处理器执行器
    /// 
    /// 功能：
    /// 1. Handler注册管理
    /// 2. 信号变化时自动评估和执行
    /// 3. 与ExecutionController集成（暂停/超时/取消控制）
    /// 4. 执行统计收集
    /// 5. 重试机制
    /// 6. 调试模式支持（绕过生产状态检查）
    /// 7. 向 Handler 提供 ISignalAccessor（支持别名的信号读写）
    /// 8. ★ 新增：条件诊断功能
    /// </summary>
    public class HandlerExecutor : IHandlerExecutor
    {
        #region 私有字段

        private readonly List<ISignalHandler> _handlers = new List<ISignalHandler>();
        private readonly object _handlersLock = new object();

        private readonly IProductionContext _productionContext;
        private readonly IDeviceManager _deviceManager;
        private readonly ITaskExecutionManager _taskManager;
        private readonly IDataFlowManager _dataFlowManager;
        private readonly ILogService _logService;
        // ★★★ 新增：服务提供者 ★★★
        private readonly IServiceProvider _serviceProvider;

        // 执行控制器（可选）
        private ExecutionController _executionController;

        // 重试配置
        private RetryConfig _retryConfig;

        // 条件上下文
        private IConditionContext _conditionContext;
        private Func<string, bool> _signalGetter;

        // 信号监控器引用（用于设备恢复后刷新）
        private ISignalMonitor _signalMonitor;

        // 信号访问器（用于传递给 Handler）
        private ISignalAccessor _signalAccessor;

        // 执行统计
        private readonly ConcurrentDictionary<string, HandlerStatistics> _statistics
            = new ConcurrentDictionary<string, HandlerStatistics>();

        private bool _disposed;

        // 调试模式标志
        private bool _isDebugMode;

        // ★ 新增：条件诊断配置
        private ConditionDiagnosticsConfig _diagnosticsConfig = ConditionDiagnosticsConfig.Default;

        #endregion

        #region 属性

        /// <summary>
        /// 调试模式（绕过 CanProcessSignals 和 ExecutionController 状态检查）
        /// </summary>
        public bool IsDebugMode
        {
            get => _isDebugMode;
            set
            {
                if (_isDebugMode != value)
                {
                    _isDebugMode = value;
                    _logService.Information("[HandlerExecutor] 调试模式: {0}", value ? "已启用" : "已禁用");
                }
            }
        }

        /// <summary>
        /// ★ 新增：条件诊断配置
        /// </summary>
        public ConditionDiagnosticsConfig DiagnosticsConfig
        {
            get => _diagnosticsConfig;
            set => _diagnosticsConfig = value ?? ConditionDiagnosticsConfig.Default;
        }

        #endregion

        #region 事件

        /// <summary>
        /// Handler执行完成事件
        /// </summary>
        public event EventHandler<HandlerExecutedEventArgs> HandlerExecuted;

        /// <summary>
        /// 错误事件
        /// </summary>
        public event EventHandler<Exception> Error;

        /// <summary>
        /// Handler执行结果事件（增强版，包含状态和重试信息）
        /// </summary>
        public event EventHandler<HandlerExecutionResultEventArgs> HandlerExecutionCompleted;


        #endregion

        #region 构造函数

        public HandlerExecutor(
            IProductionContext productionContext,
            IDeviceManager deviceManager,
            ITaskExecutionManager stationManager,
            IDataFlowManager dataFlowManager,
            ILogService logService, IServiceProvider serviceProvider = null)
        {
            _productionContext = productionContext ?? throw new ArgumentNullException(nameof(productionContext));
            _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
            _taskManager = stationManager ?? throw new ArgumentNullException(nameof(stationManager));
            _dataFlowManager = dataFlowManager ?? throw new ArgumentNullException(nameof(dataFlowManager));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _serviceProvider = serviceProvider;  // ★ 新增

            _retryConfig = new RetryConfig();

            _taskManager.TaskCompleted += OnStationCompleted;
            FlagCondition.FlagChanged += OnFlagChanged;

            _logService.Information("[HandlerExecutor] 已初始化");
        }

        #endregion

        #region 配置

        /// <summary>
        /// 设置执行控制器
        /// </summary>
        public void SetExecutionController(ExecutionController controller)
        {
            _executionController = controller;

            if (_executionController != null)
            {
                _executionController.DeviceReconnected += OnDeviceReconnected;
                _logService.Information("[HandlerExecutor] 已绑定ExecutionController");
            }
        }

        /// <summary>
        /// 设置重试配置
        /// </summary>
        public void SetRetryConfig(RetryConfig config)
        {
            _retryConfig = config ?? new RetryConfig();
        }

        /// <summary>
        /// ★ 新增：设置诊断配置
        /// </summary>
        public void SetDiagnosticsConfig(ConditionDiagnosticsConfig config)
        {
            _diagnosticsConfig = config ?? ConditionDiagnosticsConfig.Default;
            _logService.Information("[HandlerExecutor] 诊断配置已更新: Level={0}", _diagnosticsConfig.LogLevel);
        }

        /// <summary>
        /// ★ 新增：启用/禁用条件诊断
        /// </summary>
        public void EnableDiagnostics(bool enabled)
        {
            _diagnosticsConfig.Enabled = enabled;
            _logService.Information("[HandlerExecutor] 条件诊断: {0}", enabled ? "已启用" : "已禁用");
        }

        #endregion

        #region 信号源绑定

        /// <summary>
        /// 绑定信号监控器
        /// </summary>
        public void BindSignalMonitor(ISignalMonitor monitor)
        {
            _signalMonitor = monitor;
            _signalGetter = name => monitor.GetSignal(name);
            _conditionContext = new ConditionContext(_productionContext, _signalGetter, _taskManager);

            if (monitor is ISignalAccessor accessor)
            {
                _signalAccessor = accessor;
                _logService.Information("[HandlerExecutor] 已绑定信号监控器 (支持 ISignalAccessor)");
            }
            else
            {
                _signalAccessor = null;
                _logService.Information("[HandlerExecutor] 已绑定信号监控器 (不支持 ISignalAccessor)");
            }

            monitor.SignalChanged += OnSignalChanged;
        }

        /// <summary>
        /// 直接绑定信号访问器
        /// </summary>
        public void BindSignalAccessor(ISignalAccessor accessor)
        {
            _signalAccessor = accessor;
            _logService.Information("[HandlerExecutor] 已绑定 ISignalAccessor");
        }

        /// <summary>
        /// 绑定自定义信号获取函数
        /// </summary>
        public void BindSignalGetter(Func<string, bool> getter)
        {
            _signalGetter = getter;
            _conditionContext = new ConditionContext(_productionContext, _signalGetter, _taskManager);
        }

        private void OnSignalChanged(object sender, SignalChangedEventArgs e)
        {
            EvaluateAndExecute();
        }

        #endregion

        #region Handler注册管理

        /// <summary>
        /// 注册Handler
        /// </summary>
        public void Register(ISignalHandler handler)
        {
            if (handler == null) return;

            // 注入日志服务
            if (handler is SignalHandlerBase baseHandler)
            {
                baseHandler.SetLogger(_logService);
            }

            lock (_handlersLock)
            {
                _handlers.RemoveAll(h => h.HandlerId == handler.HandlerId);
                _handlers.Add(handler);
                _handlers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            }

            // 注册设备依赖
            if (_executionController != null && handler is SignalHandlerBase sh)
            {
                foreach (var deviceId in sh.DependentDevices)
                {
                    _executionController.RegisterDeviceDependency(deviceId, handler.HandlerId);
                }
            }

            // ★★★ 新增：自动提取并注册 TriggerCondition 中的 PLC 信号 ★★★
            AutoRegisterConditionSignals(handler);

            // 初始化统计
            _statistics.GetOrAdd(handler.HandlerId, _ => new HandlerStatistics
            {
                HandlerId = handler.HandlerId,
                HandlerName = handler.HandlerName
            });

            _logService.Debug("[HandlerExecutor] 已注册Handler: {0} (优先级:{1})",
                handler.HandlerId, handler.Priority);
        }

        /// <summary>
        /// ★ 新增：自动提取并注册条件中的 PLC 信号
        /// </summary>
        private void AutoRegisterConditionSignals(ISignalHandler handler)
        {
            if (handler.TriggerCondition == null)
                return;

            if (_signalMonitor == null)
            {
                _logService.Warning("[HandlerExecutor] SignalMonitor 未绑定，无法自动注册信号 (Handler: {0})",
                    handler.HandlerId);
                return;
            }

            try
            {
                // 提取所有信号名
                var signalNames = ConditionSignalExtractor.ExtractSignalNames(handler.TriggerCondition);

                if (signalNames.Count == 0)
                {
                    _logService.Debug("[HandlerExecutor] Handler {0} 的条件中无 PLC 信号", handler.HandlerId);
                    return;
                }

                // 注册到 SignalMonitor
                foreach (var signalName in signalNames)
                {
                    _signalMonitor.Register(signalName);
                }

                _logService.Information("[HandlerExecutor] Handler {0} 自动注册了 {1} 个条件信号: [{2}]",
                    handler.HandlerId,
                    signalNames.Count,
                    string.Join(", ", signalNames));
            }
            catch (Exception ex)
            {
                _logService.Warning("[HandlerExecutor] Handler {0} 自动注册信号失败: {1}",
                    handler.HandlerId, ex.Message);
            }
        }

        /// <summary>
        /// 批量注册Handler
        /// </summary>
        public void Register(params ISignalHandler[] handlers)
        {
            foreach (var h in handlers)
            {
                Register(h);
            }
        }

        /// <summary>
        /// 取消注册Handler
        /// </summary>
        public void Unregister(string handlerId)
        {
            lock (_handlersLock)
            {
                _handlers.RemoveAll(h => h.HandlerId == handlerId);
            }
            _logService.Debug("[HandlerExecutor] 已取消注册Handler: {0}", handlerId);
        }

        /// <summary>
        /// 获取所有已注册的Handler
        /// </summary>
        public IReadOnlyList<ISignalHandler> GetHandlers()
        {
            lock (_handlersLock)
            {
                return _handlers.ToList();
            }
        }

        /// <summary>
        /// 获取指定Handler
        /// </summary>
        public ISignalHandler GetHandler(string handlerId)
        {
            lock (_handlersLock)
            {
                return _handlers.FirstOrDefault(h => h.HandlerId == handlerId);
            }
        }

        /// <summary>
        /// 启用/禁用Handler
        /// </summary>
        public void SetHandlerEnabled(string handlerId, bool enabled)
        {
            var handler = GetHandler(handlerId);
            if (handler != null)
            {
                handler.IsEnabled = enabled;
                _logService.Information("[HandlerExecutor] Handler {0} 已{1}",
                    handlerId, enabled ? "启用" : "禁用");
            }
        }

        #endregion

        #region 评估和执行

        /// <summary>
        /// 评估所有Handler并执行满足条件的
        /// </summary>
        public void EvaluateAndExecute()
        {
            // 调试模式下绕过生产状态和ExecutionController检查
            if (_isDebugMode)
            {
                if (_conditionContext == null)
                {
                    _logService.Warning("[HandlerExecutor] 调试模式: _conditionContext 未初始化");
                    return;
                }
            }
            else
            {
                if (!_productionContext.CanProcessSignals || _conditionContext == null)
                {
                    return;
                }

                if (_executionController != null)
                {
                    var state = _executionController.State;
                    if (state != ExecutionState.Running)
                    {
                        return;
                    }
                }
            }

            // 获取满足条件的Handler
            List<ISignalHandler> eligible;
            lock (_handlersLock)
            {
                eligible = _handlers
                    .Where(h => h.IsEnabled)
                    .Where(h => h.TriggerCondition?.IsSatisfied(_conditionContext) ?? false)
                    .Where(h => h.CanExecute(_conditionContext))
                    .Where(h => !_taskManager.IsRunning(h.HandlerId))
                    .ToList();
            }

            // 启动满足条件的Handler
            foreach (var handler in eligible)
            {
                StartHandler(handler);
            }
        }
        /// <summary>
        /// ★ 新增：检查指定Handler是否正在运行
        /// </summary>
        public bool IsHandlerRunning(string handlerId)
        {
            return _taskManager.IsRunning(handlerId);
        }

        /// <summary>
        /// ★ 新增：获取所有正在运行的Handler ID列表
        /// </summary>
        public IReadOnlyList<string> GetRunningHandlerIds()
        {
            return _taskManager.GetRunningTasks();
        }
        /// <summary>
        /// 手动触发指定Handler（忽略触发条件）
        /// </summary>
        public void ManualTrigger(string handlerId)
        {
            var handler = GetHandler(handlerId);
            if (handler == null)
            {
                _logService.Warning("[HandlerExecutor] 未找到Handler: {0}", handlerId);
                return;
            }

            if (_taskManager.IsRunning(handlerId) )
            {
                _logService.Warning("[HandlerExecutor] Handler {0} 正在执行中", handlerId);
                return;
            }

            _logService.Information("[HandlerExecutor] 手动触发Handler: {0}", handlerId);
            StartHandler(handler);
        }

        /// <summary>
        /// 启动Handler执行
        /// </summary>
        private void StartHandler(ISignalHandler handler)
        {
            // ★ 设置条件上下文（用于诊断输出）
            if (handler is SignalHandlerBase baseHandler)
            {
                baseHandler.SetConditionContext(_conditionContext);
            }

            var handlerContext = new HandlerContext(
                _productionContext,
                _deviceManager,
                _dataFlowManager,
                _signalAccessor,
                _serviceProvider);

            var started = _taskManager.TryStart(
                handler.HandlerId,
                async ct => await ExecuteHandlerWithRetryAsync(handler, handlerContext, ct),
                handler.HandlerName);

            if (started)
            {
                _logService.Debug("[HandlerExecutor] 已启动Handler: {0}", handler.HandlerId);
            }
        }

        /// <summary>
        /// 带重试的Handler执行
        /// </summary>
        private async Task<ValueTuple<bool, string>> ExecuteHandlerWithRetryAsync(
            ISignalHandler handler,
            IHandlerContext handlerContext,
            CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var maxRetries = _retryConfig.GetMaxRetries(handler.HandlerId);
            var retryCount = 0;
            HandlerExecutionStatus status = HandlerExecutionStatus.Failed;
            bool success = false;
            string message = "";

            while (retryCount <= maxRetries && !ct.IsCancellationRequested)
            {
                try
                {
                    ValueTuple<bool, string> result;

                    if (_executionController != null)
                    {
                        var controlledResult = await _executionController.ExecuteWithControlAsync(
                            handler.HandlerId,
                            async innerCt => await handler.HandleAsync(handlerContext, innerCt));

                        success = controlledResult.Item1;
                        message = controlledResult.Item2;
                        status = controlledResult.Item3;
                    }
                    else
                    {
                        result = await handler.HandleAsync(handlerContext, ct);
                        success = result.Item1;
                        message = result.Item2;
                        status = success ? HandlerExecutionStatus.Success : HandlerExecutionStatus.Failed;
                    }

                    if (success || status == HandlerExecutionStatus.Cancelled)
                    {
                        break;
                    }

                    if (retryCount < maxRetries && ShouldRetry(status))
                    {
                        retryCount++;
                        var delay = _retryConfig.CalculateRetryDelay(retryCount);
                        _logService.Warning("[{0}] 执行失败，{1}ms后重试 (第{2}次)",
                            handler.HandlerId, delay, retryCount);

                        if (_statistics.TryGetValue(handler.HandlerId, out var stats))
                        {
                            stats.RetryCount++;
                        }

                        await Task.Delay(delay, ct);
                        continue;
                    }

                    break;
                }
                catch (OperationCanceledException)
                {
                    status = HandlerExecutionStatus.Cancelled;
                    message = "已取消";
                    break;
                }
                catch (Exception ex)
                {
                    _logService.Error(ex, "[{0}] 执行异常", handler.HandlerId);
                    Error?.Invoke(this, ex);
                    status = HandlerExecutionStatus.Failed;
                    message = ex.Message;

                    if (retryCount < maxRetries)
                    {
                        retryCount++;
                        var delay = _retryConfig.CalculateRetryDelay(retryCount);
                        _logService.Warning("[{0}] 异常后{1}ms重试 (第{2}次)",
                            handler.HandlerId, delay, retryCount);

                        await Task.Delay(delay, ct);
                        continue;
                    }
                    break;
                }
            }

            sw.Stop();

            RecordStatistics(handler.HandlerId, status, sw.Elapsed);

            HandlerExecuted?.Invoke(this, new HandlerExecutedEventArgs(
                handler.HandlerId, handler.HandlerName, success, message, sw.Elapsed));

            HandlerExecutionCompleted?.Invoke(this, new HandlerExecutionResultEventArgs
            {
                HandlerId = handler.HandlerId,
                HandlerName = handler.HandlerName,
                Status = status,
                Success = success,
                Message = message,
                Duration = sw.Elapsed,
                RetryCount = retryCount
            });

            return new ValueTuple<bool, string>(success, message);
        }

        /// <summary>
        /// 判断是否应该重试
        /// </summary>
        private bool ShouldRetry(HandlerExecutionStatus status)
        {
            switch (status)
            {
                case HandlerExecutionStatus.Failed:
                case HandlerExecutionStatus.Timeout:
                    return true;
                case HandlerExecutionStatus.DeviceDisconnected:
                case HandlerExecutionStatus.Cancelled:
                case HandlerExecutionStatus.Success:
                default:
                    return false;
            }
        }

        #endregion

        #region ★ 条件诊断

        /// <summary>
        /// ★ 新增：获取指定 Handler 的条件诊断字符串
        /// </summary>
        public string GetConditionDiagnostics(string handlerId)
        {
            var handler = GetHandler(handlerId);
            if (handler == null)
                return string.Format("未找到 Handler: {0}", handlerId);

            if (handler.TriggerCondition == null)
                return string.Format("[{0}] 无触发条件", handlerId);

            if (_conditionContext == null)
                return string.Format("[{0}] 条件上下文未初始化", handlerId);

            try
            {
                var result = handler.TriggerCondition.Evaluate(_conditionContext);
                return ConditionDiagnosticsFormatter.Format(
                    handlerId,
                    handler.HandlerName,
                    result,
                    _diagnosticsConfig);
            }
            catch (Exception ex)
            {
                return string.Format("[{0}] 诊断失败: {1}", handlerId, ex.Message);
            }
        }

        /// <summary>
        /// ★ 新增：获取指定 Handler 的条件评估结果
        /// </summary>
        public ConditionEvaluationResult GetConditionEvaluation(string handlerId)
        {
            var handler = GetHandler(handlerId);
            if (handler?.TriggerCondition == null || _conditionContext == null)
                return null;

            return handler.TriggerCondition.Evaluate(_conditionContext);
        }

        /// <summary>
        /// ★ 新增：获取所有 Handler 的条件评估结果
        /// </summary>
        public IReadOnlyList<HandlerEvaluationInfo> GetAllConditionEvaluations()
        {
            var results = new List<HandlerEvaluationInfo>();

            if (_conditionContext == null)
                return results;

            lock (_handlersLock)
            {
                foreach (var handler in _handlers)
                {
                    if (handler.TriggerCondition == null) continue;

                    try
                    {
                        var evalResult = handler.TriggerCondition.Evaluate(_conditionContext);
                        results.Add(new HandlerEvaluationInfo
                        {
                            HandlerId = handler.HandlerId,
                            HandlerName = handler.HandlerName,
                            Result = evalResult
                        });
                    }
                    catch
                    {
                        // 忽略单个 Handler 的评估错误
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// ★ 新增：获取所有 Handler 的条件诊断字符串
        /// </summary>
        public string GetAllConditionDiagnostics()
        {
            var evaluations = GetAllConditionEvaluations();
            if (evaluations.Count == 0)
                return "无可用的条件诊断信息";

            return ConditionDiagnosticsFormatter.FormatAll(evaluations, _diagnosticsConfig);
        }

        #endregion

        #region 统计

        private void RecordStatistics(string handlerId, HandlerExecutionStatus status, TimeSpan duration)
        {
            if (_statistics.TryGetValue(handlerId, out var stats))
            {
                stats.RecordExecution(status, duration);
            }
        }

        public HandlerStatistics GetStatistics(string handlerId)
        {
            _statistics.TryGetValue(handlerId, out var stats);
            return stats;
        }

        public IReadOnlyDictionary<string, HandlerStatistics> GetAllStatistics()
        {
            return new Dictionary<string, HandlerStatistics>(_statistics);
        }

        public void ResetStatistics()
        {
            foreach (var stats in _statistics.Values)
            {
                stats.Reset();
            }
            _logService.Information("[HandlerExecutor] 统计已重置");
        }

        public string GetStatisticsSummary()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("========== Handler执行统计 ==========");

            foreach (var stats in _statistics.Values.OrderByDescending(s => s.TotalExecutions))
            {
                sb.AppendLine(stats.ToString());
            }

            sb.AppendLine("=====================================");
            return sb.ToString();
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// ★★★ 新增：Flag 变化时重新评估 Handler ★★★
        /// </summary>
        private void OnFlagChanged(string flagName, bool value)
        {
            // 忽略以下划线开头的内部标志（如 _TriggerEvaluation）
            if (flagName.StartsWith("_"))
                return;

            _logService.Debug("[HandlerExecutor] Flag变化: {0} = {1}", flagName, value);

            if (_isDebugMode || _productionContext.CanProcessSignals)
            {
                

                EvaluateAndExecute();
            }
        }

        private void OnStationCompleted(object sender, TaskCompletedEventArgs e)
        {
            if (_isDebugMode || _productionContext.CanProcessSignals)
            {
                
                EvaluateAndExecute();
                
            }
        }

        private void OnDeviceReconnected(object sender, string deviceId)
        {
            _logService.Information("[HandlerExecutor] 设备 {0} 恢复，刷新信号并重新评估", deviceId);
            _signalMonitor?.RefreshAsync();
            Task.Delay(100).ContinueWith(_ => EvaluateAndExecute());
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _taskManager.TaskCompleted -= OnStationCompleted;

            FlagCondition.FlagChanged -= OnFlagChanged;

            if (_executionController != null)
            {
                _executionController.DeviceReconnected -= OnDeviceReconnected;
            }

            if (_signalMonitor != null)
            {
                _signalMonitor.SignalChanged -= OnSignalChanged;
            }

            lock (_handlersLock)
            {
                _handlers.Clear();
            }

            _statistics.Clear();

            _logService.Information("[HandlerExecutor] 已释放");
        }

        #endregion
    }
}