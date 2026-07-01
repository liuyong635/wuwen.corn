using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// 振动盘策略处理器
    /// 
    /// ═══════════════════════════════════════════════════════════
    /// 重构说明（方案一：C#集中控制 + AR纯执行）：
    ///   本 Handler 从原 DiskVisionHandler 中拆分而来，
    ///   只负责振动/抖料/排料策略，不再包含视觉检测逻辑。
    ///   
    ///   振动策略逻辑与原 DiskVisionHandler 完全一致：
    ///   - 第1次无料：只振动
    ///   - 第2次无料 + 白占比低：振动+抖料
    ///   - 第2次无料 + 白占比高：只振动
    ///   - 空振达阈值 + 白占比高：排料
    ///   - 空振达阈值 + 白占比低：重置计数器
    ///   - 白占比极低：强制振动+抖料
    ///   - 总重试超限：停机报警
    /// ═══════════════════════════════════════════════════════════
    /// 
    /// 触发条件：
    /// - 系统运行中
    /// - NeedVibrate 标志（由 VisionCoordinateHandler 设置）
    /// - 自身非忙碌
    /// 
    /// 完成后：设置 Vibrate_Complete 标志 → 触发 VisionCoordinateHandler 重新检测
    /// 
    /// 依赖设备：Vibrator
    /// </summary>
    public class DiskVibrateHandler : SignalHandlerBase
    {
        #region 成员变量

        /// <summary>振动计数器：0=首次无料，1=已振动一次</summary>
        private int _vibrateCounter = 0;

        /// <summary>空振计数器：连续无料且执行振动/抖料的累计次数</summary>
        private int _emptyCounter = 0;

        /// <summary>总连续无料重试次数</summary>
        private int _totalRetryCounter = 0;

        /// <summary>是否正在排料</summary>
        private bool _isDraining = false;

        #endregion

        #region Handler 属性

        public override string HandlerId => "DiskVibrate";
        public override string HandlerName => "振动盘策略";
        public override int Priority => 70;
        public override string[] DependentDevices => new[] { "Vibrator" };

        public override ITriggerCondition TriggerCondition => When.All(
            When.IsRunning(),
            When.FlagOn("NeedVibrate"),
            When.FlagOff("DiskVibrate_Busy")
        );

        #endregion

        #region 主执行逻辑

        protected override async Task<ValueTuple<bool, string>> ExecuteAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            SetBusy(true);
            FlagCondition.SetFlag("DiskVibrate_Busy", true);

            try
            {
                // ============ 1. 清除触发标志 ============
                FlagCondition.SetFlag("NeedVibrate", false);

                // ============ 2. 检查重置信号 ============
                // VisionCoordinateHandler 在有料时会推送 "RESET" 信号
                var resetSignal = ctx.DataFlow.Get<string>("VibrateResetSignal");
                if (resetSignal != null)
                {
                    string resetVal;
                    while (resetSignal.TryPop(out resetVal))
                    {
                        if (resetVal == "RESET")
                        {
                            _vibrateCounter = 0;
                            _emptyCounter = 0;
                            _totalRetryCounter = 0;
                            LogInfo("收到重置信号，所有计数器已清零");
                        }
                    }
                }

                // ============ 3. 获取设备 ============
                var vibrator = ctx.GetVibrator();

                if (vibrator == null || !vibrator.IsConnected)
                {
                    // ★ 修复：设备异常也设 Vibrate_Complete，防止链路断裂
                    FlagCondition.SetFlag("Vibrate_Complete", true);
                    return Fail("振动盘设备未连接");
                }

                // ============ 4. 读取 WhiteAreaRatio ============
                float whiteAreaRatio = 0f;
                var ratioData = ctx.DataFlow.Get<float>("WhiteAreaRatio");
                if (ratioData != null)
                {
                    ratioData.TryPop(out whiteAreaRatio);
                }

                // ============ 5. 读配方参数 ============
                var recipe = ctx.GetService<IRecipeService>();

                int vibrationDuration = recipe?.GetInt("Vibrator", "VibrationDuration", 3000,
                    name: "振动持续时间", unit: "ms", description: "振动1（打散振动）持续时间") ?? 3000;
                int stabilizeDelay = recipe?.GetInt("Vibrator", "StabilizeDelay", 300,
                    name: "稳定等待时间", unit: "ms", description: "振动/抖料后稳定等待时间") ?? 300;

                double whiteAreaThreshold = recipe?.GetDouble("VibrationTray", "WhiteAreaThreshold", 50.0,
                    name: "白色占比阈值", unit: "%", description: "超过此值判定为堆积") ?? 50.0;
                int emptyVibrateCount = recipe?.GetInt("VibrationTray", "EmptyVibrateCount", 5,
                    name: "空振次数阈值", unit: "次", description: "连续空振达到此次数触发排料判断") ?? 5;
                double drainDuration = recipe?.GetDouble("VibrationTray", "DrainDuration", 5.0,
                    name: "排料持续时间", unit: "秒", description: "排料振动2运行时间") ?? 5.0;
                double baffleOpenDelay = recipe?.GetDouble("VibrationTray", "BaffleOpenDelay", 0.5,
                    name: "挡料板打开延迟", unit: "秒") ?? 0.5;
                double baffleCloseDelay = recipe?.GetDouble("VibrationTray", "BaffleCloseDelay", 0.5,
                    name: "挡料板关闭延迟", unit: "秒") ?? 0.5;
                int maxTotalRetry = recipe?.GetInt("VibrationTray", "MaxTotalRetry", 20,
                    name: "最大总重试次数", unit: "次", description: "连续无料总重试上限（0=不限制）") ?? 20;
                double whiteForceFeedThreshold = recipe?.GetDouble("VibrationTray", "WhiteForceFeedThreshold", 8.0,
                    name: "强制抖料阈值", unit: "%", description: "白色占比低于此值时强制振动+抖料（0=禁用）") ?? 8.0;

                LogInfo("无料处理: 白色占比={0:F2}%, 振动计数={1}, 空振计数={2}/{3}, 总重试={4}/{5}",
                    whiteAreaRatio, _vibrateCounter, _emptyCounter, emptyVibrateCount,
                    _totalRetryCounter, maxTotalRetry > 0 ? maxTotalRetry.ToString() : "∞");

                // ============ 6. 振动策略状态机 ============

                // 6.0 总重试次数检查
                _totalRetryCounter++;
                LogInfo("总重试计数器: {0}/{1}", _totalRetryCounter,
                    maxTotalRetry > 0 ? maxTotalRetry.ToString() : "∞");

                if (maxTotalRetry > 0 && _totalRetryCounter >= maxTotalRetry)
                {
                    LogWarning("连续无料达到最大重试次数 ({0})，停机报警！", maxTotalRetry);
                    _totalRetryCounter = 0;
                    _vibrateCounter = 0;
                    _emptyCounter = 0;

                    // 即使报警，也设置完成标志让系统可以恢复
                    FlagCondition.SetFlag("Vibrate_Complete", true);
                    return Fail("连续无料超限，请检查料仓");
                }

                // 6.1 排料判断（空振次数达到阈值）
                if (_emptyCounter >= emptyVibrateCount)
                {
                    LogWarning("达到空振阈值 ({0}次)，检查白色占比", _emptyCounter);

                    if (whiteAreaRatio > whiteAreaThreshold)
                    {
                        // 白色占比高 → 确认堆积 → 排料
                        LogWarning("白色占比 {0:F2}% > {1}%，触发排料", whiteAreaRatio, whiteAreaThreshold);

                        await ExecuteDrainSequenceAsync(
                            ctx, vibrator,
                            drainDuration, baffleOpenDelay, baffleCloseDelay,
                            vibrationDuration, stabilizeDelay, ct);

                        _vibrateCounter = 0;
                        _emptyCounter = 0;

                        FlagCondition.SetFlag("Vibrate_Complete", true);
                        return Success("排料完成");
                    }
                    else
                    {
                        // 白色占比低 → 非堆积 → 重置空振计数器
                        LogInfo("白色占比 {0:F2}% <= {1}%，无堆积，重置空振计数器", whiteAreaRatio, whiteAreaThreshold);
                        _emptyCounter = 0;
                    }
                }

                // 6.2 振动策略判断
                // 白色占比极低时，强制振动+抖料
                if (whiteForceFeedThreshold > 0 && whiteAreaRatio < whiteForceFeedThreshold)
                {
                    LogWarning("白色占比 {0:F2}% < {1}%，强制振动+抖料", whiteAreaRatio, whiteForceFeedThreshold);
                    await ExecuteVibrateAndFeedAsync(vibrator, vibrationDuration, stabilizeDelay, ct);
                    _vibrateCounter = 0;
                    _emptyCounter++;
                    LogInfo("空振计数器: {0}/{1}", _emptyCounter, emptyVibrateCount);

                    FlagCondition.SetFlag("Vibrate_Complete", true);
                    return Success("强制振动+抖料完成");
                }
                else if (_vibrateCounter == 0)
                {
                    // 第1次无料：只振动
                    LogInfo("第1次无料，只振动（不抖料）");
                    await ExecuteVibrateOnlyAsync(vibrator, vibrationDuration, ct);
                    _vibrateCounter = 1;

                    FlagCondition.SetFlag("Vibrate_Complete", true);
                    return Success("只振动完成");
                }
                else
                {
                    // 第2次及以后：检查白色占比
                    if (whiteAreaRatio > whiteAreaThreshold)
                    {
                        // 白色占比高，只振动
                        LogWarning("白色占比 {0:F2}% > {1}%，只振动不抖料", whiteAreaRatio, whiteAreaThreshold);
                        await ExecuteVibrateOnlyAsync(vibrator, vibrationDuration, ct);
                        _vibrateCounter = 1;
                        _emptyCounter++;
                        LogInfo("空振计数器: {0}/{1}", _emptyCounter, emptyVibrateCount);

                        FlagCondition.SetFlag("Vibrate_Complete", true);
                        return Success("只振动完成（白色占比高）");
                    }
                    else
                    {
                        // 白色占比正常，振动+抖料
                        LogInfo("白色占比 {0:F2}% <= {1}%，振动+抖料", whiteAreaRatio, whiteAreaThreshold);
                        await ExecuteVibrateAndFeedAsync(vibrator, vibrationDuration, stabilizeDelay, ct);
                        _vibrateCounter = 0;
                        _emptyCounter++;
                        LogInfo("空振计数器: {0}/{1}", _emptyCounter, emptyVibrateCount);

                        FlagCondition.SetFlag("Vibrate_Complete", true);
                        return Success("振动+抖料完成");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogError(ex, "振动处理异常");

                // 即使异常也设置完成标志，避免系统卡死
                FlagCondition.SetFlag("Vibrate_Complete", true);
                return Fail("处理异常: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
                FlagCondition.SetFlag("DiskVibrate_Busy", false);
            }
        }

        #endregion

        #region 只振动（不抖料）

        /// <summary>
        /// 只执行振动1（打散振动），不抖料
        /// 迁移自原 DiskVisionHandler.ExecuteVibrateOnlyAsync
        /// </summary>
        private async Task ExecuteVibrateOnlyAsync(
            IVibratorDevice vibrator,
            int vibrationDuration,
            CancellationToken ct)
        {
            LogInfo("开始只振动（不抖料），持续 {0}ms", vibrationDuration);

            await vibrator.StartVibrationAsync(ct);
            await Task.Delay(vibrationDuration, ct);
            await vibrator.StopVibrationAsync(ct);

            LogInfo("只振动完成");
        }

        #endregion

        #region 振动 + 抖料

        /// <summary>
        /// 执行振动1 + 抖料
        /// 迁移自原 DiskVisionHandler.ExecuteVibrateAndFeedAsync
        /// </summary>
        private async Task ExecuteVibrateAndFeedAsync(
            IVibratorDevice vibrator,
            int vibrationDuration,
            int stabilizeDelay,
            CancellationToken ct)
        {
            LogInfo("开始振动+抖料");

            // 1. 启动振动1
            LogInfo("启动振动1，持续 {0}ms", vibrationDuration);
            await vibrator.StartVibrationAsync(ct);

            // 2. 抖料（在振动过程中）
            LogInfo("开始抖料进料");
            await vibrator.StartFeedAsync(ct);
            await Task.Delay(vibrationDuration / 3, ct);
            await vibrator.StopFeedAsync(ct);

            await Task.Delay(vibrationDuration / 3 * 2, ct);
            await vibrator.StopVibrationAsync(ct);

            // 3. 等待稳定
            LogInfo("等待稳定 {0}ms", stabilizeDelay);
            await Task.Delay(stabilizeDelay, ct);

            LogInfo("振动+抖料完成");
        }

        #endregion

        #region 排料流程

        /// <summary>
        /// 执行完整排料流程
        /// 迁移自原 DiskVisionHandler.ExecuteDrainSequenceAsync
        /// 
        /// 步骤：
        /// 1. 打开挡料板
        /// 2. 延迟等待
        /// 3. 启动振动2（排料振动）
        /// 4. 持续排料
        /// 5. 停止振动
        /// 6. 关闭挡料板
        /// 7. 延迟等待
        /// 8. 振动+抖料补充
        /// </summary>
        private async Task ExecuteDrainSequenceAsync(
            IHandlerContext ctx,
            IVibratorDevice vibrator,
            double drainDuration,
            double baffleOpenDelay,
            double baffleCloseDelay,
            int vibrationDuration,
            int stabilizeDelay,
            CancellationToken ct)
        {
            LogWarning("========== 开始排料流程 ==========");
            _isDraining = true;

            try
            {
                // 1. 打开挡料板
                LogInfo("1. 打开挡料板");
                WriteSignal(ctx, "Vibrator_PourDoor", true);

                // 2. 延迟
                LogInfo("2. 延迟 {0}s", baffleOpenDelay);
                await Task.Delay((int)(baffleOpenDelay * 1000), ct);

                // 3. 启动振动2
                LogInfo("3. 启动振动2");
                await vibrator.StartVibrationGroup2Async(ct);

                // 4. 持续排料
                LogInfo("4. 持续排料 {0}s", drainDuration);
                await Task.Delay((int)(drainDuration * 1000), ct);

                // 5. 停止振动
                LogInfo("5. 停止振动");
                await vibrator.StopVibrationAsync(ct);

                // 6. 关闭挡料板
                LogInfo("6. 关闭挡料板");
                WriteSignal(ctx, "Vibrator_PourDoor", false);

                // 7. 延迟
                LogInfo("7. 延迟 {0}s", baffleCloseDelay);
                await Task.Delay((int)(baffleCloseDelay * 1000), ct);

                // 8. 振动+抖料补充
                LogInfo("8. 振动+抖料补充");
                await ExecuteVibrateAndFeedAsync(vibrator, vibrationDuration, stabilizeDelay, ct);

                LogWarning("========== 排料流程完成 ==========");
            }
            finally
            {
                _isDraining = false;
            }
        }

        #endregion
    }
}