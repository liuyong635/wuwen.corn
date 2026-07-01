using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using SeedCut.Services.Connection;  
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SeedCut.Models.PLCModule;
using SeedCut.Services;
using Microsoft.Extensions.DependencyInjection;

namespace SeedCut.ViewModels.PLCModule
{
    /// <summary>
    /// 主PLC ViewModel - 管理所有子模块
    /// </summary>
    public class MainPLCViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IPLCService _plcService;
        private readonly ConnectionMonitorService _connectionMonitor; 
        private readonly string _plcDeviceId = "PLC_Main"; 
        private readonly PLCModuleFactory _moduleFactory;
        private string _connectionStatus;
        private bool _isConnected;
        private bool _isBusy;
        private string _statusMessage;
        private PLCModuleViewModel _currentModule;
        private PLCModuleMetadata _selectedModuleMeta;

        public event PropertyChangedEventHandler PropertyChanged;

        #region 属性

        /// <summary>
        /// 连接状态文本
        /// </summary>
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set => SetProperty(ref _connectionStatus, value);
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
                    UpdateConnectionStatus();
                    CommandManager.InvalidateRequerySuggested();

                    // 连接状态变化时，通知当前模块
                    if (value && _currentModule != null)
                    {
                        _currentModule.StartRefresh();
                    }
                    else if (!value && _currentModule != null)
                    {
                        _currentModule.StopRefresh();
                    }
                }
            }
        }

        /// <summary>
        /// 是否繁忙
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// 状态消息
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// PLC地址信息（显示用）
        /// </summary>
        public string PLCAddress => $"{_plcService.ConnectionConfig?.IPAddress}:{_plcService.ConnectionConfig?.Port}";

        /// <summary>
        /// 可用模块列表
        /// </summary>
        public ObservableCollection<PLCModuleMetadata> AvailableModules { get; }

        /// <summary>
        /// 当前选中的模块元数据
        /// </summary>
        public PLCModuleMetadata SelectedModuleMeta
        {
            get => _selectedModuleMeta;
            set
            {
                if (SetProperty(ref _selectedModuleMeta, value))
                {
                    // ✅ 核心修改：更新所有模块的 IsSelected 状态
                    foreach (var module in AvailableModules)
                    {
                        module.IsSelected = (module == value);
                    }

                    LoadModule(value);
                }
            }
        }

        /// <summary>
        /// 当前显示的模块ViewModel
        /// </summary>
        public PLCModuleViewModel CurrentModule
        {
            get => _currentModule;
            private set => SetProperty(ref _currentModule, value);
        }

        #endregion

        #region 命令

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SelectModuleCommand { get; }

        #endregion

        public MainPLCViewModel(IPLCService plcService,  ConnectionMonitorService connectionMonitor, IServiceProvider provider)
        {
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
            _connectionMonitor = connectionMonitor ?? throw new ArgumentNullException(nameof(connectionMonitor));

            _moduleFactory = provider.GetRequiredService<PLCModuleFactory>();

            AvailableModules = new ObservableCollection<PLCModuleMetadata>();

            // 初始化命令
            ConnectCommand = new RelayCommand(ExecuteConnect, CanExecuteConnect);
            DisconnectCommand = new RelayCommand(ExecuteDisconnect, CanExecuteDisconnect);
            RefreshCommand = new RelayCommand(ExecuteRefresh, CanExecuteRefresh);
            SelectModuleCommand = new RelayCommand<PLCModuleMetadata>(ExecuteSelectModule);

            // 加载所有模块
            LoadAvailableModules();
            _connectionMonitor.DeviceStatusChanged += OnDeviceStatusChanged;

            // 初始化连接状态
            UpdateConnectionStatus();

            // 默认选择第一个模块
            if (AvailableModules.Count > 0)
            {
                SelectedModuleMeta = AvailableModules[0];
            }
            
            // ✅ 添加诊断输出
            System.Diagnostics.Debug.WriteLine($"✅ MainPLCViewModel 构造完成");
            System.Diagnostics.Debug.WriteLine($"📦 加载的模块数量: {AvailableModules.Count}");
            foreach (var module in AvailableModules)
            {
                System.Diagnostics.Debug.WriteLine($"   - {module.DisplayName} ({module.ModuleId})");
            }
        }

        #region 私有方法

        /// <summary>
        /// 加载可用模块列表
        /// </summary>
        private void LoadAvailableModules()
        {
            AvailableModules.Clear();
            var modules = _moduleFactory.GetAllModules();

            System.Diagnostics.Debug.WriteLine($"🔍 从工厂获取到 {modules.Count} 个模块");

            foreach (var module in modules)
            {
                AvailableModules.Add(module);
            }
        }

        /// <summary>
        /// 加载指定模块
        /// </summary>
        private void LoadModule(PLCModuleMetadata metadata)
        {
            if (metadata == null)
                return;

            try
            {
                // 停止旧模块的刷新
                _currentModule?.StopRefresh();

                // 创建新模块ViewModel
                CurrentModule = new PLCModuleViewModel(_plcService, metadata);

                // 如果已连接，启动刷新
                if (IsConnected)
                {
                    CurrentModule.StartRefresh();
                }

                StatusMessage = $"已加载模块: {metadata.DisplayName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载模块失败: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"加载模块失败: {ex}");
            }
        }

        /// <summary>
        /// 更新连接状态显示
        /// </summary>
        private void UpdateConnectionStatus()
        {
            // ✅ 从 ConnectionMonitorService 获取实时状态
            var deviceStatus = _connectionMonitor.GetDeviceStatus(_plcDeviceId);

            if (deviceStatus != null)
            {
                IsConnected = deviceStatus.IsConnected;

                if (deviceStatus.IsConnected)
                {
                    ConnectionStatus = $"✓ 已连接 - {PLCAddress}";
                }
                else if (deviceStatus.IsReconnecting)
                {
                    ConnectionStatus = $"⟳ 重连中 (第{deviceStatus.RetryCount}次) - {PLCAddress}";
                }
                else
                {
                    ConnectionStatus = $"✗ 未连接 - {PLCAddress}";
                }
            }
            else
            {
                // ✅ 降级方案也改为从监控服务获取，而不是直接读PLCService
                IsConnected = false;
                ConnectionStatus = $"✗ 未连接 - {PLCAddress}";

            }
        }

        #endregion
        /// <summary>
        /// ✅ 新增：连接状态变化处理
        /// </summary>
        private void OnDeviceStatusChanged(object sender, DeviceConnectionStatusChangedEventArgs e)
        {
            if (e.DeviceId == _plcDeviceId)
            {
                // 在UI线程更新状态
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateConnectionStatus();

                    if (e.IsConnected)
                    {
                        StatusMessage = $"PLC已连接 ({e.Status.GetStatusText()})";
                        _currentModule?.StartRefresh();
                    }
                    else
                    {
                        StatusMessage = $"PLC断开 ({e.Status.GetStatusText()})";
                        _currentModule?.StopRefresh();
                    }
                });
            }
        }
        #region 命令实现

        private async void ExecuteConnect()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "正在连接PLC...";

            try
            {
                // ✅ 通过 ConnectionMonitorService 连接
                bool result = await _connectionMonitor.ConnectDeviceAsync(_plcDeviceId);

                if (result)
                {
                    StatusMessage = "PLC连接成功";
                }
                else
                {
                    StatusMessage = "PLC连接失败";
                }

                UpdateConnectionStatus();
            }
            catch (Exception ex)
            {
                StatusMessage = $"连接异常: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"PLC连接异常: {ex}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanExecuteConnect()
        {
            return !IsBusy && !IsConnected;
        }

        private void ExecuteDisconnect()
        {
            if (IsBusy) return;

            try
            {
                // ✅ 通过 ConnectionMonitorService 断开
                _connectionMonitor.DisconnectDevice(_plcDeviceId);

                StatusMessage = "PLC已断开";
                UpdateConnectionStatus();
            }
            catch (Exception ex)
            {
                StatusMessage = $"断开异常: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"PLC断开异常: {ex}");
            }
        }

        private bool CanExecuteDisconnect()
        {
            return !IsBusy && IsConnected;
        }

        private void ExecuteRefresh()
        {
            if (_currentModule != null)
            {
                foreach (var section in _currentModule.ControlSections)
                {
                    section.RefreshAll();
                }
                StatusMessage = "已刷新当前模块数据";
            }
        }

        private bool CanExecuteRefresh()
        {
            return !IsBusy && IsConnected && _currentModule != null;
        }

        /// <summary>
        /// ✅ 新增：选择模块命令
        /// </summary>
        private void ExecuteSelectModule(PLCModuleMetadata module)
        {
            if (module != null && module.IsEnabled)
            {
                SelectedModuleMeta = module;
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _currentModule?.StopRefresh();

            // ✅ 取消订阅事件
            if (_connectionMonitor != null)
            {
                _connectionMonitor.DeviceStatusChanged -= OnDeviceStatusChanged;
            }
        }

        #endregion

        #region INotifyPropertyChanged

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        #endregion
    }

    /// <summary>
    /// 简单的 RelayCommand 实现
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();

        public void Execute(object parameter) => _execute();
    }

    /// <summary>
    /// 泛型 RelayCommand 实现
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecute == null) return true;
            if (parameter == null && typeof(T).IsValueType) return false;
            return _canExecute((T)parameter);
        }

        public void Execute(object parameter) => _execute((T)parameter);
    }
}