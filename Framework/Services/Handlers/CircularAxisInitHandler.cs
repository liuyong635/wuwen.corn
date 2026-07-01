using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// 环形轴初始化处理器（重构版）
    /// 
    /// 触发方式：UI 初始化按钮
    /// 
    /// 信号定义：
    /// - M1002.0 (CircularAxis_Init)           - 发送初始化脉冲
    /// - M1002.7 (CircularAxis_InitInProgress) - 初始化中标志 (1=进行中, 0=完成)
    /// - M1003.0 (CircularAxis_InitTimeout)    - 初始化超时标志 (1=超时, 需手动复位)
    /// 
    /// 执行流程：
    /// 1. 检查 M1003.0 是否存在未清除的超时标志
    /// 2. 发送初始化脉冲到 M1002.0
    /// 3. 等待 M1002.7 变为 0（初始化完成）
    /// 4. 监控 M1003.0，如果变为 1 则触发超时报警弹窗
    /// 
    /// 触发条件：
    /// - CircularAxisInit_Request = true（初始化按钮触发）
    /// - CircularAxisInit_Busy = false
    /// 
    /// 依赖设备：PLC
    /// </summary>
    public class CircularAxisInitHandler : SignalHandlerBase
    {
        #region 常量定义

        // ========== PLC 信号定义 ==========
        /// <summary>
        /// M1002.0 - 环形导轨轴初始化（脉冲触发）
        /// </summary>
        private const string SIG_INIT_TRIGGER = "CircularAxis_Init";

        /// <summary>
        /// M1002.7 - 环形导轨轴初始化中 (1=进行中, 0=完成)
        /// </summary>
        private const string SIG_INIT_IN_PROGRESS = "CircularAxis_InitInProgress";

        /// <summary>
        /// M1003.0 - 环形导轨轴初始化超时 (1=超时, 需手动复位)
        /// </summary>
        private const string SIG_INIT_TIMEOUT = "CircularAxis_InitTimeout";

        // ========== 超时配置 ==========
        /// <summary>
        /// 软件层面的初始化超时时间
        /// 应略大于PLC的超时时间，作为兜底保护
        /// </summary>
        private static readonly TimeSpan INIT_TIMEOUT = TimeSpan.FromSeconds(60);

        /// <summary>
        /// 脉冲持续时间（毫秒）
        /// </summary>
        private const int PULSE_DURATION_MS = 500;

        /// <summary>
        /// 轮询间隔（毫秒）
        /// </summary>
        private const int POLL_INTERVAL_MS = 100;

        #endregion

        #region Handler属性

        public override string HandlerId => "CircularAxisInit";

        public override string HandlerName => "环形轴初始化";

        /// <summary>
        /// 优先级：95
        /// </summary>
        public override int Priority => 95;

        public override string[] DependentDevices => new[] { "PLC" };

        /// <summary>
        /// 触发条件：
        /// - CircularAxisInit_Request = true（初始化按钮）
        /// - CircularAxisInit_Busy = false
        /// </summary>
        public override ITriggerCondition TriggerCondition => When.All(
            When.FlagOn("CircularAxisInit_Request"),
            When.FlagOff("CircularAxisInit_Busy")
        );

        #endregion

        #region 执行逻辑

        protected override async Task<ValueTuple<bool, string>> ExecuteAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            // 设置忙碌标志
            SetBusy(true);
            FlagCondition.SetFlag("CircularAxisInit_Busy", true);

            // 清除请求标志，防止重复触发
            FlagCondition.SetFlag("CircularAxisInit_Request", false);

            try
            {
                

                LogInfo("========== 环形轴初始化开始 ==========");

                // ========== 步骤1: 检查超时标志 ==========
                if (ReadSignal(ctx, SIG_INIT_TIMEOUT))
                {
                    var msg = "检测到初始化超时标志(M1003.0=1)未清除，请先手动复位PLC";
                    LogWarning(msg);
                    return Fail(msg);
                }

                // ========== 步骤2: 发送初始化脉冲 ==========
                LogInfo("发送初始化脉冲 M1002.0");

                WriteSignal(ctx, SIG_INIT_TRIGGER, true);

                LogDebug("M1002.0 = true (脉冲开始)");

                await Task.Delay(PULSE_DURATION_MS, ct);

                WriteSignal(ctx, SIG_INIT_TRIGGER, false);

                LogDebug("M1002.0 = false (脉冲结束)");

                // ========== 步骤3: 等待初始化完成 ==========
                LogInfo("等待初始化完成 (M1002.7=0)...");

                var waitResult = await WaitForInitCompletionAsync(ctx, ct);

                if (waitResult.Success)
                {
                    LogInfo("========== 环形轴初始化完成 ✓ ==========");
                    return Success("环形轴初始化完成");
                }
                else
                {
                    // 触发报警弹窗
                    LogError(null, waitResult.Message);
                    return Fail(waitResult.Message);
                }
            }
            catch (OperationCanceledException)
            {
                // 取消时确保信号复位
                WriteSignal(ctx, SIG_INIT_TRIGGER, false);
                LogWarning("环形轴初始化被取消");
                throw;
            }
            catch (Exception ex)
            {
                // 异常时确保信号复位
                WriteSignal(ctx, SIG_INIT_TRIGGER, false);

                LogError(ex, "环形轴初始化异常");
                return Fail("初始化异常: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
                FlagCondition.SetFlag("CircularAxisInit_Busy", false);
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 等待初始化完成或超时
        /// </summary>
        /// <returns>(Success, Message)</returns>
        private async Task<(bool Success, string Message)> WaitForInitCompletionAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            var startTime = DateTime.Now;
            var lastLogTime = DateTime.Now;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // ========== 检查PLC超时信号 M1003.0 ==========
                    if (ReadSignal(ctx, SIG_INIT_TIMEOUT))
                    {
                        return (false, "PLC报告初始化超时 (M1003.0=1)，请检查环形轴状态并手动复位");
                    }

                    // ========== 检查初始化中信号 M1002.7 ==========
                    // M1002.7 = 1 表示正在初始化
                    // M1002.7 = 0 表示初始化完成
                    var inProgress = ReadSignal(ctx, SIG_INIT_IN_PROGRESS);

                    if (!inProgress)
                    {
                        // 初始化完成
                        var elapsed = DateTime.Now - startTime;
                        LogInfo("初始化耗时: {0:F1} 秒", elapsed.TotalSeconds);
                        return (true, "初始化完成");
                    }

                    // 定期输出进度日志（每5秒）
                    if (DateTime.Now - lastLogTime > TimeSpan.FromSeconds(5))
                    {
                        var elapsed = DateTime.Now - startTime;
                        LogDebug("初始化进行中... 已等待 {0:F0} 秒", elapsed.TotalSeconds);
                        lastLogTime = DateTime.Now;
                    }
                }
                catch (Exception ex)
                {
                    LogWarning("读取PLC信号异常: {0}", ex.Message);
                    // 继续重试，不立即失败
                }

                // ========== 检查软件超时 ==========
                if (DateTime.Now - startTime > INIT_TIMEOUT)
                {
                    return (false, string.Format("软件层等待初始化完成超时 ({0}秒)", INIT_TIMEOUT.TotalSeconds));
                }

                await Task.Delay(POLL_INTERVAL_MS, ct);
            }

            return (false, "操作被取消");
        }

        

        #endregion
    }
}