using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Execution;
using SeedCut.Framework.Services.Handlers;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Sequences
{
    /// <summary>
    /// 序列执行器（条件制造 + 观察等待模式）
    /// 
    /// 【核心变更】
    /// 不再使用 ManualTrigger 强制触发 Handler
    /// 而是：
    /// 1. 根据序列定义设置触发条件（Flag）
    /// 2. 通知 HandlerExecutor 评估
    /// 3. 观察并等待期望的 Handler 完成
    /// 
    /// 这样 Handler 仍然按其 TriggerCondition 自然触发
    /// 保持了条件驱动的设计本质
    /// 
    /// 【v1.1 变更】
    /// ★ 支持失败策略：Handler失败时可立即停止序列
    /// 
    /// 使用示例：
    /// <code>
    /// var executor = new SequenceExecutor(handlerExecutor, logService);
    /// executor.Register(PredefinedSequences.SystemInit);
    /// 
    /// var result = await executor.ExecuteAsync("SystemInit", ct);
    /// </code>
    /// </summary>
    public class SequenceExecutor : IDisposable
    {
        #region 私有字段

        private readonly HandlerExecutor _handlerExecutor;
        private readonly ILogService _logService;

        private readonly ConcurrentDictionary<string, HandlerSequence> _sequences
            = new ConcurrentDictionary<string, HandlerSequence>();

        // 正在等待完成的Handler
        private readonly ConcurrentDictionary<string, TaskCompletionSource<HandlerResult>> _pendingHandlers
            = new ConcurrentDictionary<string, TaskCompletionSource<HandlerResult>>();

        // 执行进度
        private SequenceProgress _currentProgress;

        private bool _isExecuting;
        private string _currentSequenceId;
        private CancellationTokenSource _currentCts;
        private bool _disposed;

        // ★ 新增：标记是否因失败而停止
        private bool _stoppedDueToFailure;
        private string _failureHandlerId;
        private string _failureMessage;
        private bool _endHandlersTriggered;

        // ★★★ 新增：记录被临时禁用的 Handler（用于 MaxExecutions 控制）
        private readonly HashSet<string> _temporarilyDisabledHandlers = new HashSet<string>();

        #endregion

        #region 属性

        /// <summary>
        /// 是否正在执行序列
        /// </summary>
        public bool IsExecuting => _isExecuting;

        /// <summary>
        /// 当前执行的序列ID
        /// </summary>
        public string CurrentSequenceId => _currentSequenceId;

        /// <summary>
        /// 当前执行进度
        /// </summary>
        public SequenceProgress CurrentProgress => _currentProgress;

        /// <summary>
        /// 获取所有已注册的序列
        /// </summary>
        public IReadOnlyList<HandlerSequence> Sequences => _sequences.Values.ToList();

        #endregion

        #region 事件

        /// <summary>
        /// 序列开始执行事件
        /// </summary>
        public event EventHandler<SequenceStartedEventArgs> SequenceStarted;

        /// <summary>
        /// Handler状态变化事件
        /// </summary>
        public event EventHandler<HandlerStateChangedEventArgs> HandlerStateChanged;

        /// <summary>
        /// Handler执行完成事件（单个Handler）
        /// </summary>
        public event EventHandler<HandlerCompletedEventArgs> HandlerCompleted;

        /// <summary>
        /// 序列执行完成事件
        /// </summary>
        public event EventHandler<SequenceCompletedEventArgs> SequenceCompleted;

        /// <summary>
        /// 进度更新事件
        /// </summary>
        public event EventHandler<SequenceProgressEventArgs> ProgressUpdated;

        /// <summary>
        /// ★ 新增：序列因失败停止事件
        /// </summary>
        public event EventHandler<SequenceStoppedEventArgs> SequenceStopped;

        #endregion

        #region 构造函数

        public SequenceExecutor(HandlerExecutor handlerExecutor, ILogService logService)
        {
            _handlerExecutor = handlerExecutor ?? throw new ArgumentNullException(nameof(handlerExecutor));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));

            // 订阅Handler执行完成事件（用于观察等待）
            _handlerExecutor.HandlerExecutionCompleted += OnHandlerExecutionCompleted;

            _logService.Information("[SequenceExecutor] 已初始化（条件制造模式，支持失败停止）");
        }

        #endregion

        #region 序列注册

        /// <summary>
        /// 注册序列
        /// </summary>
        public void Register(HandlerSequence sequence)
        {
            if (sequence == null)
                throw new ArgumentNullException(nameof(sequence));

            if (string.IsNullOrEmpty(sequence.SequenceId))
                throw new ArgumentException("序列ID不能为空");

            _sequences[sequence.SequenceId] = sequence;
            _logService.Debug("[SequenceExecutor] 已注册序列: {0} ({1}), 失败策略: {2}",
                sequence.SequenceId, sequence.SequenceName, sequence.FailurePolicy);
        }

        /// <summary>
        /// 批量注册序列
        /// </summary>
        public void Register(params HandlerSequence[] sequences)
        {
            foreach (var seq in sequences)
            {
                Register(seq);
            }
        }

        /// <summary>
        /// 获取序列
        /// </summary>
        public HandlerSequence GetSequence(string sequenceId)
        {
            _sequences.TryGetValue(sequenceId, out var sequence);
            return sequence;
        }

        /// <summary>
        /// 检查序列是否存在
        /// </summary>
        public bool HasSequence(string sequenceId)
        {
            return _sequences.ContainsKey(sequenceId);
        }

        #endregion

        #region 执行

        /// <summary>
        /// 执行序列（通过ID）
        /// </summary>
        public async Task<SequenceExecutionResult> ExecuteAsync(string sequenceId, CancellationToken ct = default)
        {
            if (!_sequences.TryGetValue(sequenceId, out var sequence))
            {
                return new SequenceExecutionResult
                {
                    SequenceId = sequenceId,
                    Success = false,
                    Message = "序列未找到: " + sequenceId
                };
            }

            return await ExecuteAsync(sequence, ct);
        }

        /// <summary>
        /// 执行序列
        /// </summary>
        public async Task<SequenceExecutionResult> ExecuteAsync(HandlerSequence sequence, CancellationToken ct = default)
        {
            if (sequence == null)
                throw new ArgumentNullException(nameof(sequence));

            if (_isExecuting)
            {
                return new SequenceExecutionResult
                {
                    SequenceId = sequence.SequenceId,
                    SequenceName = sequence.SequenceName,
                    Success = false,
                    Message = "另一个序列正在执行中"
                };
            }

            var result = new SequenceExecutionResult
            {
                SequenceId = sequence.SequenceId,
                SequenceName = sequence.SequenceName,
                StartTime = DateTime.Now
            };

            var stopwatch = Stopwatch.StartNew();
            var context = new SequenceContext(sequence.SequenceId, sequence.SequenceName);

            _isExecuting = true;
            _currentSequenceId = sequence.SequenceId;
            _currentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // ★ 重置失败标记
            _stoppedDueToFailure = false;
            _failureHandlerId = null;
            _failureMessage = null;
            _endHandlersTriggered = false;  // 重置ExecuteAtEnd标记

            // ★★★ 新增：清空临时禁用列表（确保上次序列的状态不会影响本次）
            _temporarilyDisabledHandlers.Clear();

            // 初始化进度跟踪
            _currentProgress = InitializeProgress(sequence);

            try
            {
                _logService.Information("[SequenceExecutor] ▶ 开始执行序列: {0} ({1}), 失败策略: {2}",
                    sequence.SequenceId, sequence.SequenceName, sequence.FailurePolicy);

                // 触发开始事件
                var triggerConditionIds = sequence.GetAllTriggerConditionIds();
                SequenceStarted?.Invoke(this, new SequenceStartedEventArgs
                {
                    SequenceId = sequence.SequenceId,
                    SequenceName = sequence.SequenceName,
                    ExpectedHandlers = sequence.ExpectedHandlers.ToList(),
                    TriggerConditionIds = triggerConditionIds.ToList()
                });

                // 1. 执行前回调
                sequence.OnBeforeExecute?.Invoke(context);

                // 2. 检查跳过条件，设置初始状态
                foreach (var expected in sequence.ExpectedHandlers)
                {
                    if (expected.SkipWhen?.Invoke(context) == true)
                    {
                        // 标记为跳过
                        UpdateHandlerState(expected.HandlerId, HandlerExecutionState.Skipped, "跳过条件满足");
                        continue;
                    }

                    // 为每个期望的Handler创建等待
                    var tcs = new TaskCompletionSource<HandlerResult>();
                    _pendingHandlers[expected.HandlerId] = tcs;
                }

                // 3. 设置触发条件（条件制造）
                ApplyTriggerConditions(sequence, context);

                // 4. 通知HandlerExecutor评估（Handler会自然触发）
                _logService.Debug("[SequenceExecutor] 通知HandlerExecutor评估");
                _handlerExecutor.EvaluateAndExecute();

                // 5. 等待期望的Handler完成（★ 支持失败停止）
                await WaitForCompletionAsync(sequence, _currentCts.Token);

                // 6. 收集结果
                CollectResults(sequence, result, context);

                // 7. 判断整体结果（★ 增加失败检测）
                DetermineOverallResult(sequence, result);

                // 8. 执行后回调
                sequence.OnAfterExecute?.Invoke(context, result);

                if (result.Success)
                {
                    _logService.Information("[SequenceExecutor] ✓ 序列执行完成: {0}, 结果: {1}",
                        sequence.SequenceId, result.Message);
                }
                else
                {
                    _logService.Warning("[SequenceExecutor] ✗ 序列执行失败: {0}, 原因: {1}",
                        sequence.SequenceId, result.Message);
                }
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Message = "序列执行被取消";
                _logService.Warning("[SequenceExecutor] ⏹ 序列执行被取消: {0}", sequence.SequenceId);

                // ★ 新增：取消时也执行清理 Handler（停止TCP服务器等）
                if (!_endHandlersTriggered && HasExecuteAtEndHandlers(sequence))
                {
                    _logService.Information("[SequenceExecutor] 序列被取消，开始执行清理Handler...");
                    try
                    {
                        // 使用独立的CancellationToken，确保清理能完成
                        using (var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                        {
                            TriggerEndHandlers(sequence);
                            _endHandlersTriggered = true;
                            await WaitForEndHandlersAsync(sequence, cleanupCts.Token);
                        }
                        _logService.Information("[SequenceExecutor] 清理Handler执行完成");
                    }
                    catch (Exception cleanupEx)
                    {
                        _logService.Warning("[SequenceExecutor] 清理Handler执行异常: {0}", cleanupEx.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "序列执行异常: " + ex.Message;
                _logService.Error(ex, "[SequenceExecutor] ✗ 序列执行异常: {0}", sequence.SequenceId);
            }
            finally
            {
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
                result.EndTime = DateTime.Now;

                // 清理触发条件
                ClearTriggerConditions(sequence);

                // ★★★ 新增：恢复被临时禁用的 Handler
                RestoreDisabledHandlers();

                _isExecuting = false;
                _currentSequenceId = null;
                _currentProgress = null;
                _currentCts?.Dispose();
                _currentCts = null;
                _pendingHandlers.Clear();

                // 触发完成事件
                SequenceCompleted?.Invoke(this, new SequenceCompletedEventArgs
                {
                    Result = result
                });
            }

            return result;
        }

        /// <summary>
        /// 初始化进度跟踪
        /// </summary>
        private SequenceProgress InitializeProgress(HandlerSequence sequence)
        {
            var progress = new SequenceProgress
            {
                SequenceId = sequence.SequenceId,
                StartTime = DateTime.Now
            };

            foreach (var expected in sequence.ExpectedHandlers)
            {
                var handler = _handlerExecutor.GetHandler(expected.HandlerId);
                progress.HandlerProgress[expected.HandlerId] = new HandlerProgressInfo
                {
                    HandlerId = expected.HandlerId,
                    HandlerName = handler?.HandlerName ?? expected.HandlerId,
                    IsRequired = expected.IsRequired,
                    State = HandlerExecutionState.Pending,
                    IsExecuteAtEnd = expected.ExecuteAtEnd  // ★ 新增
                };
            }

            return progress;
        }

        /// <summary>
        /// 应用触发条件（条件制造）
        /// ★ 支持单次执行模式
        /// </summary>
        private void ApplyTriggerConditions(HandlerSequence sequence, ISequenceContext context)
        {
            _logService.Debug("[SequenceExecutor] 开始设置触发条件...");

            // 单次执行模式下重置所有执行计数
            if (sequence.ExecutionMode == SequenceExecutionMode.SingleRun)
            {
                sequence.ResetAllExecutionCounts();
                _logService.Debug("[SequenceExecutor] 单次执行模式，已重置执行计数");
            }

            foreach (var expected in sequence.ExpectedHandlers)
            {

                // 跳过 ExecuteAtEnd=true 的Handler，这些会在其他Handler完成后触发
                if (expected.ExecuteAtEnd)
                {
                    _logService.Debug("[SequenceExecutor] Handler {0} 标记为ExecuteAtEnd，稍后触发",
                        expected.HandlerId);
                    continue;
                }

                // 跳过已标记为跳过的Handler
                if (_currentProgress.HandlerProgress.TryGetValue(expected.HandlerId, out var info)
                    && info.State == HandlerExecutionState.Skipped)
                {
                    continue;
                }

                // 单次执行模式下检查是否已达到最大次数
                if (sequence.ExecutionMode == SequenceExecutionMode.SingleRun
                    && expected.IsMaxExecutionsReached)
                {
                    _logService.Debug("[SequenceExecutor] Handler {0} 已达最大执行次数({1})，跳过",
                        expected.HandlerId, expected.MaxExecutions);
                    UpdateHandlerState(expected.HandlerId, HandlerExecutionState.Skipped, "已达最大执行次数");
                    continue;
                }

                // 更新进度信息中的最大执行次数
                if (_currentProgress.HandlerProgress.TryGetValue(expected.HandlerId, out var progressInfo))
                {
                    progressInfo.MaxExecutions = expected.MaxExecutions;
                    progressInfo.ExecutionCount = expected.CurrentExecutions;
                }

                // 设置此Handler的触发条件
                foreach (var conditionId in expected.TriggerConditionIds)
                {
                    if (ConditionCatalog.Apply(conditionId))
                    {
                        _logService.Debug("[SequenceExecutor] 已设置条件: {0} (为 {1})",
                            conditionId, expected.HandlerId);
                    }
                    else
                    {
                        _logService.Warning("[SequenceExecutor] 条件未找到: {0}", conditionId);
                    }
                }

                // 更新状态为Ready
                UpdateHandlerState(expected.HandlerId, HandlerExecutionState.Ready, "条件已设置");
            }
        }

        /// <summary>
        /// 清理触发条件
        /// </summary>
        private void ClearTriggerConditions(HandlerSequence sequence)
        {
            _logService.Debug("[SequenceExecutor] 清理触发条件...");

            foreach (var expected in sequence.ExpectedHandlers)
            {
                foreach (var conditionId in expected.TriggerConditionIds)
                {
                    ConditionCatalog.Clear(conditionId);
                }
            }
        }

        /// <summary>
        /// 等待完成
        /// ★ 支持失败策略：Handler失败时可立即停止
        /// </summary>
        private async Task WaitForCompletionAsync(HandlerSequence sequence, CancellationToken ct)
        {
            var startTime = DateTime.Now;
            var evaluationInterval = TimeSpan.FromMilliseconds(sequence.EvaluationIntervalMs);

            while (!ct.IsCancellationRequested)
            {
                // ★ 优先检查失败策略
                if (sequence.FailurePolicy != SequenceFailurePolicy.Continue)
                {
                    if (_currentProgress.HasRequiredFailure)
                    {
                        var failedHandler = _currentProgress.GetFirstFailedRequired();

                        _logService.Warning("[SequenceExecutor] ⚠ 检测到必须的Handler失败: {0} ({1}), 执行失败策略: {2}",
                            failedHandler?.HandlerId,
                            failedHandler?.Message,
                            sequence.FailurePolicy);

                        // 记录失败信息
                        _stoppedDueToFailure = true;
                        _failureHandlerId = failedHandler?.HandlerId;
                        _failureMessage = failedHandler?.Message;

                        // 根据策略处理
                        if (sequence.FailurePolicy == SequenceFailurePolicy.StopAndCleanup)
                        {
                            // 清除所有触发条件，防止Handler被重新触发
                            ClearTriggerConditions(sequence);
                            _logService.Debug("[SequenceExecutor] 已清除所有触发条件（StopAndCleanup策略）");
                        }

                        // ★★★ 新增：在停止前先触发ExecuteAtEnd Handler（清理操作）
                        if (!_endHandlersTriggered && HasExecuteAtEndHandlers(sequence))
                        {
                            _logService.Warning("[SequenceExecutor] ⚠ 序列失败，触发清理Handler以确保资源释放...");
                            TriggerEndHandlers(sequence);
                            _endHandlersTriggered = true;

                            // 等待清理Handler完成（带超时保护）
                            await WaitForEndHandlersAsync(sequence, ct);
                            _logService.Information("[SequenceExecutor] 清理Handler执行完成");
                        }

                        // 标记其他未完成的Handler为取消状态（但不包括ExecuteAtEnd Handler）
                        CancelPendingHandlers(string.Format("序列因Handler [{0}] 失败而停止", failedHandler?.HandlerId));

                        // ★ 触发停止事件
                        SequenceStopped?.Invoke(this, new SequenceStoppedEventArgs
                        {
                            SequenceId = sequence.SequenceId,
                            FailedHandlerId = failedHandler?.HandlerId,
                            FailedHandlerName = failedHandler?.HandlerName,
                            FailureMessage = failedHandler?.Message,
                            Policy = sequence.FailurePolicy
                        });

                        break; // 立即退出等待循环
                    }
                }

                // 检查是否超时
                if (DateTime.Now - startTime > sequence.Timeout)
                {
                    _logService.Warning("[SequenceExecutor] 序列执行超时");

                    // 标记未完成的Handler为超时
                    foreach (var kvp in _currentProgress.HandlerProgress)
                    {
                        if (kvp.Value.State == HandlerExecutionState.Pending ||
                            kvp.Value.State == HandlerExecutionState.Ready ||
                            kvp.Value.State == HandlerExecutionState.Running)
                        {
                            UpdateHandlerState(kvp.Key, HandlerExecutionState.Timeout, "执行超时");

                            // 完成等待
                            if (_pendingHandlers.TryRemove(kvp.Key, out var tcs))
                            {
                                tcs.TrySetResult(new HandlerResult
                                {
                                    HandlerId = kvp.Key,
                                    Success = false,
                                    Message = "执行超时",
                                    State = HandlerExecutionState.Timeout
                                });
                            }
                        }
                    }
                    break;
                }

                // ★ 新增：检查是否需要触发 ExecuteAtEnd Handler
                if (!_endHandlersTriggered && AreNonEndHandlersCompleted(sequence))
                {
                    TriggerEndHandlers(sequence);
                    _endHandlersTriggered = true;
                }

                // 检查完成条件
                if (IsSequenceCompleted(sequence))
                {
                    _logService.Debug("[SequenceExecutor] 序列完成条件满足");
                    break;
                }

                // 触发进度更新事件
                ProgressUpdated?.Invoke(this, new SequenceProgressEventArgs
                {
                    Progress = _currentProgress
                });

                // ★ 只有在没有失败的情况下才继续触发评估
                // 这样可以防止失败的Handler被重新触发
                if (!_currentProgress.HasRequiredFailure)
                {
                    // 周期性触发评估（确保Handler能被执行）
                    _handlerExecutor.EvaluateAndExecute();
                }

                await Task.Delay(evaluationInterval, ct);
            }
        }

        /// <summary>
        /// ★ 新增：取消所有待执行的Handler
        /// </summary>
        private void CancelPendingHandlers(string reason)
        {
            _logService.Debug("[SequenceExecutor] 取消待执行的Handler: {0}", reason);

            var sequence = GetSequence(_currentSequenceId);

            foreach (var kvp in _currentProgress.HandlerProgress)
            {
                // ★ 新增：跳过ExecuteAtEnd Handler（这些需要执行清理操作）
                if (kvp.Value.IsExecuteAtEnd)
                {
                    _logService.Debug("[SequenceExecutor] 保留清理Handler: {0}（ExecuteAtEnd）", kvp.Key);
                    continue;
                }

                // 只取消尚未开始或正在等待的Handler
                if (kvp.Value.State == HandlerExecutionState.Pending ||
                    kvp.Value.State == HandlerExecutionState.Ready)
                {
                    UpdateHandlerState(kvp.Key, HandlerExecutionState.Cancelled, reason);

                    if (_pendingHandlers.TryRemove(kvp.Key, out var tcs))
                    {
                        tcs.TrySetResult(new HandlerResult
                        {
                            HandlerId = kvp.Key,
                            Success = false,
                            Message = reason,
                            State = HandlerExecutionState.Cancelled
                        });
                    }

                    _logService.Debug("[SequenceExecutor] Handler {0} 已取消", kvp.Key);
                }
            }
        }

        /// <summary>
        /// 检查序列是否完成
        /// </summary>
        private bool IsSequenceCompleted(HandlerSequence sequence)
        {
            // 如果有自定义完成条件，使用自定义条件
            if (sequence.CompletionCondition != null)
            {
                return sequence.CompletionCondition(_currentProgress);
            }

            // ★★★ 修改：检查所有 Handler 是否都达到了执行次数要求
            foreach (var expected in sequence.ExpectedHandlers)
            {
                // 跳过 ExecuteAtEnd 的 Handler（这些在最后触发）
                if (expected.ExecuteAtEnd)
                    continue;

                // 跳过非必须的 Handler
                if (!expected.IsRequired)
                    continue;

                // 如果设置了 MaxExecutions，检查是否达到
                if (expected.MaxExecutions > 0)
                {
                    if (expected.CurrentExecutions < expected.MaxExecutions)
                    {
                        // 还没执行够次数，序列未完成
                        return false;
                    }
                }
                else
                {
                    // MaxExecutions <= 0 表示无限制，只需检查是否执行过一次
                    if (_currentProgress.HandlerProgress.TryGetValue(expected.HandlerId, out var info))
                    {
                        if (info.State != HandlerExecutionState.Completed &&
                            info.State != HandlerExecutionState.Skipped)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 收集结果
        /// </summary>
        private void CollectResults(HandlerSequence sequence, SequenceExecutionResult result, SequenceContext context)
        {
            foreach (var expected in sequence.ExpectedHandlers)
            {
                var handlerId = expected.HandlerId;

                if (_currentProgress.HandlerProgress.TryGetValue(handlerId, out var progressInfo))
                {
                    var handlerResult = new HandlerResult
                    {
                        HandlerId = handlerId,
                        HandlerName = progressInfo.HandlerName,
                        State = progressInfo.State,
                        Message = progressInfo.Message,
                        Duration = progressInfo.Duration ?? TimeSpan.Zero,
                        Success = progressInfo.State == HandlerExecutionState.Completed
                    };

                    result.HandlerResults[handlerId] = handlerResult;
                    context.SetHandlerResult(handlerId, handlerResult);
                }
            }
        }

        /// <summary>
        /// 判断整体结果
        /// ★ 增加失败检测
        /// </summary>
        private void DetermineOverallResult(HandlerSequence sequence, SequenceExecutionResult result)
        {
            // ★ 首先检查是否因失败而停止
            if (_stoppedDueToFailure)
            {
                result.Success = false;
                result.FailedHandlerId = _failureHandlerId;
                result.FailureReason = _failureMessage;

                var failedInfo = _currentProgress.GetFirstFailedRequired();
                result.Message = string.Format("Handler失败: [{0}] {1} - {2}",
                    failedInfo?.HandlerId ?? _failureHandlerId,
                    failedInfo?.HandlerName ?? "未知",
                    failedInfo?.Message ?? _failureMessage ?? "未知错误");
                return;
            }

            // ★ 检查是否有必须的Handler失败
            if (_currentProgress.HasRequiredFailure)
            {
                result.Success = false;
                var failedHandler = _currentProgress.GetFirstFailedRequired();
                result.FailedHandlerId = failedHandler?.HandlerId;
                result.FailureReason = failedHandler?.Message;
                result.Message = string.Format("Handler失败: [{0}] {1} - {2}",
                    failedHandler?.HandlerId,
                    failedHandler?.HandlerName,
                    failedHandler?.Message ?? "未知错误");
                return;
            }

            if (sequence.AllowPartialSuccess)
            {
                // 部分成功模式：只要有一个成功就算成功
                result.Success = result.SuccessCount > 0;
            }
            else
            {
                // 全部成功模式：所有必须的Handler都成功
                result.Success = _currentProgress.AllRequiredSuccess;
            }

            // 生成消息
            var parts = new List<string>();
            parts.Add(string.Format("执行 {0}/{1}", result.ExecutedCount, result.HandlerResults.Count));

            if (result.SuccessCount > 0)
                parts.Add(string.Format("成功 {0}", result.SuccessCount));

            if (result.FailedCount > 0)
                parts.Add(string.Format("失败 {0}", result.FailedCount));

            if (result.SkippedCount > 0)
                parts.Add(string.Format("跳过 {0}", result.SkippedCount));

            if (result.CancelledCount > 0)
                parts.Add(string.Format("取消 {0}", result.CancelledCount));

            result.Message = string.Join(", ", parts);
        }

        /// <summary>
        /// 取消当前执行的序列
        /// </summary>
        public void Cancel()
        {
            if (_isExecuting && _currentCts != null)
            {
                _logService.Information("[SequenceExecutor] 请求取消序列: {0}", _currentSequenceId);
                _currentCts.Cancel();
            }
        }

        #endregion

        #region 手动条件设置（供UI调用）

        /// <summary>
        /// ★ 新增：获取序列的推荐默认条件
        /// </summary>
        public IReadOnlyList<ConditionItem> GetRecommendedConditions(string sequenceId)
        {
            if (!_sequences.TryGetValue(sequenceId, out var sequence))
            {
                return new List<ConditionItem>();
            }

            return sequence.GetDefaultConditions();
        }

        /// <summary>
        /// ★ 新增：获取序列的默认条件ID列表
        /// </summary>
        public IReadOnlyList<string> GetDefaultConditionIds(string sequenceId)
        {
            if (!_sequences.TryGetValue(sequenceId, out var sequence))
            {
                return new List<string>();
            }

            return sequence.DefaultConditionIds ?? new List<string>();
        }

        /// <summary>
        /// ★ 新增：应用序列的默认条件（用户确认后调用）
        /// </summary>
        public void ApplyDefaultConditions(string sequenceId)
        {
            if (!_sequences.TryGetValue(sequenceId, out var sequence))
            {
                _logService.Warning("[SequenceExecutor] 序列未找到: {0}", sequenceId);
                return;
            }

            if (sequence.DefaultConditionIds == null || sequence.DefaultConditionIds.Count == 0)
            {
                _logService.Debug("[SequenceExecutor] 序列 {0} 没有默认条件", sequenceId);
                return;
            }

            _logService.Information("[SequenceExecutor] 应用默认条件: {0} ({1}个条件)",
                sequenceId, sequence.DefaultConditionIds.Count);

            foreach (var conditionId in sequence.DefaultConditionIds)
            {
                if (ConditionCatalog.Apply(conditionId))
                {
                    _logService.Debug("[SequenceExecutor] 已设置默认条件: {0}", conditionId);
                }
                else
                {
                    _logService.Warning("[SequenceExecutor] 默认条件未找到: {0}", conditionId);
                }
            }

            // 触发评估
            _handlerExecutor.EvaluateAndExecute();
        }

        /// <summary>
        /// ★ 新增：检查序列是否有默认条件
        /// </summary>
        public bool HasDefaultConditions(string sequenceId)
        {
            if (!_sequences.TryGetValue(sequenceId, out var sequence))
            {
                return false;
            }

            return sequence.DefaultConditionIds != null && sequence.DefaultConditionIds.Count > 0;
        }

        /// <summary>
        /// 手动应用条件（不执行序列）
        /// </summary>
        public void ApplyConditions(params string[] conditionIds)
        {
            foreach (var id in conditionIds)
            {
                if (ConditionCatalog.Apply(id))
                {
                    _logService.Debug("[SequenceExecutor] 手动设置条件: {0}", id);
                }
            }

            // 触发评估
            _handlerExecutor.EvaluateAndExecute();
        }

        /// <summary>
        /// 手动清除条件
        /// </summary>
        public void ClearConditions(params string[] conditionIds)
        {
            foreach (var id in conditionIds)
            {
                ConditionCatalog.Clear(id);
                _logService.Debug("[SequenceExecutor] 手动清除条件: {0}", id);
            }
        }

        /// <summary>
        /// 清除所有条件
        /// </summary>
        public void ClearAllConditions()
        {
            ConditionCatalog.ClearAll();
            _logService.Debug("[SequenceExecutor] 已清除所有条件");
        }

        /// <summary>
        /// 执行快捷操作
        /// </summary>
        public void ExecuteQuickAction(string actionId)
        {
            var action = QuickActionCatalog.Get(actionId);
            if (action == null)
            {
                _logService.Warning("[SequenceExecutor] 快捷操作未找到: {0}", actionId);
                return;
            }

            _logService.Information("[SequenceExecutor] 执行快捷操作: {0}", action.DisplayName);
            action.Apply();

            // 触发评估
            _handlerExecutor.EvaluateAndExecute();
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 处理Handler执行完成事件
        /// ★ 支持执行计数更新
        /// </summary>
        private void OnHandlerExecutionCompleted(object sender, HandlerExecutionResultEventArgs e)
        {
            // 检查是否是我们正在等待的Handler
            if (_pendingHandlers.TryRemove(e.HandlerId, out var tcs))
            {
                // ★ 修复：根据执行次数判断真正的完成状态
                HandlerExecutionState state;
                if (!e.Success)
                {
                    state = HandlerExecutionState.Failed;
                }
                else
                {
                    // 先获取 expected 来判断是否真正完成
                    var seq = GetSequence(_currentSequenceId);
                    var exp = seq?.ExpectedHandlers.FirstOrDefault(h => h.HandlerId == e.HandlerId);

                    // 注意：此时还没调用 IncrementExecutionCount，所以要 +1 判断
                    int executionsAfterThis = (exp?.CurrentExecutions ?? 0) + 1;
                    int maxExec = exp?.MaxExecutions ?? -1;

                    if (maxExec > 0 && executionsAfterThis >= maxExec)
                    {
                        // 达到最大执行次数，才算真正完成
                        state = HandlerExecutionState.Completed;
                    }
                    else if (maxExec <= 0)
                    {
                        // 无限制模式（MaxExecutions <= 0），执行一次就算完成
                        state = HandlerExecutionState.Completed;
                    }
                    else
                    {
                        // 还没执行够次数，保持 Ready 状态等待下次触发
                        state = HandlerExecutionState.Ready;
                    }
                }
                UpdateHandlerState(e.HandlerId, state, e.Message);

                // ★ 更新执行计数
                var sequence = GetSequence(_currentSequenceId);
                if (sequence != null)
                {
                    var expected = sequence.ExpectedHandlers.FirstOrDefault(h => h.HandlerId == e.HandlerId);
                    if (expected != null)
                    {
                        expected.IncrementExecutionCount();

                        // 更新进度信息中的执行计数
                        if (_currentProgress.HandlerProgress.TryGetValue(e.HandlerId, out var progressInfo))
                        {
                            progressInfo.ExecutionCount = expected.CurrentExecutions;
                        }

                        _logService.Debug("[SequenceExecutor] Handler {0} 执行次数: {1}/{2}",
                            e.HandlerId, expected.CurrentExecutions,
                            expected.MaxExecutions > 0 ? expected.MaxExecutions.ToString() : "∞");

                        // ★★★ 新增：达到最大执行次数时临时禁用 Handler
                        if (expected.MaxExecutions > 0 && expected.CurrentExecutions >= expected.MaxExecutions)
                        {
                            _handlerExecutor.SetHandlerEnabled(expected.HandlerId, false);
                            _temporarilyDisabledHandlers.Add(expected.HandlerId);

                            _logService.Information("[SequenceExecutor] ⏸ Handler {0} 达到最大执行次数({1})，已临时禁用",
                                expected.HandlerId, expected.MaxExecutions);
                        }
                    }
                }

                // 创建结果
                var result = new HandlerResult
                {
                    HandlerId = e.HandlerId,
                    HandlerName = e.HandlerName,
                    Success = e.Success,
                    Message = e.Message,
                    Duration = e.Duration,
                    State = state
                };

                tcs.TrySetResult(result);

                // 触发单个Handler完成事件
                HandlerCompleted?.Invoke(this, new HandlerCompletedEventArgs
                {
                    SequenceId = _currentSequenceId,
                    Result = result
                });

                // ★ 记录失败情况，便于日志追踪
                if (e.Success)
                {
                    _logService.Debug("[SequenceExecutor] Handler完成: {0}, 结果: 成功", e.HandlerId);
                }
                else
                {
                    _logService.Warning("[SequenceExecutor] Handler完成: {0}, 结果: 失败 - {1}",
                        e.HandlerId, e.Message);
                }
            }
        }

        /// <summary>
        /// 更新Handler状态
        /// </summary>
        private void UpdateHandlerState(string handlerId, HandlerExecutionState newState, string message = null)
        {
            if (_currentProgress == null) return;

            if (_currentProgress.HandlerProgress.TryGetValue(handlerId, out var info))
            {
                var oldState = info.State;
                info.State = newState;
                info.Message = message;

                if (newState == HandlerExecutionState.Running)
                {
                    info.StartTime = DateTime.Now;
                }
                else if (newState == HandlerExecutionState.Completed ||
                         newState == HandlerExecutionState.Failed ||
                         newState == HandlerExecutionState.Skipped ||
                         newState == HandlerExecutionState.Timeout ||
                         newState == HandlerExecutionState.Cancelled)
                {
                    info.EndTime = DateTime.Now;
                }

                // 触发状态变化事件
                HandlerStateChanged?.Invoke(this, new HandlerStateChangedEventArgs
                {
                    SequenceId = _currentSequenceId,
                    HandlerId = handlerId,
                    OldState = oldState,
                    NewState = newState,
                    Message = message
                });
            }
        }

        #endregion

        #region ★ MaxExecutions 支持

        /// <summary>
        /// ★ 新增：恢复所有被临时禁用的 Handler
        /// </summary>
        private void RestoreDisabledHandlers()
        {
            if (_temporarilyDisabledHandlers.Count == 0)
                return;

            _logService.Debug("[SequenceExecutor] 恢复 {0} 个被临时禁用的 Handler...",
                _temporarilyDisabledHandlers.Count);

            foreach (var handlerId in _temporarilyDisabledHandlers)
            {
                try
                {
                    _handlerExecutor.SetHandlerEnabled(handlerId, true);
                    _logService.Debug("[SequenceExecutor] ✓ 已恢复 Handler: {0}", handlerId);
                }
                catch (Exception ex)
                {
                    _logService.Warning("[SequenceExecutor] 恢复 Handler {0} 失败: {1}",
                        handlerId, ex.Message);
                }
            }

            _temporarilyDisabledHandlers.Clear();
        }

        /// <summary>
        /// ★ 新增：检查指定 Handler 是否已达到最大执行次数
        /// </summary>
        private bool IsHandlerMaxExecutionsReached(string handlerId)
        {
            var sequence = GetSequence(_currentSequenceId);
            if (sequence == null) return false;

            var expected = sequence.ExpectedHandlers.FirstOrDefault(h => h.HandlerId == handlerId);
            return expected?.IsMaxExecutionsReached ?? false;
        }

        #endregion

        #region ExecuteAtEnd 支持


        /// <summary>
        /// ★ 新增：检查序列中是否有ExecuteAtEnd Handler
        /// </summary>
        private bool HasExecuteAtEndHandlers(HandlerSequence sequence)
        {
            return sequence.ExpectedHandlers.Any(h => h.ExecuteAtEnd);
        }

        /// <summary>
        /// ★ 新增：等待所有ExecuteAtEnd Handler完成
        /// 带超时保护，防止清理Handler卡死导致整个序列无法结束
        /// </summary>
        private async Task WaitForEndHandlersAsync(HandlerSequence sequence, CancellationToken ct)
        {
            var timeout = TimeSpan.FromSeconds(30); // 清理Handler最多等待30秒
            var startTime = DateTime.Now;
            var checkInterval = TimeSpan.FromMilliseconds(100);

            _logService.Debug("[SequenceExecutor] 等待清理Handler完成（超时: {0}秒）...", timeout.TotalSeconds);

            while (!ct.IsCancellationRequested)
            {
                // 检查超时
                if (DateTime.Now - startTime > timeout)
                {
                    _logService.Warning("[SequenceExecutor] 清理Handler执行超时，强制继续");

                    // 标记未完成的ExecuteAtEnd Handler为超时
                    foreach (var expected in sequence.ExpectedHandlers)
                    {
                        if (!expected.ExecuteAtEnd) continue;

                        if (_currentProgress.HandlerProgress.TryGetValue(expected.HandlerId, out var info))
                        {
                            if (info.State == HandlerExecutionState.Running ||
                                info.State == HandlerExecutionState.Ready ||
                                info.State == HandlerExecutionState.Pending)
                            {
                                UpdateHandlerState(expected.HandlerId, HandlerExecutionState.Timeout, "清理超时");
                            }
                        }
                    }
                    break;
                }

                // 检查所有ExecuteAtEnd Handler是否都完成
                bool allEndHandlersCompleted = true;
                foreach (var expected in sequence.ExpectedHandlers)
                {
                    if (!expected.ExecuteAtEnd) continue;

                    if (_currentProgress.HandlerProgress.TryGetValue(expected.HandlerId, out var info))
                    {
                        // 只要状态是终态就算完成（包括成功、失败、跳过、取消、超时）
                        bool isCompleted = info.State == HandlerExecutionState.Completed ||
                                         info.State == HandlerExecutionState.Failed ||
                                         info.State == HandlerExecutionState.Skipped ||
                                         info.State == HandlerExecutionState.Cancelled ||
                                         info.State == HandlerExecutionState.Timeout;

                        if (!isCompleted)
                        {
                            allEndHandlersCompleted = false;
                            break;
                        }
                    }
                }

                if (allEndHandlersCompleted)
                {
                    _logService.Debug("[SequenceExecutor] 所有清理Handler已完成");
                    break;
                }

                // 继续触发评估，确保清理Handler能被执行
                _handlerExecutor.EvaluateAndExecute();

                await Task.Delay(checkInterval, ct);
            }
        }


        /// <summary>
        /// 检查所有非ExecuteAtEnd的必须Handler是否都完成了
        /// ★ 修复：必须同时检查状态和运行状态
        /// </summary>
        private bool AreNonEndHandlersCompleted(HandlerSequence sequence)
        {
            foreach (var expected in sequence.ExpectedHandlers)
            {
                // 跳过 ExecuteAtEnd Handler
                if (expected.ExecuteAtEnd)
                    continue;

                // 跳过非必须的Handler
                if (!expected.IsRequired)
                    continue;

                // ★★★ 关键修复：检查Handler是否正在运行 ★★★
                // 如果Handler正在运行，则序列未完成
                if (_handlerExecutor.IsHandlerRunning(expected.HandlerId))
                {
                    return false;
                }

                // 检查执行次数是否满足要求
                if (expected.MaxExecutions > 0)
                {
                    // 有次数限制：必须执行够次数
                    if (expected.CurrentExecutions < expected.MaxExecutions)
                    {
                        return false;
                    }
                }
                else
                {
                    // 无次数限制：检查状态是否已完成
                    if (_currentProgress.HandlerProgress.TryGetValue(expected.HandlerId, out var info))
                    {
                        if (info.State != HandlerExecutionState.Completed &&
                            info.State != HandlerExecutionState.Failed &&
                            info.State != HandlerExecutionState.Skipped)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// ★ 新增：触发所有 ExecuteAtEnd Handler
        /// </summary>
        private void TriggerEndHandlers(HandlerSequence sequence)
        {
            _logService.Information("[SequenceExecutor] ========== 所有业务Handler已完成，开始触发清理Handler ==========");

            foreach (var expected in sequence.ExpectedHandlers)
            {
                // 只处理 ExecuteAtEnd Handler
                if (!expected.ExecuteAtEnd)
                    continue;

                // 跳过已标记为跳过的Handler
                if (_currentProgress.HandlerProgress.TryGetValue(expected.HandlerId, out var info)
                    && info.State == HandlerExecutionState.Skipped)
                {
                    continue;
                }

                _logService.Information("[SequenceExecutor] 触发清理Handler: {0}", expected.HandlerId);

                // 为此Handler创建等待（如果还没有）
                if (!_pendingHandlers.ContainsKey(expected.HandlerId))
                {
                    var tcs = new TaskCompletionSource<HandlerResult>();
                    _pendingHandlers[expected.HandlerId] = tcs;
                }

                // 设置此Handler的触发条件
                foreach (var conditionId in expected.TriggerConditionIds)
                {
                    if (ConditionCatalog.Apply(conditionId))
                    {
                        _logService.Debug("[SequenceExecutor] 已设置结束条件: {0} (为 {1})",
                            conditionId, expected.HandlerId);
                    }
                    else
                    {
                        _logService.Warning("[SequenceExecutor] 结束条件未找到: {0}", conditionId);
                    }
                }

                // 更新状态为Ready
                UpdateHandlerState(expected.HandlerId, HandlerExecutionState.Ready, "清理条件已设置，等待执行");
            }

            // 立即触发评估，让Handler被执行
            _handlerExecutor.EvaluateAndExecute();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Cancel();

            _handlerExecutor.HandlerExecutionCompleted -= OnHandlerExecutionCompleted;

            _currentCts?.Dispose();
            _sequences.Clear();
            _pendingHandlers.Clear();

            _logService.Information("[SequenceExecutor] 已释放");
        }

        #endregion
    }
}