using SeedCut.Framework.Core;
using SeedCut.Framework.Core.SlotSeedTracker;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// 系统启动处理器（重构版）
    /// 
    /// 触发方式：UI 启动按钮
    /// 
    /// 前置条件：
    /// - M1002.7 (CircularAxis_InitInProgress) = 0（环形轴不在初始化中）
    /// 
    /// 功能：
    /// 1. 检查环形轴初始化状态
    /// 2. 重置生产计数器
    /// 3. 写入 M1012.1 脉冲（上位机-启动，JOG模式）
    /// 4. 使能机器人伺服
    /// 5. 运行机器人AR程序（触发机器人发送WAIT_INIT命令）
    /// 
    /// 触发条件：
    /// - System_Start_Request = true（启动按钮触发）
    /// - SystemStartup_Busy = false
    /// 
    /// 对应C++代码：
    /// mainuser.cpp slot_runProcessMain()
    /// - catchNumber = 0; LaserVisionNumber = 0; smallNumber = 0; largeNumber = 0;
    /// - diskCtrl->setLightA(true);
    /// - robotCtrl->SetServoEnable(true);
    /// - robotCtrl->RunAR();
    /// 
    /// 依赖设备：PLC, Robot
    /// </summary>
    public class SystemStartupHandler : SignalHandlerBase
    {
        #region 常量定义

        // ========== PLC 信号定义 ==========
        /// <summary>
        /// M1002.7 - 环形导轨轴初始化中 (1=进行中, 0=完成)
        /// 启动前必须确保此信号为0
        /// </summary>
        private const string SIG_CIRCULAR_INIT_IN_PROGRESS = "CircularAxis_InitInProgress";

        /// <summary>
        /// M1012.1 - 上位机启动（JOG模式，脉冲写入）
        /// </summary>
        private const string SIG_UPPERPC_START = "UpperPC_Start";

        private const string SIG_CUT_COMPLETE = "LaserCut_Complete";          // 1002.6


        /// <summary>
        /// 飞拍偏差检测流程名称
        /// </summary>
        private const string FLY_CAPTURE_PROCEDURE = "飞拍偏差检测";

        #endregion

        #region Handler属性

        public override string HandlerId => "SystemStartup";

        public override string HandlerName => "系统启动";

        /// <summary>
        /// 最高优先级
        /// </summary>
        public override int Priority => 100;

        public override string[] DependentDevices => new[] { "PLC", "Robot" };

        /// <summary>
        /// 触发条件：
        /// - System_Start_Request = true（启动按钮）
        /// - SystemStartup_Busy = false
        /// </summary>
        public override ITriggerCondition TriggerCondition => When.All(
            When.FlagOn("System_Start_Request"),
            When.FlagOff("SystemStartup_Busy")
        );

        #endregion

        #region 执行逻辑

        protected override async Task<ValueTuple<bool, string>> ExecuteAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            // 设置忙碌标志
            SetBusy(true);
            FlagCondition.SetFlag("SystemStartup_Busy", true);

            // 立即清除启动请求标志，防止重复触发
            FlagCondition.SetFlag("System_Start_Request", false);

            try
            {
                LogInfo("========== 系统启动流程开始 ==========");

                // 获取设备
                var plc = ctx.GetPlc();
                var robot = ctx.GetRobot();
                var vibrator = ctx.GetVibrator();
                var vision = ctx.GetVision();  // ← 新增

                // ========== 步骤0: 检查PLC ==========
                if (plc == null || !plc.IsConnected)
                {
                    LogError(null, "PLC未连接，无法启动");
                    return Fail("PLC未连接");
                }

                // ========== 步骤1: ★ 检查环形轴初始化状态 ==========
                LogInfo("[步骤1] 检查环形轴状态...");

                if (ReadSignal(ctx, SIG_CIRCULAR_INIT_IN_PROGRESS))
                {
                    var msg = "环形轴正在初始化中 (M1002.7=1)，请等待初始化完成后再启动";
                    LogWarning(msg);
                    return Fail(msg);
                }

                

                LogInfo("[步骤1] 环形轴状态正常 ✓");


                // ========== 步骤1.5: ★ 初始化种子追踪器 ==========
                LogInfo("[步骤1.5] 初始化种子追踪器...");
                var tracker = ctx.GetService<ISlotSeedTracker>();
                if (tracker != null)
                {
                    tracker.Initialize(clearAllSlots: true);
                    LogInfo("[步骤1.5] 种子追踪器已初始化 ✓");

                    
                    
                }
                else
                {
                    LogWarning("[步骤1.5] 种子追踪器未注册，跳过");
                }


                // ========== 步骤2: 重置生产计数 ==========
                LogInfo("[步骤2] 重置生产计数器...");
                ResetProductionCounters(ctx);
                LogInfo("[步骤2] 计数器已重置 ✓");

                // ========== 步骤2.5: ★ 清空激光命令队列 ==========
                LogInfo("[步骤2.5] 清空激光命令队列...");
                var laserQueue = ctx.DataFlow.Get<LaserCommand>("LaserQueue");
                if (laserQueue != null)
                {
                    laserQueue.Clear();
                    LogInfo("[步骤2.5] 激光命令队列已清空 ✓");
                }


                // ========== 步骤3: 启用振动盘光源（可选）==========
                await EnableVibratorLightAsync(vibrator, ct);

                // ========== 步骤4: ★ 写入上位机启动脉冲 M1012.1 ==========
                LogInfo("[步骤4] 写入上位机启动脉冲 M1012.1...");
                WriteSignalPulse(ctx, SIG_UPPERPC_START, 50);
                LogInfo("[步骤4] 启动脉冲发送完成 ✓");

                // ========== 步骤5: 检查机器人连接 ==========
                LogInfo("[步骤5] 检查机器人连接...");

                if (robot == null || !robot.IsConnected)
                {
                    LogError(null, "机器人未连接，无法启动");
                    return Fail("机器人未连接");
                }

                LogInfo("[步骤5] 机器人已连接 ✓");

                // ========== 步骤6: 使能机器人伺服 ==========
                LogInfo("[步骤6] 使能机器人伺服...");

                if (!robot.IsTcpServerRunning)
                {
                    var result = await robot.StartTcpServerAsync(6000, ct);  // ⭐ 使用接口方法
                    if (!result) return Fail("TCP服务器启动失败");
                }

                var servoResult = await EnableRobotServoAsync(robot, ct);
                if (!servoResult)
                {
                    LogError(null, "机器人伺服使能失败");
                    return Fail("机器人伺服使能失败");
                }

                LogInfo("[步骤6] 机器人伺服已使能 ✓");

                // ========== 步骤7: 运行AR程序 ==========
                LogInfo("[步骤7] 启动机器人AR程序...");

                var runResult = await RunRobotARProgramAsync(robot, ct);
                if (!runResult)
                {
                    LogError(null, "机器人AR程序启动失败");
                    return Fail("机器人AR程序启动失败");
                }

                LogInfo("[步骤7] AR程序已启动 ✓");

                //直接发送激光完成信号，跳过第一次激光
                WriteSignalPulse(ctx, SIG_CUT_COMPLETE);


                // ========== 步骤7.5: ★ 启动飞拍流程连续执行 ==========
                await StartFlyCaptureProcessAsync(vision, ct);

                // ========== 步骤8: 设置系统状态为运行中 ==========
                ctx.Production.Start();

                LogInfo("========== 系统启动流程完成 ✓ ==========");
                LogInfo("等待机器人发送WAIT_INIT命令...");




                return Success("系统启动成功，等待机器人初始化");
            }
            catch (OperationCanceledException)
            {
                LogWarning("系统启动被取消");
                throw;
            }
            catch (Exception ex)
            {
                LogError(ex, "系统启动异常");
                return Fail("启动异常: " + ex.Message);
            }
            finally
            {
                // 清除忙碌标志
                SetBusy(false);
                FlagCondition.SetFlag("SystemStartup_Busy", false);
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 重置生产计数器
        /// 对应C++: catchNumber = 0; LaserVisionNumber = 0; smallNumber = 0; largeNumber = 0;
        /// </summary>
        private void ResetProductionCounters(IHandlerContext ctx)
        {
            LogDebug("重置生产计数器");

            // 重置Production上下文中的计数
            ctx.Production.ResetCounts();

            // 重置其他相关标志（如果需要）
            ctx.SetFlag("CatchNumber", 0);
            ctx.SetFlag("LaserVisionNumber", 0);
            ctx.SetFlag("SmallNumber", 0);
            ctx.SetFlag("LargeNumber", 0);

            LogDebug("计数器已重置");
        }

        /// <summary>
        /// 启用振动盘光源
        /// 对应C++: diskCtrl->setLightA(true);
        /// </summary>
        private async Task EnableVibratorLightAsync(IVibratorDevice vibrator, CancellationToken ct)
        {
            if (vibrator == null || !vibrator.IsConnected)
            {
                LogDebug("振动盘设备不可用，跳过光源启用");
                return;
            }

            try
            {
                LogDebug("启用振动盘光源");

                // 注意：如果IVibratorDevice接口没有SetLightAsync方法，
                // 需要在接口中添加，或者通过PLC控制光源
                await vibrator.SetLightAAsync(true, ct);

                // 临时方案：通过PLC控制光源（如果光源由PLC控制）
                LogDebug("振动盘光源控制待实现（需要确认接口）");

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                // 光源控制失败不影响主流程
                LogWarning("启用振动盘光源失败: {0}", ex.Message);
            }
        }

       

        /// <summary>
        /// 使能机器人伺服
        /// 对应C++: robotCtrl->SetServoEnable(true);
        /// </summary>
        private async Task<bool> EnableRobotServoAsync(IRobotDevice robot, CancellationToken ct)
        {
            try
            {
                var result = await robot.EnableServoAsync(true, ct);

                if (result)
                {
                    LogDebug("机器人伺服使能成功");
                }
                else
                {
                    LogError(null, "机器人伺服使能失败");
                }

                return result;
            }
            catch (Exception ex)
            {
                LogError(ex, "机器人伺服使能异常");
                return false;
            }
        }

        /// <summary>
        /// 运行机器人AR程序
        /// 对应C++: robotCtrl->RunAR();
        /// 
        /// 关键：AR程序启动后，机器人会发送WAIT_INIT命令请求初始化
        /// </summary>
        private async Task<bool> RunRobotARProgramAsync(IRobotDevice robot, CancellationToken ct)
        {
            try
            {
                // RunProgramAsync内部会调用RunARAsync
                var result = await robot.RunProgramAsync("AR", ct);

                if (result)
                {
                    LogDebug("机器人AR程序启动成功");
                    LogDebug("机器人将发送WAIT_INIT命令，由RobotCommandHandler处理");
                }
                else
                {
                    LogError(null, "机器人AR程序启动失败");
                }

                return result;
            }
            catch (Exception ex)
            {
                LogError(ex, "机器人AR程序启动异常");
                return false;
            }
        }

        /// <summary>
        /// 启动飞拍偏差检测流程（连续执行，硬触发监听模式）
        /// 
        /// 飞拍流程说明：
        /// - 机器人DO18触发相机拍照
        /// - VM流程自动执行偏差计算
        /// - 结果通过HeadCam通道TCP发送给机器人
        /// - C#上位机不参与offset数据流，只负责启动/停止流程
        /// </summary>
        private async Task StartFlyCaptureProcessAsync(IVisionDevice vision, CancellationToken ct)
        {
            if (vision == null || !vision.IsConnected)
            {
                LogDebug("[步骤7.5] 视觉设备不可用，跳过飞拍流程启动");
                return;
            }

            try
            {
                LogInfo("[步骤7.5] 启动飞拍偏差检测流程...");

                // 检查流程是否存在
                var procedureNames = vision.GetProcedureNames();
                if (!procedureNames.Contains(FLY_CAPTURE_PROCEDURE))
                {
                    LogWarning("[步骤7.5] 飞拍流程 '{0}' 不存在，跳过", FLY_CAPTURE_PROCEDURE);
                    return;
                }

                // 启动连续执行（硬触发监听模式）
                var result = await vision.StartContinuousRunAsync(FLY_CAPTURE_PROCEDURE, 0, ct);

                if (result)
                {
                    LogInfo("[步骤7.5] 飞拍流程已启动 ✓");
                    LogInfo("         流程将等待机器人DO18触发信号");
                }
                else
                {
                    // 飞拍启动失败不阻断主流程（飞拍是可选功能）
                    LogWarning("[步骤7.5] 飞拍流程启动失败: {0}", vision.LastError);
                }
            }
            catch (Exception ex)
            {
                // 飞拍启动异常不阻断主流程
                LogWarning("[步骤7.5] 飞拍流程启动异常: {0}", ex.Message);
            }
        }

        #endregion
    }
}