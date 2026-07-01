using Microsoft.Extensions.DependencyInjection;
using SeedCut.Models;
using SeedCut.ViewModels;
using System;
using System.Windows;
using System.Windows.Input;

namespace SeedCut.Views
{
    /// <summary>
    /// 登录窗口
    /// </summary>
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;
        private readonly IServiceProvider _serviceProvider;

        public LoginWindow(IServiceProvider serviceProvider)
        {
            InitializeComponent();
            _serviceProvider = serviceProvider;
            _viewModel = new LoginViewModel();
            _viewModel.LoginSuccess += OnLoginSuccess;
            DataContext = _viewModel;
        }

        private void OnLoginSuccess(object sender, UserType userType)
        {
            // 登录成功后直接跳转到主窗口
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
            this.Close();
        }

        #region 窗口控制按钮事件

        /// <summary>
        /// 标题栏鼠标按下事件 - 允许拖动窗口
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // 双击标题栏最大化/还原
                MaximizeRestore();
            }
            else
            {
                // 单击拖动窗口
                this.DragMove();
            }
        }

        /// <summary>
        /// 最小化按钮点击事件
        /// </summary>
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// 最大化/还原按钮点击事件
        /// </summary>
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            MaximizeRestore();
        }

        /// <summary>
        /// 关闭按钮点击事件
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        /// <summary>
        /// 切换最大化/还原状态
        /// </summary>
        private void MaximizeRestore()
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                MaximizeButton.Content = "□";
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                MaximizeButton.Content = "❐";
            }
        }

        #endregion
    }
}