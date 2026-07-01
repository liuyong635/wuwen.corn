using SeedCut.Framework.Core.SlotSeedTracker;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// 转盘监控处理器（修复版 v2）
    /// 
    /// ★★★ v2 修复内容 ★★★
    /// 问题：防抖跳过后立即释放 Busy flag，导致条件瞬间再次满足，
    ///       形成 0ms 级别的死循环（busy-spin），最终打满 CPU 闪退。
    /// 
    /// 修复方案：
    /// 防抖命中时，await Task.Delay(剩余冷却时间) 再返回，
    /// 确保 Busy flag 在冷却期内保持为 true，从源头阻断重入循环。
    /// 
    /// ★★★ v1 修复内容 ★★★
    /// 1. 删除错误的"索引未变化"检查
    /// 2. 使用 Handler 级别的 _lastHandledIndex 防止同一信号周期内重复处理
    /// 3. 依赖 SlotSeedTracker.OnTurntableRotated() 内部的100ms防抖机制
    /// 
    /// 功能：
    /// 1. 监听转盘移动完成信号 (M1030.4)
    /// 2. 检测上升沿（0→1）时通知 SlotSeedTracker
    /// 3. 更新所有工位的种子映射
    /// 
    /// 触发条件：
    /// - 系统运行中
    /// - 转盘移动完成信号下降沿 (Turntable_MoveComplete = false，表示开始移动)
    /// - 自身非忙碌状态
    /// 
    /// 信号说明：
    /// - M1030.4 (上位机-移动完成): 1=移动完成, 0=移动中
    /// - 本Handler监听下降沿（开始移动），然后等待上升沿（移动完成）
    /// 
    /// 依赖设备：PLC
    /// 依赖服务：ISlotSeedTracker
    /// </summary>
    public class TurntableMonitorHandler : SignalHandlerBase
    {
        #region 常量定义

        /// <summary>
        /// 转盘移动完成信号
        /// M1030.4 - 上位机-移动完成 (1=完成, 0=移动中)
        /// </summary>
        private const string SIG_TURNTABLE_MOVE_COMPLETE = "Turntable_MoveComplete";

        /// <summary>
        /// 等待转盘移动完成的超时时间
        /// </summary>
        private const int MOVE_COMPLETE_TIMEOUT_SEC = 10;

        #endregion

        #region ★ 修复：正确的防重入字段

        /// <summary>
        /// ★ 记录上次成功调用 OnTurntableRotated 后的转盘索引
        /// 用于防止同一 Handler 执行周期内重复调用（虽然通常不会发生）
        /// </summary>
        private long _lastHandledIndex = -1;

        /// <summary>
        /// ★ 记录上次处理的时间戳，用于防抖
        /// 防止 PLC 信号抖动导致的重复触发
        /// </summary>
        private DateTime _lastHandledTime = DateTime.MinValue;

        /// <summary>
        /// 最小处理间隔（毫秒）
        /// 物理上转盘不可能在这个时间内完成两次转动
        /// </summary>
        private const int MIN_HANDLE_INTERVAL_MS = 500;

        #endregion

        #region Handler属性

        public override string HandlerId => "TurntableMonitor";

        public override string HandlerName => "转盘监控";

        /// <summary>
        /// 高优先级，确保转盘状态及时更新
        /// </summary>
        public override int Priority => 95;

        public override string[] DependentDevices => new[] { "PLC" };

        /// <summary>
        /// 触发条件：
        /// - 系统运行中
        /// - 转盘移动完成信号为 false（表示开始移动）
        /// - 非忙碌
        /// 
        /// 当信号从1变为0时触发本Handler，然后等待变回1
        /// </summary>
        public override ITriggerCondition TriggerCondition => When.All(
            When.IsRunning(),
            When.SignalOff(SIG_TURNTABLE_MOVE_COMPLETE),  // 信号为0时触发（开始移动）
            When.FlagOff("TurntableMonitor_Busy")
        );

        #endregion

        #region 执行逻辑

        protected override async Task<ValueTuple<bool, string>> ExecuteAsync(
            IHandlerContext ctx,
            CancellationToken ct)
        {
            SetBusy(true);
            FlagCondition.SetFlag("TurntableMonitor_Busy", true);

            try
            {
                // ★ 步骤1：防抖检查（在开始处理之前）
                var now = DateTime.Now;
                var elapsed = (now - _lastHandledTime).TotalMilliseconds;
                if (elapsed < MIN_HANDLE_INTERVAL_MS)
                {
                    // ★★★ v2 修复：等待剩余冷却时间再返回 ★★★
                    // 旧代码直接 return，导致 finally 立即释放 Busy flag，
                    // 条件瞬间再次满足 → 无限循环 → CPU 打满 → 闪退。
                    // 现在 await 剩余时间，让 Busy 在冷却期内保持为 true。
                    var remainingMs = (int)(MIN_HANDLE_INTERVAL_MS - elapsed);
                    LogDebug("防抖：距上次处理仅{0:F0}ms，等待{1}ms后释放", elapsed, remainingMs);
                    await Task.Delay(remainingMs, ct);
                    return Success("防抖跳过");
                }

                LogInfo("检测到转盘开始移动，等待完成...");

                // ★ 步骤2：等待移动完成信号变为1
                var completed = await ctx.Signal.WaitForBitAsync(
                    SIG_TURNTABLE_MOVE_COMPLETE,
                    true,  // 等待信号变为1（移动完成）
                    TimeSpan.FromSeconds(MOVE_COMPLETE_TIMEOUT_SEC),
                    ct);

                if (!completed)
                {
                    LogWarning("等待转盘移动完成超时({0}秒)", MOVE_COMPLETE_TIMEOUT_SEC);
                    return Fail("转盘移动超时");
                }

                // ★ 步骤3：获取追踪器并更新索引
                var tracker = ctx.GetService<ISlotSeedTracker>();

                if (tracker == null || !tracker.IsInitialized)
                {
                    LogWarning("追踪器不可用或未初始化，跳过索引更新");
                    return Success("追踪器不可用");
                }

                // ★★★ 关键修复：直接调用 OnTurntableRotated() ★★★
                // SlotSeedTracker 内部有 100ms 防抖机制，会自动处理重复调用
                var previousIndex = tracker.TurntableIndex;
                tracker.OnTurntableRotated();
                var newIndex = tracker.TurntableIndex;

                // 检查是否真的更新了（SlotSeedTracker内部防抖可能拒绝更新）
                if (newIndex == previousIndex)
                {
                    LogDebug("SlotSeedTracker 防抖生效，索引未变化");
                    return Success("防抖：索引未变化");
                }

                // ★ 步骤4：更新本地记录
                _lastHandledIndex = newIndex;
                _lastHandledTime = DateTime.Now;

                LogInfo("转盘转动完成: Index {0} → {1}", previousIndex, newIndex);

                return Success(string.Format("转盘转动完成: {0} → {1}", previousIndex, newIndex));
            }
            finally
            {
                SetBusy(false);
                FlagCondition.SetFlag("TurntableMonitor_Busy", false);
            }
        }

        #endregion
    }
}