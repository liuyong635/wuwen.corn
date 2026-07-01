using SeedCut.Framework.Core;
using SeedCut.Framework.Core.SlotSeedTracker;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VM.Core;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// 大料盘视觉处理器
    /// 
    /// 功能：
    /// 1. 响应大料盘拍照请求信号
    /// 2. 执行视觉流程检测大料盘中的种子位置
    /// 3. 将检测结果（坐标数据）推送到DataPipeline
    /// 4. 用于摆盘、质检等后续处理
    /// 
    /// 触发条件：
    /// - 系统运行中
    /// - 收到大料盘拍照请求
    /// - 自身非忙碌状态
    /// 
    /// 信号交互：
    /// 1. PLC发送拍照请求
    /// 2. Handler执行视觉处理
    /// 3. Handler设置拍照完成信号
    /// 
    /// 依赖设备：PLC, Vision
    /// 
    /// ★ 与C++对应关系：
    /// - C++ 函数: processLargeVision() + onLargeImageDataReady()
    /// - C++ 流程名: "大料盘有无检测"
    /// - C++ 模块: "DL目标检测G1" CNN模块
    /// - C++ 完成信号: 在触发时立即写入 M1012.6
    /// </summary>
    public class LargeTrayVisionHandler : SignalHandlerBase
    {
        #region 常量定义

        // PLC信号名称
        private const string SIG_PHOTO_REQUEST = "LargeTray_PhotoRequest";
        private const string SIG_PHOTO_COMPLETE = "LargeTray_PhotoComplete";

        // ★ 修复：视觉流程名称 - 与C++保持一致
        private const string VISION_PROCEDURE = "大料盘有无检测";
        private const int LARGE_TRAY_VISION_SLOT = 7;

        // 数据管道名称
        private const string PIPELINE_LARGE_TRAY_COORDS = "LargeTrayCoordinates";
        private const int SIGNAL_DEBOUNCE_MS = 1000;  // PLC信号防抖等待时间

        // 脉冲持续时间
        private const int PULSE_DURATION_MS = 50;

        #endregion

        #region Handler属性
        private long _lastProcessedTurntableIndex = -1;

        public override string HandlerId => "LargeTrayVision";

        public override string HandlerName => "大料盘视觉处理";

        public override int Priority => 78;

        public override string[] DependentDevices => new[] { "PLC", "Vision" };

        /// <summary>
        /// 触发条件：运行中 + 拍照请求 + 非忙碌
        /// </summary>
        public override ITriggerCondition TriggerCondition => When.All(
            When.IsRunning(),
            When.SignalOn(SIG_PHOTO_REQUEST),
            When.FlagOff("LargeTrayVision_Busy")
        );

        #endregion

        #region 执行逻辑

        protected override async Task<ValueTuple<bool, string>> ExecuteAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            // 设置忙碌标志
            SetBusy(true);
            FlagCondition.SetFlag("LargeTrayVision_Busy", true);

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


                LogInfo("接收到大料盘拍照请求，发送完成信号");
                WriteSignalPulse(ctx, SIG_PHOTO_COMPLETE, PULSE_DURATION_MS);

                // 步骤2：执行视觉检测
                // ★ 与C++一致：使用"大料盘有无检测"流程
                LogInfo("开始执行大料盘视觉检测: {0}", VISION_PROCEDURE);

                var visionResult = await vision.ExecuteAsync(VISION_PROCEDURE, ct);
                bool hasSeed = false;
                int seedCount = 0;
                var procedure = vision.GetLastProcedure() as VmProcedure;
                if (procedure != null)
                {
                    ExtractTrayVisionData(procedure, "Large", out hasSeed, out seedCount);
                }
                // ★ 获取追踪器和种子ID
                var tracker = ctx.GetService<ISlotSeedTracker>();
                string seedId = null;

                if (tracker != null && tracker.IsInitialized)
                {
                    seedId = tracker.GetSeedIdAt(LARGE_TRAY_VISION_SLOT);
                    long turntableIndex = tracker.TurntableIndex;

                    // ★ 防重入检查
                    if (turntableIndex > 0 && turntableIndex == _lastProcessedTurntableIndex)
                    {
                        LogDebug("同一转盘周期内已处理过(Index={0})，跳过", turntableIndex);
                        WriteSignalPulse(ctx, SIG_PHOTO_COMPLETE, PULSE_DURATION_MS);
                        return Success("同一转盘周期，跳过");
                    }
                    _lastProcessedTurntableIndex = turntableIndex;

                    LogInfo("工位{0}种子: {1}", LARGE_TRAY_VISION_SLOT, seedId ?? "null");
                }

                if (!visionResult.Success)
                {
                    LogWarning("视觉检测失败: {0}", visionResult.Message);
                    // 即使失败也发送完成信号
                    return Fail("视觉检测失败: " + visionResult.Message);
                }

                LogInfo("视觉检测完成，耗时: {0}ms", visionResult.ExecutionTime.TotalMilliseconds);

                // 步骤3：提取坐标数据
                // ★ VisionDeviceAdapter 已从 "DL目标检测G1" CNN模块提取坐标数据
                var coordinates = ExtractCoordinates(visionResult);
                seedCount = coordinates.Count;


                // ★ 附加视觉数据到种子
                if (tracker != null && !string.IsNullOrEmpty(seedId))
                {
                    tracker.AttachData(seedId, "LargeTrayVisionTime", DateTime.Now);
                    tracker.AttachData(seedId, "LargeTrayVisionSuccess", visionResult.Success);
                    tracker.AttachData(seedId, "PostCutSeedCount", seedCount);  // 切片后种子数量

                    // ★ 可选：标记种子生命周期完成（如果这是最后一个检测工位）
                     tracker.AttachData(seedId, "ProcessComplete", true);
                }

                LogInfo("检测到 {0} 个种子位置", seedCount);

                // 步骤4：推送坐标到管道
                if (seedCount > 0)
                {
                    var pipeline = ctx.DataFlow.GetOrCreate<TrayCoordinate>(PIPELINE_LARGE_TRAY_COORDS);

                    // 清空旧数据
                    while (pipeline.TryPop(out _)) { }

                    foreach (var coord in coordinates)
                    {
                        pipeline.Push(coord);
                    }

                    LogDebug("已推送 {0} 个坐标到管道", seedCount);

                    // 设置数据就绪标志
                    FlagCondition.SetFlag("LargeTrayVision_DataReady", true);
                }

                // 步骤5：保存检测结果到上下文
                ctx.SetFlag("LargeTray_SeedCount", seedCount);
                ctx.SetFlag("LargeTray_LastVisionTime", DateTime.Now);

                // 步骤6：发送完成信号
                LogInfo("发送大料盘视觉完成信号");

                return Success(string.Format("大料盘视觉完成，检测到 {0} 个种子", seedCount));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogError(ex, "大料盘视觉处理异常");
                return Fail("处理异常: " + ex.Message);
            }
            finally
            {
                try
                {
                    await Task.Delay(SIGNAL_DEBOUNCE_MS, CancellationToken.None);
                }
                catch { }
                // 清除忙碌标志
                // ★ 与C++一致：在 onLargeVisionFinished 中直接清除
                SetBusy(false);
                FlagCondition.SetFlag("LargeTrayVision_Busy", false);
            }
        }

        #endregion

        #region 辅助方法

        #region 视觉数据提取

        /// <summary>
        /// 提取料盘视觉检测数据（CNN检测结果）
        /// </summary>
        private void ExtractTrayVisionData(VmProcedure procedure, string trayType,
            out bool hasSeed, out int seedCount)
        {
            hasSeed = false;
            seedCount = 0;

            try
            {
                // 获取 CNN 模块 "DL目标检测G1"
                dynamic cnnTool = null;
                try
                {
                    cnnTool = procedure["DL目标检测G1"];
                }
                catch
                {
                    LogDebug("未找到DL目标检测G1模块");
                    return;
                }

                if (cnnTool == null) return;

                dynamic cnnResult = null;
                try
                {
                    cnnResult = cnnTool.GetResult();
                }
                catch
                {
                    cnnResult = cnnTool.ModuResult;
                }

                if (cnnResult == null) return;

                int predictNum = 0;
                try
                {
                    predictNum = cnnResult.GetPredictNumber();
                }
                catch
                {
                    predictNum = cnnResult.PredictNumber;
                }

                if (predictNum == 0) return;

                // 提取检测结果
                int validCount = 0;
                for (int i = 0; i < predictNum; i++)
                {
                    try
                    {
                        var predictInfo = cnnResult.GetPredictInfo(i);
                        if (predictInfo == null) continue;

                        // 大料盘直接计数，小料盘需要检查 Label == 1
                        if (trayType == "Small")
                        {
                            int label = predictInfo.GetLabel();
                            if (label != 1) continue;
                        }

                        validCount++;
                    }
                    catch { }
                }

                seedCount = validCount;
                hasSeed = validCount > 0;
                LogDebug("{0}料盘检测到 {1} 个有效目标", trayType, validCount);
            }
            catch (Exception ex)
            {
                LogDebug("ExtractTrayVisionData异常: {0}", ex.Message);
            }
        }

        #endregion

        /// <summary>
        /// 从视觉结果中提取坐标数据
        /// 
        /// ★ 与C++逻辑对应：
        /// C++ 使用 "DL目标检测G1" CNN模块，提取:
        /// - GetPredictNumber() 检测数量
        /// - GetPredictInfo(0)->GetPredictBox() 检测框信息
        ///   - CenterPoint.X/Y, Angle, BoxWidth, BoxHeight
        /// 
        /// VisionDeviceAdapter 会将这些数据放入 Results 字典:
        /// - SeedCount, HasSeed
        /// - X0, Y0, A0, Width0, Height0 (第一个检测结果)
        /// </summary>
        private List<TrayCoordinate> ExtractCoordinates(VisionDeviceResult result)
        {
            var coordinates = new List<TrayCoordinate>();

            if (result?.Results == null)
                return coordinates;

            // 方式1：从 VisionDeviceAdapter 提取的索引坐标获取 (X0,Y0,A0, X1,Y1,A1 ...)
            int index = 0;
            while (true)
            {
                string xKey = string.Format("X{0}", index);
                string yKey = string.Format("Y{0}", index);
                string aKey = string.Format("A{0}", index);

                if (!result.Results.ContainsKey(xKey))
                    break;

                var coord = new TrayCoordinate
                {
                    Index = index,
                    X = result.GetDouble(xKey),
                    Y = result.GetDouble(yKey),
                    Angle = result.GetDouble(aKey, 0),
                    TrayType = TrayType.Large,
                    Timestamp = DateTime.Now
                };

                // 提取宽高信息（如果有）
                string wKey = string.Format("Width{0}", index);
                string hKey = string.Format("Height{0}", index);
                if (result.Results.ContainsKey(wKey))
                {
                    coord.Width = result.GetDouble(wKey);
                }
                if (result.Results.ContainsKey(hKey))
                {
                    coord.Height = result.GetDouble(hKey);
                }

                coordinates.Add(coord);
                index++;
            }

            // 方式2：从 SlotCoords 数组获取
            if (coordinates.Count == 0 && result.Results.ContainsKey("SlotCoords"))
            {
                var slotData = result.Results["SlotCoords"];
                if (slotData is IEnumerable<object> slots)
                {
                    foreach (var slot in slots)
                    {
                        if (slot is IDictionary<string, object> dict)
                        {
                            var coord = new TrayCoordinate
                            {
                                Index = coordinates.Count,
                                X = Convert.ToDouble(dict.ContainsKey("X") ? dict["X"] : 0),
                                Y = Convert.ToDouble(dict.ContainsKey("Y") ? dict["Y"] : 0),
                                Angle = Convert.ToDouble(dict.ContainsKey("Angle") ? dict["Angle"] : 0),
                                SlotId = dict.ContainsKey("SlotId") ? Convert.ToInt32(dict["SlotId"]) : -1,
                                HasSeed = dict.ContainsKey("HasSeed") ? Convert.ToBoolean(dict["HasSeed"]) : true,
                                TrayType = TrayType.Large,
                                Timestamp = DateTime.Now
                            };
                            coordinates.Add(coord);
                        }
                    }
                }
            }

            // 方式3：获取种子数量和位置信息
            if (coordinates.Count == 0)
            {
                int seedCount = result.GetInt("SeedCount", 0);
                for (int i = 0; i < seedCount; i++)
                {
                    string posKey = string.Format("Pos{0}", i);
                    if (result.Results.ContainsKey(posKey))
                    {
                        var posData = result.Results[posKey];
                        if (posData is string posStr)
                        {
                            // 解析 "X,Y,A" 格式
                            var parts = posStr.Split(',');
                            if (parts.Length >= 2)
                            {
                                var coord = new TrayCoordinate
                                {
                                    Index = i,
                                    X = double.TryParse(parts[0], out var x) ? x : 0,
                                    Y = double.TryParse(parts[1], out var y) ? y : 0,
                                    Angle = parts.Length > 2 && double.TryParse(parts[2], out var a) ? a : 0,
                                    TrayType = TrayType.Large,
                                    Timestamp = DateTime.Now
                                };
                                coordinates.Add(coord);
                            }
                        }
                    }
                }
            }

            return coordinates;
        }

        #endregion

        #region 静态方法

        /// <summary>
        /// 检查大料盘视觉数据是否就绪
        /// </summary>
        public static bool IsDataReady()
        {
            return FlagCondition.GetFlag("LargeTrayVision_DataReady");
        }

        /// <summary>
        /// 清除数据就绪标志
        /// </summary>
        public static void ClearDataReady()
        {
            FlagCondition.SetFlag("LargeTrayVision_DataReady", false);
        }

        #endregion
    }

    #region 数据结构

    /// <summary>
    /// 托盘类型
    /// </summary>
    public enum TrayType
    {
        Small,
        Large
    }

    /// <summary>
    /// 托盘坐标数据
    /// </summary>
    public class TrayCoordinate
    {
        /// <summary>
        /// 坐标索引
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 槽位ID
        /// </summary>
        public int SlotId { get; set; } = -1;

        /// <summary>
        /// X坐标 (mm)
        /// </summary>
        public double X { get; set; }

        /// <summary>
        /// Y坐标 (mm)
        /// </summary>
        public double Y { get; set; }

        /// <summary>
        /// 角度 (度)
        /// </summary>
        public double Angle { get; set; }

        /// <summary>
        /// 宽度 (像素或mm)
        /// </summary>
        public double Width { get; set; }

        /// <summary>
        /// 高度 (像素或mm)
        /// </summary>
        public double Height { get; set; }

        /// <summary>
        /// 是否有种子
        /// </summary>
        public bool HasSeed { get; set; } = true;

        /// <summary>
        /// 托盘类型
        /// </summary>
        public TrayType TrayType { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        public override string ToString()
        {
            return string.Format("TrayCoord[{0}]: Slot{1} ({2:F2}, {3:F2}, {4:F1}°) {5}",
                Index, SlotId, X, Y, Angle, HasSeed ? "有种子" : "空");
        }
    }

    #endregion
}