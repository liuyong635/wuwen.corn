using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SeedCut.Framework.Models;
using SeedCut.Framework.Services.Interfaces;

namespace SeedCut.Framework.Services.Alarm
{
    /// <summary>
    /// 报警服务实现
    /// 纯报警管理，不依赖任何具体设备
    /// </summary>
    public class AlarmService : IAlarmService
    {
        private readonly ILogService _logService;

        // 活动报警字典（Key: SourceIdentifier 或 AlarmId）
        private readonly ConcurrentDictionary<string, AlarmItem> _activeAlarms;

        // 按来源标识索引的活动报警（用于快速查找）
        private readonly ConcurrentDictionary<string, string> _sourceToAlarmIdMap;

        // 报警历史列表
        private readonly List<AlarmItem> _alarmHistory;
        private readonly object _historyLock = new object();
        private const int MaxHistoryCount = 1000;

        // 报警代码计数器
        private int _alarmCodeCounter = 1;
        private readonly object _codeLocker = new object();

        private bool _disposed;

        #region 事件

        public event EventHandler<AlarmTriggeredEventArgs> AlarmTriggered;
        public event EventHandler<AlarmRecoveredEventArgs> AlarmRecovered;
        public event EventHandler ActiveAlarmsChanged;

        #endregion

        #region 属性

        public int ActiveAlarmCount
        {
            get { return _activeAlarms.Count; }
        }

        public int UnacknowledgedAlarmCount
        {
            get { return _activeAlarms.Values.Count(a => !a.IsAcknowledged); }
        }

        public bool HasActiveAlarms
        {
            get { return _activeAlarms.Count > 0; }
        }

        public AlarmItem LatestAlarm
        {
            get { return _activeAlarms.Values.OrderByDescending(a => a.TriggerTime).FirstOrDefault(); }
        }

        #endregion

        #region 构造函数

        public AlarmService(ILogService logService)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));

            _activeAlarms = new ConcurrentDictionary<string, AlarmItem>(StringComparer.OrdinalIgnoreCase);
            _sourceToAlarmIdMap = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _alarmHistory = new List<AlarmItem>();

            _logService.Information("报警服务已初始化");
        }

        /// <summary>
        /// 无日志服务的构造函数（用于测试或简单场景）
        /// </summary>
        public AlarmService()
        {
            _activeAlarms = new ConcurrentDictionary<string, AlarmItem>(StringComparer.OrdinalIgnoreCase);
            _sourceToAlarmIdMap = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _alarmHistory = new List<AlarmItem>();
        }

        #endregion

        #region 报警触发/恢复

        public string TriggerAlarm(string name, AlarmLevel level,
            string description = null, string sourceModule = null,
            string sourceIdentifier = null, bool showPopup = true)
        {
            // 如果有来源标识，检查是否已存在该来源的活动报警
            if (!string.IsNullOrEmpty(sourceIdentifier))
            {
                if (_sourceToAlarmIdMap.ContainsKey(sourceIdentifier))
                {
                    // 该来源的报警已存在，不重复触发
                    return _sourceToAlarmIdMap[sourceIdentifier];
                }
            }

            var alarm = new AlarmItem
            {
                AlarmCode = GenerateAlarmCode(level),
                Name = name,
                Description = description ?? name,
                Level = level,
                SourceModule = sourceModule,
                SourceIdentifier = sourceIdentifier,
                TriggerTime = DateTime.Now,
                ShowPopup = showPopup,
                IsActive = true
            };

            // 使用 AlarmId 作为主键
            _activeAlarms[alarm.AlarmId] = alarm;

            // 如果有来源标识，建立索引
            if (!string.IsNullOrEmpty(sourceIdentifier))
            {
                _sourceToAlarmIdMap[sourceIdentifier] = alarm.AlarmId;
            }

            // 添加到历史
            AddToHistory(alarm.Clone());

            // 记录日志
            if (_logService != null)
            {
                _logService.LogAlarmTriggered(alarm);
            }

            System.Diagnostics.Debug.WriteLine(string.Format("🚨 报警触发: [{0}] {1} | 来源: {2}",
                alarm.AlarmCode, alarm.Name, sourceIdentifier ?? "N/A"));

            // 触发事件（在 UI 线程）
            RaiseEventOnUIThread(() =>
            {
                var handler = AlarmTriggered;
                if (handler != null)
                {
                    handler(this, new AlarmTriggeredEventArgs(alarm, showPopup));
                }

                var changedHandler = ActiveAlarmsChanged;
                if (changedHandler != null)
                {
                    changedHandler(this, EventArgs.Empty);
                }
            });

            return alarm.AlarmId;
        }

        public void RecoverAlarmBySource(string sourceIdentifier)
        {
            if (string.IsNullOrEmpty(sourceIdentifier))
                return;

            string alarmId;
            if (_sourceToAlarmIdMap.TryGetValue(sourceIdentifier, out alarmId))
            {
                RecoverAlarmInternal(alarmId, sourceIdentifier);
            }
        }

        public void RecoverAlarm(string alarmId)
        {
            if (string.IsNullOrEmpty(alarmId))
                return;

            RecoverAlarmInternal(alarmId, null);
        }

        private void RecoverAlarmInternal(string alarmId, string sourceIdentifier)
        {
            AlarmItem alarm;
            if (_activeAlarms.TryGetValue(alarmId, out alarm))
            {
                alarm.MarkAsRecovered();

                // 如果已确认，从活动列表移除
                if (alarm.IsAcknowledged)
                {
                    AlarmItem removed;
                    _activeAlarms.TryRemove(alarmId, out removed);
                }

                // 移除来源索引
                if (!string.IsNullOrEmpty(alarm.SourceIdentifier))
                {
                    string removedId;
                    _sourceToAlarmIdMap.TryRemove(alarm.SourceIdentifier, out removedId);
                }

                // 更新历史记录
                UpdateHistoryRecord(alarm);

                // 记录日志
                if (_logService != null)
                {
                    _logService.LogAlarmRecovered(alarm);
                }

                System.Diagnostics.Debug.WriteLine(string.Format("✅ 报警恢复: [{0}] {1}", alarm.AlarmCode, alarm.Name));

                // 触发事件
                RaiseEventOnUIThread(() =>
                {
                    var handler = AlarmRecovered;
                    if (handler != null)
                    {
                        handler(this, new AlarmRecoveredEventArgs(alarm));
                    }

                    var changedHandler = ActiveAlarmsChanged;
                    if (changedHandler != null)
                    {
                        changedHandler(this, EventArgs.Empty);
                    }
                });
            }
        }

        #endregion

        #region 活动报警管理

        public IReadOnlyList<AlarmItem> GetActiveAlarms()
        {
            return _activeAlarms.Values.OrderByDescending(a => a.TriggerTime).ToList().AsReadOnly();
        }

        public IReadOnlyList<AlarmItem> GetUnacknowledgedAlarms()
        {
            return _activeAlarms.Values
                .Where(a => !a.IsAcknowledged)
                .OrderByDescending(a => a.TriggerTime)
                .ToList()
                .AsReadOnly();
        }

        public void AcknowledgeAlarm(string alarmId, string userName = null)
        {
            AlarmItem alarm;
            if (_activeAlarms.TryGetValue(alarmId, out alarm))
            {
                alarm.Acknowledge(userName);

                // 记录日志
                if (_logService != null)
                {
                    _logService.LogAlarmAcknowledged(alarm, userName ?? "Unknown");
                }

                // 如果已恢复且已确认，从活动列表移除
                if (!alarm.IsActive)
                {
                    AlarmItem removed;
                    _activeAlarms.TryRemove(alarmId, out removed);

                    if (!string.IsNullOrEmpty(alarm.SourceIdentifier))
                    {
                        string removedId;
                        _sourceToAlarmIdMap.TryRemove(alarm.SourceIdentifier, out removedId);
                    }
                }

                UpdateHistoryRecord(alarm);

                RaiseEventOnUIThread(() =>
                {
                    var handler = ActiveAlarmsChanged;
                    if (handler != null)
                    {
                        handler(this, EventArgs.Empty);
                    }
                });
            }
        }

        public void AcknowledgeAllAlarms(string userName = null)
        {
            var alarmsToAck = _activeAlarms.Values.Where(a => !a.IsAcknowledged).ToList();

            foreach (var alarm in alarmsToAck)
            {
                alarm.Acknowledge(userName);

                if (_logService != null)
                {
                    _logService.LogAlarmAcknowledged(alarm, userName ?? "Unknown");
                }

                UpdateHistoryRecord(alarm);
            }

            // 移除已恢复且已确认的报警
            var keysToRemove = _activeAlarms
                .Where(kvp => !kvp.Value.IsActive && kvp.Value.IsAcknowledged)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                AlarmItem removed;
                if (_activeAlarms.TryRemove(key, out removed))
                {
                    if (!string.IsNullOrEmpty(removed.SourceIdentifier))
                    {
                        string removedId;
                        _sourceToAlarmIdMap.TryRemove(removed.SourceIdentifier, out removedId);
                    }
                }
            }

            RaiseEventOnUIThread(() =>
            {
                var handler = ActiveAlarmsChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            });
        }

        public void ClearRecoveredAlarms()
        {
            var keysToRemove = _activeAlarms
                .Where(kvp => !kvp.Value.IsActive && kvp.Value.IsAcknowledged)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                AlarmItem removed;
                if (_activeAlarms.TryRemove(key, out removed))
                {
                    if (!string.IsNullOrEmpty(removed.SourceIdentifier))
                    {
                        string removedId;
                        _sourceToAlarmIdMap.TryRemove(removed.SourceIdentifier, out removedId);
                    }
                }
            }

            if (keysToRemove.Count > 0)
            {
                if (_logService != null)
                {
                    _logService.Information("已清除 {Count} 条已恢复的报警", keysToRemove.Count);
                }

                RaiseEventOnUIThread(() =>
                {
                    var handler = ActiveAlarmsChanged;
                    if (handler != null)
                    {
                        handler(this, EventArgs.Empty);
                    }
                });
            }
        }

        public void ClearAllAlarms()
        {
            _activeAlarms.Clear();
            _sourceToAlarmIdMap.Clear();

            if (_logService != null)
            {
                _logService.Warning("已强制清除所有活动报警");
            }

            RaiseEventOnUIThread(() =>
            {
                var handler = ActiveAlarmsChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            });
        }

        #endregion

        #region 历史记录

        private void AddToHistory(AlarmItem alarm)
        {
            lock (_historyLock)
            {
                _alarmHistory.Insert(0, alarm);
                while (_alarmHistory.Count > MaxHistoryCount)
                {
                    _alarmHistory.RemoveAt(_alarmHistory.Count - 1);
                }
            }
        }

        private void UpdateHistoryRecord(AlarmItem alarm)
        {
            lock (_historyLock)
            {
                var historyRecord = _alarmHistory.FirstOrDefault(h => h.AlarmId == alarm.AlarmId);
                if (historyRecord != null)
                {
                    historyRecord.IsActive = alarm.IsActive;
                    historyRecord.IsAcknowledged = alarm.IsAcknowledged;
                    historyRecord.AcknowledgedTime = alarm.AcknowledgedTime;
                    historyRecord.AcknowledgedBy = alarm.AcknowledgedBy;
                    historyRecord.RecoveredTime = alarm.RecoveredTime;
                }
            }
        }

        public IReadOnlyList<AlarmItem> GetAlarmHistory(int maxCount = 100)
        {
            lock (_historyLock)
            {
                return _alarmHistory.Take(maxCount).ToList().AsReadOnly();
            }
        }

        public IReadOnlyList<AlarmItem> GetAlarmHistory(DateTime startTime, DateTime endTime)
        {
            lock (_historyLock)
            {
                return _alarmHistory
                    .Where(a => a.TriggerTime >= startTime && a.TriggerTime <= endTime)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public void ClearAlarmHistory()
        {
            lock (_historyLock)
            {
                _alarmHistory.Clear();
            }

            if (_logService != null)
            {
                _logService.Information("报警历史已清除");
            }
        }

        #endregion

        #region 测试

        public void TriggerTestAlarm(string name, AlarmLevel level, string description = null)
        {
            TriggerAlarm(
                name: name,
                level: level,
                description: description ?? string.Format("测试报警: {0}", name),
                sourceModule: "测试模块",
                sourceIdentifier: string.Format("_TEST_{0}", Guid.NewGuid().ToString("N").Substring(0, 8)),
                showPopup: true
            );
        }

        #endregion

        #region 私有方法

        private string GenerateAlarmCode(AlarmLevel level)
        {
            lock (_codeLocker)
            {
                string prefix;
                switch (level)
                {
                    case AlarmLevel.Info:
                        prefix = "INF";
                        break;
                    case AlarmLevel.Warning:
                        prefix = "WRN";
                        break;
                    case AlarmLevel.Error:
                        prefix = "ERR";
                        break;
                    case AlarmLevel.Critical:
                        prefix = "CRT";
                        break;
                    default:
                        prefix = "ALM";
                        break;
                }
                return string.Format("{0}{1:D3}", prefix, _alarmCodeCounter++);
            }
        }

        private void RaiseEventOnUIThread(Action action)
        {
            var app = System.Windows.Application.Current;
            if (app != null)
            {
                app.Dispatcher.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _activeAlarms.Clear();
                _sourceToAlarmIdMap.Clear();

                lock (_historyLock)
                {
                    _alarmHistory.Clear();
                }

                _disposed = true;
            }
        }

        #endregion
    }
}