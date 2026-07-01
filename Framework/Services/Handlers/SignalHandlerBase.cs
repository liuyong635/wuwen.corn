using SeedCut.Framework.Core;
using SeedCut.Framework.Models;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Execution;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// SignalHandler 基类
    /// 
    /// ★ 修改：新增条件诊断功能
    /// - 执行时自动输出触发条件的详细评估结果
    /// - 可通过 EnableConditionDiagnostics 开关控制
    /// 
    /// 提供功能：
    /// 1. 标准化的接口实现
    /// 2. 日志记录
    /// 3. 执行计时
    /// 4. 异常包装
    /// 5. 常用辅助方法（等待信号、等待标志等）
    /// 6. 报警服务集成
    /// 7. ★ 条件诊断输出
    /// </summary>
    public abstract class SignalHandlerBase : ISignalHandler
    {
        #region 抽象属性（子类必须实现）

        public abstract string HandlerId { get; }
        public abstract string HandlerName { get; }
        public abstract ITriggerCondition TriggerCondition { get; }

        #endregion

        #region 可重写属性

        /// <summary>
        /// 最小执行间隔（毫秒）
        /// 0 = 不限制（默认）
        /// >0 = 两次执行之间的最小间隔，用于防止PLC信号抖动导致的重复触发
        /// </summary>
        public virtual int MinExecutionIntervalMs => 0;
        public virtual int Priority { get { return 50; } }
        public virtual bool IsEnabled { get; set; } = true;
        public virtual string[] DependentDevices { get { return new string[0]; } }

        /// <summary>
        /// 上次执行完成时间（用于防重复保护）
        /// </summary>
        private DateTime _lastExecutionTime = DateTime.MinValue;

        #endregion

        #region ★ 条件诊断配置

        /// <summary>
        /// ★ 新增：是否启用条件诊断输出
        /// 默认启用，会在执行时输出触发条件的详细信息
        /// </summary>
        public bool EnableConditionDiagnostics { get; set; } = true;

        /// <summary>
        /// ★ 新增：诊断配置
        /// </summary>
        public ConditionDiagnosticsConfig DiagnosticsConfig { get; set; } = ConditionDiagnosticsConfig.Default;

        /// <summary>
        /// ★ 新增：条件上下文（由执行器注入，用于诊断）
        /// </summary>
        protected IConditionContext ConditionContext { get; private set; }

        /// <summary>
        /// ★ 新增：设置条件上下文
        /// </summary>
        public void SetConditionContext(IConditionContext context)
        {
            ConditionContext = context;
        }

        #endregion

        #region 日志与报警服务

        protected ILogService Log { get; private set; }
        protected IAlarmService AlarmService { get; private set; }

        public void SetLogger(ILogService logService)
        {
            Log = logService;
        }

        public void SetAlarmService(IAlarmService alarmService)
        {
            AlarmService = alarmService;
        }

        protected void LogInfo(string message, params object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            Log?.Information("[{0}] {1}", HandlerId, formatted);
        }

        protected void LogDebug(string message, params object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            Log?.Debug("[{0}] {1}", HandlerId, formatted);
        }

        protected void LogWarning(string message, params object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            Log?.Warning("[{0}] {1}", HandlerId, formatted);

            if (AlarmService != null)
            {
                AlarmService.TriggerAlarm(
                    name: string.Format("[{0}] {1}", HandlerName, formatted),
                    level: AlarmLevel.Warning,
                    description: formatted,
                    sourceModule: HandlerId,
                    sourceIdentifier: string.Format("{0}_{1:HHmmss}", HandlerId, DateTime.Now),
                    showPopup: false
                );
            }
        }

        protected void LogError(Exception ex, string message, params object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            Log?.Error(ex, "[{0}] {1}", HandlerId, formatted);

            if (AlarmService != null)
            {
                var description = ex != null
                    ? string.Format("{0}: {1}", formatted, ex.Message)
                    : formatted;

                AlarmService.TriggerAlarm(
                    name: string.Format("[{0}] {1}", HandlerName, formatted),
                    level: AlarmLevel.Error,
                    description: description,
                    sourceModule: HandlerId,
                    sourceIdentifier: string.Format("{0}_{1:HHmmss}", HandlerId, DateTime.Now),
                    showPopup: true
                );
            }
        }

        #endregion

        #region 执行方法

        public virtual bool CanExecute(IConditionContext context)
        {
            return true;
        }

        /// <summary>
        /// 执行入口（ISignalHandler接口实现）
        /// ★ 修改：执行前输出条件诊断信息
        /// </summary>
        public async Task<ValueTuple<bool, string>> HandleAsync(
            IHandlerContext context,
            CancellationToken ct)
        {
            var startTime = DateTime.Now;

            try
            {

                // ★ 防重复保护检查
                if (MinExecutionIntervalMs > 0)
                {
                    var elapsedProtect = (DateTime.Now - _lastExecutionTime).TotalMilliseconds;
                    if (elapsedProtect < MinExecutionIntervalMs)
                    {
                        LogDebug("防重复保护：距上次执行仅{0:F0}ms（需>{1}ms），跳过",
                            elapsedProtect, MinExecutionIntervalMs);
                        return new ValueTuple<bool, string>(true, "防重复跳过");
                    }
                }

                // ★ 新增：输出条件诊断信息
                if (EnableConditionDiagnostics && TriggerCondition != null && ConditionContext != null)
                {
                    LogConditionDiagnostics();
                }

                LogDebug("开始执行");

                var result = await ExecuteAsync(context, ct);

                var elapsed = DateTime.Now - startTime;
                LogDebug("执行完成, 耗时: {0}ms, 结果: {1}", elapsed.TotalMilliseconds.ToString("F0"), result.Item1);

                return result;
            }
            catch (OperationCanceledException)
            {
                var elapsed = DateTime.Now - startTime;
                Log?.Warning("[{0}] 执行已取消, 耗时: {1}ms", HandlerId, elapsed.TotalMilliseconds.ToString("F0"));
                return new ValueTuple<bool, string>(false, "操作已取消");
            }
            catch (Exception ex)
            {
                var elapsed = DateTime.Now - startTime;
                LogError(ex, "执行异常, 耗时: {0}ms", elapsed.TotalMilliseconds.ToString("F0"));
                return new ValueTuple<bool, string>(false, "异常: " + ex.Message);
            }
            finally
            {
                // ★ 记录执行完成时间
                _lastExecutionTime = DateTime.Now;
            }
        }

        /// <summary>
        /// ★ 新增：输出条件诊断日志
        /// </summary>
        protected virtual void LogConditionDiagnostics()
        {
            try
            {
                // 评估条件
                var result = TriggerCondition.Evaluate(ConditionContext);

                // 格式化输出
                var formatted = ConditionDiagnosticsFormatter.Format(
                    HandlerId,
                    HandlerName,
                    result,
                    DiagnosticsConfig);

                // 输出日志
                Log?.Debug("[条件诊断]\n{0}", formatted);
            }
            catch (Exception ex)
            {
                // 诊断失败不影响正常执行
                Log?.Debug("[{0}] 条件诊断失败: {1}", HandlerId, ex.Message);
            }
        }

        /// <summary>
        /// ★ 新增：获取条件评估结果（供外部调用）
        /// </summary>
        public ConditionEvaluationResult GetConditionEvaluation(IConditionContext context = null)
        {
            var ctx = context ?? ConditionContext;
            if (TriggerCondition == null || ctx == null)
            {
                return new ConditionEvaluationResult
                {
                    Description = "(无条件)",
                    ConditionType = "None",
                    IsSatisfied = true
                };
            }

            return TriggerCondition.Evaluate(ctx);
        }

        /// <summary>
        /// ★ 新增：获取格式化的条件诊断字符串
        /// </summary>
        public string GetConditionDiagnosticsString(IConditionContext context = null)
        {
            var result = GetConditionEvaluation(context);
            return ConditionDiagnosticsFormatter.Format(HandlerId, HandlerName, result, DiagnosticsConfig);
        }

        protected abstract Task<ValueTuple<bool, string>> ExecuteAsync(
            IHandlerContext context,
            CancellationToken ct);

        #endregion

        #region 辅助方法（保持不变）

        

        protected bool ReadSignal(IHandlerContext ctx, string signalName)
        {
            return ctx.Signal?.ReadBit(signalName) ?? false;
        }

        protected void WriteSignal(IHandlerContext ctx, string signalName, bool value)
        {
            ctx.Signal?.WriteBit(signalName, value);
            LogDebug("写入信号: {0} = {1}", signalName, value);
        }

        protected void WriteSignalPulse(IHandlerContext ctx, string signalName, int durationMs = 50)
        {
            ctx.Signal?.WriteBitPulse(signalName, durationMs);
            LogDebug("写入脉冲: {0} ({1}ms)", signalName, durationMs);
        }



        protected void SetBusy(bool busy)
        {
            FlagCondition.SetFlag(HandlerId + "_Busy", busy);
        }

        protected bool IsBusy()
        {
            return FlagCondition.GetFlag(HandlerId + "_Busy");
        }

        protected ValueTuple<bool, string> Success(string message = "成功")
        {
            return new ValueTuple<bool, string>(true, message);
        }

        protected ValueTuple<bool, string> Fail(string message)
        {
            return new ValueTuple<bool, string>(false, message);
        }

        #endregion
    }
}