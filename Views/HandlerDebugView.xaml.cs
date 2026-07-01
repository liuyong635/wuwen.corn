using Microsoft.Extensions.DependencyInjection;
using SeedCut.ViewModels;
using System;
using System.Windows.Controls;

namespace SeedCut.Views
{
    /// <summary>
    /// HandlerDebugView.xaml 的交互逻辑
    /// 
    /// 序列调试视图，包含：
    /// - 左侧：条件选择器
    /// - 右侧：复合操作/一键执行（序列调试）
    /// </summary>
    public partial class HandlerDebugView : UserControl, IDisposable
    {
        private HandlerDebugViewModel _viewModel;
        private bool _disposed;

        /// <summary>
        /// 带服务提供者的构造函数（运行时使用）
        /// </summary>
        public HandlerDebugView(IServiceProvider serviceProvider)
        {
            InitializeComponent();

            if (serviceProvider != null)
            {
                _viewModel = serviceProvider.GetService<HandlerDebugViewModel>();
                DataContext = _viewModel;
            }
        }

        /// <summary>
        /// 无参构造函数（设计器使用）
        /// 注意：设计器模式下不会有真实的 ViewModel
        /// </summary>
        public HandlerDebugView()
        {
            InitializeComponent();

            // 设计时可以设置一些模拟数据
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                // 设计模式，不创建 ViewModel
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _viewModel?.Dispose();
            _viewModel = null;
        }
    }
}