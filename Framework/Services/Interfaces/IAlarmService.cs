using System;
using System.Collections.Generic;
using SeedCut.Framework.Models;

namespace SeedCut.Framework.Services.Interfaces
{
    /// <summary>
    /// 报警服务接口
    /// 纯报警管理，不依赖任何具体设备（PLC、机器人等）
    /// </summary>
    public interface IAlarmService : IDisposable
    {
        #region 事件

        /// <summary>
        /// 报警触发事件
        /// </summary>
        event EventHandler<AlarmTriggeredEventArgs> AlarmTriggered;

        /// <summary>
        /// 报警恢复事件
        /// </summary>
        event EventHandler<AlarmRecoveredEventArgs> AlarmRecovered;

        /// <summary>
        /// 活动报警变化事件
        /// </summary>
        event EventHandler ActiveAlarmsChanged;

        #endregion

        #region 属性

        /// <summary>
        /// 活动报警数量
        /// </summary>
        int ActiveAlarmCount { get; }

        /// <summary>
        /// 未确认报警数量
        /// </summary>
        int UnacknowledgedAlarmCount { get; }

        /// <summary>
        /// 是否有活动报警
        /// </summary>
        bool HasActiveAlarms { get; }

        /// <summary>
        /// 最新的报警
        /// </summary>
        AlarmItem LatestAlarm { get; }

        #endregion

        #region 报警触发/恢复

        /// <summary>
        /// 触发报警
        /// </summary>
        /// <param name="name">报警名称</param>
        /// <param name="level">报警级别</param>
        /// <param name="description">详细描述（可选）</param>
        /// <param name="sourceModule">来源模块（如"PLC"、"机器人"、"视觉"）</param>
        /// <param name="sourceIdentifier">来源标识（如 PLC 地址名、设备 ID，用于恢复报警）</param>
        /// <param name="showPopup">是否显示弹窗</param>
        /// <returns>报警ID</returns>
        string TriggerAlarm(string name, AlarmLevel level,
            string description = null, string sourceModule = null,
            string sourceIdentifier = null, bool showPopup = true);

        /// <summary>
        /// 通过来源标识恢复报警
        /// 适用于：PLC 信号恢复、设备状态恢复等
        /// </summary>
        /// <param name="sourceIdentifier">来源标识</param>
        void RecoverAlarmBySource(string sourceIdentifier);

        /// <summary>
        /// 通过报警ID恢复报警
        /// </summary>
        /// <param name="alarmId">报警ID</param>
        void RecoverAlarm(string alarmId);

        #endregion

        #region 活动报警管理

        /// <summary>
        /// 获取所有活动报警
        /// </summary>
        IReadOnlyList<AlarmItem> GetActiveAlarms();

        /// <summary>
        /// 获取未确认的报警
        /// </summary>
        IReadOnlyList<AlarmItem> GetUnacknowledgedAlarms();

        /// <summary>
        /// 确认报警
        /// </summary>
        /// <param name="alarmId">报警ID</param>
        /// <param name="userName">确认人</param>
        void AcknowledgeAlarm(string alarmId, string userName = null);

        /// <summary>
        /// 确认所有报警
        /// </summary>
        /// <param name="userName">确认人</param>
        void AcknowledgeAllAlarms(string userName = null);

        /// <summary>
        /// 清除已恢复且已确认的报警
        /// </summary>
        void ClearRecoveredAlarms();

        /// <summary>
        /// 强制清除所有活动报警
        /// </summary>
        void ClearAllAlarms();

        #endregion

        #region 历史记录

        /// <summary>
        /// 获取报警历史
        /// </summary>
        /// <param name="maxCount">最大数量</param>
        IReadOnlyList<AlarmItem> GetAlarmHistory(int maxCount = 100);

        /// <summary>
        /// 获取指定时间范围的报警历史
        /// </summary>
        IReadOnlyList<AlarmItem> GetAlarmHistory(DateTime startTime, DateTime endTime);

        /// <summary>
        /// 清除报警历史
        /// </summary>
        void ClearAlarmHistory();

        #endregion

        #region 测试

        /// <summary>
        /// 触发测试报警
        /// </summary>
        void TriggerTestAlarm(string name, AlarmLevel level, string description = null);

        #endregion
    }

    #region 事件参数类

    /// <summary>
    /// 报警触发事件参数
    /// </summary>
    public class AlarmTriggeredEventArgs : EventArgs
    {
        /// <summary>
        /// 报警实例
        /// </summary>
        public AlarmItem Alarm { get; private set; }

        /// <summary>
        /// 是否显示弹窗
        /// </summary>
        public bool ShowPopup { get; private set; }

        public AlarmTriggeredEventArgs(AlarmItem alarm, bool showPopup)
        {
            Alarm = alarm;
            ShowPopup = showPopup;
        }

        public AlarmTriggeredEventArgs(AlarmItem alarm)
            : this(alarm, alarm != null && alarm.ShowPopup)
        {
        }
    }

    /// <summary>
    /// 报警恢复事件参数
    /// </summary>
    public class AlarmRecoveredEventArgs : EventArgs
    {
        /// <summary>
        /// 报警实例
        /// </summary>
        public AlarmItem Alarm { get; private set; }

        /// <summary>
        /// 恢复时间
        /// </summary>
        public DateTime RecoveredTime { get; private set; }

        /// <summary>
        /// 持续时间
        /// </summary>
        public TimeSpan Duration { get; private set; }

        public AlarmRecoveredEventArgs(AlarmItem alarm)
        {
            Alarm = alarm;
            RecoveredTime = alarm.RecoveredTime ?? DateTime.Now;
            Duration = alarm.Duration;
        }
    }

    #endregion
}