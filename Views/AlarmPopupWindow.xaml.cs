using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SeedCut.Framework.Models;
using SeedCut.Models;

namespace SeedCut.Views
{
    /// <summary>
    /// 报警弹窗
    /// </summary>
    public partial class AlarmPopupWindow : Window
    {
        private AlarmItem _alarm;

        /// <summary>
        /// 查看全部报警请求事件
        /// </summary>
        public event EventHandler ViewAllRequested;

        /// <summary>
        /// 确认报警事件
        /// </summary>
        public event EventHandler<string> AlarmAcknowledged;

        public AlarmPopupWindow()
        {
            InitializeComponent();
        }

        public AlarmPopupWindow(AlarmItem alarm) : this()
        {
            SetAlarm(alarm);
        }

        /// <summary>
        /// 设置报警信息
        /// </summary>
        public void SetAlarm(AlarmItem alarm)
        {
            _alarm = alarm;

            // 设置颜色
            var bgColor = (Color)ColorConverter.ConvertFromString(alarm.LevelBackgroundColor);
            var fgColor = (Color)ColorConverter.ConvertFromString(alarm.LevelColor);

            MainBorder.Background = new SolidColorBrush(bgColor);
            MainBorder.BorderBrush = new SolidColorBrush(fgColor);

            // 标题栏
            var titleBar = (System.Windows.Controls.Border)((System.Windows.Controls.Grid)MainBorder.Child).Children[0];
            titleBar.Background = new SolidColorBrush(fgColor);

            // 确认按钮
            AcknowledgeButton.Background = new SolidColorBrush(fgColor);

            // 设置内容
            IconText.Text = alarm.LevelIcon;
            TitleText.Text = alarm.LevelDisplayName + "报警";
            TimeText.Text = alarm.TriggerTime.ToString("HH:mm:ss");
            AlarmCodeText.Text = alarm.AlarmCode;
            AlarmCodeText.Foreground = new SolidColorBrush(fgColor);
            AlarmNameText.Text = alarm.Name;
            SourceModuleText.Text = alarm.SourceModule ?? "未知";
            DescriptionText.Text = alarm.Description ?? alarm.Name;

            // 建议处理
            // 如果没有建议，可以设置默认建议
            var suggestion = GetSuggestedAction(alarm);
            SuggestedActionText.Text = suggestion;
            SuggestedActionText.Visibility = string.IsNullOrEmpty(suggestion)
                ? Visibility.Collapsed
                : Visibility.Visible;

            // 如果已确认，禁用确认按钮
            if (alarm.IsAcknowledged)
            {
                AcknowledgeButton.Content = "已确认";
                AcknowledgeButton.IsEnabled = false;
            }
        }

        private string GetSuggestedAction(AlarmItem alarm)
        {
            // 如果有自定义建议，返回自定义建议
            // 否则根据报警类型返回默认建议
            if (!string.IsNullOrEmpty(alarm.Description) && alarm.Description != alarm.Name)
            {
                // 检查是否有建议处理方式的PLCAddressItem
                // 这里简化处理，直接返回描述
            }

            // 默认建议
            var name = alarm.Name.ToLower();
            if (name.Contains("急停"))
                return "请检查急停按钮，确认安全后释放急停并复位系统";
            if (name.Contains("极限"))
                return "请检查机械位置，手动退出极限区域";
            if (name.Contains("伺服") || name.Contains("驱动"))
                return "请检查伺服驱动器状态和错误代码";
            if (name.Contains("气压"))
                return "请检查气源供应是否正常";
            if (name.Contains("通讯") || name.Contains("通信"))
                return "请检查网络连接和设备状态";
            if (name.Contains("温度"))
                return "请检查散热系统是否正常工作";

            return null;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ViewAllButton_Click(object sender, RoutedEventArgs e)
        {
            ViewAllRequested?.Invoke(this, EventArgs.Empty);
            Close();
        }

        private void AcknowledgeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_alarm != null && !_alarm.IsAcknowledged)
            {
                AlarmAcknowledged?.Invoke(this, _alarm.AlarmId);
                AcknowledgeButton.Content = "已确认";
                AcknowledgeButton.IsEnabled = false;
            }
        }

        #region 静态方法

        /// <summary>
        /// 显示报警弹窗（非模态）
        /// </summary>
        public static AlarmPopupWindow ShowAlarm(AlarmItem alarm, Action onViewAll = null, Action<string> onAcknowledge = null)
        {
            var window = new AlarmPopupWindow(alarm);

            if (onViewAll != null)
            {
                window.ViewAllRequested += (s, e) => onViewAll();
            }

            if (onAcknowledge != null)
            {
                window.AlarmAcknowledged += (s, alarmId) => onAcknowledge(alarmId);
            }

            window.Show();
            return window;
        }

        /// <summary>
        /// 显示报警弹窗（模态）
        /// </summary>
        public static bool? ShowAlarmModal(AlarmItem alarm, Action onViewAll = null, Action<string> onAcknowledge = null)
        {
            var window = new AlarmPopupWindow(alarm);

            if (onViewAll != null)
            {
                window.ViewAllRequested += (s, e) => onViewAll();
            }

            if (onAcknowledge != null)
            {
                window.AlarmAcknowledged += (s, alarmId) => onAcknowledge(alarmId);
            }

            return window.ShowDialog();
        }

        #endregion
    }
}