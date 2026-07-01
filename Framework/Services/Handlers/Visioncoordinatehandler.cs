using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VM.Core;
using VM.PlatformSDKCS;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// 视觉坐标处理器
    /// 
    /// ═══════════════════════════════════════════════════════════
    /// 重构说明（方案一：C#集中控制 + AR纯执行）：
    ///   
    ///   删除了 HeadCam TCP 通道。VM 脚本3 的协议字符串不再通过 TCP 发送，
    ///   而是写入全局变量 "ProtocolStr"，本 Handler 通过 SDK 读取。
    ///   
    ///   ProtocolStr 格式与原 HeadCam TCP 完全一致：
    ///     有坐标: "235.716;240.556;6.27\n237.100;241.200;-3.50"
    ///     无坐标: "NO_POINTS"
    ///   
    ///   C# 端负责：
    ///   1. 解析协议字符串
    ///   2. 角度转换（原 Lua processCurrentPoint 中的 180°/360° 逻辑）
    ///   3. 组装 POINTS 命令下发 AR
    /// ═══════════════════════════════════════════════════════════
    /// 
    /// 触发条件：
    /// - 系统运行中
    /// - (AR就绪 或 批次完成 或 振动完成)
    /// - 自身非忙碌
    /// 
    /// 依赖设备：Vision, Robot
    /// </summary>
    public class VisionCoordinateHandler : SignalHandlerBase
    {
        #region 常量

        private const string VISION_PROCEDURE = "流程1";
        private const string GLOBAL_VAR_MODULE_NAME = "全局变量1";

        #endregion

        #region Handler 属性

        public override string HandlerId => "VisionCoordinate";
        public override string HandlerName => "视觉坐标处理";
        public override int Priority => 80;
        public override string[] DependentDevices => new[] { "Vision", "Robot" };

        public override ITriggerCondition TriggerCondition => When.All(
            When.IsRunning(),
            When.Any(
                When.FlagOn("Robot_Ready_Received"),
                When.FlagOn("Robot_BatchDone_Received"),
                When.FlagOn("Vibrate_Complete")
            ),
            When.FlagOff("VisionCoordinate_Busy")
        );

        #endregion

        #region 主执行逻辑

        protected override async Task<ValueTuple<bool, string>> ExecuteAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            SetBusy(true);
            FlagCondition.SetFlag("VisionCoordinate_Busy", true);

            try
            {
                // ============ 1. 清除触发标志 ============
                FlagCondition.SetFlag("Robot_Ready_Received", false);
                FlagCondition.SetFlag("Robot_BatchDone_Received", false);
                FlagCondition.SetFlag("Vibrate_Complete", false);

                // ============ 2. 获取设备 ============
                var vision = ctx.GetVision();
                var robot = ctx.GetRobot();

                if (vision == null || !vision.IsConnected)
                    return Fail("视觉设备未连接");

                if (robot == null || !robot.IsConnected)
                    return Fail("机器人设备未连接");

                // ============ 3. 读配方参数 ============
                var recipe = ctx.GetService<IRecipeService>();

                double safeMinX = recipe?.GetDouble("VisionLimit", "SafeMinX", 101.0,
                    name: "X坐标下限", unit: "mm", description: "视觉坐标X轴安全范围下限") ?? 101.0;
                double safeMaxX = recipe?.GetDouble("VisionLimit", "SafeMaxX", 366.3,
                    name: "X坐标上限", unit: "mm", description: "视觉坐标X轴安全范围上限") ?? 366.3;
                double safeMinY = recipe?.GetDouble("VisionLimit", "SafeMinY", 0.7,
                    name: "Y坐标下限", unit: "mm", description: "视觉坐标Y轴安全范围下限") ?? 0.7;
                double safeMaxY = recipe?.GetDouble("VisionLimit", "SafeMaxY", 241.0,
                    name: "Y坐标上限", unit: "mm", description: "视觉坐标Y轴安全范围上限") ?? 241.0;


                // ---- 脚本1：粘连筛选参数 ----
                double areaMin = recipe?.GetDouble("BlobFilter", "AreaMin", 40000.0,
                    name: "面积下限", unit: "px²", description: "Blob面积筛选下限") ?? 40000.0;
                double areaMax = recipe?.GetDouble("BlobFilter", "AreaMax", 95000.0,
                    name: "面积上限", unit: "px²", description: "Blob面积筛选上限") ?? 95000.0;
                double compactnessMax = recipe?.GetDouble("BlobFilter", "CompactnessMax", 1.9,
                    name: "紧凑度上限", description: "圆形度阈值，越大越容许不规则形状") ?? 1.9;
                double aspectRatioMax = recipe?.GetDouble("BlobFilter", "AspectRatioMax", 2.5,
                    name: "长短比上限", description: "矩形长短边比阈值") ?? 2.5;

                // ---- 脚本2：夹爪碰撞检测参数 ----
                double gripperLength = recipe?.GetDouble("Gripper", "GripperLength", 400.0,
                    name: "夹爪长度", unit: "px", description: "夹爪模拟矩形长度") ?? 400.0;
                double gripperWidth = recipe?.GetDouble("Gripper", "GripperWidth", 400.0,
                    name: "夹爪宽度", unit: "px", description: "夹爪模拟矩形宽度") ?? 400.0;
                int collisionSampleStep = recipe?.GetInt("Gripper", "CollisionSampleStep", 3,
                    name: "采样步长", unit: "px", description: "碰撞检测像素采样间隔，越小越精确但越慢") ?? 3;
                int whiteThreshold = recipe?.GetInt("Gripper", "WhiteThreshold", 200,
                    name: "白色阈值", description: "二值图白色像素判定阈值(0-255)") ?? 200;
                int boxExpandPixel = recipe?.GetInt("Gripper", "BoxExpandPixel", 30,
                    name: "排除框外扩", unit: "px", description: "自身Blob排除框向外扩展像素数") ?? 30;
                // ---- 脚本2：小头框面积占比过滤 ----
                double tipAreaRatioMax = recipe?.GetDouble("Gripper", "TipAreaRatioMax", 0.667,
                    name: "小头框占比上限", description: "DL小头框面积/玉米Blob面积的比值上限，超过则认为DL框了整颗玉米而非小头") ?? 0.667;

                double angleOffset = recipe?.GetDouble("Vision", "AngleOffset", 0.0,
                    name: "角度补偿", unit: "°", description: "视觉角度全局补偿值") ?? 0.0;
                double angleThreshold = recipe?.GetDouble("Vision", "AngleThreshold", 300.0,
                    name: "角度归范围阈值", unit: "°", description: "360°归范围判断阈值") ?? 300.0;

                double catchZ = recipe?.GetDouble("Robot", "CatchZ", -136.5,
                    name: "抓取高度", unit: "mm", description: "抓取下降高度") ?? -136.5;

                // ============ 4. 设置 VM 全局变量 ============
                await SetVmGlobalParametersAsync(
                    safeMinX, safeMaxX, safeMinY, safeMaxY, angleOffset,
                    areaMin, areaMax, compactnessMax, aspectRatioMax,
                    gripperLength, gripperWidth,
                    collisionSampleStep, whiteThreshold, boxExpandPixel,
                    tipAreaRatioMax,     // ← 新增
                    ct, ctx);
                // ============ 5. 执行 VM 视觉检测 ============
                LogInfo("开始执行视觉检测: {0}", VISION_PROCEDURE);

                var visionResult = await vision.ExecuteAsync(VISION_PROCEDURE, ct);

                if (!visionResult.Success)
                {
                    LogWarning("视觉检测失败: {0}，按无料处理继续重试", visionResult.Message);
                    // ★ 修复：检测失败也走 HandleNoSeed，设置 NeedVibrate 保持链路
                    // 尝试读取 WhiteAreaRatio（可能有上次的值），读不到就用 0
                    float fallbackRatio = ReadGlobalVarFloat("WhiteAreaRatio", 0f);
                    await HandleNoSeed(ctx, robot, fallbackRatio, ct);
                    return Fail("视觉检测失败: " + visionResult.Message);
                }

                // ============ 6. 从 VM 全局变量读取结果 ============
                int blobNum = ReadGlobalVarInt("BlobNum", 0);
                float whiteAreaRatio = ReadGlobalVarFloat("WhiteAreaRatio", 0f);
                int coordinateOutOfBound = ReadGlobalVarInt("CoordinateOutOfBound", 0);
                string protocolStr = ReadGlobalVarString("ProtocolStr", "NO_POINTS");

                LogInfo("检测结果: 种子数={0}, 白色占比={1:F2}%, 坐标超限={2}",
                    blobNum, whiteAreaRatio, coordinateOutOfBound);

                if (coordinateOutOfBound != 0)
                {
                    LogWarning("检测到部分坐标超限，已被脚本3剔除");
                }

                // ============ 7. 判断 ProtocolStr ============

                if (string.IsNullOrEmpty(protocolStr) || protocolStr == "NO_POINTS" || blobNum <= 0)
                {
                    // 无料
                    LogWarning("检测无料 (BlobNum={0}, ProtocolStr={1})",
                        blobNum, protocolStr == "NO_POINTS" ? "NO_POINTS" : "empty");
                    await HandleNoSeed(ctx, robot, whiteAreaRatio, ct);
                    return Success("无料，已请求振动");
                }

                // ============ 8. 解析协议字符串 + 角度转换 + 组装POINTS ============

                // 解析原始协议字符串
                // 格式: "x;y;angle\nx;y;angle\n..."（每行一个点，分号分隔）
                var validPoints = new List<PointData>();

                string[] lines = protocolStr.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    string[] parts = line.Split(';');
                    if (parts.Length < 3)
                    {
                        LogDebug("坐标行格式错误，跳过: \"{0}\"", line);
                        continue;
                    }

                    float x, y, angle;
                    if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                        !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                        !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out angle))
                    {
                        LogDebug("坐标数值解析失败，跳过行{0}: \"{1}\"", i, line);
                        continue;
                    }

                    // 角度转换（原 Lua processCurrentPoint 中的逻辑）
                    // VM脚本3已经加了angleOffset，这里不再重复加
                    // c = angle - 180, 然后归到 ±threshold 范围
                    double c = angle - 180.0;
                    if (c > angleThreshold) c -= 360.0;
                    if (c < -angleThreshold) c += 360.0;

                    validPoints.Add(new PointData
                    {
                        X = x,
                        Y = y,
                        Z = (float)catchZ,
                        C = (float)c
                    });
                }

                if (validPoints.Count <= 0)
                {
                    LogWarning("协议字符串解析后无有效坐标");
                    await HandleNoSeed(ctx, robot, whiteAreaRatio, ct);
                    return Success("坐标解析为空，已请求振动");
                }

                // 组装 POINTS 命令
                var sb = new StringBuilder();
                sb.AppendFormat("POINTS:{0}", validPoints.Count);

                for (int i = 0; i < validPoints.Count; i++)
                {
                    var pt = validPoints[i];
                    sb.AppendFormat(CultureInfo.InvariantCulture,
                        ";{0:F3};{1:F3};{2:F3};{3:F3}",
                        pt.X, pt.Y, pt.Z, pt.C);
                }

                string pointsCmd = sb.ToString();
                LogInfo("下发 {0} 个坐标点给AR", validPoints.Count);
                LogDebug("POINTS命令: {0}",
                    pointsCmd.Length > 200 ? pointsCmd.Substring(0, 200) + "..." : pointsCmd);

                await robot.SendCommandAsync(pointsCmd, ct);

                // 通知 DiskVibrateHandler 重置计数器
                var resetData = ctx.DataFlow.GetOrCreate<string>("VibrateResetSignal");
                resetData.Push("RESET");

                return Success(string.Format("下发 {0} 个坐标", validPoints.Count));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogError(ex, "视觉坐标处理异常");
                // ★ 修复：异常时也设置 NeedVibrate，防止链路断裂
                // 确保 DiskVibrate 可以继续触发，系统不会卡死
                FlagCondition.SetFlag("NeedVibrate", true);
                return Fail("处理异常: " + ex.Message);
            }
            finally
            {
                SetBusy(false);
                FlagCondition.SetFlag("VisionCoordinate_Busy", false);
            }
        }

        #endregion

        #region 坐标数据结构

        /// <summary>单个坐标点（C#端处理后的最终值）</summary>
        private struct PointData
        {
            public float X;
            public float Y;
            public float Z;
            public float C;
        }

        #endregion

        #region 无料处理

        private async Task HandleNoSeed(
            IHandlerContext ctx,
            IRobotDevice robot,
            float whiteAreaRatio,
            CancellationToken ct)
        {
            await SendCommandSafe(robot, "NO_SEED", ct);

            var ratioData = ctx.DataFlow.GetOrCreate<float>("WhiteAreaRatio");
            ratioData.Push(whiteAreaRatio);

            FlagCondition.SetFlag("NeedVibrate", true);
        }

        #endregion

        #region VM 全局变量操作

        private async Task SetVmGlobalParametersAsync(
            double safeMinX, double safeMaxX,
            double safeMinY, double safeMaxY,
            double angleOffset,
            double areaMin, double areaMax,
            double compactnessMax, double aspectRatioMax,
            double gripperLength, double gripperWidth,
            int collisionSampleStep, int whiteThreshold, int boxExpandPixel,
            double tipAreaRatioMax,   // ← 新增
            CancellationToken ct, IHandlerContext ctx)
        {
            try
            {
                dynamic globalVar = VmSolution.Instance[GLOBAL_VAR_MODULE_NAME];
                if (globalVar != null)
                {
                    // ============ 现有代码（不修改）============
                    globalVar.SetGlobalVar("SafeMinX", safeMinX.ToString("F1"));
                    globalVar.SetGlobalVar("SafeMaxX", safeMaxX.ToString("F1"));
                    globalVar.SetGlobalVar("SafeMinY", safeMinY.ToString("F1"));
                    globalVar.SetGlobalVar("SafeMaxY", safeMaxY.ToString("F1"));
                    globalVar.SetGlobalVar("AngleOffset", angleOffset.ToString("F2"));
                    globalVar.SetGlobalVar("CoordinateOutOfBound", "0");

                    // ---- 脚本1：粘连筛选 ----
                    globalVar.SetGlobalVar("AreaMin", areaMin.ToString("F0"));
                    globalVar.SetGlobalVar("AreaMax", areaMax.ToString("F0"));
                    globalVar.SetGlobalVar("CompactnessMax", compactnessMax.ToString("F2"));
                    globalVar.SetGlobalVar("AspectRatioMax", aspectRatioMax.ToString("F1"));
                    // ---- 脚本2：小头框面积占比过滤 ----
                    globalVar.SetGlobalVar("TipAreaRatioMax", tipAreaRatioMax.ToString("F3"));
                    // ---- 脚本2：夹爪碰撞 ----
                    globalVar.SetGlobalVar("GripperLength", gripperLength.ToString("F0"));
                    globalVar.SetGlobalVar("GripperWidth", gripperWidth.ToString("F0"));
                    globalVar.SetGlobalVar("CollisionSampleStep", collisionSampleStep.ToString());
                    globalVar.SetGlobalVar("WhiteThreshold", whiteThreshold.ToString());
                    globalVar.SetGlobalVar("BoxExpandPixel", boxExpandPixel.ToString());

                    globalVar.SetGlobalVar("ProtocolStr", "");

                    // ========== 🆕 新增：数据采集参数写入 ==========
                    try
                    {
                        var recipe = ctx.GetService<IRecipeService>();
                        bool collectEnabled = recipe?.GetBool("DataCollection", "Enabled", true,
                            name: "数据采集开关", description: "开启后VM流程会自动保存图像到本地") ?? true;
                        string basePath = recipe?.GetString("DataCollection", "SavePath", @"D:\CollectedData",
                            name: "采集存储路径", description: "图像保存根路径") ?? @"D:\CollectedData";

                        // 写入开关
                        globalVar.SetGlobalVar("DataCollectionEnabled", collectEnabled ? "1" : "0");

                        // 构建当天路径: {basePath}\{配方名}_{yyyyMMdd}
                        if (collectEnabled)
                        {
                            string recipeName = recipe?.CurrentRecipeName ?? "Default";
                            
                            // ★ 增强修复1：清除配方名中的非法路径字符，避免路径错误
                            char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
                            string safeRecipeName = string.Join("_", recipeName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
                            if (string.IsNullOrWhiteSpace(safeRecipeName)) safeRecipeName = "Default";
                            
                            // ★ 增强修复2：验证 basePath 是否有效
                            if (string.IsNullOrWhiteSpace(basePath))
                            {
                                basePath = @"D:\CollectedData";
                                LogDebug("数据采集路径为空，使用默认路径: {0}", basePath);
                            }
                            
                            string dateFolder = string.Format("{0}_{1}", safeRecipeName, DateTime.Now.ToString("yyyyMMdd"));
                            string fullBasePath = System.IO.Path.Combine(basePath, dateFolder);
                            
                            // ★ 关键修复：确保目录存在，避免 VisionMaster imgcodecs_imwrite 报错
                            if (!System.IO.Directory.Exists(fullBasePath))
                            {
                                try
                                {
                                    System.IO.Directory.CreateDirectory(fullBasePath);
                                    LogDebug("已创建数据采集目录: {0}", fullBasePath);
                                }
                                catch (Exception dirEx)
                                {
                                    LogDebug("创建数据采集目录失败: {0}，尝试使用临时路径", dirEx.Message);
                                    // ★ 增强修复3：目录创建失败时，尝试使用程序目录下的备用路径
                                    try
                                    {
                                        string fallbackPath = System.IO.Path.Combine(
                                            System.IO.Directory.GetCurrentDirectory(),
                                            "CollectedData",
                                            dateFolder);
                                        System.IO.Directory.CreateDirectory(fallbackPath);
                                        fullBasePath = fallbackPath;
                                        LogDebug("已使用备用数据采集路径: {0}", fullBasePath);
                                    }
                                    catch (Exception fallbackEx)
                                    {
                                        LogDebug("备用路径创建也失败: {0}，禁用数据采集", fallbackEx.Message);
                                        collectEnabled = false;
                                        globalVar.SetGlobalVar("DataCollectionEnabled", "0");
                                        fullBasePath = "";
                                    }
                                }
                            }
                            
                            globalVar.SetGlobalVar("DataCollectionBasePath", fullBasePath);
                            LogDebug("数据采集已启用, 路径: {0}", fullBasePath);
                        }
                        else
                        {
                            globalVar.SetGlobalVar("DataCollectionBasePath", "");
                        }
                    }
                    catch (Exception collectEx)
                    {
                        LogDebug("数据采集参数写入失败（不影响生产）: {0}", collectEx.Message);
                    }
                    // ========== 新增结束 ==========
                }

                LogDebug("已设置VM参数: X[{0:F1}~{1:F1}], Y[{2:F1}~{3:F1}], AngleOffset={4:F2}",
                    safeMinX, safeMaxX, safeMinY, safeMaxY, angleOffset);

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                LogWarning("设置VM全局变量失败: {0}", ex.Message);
            }
        }

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

        private float ReadGlobalVarFloat(string varName, float defaultValue)
        {
            try
            {
                dynamic globalVar = VmSolution.Instance[GLOBAL_VAR_MODULE_NAME];
                if (globalVar != null)
                {
                    string value = globalVar.GetGlobalVar(varName);
                    if (!string.IsNullOrEmpty(value) && float.TryParse(value,
                        NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                        return result;
                }
            }
            catch (Exception ex)
            {
                LogDebug("读取全局变量 {0} 失败: {1}", varName, ex.Message);
            }
            return defaultValue;
        }

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

        private async Task SendCommandSafe(IRobotDevice robot, string command, CancellationToken ct)
        {
            try
            {
                if (robot != null && robot.IsConnected)
                    await robot.SendCommandAsync(command, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                LogWarning("发送命令 {0} 失败: {1}", command, ex.Message);
            }
        }

        #endregion
    }
}