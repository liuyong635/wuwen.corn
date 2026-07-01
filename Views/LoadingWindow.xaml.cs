using Microsoft.Extensions.DependencyInjection;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Services.DeviceAdapter;
using SeedCut.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace SeedCut.Views
{
    public partial class LoadingWindow : Window
    {
        private readonly ISystemMonitor _systemMonitor;
        private readonly IServiceProvider _serviceProvider;
        private readonly DeviceInitializationService _deviceInitService;  // 新增
        private bool _checkPassed = false;

        public LoadingWindow(
            ISystemMonitor systemMonitor,
            IServiceProvider serviceProvider,
            DeviceInitializationService deviceInitService)  // 新增参数
        {
            InitializeComponent();
            _systemMonitor = systemMonitor;
            _serviceProvider = serviceProvider;
            _deviceInitService = deviceInitService;  // 新增

            this.DataContext = new LoadingViewModel(systemMonitor);
            _systemMonitor.CheckCompleted += OnCheckCompleted;
            this.Loaded += LoadingWindow_Loaded;
        }

        private async void LoadingWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(500);
            await _systemMonitor.StartCheckAsync();
        }

        private async void OnCheckCompleted(object sender, bool allPassed)
        {
            _checkPassed = allPassed;

            if (allPassed)
            {
                // ========== 检查通过后，生产模式下自动连接设备 ==========
                if (_deviceInitService.IsProductionMode)
                {
                    await ConnectDevicesAsync();
                }

                // ========== 延迟后跳转到登录窗口 ==========
                await Task.Delay(500);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var loginWindow = _serviceProvider.GetRequiredService<LoginWindow>();
                        loginWindow.Show();
                        this.Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"无法打开登录窗口: {ex.Message}", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        Application.Current.Shutdown();
                    }
                });
            }
        }

        /// <summary>
        /// 连接所有设备（生产模式）
        /// </summary>
        private async Task ConnectDevicesAsync()
        {
            try
            {
                // 更新UI提示（可选：通过ViewModel更新）
                if (DataContext is LoadingViewModel vm)
                {
                    vm.UpdateStatus("正在连接设备...");
                }

                var progress = new Progress<(string deviceName, int progress)>(p =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (DataContext is LoadingViewModel viewModel)
                        {
                            viewModel.UpdateStatus($"正在连接: {p.deviceName}");
                        }
                    });
                });

                var results = await _deviceInitService.ConnectAllDevicesAsync(progress);

                // 检查关键设备是否连接成功
                var failedCritical = results
                    .Where(r => !r.Success && !r.WasSkipped && IsCriticalDevice(r.DeviceId))
                    .ToList();

                if (failedCritical.Any())
                {
                    var failedNames = string.Join(", ", failedCritical.Select(f => f.DisplayName));
                    System.Diagnostics.Debug.WriteLine($"⚠️ 关键设备连接失败: {failedNames}");
                    // 可以选择是否阻止进入系统
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 设备连接异常: {ex.Message}");
            }
        }

        private bool IsCriticalDevice(string deviceId)
        {
            return _deviceInitService.Config.Devices
                .FirstOrDefault(d => d.DeviceId == deviceId)?.IsCritical ?? false;
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_checkPassed)
            {
                var result = MessageBox.Show(
                    "系统检查未通过，确定要退出程序吗？",
                    "确认退出",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                }
            }
            base.OnClosing(e);
        }
    }
}