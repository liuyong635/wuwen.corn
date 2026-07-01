using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedCut.Framework.Services.Sequences
{
    #region 序列执行模式

    /// <summary>
    /// 序列执行模式
    /// </summary>
    public enum SequenceExecutionMode
    {
        /// <summary>
        /// 正常模式 - Handler可按条件重复执行
        /// </summary>
        Normal,

        /// <summary>
        /// 单次执行模式 - 每个Handler只执行一次后标记完成
        /// 适用于测试场景
        /// </summary>
        SingleRun,

        /// <summary>
        /// 循环模式 - 完成后自动重新开始
        /// </summary>
        Loop
    }

    #endregion

    #region 序列失败策略

    /// <summary>
    /// 序列失败策略
    /// </summary>
    public enum SequenceFailurePolicy
    {
        /// <summary>
        /// 继续执行 - 即使某个Handler失败，也继续等待其他Handler
        /// </summary>
        Continue,

        /// <summary>
        /// 立即停止 - 任何必须的Handler失败时立即停止序列（默认）
        /// </summary>
        StopOnFirstFailure,

        /// <summary>
        /// 停止并清理 - 失败时停止序列并清除所有触发条件
        /// </summary>
        StopAndCleanup
    }

    #endregion

    #region Handler序列定义

    /// <summary>
    /// Handler序列定义
    /// 
    /// 描述一组Handler的执行期望：
    /// - 期望哪些Handler执行
    /// - 如何触发这些Handler（设置哪些条件）
    /// - 执行超时和成功条件
    /// </summary>
    public class HandlerSequence
    {
        /// <summary>
        /// 序列唯一标识
        /// </summary>
        public string SequenceId { get; set; }

        /// <summary>
        /// 序列显示名称
        /// </summary>
        public string SequenceName { get; set; }

        /// <summary>
        /// 序列描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 期望执行的Handler列表
        /// </summary>
        public List<ExpectedHandler> ExpectedHandlers { get; set; } = new List<ExpectedHandler>();

        /// <summary>
        /// 默认条件ID列表
        /// 选择此序列时作为推荐配置显示给用户
        /// </summary>
        public List<string> DefaultConditionIds { get; set; } = new List<string>();

        /// <summary>
        /// 执行模式
        /// </summary>
        public SequenceExecutionMode ExecutionMode { get; set; } = SequenceExecutionMode.Normal;

        /// <summary>
        /// ★ 失败策略
        /// 默认为 StopOnFirstFailure（失败即停止）
        /// </summary>
        public SequenceFailurePolicy FailurePolicy { get; set; } = SequenceFailurePolicy.StopOnFirstFailure;

        /// <summary>
        /// 执行超时时间
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// 评估间隔（毫秒）
        /// </summary>
        public int EvaluationIntervalMs { get; set; } = 100;

        /// <summary>
        /// 是否允许部分成功
        /// true: 只要有一个成功就算成功
        /// false: 所有必须的Handler都成功才算成功
        /// </summary>
        public bool AllowPartialSuccess { get; set; } = false;

        /// <summary>
        /// 执行前回调
        /// </summary>
        public Action<ISequenceContext> OnBeforeExecute { get; set; }

        /// <summary>
        /// 执行后回调
        /// </summary>
        public Action<ISequenceContext, SequenceExecutionResult> OnAfterExecute { get; set; }

        /// <summary>
        /// 自定义完成条件
        /// 返回true表示序列可以完成
        /// </summary>
        public Func<SequenceProgress, bool> CompletionCondition { get; set; }

        /// <summary>
        /// 获取所有触发条件ID
        /// </summary>
        public IEnumerable<string> GetAllTriggerConditionIds()
        {
            return ExpectedHandlers
                .SelectMany(h => h.TriggerConditionIds)
                .Distinct();
        }

        /// <summary>
        /// 获取推荐的默认条件
        /// </summary>
        public IReadOnlyList<ConditionItem> GetDefaultConditions()
        {
            var result = new List<ConditionItem>();
            foreach (var id in DefaultConditionIds)
            {
                var condition = ConditionCatalog.Get(id);
                if (condition != null)
                {
                    result.Add(condition);
                }
            }
            return result;
        }

        /// <summary>
        /// 重置所有Handler的执行计数
        /// </summary>
        public void ResetAllExecutionCounts()
        {
            foreach (var handler in ExpectedHandlers)
            {
                handler.ResetExecutionCount();
            }
        }
    }

    #endregion

    #region 期望Handler定义

    /// <summary>
    /// 期望执行的Handler
    /// </summary>
    public class ExpectedHandler
    {
        /// <summary>
        /// Handler ID
        /// </summary>
        public string HandlerId { get; set; }

        /// <summary>
        /// 是否必须执行成功
        /// true: 失败会导致序列失败
        /// false: 可选执行
        /// </summary>
        public bool IsRequired { get; set; } = true;

        /// <summary>
        /// 触发条件ID列表
        /// 这些条件会在序列执行时被设置
        /// </summary>
        public List<string> TriggerConditionIds { get; set; } = new List<string>();

        /// <summary>
        /// 跳过条件
        /// 如果返回true，则跳过此Handler
        /// </summary>
        public Func<ISequenceContext, bool> SkipWhen { get; set; }

        /// <summary>
        /// 最大执行次数
        /// -1 表示无限制，默认-1
        /// 设置为1表示只执行一次
        /// </summary>
        public int MaxExecutions { get; set; } = -1;

        /// <summary>
        /// ★ 新增：是否在其他Handler完成后才执行
        /// 
        /// 用于清理Handler（如SystemShutdown）：
        /// - 设置为true时，此Handler不会在序列开始时被触发
        /// - 当所有其他必须的Handler完成后，自动设置触发条件
        /// 
        /// 默认为false（正常触发）
        /// </summary>
        public bool ExecuteAtEnd { get; set; } = false;

        /// <summary>
        /// 当前已执行次数（运行时状态）
        /// </summary>
        public int CurrentExecutions { get; set; } = 0;

        /// <summary>
        /// 是否已达到最大执行次数
        /// </summary>
        public bool IsMaxExecutionsReached => MaxExecutions > 0 && CurrentExecutions >= MaxExecutions;

        /// <summary>
        /// 重置执行计数
        /// </summary>
        public void ResetExecutionCount()
        {
            CurrentExecutions = 0;
        }

        /// <summary>
        /// 增加执行计数
        /// </summary>
        public void IncrementExecutionCount()
        {
            CurrentExecutions++;
        }
    }

    #endregion

    #region 序列上下文

    /// <summary>
    /// 序列上下文接口
    /// </summary>
    public interface ISequenceContext
    {
        string SequenceId { get; }
        string SequenceName { get; }

        void Set<T>(string key, T value);
        T Get<T>(string key, T defaultValue = default(T));
        bool Has(string key);
        void Remove(string key);

        void SetHandlerResult(string handlerId, HandlerResult result);
        HandlerResult GetHandlerResult(string handlerId);
    }

    /// <summary>
    /// 序列上下文实现
    /// </summary>
    public class SequenceContext : ISequenceContext
    {
        private readonly Dictionary<string, object> _data = new Dictionary<string, object>();
        private readonly Dictionary<string, HandlerResult> _handlerResults = new Dictionary<string, HandlerResult>();

        public string SequenceId { get; }
        public string SequenceName { get; }

        public SequenceContext(string sequenceId, string sequenceName)
        {
            SequenceId = sequenceId;
            SequenceName = sequenceName;
        }

        public void Set<T>(string key, T value)
        {
            _data[key] = value;
        }

        public T Get<T>(string key, T defaultValue = default(T))
        {
            if (_data.TryGetValue(key, out var value) && value is T t)
            {
                return t;
            }
            return defaultValue;
        }

        public bool Has(string key)
        {
            return _data.ContainsKey(key);
        }

        public void Remove(string key)
        {
            _data.Remove(key);
        }

        public void SetHandlerResult(string handlerId, HandlerResult result)
        {
            _handlerResults[handlerId] = result;
        }

        public HandlerResult GetHandlerResult(string handlerId)
        {
            _handlerResults.TryGetValue(handlerId, out var result);
            return result;
        }
    }

    #endregion

    #region 执行结果

    /// <summary>
    /// 序列执行结果
    /// </summary>
    public class SequenceExecutionResult
    {
        public string SequenceId { get; set; }
        public string SequenceName { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        /// <summary>
        /// ★ 失败原因（如果是因为Handler失败而停止）
        /// </summary>
        public string FailureReason { get; set; }

        /// <summary>
        /// ★ 导致失败的Handler ID
        /// </summary>
        public string FailedHandlerId { get; set; }

        public Dictionary<string, HandlerResult> HandlerResults { get; set; }
            = new Dictionary<string, HandlerResult>();

        public int ExecutedCount => HandlerResults.Count(r =>
            r.Value.State == HandlerExecutionState.Completed ||
            r.Value.State == HandlerExecutionState.Failed);

        public int SuccessCount => HandlerResults.Count(r =>
            r.Value.State == HandlerExecutionState.Completed);

        public int FailedCount => HandlerResults.Count(r =>
            r.Value.State == HandlerExecutionState.Failed);

        public int SkippedCount => HandlerResults.Count(r =>
            r.Value.State == HandlerExecutionState.Skipped);

        public int CancelledCount => HandlerResults.Count(r =>
            r.Value.State == HandlerExecutionState.Cancelled);
    }

    /// <summary>
    /// Handler执行结果
    /// </summary>
    public class HandlerResult
    {
        public string HandlerId { get; set; }
        public string HandlerName { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public TimeSpan Duration { get; set; }
        public HandlerExecutionState State { get; set; }
    }

    /// <summary>
    /// Handler执行状态
    /// </summary>
    public enum HandlerExecutionState
    {
        /// <summary>待执行</summary>
        Pending,
        /// <summary>条件已就绪</summary>
        Ready,
        /// <summary>正在执行</summary>
        Running,
        /// <summary>已完成</summary>
        Completed,
        /// <summary>执行失败</summary>
        Failed,
        /// <summary>已跳过</summary>
        Skipped,
        /// <summary>执行超时</summary>
        Timeout,
        /// <summary>已取消</summary>
        Cancelled
    }

    #endregion

    #region 进度跟踪

    /// <summary>
    /// 序列执行进度
    /// </summary>
    public class SequenceProgress
    {
        public string SequenceId { get; set; }
        public DateTime StartTime { get; set; }

        public Dictionary<string, HandlerProgressInfo> HandlerProgress { get; set; }
            = new Dictionary<string, HandlerProgressInfo>();

        /// <summary>
        /// 所有必须的Handler是否都完成了
        /// </summary>
        public bool AllRequiredCompleted => HandlerProgress.Values
            .Where(h => h.IsRequired)
            .All(h => h.State == HandlerExecutionState.Completed ||
                      h.State == HandlerExecutionState.Skipped);

        /// <summary>
        /// 所有必须的Handler是否都成功了
        /// </summary>
        public bool AllRequiredSuccess => HandlerProgress.Values
            .Where(h => h.IsRequired)
            .All(h => h.State == HandlerExecutionState.Completed ||
                      h.State == HandlerExecutionState.Skipped);

        /// <summary>
        /// ★ 是否有必须的Handler失败了
        /// </summary>
        public bool HasRequiredFailure => HandlerProgress.Values
            .Where(h => h.IsRequired)
            .Any(h => h.State == HandlerExecutionState.Failed);

        /// <summary>
        /// ★ 是否有任何Handler失败了（包括非必须的）
        /// </summary>
        public bool HasAnyFailure => HandlerProgress.Values
            .Any(h => h.State == HandlerExecutionState.Failed);

        /// <summary>
        /// ★ 获取第一个失败的必须Handler
        /// </summary>
        public HandlerProgressInfo GetFirstFailedRequired()
        {
            return HandlerProgress.Values
                .Where(h => h.IsRequired && h.State == HandlerExecutionState.Failed)
                .FirstOrDefault();
        }

        /// <summary>
        /// ★ 获取所有失败的Handler
        /// </summary>
        public IEnumerable<HandlerProgressInfo> GetAllFailed()
        {
            return HandlerProgress.Values
                .Where(h => h.State == HandlerExecutionState.Failed);
        }

        /// <summary>
        /// 完成百分比
        /// </summary>
        public int PercentComplete
        {
            get
            {
                if (HandlerProgress.Count == 0) return 0;

                var completed = HandlerProgress.Values.Count(h =>
                    h.State == HandlerExecutionState.Completed ||
                    h.State == HandlerExecutionState.Failed ||
                    h.State == HandlerExecutionState.Skipped ||
                    h.State == HandlerExecutionState.Timeout ||
                    h.State == HandlerExecutionState.Cancelled);

                return (int)(completed * 100.0 / HandlerProgress.Count);
            }
        }
    }

    /// <summary>
    /// Handler进度信息
    /// </summary>
    public class HandlerProgressInfo
    {
        public string HandlerId { get; set; }
        public string HandlerName { get; set; }
        public bool IsRequired { get; set; }
        public HandlerExecutionState State { get; set; }
        public string Message { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int ExecutionCount { get; set; }
        public int MaxExecutions { get; set; } = -1;

        public TimeSpan? Duration
        {
            get
            {
                if (StartTime.HasValue && EndTime.HasValue)
                {
                    return EndTime.Value - StartTime.Value;
                }
                return null;
            }
        }

        /// <summary>
        /// 显示执行次数（如 "1/1" 或 "2/∞"）
        /// </summary>
        public string ExecutionCountDisplay
        {
            get
            {
                var max = MaxExecutions > 0 ? MaxExecutions.ToString() : "∞";
                return string.Format("{0}/{1}", ExecutionCount, max);
            }
        }

        public bool IsExecuteAtEnd { get; set; }
    }

    #endregion

    #region 事件参数

    public class SequenceStartedEventArgs : EventArgs
    {
        public string SequenceId { get; set; }
        public string SequenceName { get; set; }
        public List<ExpectedHandler> ExpectedHandlers { get; set; }
        public List<string> TriggerConditionIds { get; set; }
    }

    public class SequenceCompletedEventArgs : EventArgs
    {
        public SequenceExecutionResult Result { get; set; }
    }

    public class HandlerStateChangedEventArgs : EventArgs
    {
        public string SequenceId { get; set; }
        public string HandlerId { get; set; }
        public HandlerExecutionState OldState { get; set; }
        public HandlerExecutionState NewState { get; set; }
        public string Message { get; set; }
    }

    public class HandlerCompletedEventArgs : EventArgs
    {
        public string SequenceId { get; set; }
        public HandlerResult Result { get; set; }
    }

    public class SequenceProgressEventArgs : EventArgs
    {
        public SequenceProgress Progress { get; set; }
    }

    /// <summary>
    /// ★ 新增：序列因失败停止事件参数
    /// </summary>
    public class SequenceStoppedEventArgs : EventArgs
    {
        public string SequenceId { get; set; }
        public string FailedHandlerId { get; set; }
        public string FailedHandlerName { get; set; }
        public string FailureMessage { get; set; }
        public SequenceFailurePolicy Policy { get; set; }
    }

    #endregion
}