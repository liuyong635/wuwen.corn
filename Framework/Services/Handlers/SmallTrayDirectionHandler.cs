using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using VM.Core;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// 小料盘方向检测处理器
    /// 
    /// 功能：
    /// 1. 响应小料盘方向检测请求信号
    /// 2. 执行视觉检测料盘方向
    /// 3. 根据检测结果设置方向正确/错误信号
    /// 
    /// 触发条件：
    /// - 系统运行中
    /// - 收到小料盘方向检测请求 (SmallTray_DetectDirRequest)
    /// - 自身非忙碌状态
    /// 
    /// 信号交互：
    /// 1. PLC发送 SmallTray_DetectDirRequest = true (M3428.1)
    /// 2. Handler执行视觉检测
    /// 3. Handler设置 SmallTray_DirCorrect (M3428.2) 或 SmallTray_DirError (M3428.3)
    /// 
    /// 依赖设备：PLC, Vision
    /// 
    /// ★ 与C++对应关系：
    /// - C++ 函数: processSmallCheck() + onSmallCheckDataReady()
    /// - C++ 流程名: "小料盘有无检测" (与视觉检测共用同一流程)
    /// - C++ 模块: "料盘摆放检测" If模块
    /// - C++ 判断逻辑: 检查 GetNIfResult() == 0 表示正确 (当前被注释，默认返回正确)
    /// </summary>
    public class SmallTrayDirectionHandler : SignalHandlerBase
    {
        #region 常量定义

        // PLC信号名称
        private const string SIG_DETECT_REQUEST = "SmallTray_DetectDirRequest";
        private const string SIG_DIR_CORRECT = "SmallTray_DirCorrect";
        private const string SIG_DIR_ERROR = "SmallTray_DirError";
        private const string SIG_FEED_IN_PLACE = "WaitSmallTray_FeedInPlace";

        // ★ 修复：视觉流程名称 - 与C++保持一致
        // C++ 中方向检测和视觉检测使用同一个流程
        private const string VISION_PROCEDURE = "小料盘有无检测";

        // 超时配置
        private static readonly TimeSpan VISION_TIMEOUT = TimeSpan.FromSeconds(10);

        // ★ 与C++一致：500ms延时清除忙碌标志
        private const int BUSY_CLEAR_DELAY_MS = 500;

        #endregion

        #region Handler属性

        public override string HandlerId => "SmallTrayDirection";

        public override string HandlerName => "小料盘方向检测";

        public override int Priority => 75;

        public override string[] DependentDevices => new[] { "PLC", "Vision" };

        /// <summary>
        /// 触发条件：运行中 + 方向检测请求 + 非忙碌
        /// </summary>
        public override ITriggerCondition TriggerCondition => When.All(
            When.IsRunning(),
            When.SignalOn(SIG_DETECT_REQUEST),
            When.FlagOff("SmallTrayDirection_Busy")
        );

        #endregion

        #region 执行逻辑

        protected override async Task<ValueTuple<bool, string>> ExecuteAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            // 设置忙碌标志
            SetBusy(true);
            FlagCondition.SetFlag("SmallTrayDirection_Busy", true);

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

                // 步骤1：清除之前的状态信号
                WriteSignal(ctx, SIG_DIR_CORRECT, false);
                WriteSignal(ctx, SIG_DIR_ERROR, false);

                //// 步骤2：检查料盘是否到位
                //if (!ReadSignal(ctx, SIG_FEED_IN_PLACE))
                //{
                //    LogWarning("小料盘未到位，无法检测方向");
                //    return Fail("料盘未到位");
                //}

                // 步骤3：执行视觉检测
                // ★ 与C++一致：使用"小料盘有无检测"流程
                LogInfo("开始执行小料盘方向检测，流程: {0}", VISION_PROCEDURE);

                var visionResult = await vision.ExecuteAsync(VISION_PROCEDURE, ct);

                if (!visionResult.Success)
                {
                    LogWarning("视觉检测失败: {0}", visionResult.Message);
                    WriteSignal(ctx, SIG_DIR_ERROR, true);
                    return Fail("视觉检测失败: " + visionResult.Message);
                }
                // 自己提取方向检测结果
                bool directionCorrect = true;  // 默认正确（与C++一致）
                var procedure = vision.GetLastProcedure() as VmProcedure;
                if (procedure != null)
                {
                    directionCorrect = ExtractSmallTrayDirection(procedure);
                }
                // 步骤4：解析检测结果
                // ★ VisionDeviceAdapter 已从 "料盘摆放检测" If模块提取 Direction 字段
                directionCorrect = ParseDirectionResult(visionResult);

                // 步骤5：设置结果信号
                // ★ 与C++一致：写入 M3428.2 (正确) 或 M3428.3 (错误)
                if (directionCorrect)
                {
                    LogInfo("小料盘方向正确");
                    WriteSignal(ctx, SIG_DIR_CORRECT, true);
                    WriteSignal(ctx, SIG_DIR_ERROR, false);
                }
                else
                {
                    LogWarning("小料盘方向错误");
                    WriteSignal(ctx, SIG_DIR_CORRECT, false);
                    WriteSignal(ctx, SIG_DIR_ERROR, true);

                    // 记录错误计数
                    ctx.Production.IncrementError();
                }

                return Success(directionCorrect ? "方向正确" : "方向错误");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogError(ex, "方向检测处理异常");
                // 设置错误信号
                try
                {
                    WriteSignal(ctx, SIG_DIR_ERROR, true);
                }
                catch { }
                return Fail("处理异常: " + ex.Message);
            }
            finally
            {
                // ★ 与C++一致：延时500ms后清除忙碌标志
                _ = Task.Delay(BUSY_CLEAR_DELAY_MS).ContinueWith(_ =>
                {
                    SetBusy(false);
                    FlagCondition.SetFlag("SmallTrayDirection_Busy", false);
                });
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 解析视觉检测结果，判断方向是否正确
        /// 
        /// ★ 与C++逻辑对应：
        /// C++ 使用 "料盘摆放检测" If模块，检查:
        /// - GetNIfResult() == 0 表示正确
        /// 
        /// 但C++当前该逻辑被注释，默认返回正确
        /// VisionDeviceAdapter 会提取 Direction 字段
        /// </summary>
        private bool ParseDirectionResult(VisionDeviceResult result)
        {
            if (result?.Results == null)
            {
                // ★ 与C++当前行为一致：默认返回正确
                LogWarning("视觉结果为空，默认方向正确");
                return true;
            }

            // 优先使用 VisionDeviceAdapter 提取的 Direction 字段
            if (result.Results.ContainsKey("Direction"))
            {
                var direction = result.GetString("Direction", "").ToUpperInvariant();
                bool isCorrect = direction == "CORRECT" || direction == "OK" || direction == "1" || direction == "TRUE";

                // 记录检测方法
                string method = result.Results.ContainsKey("DirectionCheckMethod")
                    ? result.GetString("DirectionCheckMethod", "Unknown")
                    : "Unknown";
                LogDebug("方向检测结果: {0}, 检测方法: {1}", direction, method);

                return isCorrect;
            }

            // 兼容：检查 If 模块相关字段
            if (result.Results.ContainsKey("IfModuleResult"))
            {
                int ifResult = result.GetInt("IfModuleResult", -1);
                // 与C++逻辑一致：GetNIfResult() == 0 表示正确
                bool isCorrect = ifResult == 0;
                LogDebug("If模块检测: 结果={0}, 判定={1}", ifResult, isCorrect ? "正确" : "错误");
                return isCorrect;
            }

            // ★ 与C++当前行为一致：默认返回正确
            LogWarning("视觉结果中未找到方向信息，默认为正确");
            return true;
        }
        /// <summary>
        /// 提取小料盘方向检测数据（If模块）
        /// </summary>
        private bool ExtractSmallTrayDirection(VmProcedure procedure)
        {
            try
            {
                // 尝试获取 If 模块 "料盘摆放检测"
                dynamic ifTool = null;
                try
                {
                    ifTool = procedure["料盘摆放检测"];
                }
                catch
                {
                    LogDebug("未找到料盘摆放检测模块");
                    return true;
                }

                if (ifTool == null) return true;

                dynamic ifResult = null;
                try
                {
                    ifResult = ifTool.GetResult();
                }
                catch
                {
                    ifResult = ifTool.ModuResult;
                }

                if (ifResult == null) return true;

                try
                {
                    int nIfResult = ifResult.GetNIfResult();
                    LogDebug("If模块结果: {0}", nIfResult);

                    // ★ 与C++逻辑一致：GetNIfResult() == 0 表示正确
                    // return nIfResult == 0;
                }
                catch (Exception ex)
                {
                    LogDebug("提取If模块结果失败: {0}", ex.Message);
                }

                // 当前与C++保持一致，默认返回正确
                return true;
            }
            catch (Exception ex)
            {
                LogDebug("ExtractSmallTrayDirection异常: {0}", ex.Message);
                return true;
            }
        }
        #endregion
    }
}