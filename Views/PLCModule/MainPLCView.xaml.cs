using System;
using System.Windows.Controls;
using SeedCut.ViewModels.PLCModule;

namespace SeedCut.Views.PLCModule
{
    /// <summary>
    /// MainPLCView.xaml 的交互逻辑
    /// </summary>
    public partial class MainPLCView : UserControl
    {
        public MainPLCView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 带ViewModel的构造函数
        /// </summary>
        public MainPLCView(MainPLCViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        }
    }
}