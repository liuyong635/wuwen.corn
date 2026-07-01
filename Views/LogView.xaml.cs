using SeedCut.ViewModels;
using System.Windows.Controls;

namespace SeedCut.Views
{
    /// <summary>
    /// LogView.xaml 的交互逻辑
    /// </summary>
    public partial class LogView : UserControl
    {
        public LogView()
        {
            InitializeComponent();
            DataContext = new LogViewModel();
        }
    }
}