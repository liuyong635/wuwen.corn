using SeedCut.Services;
using SeedCut.Services.Camera;
using SeedCut.Services.Connection;
using SeedCut.ViewModels;
using System;
using System.Windows.Controls;

namespace SeedCut.Views
{
    /// <summary>
    /// CameraView.xaml 的交互逻辑（改造版）
    /// </summary>
    public partial class CameraView : UserControl
    {
        private CameraViewModel _viewModel;

        /// <summary>
        /// 构造函数 - 注入所需的依赖服务
        /// </summary>
        /// <param name="cameraService">原有的相机服务（用于枚举连接）</param>
        /// <param name="cameraFactory">相机服务工厂（用于创建配置相机实例和适配器）</param>
        /// <param name="connectionMonitor">连接监控服务（用于管理配置相机的连接状态）</param>
        /// <param name="cameraConfig">相机配置（用于获取配置相机列表）</param>
        public CameraView(
            ICameraService cameraService,
            HikCameraServiceFactory cameraFactory,
            ConnectionMonitorService connectionMonitor,
            HikCameraConfig cameraConfig)
        {
            InitializeComponent();

            // 创建ViewModel并注入所有依赖
            _viewModel = new CameraViewModel(
                cameraService,
                cameraFactory,
                connectionMonitor,
                cameraConfig
            );

            // 设置DataContext
            this.DataContext = _viewModel;

            // 当控件卸载时清理资源
            this.Unloaded += OnUnloaded;
        }

        /// <summary>
        /// 控件卸载时的清理
        /// </summary>
        private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            _viewModel?.Dispose();
            this.Unloaded -= OnUnloaded;
        }
    }
}