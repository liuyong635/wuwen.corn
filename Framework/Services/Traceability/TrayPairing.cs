using System;

namespace SeedCut.Framework.Services.Traceability
{
    /// <summary>
    /// 料盘配对追溯记录
    /// </summary>
    public class TrayPairing
    {
        /// <summary>
        /// 系统追溯号（主键，永不重复）
        /// 格式：TR-YYYYMMDD-NNNNNN
        /// </summary>
        public string TraceId { get; set; }

        /// <summary>
        /// 小料盘条码
        /// </summary>
        public string SmallTrayBarcode { get; set; }

        /// <summary>
        /// 大料盘条码
        /// </summary>
        public string LargeTrayBarcode { get; set; }

        /// <summary>
        /// 小料盘扫码时间
        /// </summary>
        public DateTime SmallTrayScanTime { get; set; }

        /// <summary>
        /// 大料盘扫码时间
        /// </summary>
        public DateTime LargeTrayScanTime { get; set; }

        /// <summary>
        /// 配对时间（记录创建时间）
        /// </summary>
        public DateTime PairTime { get; set; }

        /// <summary>
        /// 小料盘条码是否重复
        /// </summary>
        public bool IsSmallDuplicate { get; set; }

        /// <summary>
        /// 大料盘条码是否重复
        /// </summary>
        public bool IsLargeDuplicate { get; set; }

        #region 扩展字段（预留）

        /// <summary>
        /// 批次号
        /// </summary>
        public string BatchNo { get; set; }

        /// <summary>
        /// 工单号
        /// </summary>
        public string WorkOrderNo { get; set; }

        /// <summary>
        /// 操作员ID
        /// </summary>
        public string OperatorId { get; set; }

        /// <summary>
        /// 工位号
        /// </summary>
        public string StationNo { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string Remarks { get; set; }

        #endregion

        /// <summary>
        /// 记录创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 是否有重复条码
        /// </summary>
        public bool HasDuplicate => IsSmallDuplicate || IsLargeDuplicate;

        public override string ToString()
        {
            return string.Format("[{0}] {1} <-> {2} @ {3:HH:mm:ss}",
                TraceId, SmallTrayBarcode, LargeTrayBarcode, PairTime);
        }
    }

    /// <summary>
    /// 追溯统计信息
    /// </summary>
    public class TraceStatistics
    {
        /// <summary>
        /// 总配对数
        /// </summary>
        public int TotalPairings { get; set; }

        /// <summary>
        /// 重复条码次数
        /// </summary>
        public int DuplicateCount { get; set; }

        /// <summary>
        /// 重复率
        /// </summary>
        public double DuplicateRate => TotalPairings > 0
            ? Math.Round((double)DuplicateCount / TotalPairings * 100, 2)
            : 0;

        /// <summary>
        /// 统计日期
        /// </summary>
        public DateTime StatDate { get; set; }
    }

    /// <summary>
    /// 重复条码信息
    /// </summary>
    public class DuplicateInfo
    {
        /// <summary>
        /// 条码
        /// </summary>
        public string Barcode { get; set; }

        /// <summary>
        /// 料盘类型（Small/Large）
        /// </summary>
        public string TrayType { get; set; }

        /// <summary>
        /// 出现次数
        /// </summary>
        public int OccurrenceCount { get; set; }

        /// <summary>
        /// 首次出现时间
        /// </summary>
        public DateTime FirstTime { get; set; }

        /// <summary>
        /// 最近出现时间
        /// </summary>
        public DateTime LastTime { get; set; }

        /// <summary>
        /// 时间间隔描述
        /// </summary>
        public string TimeSpanText
        {
            get
            {
                var span = LastTime - FirstTime;
                if (span.TotalSeconds < 60)
                    return string.Format("{0}秒 (可能抖动)", (int)span.TotalSeconds);
                if (span.TotalMinutes < 60)
                    return string.Format("{0}分钟", (int)span.TotalMinutes);
                if (span.TotalHours < 24)
                    return string.Format("{0}小时{1}分钟", (int)span.TotalHours, span.Minutes);
                return string.Format("{0}天", (int)span.TotalDays);
            }
        }
    }
}
