using Microsoft.Extensions.DependencyInjection;
using SeedCut.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace SeedCut.Views
{
    /// <summary>
    /// AdminView.xaml 的交互逻辑
    /// </summary>
    public partial class AdminView : UserControl
    {
        private readonly AdminViewModel _viewModel;
        private readonly IServiceProvider _serviceProvider;

        public AdminView(IServiceProvider serviceProvider)
        {
            InitializeComponent();
            _serviceProvider = serviceProvider;
            _viewModel = new AdminViewModel(serviceProvider);
            this.DataContext = _viewModel;

            // 默认显示Vision页面
            _viewModel.NavigateToModule("Vision");
        }

        private void VisionButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.NavigateToModule("Vision");
        }

        private void CameraButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.NavigateToModule("Camera");
        }

        private void RobotButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.NavigateToModule("Robot");
        }

        private void VibratorButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.NavigateToModule("Vibrator");
        }

        private void PLCButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.NavigateToModule("PLC");
        }

        private void LaserButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.NavigateToModule("Laser");
        }

        private void HM_LaserButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.NavigateToModule("HM_Laser");
        }
        private void HandlerDebugButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.NavigateToModule("HandlerDebug");
        }

        private void RecipeButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.NavigateToModule("Recipe");
        }
    }
}