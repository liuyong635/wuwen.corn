using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SeedCut.Framework.Services.Conditions
{
    #region 评估结果类

    /// <summary>
    /// 条件评估结果
    /// 包含条件的详细评估信息，用于调试和诊断
    /// </summary>
    public class ConditionEvaluationResult
    {
        /// <summary>
        /// 条件描述（来自 ITriggerCondition.Description）
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 条件类型名称（Signal/Flag/State/And/Or/Not/Station）
        /// </summary>
        public string ConditionType { get; set; }

        /// <summary>
        /// 条件是否满足
        /// </summary>
        public bool IsSatisfied { get; set; }

        /// <summary>
        /// 期望值（字符串表示）
        /// 例如：对于 SignalCondition，可能是 "True" 或 "False"
        /// </summary>
        public string ExpectedValue { get; set; }

        /// <summary>
        /// 实际值（字符串表示）
        /// 例如：信号的当前值 "True" 或 "False"
        /// </summary>
        public string ActualValue { get; set; }

        /// <summary>
        /// 相关标识符（信号名、标志名、工位ID等）
        /// </summary>
        public string Identifier { get; set; }

        /// <summary>
        /// 子条件评估结果（用于复合条件 And/Or/Not）
        /// </summary>
        public List<ConditionEvaluationResult> Children { get; set; }

        /// <summary>
        /// 是否为复合条件
        /// </summary>
        public bool IsComposite => Children != null && Children.Count > 0;

        /// <summary>
        /// 满足的子条件数量（仅复合条件有意义）
        /// </summary>
        public int SatisfiedChildCount => Children?.Count(c => c.IsSatisfied) ?? 0;

        /// <summary>
        /// 子条件总数（仅复合条件有意义）
        /// </summary>
        public int TotalChildCount => Children?.Count ?? 0;

        public ConditionEvaluationResult()
        {
            Children = new List<ConditionEvaluationResult>();
        }

        /// <summary>
        /// 获取简短摘要
        /// </summary>
        public string GetSummary()
        {
            var status = IsSatisfied ? "✓" : "✗";

            if (IsComposite)
            {
                return string.Format("[{0}] {1} ({2}/{3})",
                    status, ConditionType, SatisfiedChildCount, TotalChildCount);
            }

            if (!string.IsNullOrEmpty(ExpectedValue) && !string.IsNullOrEmpty(ActualValue))
            {
                return string.Format("[{0}] {1} (期望: {2}, 实际: {3})",
                    status, Description, ExpectedValue, ActualValue);
            }

            return string.Format("[{0}] {1}", status, Description);
        }
    }

    #endregion

    #region 扩展接口

    /// <summary>
    /// 可评估条件接口
    /// 扩展 ITriggerCondition，提供详细的评估结果
    /// </summary>
    public interface IEvaluatableCondition : ITriggerCondition
    {
        /// <summary>
        /// 评估条件并返回详细结果
        /// </summary>
        /// <param name="context">条件上下文</param>
        /// <returns>评估结果，包含详细信息</returns>
        ConditionEvaluationResult Evaluate(IConditionContext context);
    }

    #endregion

    #region 扩展方法

    /// <summary>
    /// 条件扩展方法
    /// 为所有 ITriggerCondition 提供 Evaluate 能力
    /// </summary>
    public static class ConditionExtensions
    {
        /// <summary>
        /// 评估条件并返回详细结果
        /// </summary>
        /// <param name="condition">要评估的条件</param>
        /// <param name="context">条件上下文</param>
        /// <returns>评估结果</returns>
        public static ConditionEvaluationResult Evaluate(
            this ITriggerCondition condition,
            IConditionContext context)
        {
            if (condition == null)
            {
                return new ConditionEvaluationResult
                {
                    Description = "(null)",
                    ConditionType = "Null",
                    IsSatisfied = false,
                    ExpectedValue = "N/A",
                    ActualValue = "N/A"
                };
            }

            // 如果条件实现了 IEvaluatableCondition，使用其实现
            if (condition is IEvaluatableCondition evaluatable)
            {
                return evaluatable.Evaluate(context);
            }

            // 否则返回基础结果（兼容未修改的条件类）
            return new ConditionEvaluationResult
            {
                Description = condition.Description,
                ConditionType = condition.GetType().Name.Replace("Condition", ""),
                IsSatisfied = condition.IsSatisfied(context),
                ExpectedValue = "N/A",
                ActualValue = "N/A"
            };
        }
    }

    #endregion

    #region 诊断配置

    /// <summary>
    /// 诊断日志级别
    /// </summary>
    public enum DiagnosticsLogLevel
    {
        /// <summary>关闭诊断</summary>
        Off,
        /// <summary>仅输出触发的 Handler</summary>
        Triggered,
        /// <summary>输出所有 Handler（包括未触发的）</summary>
        All,
        /// <summary>详细模式（展开所有子条件）</summary>
        Verbose
    }

    /// <summary>
    /// 条件诊断配置
    /// </summary>
    public class ConditionDiagnosticsConfig
    {
        /// <summary>
        /// 是否启用条件诊断
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 诊断日志级别
        /// </summary>
        public DiagnosticsLogLevel LogLevel { get; set; } = DiagnosticsLogLevel.Triggered;

        /// <summary>
        /// 是否展开复合条件的子条件
        /// </summary>
        public bool ExpandCompositeConditions { get; set; } = true;

        /// <summary>
        /// 是否高亮显示阻塞原因（第一个不满足的条件）
        /// </summary>
        public bool HighlightBlockingCondition { get; set; } = true;

        /// <summary>
        /// 是否高亮显示触发原因（Or 条件中第一个满足的）
        /// </summary>
        public bool HighlightTriggerReason { get; set; } = true;

        /// <summary>
        /// 默认配置（仅输出触发的 Handler）
        /// </summary>
        public static ConditionDiagnosticsConfig Default => new ConditionDiagnosticsConfig();

        /// <summary>
        /// 调试配置（输出所有 Handler 的详细信息）
        /// </summary>
        public static ConditionDiagnosticsConfig Debug => new ConditionDiagnosticsConfig
        {
            Enabled = true,
            LogLevel = DiagnosticsLogLevel.All,
            ExpandCompositeConditions = true,
            HighlightBlockingCondition = true,
            HighlightTriggerReason = true
        };

        /// <summary>
        /// 详细配置（最详细的输出）
        /// </summary>
        public static ConditionDiagnosticsConfig Verbose => new ConditionDiagnosticsConfig
        {
            Enabled = true,
            LogLevel = DiagnosticsLogLevel.Verbose,
            ExpandCompositeConditions = true,
            HighlightBlockingCondition = true,
            HighlightTriggerReason = true
        };
    }

    #endregion

    #region 诊断格式化工具

    /// <summary>
    /// 条件诊断格式化工具
    /// 将评估结果格式化为可读的日志字符串
    /// </summary>
    public static class ConditionDiagnosticsFormatter
    {
        /// <summary>
        /// 格式化单个 Handler 的条件评估结果
        /// </summary>
        /// <param name="handlerId">Handler ID</param>
        /// <param name="handlerName">Handler 名称</param>
        /// <param name="result">评估结果</param>
        /// <param name="config">诊断配置</param>
        /// <returns>格式化的字符串</returns>
        public static string Format(
            string handlerId,
            string handlerName,
            ConditionEvaluationResult result,
            ConditionDiagnosticsConfig config = null)
        {
            config = config ?? ConditionDiagnosticsConfig.Default;
            var sb = new StringBuilder();

            var status = result.IsSatisfied ? "✓ 触发" : "✗ 未触发";
            sb.AppendFormat("[{0}] {1} ({2})", handlerId, handlerName, status);
            sb.AppendLine();

            if (config.ExpandCompositeConditions)
            {
                FormatResultTree(sb, result, "  ", config, isLast: true, isRoot: true);
            }
            else
            {
                sb.AppendFormat("  条件: {0}", result.Description);
                sb.AppendLine();
                sb.AppendFormat("  结果: {0}", result.GetSummary());
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// 格式化多个 Handler 的条件评估结果（批量输出）
        /// </summary>
        public static string FormatAll(
            IEnumerable<HandlerEvaluationInfo> evaluations,
            ConditionDiagnosticsConfig config = null)
        {
            config = config ?? ConditionDiagnosticsConfig.Default;
            var sb = new StringBuilder();

            sb.AppendLine("========== Handler 条件评估 ==========");

            var triggered = evaluations.Where(e => e.Result.IsSatisfied).ToList();
            var notTriggered = evaluations.Where(e => !e.Result.IsSatisfied).ToList();

            if (triggered.Any())
            {
                sb.AppendLine("【将触发】");
                foreach (var eval in triggered)
                {
                    sb.Append(Format(eval.HandlerId, eval.HandlerName, eval.Result, config));
                    sb.AppendLine();
                }
            }

            if (config.LogLevel >= DiagnosticsLogLevel.All && notTriggered.Any())
            {
                sb.AppendLine("【未触发】");
                foreach (var eval in notTriggered)
                {
                    sb.Append(Format(eval.HandlerId, eval.HandlerName, eval.Result, config));
                    sb.AppendLine();
                }
            }

            sb.AppendLine("======================================");
            return sb.ToString();
        }

        /// <summary>
        /// 递归格式化条件树
        /// </summary>
        private static void FormatResultTree(
            StringBuilder sb,
            ConditionEvaluationResult result,
            string indent,
            ConditionDiagnosticsConfig config,
            bool isLast,
            bool isRoot = false)
        {
            // 绘制树形结构
            string prefix;
            string childIndent;

            if (isRoot)
            {
                prefix = "";
                childIndent = indent;
            }
            else
            {
                prefix = isLast ? "└─ " : "├─ ";
                childIndent = indent + (isLast ? "   " : "│  ");
            }

            // 状态图标
            var statusIcon = result.IsSatisfied ? "✓" : "✗";

            // 构建行内容
            var lineBuilder = new StringBuilder();
            lineBuilder.AppendFormat("{0}{1}[{2}] ", indent, prefix, statusIcon);

            if (result.IsComposite)
            {
                // 复合条件
                lineBuilder.AppendFormat("{0} ({1}/{2} 满足)",
                    result.ConditionType,
                    result.SatisfiedChildCount,
                    result.TotalChildCount);
            }
            else
            {
                // 简单条件
                lineBuilder.Append(result.Description);
            }

            // 添加注解
            var annotation = GetAnnotation(result, config);
            if (!string.IsNullOrEmpty(annotation))
            {
                lineBuilder.AppendFormat("  {0}", annotation);
            }

            sb.AppendLine(lineBuilder.ToString());

            // 递归处理子条件
            if (result.IsComposite && config.ExpandCompositeConditions)
            {
                for (int i = 0; i < result.Children.Count; i++)
                {
                    var child = result.Children[i];
                    var isChildLast = (i == result.Children.Count - 1);
                    FormatResultTree(sb, child, childIndent, config, isChildLast);
                }
            }
            // 非复合条件，显示期望值和实际值
            else if (!result.IsComposite &&
                     !string.IsNullOrEmpty(result.ExpectedValue) &&
                     result.ExpectedValue != "N/A")
            {
                sb.AppendFormat("{0}      期望: {1}, 实际: {2}",
                    childIndent, result.ExpectedValue, result.ActualValue);
                sb.AppendLine();
            }
        }

        /// <summary>
        /// 获取条件注解（阻塞原因、触发原因等）
        /// </summary>
        private static string GetAnnotation(
            ConditionEvaluationResult result,
            ConditionDiagnosticsConfig config)
        {
            if (!result.IsSatisfied && config.HighlightBlockingCondition)
            {
                // 不满足的条件，可能是阻塞原因
                if (!result.IsComposite)
                {
                    return "← 阻塞";
                }
            }

            if (result.IsSatisfied && config.HighlightTriggerReason)
            {
                // Or 条件中第一个满足的子条件
                if (result.ConditionType == "Or" && result.Children != null)
                {
                    var firstSatisfied = result.Children.FirstOrDefault(c => c.IsSatisfied);
                    if (firstSatisfied != null)
                    {
                        // 注解会在子条件上显示
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 格式化简洁的单行摘要
        /// </summary>
        public static string FormatOneLine(
            string handlerId,
            ConditionEvaluationResult result)
        {
            var status = result.IsSatisfied ? "✓" : "✗";

            if (!result.IsSatisfied && !result.IsComposite)
            {
                return string.Format("[{0}] [{1}] {2} (期望: {3}, 实际: {4})",
                    handlerId, status, result.Description,
                    result.ExpectedValue, result.ActualValue);
            }

            if (result.IsComposite)
            {
                return string.Format("[{0}] [{1}] {2} ({3}/{4} 满足)",
                    handlerId, status, result.ConditionType,
                    result.SatisfiedChildCount, result.TotalChildCount);
            }

            return string.Format("[{0}] [{1}] {2}", handlerId, status, result.Description);
        }
    }

    /// <summary>
    /// Handler 评估信息
    /// </summary>
    public class HandlerEvaluationInfo
    {
        public string HandlerId { get; set; }
        public string HandlerName { get; set; }
        public ConditionEvaluationResult Result { get; set; }
    }

    #endregion
}