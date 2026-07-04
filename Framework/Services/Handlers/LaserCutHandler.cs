using SeedCut.Framework.Core.SlotSeedTracker;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using VM.Core;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// 激光切割处理器（多Pass版）
    /// 
    /// ★★★ v2 改动内容 ★★★
    /// 1. 支持1~5次不同参数的多Pass切割（通过配方管理）
    /// 2. 利用HM控制卡UDM图层机制，一次下载+一次打标完成所有Pass
    /// 3. 替换原有 ExecuteLaserCutAsync 循环逻辑
    /// 4. 配方参数从 CutRepeatCount 升级为 CutPassCount + Pass{N}_* 逐次参数
    /// 
    /// 配方参数（"LaserCut" 分组）：
    /// - CutPassCount       : 切割次数 (1~5)
    /// - CutIntervalMs      : Pass间等待时间 (ms)
    /// - Pass{N}_MarkSpeed  : 第N次打标速度 (mm/s)
    /// - Pass{N}_Frequency  : 第N次频率 (kHz)
    /// - Pass{N}_LaserPower : 第N次能量 (%)
    /// - CompleteDelayMs    : 完成信号后等待 (ms)
    /// - PulseDurationMs    : 完成脉冲宽度 (ms)
    /// - LaserCutSlot       : 切割工位号
    /// - VisionToCutGap     : 视觉到切割工位间距
    /// 
    /// 依赖设备：PLC, HM_Laser
    /// </summary>
    public class LaserCutHandler : SignalHandlerBase
    {
        #region 常量定义（信号名 / 管道名 — 协议级，不可配方化）

        private const string SIG_CUT_REQUEST = "LaserCut_RequestSig";
        private const string SIG_CUT_COMPLETE = "LaserCut_Complete";
        private const string SIG_BLOCK_CYLINDER_ALARM = "LaserCut_BlockCylinder_Alarm";
        public const string SIG_SMALL_DROP_POHTO = "Small_drop_photo";
        private const string PIPELINE_LASER_QUEUE = "LaserQueue";

        /// <summary>
        /// 最大支持Pass数
        /// </summary>
        private const int MAX_PASS_COUNT = 5;

        #endregion

        #region Handler属性

        public override string HandlerId => "LaserCut";
        public override string HandlerName => "激光切割";
        public override int Priority => 84;
        public override string[] DependentDevices => new[] { "PLC", "HM_Laser" };
        /// <summary>
        /// 连续掉落失败次数，先放这里后期移到流程上下文来处理
        /// </summary>
        public static int DropFailureCount { get; set; } = 0;
        public override ITriggerCondition TriggerCondition => When.All(
            When.IsRunning(),
            When.SignalOn(SIG_CUT_REQUEST),
            When.FlagOff("LaserCut_Busy")
        );

        #endregion

        #region 执行逻辑

        protected override async Task<ValueTuple<bool, string>> ExecuteAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            if(SeedCutApp.Default.SkipLaserCut)
            {
                await SendCutCompleteAsync(ctx, 50, 50, ct);
                return Success();
            }
            SetBusy(true);
            FlagCondition.SetFlag("LaserCut_Busy", true);

            try
            {
                var vision = ctx.GetVision();

                if (vision == null)
                {
                    return Fail("视觉设备未找到");
                }

                if (!vision.IsConnected)
                {
                    return Fail("视觉设备未连接");
                }

                var laser = ctx.GetLaser();

                if (laser == null)
                    return Fail("激光设备未找到");

                if (!laser.IsConnected)
                    return Fail("激光设备未连接");

                // ============ 读取配方参数 ============
                var recipe = ctx.GetService<IRecipeService>();

                // --- 全局参数 ---
                int passCount = recipe?.GetInt("LaserCut", "CutPassCount", 1,
                    name: "切割次数", description: "激光切割Pass数(1~5)") ?? 1;
                passCount = Math.Max(1, Math.Min(MAX_PASS_COUNT, passCount));

                int cutIntervalMs = recipe?.GetInt("LaserCut", "CutIntervalMs", 0,
                    name: "Pass间隔", unit: "ms", description: "两次切割之间的等待时间") ?? 0;

                int completeDelayMs = recipe?.GetInt("LaserCut", "CompleteDelayMs", 200,
                    name: "完成延迟", unit: "ms", description: "发送完成信号后的等待时间") ?? 200;
                int pulseDurationMs = recipe?.GetInt("LaserCut", "PulseDurationMs", 50,
                    name: "脉冲宽度", unit: "ms", description: "完成信号脉冲持续时间") ?? 50;
                int laserCutSlot = recipe?.GetInt("LaserCut", "LaserCutSlot", 5,
                    name: "切割工位号", description: "激光切割对应的转盘工位号") ?? 5;
                int visionToCutGap = recipe?.GetInt("LaserCut", "VisionToCutGap", 2,
                    name: "视觉到切割间距", description: "视觉工位到切割工位的工位间距") ?? 2;

                // --- 逐次参数（Pass1 ~ PassN）---
                var passParams = ReadPassParams(recipe, passCount);

                // 获取追踪器和当前状态
                var tracker = ctx.GetService<ISlotSeedTracker>();
                string expectedSeedId = null;
                long currentIndex = 0;

                if (tracker != null && tracker.IsInitialized)
                {
                    expectedSeedId = tracker.GetSeedIdAt(laserCutSlot);
                    currentIndex = tracker.TurntableIndex;
                    LogInfo("工位{0}期望种子: {1}, 当前转盘索引: {2}",
                        laserCutSlot, expectedSeedId ?? "null", currentIndex);
                }

                // ========== 安全检查 ==========
                try
                {
                    if (ReadSignal(ctx, SIG_BLOCK_CYLINDER_ALARM))
                    {
                        LogWarning("挡料气缸报警，无法执行切割");
                        return Fail("挡料气缸报警");
                    }
                }
                catch (Exception ex)
                {
                    LogWarning("读取挡料气缸状态失败: {0}", ex.Message);
                }

                // ========== 命令获取和匹配 ==========
                var pipeline = ctx.DataFlow.Get<LaserCommand>(PIPELINE_LASER_QUEUE);

                // 如果工位没有种子，无法匹配，跳过
                if (string.IsNullOrEmpty(expectedSeedId))
                {
                    LogInfo("工位{0}无种子，跳过切割", laserCutSlot);

                    await SendCutCompleteAsync(ctx, pulseDurationMs, completeDelayMs, ct);
                    return Success(string.Format("工位{0}无种子", laserCutSlot));
                }


                // 查找匹配的命令
                LaserCommand command = FindMatchingCommand(pipeline, expectedSeedId, currentIndex, visionToCutGap);

                if (command == null)
                {
                    LogDebug("未找到匹配工位{0}种子({1})的命令", laserCutSlot, expectedSeedId);
                    await SendCutCompleteAsync(ctx, pulseDurationMs, completeDelayMs, ct);
                    return Success("等待命令入队");
                }

                LogInfo("找到匹配命令: SeedId={0}, 命令长度={1}",
                    command.SeedId, command.RawCommand?.Length ?? 0);

                // ========== 检查空数据 ==========
                if (command.CommandType == LaserCommandType.Empty ||
                    command.RawCommand == "Empty" ||
                    string.IsNullOrEmpty(command.RawCommand))
                {
                    LogInfo("空切割数据，跳过切割");

                    if (tracker != null && !string.IsNullOrEmpty(command.SeedId))
                    {
                        tracker.AttachData(command.SeedId, "LaserCutTime", DateTime.Now);
                        tracker.AttachData(command.SeedId, "LaserCutSkipped", true);
                    }

                    await SendCutCompleteAsync(ctx, pulseDurationMs, completeDelayMs, ct);
                    return Success("空数据，跳过切割");
                }

                // ========== ★ 执行多Pass激光切割 ========== 
                LogInfo("开始执行多Pass激光切割: {0}个Pass, SeedId={1}",
                    passCount, command.SeedId);
                await TakePhotoBeforLaserCut(vision, ctx);
                var cutResult = false;
                await App.Current.Dispatcher.Invoke(async () =>
                   {
                       cutResult = await LaserCut(ctx, vision, laser, command, passCount, passParams, cutIntervalMs, ct, CheckSeedCutDown);
                       await Task.Delay(500);
                   });


                // 附加切割数据到追踪器
                if (tracker != null && !string.IsNullOrEmpty(command.SeedId))
                {
                    tracker.AttachData(command.SeedId, "LaserCutTime", DateTime.Now);
                    tracker.AttachData(command.SeedId, "LaserCutSuccess", cutResult);
                    tracker.AttachData(command.SeedId, "LaserCutSkipped", false);
                    tracker.AttachData(command.SeedId, "LaserCutPassCount", passCount);
                    tracker.AttachData(command.SeedId, "CutTurntableIndex", currentIndex);
                }

                if (!cutResult)
                {
                    LogWarning("多Pass激光切割执行失败");
                    await SendCutCompleteAsync(ctx, pulseDurationMs, completeDelayMs, ct);
                    return Fail("激光切割失败");
                }

                LogInfo("多Pass激光切割完成: {0}个Pass", passCount);
                await SendCutCompleteAsync(ctx, pulseDurationMs, completeDelayMs, ct);

                ///挑落检查
                bool dropResult = await CheckSeedCutDown(vision, ctx, ct);
                if (dropResult)
                {
                    DropFailureCount = 0;
                }
                else
                {
                    DropFailureCount++;
                }
                if (DropFailureCount >= 3)
                {
                    FlagCondition.SetFlag("WorkAbort", true);
                }

                return Success(string.Format("激光切割完成({0}个Pass)", passCount));
            }
            catch (OperationCanceledException)
            {
                try
                {
                    var laser = ctx.GetLaser();
                    if (laser != null && laser.IsConnected)
                    {
                        await laser.StopMarkAsync(ct);
                    }
                }
                catch { }
                throw;
            }
            catch (Exception ex)
            {
                LogError(ex, "激光切割处理异常");
                return Fail("处理异常: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
                FlagCondition.SetFlag("LaserCut_Busy", false);
            }
        }

        #endregion

        #region 配方参数读取

        /// <summary>
        /// 从配方读取每个Pass的独立参数
        /// 
        /// 配方Key格式：Pass{N}_MarkSpeed, Pass{N}_Frequency, Pass{N}_LaserPower
        /// 其中 N = 1, 2, 3, 4, 5
        /// 
        /// 未配置的Pass使用默认值：Speed=1000, Freq=50, Power=50
        /// </summary>
        private LaserPassParam[] ReadPassParams(IRecipeService recipe, int passCount)
        {
            var passParams = new LaserPassParam[passCount];

            for (int i = 0; i < passCount; i++)
            {
                int n = i + 1;  // Pass编号从1开始

                passParams[i] = new LaserPassParam
                {
                    MarkSpeed = (uint)(recipe?.GetInt("LaserCut",
                        string.Format("Pass{0}_MarkSpeed", n), 1000,
                        name: string.Format("第{0}次速度", n),
                        unit: "mm/s",
                        description: string.Format("第{0}次切割的打标速度", n)) ?? 1000),

                    Frequency = recipe?.GetFloat("LaserCut",
                        string.Format("Pass{0}_Frequency", n), 50f,
                        name: string.Format("第{0}次频率", n),
                        unit: "kHz",
                        description: string.Format("第{0}次切割的激光频率", n)) ?? 50f,

                    LaserPower = recipe?.GetFloat("LaserCut",
                        string.Format("Pass{0}_LaserPower", n), 50f,
                        name: string.Format("第{0}次能量", n),
                        unit: "%",
                        description: string.Format("第{0}次切割的激光能量", n)) ?? 50f,
                };
            }

            return passParams;
        }

        #endregion

        #region 查找匹配命令

        /// <summary>
        /// 从队列中查找与指定工位种子ID匹配的命令
        /// （此方法与原版完全一致，未修改）
        /// </summary>
        private LaserCommand FindMatchingCommand(
            IDataPipeline<LaserCommand> pipeline,
            string expectedSeedId,
            long currentIndex,
            int visionToCutGap)
        {
            if (pipeline == null || pipeline.IsEmpty)
                return null;

            LaserCommand matchedCommand = null;
            int duplicateCount = 0;

            var tempList = new System.Collections.Generic.List<LaserCommand>();
            while (pipeline.TryPop(out var cmd))
            {
                tempList.Add(cmd);
            }

            if (tempList.Count > 0)
            {
                LogDebug("队列中有 {0} 个命令待检查", tempList.Count);
            }

            foreach (var cmd in tempList)
            {
                if (cmd.SeedId == expectedSeedId)
                {
                    if (matchedCommand == null)
                    {
                        matchedCommand = cmd;
                        LogDebug("从队列找到匹配命令: SeedId={0}", cmd.SeedId);
                    }
                    else
                    {
                        duplicateCount++;
                        LogWarning("丢弃重复命令: SeedId={0}, 这是第 {1} 个重复",
                            cmd.SeedId, duplicateCount);
                    }
                }
                else if (IsCommandExpired(cmd, currentIndex, visionToCutGap))
                {
                    LogDebug("丢弃过期命令: SeedId={0}, SourceIndex={1}, CurrentIndex={2}",
                        cmd.SeedId, cmd.SourceTurntableIndex, currentIndex);
                }
                else
                {
                    pipeline.Push(cmd);
                }
            }

            if (duplicateCount > 0)
            {
                LogWarning("共丢弃 {0} 个重复命令 (SeedId={1})", duplicateCount, expectedSeedId);
            }

            return matchedCommand;
        }

        /// <summary>
        /// 判断命令是否已过期
        /// </summary>
        private bool IsCommandExpired(LaserCommand cmd, long currentIndex, int visionToCutGap)
        {
            if (string.IsNullOrEmpty(cmd.SeedId))
                return true;

            if (cmd.SourceTurntableIndex <= 0)
                return true;

            long expectedCutIndex = cmd.SourceTurntableIndex + visionToCutGap;

            return currentIndex > expectedCutIndex + 2;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 构建激光命令字符串（坐标格式化）
        /// </summary>
        private string BuildLaserCommand(string coordinates)
        {
            if (coordinates.StartsWith("AddLines[") || coordinates.StartsWith("AddAreas["))
            {
                return coordinates;
            }

            string trimmed = coordinates.TrimEnd(']');
            return string.Format("AddLines[{0}]", trimmed);
        }

        /// <summary>
        /// 发送切割完成信号
        /// </summary>
        private async Task SendCutCompleteAsync(
            IHandlerContext ctx,
            int pulseDurationMs,
            int completeDelayMs,
            CancellationToken ct)
        {
            LogInfo("发送激光切割完成信号");
            WriteSignalPulse(ctx, SIG_CUT_COMPLETE, pulseDurationMs);

            // 等待 Request 信号复位，避免被重复触发
            try
            {
                var resetOk = await ctx.Signal.WaitForBitAsync(
                    SIG_CUT_REQUEST,
                    false,
                    TimeSpan.FromMilliseconds(300),
                    ct);

                if (!resetOk)
                {
                    LogDebug("Request 信号未在 300ms 内复位（PLC 响应慢）");
                }
            }
            catch (Exception ex)
            {
                LogDebug("等待 Request 复位异常: {0}", ex.Message);
            }

            await Task.Delay(completeDelayMs, ct);
        }

        private async Task TakePhotoBeforLaserCut(IVisionDevice vision, IHandlerContext ctx)
        {
            var visionResult = await vision.ExecuteAsync("小切割前", ctx);
            this.LogDebug("小切割前拍照");
        }

        private async Task<bool> CheckSeedCutDown(IVisionDevice vision, IHandlerContext ctx, CancellationToken ct)
        {
            this.LogDebug("小料盘掉落");
            await Task.Delay(1000);
            if (ct.IsCancellationRequested)
            {
                return true;
            }

            this.LogDebug("小料盘有无检测");
            var visionResult = await vision.ExecuteAsync("小料盘有无检测", ctx);
            if (visionResult == null || !visionResult.Success)
                return false;
            int valid = ReadGlobalVarInt("SmallDropState", 0);
            this.LogDebug(valid == 1 ? "有掉落" : "无掉落");
            return valid == 1;
        }
        /// <summary>
        /// 读取整型全局变量
        /// </summary>
        private int ReadGlobalVarInt(string varName, int defaultValue)
        {
            try
            {
                dynamic globalVar = VmSolution.Instance[LaserVisionHandler.GLOBAL_VAR_MODULE_NAME];
                if (globalVar != null)
                {
                    string value = globalVar.GetGlobalVar(varName);
                    if (!string.IsNullOrEmpty(value) && int.TryParse(value, out int result))
                        return result;
                }
            }
            catch (Exception ex)
            {
                LogDebug("读取全局变量 {0} 失败: {1}", varName, ex.Message);
            }
            return defaultValue;
        }
        private async Task<bool> LaserCut(IHandlerContext ctx, IVisionDevice vision, ILaserDevice laser, LaserCommand command, int passCount, LaserPassParam[] passParams, int cutIntervalMs, CancellationToken ct, Func<IVisionDevice, IHandlerContext, CancellationToken, Task<bool>> checkFun = null)
        {
            for (int i = 0; i < passCount; i++)
            {
                LogInfo("  Pass{0}: Speed={1}mm/s, Freq={2}kHz, Power={3}%",
                    i + 1, passParams[i].MarkSpeed,
                    passParams[i].Frequency, passParams[i].LaserPower);
            }

            // 构建坐标字符串
            string coordinates = BuildLaserCommand(command.RawCommand);

            // 构建多Pass请求
            var request = new MultiPassMarkRequest
            {
                Coordinates = coordinates,
                PassParams = passParams,
                IntervalMs = cutIntervalMs
            };

            // 调用多Pass打标接口
            var cutResult = await laser.AddLinesAndMarkMultiPassAsync(request, ct);

            if (checkFun == null)
            {
                this.LogDebug("第二次切割");
            }
            if (checkFun != null && await checkFun.Invoke(vision, ctx, ct) == false)
            {
                this.LogDebug("没有检测到下落，切第二次");
                return await LaserCut(ctx, vision, laser, command, passCount, passParams, cutIntervalMs, ct);
            }
            return false;
        }
        #endregion
    }
}