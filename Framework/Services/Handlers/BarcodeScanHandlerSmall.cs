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
    public class BarcodeScanHandlerSmall : BarcodeScanHandlerBase
    {
        #region 常量定义

        // PLC信号名称（需与SignalAliasConfig.cs中的别名匹配）
        public override string SIG_TRAY_SCAN_REQUEST {get;}= "SmallTray_ScanRequest";  // M3428.5 -> "小料盘请求扫码"
        public override TrayType TrayType => TrayType.Small;

        public override string BusyFlag => TrayType + "Busy";
        // 条码最小长度
        private const int MIN_BARCODE_LENGTH = 6;

        #endregion

        #region Handler属性


        public override string[] DependentDevices => new[] { "Scanner_Small" };

        /// <summary>
        /// 触发条件：(小托盘扫码请求 或 大托盘扫码请求) + 非忙碌
        /// </summary>
      

        #endregion


    }


}