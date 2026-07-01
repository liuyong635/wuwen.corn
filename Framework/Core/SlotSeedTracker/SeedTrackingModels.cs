using System;
using System.Collections.Generic;

namespace SeedCut.Framework.Core.SlotSeedTracker
{
    /// <summary>
    /// 单个工位状态
    /// </summary>
    public class SlotState
    {
        /// <summary>
        /// 工位号（1-8）
        /// </summary>
        public int SlotNo { get; set; }

        /// <summary>
        /// 当前种子ID（null表示空工位）
        /// </summary>
        public string SeedId { get; set; }

        /// <summary>
        /// 工位占用状态
        /// </summary>
        public SlotOccupancy Occupancy { get; set; }

        /// <summary>
        /// 种子进入此工位的时间
        /// </summary>
        public DateTime? EnteredAt { get; set; }

        /// <summary>
        /// 工位描述（用于显示）
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 是否为关键工位（有实际功能）
        /// </summary>
        public bool IsKeyStation { get; set; }

        /// <summary>
        /// 克隆当前状态
        /// </summary>
        public SlotState Clone()
        {
            return new SlotState
            {
                SlotNo = SlotNo,
                SeedId = SeedId,
                Occupancy = Occupancy,
                EnteredAt = EnteredAt,
                Description = Description,
                IsKeyStation = IsKeyStation
            };
        }

        public override string ToString()
        {
            return string.Format("工位{0}[{1}]: {2}",
                SlotNo,
                Description ?? "-",
                string.IsNullOrEmpty(SeedId) ? "空" : SeedId);
        }
    }

    /// <summary>
    /// 工位占用状态
    /// </summary>
    public enum SlotOccupancy
    {
        /// <summary>
        /// 空（无种子）
        /// </summary>
        Empty,

        /// <summary>
        /// 占用（有种子）
        /// </summary>
        Occupied,

        /// <summary>
        /// 未知（启动时未确认）
        /// </summary>
        Unknown
    }

    /// <summary>
    /// 种子移除原因
    /// </summary>
    public enum SeedRemoveReason
    {
        /// <summary>
        /// 正常流程结束
        /// </summary>
        Normal,

        /// <summary>
        /// 检测失败（方向错误等）
        /// </summary>
        Failed,

        /// <summary>
        /// 手动移除
        /// </summary>
        Manual,

        /// <summary>
        /// 系统重置
        /// </summary>
        SystemReset
    }

    /// <summary>
    /// 种子生命周期数据
    /// </summary>
    public class SeedLifecycle
    {
        /// <summary>
        /// 种子唯一ID
        /// </summary>
        public string SeedId { get; set; }

        /// <summary>
        /// 创建时间（上料时间）
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 完成时间
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// 当前状态
        /// </summary>
        public SeedStatus Status { get; set; }

        /// <summary>
        /// 结果（OK/NG）
        /// </summary>
        public SeedResult? Result { get; set; }

        /// <summary>
        /// 工件类型
        /// </summary>
        public string WorkpieceType { get; set; } = "Seed";

        /// <summary>
        /// 创建时的工位号
        /// </summary>
        public int CreatedAtSlot { get; set; }

        /// <summary>
        /// 当前所在工位号
        /// </summary>
        public int CurrentSlotNo { get; set; }

        /// <summary>
        /// 工位历史记录
        /// </summary>
        public List<SlotVisitRecord> SlotHistory { get; set; } = new List<SlotVisitRecord>();

        /// <summary>
        /// 附加数据（视觉图像、切割轨迹等）
        /// </summary>
        public Dictionary<string, object> Attachments { get; set; } = new Dictionary<string, object>();

        #region 便捷访问器

        /// <summary>
        /// 激光视觉时间
        /// </summary>
        public DateTime? LaserVisionTime => GetAttachment<DateTime?>("LaserVisionTime");

        /// <summary>
        /// 激光切割时间
        /// </summary>
        public DateTime? LaserCutTime => GetAttachment<DateTime?>("LaserCutTime");

        /// <summary>
        /// 激光视觉是否有有效数据
        /// </summary>
        public bool? HasValidLaserData => GetAttachment<bool?>("HasValidLaserData");

        /// <summary>
        /// 激光命令长度
        /// </summary>
        public int? LaserCommandLength => GetAttachment<int?>("LaserCommandLength");

        /// <summary>
        /// 拍照时的转盘索引
        /// </summary>
        public long? VisionTurntableIndex => GetAttachment<long?>("VisionTurntableIndex");

        /// <summary>
        /// 切割时的转盘索引
        /// </summary>
        public long? CutTurntableIndex => GetAttachment<long?>("CutTurntableIndex");

        private T GetAttachment<T>(string key)
        {
            if (Attachments != null && Attachments.TryGetValue(key, out var val))
            {
                if (val is T t) return t;
                // 尝试转换
                try
                {
                    return (T)Convert.ChangeType(val, typeof(T));
                }
                catch { }
            }
            return default;
        }

        #endregion

        /// <summary>
        /// 添加工位访问记录
        /// </summary>
        public void RecordSlotVisit(int slotNo, string action)
        {
            // 结束上一个工位的访问
            if (SlotHistory.Count > 0)
            {
                var last = SlotHistory[SlotHistory.Count - 1];
                if (!last.LeftAt.HasValue)
                {
                    last.LeftAt = DateTime.Now;
                }
            }

            // 添加新记录
            SlotHistory.Add(new SlotVisitRecord
            {
                SlotNo = slotNo,
                EnteredAt = DateTime.Now,
                Action = action
            });

            CurrentSlotNo = slotNo;
        }

        public override string ToString()
        {
            return string.Format("Seed[{0}] Status={1} Slot={2} Created={3:HH:mm:ss}",
                SeedId, Status, CurrentSlotNo, CreatedAt);
        }
    }

    /// <summary>
    /// 工位访问记录
    /// </summary>
    public class SlotVisitRecord
    {
        public int SlotNo { get; set; }
        public DateTime EnteredAt { get; set; }
        public DateTime? LeftAt { get; set; }
        public string Action { get; set; }  // "上料", "激光视觉", "激光切割", etc.

        /// <summary>
        /// 停留时长
        /// </summary>
        public TimeSpan? Duration => LeftAt.HasValue ? LeftAt.Value - EnteredAt : (TimeSpan?)null;

        public override string ToString()
        {
            return string.Format("工位{0}: {1} @ {2:HH:mm:ss}", SlotNo, Action, EnteredAt);
        }
    }

    /// <summary>
    /// 种子状态
    /// </summary>
    public enum SeedStatus
    {
        /// <summary>
        /// 流转中
        /// </summary>
        InProgress,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed,

        /// <summary>
        /// 已失败
        /// </summary>
        Failed,

        /// <summary>
        /// 已丢弃
        /// </summary>
        Discarded
    }

    /// <summary>
    /// 种子结果
    /// </summary>
    public enum SeedResult
    {
        OK,
        NG
    }

    /// <summary>
    /// 工位配置（用于初始化）
    /// </summary>
    public static class SlotConfiguration
    {
        /// <summary>
        /// 获取默认的8工位配置
        /// </summary>
        public static List<SlotState> GetDefaultSlots()
        {
            return new List<SlotState>
            {
                new SlotState { SlotNo = 1, Description = "上料", IsKeyStation = true },
                new SlotState { SlotNo = 2, Description = "过渡", IsKeyStation = false },
                new SlotState { SlotNo = 3, Description = "激光视觉", IsKeyStation = true },
                new SlotState { SlotNo = 4, Description = "过渡", IsKeyStation = false },
                new SlotState { SlotNo = 5, Description = "激光切割", IsKeyStation = true },
                new SlotState { SlotNo = 6, Description = "过渡", IsKeyStation = false },
                new SlotState { SlotNo = 7, Description = "大料盘检测", IsKeyStation = true },
                new SlotState { SlotNo = 8, Description = "过渡", IsKeyStation = false }
            };
        }

        /// <summary>
        /// 视觉工位到切割工位的间隔（工位数）
        /// </summary>
        public const int VisionToCutGap = 2;  // 工位3 → 工位5

        /// <summary>
        /// 上料工位
        /// </summary>
        public const int LoadingSlot = 1;

        /// <summary>
        /// 激光视觉工位
        /// </summary>
        public const int LaserVisionSlot = 3;

        /// <summary>
        /// 激光切割工位
        /// </summary>
        public const int LaserCutSlot = 5;

        /// <summary>
        /// 大料盘检测工位
        /// </summary>
        public const int LargeTraySlot = 7;
    }
}