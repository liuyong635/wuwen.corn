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
    /// 激光视觉处理器（全局变量版）
    /// 
    /// ★★★ 改造内容 ★★★
    /// 数据通道从 Group 模块输出 → 全局变量读取
    ///   - 移除 ExtractLaserTrajectoryData / ConvertStringDataArray
    ///   - 移除 IMVSBlobFindModuCs / IMVSGroupCs / VM.PlatformSDKCS 依赖
    ///   - 新增 ReadGlobalVarString / ReadGlobalVarInt（参考 VisionCoordinateHandler）
    ///   - VM 脚本_拼接路径 将结果写入全局变量 LaserRouteStr / LaserPathValid
    ///   - Handler 执行前清空全局变量，执行后读取
    /// 
    /// 功能：
    /// 1. 响应激光拍照请求信号
    /// 2. 执行"轨迹规划"视觉流程
    /// 3. 从全局变量读取激光路径字符串（AddLines[...]）
    /// 4. 将命令推送到DataPipeline供LaserCutHandler发送
    /// 
    /// 触发条件：
    /// - 系统运行中
    /// - 收到激光拍照请求 (LaserPhoto_Request) - 地址1002.3
    /// - 自身非忙碌状态
    /// 
    /// 依赖设备：PLC, Vision
    /// </summary>
    public class LaserVisionHandler : SignalHandlerBase
    {
        #region 常量定义

        // PLC信号名称（别名，由SignalAliasConfig映射）
        private const string SIG_PHOTO_REQUEST = "LaserPhoto_Request";      // 1002.3
        private const string SIG_PHOTO_COMPLETE = "LaserPhoto_Complete";    // 1002.4

        // 视觉流程名称（与C++ u8"轨迹规划" 对应）
        private const string VISION_PROCEDURE = "路径规划";

        private const int LASER_VISION_SLOT = 3;

        // 数据管道名称
        private const string PIPELINE_LASER_QUEUE = "LaserQueue";
        private const string PIPELINE_LASER_IMAGE = "LaserImageQueue";

        // 脉冲持续时间
        private const int PULSE_DURATION_MS = 50;

        // 完成后的延迟（给PLC处理时间）
        private const int COMPLETE_DELAY_MS = 200;

        // ★ 全局变量模块名称（与流程1共用）
        public const string GLOBAL_VAR_MODULE_NAME = "全局变量1";

        // ★ 全局变量名称
        private const string GVAR_LASER_ROUTE_STR = "LaserRouteStr";
        private const string GVAR_LASER_PATH_VALID = "LaserPathValid";

        #endregion

        #region 防重复入队

        /// <summary>
        /// 记录当前转盘周期内已处理的种子ID
        /// Key: (SeedId, TurntableIndex)
        /// </summary>
        private readonly HashSet<string> _processedKeys = new HashSet<string>();

        /// <summary>
        /// 上一次清理时的转盘索引（用于自动清理旧记录）
        /// </summary>
        private long _lastCleanupIndex = -1;

        /// <summary>
        /// 生成防重复 Key
        /// </summary>
        private static string MakeProcessedKey(string seedId, long turntableIndex)
        {
            return string.Format("{0}@{1}", seedId, turntableIndex);
        }

        #endregion

        #region Handler属性

        public override string HandlerId => "LaserVision";

        public override string HandlerName => "激光视觉处理";

        public override int Priority => 85;

        public override string[] DependentDevices => new[] { "PLC", "Vision" };

        /// <summary>
        /// 触发条件：运行中 + 激光拍照请求 + 非忙碌
        /// </summary>
        public override ITriggerCondition TriggerCondition => When.All(
            When.IsRunning(),
            When.SignalOn(SIG_PHOTO_REQUEST),
            When.FlagOff("LaserVision_Busy")
        );

        #endregion

        #region 执行逻辑

        protected override async Task<ValueTuple<bool, string>> ExecuteAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            // 设置忙碌标志
            SetBusy(true);
            FlagCondition.SetFlag("LaserVision_Busy", true);

            try
            {
                var vision = ctx.GetVision();

                if (vision == null)
                    return Fail("视觉设备未找到");

                if (!vision.IsConnected)
                    return Fail("视觉设备未连接");
                var recipe = ctx.GetService<IRecipeService>();

                // ---- 脚本A：ROI 计算 ----
                double roiWidth = recipe?.GetDouble("LaserROI", "RoiWidth", 1500.0,
                    name: "ROI宽度", unit: "px", description: "玉米检测区域矩形宽度") ?? 1500.0;
                double roiYMargin = recipe?.GetDouble("LaserROI", "RoiYMargin", 0,
                    name: "Y边距", unit: "px", description: "ROI上下方向内缩边距") ?? 0;

                // ---- 脚本B：切割路径计算 ----
                double cutAngleX = recipe?.GetDouble("LaserCut", "CutAngleX", 10.0,
                    name: "射线C角度", unit: "°", description: "第一条射线的起始角度") ?? 10.0;
                double cutAngleY = recipe?.GetDouble("LaserCut", "CutAngleY", 45.0,
                    name: "C-D夹角", unit: "°", description: "两条射线之间的夹角") ?? 45.0;
                double cutInsetMM = recipe?.GetDouble("LaserCut", "CutInsetMM", 3.0,
                    name: "内缩距离", unit: "mm", description: "从轮廓边缘向内收缩的距离") ?? 3.0;
                double cutExtendMM = recipe?.GetDouble("LaserCut", "CutExtendMM", 6.0,
                    name: "外延距离", unit: "mm", description: "从轮廓边缘向外延伸的距离") ?? 6.0;
                double pixelSize = recipe?.GetDouble("LaserCut", "PixelSize", 0.05,
                    name: "像素尺寸", unit: "mm/px", description: "相机标定的像素物理尺寸") ?? 0.05;
                // 获取追踪器和种子ID
                var tracker = ctx.GetService<ISlotSeedTracker>();
                string seedId = null;
                long turntableIndex = 0;

                if (tracker != null && tracker.IsInitialized)
                {
                    seedId = tracker.GetSeedIdAt(LASER_VISION_SLOT);
                    turntableIndex = tracker.TurntableIndex;

                    LogInfo("工位{0}种子: {1}, 转盘索引: {2}", LASER_VISION_SLOT, seedId ?? "null", turntableIndex);

                    // 自动清理旧周期的记录
                    if (turntableIndex > _lastCleanupIndex + 2)
                    {
                        _processedKeys.Clear();
                        _lastCleanupIndex = turntableIndex;
                    }

                    // 检查是否已处理过
                    if (!string.IsNullOrEmpty(seedId))
                    {
                        var processedKey = MakeProcessedKey(seedId, turntableIndex);
                        if (_processedKeys.Contains(processedKey))
                        {
                            LogDebug("种子 {0} 在索引 {1} 已入队，跳过重复请求", seedId, turntableIndex);
                            await SendPhotoCompleteAsync(ctx, ct);
                            return Success("重复请求，已跳过");
                        }
                    }

                    // 无追踪记录时跳过视觉处理
                    if (string.IsNullOrEmpty(seedId))
                    {
                        LogWarning("工位{0}无追踪记录，跳过视觉（可能是残留种子）", LASER_VISION_SLOT);
                        EnqueueEmptyCommand(ctx, null, LASER_VISION_SLOT, turntableIndex);
                        await SendPhotoCompleteAsync(ctx, ct);
                        return Success("跳过视觉：工位无追踪记录");
                    }
                }
                else
                {
                    // 追踪器不可用时也跳过
                    LogWarning("追踪器不可用，跳过视觉处理");
                    await SendPhotoCompleteAsync(ctx, ct);
                    return Success("跳过视觉：追踪器不可用");
                }

                // ========== 步骤1：清空全局变量 + 执行视觉流程 ==========
                LogInfo("开始执行激光视觉: {0}", VISION_PROCEDURE);

                // ★ 清空上一次残留值，防止误读
                ClearLaserGlobalVars();
                // 写入全局变量
                SetLaserVmGlobalParameters(
                    roiWidth, roiYMargin,
                    cutAngleX, cutAngleY,
                    cutInsetMM, cutExtendMM,
                    pixelSize);
                var visionResult = await vision.ExecuteAsync(VISION_PROCEDURE, ct);

                if (!visionResult.Success)
                {
                    LogWarning("激光视觉处理失败: {0}", visionResult.Message);

                    // 即使失败也要入队 Empty 并发送完成信号
                    EnqueueEmptyCommand(ctx, seedId, LASER_VISION_SLOT, turntableIndex);

                    // 附加失败信息到种子
                    if (tracker != null && !string.IsNullOrEmpty(seedId))
                    {
                        tracker.AttachData(seedId, "LaserVisionTime", DateTime.Now);
                        tracker.AttachData(seedId, "LaserVisionSuccess", false);
                        tracker.AttachData(seedId, "LaserVisionError", visionResult.Message);
                    }

                    await SendPhotoCompleteAsync(ctx, ct);
                    return Fail("视觉处理失败: " + visionResult.Message);
                }

                LogInfo("视觉处理完成，耗时: {0}ms", visionResult.ExecutionTime.TotalMilliseconds);

                // ========== 步骤2：从全局变量读取激光路径 ==========
                string laserCommand = "Empty";
                bool hasValidData = false;

                int pathValid = ReadGlobalVarInt(GVAR_LASER_PATH_VALID, 0);
                string routeStr = ReadGlobalVarString(GVAR_LASER_ROUTE_STR, "");

                LogDebug("全局变量: PathValid={0}, RouteStr长度={1}", pathValid, routeStr?.Length ?? 0);

                if (pathValid == 1
                    && !string.IsNullOrEmpty(routeStr)
                    && routeStr.StartsWith("AddLines["))
                {
                    laserCommand = routeStr;
                    hasValidData = true;
                    LogInfo("从全局变量读取路径成功, 长度: {0}", routeStr.Length);
                }
                else
                {
                    LogDebug("全局变量无有效路径: PathValid={0}, RouteStr={1}",
                        pathValid, string.IsNullOrEmpty(routeStr) ? "(空)" : routeStr);
                }

                // 附加数据到种子生命周期
                if (tracker != null && !string.IsNullOrEmpty(seedId))
                {
                    tracker.AttachData(seedId, "LaserVisionTime", DateTime.Now);
                    tracker.AttachData(seedId, "LaserVisionSuccess", true);
                    tracker.AttachData(seedId, "HasValidLaserData", hasValidData);
                    tracker.AttachData(seedId, "VisionTurntableIndex", turntableIndex);

                    if (hasValidData && laserCommand != "Empty")
                    {
                        tracker.AttachData(seedId, "LaserCommandLength", laserCommand.Length);
                    }
                }

                if (!hasValidData || string.IsNullOrEmpty(laserCommand) || laserCommand == "Empty")
                {
                    // 无有效数据，入队 Empty
                    LogWarning("无有效激光切割数据");
                    EnqueueEmptyCommand(ctx, seedId, LASER_VISION_SLOT, turntableIndex);
                }
                else
                {
                    // 有有效数据，入队命令
                    LogInfo("获取激光命令成功，长度: {0}", laserCommand.Length);
                    LogDebug("命令预览: {0}",
                        laserCommand.Length > 100 ? laserCommand.Substring(0, 100) + "..." : laserCommand);
                    EnqueueLaserCommand(ctx, laserCommand, seedId, LASER_VISION_SLOT, turntableIndex);

                    // 记录已处理
                    if (!string.IsNullOrEmpty(seedId))
                    {
                        _processedKeys.Add(MakeProcessedKey(seedId, turntableIndex));
                    }
                }

                // ========== 步骤3：保存图像数据（可选）==========
                if (visionResult.ImageData != null && visionResult.ImageData.Length > 0)
                {
                    var imagePipeline = ctx.DataFlow.GetOrCreate<byte[]>(PIPELINE_LASER_IMAGE);
                    imagePipeline.Push(visionResult.ImageData);
                    LogDebug("已保存视觉图像 ({0} bytes)", visionResult.ImageData.Length);
                }

                // ========== 步骤4：发送完成信号 ==========
                await SendPhotoCompleteAsync(ctx, ct);

                return Success(hasValidData ? "激光视觉完成，已生成切割命令" : "激光视觉完成（无有效数据）");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogError(ex, "激光视觉处理异常");
                return Fail("处理异常: " + ex.Message);
            }
            finally
            {
                // 清除忙碌标志
                SetBusy(false);
                FlagCondition.SetFlag("LaserVision_Busy", false);
            }
        }

        #endregion

        #region 辅助方法

        private void SetLaserVmGlobalParameters(
            double roiWidth, double roiYMargin,
            double cutAngleX, double cutAngleY,
            double cutInsetMM, double cutExtendMM,
            double pixelSize)
        {
            try
            {
                dynamic globalVar = VmSolution.Instance["全局变量1"];
                if (globalVar == null) return;

                // ---- 脚本A：ROI ----
                globalVar.SetGlobalVar("RoiWidth", roiWidth.ToString("F0"));
                globalVar.SetGlobalVar("RoiYMargin", roiYMargin.ToString("F1"));

                // ---- 脚本B：切割路径 ----
                globalVar.SetGlobalVar("CutAngleX", cutAngleX.ToString("F1"));
                globalVar.SetGlobalVar("CutAngleY", cutAngleY.ToString("F1"));
                globalVar.SetGlobalVar("CutInsetMM", cutInsetMM.ToString("F1"));
                globalVar.SetGlobalVar("CutExtendMM", cutExtendMM.ToString("F1"));
                globalVar.SetGlobalVar("PixelSize", pixelSize.ToString("F4"));

                // ---- 清除上一轮输出 ----
                globalVar.SetGlobalVar("LaserRouteStr", "");
                globalVar.SetGlobalVar("LaserPathValid", "0");

                LogDebug("已设置激光VM参数: AngleX={0}, AngleY={1}, Inset={2}mm, Extend={3}mm, Pixel={4}mm/px",
                    cutAngleX, cutAngleY, cutInsetMM, cutExtendMM, pixelSize);
            }
            catch (Exception ex)
            {
                LogWarning("设置激光VM全局变量失败: {0}", ex.Message);
            }
        }


        /// <summary>
        /// 发送拍照完成信号
        /// </summary>
        private async Task SendPhotoCompleteAsync(IHandlerContext ctx, CancellationToken ct)
        {
            LogInfo("发送激光视觉完成信号");
            WriteSignalPulse(ctx, SIG_PHOTO_COMPLETE, PULSE_DURATION_MS);

            // 等待 Request 信号复位，避免被重复触发
            try
            {
                var resetOk = await ctx.Signal.WaitForBitAsync(
                    SIG_PHOTO_REQUEST,
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

            // 无论是否复位成功，都继续（防重复由 _processedKeys 保证）
            await Task.Delay(COMPLETE_DELAY_MS, ct);
        }

        /// <summary>
        /// 入队激光命令（携带追踪信息）
        /// </summary>
        private void EnqueueLaserCommand(IHandlerContext ctx, string command,
            string seedId, int slotNo, long turntableIndex)
        {
            var pipeline = ctx.DataFlow.GetOrCreate<LaserCommand>(PIPELINE_LASER_QUEUE);
            pipeline.Push(new LaserCommand
            {
                CommandType = LaserCommandType.RawCommand,
                RawCommand = command,
                Timestamp = DateTime.Now,
                SeedId = seedId,
                SourceSlotNo = slotNo,
                SourceTurntableIndex = turntableIndex
            });
            LogDebug("激光命令已入队, SeedId={0}, SlotNo={1}, Index={2}",
                seedId ?? "null", slotNo, turntableIndex);
        }

        /// <summary>
        /// 入队空命令（携带追踪信息）
        /// </summary>
        private void EnqueueEmptyCommand(IHandlerContext ctx,
            string seedId, int slotNo, long turntableIndex)
        {
            var pipeline = ctx.DataFlow.GetOrCreate<LaserCommand>(PIPELINE_LASER_QUEUE);
            pipeline.Push(new LaserCommand
            {
                CommandType = LaserCommandType.Empty,
                RawCommand = "Empty",
                Timestamp = DateTime.Now,
                SeedId = seedId,
                SourceSlotNo = slotNo,
                SourceTurntableIndex = turntableIndex
            });
            LogDebug("空命令已入队, SeedId={0}, SlotNo={1}, Index={2}",
                seedId ?? "null", slotNo, turntableIndex);
        }

        #endregion

        #region 全局变量操作（参考 VisionCoordinateHandler）

        /// <summary>
        /// 读取字符串类型全局变量
        /// </summary>
        private string ReadGlobalVarString(string varName, string defaultValue)
        {
            try
            {
                dynamic globalVar = VmSolution.Instance[GLOBAL_VAR_MODULE_NAME];
                if (globalVar != null)
                {
                    string value = globalVar.GetGlobalVar(varName);
                    if (value != null)
                        return value;
                }
            }
            catch (Exception ex)
            {
                LogDebug("读取全局变量 {0} 失败: {1}", varName, ex.Message);
            }
            return defaultValue;
        }

        /// <summary>
        /// 读取整型全局变量
        /// </summary>
        private int ReadGlobalVarInt(string varName, int defaultValue)
        {
            try
            {
                dynamic globalVar = VmSolution.Instance[GLOBAL_VAR_MODULE_NAME];
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

        /// <summary>
        /// 清空激光相关全局变量（执行视觉前调用，防止读到上次残留值）
        /// </summary>
        private void ClearLaserGlobalVars()
        {
            try
            {
                dynamic globalVar = VmSolution.Instance[GLOBAL_VAR_MODULE_NAME];
                if (globalVar != null)
                {
                    globalVar.SetGlobalVar(GVAR_LASER_ROUTE_STR, "");
                    globalVar.SetGlobalVar(GVAR_LASER_PATH_VALID, "0");
                    LogDebug("已清空激光全局变量");
                }
            }
            catch (Exception ex)
            {
                LogDebug("清空激光全局变量失败: {0}", ex.Message);
            }
        }

        #endregion
    }

    #region 数据结构

    /// <summary>
    /// 激光命令类型
    /// </summary>
    public enum LaserCommandType
    {
        /// <summary>
        /// 空数据（无切割）
        /// </summary>
        Empty,

        /// <summary>
        /// 原始命令（由视觉脚本生成的坐标字符串）
        /// </summary>
        RawCommand,

        /// <summary>
        /// 标记命令
        /// </summary>
        Mark
    }

    /// <summary>
    /// 激光切割命令
    /// </summary>
    public class LaserCommand
    {
        /// <summary>
        /// 命令类型
        /// </summary>
        public LaserCommandType CommandType { get; set; }

        /// <summary>
        /// 原始命令字符串（视觉脚本输出的坐标数据）
        /// </summary>
        public string RawCommand { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        #region 追溯字段

        /// <summary>
        /// 种子唯一ID - 用于关联拍照和切割数据
        /// </summary>
        public string SeedId { get; set; }

        /// <summary>
        /// 拍照时的工位号（应该是3）
        /// </summary>
        public int SourceSlotNo { get; set; }

        /// <summary>
        /// 拍照时的转盘索引 - 用于计算预期的切割工位
        /// </summary>
        public long SourceTurntableIndex { get; set; }

        #endregion

        #region 校验方法

        /// <summary>
        /// 校验命令是否与当前工位匹配
        /// </summary>
        public bool ValidateForCurrentIndex(long currentTurntableIndex, int slotGap = 2)
        {
            long expectedIndex = SourceTurntableIndex + slotGap;
            return currentTurntableIndex == expectedIndex;
        }

        #endregion

        public override string ToString()
        {
            if (CommandType == LaserCommandType.Empty)
                return string.Format("LaserCmd[Empty] SeedId={0}", SeedId ?? "null");

            var preview = RawCommand != null && RawCommand.Length > 30
                ? RawCommand.Substring(0, 30) + "..."
                : RawCommand;
            return string.Format("LaserCmd[{0}] SeedId={1} Slot={2} Cmd={3}",
                CommandType, SeedId ?? "null", SourceSlotNo, preview);
        }
    }

    #endregion
}