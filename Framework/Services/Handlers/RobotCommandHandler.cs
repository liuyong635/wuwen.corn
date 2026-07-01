//using SeedCut.Framework.Core;
//using SeedCut.Framework.Core.SlotSeedTracker;
//using SeedCut.Framework.Services.Conditions;
//using SeedCut.Framework.Services.Interfaces;
//using SeedCut.Services;
//using System;
//using System.Threading;
//using System.Threading.Tasks;

//namespace SeedCut.Framework.Services.Handlers
//{
//    /// <summary>
//    /// 机器人命令处理器
//    /// 
//    /// 功能：
//    /// 1. 响应机器人AR程序发送的TCP命令
//    /// 2. 根据命令类型执行对应的PLC操作或回复
//    /// 3. 协调机器人与上位机之间的交互
//    /// 
//    /// 触发条件：
//    /// - 系统运行中
//    /// - 收到待处理的机器人命令 (Robot_Command_Pending)
//    /// - 自身非忙碌状态
//    /// 
//    /// ★ 修改说明：
//    /// 1. HandleBatchCompleteAsync 现在发送 RECEIVE_SUCCESS 给 AR，防止 BATCH_COMPLETE 重复发送
//    /// 2. 批次完成时通过 DiskVision_BatchComplete 专用标志通知 DiskVisionHandler，
//    ///    避免与 Robot_BatchComplete_Received 标志竞争
//    /// 3. ALLOW_PROCESS 不再因 DiskVision_Busy 无限延迟，改为带超时的等待
//    /// 4. HandleNoPointsAsync 同样发送 RECEIVE_SUCCESS
//    /// 
//    /// 支持的命令：
//    /// - ALLOW_PROCESS: 允许开始抓取 → 发送START_PROCESSING
//    /// - CHECK_MATERIAL: 物料检测请求 → 读取传感器并回复
//    /// - CLAMP: 柔爪抓取 → 写PLC信号
//    /// - RELEASE: 柔爪松开 → 写PLC信号
//    /// - BATCH_COMPLETE: 批次完成 → 发送确认 + 触发新视觉
//    /// - VISION_NO_POINTS: 无可抓点 → 发送确认 + 触发振动
//    /// - BACK_DROP_READY: 返回放料位 → 更新生产计数
//    /// - WAIT_INIT: 请求初始化 → 发送INIT命令
//    /// 
//    /// 使用的IDevice接口：
//    /// - IRobotDevice: 发送命令给机器人
//    /// - IPlcDevice: 读写PLC信号
//    /// 
//    /// 依赖设备：Robot, PLC
//    /// </summary>
//    public class RobotCommandHandler : SignalHandlerBase
//    {
//        #region 常量定义

//        // PLC信号名称（根据实际配置调整）
//        private const string SIG_MATERIAL_DETECT = "Material_Detect";
//        private const string SIG_GRIPPER = "Gripper_Control";  // M1003.1

//        // 机器人命令
//        private const string CMD_INIT = "INIT";
//        private const string CMD_START_PROCESSING = "START_PROCESSING";
//        private const string CMD_HAS_MATERIAL = "HAS_MATERIAL";
//        private const string CMD_NO_MATERIAL = "NO_MATERIAL";
//        private const string CMD_RECEIVE_SUCCESS = "RECEIVE_SUCCESS";  // ★ 新增：确认命令
//        private const int LOADING_SLOT = 1;

//        /// <summary>
//        /// ★ 新增：ALLOW_PROCESS 等待 DiskVision 完成的最大超时时间（ms）
//        /// </summary>
//        private const int ALLOW_PROCESS_WAIT_TIMEOUT_MS = 15000;

//        #endregion

//        #region Handler属性

//        public override string HandlerId => "RobotCommand";

//        public override string HandlerName => "机器人命令处理";

//        public override int Priority => 90;

//        public override string[] DependentDevices => new[] { "Robot", "PLC" };

//        /// <summary>
//        /// 触发条件：运行中 + 有待处理命令 + 非忙碌
//        /// </summary>
//        public override ITriggerCondition TriggerCondition => When.All(
//            When.IsRunning(),
//            When.FlagOn("Robot_Command_Pending"),
//            When.FlagOff("RobotCommand_Busy")
//        );

//        #endregion

//        #region 执行逻辑

//        protected override async Task<ValueTuple<bool, string>> ExecuteAsync(
//            IHandlerContext ctx,
//            CancellationToken ct)
//        {
//            // 设置忙碌标志
//            SetBusy(true);
//            FlagCondition.SetFlag("RobotCommand_Busy", true);

//            try
//            {
//                var robot = ctx.GetRobot();
//                var plc = ctx.GetPlc();
//                var signal = ctx.Signal;

//                if (robot == null || !robot.IsConnected)
//                {
//                    return Fail("机器人设备未连接");
//                }

//                // 获取待处理的命令类型
//                var commandType = GetPendingCommandType();

//                if (commandType == RobotCommandType.Unknown)
//                {
//                    // 清除待处理标志
//                    FlagCondition.SetFlag("Robot_Command_Pending", false);
//                    return Fail("无有效的待处理命令");
//                }

//                LogInfo("处理机器人命令: {0}", commandType);

//                // 根据命令类型执行对应操作
//                var result = await ProcessCommandAsync(ctx, robot, signal, commandType, ct);

//                // 清除对应的命令标志
//                ClearCommandFlag(commandType);

//                // 检查是否还有其他待处理命令
//                if (!HasPendingCommands())
//                {
//                    FlagCondition.SetFlag("Robot_Command_Pending", false);
//                }

//                return result;
//            }
//            catch (OperationCanceledException)
//            {
//                throw;
//            }
//            catch (Exception ex)
//            {
//                LogError(ex, "命令处理异常");
//                return Fail("处理异常: " + ex.Message);
//            }
//            finally
//            {
//                // 清除忙碌标志
//                SetBusy(false);
//                FlagCondition.SetFlag("RobotCommand_Busy", false);
//            }
//        }

//        #endregion

//        #region 命令处理

//        /// <summary>
//        /// 根据命令类型执行对应操作
//        /// </summary>
//        private async Task<ValueTuple<bool, string>> ProcessCommandAsync(
//            IHandlerContext ctx,
//            IRobotDevice robot,
//            ISignalAccessor signal,
//            RobotCommandType commandType,
//            CancellationToken ct)
//        {
//            switch (commandType)
//            {
//                case RobotCommandType.AllowProcess:
//                    return await HandleAllowProcessAsync(robot, signal, ct);

//                case RobotCommandType.CheckMaterial:
//                    return await HandleCheckMaterialAsync(robot, signal, ct);

//                case RobotCommandType.Clamp:
//                    return await HandleClampAsync(signal, ct);

//                case RobotCommandType.Release:
//                    return await HandleReleaseAsync(ctx, signal, ct);

//                case RobotCommandType.BatchComplete:
//                    return await HandleBatchCompleteAsync(ctx, ct);

//                case RobotCommandType.NoPoints:
//                    return await HandleNoPointsAsync(ctx, ct);

//                case RobotCommandType.BackDropReady:
//                    return await HandleBackDropReadyAsync(ctx, ct);

//                case RobotCommandType.WaitInit:
//                    return await HandleWaitInitAsync(robot, ct);

//                case RobotCommandType.BatchNext:
//                    return HandleBatchNext();

//                default:
//                    return Fail("未知命令类型: " + commandType);
//            }
//        }

//        /// <summary>
//        /// 处理ALLOW_PROCESS命令 - 允许开始抓取
//        /// 
//        /// ★ 修改：不再因 DiskVision_Busy 跳过，而是在此处带超时等待
//        /// </summary>
//        private async Task<ValueTuple<bool, string>> HandleAllowProcessAsync(
//            IRobotDevice robot,
//            ISignalAccessor signal,
//            CancellationToken ct)
//        {
//            LogInfo("收到ALLOW_PROCESS，准备发送START_PROCESSING");

//            // ★ 如果 DiskVision 仍在执行，等待其完成（带超时）
//            if (FlagCondition.GetFlag("DiskVision_Busy"))
//            {
//                LogInfo("DiskVision 正在执行，等待完成...");
//                int waited = 0;
//                while (FlagCondition.GetFlag("DiskVision_Busy") && waited < ALLOW_PROCESS_WAIT_TIMEOUT_MS)
//                {
//                    ct.ThrowIfCancellationRequested();
//                    await Task.Delay(100, ct);
//                    waited += 100;
//                }

//                if (FlagCondition.GetFlag("DiskVision_Busy"))
//                {
//                    LogWarning("等待DiskVision超时({0}ms)，强制继续", ALLOW_PROCESS_WAIT_TIMEOUT_MS);
//                }
//                else
//                {
//                    LogInfo("DiskVision 已完成，继续处理 ALLOW_PROCESS (等待了{0}ms)", waited);
//                }
//            }

//            await robot.SendCommandAsync(CMD_START_PROCESSING, ct);

//            return Success("已发送START_PROCESSING");
//        }

//        /// <summary>
//        /// 处理CHECK_MATERIAL命令 - 物料检测
//        /// </summary>
//        private async Task<ValueTuple<bool, string>> HandleCheckMaterialAsync(
//            IRobotDevice robot,
//            ISignalAccessor signal,
//            CancellationToken ct)
//        {
//            LogInfo("收到CHECK_MATERIAL，检测物料");

//            bool hasMaterial = false;

//            try
//            {
//                hasMaterial = signal.ReadBit(SIG_MATERIAL_DETECT);
//            }
//            catch (Exception ex)
//            {
//                LogWarning("读取物料传感器失败: {0}", ex.Message);
//            }

//            var response = hasMaterial ? CMD_HAS_MATERIAL : CMD_NO_MATERIAL;
//            LogInfo("物料检测结果: {0}", response);

//            await robot.SendCommandAsync(response, ct);

//            return Success(string.Format("物料检测: {0}", response));
//        }

//        /// <summary>
//        /// 处理CLAMP命令 - 柔爪抓取
//        /// </summary>
//        private async Task<ValueTuple<bool, string>> HandleClampAsync(
//            ISignalAccessor signal,
//            CancellationToken ct)
//        {
//            LogInfo("收到CLAMP，执行柔爪抓取");

//            try
//            {
//                // 写TRUE保持（不是脉冲！）
//                signal.WriteBit(SIG_GRIPPER, true);
//                LogDebug("写入柔爪抓取信号: M1003.1 = TRUE");
//            }
//            catch (Exception ex)
//            {
//                LogWarning("柔爪抓取信号写入失败: {0}", ex.Message);
//                return Fail("柔爪抓取失败: " + ex.Message);
//            }

//            await Task.CompletedTask;
//            return Success("柔爪抓取完成");
//        }

//        /// <summary>
//        /// 处理RELEASE命令 - 柔爪松开
//        /// </summary>
//        private async Task<ValueTuple<bool, string>> HandleReleaseAsync(
//            IHandlerContext ctx,
//            ISignalAccessor signal,
//            CancellationToken ct)
//        {
//            LogInfo("收到RELEASE，执行柔爪松开");

//            try
//            {
//                // 写FALSE保持（不是脉冲！）
//                signal.WriteBit(SIG_GRIPPER, false);

//                // 在工位1创建新种子（这是种子进入系统的入口）
//                var tracker = ctx.GetService<ISlotSeedTracker>();

//                if (tracker != null && tracker.IsInitialized)
//                {
//                    // 创建新种子
//                    string seedId = tracker.PlaceSeed(LOADING_SLOT, "Seed");

//                    // 附加上料数据
//                    tracker.AttachData(seedId, "LoadingTime", DateTime.Now);
//                    tracker.AttachData(seedId, "LoadingTurntableIndex", tracker.TurntableIndex);

//                    LogInfo("工位{0}创建新种子: {1}", LOADING_SLOT, seedId);
//                }

//                LogDebug("写入柔爪松开信号: M1003.1 = FALSE");
//            }
//            catch (Exception ex)
//            {
//                LogWarning("柔爪松开信号写入失败: {0}", ex.Message);
//                return Fail("柔爪松开失败: " + ex.Message);
//            }

//            await Task.CompletedTask;
//            return Success("柔爪松开完成");
//        }

//        /// <summary>
//        /// 处理BATCH_COMPLETE命令 - 批次完成
//        /// 
//        /// ★★★ 关键修改 ★★★
//        /// 1. 发送 RECEIVE_SUCCESS 给 AR 端，阻止 AR 重复发送 BATCH_COMPLETE
//        /// 2. 设置 DiskVision_BatchComplete 专用标志通知 DiskVisionHandler
//        ///    不再直接依赖 Robot_BatchComplete_Received（该标志由本 Handler 消费）
//        /// </summary>
//        private async Task<ValueTuple<bool, string>> HandleBatchCompleteAsync(
//            IHandlerContext ctx,
//            CancellationToken ct)
//        {
//            LogInfo("收到BATCH_COMPLETE，发送确认并通知视觉处理");

//            // ★ 第1步：立即发送确认给 AR 端，阻止重复发送
//            var robot = ctx.GetRobot();
//            if (robot != null && robot.IsConnected)
//            {
//                await robot.SendCommandAsync(CMD_RECEIVE_SUCCESS, ct);
//                LogDebug("已发送 RECEIVE_SUCCESS 确认");
//            }

//            // ★ 第2步：设置专用标志，触发 DiskVisionHandler
//            FlagCondition.SetFlag("DiskVision_BatchComplete", true);

//            return Success("已确认并通知视觉处理");
//        }

//        /// <summary>
//        /// ★ 新增：处理VISION_NO_POINTS命令 - 无可抓点
//        /// 
//        /// 由 AR 端在视觉返回 NO_POINTS 时发送。
//        /// 发送 RECEIVE_SUCCESS 确认，并设置标志触发 DiskVisionHandler。
//        /// </summary>
//        private async Task<ValueTuple<bool, string>> HandleNoPointsAsync(
//            IHandlerContext ctx,
//            CancellationToken ct)
//        {
//            LogInfo("收到VISION_NO_POINTS，通知视觉处理");

//            // ★ 删除：不再发送 RECEIVE_SUCCESS（由 DiskVisionHandler 的
//            //   RECEIVE_DISKRUN_SUCCESS 作为最终确认）
//            // var robot = ctx.GetRobot();
//            // if (robot != null && robot.IsConnected)
//            // {
//            //     await robot.SendCommandAsync(CMD_RECEIVE_SUCCESS, ct);
//            // }

//            // ★ 显式设置标志，确保 DiskVisionHandler 能触发
//            FlagCondition.SetFlag("Vision_NoPoints_Received", true);

//            await Task.CompletedTask;
//            return Success("已通知视觉处理");
//        }

//        /// <summary>
//        /// 处理BACK_DROP_READY命令 - 返回放料位
//        /// </summary>
//        private async Task<ValueTuple<bool, string>> HandleBackDropReadyAsync(
//            IHandlerContext ctx,
//            CancellationToken ct)
//        {
//            LogInfo("收到BACK_DROP_READY，更新计数");

//            // 更新生产计数
//            ctx.Production.IncrementCount();

//            await Task.CompletedTask;
//            return Success("计数已更新");
//        }

//        /// <summary>
//        /// 处理WAIT_INIT命令 - 请求初始化
//        /// </summary>
//        private async Task<ValueTuple<bool, string>> HandleWaitInitAsync(
//            IRobotDevice robot,
//            CancellationToken ct)
//        {
//            LogInfo("收到WAIT_INIT，发送INIT");

//            await robot.SendCommandAsync(CMD_INIT, ct);

//            return Success("已发送INIT");
//        }

//        /// <summary>
//        /// 处理BATCH_NEXT命令 - 下一个坐标
//        /// </summary>
//        private ValueTuple<bool, string> HandleBatchNext()
//        {
//            LogDebug("收到BATCH_NEXT，处理下一个坐标");
//            return Success("继续处理");
//        }

//        #endregion

//        #region 命令标志管理

//        /// <summary>
//        /// 获取当前待处理的命令类型（按优先级）
//        /// 
//        /// ★ 修改：
//        /// 1. ALLOW_PROCESS 不再因 DiskVision_Busy 跳过，直接返回让 HandleAllowProcessAsync 内部等待
//        /// 2. 新增 NoPoints 命令类型
//        /// </summary>
//        private RobotCommandType GetPendingCommandType()
//        {
//            // 按优先级检查（高优先级先处理）

//            // 这些命令在任何时候都需要立即响应
//            if (FlagCondition.GetFlag("Robot_CheckMaterial_Received"))
//                return RobotCommandType.CheckMaterial;

//            if (FlagCondition.GetFlag("Robot_Clamp_Received"))
//                return RobotCommandType.Clamp;

//            if (FlagCondition.GetFlag("Robot_Release_Received"))
//                return RobotCommandType.Release;

//            // ★ ALLOW_PROCESS：不再跳过，直接处理（内部等待 DiskVision 完成）
//            if (FlagCondition.GetFlag("Robot_AllowProcess_Received"))
//                return RobotCommandType.AllowProcess;

//            if (FlagCondition.GetFlag("Robot_BackDropReady_Received"))
//                return RobotCommandType.BackDropReady;

//            // ★ 新增：NoPoints 命令
//            if (FlagCondition.GetFlag("Robot_NoPoints_Received"))
//                return RobotCommandType.NoPoints;

//            if (FlagCondition.GetFlag("Robot_BatchComplete_Received"))
//                return RobotCommandType.BatchComplete;

//            if (FlagCondition.GetFlag("Robot_WaitInit_Received"))
//                return RobotCommandType.WaitInit;

//            if (FlagCondition.GetFlag("Robot_BatchNext_Received"))
//                return RobotCommandType.BatchNext;

//            return RobotCommandType.Unknown;
//        }

//        /// <summary>
//        /// 清除对应命令的标志
//        /// </summary>
//        private void ClearCommandFlag(RobotCommandType commandType)
//        {
//            switch (commandType)
//            {
//                case RobotCommandType.AllowProcess:
//                    FlagCondition.SetFlag("Robot_AllowProcess_Received", false);
//                    break;
//                case RobotCommandType.CheckMaterial:
//                    FlagCondition.SetFlag("Robot_CheckMaterial_Received", false);
//                    break;
//                case RobotCommandType.Clamp:
//                    FlagCondition.SetFlag("Robot_Clamp_Received", false);
//                    break;
//                case RobotCommandType.Release:
//                    FlagCondition.SetFlag("Robot_Release_Received", false);
//                    break;
//                case RobotCommandType.BatchComplete:
//                    FlagCondition.SetFlag("Robot_BatchComplete_Received", false);
//                    break;
//                case RobotCommandType.NoPoints:
//                    FlagCondition.SetFlag("Robot_NoPoints_Received", false);
//                    break;
//                case RobotCommandType.BackDropReady:
//                    FlagCondition.SetFlag("Robot_BackDropReady_Received", false);
//                    break;
//                case RobotCommandType.WaitInit:
//                    FlagCondition.SetFlag("Robot_WaitInit_Received", false);
//                    break;
//                case RobotCommandType.BatchNext:
//                    FlagCondition.SetFlag("Robot_BatchNext_Received", false);
//                    break;
//            }
//        }

//        /// <summary>
//        /// 检查是否还有待处理的命令
//        /// </summary>
//        private bool HasPendingCommands()
//        {
//            return FlagCondition.GetFlag("Robot_AllowProcess_Received")
//                || FlagCondition.GetFlag("Robot_CheckMaterial_Received")
//                || FlagCondition.GetFlag("Robot_Clamp_Received")
//                || FlagCondition.GetFlag("Robot_Release_Received")
//                || FlagCondition.GetFlag("Robot_BatchComplete_Received")
//                || FlagCondition.GetFlag("Robot_NoPoints_Received")
//                || FlagCondition.GetFlag("Robot_BackDropReady_Received")
//                || FlagCondition.GetFlag("Robot_WaitInit_Received")
//                || FlagCondition.GetFlag("Robot_BatchNext_Received");
//        }

//        #endregion
//    }
//}