using Microsoft.Extensions.Logging;
using SeedCut.Models;
using SeedCut.Services;
using SeedCut.Views;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SeedCut.ViewModels
{
    public class ModuleCheckItemViewModel : INotifyPropertyChanged
    {
        private string _status = "not_checked";
        private string _message = string.Empty;

        public string ModuleName { get; set; }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusColor));
                    OnPropertyChanged(nameof(StatusColorBrush));
                    OnPropertyChanged(nameof(StatusIcon));
                }
            }
        }

        public string Message
        {
            get => _message;
            set
            {
                if (_message != value)
                {
                    _message = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MessageColor));
                    OnPropertyChanged(nameof(MessageColorBrush));
                }
            }
        }

        public bool IsCritical { get; set; }

        // 颜色字符串
        public string StatusColor
        {
            get
            {
                switch (Status)
                {
                    case "success": return "#4CAF50";
                    case "failed": return "#F44336";
                    case "warning": return "#FF9800";
                    case "checking": return "#2196F3";
                    default: return "#BDBDBD";
                }
            }
        }

        public string MessageColor
        {
            get
            {
                switch (Status)
                {
                    case "success": return "#4CAF50";
                    case "failed": return "#F44336";
                    case "warning": return "#FF9800";
                    default: return "#9E9E9E";
                }
            }
        }

        // Brush 属性用于 XAML 绑定
        public Brush StatusColorBrush
        {
            get
            {
                try
                {
                    return (Brush)new BrushConverter().ConvertFromString(StatusColor);
                }
                catch
                {
                    return Brushes.Gray;
                }
            }
        }

        public Brush MessageColorBrush
        {
            get
            {
                try
                {
                    return (Brush)new BrushConverter().ConvertFromString(MessageColor);
                }
                catch
                {
                    return Brushes.Gray;
                }
            }
        }

        public string StatusIcon
        {
            get
            {
                switch (Status)
                {
                    case "success": return "✓";
                    case "failed": return "✗";
                    case "warning": return "!";
                    case "checking": return "⟳";
                    default: return "○";
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}