using Microsoft.Extensions.DependencyInjection;
using SeedCut.Framework.Services.Devices;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Framework.Services.Sequences;
using SeedCut.Framework.Services.Traceability;
using SeedCut.Models.PLCModule;
using SeedCut.Services;
using SeedCut.Services.Camera;  // ✅ 新增：多相机架构
using SeedCut.Services.Connection;
using SeedCut.Services.DeviceAdapter;
using SeedCut.ViewModels.PLCModule;
using SeedCut.Views;
using SeedCut.Views.PLCModule;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// 主窗口的ViewModel
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        #region 事件

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region 私有字段

        private readonly IServiceProvider _serviceProvider;
        private string _currentUserName = "Admin";
        private string _currentTime = DateTime.Now.ToString("yyyy/MM/dd 星期二 HH:mm");
        private string _selectedPage = "主页";
        private object _currentPageContent;

        // 任务配置
        private uint _taskQuantity = 1000;
        private string _selectedSeedCode = "玉米";
        private string _selectedCutArea = "区域A";

        // 效率统计
        private string _runningTime = "";
        private string _averageTime = "";
        private string _grabCount = "";
        private string _laserGrabCount = "";
        private string _smallMaterialCount = "";
        private string _largeMaterialCount = "";
        private string _laserVisionSuccessRate = "";
        private string _smallMaterialSuccessRate = "";
        private string _largeMaterialSuccessRate = "";


        // ========== 设备管理相关字段（新增） ==========
        private readonly IDeviceManager _deviceManager;
        private readonly DeviceInitializationService _deviceInitService;
        private bool _isDeviceOperating = false;
        private string _deviceOperationStatus;
        #endregion

        #region 属性
        /// <summary>
        /// 报警视图模型（用于主窗口报警栏绑定）
        /// </summary>
        public AlarmViewModel AlarmViewModel { get; private set; }
        public string CurrentUserName
        {
            get => _currentUserName;
            set
            {
                if (_currentUserName != value)
                {
                    _currentUserName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string CurrentTime
        {
            get => _currentTime;
            set
            {
                if (_currentTime != value)
                {
                    _currentTime = value;
                    OnPropertyChanged();
                }
            }
        }



        public string ConnectionStatus
        {
            get
            {
                if (TotalDeviceCount == 0)
                    return "无设备";
                if (AreAllDevicesConnected)
                    return "已连接";
                if (ConnectedDeviceCount > 0)
                    return $"部分连接 ({ConnectedDeviceCount}/{TotalDeviceCount})";
                return "未连接";
            }
        }

        public string SelectedPage
        {
            get => _selectedPage;
            set
            {
                if (_selectedPage != value)
                {
                    _selectedPage = value;
                    OnPropertyChanged();
                }
            }
        }

        public object CurrentPageContent
        {
            get => _currentPageContent;
            set
            {
                if (_currentPageContent != value)
                {
                    _currentPageContent = value;
                    OnPropertyChanged();
                }
            }
        }

        // 任务配置属性
        public uint TaskQuantity
        {
            get => _taskQuantity;
            set
            {
                if (_taskQuantity != value)
                {
                    _taskQuantity = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedSeedCode
        {
            get => _selectedSeedCode;
            set
            {
                if (_selectedSeedCode != value)
                {
                    _selectedSeedCode = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedCutArea
        {
            get => _selectedCutArea;
            set
            {
                if (_selectedCutArea != value)
                {
                    _selectedCutArea = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<string> SeedCodes
        {
            get => seedCodes;
            set
            {
                seedCodes = value;
                OnPropertyChanged(nameof(SeedCodes));
            }
        }
        public ObservableCollection<string> CutAreas { get => cutAreas; 
            set
            {
                cutAreas = value;
                OnPropertyChanged(nameof(CutAreas));
            }
        }

        // 效率统计属性
        public string RunningTime
        {
            get => _runningTime;
            set
            {
                if (_runningTime != value)
                {
                    _runningTime = value;
                    OnPropertyChanged();
                }
            }
        }

        public string AverageTime
        {
            get => _averageTime;
            set
            {
                if (_averageTime != value)
                {
                    _averageTime = value;
                    OnPropertyChanged();
                }
            }
        }

        public string GrabCount
        {
            get => _grabCount;
            set
            {
                if (_grabCount != value)
                {
                    _grabCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LaserGrabCount
        {
            get => _laserGrabCount;
            set
            {
                if (_laserGrabCount != value)
                {
                    _laserGrabCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SmallMaterialCount
        {
            get => _smallMaterialCount;
            set
            {
                if (_smallMaterialCount != value)
                {
                    _smallMaterialCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LargeMaterialCount
        {
            get => _largeMaterialCount;
            set
            {
                if (_largeMaterialCount != value)
                {
                    _largeMaterialCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LaserVisionSuccessRate
        {
            get => _laserVisionSuccessRate;
            set
            {
                if (_laserVisionSuccessRate != value)
                {
                    _laserVisionSuccessRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SmallMaterialSuccessRate
        {
            get => _smallMaterialSuccessRate;
            set
            {
                if (_smallMaterialSuccessRate != value)
                {
                    _smallMaterialSuccessRate = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LargeMaterialSuccessRate
        {
            get => _largeMaterialSuccessRate;
            set
            {
                if (_largeMaterialSuccessRate != value)
                {
                    _largeMaterialSuccessRate = value;
                    OnPropertyChanged();
                }
            }
        }
        #region 设备连接状态属性（新增）

        /// <summary>
        /// 设备状态列表（供 Popup 显示）
        /// </summary>
        public ObservableCollection<DeviceStatusItem> DeviceStatusList { get; }
            = new ObservableCollection<DeviceStatusItem>();

        /// <summary>
        /// 是否所有设备已连接
        /// </summary>
        public bool AreAllDevicesConnected => _deviceManager?.AreAllConnected ?? false;

        /// <summary>
        /// 已连接设备数量
        /// </summary>
        public int ConnectedDeviceCount => DeviceStatusList?.Count(d => d.IsConnected) ?? 0;

        /// <summary>
        /// 总设备数量
        /// </summary>
        public int TotalDeviceCount => DeviceStatusList?.Count ?? 0;

        /// <summary>
        /// 总体连接状态指示灯颜色
        /// </summary>
        public Brush OverallConnectionBrush
        {
            get
            {
                if (TotalDeviceCount == 0)
                    return new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)); // 灰色

                var hasReconnecting = DeviceStatusList?.Any(d => d.IsReconnecting) ?? false;

                if (AreAllDevicesConnected)
                    return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // 绿色

                if (hasReconnecting)
                    return new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)); // 橙色

                if (ConnectedDeviceCount > 0)
                    return new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)); // 黄色

                return new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)); // 灰色
            }
        }

        /// <summary>
        /// 连接状态背景色
        /// </summary>
        public Brush ConnectionStatusBackground
        {
            get
            {
                if (AreAllDevicesConnected)
                    return new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)); // 浅绿色
                if (ConnectedDeviceCount > 0)
                    return new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1)); // 浅黄色
                return new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)); // 浅灰色
            }
        }

        /// <summary>
        /// 连接状态文字颜色
        /// </summary>
        public Brush ConnectionStatusForeground
        {
            get
            {
                if (AreAllDevicesConnected)
                    return new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); // 深绿色
                if (ConnectedDeviceCount > 0)
                    return new SolidColorBrush(Color.FromRgb(0xF5, 0x7C, 0x00)); // 深橙色
                return new SolidColorBrush(Color.FromRgb(0x61, 0x61, 0x61)); // 深灰色
            }
        }

        /// <summary>
        /// 是否正在执行设备操作
        /// </summary>
        public bool IsDeviceOperating
        {
            get => _isDeviceOperating;
            set
            {
                if (_isDeviceOperating != value)
                {
                    _isDeviceOperating = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// 设备操作状态文本
        /// </summary>
        public string DeviceOperationStatus
        {
            get => _deviceOperationStatus;
            set
            {
                if (_deviceOperationStatus != value)
                {
                    _deviceOperationStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion
        #endregion

        #region 命令

        public ICommand NavigateCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand InitializeCommand { get; }
        public ICommand ClearCommand { get; }

        private readonly IPLCService _plcService;

        /// <summary>
        /// 连接所有设备命令
        /// </summary>
        public ICommand ConnectAllDevicesCommand { get; }

        /// <summary>
        /// 断开所有设备命令
        /// </summary>
        public ICommand DisconnectAllDevicesCommand { get; }

        #endregion

        #region 构造函数
        public MainViewModel(
            IServiceProvider serviceProvider,
            IDeviceManager deviceManager,
            DeviceInitializationService deviceInitService,
            AlarmViewModel alarmViewModel, IPLCService plcService)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
            _deviceInitService = deviceInitService ?? throw new ArgumentNullException(nameof(deviceInitService));
            AlarmViewModel = alarmViewModel ?? throw new ArgumentNullException(nameof(alarmViewModel));

            // 初始化设备状态列表
            DeviceStatusList = new ObservableCollection<DeviceStatusItem>();

            // 初始化命令
            NavigateCommand = new RelayCommand<string>(OnNavigate);
            SaveConfigCommand = new RelayCommand(OnSaveConfig, CanSaveConfig);
            StartCommand = new RelayCommand(async () => await OnStartAsync(), CanStart);
            PauseCommand = new RelayCommand(OnPause, CanPause);
            StopCommand = new RelayCommand(async () => await OnStopAsync(), CanStop);
            ResetCommand = new RelayCommand(OnReset);
            InitializeCommand = new AsyncRelayCommand(OnInitialize);
            ClearCommand = new RelayCommand(OnClear);
            this._plcService = plcService;

            // 设备连接命令
            // ✅ 新代码：
            ConnectAllDevicesCommand = new AsyncRelayCommand(
                ConnectAllDevicesAsync,  // 直接传递方法引用
                () => !IsDeviceOperating && !AreAllDevicesConnected);


            // ✅ 新代码：
            DisconnectAllDevicesCommand = new AsyncRelayCommand(
                DisconnectAllDevicesAsync,  // 直接传递方法引用
                () => !IsDeviceOperating && ConnectedDeviceCount > 0);

            // 初始化下拉框数据
            SeedCodes = new ObservableCollection<string> { "玉米", "小麦", "水稻", "大豆" };
            CutAreas = new ObservableCollection<string> { "区域A", "区域B", "区域C" };

            // 订阅 DeviceManager 事件
            _deviceManager.ConnectionStateChanged += OnDeviceManagerConnectionStateChanged;

            // 初始化设备状态列表
            InitializeDeviceStatusList();

            Debug.WriteLine("✅ MainViewModel 已创建，已订阅 DeviceManager 事件");
        }



        #endregion

        private bool CanStart() => !IsDeviceOperating;

        private HandlerSequence handlerSequence;
        private ObservableCollection<string> seedCodes;
        private ObservableCollection<string> cutAreas;

        private async Task OnStartAsync()
        {
            // 如果设备未全部连接，先连接设备
            if (!AreAllDevicesConnected && _deviceManager != null)
            {
                var result = MessageBox.Show(
                    "部分设备未连接，是否先连接所有设备？",
                    "提示",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await ConnectAllDevicesAsync();

                }
                //else if (result == MessageBoxResult.Cancel)
                //{
                //    return;
                //}
            }
            if (MessageBox.Show("系统开始启动", "提示", MessageBoxButton.OK, MessageBoxImage.Information) == MessageBoxResult.OK)
            {
                HandlerDebugViewModel handlerDebug = _serviceProvider.GetService<HandlerDebugViewModel>();

                handlerDebug.SelectedSequence = handlerDebug.Sequences[0];
                handlerDebug.ApplyRecommendedConditionsCommand?.Execute(this);
                await Task.Delay(1000);
                handlerDebug.ExecuteSequenceCommand?.Execute(this);
            }


        }
        private bool CanPause() => !IsDeviceOperating;



        private bool CanStop() => !IsDeviceOperating;

        private async Task OnStopAsync()
        {
            //// 停止时断开所有设备
            //if (ConnectedDeviceCount > 0 && _deviceManager != null)
            //{
            //    var result = MessageBox.Show(
            //        "是否同时断开所有设备连接？",
            //        "提示",
            //        MessageBoxButton.YesNo,
            //        MessageBoxImage.Question);

            //    if (result == MessageBoxResult.Yes)
            //    {
            //        await DisconnectAllDevicesAsync();
            //    }
            //}
            HandlerDebugViewModel handlerDebug = _serviceProvider.GetService<HandlerDebugViewModel>();

            handlerDebug.CancelSequenceCommand?.Execute(this);
            await Task.Delay(1000);

            MessageBox.Show("系统停止", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 初始化 AlarmViewModel
        /// </summary>
        private void InitializeAlarmViewModel()
        {
            if (_serviceProvider != null)
            {
                try
                {
                    // 从 DI 容器获取 AlarmViewModel（单例）
                    AlarmViewModel = _serviceProvider.GetRequiredService<AlarmViewModel>();

                    // 同步当前用户名
                    AlarmViewModel.CurrentUserName = CurrentUserName;

                    System.Diagnostics.Debug.WriteLine("✅ AlarmViewModel 初始化成功（从DI容器）");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 无法从DI获取 AlarmViewModel: {ex.Message}");


                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("⚠️ ServiceProvider 为 null，尝试手动创建 AlarmViewModel");
            }
        }
        #region 设备状态管理（新增）

        /// <summary>
        /// 初始化设备状态列表
        /// </summary>
        private void InitializeDeviceStatusList()
        {
            DeviceStatusList.Clear();

            if (_deviceManager == null) return;

            foreach (var device in _deviceManager.Devices.Values)
            {
                var item = new DeviceStatusItem
                {
                    DeviceId = device.DeviceId,
                    DeviceName = device.DeviceName,
                    IsConnected = device.IsConnected,
                    IsReconnecting = device.ConnectionState == Framework.Core.DeviceConnectionState.Reconnecting,
                    StatusText = GetDeviceStatusText(device)
                };

                DeviceStatusList.Add(item);
            }

            NotifyConnectionStatusChanged();
            Debug.WriteLine($"✅ 设备状态列表已初始化，共 {DeviceStatusList.Count} 个设备");
        }

        /// <summary>
        /// 更新设备状态列表
        /// ✅ 修复：使用 BeginInvoke 替代 Invoke，避免跨线程死锁
        /// </summary>
        private void UpdateDeviceStatusList()
        {
            if (_deviceManager == null) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            // ✅ 使用 BeginInvoke 异步更新UI，不阻塞调用线程
            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    foreach (var device in _deviceManager.Devices.Values)
                    {
                        var item = DeviceStatusList.FirstOrDefault(d => d.DeviceId == device.DeviceId);
                        if (item != null)
                        {
                            item.IsConnected = device.IsConnected;
                            item.IsReconnecting = device.ConnectionState == Framework.Core.DeviceConnectionState.Reconnecting;
                            item.StatusText = GetDeviceStatusText(device);
                        }
                        else
                        {
                            DeviceStatusList.Add(new DeviceStatusItem
                            {
                                DeviceId = device.DeviceId,
                                DeviceName = device.DeviceName,
                                IsConnected = device.IsConnected,
                                IsReconnecting = device.ConnectionState == Framework.Core.DeviceConnectionState.Reconnecting,
                                StatusText = GetDeviceStatusText(device)
                            });
                        }
                    }

                    NotifyConnectionStatusChanged();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ 更新设备状态列表异常: {ex.Message}");
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// 获取设备状态文本
        /// </summary>
        private string GetDeviceStatusText(IDevice device)
        {
            switch (device.ConnectionState)
            {
                case Framework.Core.DeviceConnectionState.Connected:
                    return "已连接";
                case Framework.Core.DeviceConnectionState.Connecting:
                    return "连接中...";
                case Framework.Core.DeviceConnectionState.Reconnecting:
                    return "重连中...";
                case Framework.Core.DeviceConnectionState.Error:
                    return $"错误: {device.LastError}";
                default:
                    return "未连接";
            }
        }

        /// <summary>
        /// 通知连接状态相关属性变更
        /// </summary>
        private void NotifyConnectionStatusChanged()
        {
            OnPropertyChanged(nameof(AreAllDevicesConnected));
            OnPropertyChanged(nameof(ConnectedDeviceCount));
            OnPropertyChanged(nameof(TotalDeviceCount));
            OnPropertyChanged(nameof(ConnectionStatus));
            OnPropertyChanged(nameof(OverallConnectionBrush));
            OnPropertyChanged(nameof(ConnectionStatusBackground));
            OnPropertyChanged(nameof(ConnectionStatusForeground));
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// DeviceManager 连接状态变化事件处理
        /// </summary>
        private void OnDeviceManagerConnectionStateChanged(object sender, DeviceConnectionSummary summary)
        {
            Debug.WriteLine($"📡 设备状态变化: {summary.ConnectedCount}/{summary.TotalCount} 已连接");
            UpdateDeviceStatusList();
        }

        /// <summary>
        /// 连接所有设备
        /// ✅ 优化：添加 ConfigureAwait(false) 避免死锁
        /// </summary>
        private async Task ConnectAllDevicesAsync()
        {
            if (_deviceManager == null)
            {
                MessageBox.Show("设备管理器未初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                IsDeviceOperating = true;
                DeviceOperationStatus = "正在连接所有设备...";

                Debug.WriteLine("🔌 开始连接所有设备...");

                // ✅ 使用 ConfigureAwait(false) 确保不阻塞UI线程
                var result = await Task.Run(async () =>
                {
                    return await _deviceManager.ConnectAllAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);

                // ✅ 由于使用了 ConfigureAwait(false)，需要手动切回UI线程更新状态
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (result)
                    {
                        DeviceOperationStatus = "所有设备连接成功";
                        Debug.WriteLine("✅ 所有设备连接成功");
                    }
                    else
                    {
                        var summary = _deviceManager.GetConnectionSummary();
                        DeviceOperationStatus = $"部分设备连接失败 ({summary.ConnectedCount}/{summary.TotalCount})";
                        Debug.WriteLine($"⚠️ 部分设备连接失败: {summary.ConnectedCount}/{summary.TotalCount}");
                    }

                    UpdateDeviceStatusList();
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DeviceOperationStatus = $"连接失败: {ex.Message}";
                    Debug.WriteLine($"❌ 连接设备失败: {ex.Message}");
                    MessageBox.Show($"连接设备失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsDeviceOperating = false;
                });
            }
        }

        /// <summary>
        /// 断开所有设备
        /// ✅ 优化：添加 ConfigureAwait(false) 避免死锁
        /// </summary>
        private async Task DisconnectAllDevicesAsync()
        {
            if (_deviceManager == null)
            {
                MessageBox.Show("设备管理器未初始化", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                IsDeviceOperating = true;
                DeviceOperationStatus = "正在断开所有设备...";

                Debug.WriteLine("🔌 开始断开所有设备...");

                // ✅ 使用 ConfigureAwait(false) 确保不阻塞UI线程
                await Task.Run(async () =>
                {
                    await _deviceManager.DisconnectAllAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);

                // ✅ 由于使用了 ConfigureAwait(false)，需要手动切回UI线程更新状态
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DeviceOperationStatus = "所有设备已断开";
                    Debug.WriteLine("✅ 所有设备已断开");
                    UpdateDeviceStatusList();
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DeviceOperationStatus = $"断开失败: {ex.Message}";
                    Debug.WriteLine($"❌ 断开设备失败: {ex.Message}");
                    MessageBox.Show($"断开设备失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsDeviceOperating = false;
                });
            }
        }

        #endregion
        #region 命令处理

        private void OnNavigate(string page)
        {
            SelectedPage = page;

            // 根据页面名称切换内容
            switch (page)
            {
                case "主页":
                    CurrentPageContent = null; // 主页内容直接在MainWindow中显示
                    break;
                case "监测":
                    CurrentPageContent = new MonitorView();
                    break;
                case "日志":
                    CurrentPageContent = new LogView();
                    break;

                case "追溯":
                    CurrentPageContent = CreateTraceQueryView();
                    break;
                case "管理员":
                    // 创建AdminView（包含Vision等子页面）
                    CurrentPageContent = CreateAdminView();
                    break;
                default:
                    CurrentPageContent = null;
                    break;
            }
        }

        /// <summary>
        /// ✅ 新增：显示报警历史页面
        /// 用于从报警栏点击跳转到报警历史
        /// </summary>
        public void ShowAlarmHistory()
        {
            try
            {
                SelectedPage = "报警历史";
                CurrentPageContent = CreateAlarmHistoryView();

                System.Diagnostics.Debug.WriteLine("✓ 已切换到监测页面（包含报警历史）");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 显示报警历史失败: {ex.Message}");
                MessageBox.Show($"无法显示报警历史：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// ✅ 新增（可选）：创建独立的报警历史视图
        /// 如果报警历史是独立页面，可以使用此方法
        /// </summary>
        private object CreateAlarmHistoryView()
        {
            if (_serviceProvider != null)
            {
                try
                {
                    var view = _serviceProvider.GetRequiredService<AlarmHistoryView>();

                    // ✅ 添加调试：检查是否设置了 DataContext
                    Debug.WriteLine($"🔍 CreateAlarmHistoryView:");
                    Debug.WriteLine($"   View: {view}");
                    Debug.WriteLine($"   View.DataContext: {view.DataContext?.GetType().Name ?? "NULL"}");
                    Debug.WriteLine($"   AlarmViewModel: {AlarmViewModel}");

                    // ❌ 问题：你可能忘记设置 DataContext！
                    view.DataContext = AlarmViewModel;  // 👈 确保添加这一行

                    return view;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ 从 DI 获取 AlarmHistoryView 失败: {ex.Message}");
                    var view = new AlarmHistoryView();
                    view.DataContext = AlarmViewModel;  // 👈 确保添加这一行
                    return view;
                }
            }
            else
            {
                var view = new AlarmHistoryView();
                view.DataContext = AlarmViewModel;  // 👈 确保添加这一行
                return view;
            }
        }

        /// <summary>
        /// 创建追溯查询视图
        /// </summary>
        private object CreateTraceQueryView()
        {
            if (_serviceProvider != null)
            {
                try
                {
                    // 从DI获取追溯服务
                    var traceService = _serviceProvider.GetService<ITraceService>();

                    if (traceService != null)
                    {
                        // 创建ViewModel
                        var viewModel = new TraceQueryViewModel(traceService);

                        // 创建View并设置DataContext
                        var view = new TraceQueryPage();
                        view.DataContext = viewModel;

                        return view;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ ITraceService 未注册");
                        MessageBox.Show("追溯服务未初始化，请检查配置", "提示",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 创建追溯视图失败: {ex.Message}");
                    MessageBox.Show($"无法加载追溯页面：{ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("⚠️ ServiceProvider 为 null");
                MessageBox.Show("服务提供者未初始化", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        private object CreateAdminView()
        {
            if (_serviceProvider != null)
            {
                try
                {
                    return _serviceProvider.GetRequiredService<AdminView>();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 从 DI 获取 AdminView 失败: {ex.Message}");

                    // 手动创建 AdminView
                    try
                    {
                        return new AdminView(_serviceProvider);
                    }
                    catch (Exception ex2)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ 手动创建 AdminView 失败: {ex2.Message}");
                        MessageBox.Show($"无法加载管理员页面：{ex2.Message}", "错误",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return null;
                    }
                }
            }
            else
            {
                // ✅ 没有 DI 容器时，尝试手动构建完整的依赖链
                System.Diagnostics.Debug.WriteLine("⚠️ ServiceProvider 为 null，尝试手动创建");

                try
                {
                    // 手动创建临时的 ServiceProvider
                    var services = new ServiceCollection();

                    // ✅ 注册日志服务（基础服务）
                    services.AddSingleton<ILogService, SeedCut.Framework.Services.Logging.SerilogService>();

                    // ✅ 注册 Vision 服务
                    services.AddSingleton<IVisionService, VisionMasterService>();
                    services.AddTransient<VisionViewModel>();
                    services.AddTransient<VisionView>();

                    // ============================================================
                    // ✅ 注册 Camera 服务（多相机架构）
                    // ============================================================
                    services.AddSingleton<HikCameraConfig>(sp => HikCameraConfig.Load());
                    services.AddSingleton<HikCameraServiceFactory>();
                    services.AddSingleton<ICameraService>(sp =>
                    {
                        var factory = sp.GetRequiredService<HikCameraServiceFactory>();
                        factory.InitializeSDK();
                        return factory.CreateInstance("Camera_Disk");  // 振动盘相机作为默认
                    });
                    services.AddTransient<CameraViewModel>();
                    services.AddTransient<CameraView>();

                    // ✅ 注册 Robot 服务
                    services.AddSingleton<IRobotService, RobotService>();
                    services.AddTransient<RobotViewModel>();
                    services.AddTransient<RobotView>();

                    // ✅ 新增：注册机器人连接适配器
                    services.AddSingleton<RobotServiceConnectAdapter>(sp =>
                    {
                        var robotService = sp.GetRequiredService<IRobotService>();
                        var logService = sp.GetService<ILogService>();
                        return new RobotServiceConnectAdapter(robotService, logService);
                    });

                    // ✅ 注册报警服务（新增）
                    services.AddSingleton<IAlarmService, SeedCut.Framework.Services.Alarm.AlarmService>();

                    // ✅ 注册振动盘服务
                    services.AddSingleton<IVibratorService>(sp =>
                    {
                        var logService = sp.GetRequiredService<ILogService>();
                        var alarmService = sp.GetService<IAlarmService>(); // 可选，允许为null
                        return new VibratorService(logService, alarmService);
                    }); services.AddTransient<VibratorViewModel>();
                    services.AddTransient<VibratorView>();

                    // ✅ 新增：注册振动盘连接适配器
                    services.AddSingleton<VibratorServiceConnectAdapter>(sp =>
                    {
                        var vibratorService = sp.GetRequiredService<IVibratorService>();
                        var logService = sp.GetService<ILogService>();
                        return new VibratorServiceConnectAdapter(vibratorService, logService);
                    });


                    // ✅ 注册PLC服务
                    services.AddSingleton<PLCAddressRegistry>();
                    services.AddSingleton<IPLCService, PLCService>();
                    services.AddSingleton<PLCModuleFactory>();

                    // ✅ 注册PLC连接适配器
                    services.AddSingleton<PLCServiceConnectAdapter>(sp =>
                    {
                        var plcService = sp.GetRequiredService<IPLCService>();
                        var logService = sp.GetService<ILogService>();
                        return new PLCServiceConnectAdapter(plcService, logService);
                    });

                    services.AddTransient<MainPLCViewModel>();
                    services.AddTransient<MainPLCView>();

                    // ✅ 注册激光器服务
                    services.AddSingleton<ILaserService, LaserService>();
                    services.AddTransient<LaserDebugViewModel>();
                    services.AddTransient<LaserDebugView>();

                    services.AddSingleton<HM_LaserService>(sp =>
                    {
                        var logService = sp.GetService<ILogService>();
                        return new HM_LaserService(logService);
                    });

                    services.AddTransient<HM_LaserDebugViewModel>(sp =>
                    {
                        var hmService = sp.GetRequiredService<HM_LaserService>();
                        return new HM_LaserDebugViewModel(hmService);
                    });

                    services.AddTransient<HM_LaserDebugView>();


                    // ✅ 注册连接监控服务
                    services.AddSingleton<ConnectionMonitorService>();

                    // 注册 Admin 视图
                    services.AddTransient<AdminViewModel>();
                    services.AddTransient<AdminView>();

                    var tempProvider = services.BuildServiceProvider();

                    // 从临时 Provider 创建 AdminView
                    return tempProvider.GetRequiredService<AdminView>();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ 创建临时 ServiceProvider 失败: {ex.Message}");
                    MessageBox.Show($"无法创建管理员页面：{ex.Message}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }
            }
        }

        private bool CanSaveConfig()
        {
            return TaskQuantity > 0;
        }

        private void OnSaveConfig()
        {
            // TODO: 实现保存配置逻辑
            MessageBox.Show("配置已保存", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnStart()
        {
            // TODO: 实现启动逻辑
            MessageBox.Show("系统启动", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnPause()
        {
            // TODO: 实现暂停逻辑
            MessageBox.Show("系统暂停", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnStop()
        {
            // TODO: 实现停止逻辑
            MessageBox.Show("系统停止", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnReset()
        {
            //上位机 - 复位
            PLCAddressItem addressItem = this._plcService.AddressRegistry.GetAddress("上位机-复位");

            this._plcService?.WriteBit(addressItem.DBNumber, addressItem.StartAddress, addressItem.BitPosition, true);
            //await Task.Delay(2000);
            // TODO: 实现复位逻辑
            MessageBox.Show("系统复位", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task OnInitialize()
        {
            //环形导轨轴初始化
            PLCAddressItem addressItem = this._plcService.AddressRegistry.GetAddress("环形导轨轴初始化");

            this._plcService?.WriteBit(addressItem.DBNumber, addressItem.StartAddress, addressItem.BitPosition, false);
            await Task.Delay(2000);
            this._plcService?.WriteBit(addressItem.DBNumber, addressItem.StartAddress, addressItem.BitPosition, true);

            // TODO: 实现初始化逻辑
            MessageBox.Show("系统初始化", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnClear()
        {
            //上位机-周期性停机标志
            // TODO: 实现清料逻辑
            PLCAddressItem addressItem = this._plcService.AddressRegistry.GetAddress("上位机-周期性停机标志");

            this._plcService?.WriteBit(addressItem.DBNumber, addressItem.StartAddress, addressItem.BitPosition, true);
            MessageBox.Show("清料", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region INotifyPropertyChanged

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}