using System;
using System.Collections.Generic;

namespace SeedCut.Framework.Core.SlotSeedTracker
{
    /// <summary>
    /// 工位种子追踪器接口
    /// 
    /// 职责：
    /// 1. 管理8个工位的种子状态
    /// 2. 响应转盘转动，更新工位映射
    /// 3. 提供种子ID查询和校验
    /// 4. 支持生命周期数据追溯
    /// 
    /// 使用方式：
    /// ```csharp
    /// var tracker = ctx.GetService<ISlotSeedTracker>();
    /// string seedId = tracker.GetOrCreateSeedIdAt(3);  // 工位3
    /// tracker.AttachData(seedId, "LaserVisionTime", DateTime.Now);
    /// ```
    /// </summary>
    public interface ISlotSeedTracker : IDisposable
    {
        #region 属性

        /// <summary>
        /// 工位总数（默认8）
        /// </summary>
        int SlotCount { get; }

        /// <summary>
        /// 当前转盘位置索引（0-based，每转一格+1）
        /// </summary>
        long TurntableIndex { get; }

        /// <summary>
        /// 是否已初始化
        /// </summary>
        bool IsInitialized { get; }

        #endregion

        #region 事件

        /// <summary>
        /// 转盘转动事件
        /// </summary>
        event EventHandler<TurntableRotatedEventArgs> TurntableRotated;

        /// <summary>
        /// 种子状态变更事件
        /// </summary>
        event EventHandler<SeedStateChangedEventArgs> SeedStateChanged;

        #endregion

        #region 工位查询

        /// <summary>
        /// 获取指定工位当前的种子ID
        /// </summary>
        /// <param name="slotNo">工位号（1-8）</param>
        /// <returns>种子ID，无种子返回null</returns>
        string GetSeedIdAt(int slotNo);

        /// <summary>
        /// 获取指定工位的种子ID，如果不存在则创建新的
        /// ★ 支持异常恢复场景：任何工位都可以创建种子ID
        /// </summary>
        /// <param name="slotNo">工位号（1-8）</param>
        /// <returns>种子ID（已存在或新创建）</returns>
        string GetOrCreateSeedIdAt(int slotNo);

        /// <summary>
        /// 获取指定工位的完整状态
        /// </summary>
        SlotState GetSlotState(int slotNo);

        /// <summary>
        /// 获取所有工位状态
        /// </summary>
        IReadOnlyList<SlotState> GetAllSlotStates();

        /// <summary>
        /// 检查指定工位是否有种子
        /// </summary>
        bool HasSeedAt(int slotNo);

        #endregion

        #region 种子生命周期

        /// <summary>
        /// 在指定工位放入新种子（生成新ID）
        /// 通常在工位1上料时调用
        /// </summary>
        /// <param name="slotNo">工位号</param>
        /// <param name="workpieceType">工件类型（可选）</param>
        /// <returns>新生成的种子ID</returns>
        string PlaceSeed(int slotNo, string workpieceType = "Seed");

        /// <summary>
        /// 标记指定工位的种子已移除
        /// 通常在工位5/7掉落时调用
        /// </summary>
        /// <param name="slotNo">工位号</param>
        /// <param name="reason">移除原因</param>
        void RemoveSeed(int slotNo, SeedRemoveReason reason = SeedRemoveReason.Normal);

        /// <summary>
        /// 附加数据到种子（如视觉结果、切割轨迹等）
        /// </summary>
        /// <param name="seedId">种子ID</param>
        /// <param name="key">数据键</param>
        /// <param name="data">数据对象</param>
        void AttachData(string seedId, string key, object data);

        /// <summary>
        /// 获取种子的附加数据
        /// </summary>
        T GetAttachedData<T>(string seedId, string key);

        /// <summary>
        /// 获取种子的完整生命周期数据
        /// </summary>
        SeedLifecycle GetLifecycle(string seedId);

        #endregion

        #region 转盘控制

        /// <summary>
        /// 通知转盘已转动一格
        /// 所有工位的种子ID向后移动一位（工位N的种子移到工位N+1）
        /// </summary>
        void OnTurntableRotated();

        /// <summary>
        /// 初始化/重置追踪器
        /// </summary>
        /// <param name="clearAllSlots">是否清空所有工位（默认true）</param>
        void Initialize(bool clearAllSlots = true);

        /// <summary>
        /// 手动设置工位状态（用于恢复或调试）
        /// </summary>
        void SetSlotState(int slotNo, string seedId, SlotOccupancy occupancy);

        #endregion

        #region 校验

        /// <summary>
        /// 校验种子ID是否与指定工位匹配
        /// </summary>
        /// <param name="slotNo">工位号</param>
        /// <param name="expectedSeedId">期望的种子ID</param>
        /// <returns>是否匹配</returns>
        bool ValidateSeedAt(int slotNo, string expectedSeedId);

        /// <summary>
        /// 获取最近N条生命周期记录（用于追溯）
        /// </summary>
        IReadOnlyList<SeedLifecycle> GetRecentLifecycles(int count = 100);

        #endregion
    }

    #region 事件参数

    /// <summary>
    /// 转盘转动事件参数
    /// </summary>
    public class TurntableRotatedEventArgs : EventArgs
    {
        /// <summary>
        /// 转动前的索引
        /// </summary>
        public long PreviousIndex { get; set; }

        /// <summary>
        /// 转动后的索引
        /// </summary>
        public long CurrentIndex { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 种子状态变更事件参数
    /// </summary>
    public class SeedStateChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 种子ID
        /// </summary>
        public string SeedId { get; set; }

        /// <summary>
        /// 工位号
        /// </summary>
        public int SlotNo { get; set; }

        /// <summary>
        /// 变更类型
        /// </summary>
        public SeedStateChangeType ChangeType { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 种子状态变更类型
    /// </summary>
    public enum SeedStateChangeType
    {
        /// <summary>
        /// 新放入
        /// </summary>
        Placed,

        /// <summary>
        /// 移动到新工位
        /// </summary>
        Moved,

        /// <summary>
        /// 移除
        /// </summary>
        Removed,

        /// <summary>
        /// 数据更新
        /// </summary>
        DataUpdated
    }

    #endregion
}