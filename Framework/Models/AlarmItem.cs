using System;
using System.ComponentModel;

namespace SeedCut.Framework.Models
{
    /// <summary>
    /// 报警实例 - 表示一个具体的报警事件
    /// </summary>
    public class AlarmItem : INotifyPropertyChanged
    {
        private bool _isActive = true;
        private bool _isAcknowledged = false;
        private DateTime? _acknowledgedTime;
        private DateTime? _recoveredTime;
        private string _acknowledgedBy;

        #region 基本属性

        /// <summary>
        /// 报警ID（唯一标识）
        /// </summary>
        public string AlarmId { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();

        /// <summary>
        /// 报警代码（如ERR001）
        /// </summary>
        public string AlarmCode { get; set; }

        /// <summary>
        /// 报警名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 报警描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 报警级别
        /// </summary>
        public AlarmLevel Level { get; set; } = AlarmLevel.Error;

        /// <summary>
        /// 来源模块（如"PLC"、"机器人"、"视觉"、"数据库"等）
        /// </summary>
        public string SourceModule { get; set; }

        /// <summary>
        /// 来源标识（可选）
        /// - PLC报警：地址名称
        /// - 软件报警：异常类型或模块名
        /// - 设备报警：设备ID
        /// </summary>
        public string SourceIdentifier { get; set; }

        /// <summary>
        /// 触发时间
        /// </summary>
        public DateTime TriggerTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 是否显示弹窗
        /// </summary>
        public bool ShowPopup { get; set; } = true;

        #endregion

        #region 兼容性属性（保留旧名称）

        

        #endregion

        #region 状态属性

        /// <summary>
        /// 是否活动（未恢复）
        /// </summary>
        public bool IsActive
        {
            get { return _isActive; }
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged(nameof(IsActive));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(CanBeCleared));
                }
            }
        }

        /// <summary>
        /// 是否已确认
        /// </summary>
        public bool IsAcknowledged
        {
            get { return _isAcknowledged; }
            set
            {
                if (_isAcknowledged != value)
                {
                    _isAcknowledged = value;
                    OnPropertyChanged(nameof(IsAcknowledged));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(CanBeCleared));
                }
            }
        }

        /// <summary>
        /// 确认时间
        /// </summary>
        public DateTime? AcknowledgedTime
        {
            get { return _acknowledgedTime; }
            set
            {
                _acknowledgedTime = value;
                OnPropertyChanged(nameof(AcknowledgedTime));
            }
        }

        /// <summary>
        /// 确认人
        /// </summary>
        public string AcknowledgedBy
        {
            get { return _acknowledgedBy; }
            set
            {
                _acknowledgedBy = value;
                OnPropertyChanged(nameof(AcknowledgedBy));
            }
        }

        /// <summary>
        /// 恢复时间
        /// </summary>
        public DateTime? RecoveredTime
        {
            get { return _recoveredTime; }
            set
            {
                _recoveredTime = value;
                OnPropertyChanged(nameof(RecoveredTime));
                OnPropertyChanged(nameof(Duration));
                OnPropertyChanged(nameof(DurationText));
            }
        }

        #endregion

        #region 计算属性

        /// <summary>
        /// 持续时间
        /// </summary>
        public TimeSpan Duration
        {
            get
            {
                var endTime = RecoveredTime ?? DateTime.Now;
                return endTime - TriggerTime;
            }
        }

        /// <summary>
        /// 持续时间文本
        /// </summary>
        public string DurationText
        {
            get
            {
                var duration = Duration;
                if (duration.TotalDays >= 1)
                    return string.Format("{0}天{1}时{2}分", (int)duration.TotalDays, duration.Hours, duration.Minutes);
                if (duration.TotalHours >= 1)
                    return string.Format("{0}时{1}分{2}秒", (int)duration.TotalHours, duration.Minutes, duration.Seconds);
                if (duration.TotalMinutes >= 1)
                    return string.Format("{0}分{1}秒", (int)duration.TotalMinutes, duration.Seconds);
                return string.Format("{0}秒", (int)duration.TotalSeconds);
            }
        }

        /// <summary>
        /// 状态文本
        /// </summary>
        public string StatusText
        {
            get
            {
                if (IsActive && !IsAcknowledged)
                    return "活动";
                if (IsActive && IsAcknowledged)
                    return "已确认";
                if (!IsActive && !IsAcknowledged)
                    return "已恢复";
                return "已处理";
            }
        }

        /// <summary>
        /// 是否可以被清除
        /// </summary>
        public bool CanBeCleared
        {
            get { return !IsActive && IsAcknowledged; }
        }

        /// <summary>
        /// 级别显示名称
        /// </summary>
        public string LevelDisplayName
        {
            get { return Level.GetDisplayName(); }
        }

        /// <summary>
        /// 级别颜色
        /// </summary>
        public string LevelColor
        {
            get { return Level.GetColorCode(); }
        }

        /// <summary>
        /// 级别背景色
        /// </summary>
        public string LevelBackgroundColor
        {
            get { return Level.GetBackgroundColorCode(); }
        }

        /// <summary>
        /// 级别图标
        /// </summary>
        public string LevelIcon
        {
            get { return Level.GetIcon(); }
        }

        #endregion

        #region 方法

        /// <summary>
        /// 确认报警
        /// </summary>
        public void Acknowledge(string userName = null)
        {
            IsAcknowledged = true;
            AcknowledgedTime = DateTime.Now;
            AcknowledgedBy = userName ?? "Unknown";
        }

        /// <summary>
        /// 标记为已恢复
        /// </summary>
        public void MarkAsRecovered()
        {
            IsActive = false;
            RecoveredTime = DateTime.Now;
        }

        /// <summary>
        /// 克隆报警实例
        /// </summary>
        public AlarmItem Clone()
        {
            return new AlarmItem
            {
                AlarmId = this.AlarmId,
                AlarmCode = this.AlarmCode,
                Name = this.Name,
                Description = this.Description,
                Level = this.Level,
                SourceModule = this.SourceModule,
                SourceIdentifier = this.SourceIdentifier,
                TriggerTime = this.TriggerTime,
                ShowPopup = this.ShowPopup,
                IsActive = this.IsActive,
                IsAcknowledged = this.IsAcknowledged,
                AcknowledgedTime = this.AcknowledgedTime,
                AcknowledgedBy = this.AcknowledgedBy,
                RecoveredTime = this.RecoveredTime
            };
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        #endregion

        public override string ToString()
        {
            return string.Format("[{0}] {1} ({2})", AlarmCode, Name, StatusText);
        }
    }
}