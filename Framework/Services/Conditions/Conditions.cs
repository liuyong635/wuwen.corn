using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace SeedCut.Framework.Services.Conditions
{
    #region 条件实现（已增强：实现 IEvaluatableCondition）

    /// <summary>
    /// 信号条件
    /// ★ 修改：实现 IEvaluatableCondition 接口
    /// </summary>
    public class SignalCondition : IEvaluatableCondition
    {
        public string SignalName { get; }
        public bool ExpectedValue { get; }

        public SignalCondition(string signalName, bool expectedValue = true)
        {
            SignalName = signalName;
            ExpectedValue = expectedValue;
        }

        public bool IsSatisfied(IConditionContext context)
            => context?.GetSignal(SignalName) == ExpectedValue;

        public string Description => string.Format("Signal[{0}]=={1}", SignalName, ExpectedValue);

        /// <summary>
        /// ★ 新增：评估并返回详细结果
        /// </summary>
        public ConditionEvaluationResult Evaluate(IConditionContext context)
        {
            var actualValue = context?.GetSignal(SignalName) ?? false;
            return new ConditionEvaluationResult
            {
                Description = Description,
                ConditionType = "Signal",
                Identifier = SignalName,
                IsSatisfied = actualValue == ExpectedValue,
                ExpectedValue = ExpectedValue.ToString(),
                ActualValue = actualValue.ToString()
            };
        }
    }

    /// <summary>
    /// 状态条件 - 基于 IProductionContext 的属性
    /// ★ 修改：实现 IEvaluatableCondition 接口
    /// </summary>
    public class StateCondition : IEvaluatableCondition
    {
        public SystemState? RequiredState { get; }
        public RunningSubState? RequiredSub { get; }

        public StateCondition(SystemState? state, RunningSubState? sub = null)
        {
            RequiredState = state;
            RequiredSub = sub;
        }

        public bool IsSatisfied(IConditionContext context)
        {
            if (context?.Context == null) return false;
            if (!RequiredState.HasValue) return true;

            var ctx = context.Context;
            switch (RequiredState.Value)
            {
                case SystemState.Running:
                    return ctx.IsRunning && !ctx.IsPaused;
                case SystemState.Paused:
                    return ctx.IsPaused;
                case SystemState.Ready:
                    return !ctx.IsRunning && !ctx.IsEmergencyStopped;
                case SystemState.Idle:
                    return !ctx.IsRunning && !ctx.IsPaused && !ctx.IsEmergencyStopped;
                case SystemState.EmergencyStopped:
                    return ctx.IsEmergencyStopped;
                case SystemState.Error:
                    return ctx.IsEmergencyStopped;
                default:
                    return false;
            }
        }

        public string Description
        {
            get
            {
                if (!RequiredState.HasValue) return "State[Any]";
                var s = RequiredState.Value.ToString();
                if (RequiredSub.HasValue && RequiredSub.Value != RunningSubState.None)
                    s += "." + RequiredSub.Value;
                return string.Format("State[{0}]", s);
            }
        }

        /// <summary>
        /// ★ 新增：评估并返回详细结果
        /// </summary>
        public ConditionEvaluationResult Evaluate(IConditionContext context)
        {
            var actualState = GetCurrentState(context);
            var isSatisfied = IsSatisfied(context);

            return new ConditionEvaluationResult
            {
                Description = Description,
                ConditionType = "State",
                Identifier = RequiredState?.ToString() ?? "Any",
                IsSatisfied = isSatisfied,
                ExpectedValue = RequiredState?.ToString() ?? "Any",
                ActualValue = actualState
            };
        }

        /// <summary>
        /// 获取当前系统状态的字符串表示
        /// </summary>
        private string GetCurrentState(IConditionContext context)
        {
            if (context?.Context == null) return "Unknown";

            var ctx = context.Context;
            if (ctx.IsEmergencyStopped) return "EmergencyStopped";
            if (ctx.IsPaused) return "Paused";
            if (ctx.IsRunning) return "Running";
            return "Idle";
        }
    }

    /// <summary>
    /// 标志条件 - 使用独立的标志存储
    /// ★ 修改：实现 IEvaluatableCondition 接口
    /// </summary>
    public class FlagCondition : IEvaluatableCondition
    {
        // 静态标志存储
        private static readonly ConcurrentDictionary<string, bool> _flags = new ConcurrentDictionary<string, bool>();

        public string FlagName { get; }
        public bool ExpectedValue { get; }

        public FlagCondition(string flagName, bool expectedValue = true)
        {
            FlagName = flagName;
            ExpectedValue = expectedValue;
        }

        public bool IsSatisfied(IConditionContext context)
        {
            return _flags.TryGetValue(FlagName, out var value) && value == ExpectedValue;
        }

        public string Description => string.Format("Flag[{0}]=={1}", FlagName, ExpectedValue);

        /// <summary>
        /// ★ 新增：评估并返回详细结果
        /// </summary>
        public ConditionEvaluationResult Evaluate(IConditionContext context)
        {
            var actualValue = _flags.TryGetValue(FlagName, out var v) ? v : false;
            return new ConditionEvaluationResult
            {
                Description = Description,
                ConditionType = "Flag",
                Identifier = FlagName,
                IsSatisfied = actualValue == ExpectedValue,
                ExpectedValue = ExpectedValue.ToString(),
                ActualValue = actualValue.ToString()
            };
        }

        // 静态方法保持不变
        // ★ 修改：SetFlag 时触发事件
        public static void SetFlag(string flagName, bool value)
        {
            var oldValue = _flags.TryGetValue(flagName, out var v) ? v : false;
            _flags[flagName] = value;

            // 只在值真正变化时触发事件
            if (oldValue != value)
            {
                FlagChanged?.Invoke(flagName, value);
            }
        }

        // ★★★ 新增：标志变化事件 ★★★
        public static event Action<string, bool> FlagChanged;

        public static bool GetFlag(string flagName, bool defaultValue = false)
            => _flags.TryGetValue(flagName, out var value) ? value : defaultValue;
        public static bool RemoveFlag(string flagName)
        {
            var removed = _flags.TryRemove(flagName, out var oldValue);
            if (removed && oldValue)
            {
                FlagChanged?.Invoke(flagName, false);
            }
            return removed;
        }
        public static void ClearAllFlags() => _flags.Clear();

        /// <summary>
        /// ★ 新增：获取所有标志的当前状态（用于诊断）
        /// </summary>
        public static IReadOnlyDictionary<string, bool> GetAllFlags()
            => new Dictionary<string, bool>(_flags);
    }

    /// <summary>
    /// 工位条件
    /// ★ 修改：实现 IEvaluatableCondition 接口
    /// </summary>
    public class StationCondition : IEvaluatableCondition
    {
        public string StationId { get; }
        public bool ExpectedBusy { get; }

        public StationCondition(string stationId, bool expectedBusy)
        {
            StationId = stationId;
            ExpectedBusy = expectedBusy;
        }

        public bool IsSatisfied(IConditionContext context)
            => context?.IsStationBusy(StationId) == ExpectedBusy;

        public string Description => ExpectedBusy
            ? string.Format("Station[{0}].Busy", StationId)
            : string.Format("Station[{0}].Idle", StationId);

        /// <summary>
        /// ★ 新增：评估并返回详细结果
        /// </summary>
        public ConditionEvaluationResult Evaluate(IConditionContext context)
        {
            var actualBusy = context?.IsStationBusy(StationId) ?? false;
            return new ConditionEvaluationResult
            {
                Description = Description,
                ConditionType = "Station",
                Identifier = StationId,
                IsSatisfied = actualBusy == ExpectedBusy,
                ExpectedValue = ExpectedBusy ? "Busy" : "Idle",
                ActualValue = actualBusy ? "Busy" : "Idle"
            };
        }
    }

    /// <summary>
    /// 组合条件（AND）
    /// ★ 修改：实现 IEvaluatableCondition 接口
    /// </summary>
    public class AndCondition : IEvaluatableCondition
    {
        private readonly List<ITriggerCondition> _conditions = new List<ITriggerCondition>();

        public AndCondition(params ITriggerCondition[] conditions)
        {
            _conditions.AddRange(conditions.Where(c => c != null));
        }

        public AndCondition And(ITriggerCondition condition)
        {
            if (condition != null) _conditions.Add(condition);
            return this;
        }

        public bool IsSatisfied(IConditionContext context)
            => _conditions.Count == 0 || _conditions.All(c => c.IsSatisfied(context));

        public string Description => string.Format("({0})",
            string.Join(" AND ", _conditions.Select(c => c.Description)));

        /// <summary>
        /// ★ 新增：评估并返回详细结果
        /// </summary>
        public ConditionEvaluationResult Evaluate(IConditionContext context)
        {
            var result = new ConditionEvaluationResult
            {
                Description = Description,
                ConditionType = "And",
                Children = new List<ConditionEvaluationResult>()
            };

            foreach (var condition in _conditions)
            {
                var childResult = condition.Evaluate(context);
                result.Children.Add(childResult);
            }

            result.IsSatisfied = result.Children.Count == 0 || result.Children.All(c => c.IsSatisfied);
            result.ExpectedValue = "All";
            result.ActualValue = string.Format("{0}/{1}", result.SatisfiedChildCount, result.TotalChildCount);

            return result;
        }

        /// <summary>
        /// ★ 新增：获取子条件列表（用于诊断）
        /// </summary>
        public IReadOnlyList<ITriggerCondition> GetConditions() => _conditions.ToList();
    }

    /// <summary>
    /// 或条件（OR）
    /// ★ 修改：实现 IEvaluatableCondition 接口
    /// </summary>
    public class OrCondition : IEvaluatableCondition
    {
        private readonly List<ITriggerCondition> _conditions = new List<ITriggerCondition>();

        public OrCondition(params ITriggerCondition[] conditions)
        {
            _conditions.AddRange(conditions.Where(c => c != null));
        }

        public OrCondition Or(ITriggerCondition condition)
        {
            if (condition != null) _conditions.Add(condition);
            return this;
        }

        public bool IsSatisfied(IConditionContext context)
            => _conditions.Any(c => c.IsSatisfied(context));

        public string Description => string.Format("({0})",
            string.Join(" OR ", _conditions.Select(c => c.Description)));

        /// <summary>
        /// ★ 新增：评估并返回详细结果
        /// </summary>
        public ConditionEvaluationResult Evaluate(IConditionContext context)
        {
            var result = new ConditionEvaluationResult
            {
                Description = Description,
                ConditionType = "Or",
                Children = new List<ConditionEvaluationResult>()
            };

            foreach (var condition in _conditions)
            {
                var childResult = condition.Evaluate(context);
                result.Children.Add(childResult);
            }

            result.IsSatisfied = result.Children.Any(c => c.IsSatisfied);
            result.ExpectedValue = "Any";
            result.ActualValue = string.Format("{0}/{1}", result.SatisfiedChildCount, result.TotalChildCount);

            return result;
        }

        /// <summary>
        /// ★ 新增：获取子条件列表（用于诊断）
        /// </summary>
        public IReadOnlyList<ITriggerCondition> GetConditions() => _conditions.ToList();
    }

    /// <summary>
    /// 非条件（NOT）
    /// ★ 修改：实现 IEvaluatableCondition 接口
    /// </summary>
    public class NotCondition : IEvaluatableCondition
    {
        private readonly ITriggerCondition _condition;

        public NotCondition(ITriggerCondition condition) => _condition = condition;

        public bool IsSatisfied(IConditionContext context)
            => !_condition.IsSatisfied(context);

        public string Description => string.Format("NOT({0})", _condition.Description);

        /// <summary>
        /// ★ 新增：获取内部条件（用于信号提取）
        /// </summary>
        public ITriggerCondition GetInnerCondition() => _condition;

        /// <summary>
        /// ★ 新增：评估并返回详细结果
        /// </summary>
        public ConditionEvaluationResult Evaluate(IConditionContext context)
        {
            var innerResult = _condition.Evaluate(context);

            return new ConditionEvaluationResult
            {
                Description = Description,
                ConditionType = "Not",
                IsSatisfied = !innerResult.IsSatisfied,
                ExpectedValue = "False",
                ActualValue = innerResult.IsSatisfied.ToString(),
                Children = new List<ConditionEvaluationResult> { innerResult }
            };
        }
    }

    #endregion

    #region 条件构建器（保持不变）

    /// <summary>
    /// 条件构建器（流式API）
    /// </summary>
    public static class When
    {
        // 信号条件
        public static SignalCondition Signal(string name, bool value = true) => new SignalCondition(name, value);
        public static SignalCondition SignalOn(string name) => new SignalCondition(name, true);
        public static SignalCondition SignalOff(string name) => new SignalCondition(name, false);

        // 状态条件
        public static StateCondition State(SystemState state, RunningSubState? sub = null) => new StateCondition(state, sub);
        public static StateCondition IsRunning(RunningSubState? sub = null) => new StateCondition(SystemState.Running, sub);
        public static StateCondition IsReady() => new StateCondition(SystemState.Ready);
        public static StateCondition IsPaused() => new StateCondition(SystemState.Paused);
        public static StateCondition IsIdle() => new StateCondition(SystemState.Idle);

        // 标志条件
        public static FlagCondition Flag(string name, bool value = true) => new FlagCondition(name, value);
        public static FlagCondition FlagOn(string name) => new FlagCondition(name, true);
        public static FlagCondition FlagOff(string name) => new FlagCondition(name, false);

        // 工位条件
        public static StationCondition StationBusy(string id) => new StationCondition(id, true);
        public static StationCondition StationIdle(string id) => new StationCondition(id, false);

        // 组合条件
        public static AndCondition All(params ITriggerCondition[] conditions) => new AndCondition(conditions);
        public static OrCondition Any(params ITriggerCondition[] conditions) => new OrCondition(conditions);
        public static NotCondition Not(ITriggerCondition condition) => new NotCondition(condition);

        // 常用组合
        public static AndCondition RunningWithSignal(string signalName, bool value = true)
            => All(IsRunning(), Signal(signalName, value));

        public static AndCondition RunningWithSignalAndNotBusy(string signalName, string busyFlag)
            => All(IsRunning(), SignalOn(signalName), FlagOff(busyFlag));
    }

    #endregion

    #region 条件上下文（保持不变）

    /// <summary>
    /// 条件上下文实现
    /// </summary>
    public class ConditionContext : IConditionContext
    {
        private readonly Func<string, bool> _signalGetter;
        private readonly ITaskExecutionManager _stations;

        public IProductionContext Context { get; }

        public ConditionContext(
            IProductionContext context,
            Func<string, bool> signalGetter,
            ITaskExecutionManager stations)
        {
            Context = context;
            _signalGetter = signalGetter;
            _stations = stations;
        }

        public bool GetSignal(string name, bool defaultValue = false)
            => _signalGetter?.Invoke(name) ?? defaultValue;

        public bool IsStationBusy(string stationId)
            => _stations?.IsRunning(stationId) ?? false;
    }

    #endregion


    #region 信号提取器（用于自动注册监控信号）

    /// <summary>
    /// 条件信号提取器
    /// 用于从 TriggerCondition 中递归提取所有需要监控的 PLC 信号名
    /// </summary>
    public static class ConditionSignalExtractor
    {
        /// <summary>
        /// 从触发条件中提取所有信号名
        /// </summary>
        /// <param name="condition">触发条件</param>
        /// <returns>信号名集合（去重）</returns>
        public static HashSet<string> ExtractSignalNames(ITriggerCondition condition)
        {
            var signals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ExtractSignalNamesRecursive(condition, signals);
            return signals;
        }

        /// <summary>
        /// 递归提取信号名
        /// </summary>
        private static void ExtractSignalNamesRecursive(ITriggerCondition condition, HashSet<string> signals)
        {
            if (condition == null)
                return;

            // 直接是 SignalCondition
            if (condition is SignalCondition signalCondition)
            {
                if (!string.IsNullOrEmpty(signalCondition.SignalName))
                {
                    signals.Add(signalCondition.SignalName);
                }
                return;
            }

            // AndCondition - 递归处理子条件
            if (condition is AndCondition andCondition)
            {
                foreach (var child in andCondition.GetConditions())
                {
                    ExtractSignalNamesRecursive(child, signals);
                }
                return;
            }

            // OrCondition - 递归处理子条件
            if (condition is OrCondition orCondition)
            {
                foreach (var child in orCondition.GetConditions())
                {
                    ExtractSignalNamesRecursive(child, signals);
                }
                return;
            }

            // NotCondition - 递归处理内部条件
            if (condition is NotCondition notCondition)
            {
                // NotCondition 没有暴露内部条件的方法，需要添加
                var innerCondition = notCondition.GetInnerCondition();
                if (innerCondition != null)
                {
                    ExtractSignalNamesRecursive(innerCondition, signals);
                }
                return;
            }

            // 其他类型的条件（StateCondition, FlagCondition, StationCondition）不包含 PLC 信号
        }
    }

    #endregion
}