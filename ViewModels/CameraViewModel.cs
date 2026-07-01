using SeedCut.Services;
using SeedCut.Services.Camera;
using SeedCut.Services.Connection;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// 相机页面ViewModel（改造版）
    /// 支持两种连接方式：
    /// 1. 枚举连接（原有功能，保持不变）
    /// 2. 配置连接（新增功能，纳入ConnectionMonitorService监控）
    /// </summary>
    public class CameraViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ICameraService _cameraService;
        private readonly HikCameraServiceFactory _cameraFactory;
        private readonly ConnectionMonitorService _connectionMonitor;
        private readonly HikCameraConfig _cameraConfig;

        // 当前配置连接的相机实例和适配器
        private HikCameraInstance _currentConfiguredInstance;
        private HikCameraConnectAdapter _currentAdapter;
        private string _currentDeviceId;

        // 绑定属性
        private int _selectedCameraIndex = -1;
        private CameraInstanceConfig _selectedConfiguredCamera;
        private string _exposureTime = "5000";
        private string _gain = "0";
        private string _frameRate = "30";
        private string _selectedPixelFormat = "Mono8";
        private string _statusMessage = "相机未连接";
        private bool _isStatusError;
        private Brush _connectionIndicatorColor;

        // ========== 构造函数 ==========
        public CameraViewModel(
            ICameraService cameraService,
            HikCameraServiceFactory cameraFactory,
            ConnectionMonitorService connectionMonitor,
            HikCameraConfig cameraConfig)
        {
            _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
            _cameraFactory = cameraFactory ?? throw new ArgumentNullException(nameof(cameraFactory));
            _connectionMonitor = connectionMonitor ?? throw new ArgumentNullException(nameof(connectionMonitor));
            _cameraConfig = cameraConfig ?? throw new ArgumentNullException(nameof(cameraConfig));

            // 订阅服务事件
            _cameraService.PropertyChanged += OnServicePropertyChanged;
            _cameraService.ErrorOccurred += OnServiceError;
            _cameraService.MessageReceived += OnServiceMessage;
            _cameraService.ImageReceived += OnServiceImageReceived;

            // 订阅连接监控服务事件
            _connectionMonitor.DeviceStatusChanged += OnDeviceStatusChanged;

            // 初始化命令
            InitializeCommands();

            // 初始化像素格式列表
            PixelFormats = new ObservableCollection<string>
            {
                "Mono8",
                "RGB8Packed",
                "BayerRG8",
                "BayerGR8",
                "BayerGB8",
                "BayerBG8"
            };

            // 加载配置相机列表
            ConfiguredCameras = new ObservableCollection<CameraInstanceConfig>(_cameraConfig.Cameras ?? new System.Collections.Generic.List<CameraInstanceConfig>());

            // 初始化状态指示灯颜色
            ConnectionIndicatorColor = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // 红色（未连接）

            // 初始化SDK
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_cameraService.InitializeSDK())
                {
                    MessageBox.Show("相机SDK初始化失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }));
        }

        // ========== 属性 - 原有枚举连接相关 ==========
        public ObservableCollection<string> CameraList =>
            new ObservableCollection<string>(_cameraService.CameraList);

        public int SelectedCameraIndex
        {
            get => _selectedCameraIndex;
            set => SetProperty(ref _selectedCameraIndex, value);
        }

        public bool IsConnected => _cameraService.IsConnected;
        public bool IsCapturing => _cameraService.IsCapturing;
        public int FrameCount => _cameraService.FrameCount;
        public string CurrentResolution => _cameraService.CurrentResolution;
        public double CurrentFps => _cameraService.CurrentFps;
        public string SavePath => _cameraService.SavePath;
        public BitmapSource CurrentImage
        {
            get
            {
                // 优先返回配置相机的图像
                if (_currentConfiguredInstance != null && _currentConfiguredInstance.IsConnected)
                {
                    return _currentConfiguredInstance.CurrentImage;
                }
                // 否则返回枚举相机的图像
                return _cameraService.CurrentImage;
            }
        }

        // ========== 属性 - 新增配置连接相关 ==========

        /// <summary>
        /// 配置的相机列表
        /// </summary>
        public ObservableCollection<CameraInstanceConfig> ConfiguredCameras { get; }

        /// <summary>
        /// 当前选中的配置相机
        /// </summary>
        public CameraInstanceConfig SelectedConfiguredCamera
        {
            get => _selectedConfiguredCamera;
            set => SetProperty(ref _selectedConfiguredCamera, value);
        }

        /// <summary>
        /// 当前配置相机的设备ID
        /// </summary>
        public string CurrentDeviceId
        {
            get => _currentDeviceId;
            private set => SetProperty(ref _currentDeviceId, value);
        }

        /// <summary>
        /// 连接状态指示灯颜色
        /// </summary>
        public Brush ConnectionIndicatorColor
        {
            get => _connectionIndicatorColor;
            set => SetProperty(ref _connectionIndicatorColor, value);
        }

        /// <summary>
        /// 配置相机是否已连接
        /// </summary>
        public bool IsConfiguredCameraConnected
        {
            get => _currentConfiguredInstance?.IsConnected ?? false;
        }

        /// <summary>
        /// 配置相机连接状态文本
        /// </summary>
        public string ConfiguredCameraStatusText
        {
            get
            {
                if (_currentConfiguredInstance == null)
                    return "未选择配置相机";

                var status = _connectionMonitor.GetDeviceStatus(CurrentDeviceId);
                if (status == null)
                    return "未连接";

                return status.GetStatusText();
            }
        }

        // ========== 属性 - 共用 ==========
        public string ExposureTime
        {
            get => _exposureTime;
            set => SetProperty(ref _exposureTime, value);
        }

        public string Gain
        {
            get => _gain;
            set => SetProperty(ref _gain, value);
        }

        public string FrameRate
        {
            get => _frameRate;
            set => SetProperty(ref _frameRate, value);
        }

        public ObservableCollection<string> PixelFormats { get; }

        public string SelectedPixelFormat
        {
            get => _selectedPixelFormat;
            set => SetProperty(ref _selectedPixelFormat, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsStatusError
        {
            get => _isStatusError;
            set => SetProperty(ref _isStatusError, value);
        }

        // ========== 命令 - 原有枚举连接相关 ==========
        public ICommand RefreshCameraListCommand { get; private set; }
        public ICommand ConnectCameraCommand { get; private set; }
        public ICommand DisconnectCameraCommand { get; private set; }
        public ICommand StartCaptureCommand { get; private set; }
        public ICommand StopCaptureCommand { get; private set; }
        public ICommand CaptureOnceCommand { get; private set; }
        public ICommand ApplySettingsCommand { get; private set; }
        public ICommand SelectSavePathCommand { get; private set; }
        public ICommand SaveImageCommand { get; private set; }

        // ========== 命令 - 新增配置连接相关 ==========
        public ICommand ConnectConfiguredCameraCommand { get; private set; }
        public ICommand DisconnectConfiguredCameraCommand { get; private set; }

        // ========== 命令初始化 ==========
        private void InitializeCommands()
        {
            // 原有枚举连接命令
            RefreshCameraListCommand = new RelayCommand(
                execute: () => _cameraService.RefreshCameraList()
            );

            ConnectCameraCommand = new RelayCommand(
                execute: async () =>
                {
                    if (SelectedCameraIndex >= 0)
                    {
                        await _cameraService.ConnectCameraAsync(SelectedCameraIndex);
                    }
                },
                canExecute: () => !IsConnected && SelectedCameraIndex >= 0
            );

            DisconnectCameraCommand = new RelayCommand(
                execute: () => _cameraService.DisconnectCamera(),
                canExecute: () => IsConnected
            );

            StartCaptureCommand = new RelayCommand(
                execute: async () => await _cameraService.StartCaptureAsync(),
                canExecute: () => IsConnected && !IsCapturing
            );

            StopCaptureCommand = new RelayCommand(
                execute: () => _cameraService.StopCapture(),
                canExecute: () => IsConnected && IsCapturing
            );

            CaptureOnceCommand = new RelayCommand(
                execute: async () => await _cameraService.CaptureOnceAsync(),
                canExecute: () => IsConnected && !IsCapturing
            );

            ApplySettingsCommand = new RelayCommand(
                execute: () => ApplySettings(),
                canExecute: () => IsConnected || IsConfiguredCameraConnected
            );

            SelectSavePathCommand = new RelayCommand(
                execute: () => SelectSavePath()
            );

            SaveImageCommand = new RelayCommand(
                execute: async () => await SaveConfiguredCameraImage(),
                canExecute: () => (IsConnected && CurrentImage != null) || (IsConfiguredCameraConnected && _currentConfiguredInstance?.CurrentImage != null)
            );

            // 新增配置连接命令
            ConnectConfiguredCameraCommand = new RelayCommand(
                execute: async () => await ConnectConfiguredCamera(),
                canExecute: () => SelectedConfiguredCamera != null && !IsConfiguredCameraConnected
            );

            DisconnectConfiguredCameraCommand = new RelayCommand(
                execute: () => DisconnectConfiguredCamera(),
                canExecute: () => IsConfiguredCameraConnected
            );
        }

        // ========== 配置相机连接方法 ==========

        /// <summary>
        /// 连接配置相机
        /// </summary>
        private async System.Threading.Tasks.Task ConnectConfiguredCamera()
        {
            if (SelectedConfiguredCamera == null)
            {
                ShowError("请先选择要连接的配置相机");
                return;
            }

            try
            {
                StatusMessage = $"正在连接配置相机: {SelectedConfiguredCamera.DisplayName}...";

                // 1. 创建相机实例
                _currentConfiguredInstance = _cameraFactory.CreateInstance(SelectedConfiguredCamera.CameraId);

                // 2. 创建适配器
                _currentAdapter = _cameraFactory.CreateAdapter(SelectedConfiguredCamera.CameraId);

                // 3. 注册到连接监控服务
                _connectionMonitor.RegisterDevice(_currentAdapter);

                // 4. 保存当前设备ID
                CurrentDeviceId = _currentAdapter.DeviceId;

                // 5. 通过连接监控服务连接
                bool success = await _connectionMonitor.ConnectDeviceAsync(CurrentDeviceId);

                if (success)
                {
                    StatusMessage = $"配置相机已连接: {SelectedConfiguredCamera.DisplayName}";

                    // 订阅图像接收事件
                    _currentConfiguredInstance.ImageReceived += OnConfiguredCameraImageReceived;

                    // 订阅PropertyChanged事件以更新CurrentImage
                    _currentConfiguredInstance.PropertyChanged += OnConfiguredCameraPropertyChanged;

                    // 通知UI更新
                    OnPropertyChanged(nameof(IsConfiguredCameraConnected));
                    OnPropertyChanged(nameof(ConfiguredCameraStatusText));
                    OnPropertyChanged(nameof(CurrentImage)); // 立即更新图像显示
                    CommandManager.InvalidateRequerySuggested();
                }
                else
                {
                    StatusMessage = $"配置相机连接失败: {SelectedConfiguredCamera.DisplayName}";
                    CleanupConfiguredCamera();
                }
            }
            catch (Exception ex)
            {
                ShowError($"连接配置相机失败: {ex.Message}");
                CleanupConfiguredCamera();
            }
        }

        /// <summary>
        /// 断开配置相机
        /// </summary>
        private void DisconnectConfiguredCamera()
        {
            try
            {
                if (_currentAdapter != null && !string.IsNullOrEmpty(CurrentDeviceId))
                {
                    _connectionMonitor.DisconnectDevice(CurrentDeviceId);
                    _connectionMonitor.UnregisterDevice(CurrentDeviceId);
                }

                CleanupConfiguredCamera();

                StatusMessage = "配置相机已断开";
                OnPropertyChanged(nameof(IsConfiguredCameraConnected));
                OnPropertyChanged(nameof(ConfiguredCameraStatusText));
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                ShowError($"断开配置相机失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 清理配置相机资源
        /// </summary>
        private void CleanupConfiguredCamera()
        {
            if (_currentConfiguredInstance != null)
            {
                _currentConfiguredInstance.ImageReceived -= OnConfiguredCameraImageReceived;
                _currentConfiguredInstance.PropertyChanged -= OnConfiguredCameraPropertyChanged;
                _currentConfiguredInstance = null;
            }

            _currentAdapter = null;
            CurrentDeviceId = null;

            // 通知UI更新图像显示
            OnPropertyChanged(nameof(CurrentImage));
        }

        /// <summary>
        /// 配置相机图像接收事件处理
        /// ✅ 修复：使用 BeginInvoke 替代 Invoke，避免跨线程死锁
        /// </summary>
        private void OnConfiguredCameraImageReceived(object sender, EventArgs e)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // 更新UI显示配置相机的图像
                    OnPropertyChanged(nameof(CurrentImage));
                    CommandManager.InvalidateRequerySuggested();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[相机] 更新图像异常: {ex.Message}");
                }
            }));
        }

        /// <summary>
        /// 配置相机属性变化处理
        /// ✅ 修复：使用 BeginInvoke 替代 Invoke，避免跨线程死锁
        /// </summary>
        private void OnConfiguredCameraPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ICameraService.CurrentImage))
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        OnPropertyChanged(nameof(CurrentImage));
                        CommandManager.InvalidateRequerySuggested();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[相机] 更新属性异常: {ex.Message}");
                    }
                }));
            }
        }

        /// <summary>
        /// 保存配置相机图像
        /// </summary>
        private async System.Threading.Tasks.Task SaveConfiguredCameraImage()
        {
            if (_currentConfiguredInstance != null && _currentConfiguredInstance.IsConnected)
            {
                try
                {
                    await _currentConfiguredInstance.SaveImageAsync();
                    StatusMessage = "图像已保存";
                }
                catch (Exception ex)
                {
                    ShowError($"保存图像失败: {ex.Message}");
                }
            }
            else
            {
                await _cameraService.SaveImageAsync();
            }
        }

        // ========== 配置相机采集方法（通过HikCameraInstance） ==========

        /// <summary>
        /// 开始配置相机采集
        /// </summary>
        private async System.Threading.Tasks.Task StartConfiguredCameraCapture()
        {
            if (_currentConfiguredInstance != null && _currentConfiguredInstance.IsConnected)
            {
                try
                {
                    await _currentConfiguredInstance.StartCaptureAsync();
                    StatusMessage = "配置相机已开始采集";
                    OnPropertyChanged(nameof(IsCapturing));
                    CommandManager.InvalidateRequerySuggested();
                }
                catch (Exception ex)
                {
                    ShowError($"开始采集失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 停止配置相机采集
        /// </summary>
        private void StopConfiguredCameraCapture()
        {
            if (_currentConfiguredInstance != null && _currentConfiguredInstance.IsConnected)
            {
                try
                {
                    _currentConfiguredInstance.StopCapture();
                    StatusMessage = "配置相机已停止采集";
                    OnPropertyChanged(nameof(IsCapturing));
                    CommandManager.InvalidateRequerySuggested();
                }
                catch (Exception ex)
                {
                    ShowError($"停止采集失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 配置相机单次采集
        /// </summary>
        private async System.Threading.Tasks.Task CaptureConfiguredCameraOnce()
        {
            if (_currentConfiguredInstance != null && _currentConfiguredInstance.IsConnected)
            {
                try
                {
                    await _currentConfiguredInstance.CaptureOnceAsync();
                    StatusMessage = "已完成单次采集";
                }
                catch (Exception ex)
                {
                    ShowError($"单次采集失败: {ex.Message}");
                }
            }
        }

        // ========== 私有方法 ==========
        private void ApplySettings()
        {
            if (!double.TryParse(ExposureTime, out double exposure))
            {
                ShowError("曝光时间格式错误");
                return;
            }

            if (!double.TryParse(Gain, out double gain))
            {
                ShowError("增益格式错误");
                return;
            }

            if (!double.TryParse(FrameRate, out double fps))
            {
                ShowError("帧率格式错误");
                return;
            }

            // 如果配置相机已连接，应用设置到配置相机
            if (_currentConfiguredInstance != null && _currentConfiguredInstance.IsConnected)
            {
                try
                {
                    _currentConfiguredInstance.SetExposureTime(exposure);
                    _currentConfiguredInstance.SetGain(gain);
                    _currentConfiguredInstance.SetFrameRate(fps);
                    _currentConfiguredInstance.SetPixelFormat(SelectedPixelFormat);
                    StatusMessage = "配置相机参数已应用";
                }
                catch (Exception ex)
                {
                    ShowError($"应用配置相机参数失败: {ex.Message}");
                }
            }
            else
            {
                _cameraService.ApplySettings(exposure, gain, fps, SelectedPixelFormat);
            }
        }

        private void SelectSavePath()
        {
            try
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = "选择图像保存路径";
                    dialog.ShowNewFolderButton = true;

                    // 优先使用配置相机的保存路径
                    if (_currentConfiguredInstance != null)
                    {
                        dialog.SelectedPath = _currentConfiguredInstance.SavePath;
                    }
                    else
                    {
                        dialog.SelectedPath = _cameraService.SavePath;
                    }

                    var result = dialog.ShowDialog();
                    if (result == System.Windows.Forms.DialogResult.OK)
                    {
                        if (_currentConfiguredInstance != null)
                        {
                            _currentConfiguredInstance.SavePath = dialog.SelectedPath;
                        }
                        else
                        {
                            _cameraService.SavePath = dialog.SelectedPath;
                        }
                        OnPropertyChanged(nameof(SavePath));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"无法打开文件夹选择对话框\n\n" +
                    $"错误信息: {ex.Message}\n\n" +
                    $"请添加引用：\n" +
                    $"右键项目 → 添加引用 → 程序集 → Framework\n" +
                    $"→ 勾选 System.Windows.Forms\n\n" +
                    $"当前保存路径: {_cameraService.SavePath}",
                    "需要添加引用",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
        }

        private void ShowError(string message)
        {
            MessageBox.Show(message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // ========== 事件处理 - 原有服务事件 ==========
        private void OnServicePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ICameraService.CameraList):
                    OnPropertyChanged(nameof(CameraList));
                    break;
                case nameof(ICameraService.IsConnected):
                    OnPropertyChanged(nameof(IsConnected));
                    CommandManager.InvalidateRequerySuggested();
                    if (IsConnected)
                    {
                        StatusMessage = "相机已连接";
                        IsStatusError = false;
                    }
                    else
                    {
                        StatusMessage = "相机未连接";
                        IsStatusError = false;
                    }
                    break;
                case nameof(ICameraService.IsCapturing):
                    OnPropertyChanged(nameof(IsCapturing));
                    CommandManager.InvalidateRequerySuggested();
                    break;
                case nameof(ICameraService.FrameCount):
                    OnPropertyChanged(nameof(FrameCount));
                    break;
                case nameof(ICameraService.CurrentResolution):
                    OnPropertyChanged(nameof(CurrentResolution));
                    break;
                case nameof(ICameraService.CurrentFps):
                    OnPropertyChanged(nameof(CurrentFps));
                    break;
                case nameof(ICameraService.SavePath):
                    OnPropertyChanged(nameof(SavePath));
                    break;
                case nameof(ICameraService.CurrentImage):
                    OnPropertyChanged(nameof(CurrentImage));
                    CommandManager.InvalidateRequerySuggested();
                    break;
            }
        }

        private void OnServiceError(object sender, string error)
        {
            StatusMessage = error;
            IsStatusError = true;
        }

        private void OnServiceMessage(object sender, string message)
        {
            StatusMessage = message;
            IsStatusError = false;
        }

        private void OnServiceImageReceived(object sender, EventArgs e)
        {
            // 图像已更新，CurrentImage属性会自动通知
        }

        // ========== 事件处理 - 新增连接监控服务事件 ==========

        /// <summary>
        /// 设备连接状态变化处理
        /// ✅ 修复：使用 BeginInvoke 替代 Invoke，避免跨线程死锁
        /// </summary>
        private void OnDeviceStatusChanged(object sender, DeviceConnectionStatusChangedEventArgs e)
        {
            if (e.DeviceId == CurrentDeviceId)
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // 更新连接状态
                        OnPropertyChanged(nameof(IsConfiguredCameraConnected));
                        OnPropertyChanged(nameof(ConfiguredCameraStatusText));

                        // 更新指示灯颜色
                        if (e.IsConnected)
                        {
                            ConnectionIndicatorColor = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // 绿色
                            StatusMessage = $"配置相机已连接: {SelectedConfiguredCamera?.DisplayName}";
                        }
                        else if (e.Status.IsReconnecting)
                        {
                            ConnectionIndicatorColor = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // 橙色（重连中）
                            StatusMessage = $"配置相机重连中 (第{e.Status.RetryCount}次): {SelectedConfiguredCamera?.DisplayName}";
                        }
                        else
                        {
                            ConnectionIndicatorColor = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // 红色
                            StatusMessage = $"配置相机已断开: {SelectedConfiguredCamera?.DisplayName}";
                        }

                        // 更新命令状态
                        CommandManager.InvalidateRequerySuggested();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[相机] 更新设备状态异常: {ex.Message}");
                    }
                }));
            }
        }

        // ========== INotifyPropertyChanged ==========
        public event PropertyChangedEventHandler PropertyChanged;

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

        // ========== IDisposable ==========
        public void Dispose()
        {
            // 取消订阅原有服务事件
            _cameraService.PropertyChanged -= OnServicePropertyChanged;
            _cameraService.ErrorOccurred -= OnServiceError;
            _cameraService.MessageReceived -= OnServiceMessage;
            _cameraService.ImageReceived -= OnServiceImageReceived;

            // 取消订阅连接监控服务事件
            _connectionMonitor.DeviceStatusChanged -= OnDeviceStatusChanged;

            // 断开并清理配置相机
            if (IsConfiguredCameraConnected)
            {
                DisconnectConfiguredCamera();
            }
        }
    }
}