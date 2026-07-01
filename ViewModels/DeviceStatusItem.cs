using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// 设备状态项 - 用于 MainWindow 设备状态 Popup 显示
    /// </summary>
    public class DeviceStatusItem : INotifyPropertyChanged
    {
        private string _deviceId;
        private string _deviceName;
        private bool _isConnected;
        private bool _isReconnecting;
        private string _statusText;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 设备ID
        /// </summary>
        public string DeviceId
        {
            get => _deviceId;
            set => SetProperty(ref _deviceId, value);
        }

        /// <summary>
        /// 设备显示名称
        /// </summary>
        public string DeviceName
        {
            get => _deviceName;
            set => SetProperty(ref _deviceName, value);
        }

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(StatusBrush));
                    OnPropertyChanged(nameof(StatusIcon));
                }
            }
        }

        /// <summary>
        /// 是否正在重连
        /// </summary>
        public bool IsReconnecting
        {
            get => _isReconnecting;
            set
            {
                if (SetProperty(ref _isReconnecting, value))
                {
                    OnPropertyChanged(nameof(StatusBrush));
                    OnPropertyChanged(nameof(StatusIcon));
                }
            }
        }

        /// <summary>
        /// 状态文本
        /// </summary>
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        /// <summary>
        /// 状态指示灯颜色
        /// </summary>
        public Brush StatusBrush
        {
            get
            {
                if (IsConnected)
                    return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // 绿色 #4CAF50
                if (IsReconnecting)
                    return new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)); // 橙色 #FF9800
                return new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)); // 灰色 #9E9E9E
            }
        }

        /// <summary>
        /// 状态图标
        /// </summary>
        public string StatusIcon
        {
            get
            {
                if (IsConnected) return "✓";
                if (IsReconnecting) return "⟳";
                return "✗";
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}