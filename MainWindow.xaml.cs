using SeedCut.ViewModels;
using SeedCut.Services;
using SeedCut.Views;
using System;
using System.Windows;
using System.Windows.Input;
using SeedCut.Framework.Services.Interfaces;

namespace SeedCut
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly IAlarmService _alarmService;

        public MainWindow(MainViewModel viewModel, IAlarmService alarmService)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _alarmService = alarmService ?? throw new ArgumentNullException(nameof(alarmService));
            DataContext = _viewModel;

            // ✅ 订阅报警触发事件
            _alarmService.AlarmTriggered += OnAlarmTriggered;

            System.Diagnostics.Debug.WriteLine("✅ MainWindow 已订阅报警事件");
        }

        // ✅ 报警触发时显示弹窗
        private void OnAlarmTriggered(object sender, AlarmTriggeredEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"🚨 MainWindow收到报警: {e.Alarm.Name} (ShowPopup={e.ShowPopup})");

            if (e.ShowPopup)
            {
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine("正在显示报警弹窗...");

                        AlarmPopupWindow.ShowAlarm(
                            e.Alarm,
                            onViewAll: () => _viewModel.ShowAlarmHistory(),
                            onAcknowledge: (alarmId) => _alarmService.AcknowledgeAlarm(alarmId)
                        );

                        System.Diagnostics.Debug.WriteLine("✅ 报警弹窗已显示");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 显示报警弹窗失败: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"   堆栈: {ex.StackTrace}");
                    }
                });
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("⚠️ 该报警设置为不显示弹窗");
            }
        }

        // 点击预警栏，切换到报警历史页面
        private void AlarmBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.ShowAlarmHistory();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // ✅ 清理：取消订阅
            _alarmService.AlarmTriggered -= OnAlarmTriggered;
            base.OnClosed(e);
        }
        #region 设备状态 Popup 控制

        private bool _isMouseOverTrigger = false;
        private bool _isMouseOverPopup = false;

        private void DeviceStatusTrigger_MouseEnter(object sender, MouseEventArgs e)
        {
            _isMouseOverTrigger = true;
            DeviceStatusPopup.IsOpen = true;
        }

        private void DeviceStatusTrigger_MouseLeave(object sender, MouseEventArgs e)
        {
            _isMouseOverTrigger = false;
            ClosePopupIfNeeded();
        }

        private void DeviceStatusPopup_MouseEnter(object sender, MouseEventArgs e)
        {
            _isMouseOverPopup = true;
        }

        private void DeviceStatusPopup_MouseLeave(object sender, MouseEventArgs e)
        {
            _isMouseOverPopup = false;
            ClosePopupIfNeeded();
        }

        private void ClosePopupIfNeeded()
        {
            // 延迟检查，避免鼠标从 Trigger 移动到 Popup 时闪烁
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isMouseOverTrigger && !_isMouseOverPopup)
                {
                    DeviceStatusPopup.IsOpen = false;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        #endregion
    }
}