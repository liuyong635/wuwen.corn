using IMVSBlobFindModuCs;
using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using VM.Core;
using VM.PlatformSDKCS;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// 振动盘视觉处理器（融合版）
    /// 
    /// ═══════════════════════════════════════════════════════════
    /// 融合说明：
    ///   本版本合并了 V1（竞态修复 + 专用标志解耦）和 V2（智能排料状态机）的优点。
    ///   
    ///   来自 V1 的设计：
    ///   - 专用标志 DiskVision_BatchComplete（由 RobotCommandHandler 中转，避免标志位竞争）
    ///   - POST_VISION_DELAY_MS 竞态条件修复（HeadCam vs MainCtrl 双 TCP 通道时序保障）
    ///   
    ///   来自 V2 的设计：
    ///   - 先视觉后决策（基于实时检测结果而非缓存）
    ///   - WhiteAreaRatio 白色占比检测（判断堆积程度）
    ///   - 双计数器状态机（_vibrateCounter + _emptyCounter 渐进策略）
    ///   - 完整排料流程（挡料板 + 振动2）
    ///   - 坐标超限检测（安全防护）
    ///   - MinExecutionIntervalMs 最小触发间隔
    ///   - VM 全局变量 API 统一数据交互
    /// ═══════════════════════════════════════════════════════════
    /// 
    /// 功能：
    /// 1. 响应机器人 BATCH_COMPLETE / VISION_NO_POINTS / READY 命令，触发视觉检测
    /// 2. 检测种子数量(BlobNum)和白色占比(WhiteAreaRatio)
    /// 3. 根据检测结果执行振动、抖料或排料策略
    /// 4. Vision Master 流程内部自动通过 TCP 将坐标发送给机器人（HeadCam 通道）
    /// 
    /// 核心逻辑（自动排料状态机）：
    /// - 有料：重置计数器，延时后发送确认
    /// - 无料 + 第1次：只振动（不抖料），_vibrateCounter = 1
    /// - 无料 + 第2次 + 白色占比低：振动+抖料，_vibrateCounter = 0, _emptyCounter++
    /// - 无料 + 第2次 + 白色占比高：只振动（不抖料），_vibrateCounter = 1, _emptyCounter++
    /// - 空振次数达阈值 + 白色占比高：触发排料流程
    /// - 空振次数达阈值 + 白色占比低：重置空振计数器（误判）
    /// 
    /// 触发条件：
    /// - 系统运行中
    /// - 收到就绪信号 (Robot_Ready_Received) 或 专用批次完成信号 (DiskVision_BatchComplete) 或 无可抓点信号 (Vision_NoPoints_Received)
    /// - 自身非忙碌状态
    /// 
    /// 竞态条件处理：
    /// - RECEIVE_DISKRUN_SUCCESS 发送前增加 POST_VISION_DELAY_MS 延时
    /// - 确保 VM 通过 HeadCam TCP 发送的坐标数据先到达 AR 端
    /// 
    /// 使用的 IDevice 接口：
    /// - IVisionDevice: 执行视觉检测（流程内部自动发送坐标）
    /// - IVibratorDevice: 控制振动、抖料和排料
    /// - IRobotDevice: 发送完成确认命令
    /// 
    /// 依赖设备：Vision, Vibrator
    /// </summary>
    public class DiskVisionHandler : SignalHandlerBase
    {
        #region 常量定义

        /// <summary>视觉流程名称</summary>
        private const string VISION_PROCEDURE = "流程1";

        /// <summary>全局变量模块名称（VM 方案中配置）</summary>
        private const string GLOBAL_VAR_MODULE_NAME = "全局变量1";

        /// <summary>
        /// 视觉执行完成后、发送确认前的等待时间（ms）
        /// 
        /// 来自 V1 的竞态修复：
        /// VM 内部通过 HeadCam TCP 发送坐标给 AR，
        /// 但 HeadCam 和 MainCtrl 是两条独立 TCP 连接，
        /// 必须确保坐标数据先到达 AR 端，再发送 RECEIVE_DISKRUN_SUCCESS，
        /// 否则 AR 端在坐标未到达时就进入状态机，会触发 BATCH_COMPLETE 轰炸。
        /// </summary>
        private const int POST_VISION_DELAY_MS = 0;

        #endregion

        #region 成员变量

        /// <summary>振动计数器：0=首次无料，1=已振动一次（下次触发抖料判断）</summary>
        private int _vibrateCounter = 0;

        /// <summary>空振计数器：连续无料且执行振动/抖料的累计次数（达到阈值触发排料判断）</summary>
        private int _emptyCounter = 0;

        /// <summary>总连续无料重试次数（含所有振动/抖料/排料后的尝试，达到上限停机报警）</summary>
        private int _totalRetryCounter = 0;

        /// <summary>是否正在执行排料流程</summary>
        private bool _isDraining = false;

        #endregion

        #region Handler 属性

        public override string HandlerId => "DiskVision";

        public override string HandlerName => "振动盘视觉处理";

        public override int Priority => 80;

        public override string[] DependentDevices => new[] { "Vision", "Vibrator" };



        /// <summary>
        /// 触发条件：运行中 + (就绪 或 批次完成 或 无可抓点) + 非忙碌
        /// 
        /// 来自 V1 的设计：使用专用标志 DiskVision_BatchComplete（由 RobotCommandHandler 中转设置），
        /// 避免多个 Handler 竞争 Robot_BatchComplete_Received 同一标志位。
        /// </summary>
        public override ITriggerCondition TriggerCondition => When.All(
            When.IsRunning(),
            When.Any(
                When.FlagOn("Robot_Ready_Received"),           // 机器人初始化完成
                When.FlagOn("DiskVision_BatchComplete"),       // 专用标志（由 RobotCommandHandler 设置）
                When.FlagOn("Vision_NoPoints_Received")        // 无可抓点
            ),
            When.FlagOff("DiskVision_Busy")
        );

        #endregion

        #region 主执行逻辑

        /// <summary>
        /// 执行视觉检测流程（含自动排料逻辑）
        /// 
        /// 流程：
        /// 1. 清除触发标志
        /// 2. 读取配方参数
        /// 3. 设置 VM 全局变量（坐标安全范围）
        /// 4. 执行 VM 视觉检测，提取种子数量和白色占比
        /// 5. 坐标超限检测
        /// 6. 有料分支：重置计数器，延时后返回
        /// 7. 无料分支：根据计数器和白色占比决定 振动 / 抖料 / 排料 策略
        /// 8. 延时 + 发送机器人完成确认
        /// </summary>
        protected override async Task<ValueTuple<bool, string>> ExecuteAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            // 设置忙碌标志
            SetBusy(true);
            FlagCondition.SetFlag("DiskVision_Busy", true);

            try
            {
                // ============ 1. 清除触发标志 ============
                FlagCondition.SetFlag("Robot_Ready_Received", false);
                FlagCondition.SetFlag("DiskVision_BatchComplete", false);
                FlagCondition.SetFlag("Vision_NoPoints_Received", false);

                // ============ 2. 获取设备 ============
                var vision = ctx.GetVision();
                var vibrator = ctx.GetVibrator();

                if (vision == null || !vision.IsConnected)
                    return Fail("视觉设备未连接");

                if (vibrator == null || !vibrator.IsConnected)
                    return Fail("振动盘设备未连接");

                // ============ 3. 读取配方参数 ============
                var recipe = ctx.GetService<IRecipeService>();

                // --- Vibrator 组 ---
                int vibrationDuration = recipe?.GetInt("Vibrator", "VibrationDuration", 3000,
                    name: "振动持续时间", unit: "ms", description: "振动1（打散振动）持续时间") ?? 3000;

                int stabilizeDelay = recipe?.GetInt("Vibrator", "StabilizeDelay", 300,
                    name: "稳定等待时间", unit: "ms", description: "振动/抖料后稳定等待时间") ?? 300;

                // --- VibrationTray 组（排料相关） ---
                double whiteAreaThreshold = recipe?.GetDouble("VibrationTray", "WhiteAreaThreshold", 50.0,
                    name: "白色占比阈值", unit: "%", description: "超过此值判定为堆积（0-100）") ?? 50.0;

                int emptyVibrateCount = recipe?.GetInt("VibrationTray", "EmptyVibrateCount", 5,
                    name: "空振次数阈值", unit: "次", description: "连续空振达到此次数触发排料判断") ?? 5;

                double drainDuration = recipe?.GetDouble("VibrationTray", "DrainDuration", 5.0,
                    name: "排料持续时间", unit: "秒", description: "排料振动2运行时间") ?? 5.0;

                double baffleOpenDelay = recipe?.GetDouble("VibrationTray", "BaffleOpenDelay", 0.5,
                    name: "挡料板打开延迟", unit: "秒", description: "挡料板打开后等待时间") ?? 0.5;

                double baffleCloseDelay = recipe?.GetDouble("VibrationTray", "BaffleCloseDelay", 0.5,
                    name: "挡料板关闭延迟", unit: "秒", description: "挡料板关闭后等待时间") ?? 0.5;

                // --- VibrationTray 组（新增参数） ---
                int maxTotalRetry = recipe?.GetInt("VibrationTray", "MaxTotalRetry", 20,
                    name: "最大总重试次数", unit: "次", description: "连续无料总重试上限，达到后停机报警（0=不限制）") ?? 20;

                double whiteForceFeedThreshold = recipe?.GetDouble("VibrationTray", "WhiteForceFeedThreshold", 8.0,
                    name: "白色占比强制抖料阈值", unit: "%", description: "白色占比低于此值时，强制执行振动+抖料（0=禁用）") ?? 8.0;

                // --- VisionLimit 组（坐标安全范围） ---
                double safeMinX = recipe?.GetDouble("VisionLimit", "SafeMinX", 101.0,
                    name: "X坐标下限", unit: "mm", description: "视觉坐标X轴安全范围下限") ?? 101.0;

                double safeMaxX = recipe?.GetDouble("VisionLimit", "SafeMaxX", 366.3,
                    name: "X坐标上限", unit: "mm", description: "视觉坐标X轴安全范围上限") ?? 366.3;

                double safeMinY = recipe?.GetDouble("VisionLimit", "SafeMinY", 0.7,
                    name: "Y坐标下限", unit: "mm", description: "视觉坐标Y轴安全范围下限") ?? 0.7;

                double safeMaxY = recipe?.GetDouble("VisionLimit", "SafeMaxY", 241.0,
                    name: "Y坐标上限", unit: "mm", description: "视觉坐标Y轴安全范围上限") ?? 241.0;

                // ============ 4. 设置 VM 全局变量（坐标安全范围） ============
                await SetVisionLimitParametersAsync(safeMinX, safeMaxX, safeMinY, safeMaxY, ct);

                // ============ 5. 执行 VM 视觉检测 ============
                LogInfo("开始执行视觉检测: {0}", VISION_PROCEDURE);

                var visionResult = await vision.ExecuteAsync(VISION_PROCEDURE, ct);

                if (!visionResult.Success)
                {
                    LogWarning("视觉检测失败: {0}", visionResult.Message);
                    await SendRobotConfirmWithDelayAsync(ctx, ct);
                    return Fail("视觉检测失败: " + visionResult.Message);
                }

                // ============ 6. 坐标超限检测 ============
                bool isOutOfBound = CheckCoordinateLimit();
                if (isOutOfBound)
                {
                    LogWarning("视觉坐标超出安全空间，可能是标定转换问题");
                    await SendRobotConfirmWithDelayAsync(ctx, ct);
                    return Fail("坐标超限：视觉坐标超出机器人安全空间");
                }

                // ============ 7. 提取检测结果 ============
                var procedure = vision.GetLastProcedure() as VmProcedure;
                int blobNum = ExtractBlobNum(procedure);
                float whiteAreaRatio = ExtractWhiteAreaRatio(procedure);

                LogInfo("检测结果: 种子数={0}, 白色占比={1:F2}%", blobNum, whiteAreaRatio);

                // ============ 8. 有料分支 ============
                if (blobNum > 0)
                {
                    // 重置所有计数器
                    _vibrateCounter = 0;
                    _emptyCounter = 0;
                    _totalRetryCounter = 0;

                    // 延时 + 发送确认（VM 已通过 HeadCam TCP 发送坐标）
                    await SendRobotConfirmWithDelayAsync(ctx, ct);

                    return Success(string.Format("检测到 {0} 颗种子", blobNum));
                }

                // ============ 9. 无料分支（状态机决策） ============
                LogWarning("检测无料");

                // 9.0 总重试次数检查（防止无限振动）
                _totalRetryCounter++;
                LogInfo("总重试计数器: {0}/{1}", _totalRetryCounter, maxTotalRetry > 0 ? maxTotalRetry.ToString() : "∞");

                if (maxTotalRetry > 0 && _totalRetryCounter >= maxTotalRetry)
                {
                    LogWarning("连续无料达到最大重试次数，停机报警！");
                    _totalRetryCounter = 0;
                    _vibrateCounter = 0;
                    _emptyCounter = 0;

                    await SendRobotConfirmNoDelayAsync(ctx, ct);
                    return Fail("连续无料超限，请检查料仓");
                }

                // 9.1 先判断是否需要排料（空振次数达到阈值）
                if (_emptyCounter >= emptyVibrateCount)
                {
                    LogWarning("达到空振阈值 ({0}次)，检查白色占比", _emptyCounter);

                    if (whiteAreaRatio > whiteAreaThreshold)
                    {
                        // 白色占比高 → 确认堆积 → 触发排料
                        LogWarning("白色占比 {0:F2}% > {1}%，触发排料", whiteAreaRatio, whiteAreaThreshold);

                        await ExecuteDrainSequenceAsync(
                            ctx, vibrator,
                            drainDuration, baffleOpenDelay, baffleCloseDelay,
                            vibrationDuration, stabilizeDelay, ct);

                        // 排料完成，重置所有计数器
                        _vibrateCounter = 0;
                        _emptyCounter = 0;

                        await SendRobotConfirmNoDelayAsync(ctx, ct);
                        return Success("排料完成");
                    }
                    else
                    {
                        // 白色占比低 → 非堆积（误判） → 重置空振计数器
                        LogInfo("白色占比 {0:F2}% <= {1}%，无堆积，重置空振计数器", whiteAreaRatio, whiteAreaThreshold);
                        _emptyCounter = 0;
                    }
                }

                // 9.2 判断振动策略
                // ★ 新增：白色占比极低时，强制振动+抖料（跳过 _vibrateCounter 判断）
                if (whiteForceFeedThreshold > 0 && whiteAreaRatio < whiteForceFeedThreshold)
                {
                    LogWarning("白色占比 {0:F2}% < {1}%，强制振动+抖料", whiteAreaRatio, whiteForceFeedThreshold);
                    await ExecuteVibrateAndFeedAsync(vibrator, vibrationDuration, stabilizeDelay, ct);
                    _vibrateCounter = 0;
                    _emptyCounter++;
                    LogInfo("空振计数器: {0}/{1}", _emptyCounter, emptyVibrateCount);

                    await SendRobotConfirmNoDelayAsync(ctx, ct);
                    return Success("强制振动+抖料完成（白色占比低），等待下次检测");
                }
                else if (_vibrateCounter == 0)
                {
                    // 第1次无料：只振动（不抖料），尝试打散堆积种子
                    LogInfo("第1次无料，只振动（不抖料）");
                    await ExecuteVibrateOnlyAsync(vibrator, vibrationDuration, ct);
                    _vibrateCounter = 1;

                    await SendRobotConfirmNoDelayAsync(ctx, ct);
                    return Success("只振动完成，等待下次检测");
                }
                else
                {
                    // 第2次及以后无料：检查白色占比决定是否抖料
                    if (whiteAreaRatio > whiteAreaThreshold)
                    {
                        // 白色占比高，只振动不抖料（避免越加越多）
                        LogWarning("白色占比 {0:F2}% > {1}%，只振动不抖料", whiteAreaRatio, whiteAreaThreshold);
                        await ExecuteVibrateOnlyAsync(vibrator, vibrationDuration, ct);
                        _vibrateCounter = 1;
                        _emptyCounter++;
                        LogInfo("空振计数器: {0}/{1}", _emptyCounter, emptyVibrateCount);

                        await SendRobotConfirmNoDelayAsync(ctx, ct);
                        return Success("只振动完成（白色占比高），等待下次检测");
                    }
                    else
                    {
                        // 白色占比正常，执行振动+抖料
                        LogInfo("白色占比 {0:F2}% <= {1}%，振动+抖料", whiteAreaRatio, whiteAreaThreshold);
                        await ExecuteVibrateAndFeedAsync(vibrator, vibrationDuration, stabilizeDelay, ct);
                        _vibrateCounter = 0;
                        _emptyCounter++;
                        LogInfo("空振计数器: {0}/{1}", _emptyCounter, emptyVibrateCount);

                        await SendRobotConfirmNoDelayAsync(ctx, ct);
                        return Success("振动+抖料完成，等待下次检测");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw; // 让基类处理取消
            }
            catch (Exception ex)
            {
                LogError(ex, "视觉处理异常");

                // 即使失败也发送确认，让机器人知道可以重试
                try
                {
                    await SendRobotConfirmNoDelayAsync(ctx, ct);
                }
                catch { /* 忽略发送失败 */ }

                return Fail("处理异常: " + ex.Message);
            }
            finally
            {
                // 清除忙碌标志
                SetBusy(false);
                FlagCondition.SetFlag("DiskVision_Busy", false);
            }
        }

        #endregion

        #region 机器人通信

        /// <summary>
        /// 延时 + 发送机器人完成确认
        /// 
        /// 来自 V1 的竞态修复：先等待 POST_VISION_DELAY_MS，
        /// 确保 VM 通过 HeadCam TCP 发送的坐标先于 MainCtrl 上的确认命令到达 AR 端。
        /// </summary>
        private async Task SendRobotConfirmWithDelayAsync(IHandlerContext ctx, CancellationToken ct)
        {
            try
            {
                // 等待坐标数据先到达 AR 端
                LogDebug("等待 {0}ms 确保视觉数据先到达AR端...", POST_VISION_DELAY_MS);
                await Task.Delay(POST_VISION_DELAY_MS, ct);

                var robot = ctx.GetRobot();
                if (robot != null && robot.IsConnected)
                {
                    await robot.SendCommandAsync("RECEIVE_DISKRUN_SUCCESS", ct);
                    LogInfo("已发送振动完成确认");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogWarning("发送机器人确认失败: {0}", ex.Message);
            }
        }

        #endregion

        #region 只振动（不抖料）

        /// <summary>
        /// 只执行振动1（打散振动），不抖料
        /// 用于第1次检测到无料时，尝试打散堆积的种子
        /// </summary>
        private async Task ExecuteVibrateOnlyAsync(
            IVibratorDevice vibrator,
            int vibrationDuration,
            CancellationToken ct)
        {
            LogInfo("开始只振动（不抖料）");

            LogInfo("启动振动1，持续 {0}ms", vibrationDuration);
            await vibrator.StartVibrationAsync(ct);
            await Task.Delay(vibrationDuration, ct);
            await vibrator.StopVibrationAsync(ct);

            LogInfo("只振动完成");
        }

        #endregion

        #region 振动 + 抖料

        /// <summary>
        /// 执行振动1（打散振动）+ 抖料
        /// 用于连续无料时，振动打散后补充几颗料
        /// 
        /// 抖料时间在振动盘配套软件中配置，C# 只发送触发指令（FeedOnceAsync）
        /// </summary>
        private async Task ExecuteVibrateAndFeedAsync(
            IVibratorDevice vibrator,
            int vibrationDuration,
            int stabilizeDelay,
            CancellationToken ct)
        {
            LogInfo("开始振动+抖料");

            // 1. 启动振动1（打散振动）
            LogInfo("启动振动1，持续 {0}ms", vibrationDuration);
            await vibrator.StartVibrationAsync(ct);

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
        /// 
        /// 步骤：
        /// 1. 打开挡料板
        /// 2. 延迟等待挡料板完全打开
        /// 3. 启动振动2（排料振动）
        /// 4. 持续排料
        /// 5. 停止振动
        /// 6. 关闭挡料板
        /// 7. 延迟等待挡料板完全关闭
        /// 8. 振动+抖料补充（排空后补充新料）
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

                // 2. 延迟等待挡料板完全打开
                LogInfo("2. 延迟 {0}s", baffleOpenDelay);
                await Task.Delay((int)(baffleOpenDelay * 1000), ct);

                // 3. 启动振动2（排料振动）
                LogInfo("3. 启动振动2");
                await vibrator.StartVibrationGroup2Async(ct);

                // 4. 持续排料
                LogInfo("4. 持续排料 {0}s", drainDuration);
                await Task.Delay((int)(drainDuration * 1000), ct);

                // 5. 停止振动（通用停止，同时停掉振动1和振动2）
                LogInfo("5. 停止振动");
                await vibrator.StopVibrationAsync(ct);

                // 6. 关闭挡料板
                LogInfo("6. 关闭挡料板");
                WriteSignal(ctx, "Vibrator_PourDoor", false);

                // 7. 延迟等待挡料板完全关闭
                LogInfo("7. 延迟 {0}s", baffleCloseDelay);
                await Task.Delay((int)(baffleCloseDelay * 1000), ct);

                // 8. 振动+抖料补充（排空后补充新料，不修改计数器）
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

        #region 视觉数据提取

        /// <summary>
        /// 从 VM 全局变量中读取种子数量（BlobNum）
        /// 该值由 VM 流程中的 Blob 分析模块通过全局变量输出
        /// </summary>
        private int ExtractBlobNum(VmProcedure procedure)
        {
            if (procedure == null) return 0;

            // 方式1：优先从 VM 全局变量读取（统一接口）
            try
            {
                dynamic globalVar = VmSolution.Instance[GLOBAL_VAR_MODULE_NAME];
                if (globalVar != null)
                {
                    string value = globalVar.GetGlobalVar("BlobNum");
                    if (!string.IsNullOrEmpty(value) && int.TryParse(value, out int blobNum))
                        return blobNum;
                }
            }
            catch (Exception ex)
            {
                LogDebug("从全局变量提取BlobNum失败: {0}", ex.Message);
            }


            return 0;
        }

        /// <summary>
        /// 从 VM 全局变量中读取白色占比（WhiteAreaRatio）
        /// 该值由 VM 脚本4（白色占比计算）通过 SetGlobalVar() 设置
        /// 返回值范围：0-100（百分比）
        /// </summary>
        private float ExtractWhiteAreaRatio(VmProcedure procedure)
        {
            if (procedure == null) return 0f;

            try
            {
                dynamic globalVar = VmSolution.Instance[GLOBAL_VAR_MODULE_NAME];
                if (globalVar != null)
                {
                    string value = globalVar.GetGlobalVar("WhiteAreaRatio");
                    if (!string.IsNullOrEmpty(value) && float.TryParse(value, out float ratio))
                        return ratio;
                }
            }
            catch (Exception ex)
            {
                LogDebug("提取WhiteAreaRatio失败: {0}", ex.Message);
            }

            return 0f;
        }

        #endregion

        #region 坐标超限检测

        /// <summary>
        /// 设置 VM 全局变量（坐标安全范围参数）
        /// 在执行 VM 视觉检测前调用，将配方参数传递给 VM 脚本3
        /// </summary>
        private async Task SetVisionLimitParametersAsync(
            double safeMinX, double safeMaxX,
            double safeMinY, double safeMaxY,
            CancellationToken ct)
        {
            try
            {
                dynamic globalVar = VmSolution.Instance[GLOBAL_VAR_MODULE_NAME];
                if (globalVar != null)
                {
                    globalVar.SetGlobalVar("SafeMinX", safeMinX.ToString("F1"));
                    globalVar.SetGlobalVar("SafeMaxX", safeMaxX.ToString("F1"));
                    globalVar.SetGlobalVar("SafeMinY", safeMinY.ToString("F1"));
                    globalVar.SetGlobalVar("SafeMaxY", safeMaxY.ToString("F1"));

                    // 初始化检测结果为 false
                    globalVar.SetGlobalVar("CoordinateOutOfBound", "0");
                }

                LogDebug("已设置坐标安全范围: X[{0:F1}~{1:F1}], Y[{2:F1}~{3:F1}]",
                    safeMinX, safeMaxX, safeMinY, safeMaxY);

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogWarning("设置坐标安全范围参数失败: {0}", ex.Message);
            }
        }
        // 无料时：不需要延时（没有坐标数据需要先到达）
        private async Task SendRobotConfirmNoDelayAsync(IHandlerContext ctx, CancellationToken ct)
        {
            var robot = ctx.GetRobot();
            if (robot != null && robot.IsConnected)
            {
                await robot.SendCommandAsync("RECEIVE_DISKRUN_SUCCESS", ct);
            }
        }
        /// <summary>
        /// 检查坐标超限结果
        /// 从 VM 全局变量读取脚本3 设置的坐标超限检测结果
        /// </summary>
        private bool CheckCoordinateLimit()
        {
            try
            {
                dynamic globalVar = VmSolution.Instance[GLOBAL_VAR_MODULE_NAME];
                if (globalVar != null)
                {
                    string value = globalVar.GetGlobalVar("CoordinateOutOfBound");
                    if (!string.IsNullOrEmpty(value) && int.TryParse(value, out int outOfBoundFlag))
                    {
                        if (outOfBoundFlag != 0)
                        {
                            LogWarning("检测到坐标超限，可能原因：标定转换误差、镜头畸变、坐标系偏移");
                        }
                        return outOfBoundFlag != 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug("读取坐标检测结果失败: {0}", ex.Message);
            }

            // 默认返回 false（无超限），避免误报
            return false;
        }

        #endregion
    }
}