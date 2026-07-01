using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace SeedCut.Framework.Core.SlotSeedTracker
{
    /// <summary>
    /// 工位种子追踪器实现（修复版）
    /// 
    /// ★★★ 修复内容 ★★★
    /// 1. 优化 OnTurntableRotated 的防抖日志，使用 Debug 而非 Warning
    /// 2. 增加防抖时间到 300ms（更安全的阈值）
    /// 
    /// 线程安全，使用锁保护工位状态的一致性
    /// </summary>
    public class SlotSeedTracker : ISlotSeedTracker
    {
        #region 私有字段

        private readonly object _lock = new object();
        private readonly ILogService _logService;

        // 工位状态数组（索引0=工位1, 索引7=工位8）
        private readonly SlotState[] _slots;

        // 种子生命周期缓存：SeedId -> Lifecycle
        private readonly ConcurrentDictionary<string, SeedLifecycle> _lifecycles;

        // 已完成的生命周期记录（用于追溯）
        private readonly List<SeedLifecycle> _completedLifecycles;
        private const int MaxCompletedRecords = 1000;

        /// <summary>
        /// ★ 修复：防抖时间戳
        /// </summary>
        private DateTime _lastRotationTime = DateTime.MinValue;

        /// <summary>
        /// ★ 修复：防抖间隔（从100ms增加到300ms）
        /// 物理上转盘转动一格至少需要几百毫秒
        /// </summary>
        private const int MIN_ROTATION_INTERVAL_MS = 300;

        // 种子ID生成计数器
        private long _seedIdCounter;

        // 转盘索引
        private long _turntableIndex;

        // 初始化标志
        private bool _isInitialized;
        private bool _disposed;

        #endregion

        #region 属性

        public int SlotCount => _slots.Length;
        public long TurntableIndex => _turntableIndex;
        public bool IsInitialized => _isInitialized;

        #endregion

        #region 事件

        public event EventHandler<TurntableRotatedEventArgs> TurntableRotated;
        public event EventHandler<SeedStateChangedEventArgs> SeedStateChanged;

        #endregion

        #region 构造函数

        public SlotSeedTracker(ILogService logService = null)
        {
            _logService = logService;
            _lifecycles = new ConcurrentDictionary<string, SeedLifecycle>();
            _completedLifecycles = new List<SeedLifecycle>();

            // 初始化8个工位
            var defaultSlots = SlotConfiguration.GetDefaultSlots();
            _slots = new SlotState[defaultSlots.Count];
            for (int i = 0; i < defaultSlots.Count; i++)
            {
                _slots[i] = defaultSlots[i];
                _slots[i].Occupancy = SlotOccupancy.Empty;
            }

            LogInfo("SlotSeedTracker 已创建，共 {0} 个工位", _slots.Length);
        }

        #endregion

        #region 工位查询

        public string GetSeedIdAt(int slotNo)
        {
            ValidateSlotNo(slotNo);
            lock (_lock)
            {
                return _slots[slotNo - 1].SeedId;
            }
        }

        public string GetOrCreateSeedIdAt(int slotNo)
        {
            ValidateSlotNo(slotNo);
            lock (_lock)
            {
                var slot = _slots[slotNo - 1];

                // 如果已有种子ID，直接返回
                if (!string.IsNullOrEmpty(slot.SeedId))
                {
                    LogDebug("工位{0}已有种子: {1}", slotNo, slot.SeedId);
                    return slot.SeedId;
                }

                // 创建新的种子ID（支持异常恢复场景）
                var seedId = GenerateSeedId();
                slot.SeedId = seedId;
                slot.Occupancy = SlotOccupancy.Occupied;
                slot.EnteredAt = DateTime.Now;

                // 创建生命周期
                var lifecycle = new SeedLifecycle
                {
                    SeedId = seedId,
                    CreatedAt = DateTime.Now,
                    Status = SeedStatus.InProgress,
                    CreatedAtSlot = slotNo,
                    CurrentSlotNo = slotNo
                };
                lifecycle.RecordSlotVisit(slotNo, GetSlotAction(slotNo) + "(恢复创建)");
                _lifecycles[seedId] = lifecycle;

                LogInfo("工位{0}创建新种子(恢复模式): {1}", slotNo, seedId);

                RaiseSeedStateChanged(seedId, slotNo, SeedStateChangeType.Placed);
                return seedId;
            }
        }

        public SlotState GetSlotState(int slotNo)
        {
            ValidateSlotNo(slotNo);
            lock (_lock)
            {
                return _slots[slotNo - 1].Clone();
            }
        }

        public IReadOnlyList<SlotState> GetAllSlotStates()
        {
            lock (_lock)
            {
                return _slots.Select(s => s.Clone()).ToList();
            }
        }

        public bool HasSeedAt(int slotNo)
        {
            ValidateSlotNo(slotNo);
            lock (_lock)
            {
                return !string.IsNullOrEmpty(_slots[slotNo - 1].SeedId);
            }
        }

        #endregion

        #region 种子生命周期

        public string PlaceSeed(int slotNo, string workpieceType = "Seed")
        {
            ValidateSlotNo(slotNo);
            lock (_lock)
            {
                var slot = _slots[slotNo - 1];

                // 如果工位已有种子，先标记旧种子为丢弃
                if (!string.IsNullOrEmpty(slot.SeedId))
                {
                    LogWarning("工位{0}已有种子{1}，将被覆盖", slotNo, slot.SeedId);
                    MarkSeedCompleted(slot.SeedId, SeedStatus.Discarded, SeedRemoveReason.SystemReset);
                }

                // 生成新ID
                var seedId = GenerateSeedId();
                slot.SeedId = seedId;
                slot.Occupancy = SlotOccupancy.Occupied;
                slot.EnteredAt = DateTime.Now;

                // 创建生命周期
                var lifecycle = new SeedLifecycle
                {
                    SeedId = seedId,
                    CreatedAt = DateTime.Now,
                    Status = SeedStatus.InProgress,
                    WorkpieceType = workpieceType,
                    CreatedAtSlot = slotNo,
                    CurrentSlotNo = slotNo
                };
                lifecycle.RecordSlotVisit(slotNo, GetSlotAction(slotNo));
                _lifecycles[seedId] = lifecycle;

                LogInfo("工位{0}放入种子: {1}", slotNo, seedId);

                RaiseSeedStateChanged(seedId, slotNo, SeedStateChangeType.Placed);
                return seedId;
            }
        }

        public void RemoveSeed(int slotNo, SeedRemoveReason reason = SeedRemoveReason.Normal)
        {
            ValidateSlotNo(slotNo);
            lock (_lock)
            {
                var slot = _slots[slotNo - 1];
                var seedId = slot.SeedId;

                if (string.IsNullOrEmpty(seedId))
                {
                    LogDebug("工位{0}无种子，跳过移除", slotNo);
                    return;
                }

                // 标记生命周期完成
                var status = reason == SeedRemoveReason.Normal ? SeedStatus.Completed :
                             reason == SeedRemoveReason.Failed ? SeedStatus.Failed : SeedStatus.Discarded;
                MarkSeedCompleted(seedId, status, reason);

                // 清空工位
                slot.SeedId = null;
                slot.Occupancy = SlotOccupancy.Empty;
                slot.EnteredAt = null;

                LogInfo("工位{0}移除种子: {1}, 原因: {2}", slotNo, seedId, reason);

                RaiseSeedStateChanged(seedId, slotNo, SeedStateChangeType.Removed);
            }
        }

        public void AttachData(string seedId, string key, object data)
        {
            if (string.IsNullOrEmpty(seedId) || string.IsNullOrEmpty(key))
                return;

            if (_lifecycles.TryGetValue(seedId, out var lifecycle))
            {
                lock (lifecycle.Attachments)
                {
                    lifecycle.Attachments[key] = data;
                }
                LogDebug("种子{0}附加数据: {1}", seedId, key);
            }
        }

        public T GetAttachedData<T>(string seedId, string key)
        {
            if (string.IsNullOrEmpty(seedId) || string.IsNullOrEmpty(key))
                return default;

            if (_lifecycles.TryGetValue(seedId, out var lifecycle))
            {
                lock (lifecycle.Attachments)
                {
                    if (lifecycle.Attachments.TryGetValue(key, out var val) && val is T t)
                        return t;
                }
            }
            return default;
        }

        public SeedLifecycle GetLifecycle(string seedId)
        {
            if (string.IsNullOrEmpty(seedId))
                return null;

            _lifecycles.TryGetValue(seedId, out var lifecycle);
            return lifecycle;
        }

        #endregion

        #region 转盘控制

        /// <summary>
        /// 通知转盘已转动一格
        /// 
        /// ★★★ 修复：优化防抖机制 ★★★
        /// - 使用 Debug 日志而非 Warning（防抖是正常行为，不是警告）
        /// - 增加防抖间隔到 300ms
        /// </summary>
        public void OnTurntableRotated()
        {
            lock (_lock)
            {
                // ★ 防抖检查
                var now = DateTime.Now;
                var elapsed = (now - _lastRotationTime).TotalMilliseconds;
                if (elapsed < MIN_ROTATION_INTERVAL_MS)
                {
                    // ★ 修复：使用 Debug 而非 Warning，这是正常的防抖行为
                    LogDebug("转盘防抖：距上次转动仅{0:F0}ms，忽略本次更新", elapsed);
                    return;
                }
                _lastRotationTime = now;

                var previousIndex = _turntableIndex;
                _turntableIndex++;

                // 环形移动：所有种子向后移动一位
                // 工位8的种子移出（通常应该已经被移除）
                // 工位N的种子移到工位N+1
                var lastSeedId = _slots[7].SeedId;
                if (!string.IsNullOrEmpty(lastSeedId))
                {
                    LogWarning("工位8仍有种子{0}，转盘转动时将丢失", lastSeedId);
                    MarkSeedCompleted(lastSeedId, SeedStatus.Discarded, SeedRemoveReason.Normal);
                }

                // 从后向前移动
                for (int i = 7; i > 0; i--)
                {
                    _slots[i].SeedId = _slots[i - 1].SeedId;
                    _slots[i].Occupancy = _slots[i - 1].Occupancy;
                    _slots[i].EnteredAt = string.IsNullOrEmpty(_slots[i].SeedId) ? null : (DateTime?)DateTime.Now;

                    // 更新生命周期的工位记录
                    if (!string.IsNullOrEmpty(_slots[i].SeedId))
                    {
                        var seedId = _slots[i].SeedId;
                        if (_lifecycles.TryGetValue(seedId, out var lifecycle))
                        {
                            lifecycle.RecordSlotVisit(i + 1, GetSlotAction(i + 1));
                        }
                    }
                }

                // 工位1清空（等待下一次上料）
                _slots[0].SeedId = null;
                _slots[0].Occupancy = SlotOccupancy.Empty;
                _slots[0].EnteredAt = null;

                LogInfo("转盘转动: Index {0} -> {1}", previousIndex, _turntableIndex);
                LogDebug("当前工位状态: {0}", GetSlotSummary());

                // 触发事件
                TurntableRotated?.Invoke(this, new TurntableRotatedEventArgs
                {
                    PreviousIndex = previousIndex,
                    CurrentIndex = _turntableIndex
                });
            }
        }

        public void Initialize(bool clearAllSlots = true)
        {
            lock (_lock)
            {
                if (clearAllSlots)
                {
                    // 清空所有工位
                    foreach (var slot in _slots)
                    {
                        if (!string.IsNullOrEmpty(slot.SeedId))
                        {
                            MarkSeedCompleted(slot.SeedId, SeedStatus.Discarded, SeedRemoveReason.SystemReset);
                        }
                        slot.SeedId = null;
                        slot.Occupancy = SlotOccupancy.Empty;
                        slot.EnteredAt = null;
                    }
                    LogInfo("追踪器已初始化，所有工位已清空");
                }
                else
                {
                    // 标记为Unknown状态
                    foreach (var slot in _slots)
                    {
                        if (string.IsNullOrEmpty(slot.SeedId))
                        {
                            slot.Occupancy = SlotOccupancy.Unknown;
                        }
                    }
                    LogInfo("追踪器已初始化，工位状态未知（谨慎模式）");
                }

                _turntableIndex = 0;
                _lastRotationTime = DateTime.MinValue;  // ★ 重置防抖时间戳
                _isInitialized = true;
            }
        }

        public void SetSlotState(int slotNo, string seedId, SlotOccupancy occupancy)
        {
            ValidateSlotNo(slotNo);
            lock (_lock)
            {
                var slot = _slots[slotNo - 1];
                slot.SeedId = seedId;
                slot.Occupancy = occupancy;
                slot.EnteredAt = string.IsNullOrEmpty(seedId) ? null : (DateTime?)DateTime.Now;

                LogInfo("手动设置工位{0}: SeedId={1}, Occupancy={2}", slotNo, seedId ?? "null", occupancy);
            }
        }

        #endregion

        #region 校验

        public bool ValidateSeedAt(int slotNo, string expectedSeedId)
        {
            ValidateSlotNo(slotNo);
            lock (_lock)
            {
                var actualSeedId = _slots[slotNo - 1].SeedId;
                var match = actualSeedId == expectedSeedId;

                if (!match)
                {
                    LogWarning("种子校验失败！工位{0}: 期望={1}, 实际={2}",
                        slotNo, expectedSeedId ?? "null", actualSeedId ?? "null");
                }

                return match;
            }
        }

        public IReadOnlyList<SeedLifecycle> GetRecentLifecycles(int count = 100)
        {
            lock (_completedLifecycles)
            {
                return _completedLifecycles
                    .OrderByDescending(l => l.CompletedAt ?? l.CreatedAt)
                    .Take(count)
                    .ToList();
            }
        }

        #endregion

        #region 私有辅助方法

        private void ValidateSlotNo(int slotNo)
        {
            if (slotNo < 1 || slotNo > _slots.Length)
                throw new ArgumentOutOfRangeException(nameof(slotNo),
                    string.Format("工位号必须在1-{0}之间", _slots.Length));
        }

        private string GenerateSeedId()
        {
            var counter = System.Threading.Interlocked.Increment(ref _seedIdCounter);
            return string.Format("SEED_{0:yyyyMMdd}_{1:D4}", DateTime.Now, counter % 10000);
        }

        private void MarkSeedCompleted(string seedId, SeedStatus status, SeedRemoveReason reason)
        {
            if (_lifecycles.TryGetValue(seedId, out var lifecycle))
            {
                lifecycle.Status = status;
                lifecycle.CompletedAt = DateTime.Now;
                lifecycle.Result = status == SeedStatus.Completed ? SeedResult.OK :
                                   status == SeedStatus.Failed ? SeedResult.NG : (SeedResult?)null;

                // 移到已完成列表
                lock (_completedLifecycles)
                {
                    _completedLifecycles.Add(lifecycle);
                    // 保持列表大小
                    while (_completedLifecycles.Count > MaxCompletedRecords)
                    {
                        _completedLifecycles.RemoveAt(0);
                    }
                }

                // 从活跃列表移除
                _lifecycles.TryRemove(seedId, out _);
            }
        }

        private string GetSlotAction(int slotNo)
        {
            switch (slotNo)
            {
                case 1: return "上料";
                case 3: return "激光视觉";
                case 5: return "激光切割";
                case 7: return "大料盘检测";
                default: return "过渡";
            }
        }

        private string GetSlotSummary()
        {
            var parts = new List<string>();
            foreach (var slot in _slots)
            {
                var seedPart = string.IsNullOrEmpty(slot.SeedId) ? "-" : slot.SeedId.Substring(slot.SeedId.Length - 4);
                parts.Add(string.Format("{0}:{1}", slot.SlotNo, seedPart));
            }
            return string.Join(" | ", parts);
        }

        private void RaiseSeedStateChanged(string seedId, int slotNo, SeedStateChangeType changeType)
        {
            SeedStateChanged?.Invoke(this, new SeedStateChangedEventArgs
            {
                SeedId = seedId,
                SlotNo = slotNo,
                ChangeType = changeType
            });
        }

        #endregion

        #region 日志

        private void LogInfo(string message, params object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            _logService?.Information("[SlotSeedTracker] " + formatted);
            System.Diagnostics.Debug.WriteLine("[SlotSeedTracker] " + formatted);
        }

        private void LogDebug(string message, params object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            _logService?.Debug("[SlotSeedTracker] " + formatted);
        }

        private void LogWarning(string message, params object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            _logService?.Warning("[SlotSeedTracker] ⚠️ " + formatted);
            System.Diagnostics.Debug.WriteLine("[SlotSeedTracker] ⚠️ " + formatted);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_lock)
            {
                _lifecycles.Clear();
                _completedLifecycles.Clear();
            }

            LogInfo("SlotSeedTracker 已释放");
        }

        #endregion
    }
}