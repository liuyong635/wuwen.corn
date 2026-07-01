using SeedCut.ViewModels;
using System;
using System.Windows.Controls;
using System.Windows.Input;

namespace SeedCut.Views
{
    /// <summary>
    /// RobotView.xaml 的交互逻辑
    /// </summary>
    public partial class RobotView : UserControl
    {
        /// <summary>
        /// 带ViewModel的构造函数（推荐使用）
        /// </summary>
        public RobotView(RobotViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }

        /// <summary>
        /// 默认构造函数（设计时使用）
        /// </summary>
        public RobotView()
        {
            InitializeComponent();

            // 设计时模式，不初始化
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                return;
            }
        }

        #region JOG 按钮事件处理

        // X 轴
        private void JogXPlus_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.JogXPlusCommand?.Execute(null);
        }

        private void JogXPlus_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.StopJogCommand?.Execute(null);
        }

        private void JogXMinus_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.JogXMinusCommand?.Execute(null);
        }

        private void JogXMinus_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.StopJogCommand?.Execute(null);
        }

        // Y 轴
        private void JogYPlus_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.JogYPlusCommand?.Execute(null);
        }

        private void JogYPlus_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.StopJogCommand?.Execute(null);
        }

        private void JogYMinus_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.JogYMinusCommand?.Execute(null);
        }

        private void JogYMinus_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.StopJogCommand?.Execute(null);
        }

        // Z 轴
        private void JogZPlus_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.JogZPlusCommand?.Execute(null);
        }

        private void JogZPlus_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.StopJogCommand?.Execute(null);
        }

        private void JogZMinus_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.JogZMinusCommand?.Execute(null);
        }

        private void JogZMinus_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.StopJogCommand?.Execute(null);
        }

        // C 轴
        private void JogCPlus_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.JogCPlusCommand?.Execute(null);
        }

        private void JogCPlus_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.StopJogCommand?.Execute(null);
        }

        private void JogCMinus_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.JogCMinusCommand?.Execute(null);
        }

        private void JogCMinus_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is RobotViewModel vm)
                vm.StopJogCommand?.Execute(null);
        }

        #endregion
    }
}