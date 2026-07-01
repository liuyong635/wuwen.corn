using SeedCut.ViewModels;
using System.Windows.Controls;

namespace SeedCut.Views
{
    /// <summary>
    /// VibratorView.xaml 的交互逻辑
    /// </summary>
    public partial class VibratorView : UserControl
    {
        /// <summary>
        /// 带ViewModel的构造函数（用于依赖注入）
        /// </summary>
        public VibratorView(VibratorViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        /// <summary>
        /// 默认构造函数（用于设计时）
        /// </summary>
        public VibratorView()
        {
            InitializeComponent();
        }
    }
}