using Microsoft.Extensions.DependencyInjection;
using SeedCut.ViewModels;
using System;
using System.Windows.Controls;

namespace SeedCut.Views
{
    /// <summary>
    /// LaserDebugView.xaml 的交互逻辑
    /// </summary>
    public partial class LaserDebugView : UserControl
    {
        public LaserDebugView(IServiceProvider serviceProvider)
        {
            InitializeComponent();

            var viewModel = serviceProvider.GetRequiredService<LaserDebugViewModel>();
            this.DataContext = viewModel;
        }
    }
}