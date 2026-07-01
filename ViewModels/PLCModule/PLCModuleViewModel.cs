using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using SeedCut.Models;
using SeedCut.Models.PLCModule;
using SeedCut.Services;

namespace SeedCut.ViewModels.PLCModule
{
    /// <summary>
    /// 按钮操作模式枚举（简化为两种）
    /// </summary>
    public enum ButtonMode
    {
        /// <summary>
        /// 点动模式 - 按下时True，松开时False
        /// </summary>
        Jog,

        /// <summary>
        /// 切换模式 - 第一次按下True，第二次按下False
        /// </summary>
        Toggle
    }

    /// <summary>
    /// PLC控制项ViewModel - 动态生成的UI元素
    /// </summary>
    public class PLCControlItemViewModel : INotifyPropertyChanged
    {
        private readonly IPLCService _plcService;
        private readonly PLCAddressItem _address;
        private readonly PLCControlItemConfig _config;
        private object _value;
        private object _inputValue;
        private bool _isActive;
        private string _displayValue;
        private ButtonMode _buttonMode;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler ValueWritten; // 值写入完成事件

        public PLCControlItemConfig Config => _config;
        public PLCAddressItem Address => _address;

        public string ItemId => _config.ItemId;
        public string Label => _config.Label;
        public ControlType ControlType => _config.ControlType;
        public string StyleKey => _config.StyleKey;
        public bool IsReadOnly => _config.IsReadOnly;
        public string Tooltip => _config.Tooltip;

        /// <summary>
        /// 按钮模式
        /// </summary>
        public ButtonMode ButtonMode
        {
            get => _buttonMode;
            set => SetProperty(ref _buttonMode, value);
        }

        /// <summary>
        /// 控件绑定的值(从PLC读取的实际值)
        /// </summary>
        public object Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                {
                    UpdateDisplayValue();
                    if (_inputValue == null || !_inputValue.Equals(value))
                    {
                        InputValue = value;
                    }
                }
            }
        }

        /// <summary>
        /// 用户输入的临时值
        /// </summary>
        public object InputValue
        {
            get => _inputValue;
            set
            {
                if (SetProperty(ref _inputValue, value))
                {
                    // ✅ 关键修复：强制刷新命令状态
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => CommandManager.InvalidateRequerySuggested()));
                }
            }
        }

        /// <summary>
        /// 显示值(格式化后的字符串)
        /// </summary>
        public string DisplayValue
        {
            get => _displayValue;
            private set => SetProperty(ref _displayValue, value);
        }

        /// <summary>
        /// 指示灯是否激活
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        public ICommand ClickCommand { get; }
        public ICommand UpdateCommand { get; }
        public ICommand MouseDownCommand { get; }  // ✅ 新增：鼠标按下命令
        public ICommand MouseUpCommand { get; }    // ✅ 新增：鼠标松开命令

        public PLCControlItemViewModel(
            IPLCService plcService,
            PLCAddressItem address,
            PLCControlItemConfig config)
        {
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
            _address = address ?? throw new ArgumentNullException(nameof(address));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            // 从配置中读取按钮模式，默认根据ControlType判断
            if (config.ExtraConfig.TryGetValue("ButtonMode", out var modeValue))
            {
                if (modeValue.ToString() == "Jog")
                    _buttonMode = ButtonMode.Jog;
                else
                    _buttonMode = ButtonMode.Toggle;
            }
            else
            {
                // 默认：ToggleButton使用Toggle模式，PulseButton使用Jog模式
                _buttonMode = config.ControlType == ControlType.ToggleButton
                    ? ButtonMode.Toggle
                    : ButtonMode.Jog;
            }

            ClickCommand = new RelayCommand(ExecuteClick, CanExecuteClick);
            UpdateCommand = new RelayCommand(ExecuteUpdate, CanExecuteUpdate);
            MouseDownCommand = new RelayCommand(ExecuteMouseDown, CanExecuteClick);
            MouseUpCommand = new RelayCommand(ExecuteMouseUp, CanExecuteClick);

            RefreshValue();
        }

        /// <summary>
        /// 从PLC刷新值
        /// </summary>
        public void RefreshValue()
        {
            if (!_plcService.IsConnected || _address == null)
                return;

            try
            {
                switch (_address.DataType)
                {
                    case PLCAddressType.Bit:
                        // ✅ 使用新方法
                        var bitValue = _plcService.ReadBit(_address);
                        Value = bitValue;
                        IsActive = bitValue;
                        break;

                    case PLCAddressType.Float:
                        var floatValue = _plcService.ReadFloat(
                            _address.DBNumber,
                            _address.StartAddress);
                        Value = floatValue;
                        break;

                    case PLCAddressType.Int16:
                        var intValue = _plcService.ReadInt16(
                            _address.DBNumber,
                            _address.StartAddress);
                        Value = intValue;
                        break;

                    case PLCAddressType.Int32:
                        var int32Value = _plcService.ReadInt32(
                            _address.DBNumber,
                            _address.StartAddress);
                        Value = int32Value;
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取地址 {_address.Name} 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ 点击命令（用于Toggle模式）
        /// </summary>
        private void ExecuteClick()
        {
            if (!_plcService.IsConnected || _address == null)
                return;

            try
            {
                if (ButtonMode == ButtonMode.Toggle)
                {
                    // ✅ 使用新方法
                    bool currentValue = _plcService.ReadBit(_address);
                    _plcService.WriteBit(_address, !currentValue);

                    Task.Delay(50).ContinueWith(_ =>
                    {
                        RefreshValue();
                        ValueWritten?.Invoke(this, EventArgs.Empty);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"执行点击命令失败: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ 鼠标按下命令（用于Jog模式）
        /// </summary>
        private void ExecuteMouseDown()
        {
            if (!_plcService.IsConnected || _address == null)
                return;

            try
            {
                if (ButtonMode == ButtonMode.Jog)
                {
                    // ✅ 使用新方法
                    var success = _plcService.WriteBit(_address, true);

                    if (success)
                    {
                        IsActive = true;
                        System.Diagnostics.Debug.WriteLine($"✓ 点动按下: {_address.Name} = True");
                        ValueWritten?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"执行鼠标按下失败: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ 鼠标松开命令（用于Jog模式）
        /// </summary>
        private void ExecuteMouseUp()
        {
            if (!_plcService.IsConnected || _address == null)
                return;

            try
            {
                if (ButtonMode == ButtonMode.Jog)
                {
                    // ✅ 使用新方法
                    var success = _plcService.WriteBit(_address, false);

                    if (success)
                    {
                        IsActive = false;
                        System.Diagnostics.Debug.WriteLine($"✓ 点动松开: {_address.Name} = False");
                        ValueWritten?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"执行鼠标松开失败: {ex.Message}");
            }
        }

        private bool CanExecuteClick()
        {
            return _plcService.IsConnected && !IsReadOnly;
        }

        /// <summary>
        /// 执行更新并触发全局刷新
        /// </summary>
        private void ExecuteUpdate()
        {
            if (!_plcService.IsConnected || _address == null || IsReadOnly || _inputValue == null)
                return;

            try
            {
                bool writeSuccess = false;

                switch (_address.DataType)
                {
                    case PLCAddressType.Float:
                        if (float.TryParse(_inputValue.ToString(), out float floatValue))
                        {
                            // 验证范围
                            if (_config.ExtraConfig.TryGetValue("MinValue", out var minVal) &&
                                floatValue < Convert.ToDouble(minVal))
                            {
                                System.Diagnostics.Debug.WriteLine($"输入值 {floatValue} 小于最小值 {minVal}");
                                return;
                            }
                            if (_config.ExtraConfig.TryGetValue("MaxValue", out var maxVal) &&
                                floatValue > Convert.ToDouble(maxVal))
                            {
                                System.Diagnostics.Debug.WriteLine($"输入值 {floatValue} 大于最大值 {maxVal}");
                                return;
                            }

                            writeSuccess = _plcService.WriteFloat(
                                _address.DBNumber,
                                _address.StartAddress,
                                floatValue);

                            if (writeSuccess)
                            {
                                Value = floatValue;
                                System.Diagnostics.Debug.WriteLine($"✓ 成功写入Float: {_address.Name} = {floatValue}");
                            }
                        }
                        break;

                    case PLCAddressType.Int16:
                        if (int.TryParse(_inputValue.ToString(), out int intValue))
                        {
                            // 验证范围
                            if (_config.ExtraConfig.TryGetValue("MinValue", out var minVal) &&
                                intValue < Convert.ToInt32(minVal))
                            {
                                System.Diagnostics.Debug.WriteLine($"输入值 {intValue} 小于最小值 {minVal}");
                                return;
                            }
                            if (_config.ExtraConfig.TryGetValue("MaxValue", out var maxVal) &&
                                intValue > Convert.ToInt32(maxVal))
                            {
                                System.Diagnostics.Debug.WriteLine($"输入值 {intValue} 大于最大值 {maxVal}");
                                return;
                            }

                            writeSuccess = _plcService.WriteInt16(
                                _address.DBNumber,
                                _address.StartAddress,
                                (short)intValue);

                            if (writeSuccess)
                            {
                                Value = (short)intValue;
                                System.Diagnostics.Debug.WriteLine($"✓ 成功写入Int16: {_address.Name} = {intValue}");
                            }
                        }
                        break;
                    case PLCAddressType.Int32:
                        if (int.TryParse(_inputValue.ToString(), out int int32Value))
                        {
                            // 验证范围
                            if (_config.ExtraConfig.TryGetValue("MinValue", out var minVal) &&
                                int32Value < Convert.ToInt32(minVal))
                            {
                                System.Diagnostics.Debug.WriteLine($"输入值 {int32Value} 小于最小值 {minVal}");
                                return;
                            }
                            if (_config.ExtraConfig.TryGetValue("MaxValue", out var maxVal) &&
                                int32Value > Convert.ToInt32(maxVal))
                            {
                                System.Diagnostics.Debug.WriteLine($"输入值 {int32Value} 大于最大值 {maxVal}");
                                return;
                            }

                            writeSuccess = _plcService.WriteInt32(
                                _address.DBNumber,
                                _address.StartAddress,
                                int32Value);

                            if (writeSuccess)
                            {
                                Value = (int)int32Value;
                                System.Diagnostics.Debug.WriteLine($"✓ 成功写入Int16: {_address.Name} = {int32Value}");
                            }
                        }
                        break;


                    case PLCAddressType.Bit:
                        if (_inputValue is bool boolValue)
                        {
                            writeSuccess = _plcService.WriteBit(
                                _address.DBNumber,
                                _address.StartAddress,
                                _address.BitPosition,
                                boolValue);

                            if (writeSuccess)
                            {
                                Value = boolValue;
                                System.Diagnostics.Debug.WriteLine($"✓ 成功写入Bit: {_address.Name} = {boolValue}");
                            }
                        }
                        break;
                }

                // 写入成功后触发刷新事件
                if (writeSuccess)
                {
                    ValueWritten?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"✗ 写入失败: {_address.Name}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新参数失败: {ex.Message}");
            }
        }

        private bool CanExecuteUpdate()
        {
            // 1. 检查PLC连接
            if (!_plcService.IsConnected)
            {
                System.Diagnostics.Debug.WriteLine($"❌ CanExecuteUpdate: PLC未连接");
                return false;
            }

            // 2. 检查是否只读
            if (IsReadOnly)
            {
                System.Diagnostics.Debug.WriteLine($"❌ CanExecuteUpdate: 字段只读 ({_address.Name})");
                return false;
            }

            // 3. 检查输入值是否存在
            if (_inputValue == null)
            {
                System.Diagnostics.Debug.WriteLine($"❌ CanExecuteUpdate: InputValue为null ({_address.Name})");
                return false;
            }

            // ✅ 移除值比较检查
            // 原因：用户需要能够输入和编辑，即使当前输入值等于实际值
            // 值是否改变应该在ExecuteUpdate()执行时检查并提示，而不是禁用输入

            System.Diagnostics.Debug.WriteLine($"✅ CanExecuteUpdate: 可以更新 ({_address.Name})");
            return true;
        }

        private void UpdateDisplayValue()
        {
            if (Value == null)
            {
                DisplayValue = "--";
                return;
            }

            if (_config.ExtraConfig.TryGetValue("Unit", out var unit))
            {
                if (_config.ExtraConfig.TryGetValue("DecimalPlaces", out var decimalPlaces))
                {
                    if (Value is float f)
                    {
                        DisplayValue = $"{f.ToString($"F{decimalPlaces}")} {unit}";
                        return;
                    }
                }
                DisplayValue = $"{Value} {unit}";
            }
            else
            {
                DisplayValue = Value.ToString();
            }
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }

    /// <summary>
    /// PLC控制区域ViewModel
    /// </summary>
    public class PLCControlSectionViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string SectionId { get; }
        public string Title { get; }
        public SectionType Type { get; }
        public LayoutConfig Layout { get; }
        public ObservableCollection<PLCControlItemViewModel> ControlItems { get; }

        public PLCControlSectionViewModel(PLCControlSection section, IPLCService plcService)
        {
            SectionId = section.SectionId;
            Title = section.Title;
            Type = section.Type;
            Layout = section.Layout;
            ControlItems = new ObservableCollection<PLCControlItemViewModel>();

            foreach (var itemConfig in section.ControlItems)
            {
                var address = plcService.AddressRegistry.GetAddress(itemConfig.AddressName);
                if (address != null)
                {
                    var itemViewModel = new PLCControlItemViewModel(plcService, address, itemConfig);
                    ControlItems.Add(itemViewModel);
                }
                else
                {
                    throw new InvalidOperationException($"地址 '{itemConfig.AddressName}' 不存在");
                }
            }
        }

        public void RefreshAll()
        {
            foreach (var item in ControlItems)
            {
                item.RefreshValue();
            }
        }
    }

    /// <summary>
    /// PLC模块ViewModel
    /// </summary>
    public class PLCModuleViewModel : INotifyPropertyChanged
    {
        private readonly IPLCService _plcService;
        private readonly PLCModuleMetadata _metadata;
        private readonly DispatcherTimer _refreshTimer;
        private bool _isRefreshing;
        private bool _isAutoRefreshEnabled = true;  // ✅ 默认启用自动刷新
        private int _refreshInterval = 50;        // ✅ 默认1秒

        public event PropertyChangedEventHandler PropertyChanged;

        public string ModuleId => _metadata.ModuleId;
        public string DisplayName => _metadata.DisplayName;
        public string Description => _metadata.Description;
        public ObservableCollection<PLCControlSectionViewModel> ControlSections { get; }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            set => SetProperty(ref _isRefreshing, value);
        }

        /// <summary>
        /// 是否启用自动刷新
        /// </summary>
        public bool IsAutoRefreshEnabled
        {
            get => _isAutoRefreshEnabled;
            set
            {
                if (SetProperty(ref _isAutoRefreshEnabled, value))
                {
                    if (value && _plcService.IsConnected)
                    {
                        StartRefresh();
                        System.Diagnostics.Debug.WriteLine("✅ 自动刷新已启用");
                    }
                    else
                    {
                        StopRefresh();
                        System.Diagnostics.Debug.WriteLine("⏸️ 自动刷新已停止");
                    }
                    OnPropertyChanged(nameof(RefreshStatusText));
                }
            }
        }

        /// <summary>
        /// 刷新间隔（毫秒）
        /// </summary>
        public int RefreshInterval
        {
            get => _refreshInterval;
            set
            {
                if (SetProperty(ref _refreshInterval, value))
                {
                    _refreshTimer.Interval = TimeSpan.FromMilliseconds(value);
                    System.Diagnostics.Debug.WriteLine($"⏱️ 刷新间隔已设置为: {value}ms");
                    OnPropertyChanged(nameof(RefreshStatusText));
                }
            }
        }

        /// <summary>
        /// 刷新状态文本（用于UI显示）
        /// </summary>
        public string RefreshStatusText
        {
            get
            {
                if (!_plcService.IsConnected)
                    return "未连接";
                if (IsAutoRefreshEnabled)
                    return $"自动 ({RefreshInterval}ms)";
                return "手动";
            }
        }

        public ICommand ManualRefreshCommand { get; }
        public ICommand ToggleAutoRefreshCommand { get; }  // ✅ 新增：切换自动刷新命令

        public PLCModuleViewModel(IPLCService plcService, PLCModuleMetadata metadata)
        {
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

            ControlSections = new ObservableCollection<PLCControlSectionViewModel>();

            foreach (var section in _metadata.ControlSections)
            {
                var sectionViewModel = new PLCControlSectionViewModel(section, _plcService);
                ControlSections.Add(sectionViewModel);

                // 订阅每个控件的值写入事件
                foreach (var item in sectionViewModel.ControlItems)
                {
                    item.ValueWritten += OnItemValueWritten;
                }
            }

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(_refreshInterval)  // ✅ 使用配置的间隔
            };
            _refreshTimer.Tick += OnRefreshTimerTick;

            ManualRefreshCommand = new RelayCommand(RefreshAllControls, CanRefresh);
            ToggleAutoRefreshCommand = new RelayCommand(() => IsAutoRefreshEnabled = !IsAutoRefreshEnabled);  // ✅ 新增

            // ✅ 新增：监听PLC连接状态变化
            if (_plcService is PLCService plcServiceImpl)
            {
                // 假设PLCService有ConnectionChanged事件
                // 如果没有，可以通过其他方式监听
            }

            // 模块加载时立即刷新一次
            RefreshAllControls();

            // ✅ 新增：如果已连接PLC，则自动启动定时器
            if (_plcService.IsConnected && IsAutoRefreshEnabled)
            {
                StartRefresh();
                System.Diagnostics.Debug.WriteLine("✅ 自动刷新已启动 (间隔: {0}ms)", RefreshInterval);
            }
        }

        /// <summary>
        /// 当任何控件值写入后，刷新整个模块
        /// </summary>
        private void OnItemValueWritten(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("🔄 检测到值写入，触发全局刷新...");

            // 延迟一小段时间后刷新，确保PLC已经处理完写入
            Task.Delay(100).ContinueWith(_ =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    RefreshAllControls();
                });
            });
        }

        /// <summary>
        /// 刷新所有控件
        /// </summary>
        public void RefreshAllControls()
        {
            if (!_plcService.IsConnected || IsRefreshing)
                return;

            System.Diagnostics.Debug.WriteLine("🔄 开始刷新模块所有控件...");

            IsRefreshing = true;
            try
            {
                foreach (var section in ControlSections)
                {
                    section.RefreshAll();
                }
                System.Diagnostics.Debug.WriteLine("✓ 模块刷新完成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ 刷新失败: {ex.Message}");
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private bool CanRefresh()
        {
            return _plcService.IsConnected && !IsRefreshing;
        }

        public void StartRefresh()
        {
            if (_plcService.IsConnected && IsAutoRefreshEnabled)
            {
                _refreshTimer.Start();
                System.Diagnostics.Debug.WriteLine($"▶️ 定时器已启动 (间隔: {RefreshInterval}ms)");
                OnPropertyChanged(nameof(RefreshStatusText));
            }
        }

        public void StopRefresh()
        {
            _refreshTimer.Stop();
            System.Diagnostics.Debug.WriteLine("⏹️ 定时器已停止");
            OnPropertyChanged(nameof(RefreshStatusText));
        }

        private void OnRefreshTimerTick(object sender, EventArgs e)
        {
            if (IsRefreshing || !_plcService.IsConnected)
                return;

            IsRefreshing = true;
            try
            {
                foreach (var section in ControlSections)
                {
                    section.RefreshAll();
                }
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}