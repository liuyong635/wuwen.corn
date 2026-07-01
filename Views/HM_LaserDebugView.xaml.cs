using Microsoft.Extensions.DependencyInjection;
using SeedCut.ViewModels;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SeedCut.Views
{
    /// <summary>
    /// HM_LaserDebugView.xaml 的交互逻辑
    /// </summary>
    public partial class HM_LaserDebugView : UserControl
    {
        /// <summary>
        /// 通过DI容器创建（推荐方式）
        /// </summary>
        public HM_LaserDebugView(IServiceProvider serviceProvider)
        {
            InitializeComponent();

            var viewModel = serviceProvider.GetRequiredService<HM_LaserDebugViewModel>();
            this.DataContext = viewModel;

            // 订阅配置应用事件（可选：用于通知其他组件）
            viewModel.ConfigApplied += OnConfigApplied;
        }

        /// <summary>
        /// 设计器支持（无参构造函数）
        /// </summary>
        public HM_LaserDebugView()
        {
            InitializeComponent();

            
        }

        /// <summary>
        /// 配置应用事件处理
        /// </summary>
        private void OnConfigApplied(object sender, EventArgs e)
        {
            // 这里可以添加配置应用后的额外处理
            // 例如：通知其他视图刷新、记录日志等
            System.Diagnostics.Debug.WriteLine("[HM_LaserDebugView] 配置已应用");
        }

        
    }

    

    
}
