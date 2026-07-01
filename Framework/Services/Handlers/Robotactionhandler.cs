using SeedCut.Framework.Core;
using SeedCut.Framework.Core.SlotSeedTracker;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// 机器人动作协调器
    /// 
    /// ═══════════════════════════════════════════════════════════
    /// v2.2 修改说明：
    ///   飞拍偏差数据恢复为 AR ↔ VM HeadCam TCP 直连方式。
    ///   C# 不再参与偏差读取和纠偏坐标计算。
    ///   AR 本地接收偏差 → 本地计算纠偏 → 直接运动放料。
    ///   
    ///   FLY_CAPTURE: AR 通知飞拍已触发（仅日志）
    ///   FLY_OFFSET:  AR 转发偏差数据（仅日志监控）
    ///   
    ///   删除了原 HandleFlyTriggeredAsync 中的 VM 全局变量轮询逻辑
    ///   和 PLACE 命令下发逻辑。
    /// ═══════════════════════════════════════════════════════════
    /// 
    /// 功能：
    /// 1. 从 DataFlow 队列按优先级消费 AR 上报的命令
    /// 2. 根据命令类型执行 PLC 操作 / 回复 AR / 设置标志
    /// 3. 种子追踪：松爪时创建新种子
    /// 4. 生产计数：放料完成时递增
    /// 
    /// 触发条件：
    /// - 系统运行中
    /// - 有待处理命令 (Robot_Command_Pending)
    /// - 自身非忙碌
    /// 
    /// 依赖设备：Robot, PLC
    /// </summary>
    public class RobotActionHandler : SignalHandlerBase
    {
        #region 常量

        /// <summary>PLC 物料检测信号名</summary>
        private const string SIG_MATERIAL_DETECT = "Material_Detect";

        /// <summary>PLC 柔爪控制信号名</summary>
        private const string SIG_GRIPPER = "Gripper_Control";

        /// <summary>上料工位编号</summary>
        private const int LOADING_SLOT = 1;

        #endregion

        #region Handler 属性

        public override string HandlerId => "RobotAction";
        public override string HandlerName => "机器人动作协调";
        public override int Priority => 90;
        public override string[] DependentDevices => new[] { "Robot", "PLC" };

        public override ITriggerCondition TriggerCondition => When.All(
            When.IsRunning(),
            When.FlagOn("Robot_Command_Pending"),
            When.FlagOff("RobotAction_Busy")
        );

        #endregion

        #region 主执行逻辑

        protected override async Task<ValueTuple<bool, string>> ExecuteAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            SetBusy(true);
            FlagCondition.SetFlag("RobotAction_Busy", true);

            try
            {
                var robot = ctx.GetRobot();
                var signal = ctx.Signal;

                if (robot == null || !robot.IsConnected)
                    return Fail("机器人设备未连接");

                int processedCount = 0;

                // ★ 从静态共享队列取命令（由 RobotDeviceAdapter 推入）
                while (RobotCommand.TryDequeue(out var cmd))
                {
                    ct.ThrowIfCancellationRequested();

                    LogInfo("处理命令: {0} (优先级={1})", cmd.Type, cmd.Priority);

                    await ProcessSingleCommandAsync(ctx, robot, signal, cmd, ct);
                    processedCount++;
                }

                // 队列已空，清除标志
                FlagCondition.SetFlag("Robot_Command_Pending", false);

                return Success(string.Format("处理了 {0} 条命令", processedCount));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogError(ex, "命令处理异常");
                return Fail("处理异常: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
                FlagCondition.SetFlag("RobotAction_Busy", false);
            }
        }

        #endregion

        #region 命令分发

        /// <summary>处理单条命令</summary>
        private async Task ProcessSingleCommandAsync(
            IHandlerContext ctx,
            IRobotDevice robot,
            ISignalAccessor signal,
            RobotCommand cmd,
            CancellationToken ct)
        {
            try
            {
                switch (cmd.Type)
                {
                    case RobotCommandType.WaitInit:
                        await HandleWaitInitAsync(robot, ct);
                        break;

                    case RobotCommandType.Ready:
                        HandleReady();
                        break;

                    case RobotCommandType.AtCatch:
                        await HandleAtCatchAsync(robot, cmd, ct);
                        break;

                    case RobotCommandType.Clamp:
                        HandleClamp(signal);
                        break;

                    case RobotCommandType.Grabbed:
                        HandleGrabbed();
                        break;

                    case RobotCommandType.Check:
                        await HandleCheckAsync(robot, signal, ct);
                        break;

                    case RobotCommandType.FlyCapture:
                        HandleFlyCapture();
                        break;

                    case RobotCommandType.FlyOffset:
                        HandleFlyOffset(cmd);
                        break;

                    case RobotCommandType.Release:
                        HandleRelease(ctx, signal);
                        break;

                    case RobotCommandType.Placed:
                        HandlePlaced(ctx);
                        break;

                    case RobotCommandType.BatchDone:
                        HandleBatchDone();
                        break;

                    case RobotCommandType.Error:
                        HandleError(cmd);
                        break;

                    default:
                        LogWarning("未知命令类型: {0}, 原始消息: {1}", cmd.Type, cmd.RawMessage);
                        break;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LogError(ex, "处理命令 {0} 异常", cmd.Type);
            }
        }

        #endregion

        #region 各命令处理方法

        /// <summary>WAIT_INIT → 发送 INIT</summary>
        private async Task HandleWaitInitAsync(IRobotDevice robot, CancellationToken ct)
        {
            LogInfo("收到 WAIT_INIT，发送 INIT");
            await robot.SendCommandAsync("INIT", ct);
        }

        /// <summary>READY → 设置标志触发 VisionCoordinateHandler</summary>
        private void HandleReady()
        {
            LogInfo("收到 READY，机器人已就绪");
            FlagCondition.SetFlag("Robot_Ready_Received", true);
        }

        /// <summary>AT_CATCH:n → 直接回复 GRAB</summary>
        private async Task HandleAtCatchAsync(IRobotDevice robot, RobotCommand cmd, CancellationToken ct)
        {
            LogInfo("收到 AT_CATCH:{0}，回复 GRAB", cmd.Data);
            await robot.SendCommandAsync("GRAB", ct);
        }

        /// <summary>CLAMP → 写 PLC 夹爪信号 = true</summary>
        private void HandleClamp(ISignalAccessor signal)
        {
            LogInfo("收到 CLAMP，执行柔爪抓取");
            try
            {
                signal.WriteBit(SIG_GRIPPER, true);
                LogDebug("写入夹爪信号: {0} = TRUE", SIG_GRIPPER);
            }
            catch (Exception ex)
            {
                LogWarning("夹爪抓取信号写入失败: {0}", ex.Message);
            }
        }

        /// <summary>GRABBED → 记录日志</summary>
        private void HandleGrabbed()
        {
            LogDebug("收到 GRABBED，抓取动作完成");
        }

        /// <summary>CHECK → 读 PLC 传感器，回复 MAT:1 或 MAT:0</summary>
        private async Task HandleCheckAsync(IRobotDevice robot, ISignalAccessor signal, CancellationToken ct)
        {
            LogInfo("收到 CHECK，检测物料");

            bool hasMaterial = false;
            try
            {
                hasMaterial = signal.ReadBit(SIG_MATERIAL_DETECT);
            }
            catch (Exception ex)
            {
                LogWarning("读取物料传感器失败: {0}", ex.Message);
            }

            string response = hasMaterial ? "MAT:1" : "MAT:0";
            LogInfo("物料检测结果: {0}", response);

            await robot.SendCommandAsync(response, ct);
        }

        /// <summary>
        /// FLY_CAPTURE → 仅记录日志
        /// 
        /// [v2.2] 飞拍偏差数据走 AR ↔ VM 的 HeadCam TCP 直连通道，
        /// C# 不再参与偏差读取和纠偏计算。
        /// </summary>
        private void HandleFlyCapture()
        {
            LogInfo("收到 FLY_CAPTURE（飞拍已触发，偏差走AR-VM直连）");
        }

        /// <summary>
        /// FLY_OFFSET;x;y;c;status → 记录偏差日志供监控
        /// 
        /// [v2.2] 由 AR 在本地解析偏差后转发给 C#，仅用于日志记录和UI显示。
        /// </summary>
        private void HandleFlyOffset(RobotCommand cmd)
        {
            LogInfo("飞拍偏差: {0}", cmd.Data);
        }

        /// <summary>RELEASE → 写 PLC 松爪信号 + 种子追踪</summary>
        private void HandleRelease(IHandlerContext ctx, ISignalAccessor signal)
        {
            LogInfo("收到 RELEASE，执行柔爪松开");

            try
            {
                signal.WriteBit(SIG_GRIPPER, false);
                LogDebug("写入松爪信号: {0} = FALSE", SIG_GRIPPER);
            }
            catch (Exception ex)
            {
                LogWarning("柔爪松开信号写入失败: {0}", ex.Message);
            }

            // 种子追踪：在工位1创建新种子
            try
            {
                var tracker = ctx.GetService<ISlotSeedTracker>();
                if (tracker != null && tracker.IsInitialized)
                {
                    string seedId = tracker.PlaceSeed(LOADING_SLOT, "Seed");
                    tracker.AttachData(seedId, "LoadingTime", DateTime.Now);
                    tracker.AttachData(seedId, "LoadingTurntableIndex", tracker.TurntableIndex);
                    LogInfo("工位{0}创建新种子: {1}", LOADING_SLOT, seedId);
                }
            }
            catch (Exception ex)
            {
                LogWarning("种子追踪失败: {0}", ex.Message);
            }
        }

        /// <summary>PLACED → 更新生产计数</summary>
        private void HandlePlaced(IHandlerContext ctx)
        {
            LogInfo("收到 PLACED，更新生产计数");
            ctx.Production.IncrementCount();
        }

        /// <summary>BATCH_DONE → 设置标志触发 VisionCoordinateHandler</summary>
        private void HandleBatchDone()
        {
            LogInfo("收到 BATCH_DONE，批次处理完成");
            FlagCondition.SetFlag("Robot_BatchDone_Received", true);
        }

        /// <summary>ERR:msg → 触发报警</summary>
        private void HandleError(RobotCommand cmd)
        {
            LogWarning("收到机器人错误: {0}", cmd.Data);
        }

        #endregion
    }
}