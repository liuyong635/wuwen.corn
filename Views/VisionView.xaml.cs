using SeedCut.ViewModels;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace SeedCut.Views
{
    /// <summary>
    /// VisionView.xaml 的交互逻辑
    /// </summary>
    public partial class VisionView : UserControl
    {
        private VisionViewModel _viewModel;

        public VisionView(VisionViewModel viewModel)
        {

            Debug.WriteLine("VisionView 构造函数开始");

            InitializeComponent();

            _viewModel = viewModel;
            DataContext = _viewModel;

            Debug.WriteLine($"DataContext 已设置: {DataContext != null}");
            Debug.WriteLine($"ViewModel 类型: {_viewModel?.GetType().Name}");
            Debug.WriteLine($"SelectFileCommand 是否为 null: {_viewModel?.SelectFileCommand == null}");

            // 订阅 Loaded 事件进行额外验证
            this.Loaded += VisionView_Loaded;
        }

        private void VisionView_Loaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("VisionView Loaded 事件触发");
            Debug.WriteLine($"DataContext 类型: {this.DataContext?.GetType().Name}");

            if (_viewModel != null)
            {
                Debug.WriteLine($"SelectFileCommand: {_viewModel.SelectFileCommand != null}");
                Debug.WriteLine($"LoadCommand: {_viewModel.LoadCommand != null}");
            }
        }

        /// <summary>
        /// 密码框变化时同步到 ViewModel
        /// </summary>
        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_viewModel != null && sender is PasswordBox passwordBox)
            {
                _viewModel.SolutionPassword = passwordBox.Password;
                Debug.WriteLine($"密码已更新，长度: {passwordBox.Password.Length}");
            }
        }
    }
}