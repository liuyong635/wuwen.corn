using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Framework.Services.Traceability;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// 条码扫描处理器 (修正版 - 匹配C++行为)
    /// 
    /// 功能：
    /// 1. 响应小托盘或大托盘的扫码请求信号
    /// 2. 执行条码扫描（无限重试，由PLC控制停止，匹配C++行为）
    /// 3. 验证条码有效性
    /// 4. 将扫码结果存储到上下文供后续使用
    /// 
    /// 触发条件：
    /// - 收到扫码请求 (SmallTray_ScanRequest 或 LargeTray_ScanRequest)
    /// - 自身非忙碌状态
    /// 
    /// ★★★ C++行为对照 ★★★
    /// 1. onReadTimerTimeout() 每100ms读取PLC信号M3428.5/M3458.0
    /// 2. 信号为true时设置 bSmallScanning/bLargeScanning = true
    /// 3. updateTime() 检测到扫描标志且结果是NoRead时，调用startSmallScan()
    /// 4. 持续重试直到：获得有效条码 或 PLC撤销请求信号
    /// 5. 没有最大重试次数限制，完全由PLC控制
    /// 
    ///  条码扫描处理器（集成追溯功能）
    /// 
    /// ★★★ 追溯集成说明 ★★★
    /// 1. 小料盘扫码完成后，保存条码和扫码时间到 Context
    /// 2. 大料盘扫码完成后，检查小料盘是否就绪
    /// 3. 两个都就绪时，调用 ITraceService.CreatePairingAsync() 创建配对记录
    /// 4. 配对完成后清除就绪标志，准备下一组
    /// </summary>
    public class BarcodeScanHandler : SignalHandlerBase
    {
        #region 常量定义

        // PLC信号名称（需与SignalAliasConfig.cs中的别名匹配）
        private const string SIG_SMALL_TRAY_SCAN_REQUEST = "SmallTray_ScanRequest";  // M3428.5 -> "小料盘请求扫码"
        private const string SIG_LARGE_TRAY_SCAN_REQUEST = "LargeTray_ScanRequest";  // M3458.0 -> "大料盘请求扫码"
        private const string FLAG_SMALL_SCAN_TIME = "SmallTray_ScanTime";

        // 扫码配置
        // C++: waitForBytesWritten(3000) - 单次扫码超时3秒
        private static readonly TimeSpan SCAN_TIMEOUT = TimeSpan.FromSeconds(3);
        private const int SIGNAL_DEBOUNCE_MS = 1000;  // PLC信号防抖等待时间

        // C++: readTimer->setInterval(100) - 但重试间隔约为Timer周期(1秒)
        // 这里使用500ms，比C++稍快但更合理
        private static readonly TimeSpan RETRY_INTERVAL = TimeSpan.FromMilliseconds(500);



        // 条码最小长度
        private const int MIN_BARCODE_LENGTH = 6;

        #endregion

        #region Handler属性

        public override string HandlerId => "BarcodeScan";

        public override string HandlerName => "条码扫描";

        public override int Priority => 72;

        public override string[] DependentDevices => new[] { "Scanner" };

        /// <summary>
        /// 触发条件：(小托盘扫码请求 或 大托盘扫码请求) + 非忙碌
        /// </summary>
        public override ITriggerCondition TriggerCondition => When.All(
            When.IsRunning(),

            When.Any(
                When.SignalOn(SIG_SMALL_TRAY_SCAN_REQUEST),
                When.SignalOn(SIG_LARGE_TRAY_SCAN_REQUEST)
            ),
            When.FlagOff("BarcodeScan_Busy")
        );

        #endregion

        #region 执行逻辑

        private string smallTrayCode = "";
        private string largeTrayCode = "";

        private DateTime smallTrayCodeScanTime = DateTime.Now;
        private DateTime largeTrayCodeScanTime = DateTime.Now;
        protected override async Task<ValueTuple<bool, string>> ExecuteAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            // 设置忙碌标志
            SetBusy(true);
            FlagCondition.SetFlag("BarcodeScan_Busy", true);

            try
            {
                var scanner = ctx.GetScanner();

                if (scanner == null)
                {
                    return Fail("扫码器设备未找到");
                }

                if (!scanner.IsConnected)
                {
                    return Fail("扫码器设备未连接");
                }

                // 判断是哪种托盘的扫码请求
                bool isSmallTray = ReadSignal(ctx, SIG_SMALL_TRAY_SCAN_REQUEST);
                bool isLargeTray = !isSmallTray && ReadSignal(ctx, SIG_LARGE_TRAY_SCAN_REQUEST);

                // 确定要监控的PLC信号
                string signalToWatch = isSmallTray ? SIG_SMALL_TRAY_SCAN_REQUEST : SIG_LARGE_TRAY_SCAN_REQUEST;
                string trayType = isSmallTray ? "小料盘" : "大料盘";

                LogInfo("开始执行{0}条码扫描", trayType);

                // 清除之前的扫码结果
                FlagCondition.SetFlag("BarcodeScan_Success", false);
                FlagCondition.SetFlag("BarcodeScan_Failed", false);
                ctx.SetFlag<string>("LastBarcode", null);
                ctx.SetFlag<string>("LastBarcodeType", null);


                // 扫码循环
                ScanResult scanResult = null;
                string barcode = null;
                int attemptCount = 0;

                while (!ct.IsCancellationRequested)
                {
                    attemptCount++;

                    // ★★★ 关键：每次循环检查PLC请求信号是否仍然有效 ★★★
                    // 匹配C++: onReadTimerTimeout()中每100ms读取PLC信号
                    // 当PLC信号变为false时，bSmallScanning会被设为false，停止重试
                    if (!ReadSignal(ctx, signalToWatch))
                    {
                        LogInfo("PLC已撤销{0}扫码请求信号，停止扫描", trayType);
                        // 不算失败，只是被PLC中断
                        return Fail("扫码请求已取消（PLC撤销）");
                    }

                    LogDebug("第 {0} 次扫码尝试...", attemptCount);

                    try
                    {
                        scanResult = await scanner.ScanAsync(SCAN_TIMEOUT, ct);
                    }
                    catch (Exception ex)
                    {
                        LogWarning("扫码异常: {0}，准备重试...", ex.Message);
                        await Task.Delay(RETRY_INTERVAL, ct);
                        continue;
                    }

                    if (!scanResult.Success)
                    {
                        LogWarning("扫码失败: {0}，准备重试...", scanResult.ErrorMessage ?? "未知错误");
                        await Task.Delay(RETRY_INTERVAL, ct);
                        continue;
                    }

                    barcode = scanResult.Code;

                    // 检查是否是 NoRead
                    // C++: if(receivedString.compare("NoRead", Qt::CaseInsensitive) == 0)
                    //      无任何处理，等待下次updateTime()自动重试
                    if (IsNoRead(barcode))
                    {
                        LogDebug("收到 NoRead 响应，准备重试...");
                        await Task.Delay(RETRY_INTERVAL, ct);
                        continue;  // ★ 无限重试，不限次数
                    }

                    // 验证条码长度
                    if (string.IsNullOrWhiteSpace(barcode) || barcode.Length < MIN_BARCODE_LENGTH)
                    {
                        LogWarning("条码无效或长度不足: {0}，准备重试...", barcode ?? "空");
                        await Task.Delay(RETRY_INTERVAL, ct);
                        continue;
                    }

                    // 验证条码格式
                    if (!ValidateBarcode(barcode, isSmallTray ? TrayType.Small : TrayType.Large))
                    {
                        LogWarning("条码格式无效: {0}，准备重试...", barcode);
                        await Task.Delay(RETRY_INTERVAL, ct);
                        continue;
                    }

                    // ★ 获得有效条码，跳出循环
                    LogDebug("获得有效扫码结果: {0}", barcode);
                    break;
                }

                // 检查是否因取消而退出
                ct.ThrowIfCancellationRequested();

                LogInfo("扫码成功: {0} (类型: {1})，共尝试 {2} 次",
                    barcode, scanResult?.CodeType ?? "未知", attemptCount);

                // 保存扫码结果
                // C++: smallBarCode = receivedString; ui->le_small_barcode->setText(smallBarCode);
                ctx.SetFlag("LastBarcode", barcode);
                ctx.SetFlag("LastBarcodeType", scanResult?.CodeType ?? "Unknown");
                ctx.SetFlag("LastBarcodeTime", DateTime.Now);

                // 根据托盘类型保存
                if (isSmallTray)
                {
                    ctx.SetFlag("SmallTray_Barcode", barcode);
                    ctx.SetFlag(FLAG_SMALL_SCAN_TIME, DateTime.Now);  // ★ 新增：记录扫码时间

                    FlagCondition.SetFlag("SmallTray_BarcodeReady", true);
                    LogInfo("小料盘扫码完成: {0}", barcode);
                    smallTrayCode = barcode;
                    smallTrayCodeScanTime=DateTime.Now;
                }
                else if (isLargeTray)
                {
                    ctx.SetFlag("LargeTray_Barcode", barcode);
                    FlagCondition.SetFlag("LargeTray_BarcodeReady", true);

                    ctx.SetFlag("LargeTray_ScanTime", DateTime.Now);
                    FlagCondition.SetFlag("LargeTray_BarcodeReady", true);
                    largeTrayCode = barcode;
                    largeTrayCodeScanTime=DateTime.Now;
                    LogInfo("大料盘扫码完成: {0}", barcode);
                }

                if (FlagCondition.GetFlag("SmallTray_BarcodeReady") &&
                    FlagCondition.GetFlag("LargeTray_BarcodeReady"))
                {
                    await CreateTracePairingAsync(ctx, ct);
                }

                // 设置成功标志
                FlagCondition.SetFlag("BarcodeScan_Success", true);

                // 推送到数据管道（供后续追溯使用）
                var pipeline = ctx.DataFlow.GetOrCreate<BarcodeData>("BarcodeHistory");
                pipeline.Push(new BarcodeData
                {
                    Barcode = barcode,
                    CodeType = scanResult?.CodeType,
                    TrayType = isSmallTray ? TrayType.Small : TrayType.Large,
                    Timestamp = DateTime.Now
                });

                return Success(string.Format("扫码成功: {0}", barcode));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogError(ex, "扫码处理异常");
                FlagCondition.SetFlag("BarcodeScan_Failed", true);
                return Fail("处理异常: " + ex.Message);
            }
            finally
            {

                // ★★★ 新增：等待1秒防止PLC信号重复触发 ★★★
                try
                {
                    await Task.Delay(SIGNAL_DEBOUNCE_MS, CancellationToken.None);
                }
                catch { }
                // 清除忙碌标志
                SetBusy(false);
                FlagCondition.SetFlag("BarcodeScan_Busy", false);
            }
        }

        #endregion




        #region 辅助方法


        /// <summary>
        /// 创建料盘配对追溯记录
        /// 
        /// 调用时机：小料盘和大料盘都扫码完成后
        /// 支持任意扫码顺序（小→大 或 大→小）
        /// </summary>
        private async Task CreateTracePairingAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            var traceService = ctx.GetService<ITraceService>();
            if (traceService == null)
            {
                LogWarning("追溯服务不可用，无法创建配对记录");
                return;
            }

            try
            {
                // 获取两个料盘的信息
                var smallBarcode = smallTrayCode;
                var largeBarcode = largeTrayCode;
                var smallScanTime = smallTrayCodeScanTime;
                var largeScanTime = largeTrayCodeScanTime;

                if (string.IsNullOrWhiteSpace(smallBarcode) || string.IsNullOrWhiteSpace(largeBarcode))
                {
                    LogWarning("条码信息不完整，无法创建配对记录");
                    return;
                }

                // 创建配对记录
                var traceId = await traceService.CreatePairingAsync(
                    smallBarcode,
                    largeBarcode,
                    smallScanTime,
                    largeScanTime,
                    ct);

                LogInfo("配对记录已创建: {0} ({1} <-> {2})",
                    traceId, smallBarcode, largeBarcode);

                // 清除两个就绪标志，准备下一组
                FlagCondition.SetFlag("SmallTray_BarcodeReady", false);
                FlagCondition.SetFlag("LargeTray_BarcodeReady", false);
                ctx.RemoveFlag("SmallTray_Barcode");
                ctx.RemoveFlag("LargeTray_Barcode");
                ctx.RemoveFlag("SmallTray_ScanTime");
                ctx.RemoveFlag("LargeTray_ScanTime");

                LogDebug("已清除配对状态，准备下一组扫码");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogError(ex, "创建配对记录失败");
            }
        }


        /// <summary>
        /// 检查是否是 NoRead 响应
        /// C++: receivedString.compare("NoRead", Qt::CaseInsensitive) == 0
        /// </summary>
        private bool IsNoRead(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return true;

            return string.Equals(code.Trim(), "NoRead", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 验证条码格式
        /// </summary>
        private bool ValidateBarcode(string barcode, TrayType trayType)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return false;

            // 基本长度检查
            if (barcode.Length < MIN_BARCODE_LENGTH)
                return false;

            // 检查是否包含非法字符
            foreach (char c in barcode)
            {
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                {
                    return false;
                }
            }

            // 可根据实际条码规则添加更多验证
            // 例如：前缀检查、校验位验证等

            return true;
        }

        #endregion

        #region 静态方法

        /// <summary>
        /// 获取最后一次扫描的条码
        /// </summary>
        public static string GetLastBarcode(IHandlerContext ctx)
        {
            return ctx?.GetFlag<string>("LastBarcode", null);
        }

        /// <summary>
        /// 检查扫码是否成功
        /// </summary>
        public static bool IsScanSuccess()
        {
            return FlagCondition.GetFlag("BarcodeScan_Success");
        }

        /// <summary>
        /// 清除扫码状态
        /// </summary>
        public static void ClearScanStatus()
        {
            FlagCondition.SetFlag("BarcodeScan_Success", false);
            FlagCondition.SetFlag("BarcodeScan_Failed", false);
            FlagCondition.SetFlag("SmallTray_BarcodeReady", false);
            FlagCondition.SetFlag("LargeTray_BarcodeReady", false);
        }

        #endregion
    }

    #region 数据结构

    /// <summary>
    /// 条码数据
    /// </summary>
    public class BarcodeData
    {
        /// <summary>
        /// 条码内容
        /// </summary>
        public string Barcode { get; set; }

        /// <summary>
        /// 条码类型 (QR, DataMatrix, Code128 等)
        /// </summary>
        public string CodeType { get; set; }

        /// <summary>
        /// 托盘类型
        /// </summary>
        public TrayType TrayType { get; set; }

        /// <summary>
        /// 扫描时间
        /// </summary>
        public DateTime Timestamp { get; set; }

        public override string ToString()
        {
            return string.Format("Barcode[{0}]: {1} ({2}) @ {3:HH:mm:ss}",
                TrayType, Barcode, CodeType, Timestamp);
        }
    }

    #endregion
}