using SeedCut.Framework.Config;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// 加载窗口的ViewModel
    /// 支持Debug/Release模式切换
    /// </summary>
    public class LoadingViewModel : INotifyPropertyChanged
    {
        private readonly ISystemMonitor _systemMonitor;
        private readonly DeviceConnectionConfig _deviceConfig;

        #region 私有字段

        private string _statusText = "正在初始化系统...";
        private int _progress;
        private string _errorMessage = string.Empty;
        private Visibility _errorAreaVisibility = Visibility.Collapsed;
        private Visibility _debugWarningVisibility = Visibility.Collapsed;
        private string _modeDisplayText = string.Empty;
        private Brush _modeBadgeBackground;

        #endregion

        #region 属性

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public int Progress
        {
            get => _progress;
            set
            {
                if (_progress != value)
                {
                    _progress = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public Visibility ErrorAreaVisibility
        {
            get => _errorAreaVisibility;
            set
            {
                if (_errorAreaVisibility != value)
                {
                    _errorAreaVisibility = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 调试模式警告条可见性
        /// </summary>
        public Visibility DebugWarningVisibility
        {
            get => _debugWarningVisibility;
            set
            {
                if (_debugWarningVisibility != value)
                {
                    _debugWarningVisibility = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 模式显示文本（如"调试模式"或"生产模式"）
        /// </summary>
        public string ModeDisplayText
        {
            get => _modeDisplayText;
            set
            {
                if (_modeDisplayText != value)
                {
                    _modeDisplayText = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 模式徽章背景色
        /// </summary>
        public Brush ModeBadgeBackground
        {
            get => _modeBadgeBackground;
            set
            {
                if (_modeBadgeBackground != value)
                {
                    _modeBadgeBackground = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 是否为调试模式
        /// </summary>
        public bool IsDebugMode => _deviceConfig?.IsDebugMode ?? false;

        /// <summary>
        /// 是否为生产模式
        /// </summary>
        public bool IsProductionMode => _deviceConfig?.IsProductionMode ?? true;

        /// <summary>
        /// 模块检查项集合
        /// </summary>
        public ObservableCollection<ModuleCheckItem> ModuleChecks { get; }

        #endregion

        #region 构造函数

        /// <summary>
        /// 带配置的构造函数（推荐）
        /// </summary>
        public LoadingViewModel(ISystemMonitor systemMonitor, DeviceConnectionConfig deviceConfig)
        {
            _systemMonitor = systemMonitor;
            _deviceConfig = deviceConfig;
            ModuleChecks = new ObservableCollection<ModuleCheckItem>();

            // 初始化模式显示
            InitializeModeDisplay();

            // 订阅系统监控事件
            _systemMonitor.ModuleChecked += OnModuleChecked;
            _systemMonitor.CheckCompleted += OnCheckCompleted;
            _systemMonitor.PropertyChanged += OnSystemMonitorPropertyChanged;
        }

        /// <summary>
        /// 兼容旧版本的构造函数
        /// </summary>
        public LoadingViewModel(ISystemMonitor systemMonitor)
            : this(systemMonitor, DeviceConnectionConfig.Load())
        {
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化模式显示
        /// </summary>
        private void InitializeModeDisplay()
        {
            if (_deviceConfig == null)
            {
                ModeDisplayText = "生产模式";
                ModeBadgeBackground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // 绿色
                DebugWarningVisibility = Visibility.Collapsed;
                return;
            }

            var effectiveMode = _deviceConfig.GetEffectiveMode();

            switch (effectiveMode)
            {
                case ConnectionMode.Debug:
                    ModeDisplayText = "调试模式";
                    ModeBadgeBackground = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // 橙色
                    DebugWarningVisibility = _deviceConfig.DebugSettings.ShowDebugWarning
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    StatusText = "调试模式 - 部分设备检查将被跳过";
                    break;

                case ConnectionMode.Selective:
                    ModeDisplayText = "选择性模式";
                    ModeBadgeBackground = new SolidColorBrush(Color.FromRgb(33, 150, 243)); // 蓝色
                    DebugWarningVisibility = Visibility.Collapsed;
                    StatusText = "选择性模式 - 正在初始化...";
                    break;

                case ConnectionMode.Production:
                default:
                    ModeDisplayText = "生产模式";
                    ModeBadgeBackground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // 绿色
                    DebugWarningVisibility = Visibility.Collapsed;
                    StatusText = "正在初始化系统...";
                    break;
            }

            System.Diagnostics.Debug.WriteLine($"[LoadingViewModel] 初始化完成，模式: {effectiveMode}");
        }

        #endregion

        #region 事件处理

        private void OnSystemMonitorPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ISystemMonitor.Progress))
            {
                Progress = _systemMonitor.Progress;
            }
        }

        private void OnModuleChecked(object sender, ModuleCheckedEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine($"模块检查: {e.ModuleName} - {e.Status} - {e.Message}");

                // 查找或添加模块到列表
                var existingItem = ModuleChecks.FirstOrDefault(m => m.ModuleName == e.ModuleName);

                if (existingItem != null)
                {
                    // 更新现有项
                    existingItem.Status = e.Status;
                    existingItem.Message = e.Message;
                }
                else
                {
                    // 添加新项
                    var item = new ModuleCheckItem
                    {
                        ModuleName = e.ModuleName,
                        Status = e.Status,
                        Message = e.Message,
                        IsCritical = e.IsCritical
                    };

                    // 检查是否被跳过（调试模式）
                    if (e.Message?.Contains("[调试模式]") == true)
                    {
                        item.WasSkipped = true;
                    }

                    ModuleChecks.Add(item);
                }

                // 更新总体状态文字
                UpdateStatusText(e.ModuleName);
            });
        }

        /// <summary>
        /// 更新状态文字
        /// </summary>
        private void UpdateStatusText(string currentModule)
        {
            if (IsDebugMode)
            {
                StatusText = $"[调试模式] 检查中: {currentModule} - {_systemMonitor.Progress}%";
            }
            else
            {
                StatusText = $"检查中: {currentModule} - {_systemMonitor.Progress}%";
            }
        }

        private void OnCheckCompleted(object sender, bool allCriticalPassed)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine($"系统检查完成，关键模块: {(allCriticalPassed ? "全部通过" : "存在失败")}");

                if (allCriticalPassed)
                {
                    HandleCheckSuccess();
                }
                else
                {
                    HandleCheckFailure();
                }
            });
        }

        /// <summary>
        /// 处理检查成功
        /// </summary>
        private void HandleCheckSuccess()
        {
            if (IsDebugMode)
            {
                // 调试模式：显示跳过统计
                int skippedCount = ModuleChecks.Count(m => m.WasSkipped);
                if (skippedCount > 0)
                {
                    StatusText = $"系统检查完成！（{skippedCount}项被跳过）";
                }
                else
                {
                    StatusText = "系统检查完成！";
                }
            }
            else
            {
                StatusText = "系统检查完成！";
            }

            ErrorAreaVisibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 处理检查失败
        /// </summary>
        private void HandleCheckFailure()
        {
            // 显示错误信息
            var failedModules = _systemMonitor.GetFailedCriticalModules();
            var errorMsg = "以下关键模块检查失败，程序无法启动：\n\n";

            foreach (var moduleName in failedModules)
            {
                var result = _systemMonitor.GetModuleResult(moduleName);
                errorMsg += $"• {result.ModuleName}: {result.Message}\n";
            }

            // 根据模式给出不同提示
            if (IsDebugMode)
            {
                errorMsg += "\n[调试模式提示]\n";
                errorMsg += "如需跳过此检查，请在配置文件中设置该设备的 SkipInDebug=true\n";
                errorMsg += "配置文件位置: Config/DeviceConnectionConfig.json";
            }
            else
            {
                errorMsg += "\n请检查设备连接并重新启动程序。";
            }

            ErrorMessage = errorMsg;
            ErrorAreaVisibility = Visibility.Visible;
            StatusText = "系统检查失败";

            System.Diagnostics.Debug.WriteLine($"错误信息: {errorMsg}");
        }

        #endregion
        /// <summary>
        /// 更新状态提示（供连接阶段使用）
        /// </summary>
        public void UpdateStatus(string message)
        {
            // 根据你的UI绑定方式更新，比如：
            // StatusMessage = message;
            // 或者更新某个检查项的状态
            System.Diagnostics.Debug.WriteLine($"[Loading] {message}");
        }
        #region 公共方法

        /// <summary>
        /// 获取检查结果摘要
        /// </summary>
        public string GetSummary()
        {
            int total = ModuleChecks.Count;
            int success = ModuleChecks.Count(m => m.Status == "success");
            int failed = ModuleChecks.Count(m => m.Status == "failed");
            int skipped = ModuleChecks.Count(m => m.WasSkipped);
            int warning = ModuleChecks.Count(m => m.Status == "warning");

            if (IsDebugMode && skipped > 0)
            {
                return $"总计: {total}, 成功: {success}, 失败: {failed}, 跳过: {skipped}, 警告: {warning}";
            }

            return $"总计: {total}, 成功: {success}, 失败: {failed}, 警告: {warning}";
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }

    /// <summary>
    /// 模块检查项（扩展版）
    /// </summary>
    public class ModuleCheckItem : INotifyPropertyChanged
    {
        private string _moduleName;
        private string _status;
        private string _message;
        private bool _isCritical;
        private bool _wasSkipped;

        public string ModuleName
        {
            get => _moduleName;
            set
            {
                if (_moduleName != value)
                {
                    _moduleName = value;
                    OnPropertyChanged();
                }
            }
        }

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
                    OnPropertyChanged(nameof(StatusIcon));
                    OnPropertyChanged(nameof(StatusBackground));
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
                }
            }
        }

        public bool IsCritical
        {
            get => _isCritical;
            set
            {
                if (_isCritical != value)
                {
                    _isCritical = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CriticalBadgeVisibility));
                }
            }
        }

        /// <summary>
        /// 是否在调试模式下被跳过
        /// </summary>
        public bool WasSkipped
        {
            get => _wasSkipped;
            set
            {
                if (_wasSkipped != value)
                {
                    _wasSkipped = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SkippedBadgeVisibility));
                }
            }
        }

        /// <summary>
        /// 状态颜色（前景色）
        /// </summary>
        public string StatusColor
        {
            get
            {
                if (WasSkipped) return "#9E9E9E"; // 灰色

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

        /// <summary>
        /// 状态图标
        /// </summary>
        public string StatusIcon
        {
            get
            {
                if (WasSkipped) return "○"; // 跳过图标

                switch (Status)
                {
                    case "success": return "✓";
                    case "failed": return "✗";
                    case "warning": return "!";
                    case "checking": return "...";
                    default: return "○";
                }
            }
        }

        /// <summary>
        /// 状态背景色
        /// </summary>
        public string StatusBackground
        {
            get
            {
                if (WasSkipped) return "#F5F5F5"; // 浅灰

                switch (Status)
                {
                    case "success": return "#E8F5E9";
                    case "failed": return "#FFEBEE";
                    case "warning": return "#FFF3E0";
                    case "checking": return "#E3F2FD";
                    default: return "#FAFAFA";
                }
            }
        }

        /// <summary>
        /// 关键标记可见性
        /// </summary>
        public Visibility CriticalBadgeVisibility =>
            IsCritical ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// 跳过标记可见性
        /// </summary>
        public Visibility SkippedBadgeVisibility =>
            WasSkipped ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}