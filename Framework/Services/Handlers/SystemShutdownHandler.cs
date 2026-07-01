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
    /// 系统停止处理器
    /// 
    /// 与 SystemStartupHandler 对称，负责系统停止和资源清理。
    /// 
    /// 触发方式：
    /// - UI 停止按钮（设置 System_Stop_Request = true）
    /// - 测试序列结束时作为最后一个Handler执行
    /// - 生产停止时调用
    /// 
    /// 功能：
    /// 1. 停止AR程序（Modbus写命令）
    /// 2. 断开伺服使能（安全考虑）
    /// 3. 停止TCP服务器
    /// 4. 清理所有机器人相关Flag条件
    /// 5. 停止生产状态
    /// 6. 可选：关闭振动盘光源
    /// 
    /// 触发条件：
    /// - System_Stop_Request = true（停止按钮触发）
    /// - SystemShutdown_Busy = false
    /// 
    /// 对应C++代码：
    /// mainuser.cpp 停止流程
    /// - robotCtrl->StopAR();
    /// - robotCtrl->SetServoEnable(false);
    /// 
    /// 依赖设备：Robot（PLC可选）
    /// </summary>
    public class SystemShutdownHandler : SignalHandlerBase
    {
        #region 常量定义

        /// <summary>
        /// M1012.1 - 上位机启动（JOG模式，脉冲写入）
        /// 启动时写一次脉冲，停止时再写一次脉冲实现关闭
        /// </summary>
        private const string SIG_UPPERPC_START = "UpperPC_Start";

        /// <summary>
        /// 停止脉冲持续时间（毫秒）
        /// </summary>
        private const int STOP_PULSE_DURATION_MS = 1000;

        /// <summary>
        /// 停止AR程序的超时时间
        /// </summary>
        private static readonly TimeSpan STOP_AR_TIMEOUT = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 断使能的超时时间
        /// </summary>
        private static readonly TimeSpan SERVO_DISABLE_TIMEOUT = TimeSpan.FromSeconds(3);

        #endregion

        #region Handler属性

        public override string HandlerId => "SystemShutdown";

        /// <summary>
        /// 飞拍偏差检测流程名称
        /// </summary>
        private const string FLY_CAPTURE_PROCEDURE = "飞拍偏差检测";

        public override string HandlerName => "系统停止";

        /// <summary>
        /// 最低优先级（确保其他Handler先完成）
        /// </summary>
        public override int Priority => 10;

        /// <summary>
        /// 依赖设备：Robot是必须的，PLC可选
        /// </summary>
        public override string[] DependentDevices => new[] { "Robot" };

        /// <summary>
        /// 触发条件：
        /// - System_Stop_Request = true（停止按钮）
        /// - SystemShutdown_Busy = false
        /// 
        /// 注意：不检查 IsRunning()，因为停止时可能已经不在运行状态
        /// </summary>
        public override ITriggerCondition TriggerCondition => When.All(
            When.FlagOn("System_Stop_Request"),
            When.FlagOff("SystemShutdown_Busy")
        );

        #endregion

        #region 执行逻辑

        protected override async Task<ValueTuple<bool, string>> ExecuteAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            // 设置忙碌标志
            SetBusy(true);
            FlagCondition.SetFlag("SystemShutdown_Busy", true);

            // 立即清除停止请求标志，防止重复触发
            FlagCondition.SetFlag("System_Stop_Request", false);

            var allSuccess = true;

            try
            {
                LogInfo("========== 系统停止流程开始 ==========");

                // 获取设备
                var robot = ctx.GetRobot();
                var vibrator = ctx.GetVibrator();
                var vision = ctx.GetVision();  // ← 新增

                // ========== 步骤0: 写入上位机停止脉冲 M1012.1 ==========
                LogInfo("[步骤0] 写入上位机停止脉冲 M1012.1...");
                WriteSignalPulse(ctx, SIG_UPPERPC_START, 50);
                LogInfo("[步骤0] 停止脉冲发送完成 ✓");

                // ========== 步骤0.5: ★ 停止飞拍流程 ==========
                await StopFlyCaptureProcessAsync(vision, ct);

                // ========== 步骤1: 停止AR程序 ==========
                allSuccess &= await StopARProgramAsync(robot, ct);

                // ========== 步骤2: 断开伺服使能 ==========
                allSuccess &= await DisableServoAsync(robot, ct);

                // ========== 步骤3: 停止TCP服务器 ==========
                StopTcpServer(robot);

                // ========== 步骤4: 清理所有Flag条件 ==========
                ClearAllRobotFlags();
                // ========== 步骤4.5: 清除数据管道 ==========
                ClearAllDataPipelines(ctx);

                // ========== 步骤4.6: ★ 清理种子追踪器 ==========
                ClearSeedTracker(ctx);


                // ========== 步骤5: 关闭振动盘光源（可选）==========
                await DisableVibratorLightAsync(vibrator, ct);

                // ========== 步骤6: 停止生产状态 ==========
                if (ctx.Production.IsRunning)
                {
                    LogInfo("[步骤6] 停止生产状态...");
                    ctx.Production.Stop();
                    LogInfo("[步骤6] 生产状态已停止 ✓");
                }
                else
                {
                    LogInfo("[步骤6] 生产状态未运行，跳过");
                }

                LogInfo("========== 系统停止流程完成 ({0}) ==========",
                    allSuccess ? "全部成功" : "部分失败");

                return allSuccess
                    ? Success("系统停止成功")
                    : Success("系统停止完成（部分操作失败）");
            }
            catch (OperationCanceledException)
            {
                LogWarning("系统停止被取消");
                throw;
            }
            catch (Exception ex)
            {
                LogError(ex, "系统停止异常");
                return Fail("停止异常: " + ex.Message);
            }
            finally
            {
                // 清除忙碌标志
                SetBusy(false);
                FlagCondition.SetFlag("SystemShutdown_Busy", false);
            }
        }

        #endregion

        #region 私有方法 - 停止步骤

        /// <summary>
        /// 步骤4.6: 清理种子追踪器
        /// </summary>
        private void ClearSeedTracker(IHandlerContext ctx)
        {
            LogInfo("[步骤4.6] 清理种子追踪器...");

            try
            {
                var tracker = ctx.GetService<ISlotSeedTracker>();
                if (tracker != null)
                {
                    // 获取当前状态用于日志
                    var states = tracker.GetAllSlotStates();
                    int occupiedCount = 0;
                    foreach (var state in states)
                    {
                        if (!string.IsNullOrEmpty(state.SeedId))
                            occupiedCount++;
                    }

                    if (occupiedCount > 0)
                    {
                        LogInfo("[步骤4.6] 当前有 {0} 个工位有种子，将被清空", occupiedCount);
                    }

                    // 重新初始化（清空所有工位）
                    tracker.Initialize(clearAllSlots: true);
                    LogInfo("[步骤4.6] 种子追踪器已清理 ✓");
                }
                else
                {
                    LogDebug("[步骤4.6] 种子追踪器未注册，跳过");
                }
            }
            catch (Exception ex)
            {
                LogWarning("[步骤4.6] 清理种子追踪器异常: {0}", ex.Message);
            }
        }


        /// <summary>
        /// 步骤4.5: 清除所有数据管道
        /// 
        /// 说明：
        /// - 清除激光命令队列（LaserQueue）
        /// - 清除激光图像队列（LaserImageQueue）
        /// - 清除其他可能存在的数据管道
        /// - 确保下次启动时不会有残留数据
        /// </summary>
        private void ClearAllDataPipelines(IHandlerContext ctx)
        {
            LogInfo("[步骤4.5] 清除数据管道...");

            try
            {
                // 方案1：清除所有管道数据（推荐）
                ctx.DataFlow.ClearAll();
                SeedCut.Framework.Services.Handlers.RobotCommand.ClearQueue();


                LogInfo("[步骤4.5] 数据管道已清除 ✓");
            }
            catch (Exception ex)
            {
                // 清除失败不影响主流程
                LogWarning("[步骤4.5] 清除数据管道异常: {0}", ex.Message);
            }
        }

        /// <summary>
        /// 步骤1: 停止AR程序
        /// </summary>
        private async Task<bool> StopARProgramAsync(IRobotDevice robot, CancellationToken ct)
        {
            LogInfo("[步骤1] 停止AR程序...");

            if (robot == null)
            {
                LogWarning("[步骤1] 机器人设备未注册，跳过");
                return true;
            }

            if (!robot.IsConnected)
            {
                LogWarning("[步骤1] 机器人未连接，跳过");
                return true;
            }

            if (!robot.IsProgramRunning)
            {
                LogInfo("[步骤1] AR程序未运行，跳过");
                return true;
            }

            try
            {
                var result = await robot.StopAsync(ct);
                if (result)
                {
                    LogInfo("[步骤1] AR程序已停止 ✓");
                    return true;
                }
                else
                {
                    LogWarning("[步骤1] AR程序停止失败");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogWarning("[步骤1] 停止AR程序异常: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 步骤2: 断开伺服使能
        /// </summary>
        private async Task<bool> DisableServoAsync(IRobotDevice robot, CancellationToken ct)
        {
            LogInfo("[步骤2] 断开伺服使能...");

            if (robot == null || !robot.IsConnected)
            {
                LogInfo("[步骤2] 机器人不可用，跳过");
                return true;
            }

            if (!robot.IsServoOn)
            {
                LogInfo("[步骤2] 伺服未使能，跳过");
                return true;
            }

            try
            {
                var result = await robot.EnableServoAsync(false, ct);
                if (result)
                {
                    LogInfo("[步骤2] 伺服已断开 ✓");
                    return true;
                }
                else
                {
                    LogWarning("[步骤2] 断开伺服失败");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogWarning("[步骤2] 断开伺服异常: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 步骤3: 停止TCP服务器
        /// </summary>
        private void StopTcpServer(IRobotDevice robot)
        {
            LogInfo("[步骤3] 停止TCP服务器...");

            if (robot == null)
            {
                LogInfo("[步骤3] 机器人不可用，跳过");
                return;
            }

            try
            {
                robot.StopTcpServer();
                LogInfo("[步骤3] TCP服务器已停止 ✓");
            }
            catch (Exception ex)
            {
                LogWarning("[步骤3] 停止TCP服务器异常: {0}", ex.Message);
            }
        }



        /// <summary>
        /// 步骤4: 清理所有机器人相关的Flag条件
        /// </summary>
        private void ClearAllRobotFlags()
        {
            LogInfo("[步骤4] 清理Flag条件...");

            try
            {
                // ========== 新架构标志（重构后） ==========
                // 机器人命令相关
                FlagCondition.SetFlag("Robot_Command_Pending", false);
                FlagCondition.SetFlag("Robot_Ready_Received", false);
                FlagCondition.SetFlag("Robot_BatchDone_Received", false);

                // 视觉/振动协调标志
                FlagCondition.SetFlag("NeedVibrate", false);
                FlagCondition.SetFlag("Vibrate_Complete", false);

                // Handler 忙碌标志（新名称）
                FlagCondition.SetFlag("RobotAction_Busy", false);
                FlagCondition.SetFlag("VisionCoordinate_Busy", false);
                FlagCondition.SetFlag("DiskVibrate_Busy", false);

                // ========== 其他 Handler 忙碌标志（未改动的模块） ==========
                FlagCondition.SetFlag("SystemStartup_Busy", false);
                FlagCondition.SetFlag("LaserVision_Busy", false);
                FlagCondition.SetFlag("LaserCut_Busy", false);
                FlagCondition.SetFlag("BarcodeScan_Busy", false);
                FlagCondition.SetFlag("SmallTrayVision_Busy", false);
                FlagCondition.SetFlag("SmallTrayDirection_Busy", false);
                FlagCondition.SetFlag("LargeTrayVision_Busy", false);
                FlagCondition.SetFlag("LargeTrayDirection_Busy", false);

                // 系统请求标志
                FlagCondition.SetFlag("System_Start_Request", false);

                LogInfo("[步骤4] Flag条件已清理 ✓");
            }
            catch (Exception ex)
            {
                LogWarning("[步骤4] 清理Flag异常: {0}", ex.Message);
            }
        }

        /// <summary>
        /// 步骤5: 关闭振动盘光源（可选）
        /// </summary>
        private async Task DisableVibratorLightAsync(IVibratorDevice vibrator, CancellationToken ct)
        {
            if (vibrator == null || !vibrator.IsConnected)
            {
                LogDebug("[步骤5] 振动盘设备不可用，跳过光源关闭");
                return;
            }

            try
            {
                LogInfo("[步骤5] 关闭振动盘光源...");
                await vibrator.SetLightAAsync(false, ct);
                LogInfo("[步骤5] 振动盘光源已关闭 ✓");
            }
            catch (Exception ex)
            {
                // 光源控制失败不影响主流程
                LogDebug("[步骤5] 关闭振动盘光源失败: {0}", ex.Message);
            }
        }
        /// <summary>
        /// 步骤0.5: 停止飞拍偏差检测流程
        /// 
        /// 说明：
        /// - 在停止机器人AR程序之前停止飞拍流程
        /// - 确保不会在机器人停止后仍然有视觉流程在运行
        /// </summary>
        private async Task StopFlyCaptureProcessAsync(IVisionDevice vision, CancellationToken ct)
        {
            if (vision == null || !vision.IsConnected)
            {
                LogDebug("[步骤0.5] 视觉设备不可用，跳过飞拍流程停止");
                return;
            }

            try
            {
                // 检查是否正在运行
                if (!vision.IsContinuousRunning(FLY_CAPTURE_PROCEDURE))
                {
                    LogInfo("[步骤0.5] 飞拍流程未运行，跳过");
                    return;
                }

                LogInfo("[步骤0.5] 停止飞拍偏差检测流程...");

                var result = await vision.StopContinuousRunAsync(FLY_CAPTURE_PROCEDURE, ct);

                if (result)
                {
                    LogInfo("[步骤0.5] 飞拍流程已停止 ✓");
                }
                else
                {
                    LogWarning("[步骤0.5] 飞拍流程停止失败: {0}", vision.LastError);
                }
            }
            catch (Exception ex)
            {
                // 停止失败不影响主流程
                LogWarning("[步骤0.5] 停止飞拍流程异常: {0}", ex.Message);
            }
        }

        #endregion





    }
}