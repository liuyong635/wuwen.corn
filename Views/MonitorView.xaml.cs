using SeedCut.ViewModels;
using System.Windows.Controls;

namespace SeedCut.Views
{
    /// <summary>
    /// MonitorView.xaml 的交互逻辑
    /// </summary>
    public partial class MonitorView : UserControl
    {
        public MonitorView()
        {
            InitializeComponent();
            DataContext = new MonitorViewModel();
        }
    }
}