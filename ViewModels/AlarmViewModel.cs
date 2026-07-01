using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using SeedCut.Framework.Models;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Models;
using SeedCut.Services;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// 报警视图模型
    /// </summary>
    public class AlarmViewModel : INotifyPropertyChanged
    {
        private readonly IAlarmService _alarmService;
        private readonly ILogService _logService;

        private string _latestAlarmText = "系统正常运行";
        private string _latestAlarmIcon = "✓";
        private Brush _alarmBackgroundBrush;
        private Brush _alarmForegroundBrush;
        private Brush _alarmIconBrush;
        private string _currentUserName = "Unknown";

        #region 属性

        /// <summary>
        /// 最新报警文本
        /// </summary>
        public string LatestAlarmText
        {
            get => _latestAlarmText;
            set { _latestAlarmText = value; OnPropertyChanged(nameof(LatestAlarmText)); }
        }

        /// <summary>
        /// 最新报警图标
        /// </summary>
        public string LatestAlarmIcon
        {
            get => _latestAlarmIcon;
            set { _latestAlarmIcon = value; OnPropertyChanged(nameof(LatestAlarmIcon)); }
        }

        /// <summary>
        /// 报警栏背景色
        /// </summary>
        public Brush AlarmBackgroundBrush
        {
            get => _alarmBackgroundBrush;
            set { _alarmBackgroundBrush = value; OnPropertyChanged(nameof(AlarmBackgroundBrush)); }
        }

        /// <summary>
        /// 报警栏前景色
        /// </summary>
        public Brush AlarmForegroundBrush
        {
            get => _alarmForegroundBrush;
            set { _alarmForegroundBrush = value; OnPropertyChanged(nameof(AlarmForegroundBrush)); }
        }

        /// <summary>
        /// 报警图标颜色
        /// </summary>
        public Brush AlarmIconBrush
        {
            get => _alarmIconBrush;
            set { _alarmIconBrush = value; OnPropertyChanged(nameof(AlarmIconBrush)); }
        }

        /// <summary>
        /// 是否有活动报警
        /// </summary>
        public bool HasActiveAlarms => _alarmService.HasActiveAlarms;

        /// <summary>
        /// 活动报警数量
        /// </summary>
        public int ActiveAlarmCount => _alarmService.ActiveAlarmCount;

        /// <summary>
        /// 未确认报警数量
        /// </summary>
        public int UnacknowledgedCount => _alarmService.UnacknowledgedAlarmCount;

        /// <summary>
        /// 报警摘要
        /// </summary>
        public string AlarmSummary
        {
            get
            {
                if (!HasActiveAlarms)
                    return "无活动报警";
                return $"活动: {ActiveAlarmCount} | 未确认: {UnacknowledgedCount}";
            }
        }

        /// <summary>
        /// 当前用户名
        /// </summary>
        public string CurrentUserName
        {
            get => _currentUserName;
            set { _currentUserName = value; OnPropertyChanged(nameof(CurrentUserName)); }
        }

        /// <summary>
        /// 活动报警列表
        /// </summary>
        public ObservableCollection<AlarmItem> ActiveAlarms { get; } = new ObservableCollection<AlarmItem>();

        /// <summary>
        /// 报警历史列表
        /// </summary>
        public ObservableCollection<AlarmItem> AlarmHistory { get; } = new ObservableCollection<AlarmItem>();

        #endregion

        #region 命令

        public ICommand AcknowledgeCommand { get; }
        public ICommand AcknowledgeAllCommand { get; }
        public ICommand ClearRecoveredCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand RefreshHistoryCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand TriggerTestAlarmCommand { get; }

        #endregion

        #region 构造函数

        public AlarmViewModel(IAlarmService alarmService, ILogService logService)
        {
            _alarmService = alarmService ?? throw new ArgumentNullException(nameof(alarmService));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));

            // 初始化颜色
            SetNormalColors();

            // 订阅事件
            _alarmService.AlarmTriggered += OnAlarmTriggered;
            _alarmService.AlarmRecovered += OnAlarmRecovered;
            _alarmService.ActiveAlarmsChanged += OnActiveAlarmsChanged;

            // 初始化命令
            AcknowledgeCommand = new RelayCommand<string>(ExecuteAcknowledge);
            AcknowledgeAllCommand = new RelayCommand(ExecuteAcknowledgeAll);
            ClearRecoveredCommand = new RelayCommand(ExecuteClearRecovered);
            ClearAllCommand = new RelayCommand(ExecuteClearAll);
            RefreshHistoryCommand = new RelayCommand(ExecuteRefreshHistory);
            ClearHistoryCommand = new RelayCommand(ExecuteClearHistory);
            TriggerTestAlarmCommand = new RelayCommand(ExecuteTriggerTestAlarm);

            // 初始加载
            RefreshActiveAlarms();
            RefreshAlarmHistory();
        }

        #endregion

        #region 事件处理

        private void OnAlarmTriggered(object sender, AlarmTriggeredEventArgs e)
        {
            RefreshActiveAlarms();
            RefreshAlarmHistory();  // ✅ 修复：刷新历史记录
            UpdateLatestAlarm();
        }

        private void OnAlarmRecovered(object sender, AlarmRecoveredEventArgs e)
        {
            RefreshActiveAlarms();
            RefreshAlarmHistory();  // ✅ 修复：刷新历史记录
            UpdateLatestAlarm();
        }

        private void OnActiveAlarmsChanged(object sender, EventArgs e)
        {
            RefreshActiveAlarms();
            UpdateLatestAlarm();
            OnPropertyChanged(nameof(HasActiveAlarms));
            OnPropertyChanged(nameof(ActiveAlarmCount));
            OnPropertyChanged(nameof(UnacknowledgedCount));
            OnPropertyChanged(nameof(AlarmSummary));
        }

        #endregion

        #region 私有方法

        private void RefreshActiveAlarms()
        {
            ActiveAlarms.Clear();
            foreach (var alarm in _alarmService.GetActiveAlarms())
            {
                ActiveAlarms.Add(alarm);
            }
        }

        private void RefreshAlarmHistory()
        {
            AlarmHistory.Clear();
            foreach (var alarm in _alarmService.GetAlarmHistory(100))
            {
                AlarmHistory.Add(alarm);
            }
        }

        private void UpdateLatestAlarm()
        {
            var latest = _alarmService.LatestAlarm;
            if (latest != null && latest.IsActive)
            {
                LatestAlarmText = $"[{latest.AlarmCode}] {latest.Name}";
                LatestAlarmIcon = latest.LevelIcon;
                SetAlarmColors(latest.Level);
            }
            else if (_alarmService.HasActiveAlarms)
            {
                var activeAlarm = _alarmService.GetActiveAlarms().FirstOrDefault();
                if (activeAlarm != null)
                {
                    LatestAlarmText = $"[{activeAlarm.AlarmCode}] {activeAlarm.Name}";
                    LatestAlarmIcon = activeAlarm.LevelIcon;
                    SetAlarmColors(activeAlarm.Level);
                }
            }
            else
            {
                LatestAlarmText = "系统正常运行";
                LatestAlarmIcon = "✓";
                SetNormalColors();
            }
        }

        private void SetNormalColors()
        {
            AlarmBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F5E9"));
            AlarmForegroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
            AlarmIconBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
        }

        private void SetAlarmColors(AlarmLevel level)
        {
            var bgColor = level.GetBackgroundColorCode();
            var fgColor = level.GetColorCode();

            AlarmBackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor));
            AlarmForegroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgColor));
            AlarmIconBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgColor));
        }

        #endregion

        #region 命令执行

        private void ExecuteAcknowledge(string alarmId)
        {
            if (!string.IsNullOrEmpty(alarmId))
            {
                _alarmService.AcknowledgeAlarm(alarmId, CurrentUserName);
            }
        }

        private void ExecuteAcknowledgeAll()
        {
            _alarmService.AcknowledgeAllAlarms(CurrentUserName);
        }

        private void ExecuteClearRecovered()
        {
            _alarmService.ClearRecoveredAlarms();
        }

        private void ExecuteClearAll()
        {
            _alarmService.ClearAllAlarms();
        }

        private void ExecuteRefreshHistory()
        {
            RefreshAlarmHistory();
        }

        private void ExecuteClearHistory()
        {
            _alarmService.ClearAlarmHistory();
            AlarmHistory.Clear();
        }

        private void ExecuteTriggerTestAlarm()
        {
            _alarmService.TriggerTestAlarm("测试报警", AlarmLevel.Error, "这是一个测试报警");
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}