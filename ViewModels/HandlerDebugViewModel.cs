using Microsoft.Extensions.DependencyInjection;
using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Execution;
using SeedCut.Framework.Services.Handlers;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Framework.Services.Sequences;
using SeedCut.Services.DeviceAdapter;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// Handler调试页面ViewModel
    /// 包含：条件选择器、复合操作/序列执行
    /// ★ 新增：条件诊断日志输出
    /// </summary>
    public class HandlerDebugViewModel : INotifyPropertyChanged, IDisposable
    {
        #region 私有字段
        // ★ 新增：缓存待应用的条件ID（点击一键执行时才真正设置）
        private readonly HashSet<string> _pendingConditionIds = new HashSet<string>();
        private readonly IServiceProvider _serviceProvider;
        private readonly IHandlerRegistry _handlerRegistry;
        private readonly HandlerExecutor _handlerExecutor;
        private readonly ILogService _logService;

        // 条件选择器
        private string _conditionStatusText = "";

        // 序列执行
        private SequenceExecutor _sequenceExecutor;
        private SequenceItem _selectedSequence;
        private bool _isSequenceExecuting;
        private string _seqStatusText = "就绪";
        private int _seqProgressPercent;
        private CancellationTokenSource _seqCurrentCts;

        // ★ 新增：生产上下文（用于判断系统运行状态）
        private readonly IProductionContext _productionContext;
        private readonly ITaskExecutionManager _taskManager;


        #endregion

        #region 属性 - 条件选择器

        public ObservableCollection<ConditionGroupViewModel> ConditionGroups { get; }
            = new ObservableCollection<ConditionGroupViewModel>();

        public ObservableCollection<QuickActionItem> QuickActions { get; }
            = new ObservableCollection<QuickActionItem>();

        public string ConditionStatusText
        {
            get => _conditionStatusText;
            set
            {
                if (_conditionStatusText != value)
                {
                    _conditionStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public int SelectedConditionCount => ConditionGroups
            .SelectMany(g => g.Conditions)
            .Count(c => c.IsSelected);

        #endregion

        #region 属性 - 序列执行

        public ObservableCollection<SequenceItem> Sequences { get; } = new ObservableCollection<SequenceItem>();

        public ObservableCollection<string> SeqExecutionLogs { get; } = new ObservableCollection<string>();

        public ObservableCollection<HandlerProgressItem> ProgressItems { get; } = new ObservableCollection<HandlerProgressItem>();

        /// <summary>
        /// 推荐条件列表（选择序列时自动更新）
        /// </summary>
        public ObservableCollection<RecommendedConditionItem> RecommendedConditions { get; }
            = new ObservableCollection<RecommendedConditionItem>();

        public SequenceItem SelectedSequence
        {
            get => _selectedSequence;
            set
            {
                if (_selectedSequence != value)
                {
                    _selectedSequence = value;

                    // ★ 新增：切换序列时清空预设条件
                    _pendingConditionIds.Clear();

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSequenceSelection));
                    OnPropertyChanged(nameof(SelectedSequenceInfo));
                    UpdateProgressItems();
                    UpdateRecommendedConditions();
                }
            }
        }

        /// <summary>
        /// 是否有推荐条件
        /// </summary>
        public bool HasRecommendedConditions => RecommendedConditions.Count > 0;

        public bool HasSequenceSelection => SelectedSequence != null;

        public string SelectedSequenceInfo
        {
            get
            {
                if (SelectedSequence == null) return "请选择一个复合操作";

                var seq = SelectedSequence;
                return $"包含 {seq.HandlerCount} 个Handler: {seq.HandlerList}\n" +
                       $"超时时间: {seq.Timeout.TotalSeconds}秒    " +
                       (seq.AllowPartialSuccess ? "允许部分成功" : "需全部成功");
            }
        }

        public bool IsSequenceExecuting
        {
            get => _isSequenceExecuting;
            set
            {
                if (_isSequenceExecuting != value)
                {
                    _isSequenceExecuting = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanExecuteSequence));
                    OnPropertyChanged(nameof(CanCancelSequence));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// 是否可以执行序列
        /// ★ 修改：增加系统运行状态检查，机器运行中时禁止启动新序列
        /// </summary>
        public bool CanExecuteSequence =>
            HasSequenceSelection &&
            !IsSequenceExecuting &&
            (_taskManager?.RunningCount ?? 0) == 0 &&
            !(_productionContext?.IsRunning ?? false);  // ★ 新增：系统运行中时禁止

        /// <summary>
        /// 是否可以取消
        /// ★ 修改：序列执行中 或 系统运行中 都可以取消
        /// </summary>
        public bool CanCancelSequence => IsSequenceExecuting || (_productionContext?.IsRunning ?? false);

        public string SeqStatusText
        {
            get => _seqStatusText;
            set
            {
                if (_seqStatusText != value)
                {
                    _seqStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public int SeqProgressPercent
        {
            get => _seqProgressPercent;
            set
            {
                if (_seqProgressPercent != value)
                {
                    _seqProgressPercent = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region 命令

        // 条件选择器命令
        public ICommand ApplySelectedConditionsCommand { get; }
        public ICommand ClearSelectedConditionsCommand { get; }
        public ICommand ClearAllConditionsCommand { get; }
        public ICommand RefreshConditionStatusCommand { get; }
        public ICommand ExecuteQuickActionCommand { get; }

        // 序列执行命令
        public ICommand ExecuteSequenceCommand { get; }
        public ICommand CancelSequenceCommand { get; }
        public ICommand RefreshSequencesCommand { get; }
        public ICommand ClearSeqLogsCommand { get; }
        public ICommand ApplyRecommendedConditionsCommand { get; }


        #endregion

        #region 构造函数

        public HandlerDebugViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _logService = serviceProvider.GetService<ILogService>();

            _handlerRegistry = serviceProvider.GetService<IHandlerRegistry>();
            _handlerExecutor = serviceProvider.GetService<HandlerExecutor>();

            _taskManager = serviceProvider.GetService<ITaskExecutionManager>();
            if (_taskManager != null)
            {
                _taskManager.TaskCompleted += OnTaskCompleted;
                _taskManager.AllTasksCompleted += OnAllTasksCompleted;
            }

            // ★ 新增：获取生产上下文并订阅状态变化事件
            _productionContext = serviceProvider.GetService<IProductionContext>();
            if (_productionContext != null)
            {
                _productionContext.StateChanged += OnProductionStateChanged;
            }


            // 初始化条件选择器命令
            ApplySelectedConditionsCommand = new RelayCommand(() => ApplySelectedConditions(), () => !IsSequenceExecuting);
            ClearSelectedConditionsCommand = new RelayCommand(() => ClearSelectedConditions(), () => !IsSequenceExecuting);
            ClearAllConditionsCommand = new RelayCommand(() => ClearAllConditions(), () => !IsSequenceExecuting);
            RefreshConditionStatusCommand = new RelayCommand(() => RefreshConditionStatus());
            ExecuteQuickActionCommand = new RelayCommand<string>(actionId => ExecuteQuickAction(actionId), _ => !IsSequenceExecuting);

            // 初始化序列执行命令
            ExecuteSequenceCommand = new AsyncRelayCommand(async () => await ExecuteSelectedSequenceAsync(), () => CanExecuteSequence);
            CancelSequenceCommand = new RelayCommand(() => CancelSequenceExecution(), () => CanCancelSequence);
            RefreshSequencesCommand = new RelayCommand(() => RefreshSequenceList());
            ClearSeqLogsCommand = new RelayCommand(() => SeqExecutionLogs.Clear());
            ApplyRecommendedConditionsCommand = new RelayCommand(() => ApplyRecommendedConditions(), () => HasRecommendedConditions && !IsSequenceExecuting);

   

            // 初始化序列执行器
            if (_handlerExecutor != null)
            {
                InitializeSequenceExecutor();
            }

            // 初始化加载
            LoadConditionGroups();
            LoadQuickActions();
            RefreshSequenceList();
        }

        private void InitializeSequenceExecutor()
        {
            if (_handlerExecutor == null || _logService == null) return;

            _sequenceExecutor = new SequenceExecutor(_handlerExecutor, _logService);

            _sequenceExecutor.SequenceStarted += OnSequenceStarted;
            _sequenceExecutor.HandlerCompleted += OnSeqHandlerCompleted;
            _sequenceExecutor.SequenceCompleted += OnSequenceCompleted;

            foreach (var seq in PredefinedSequences.GetAll())
            {
                _sequenceExecutor.Register(seq);
            }
        }

        #endregion

        #region 公共方法

        public void RefreshSequenceList()
        {
            Sequences.Clear();

            if (_sequenceExecutor == null)
            {
                AddSeqLog("⚠️ 序列执行器未初始化");
                return;
            }

            foreach (var seq in _sequenceExecutor.Sequences.OrderBy(s => s.SequenceId != "FullTest").ThenBy(s => s.SequenceId))
            {
                Sequences.Add(new SequenceItem
                {
                    SequenceId = seq.SequenceId,
                    SequenceName = seq.SequenceName,
                    Description = seq.Description,
                    HandlerIds = seq.ExpectedHandlers?.Select(h => h.HandlerId).ToList() ?? new List<string>(),
                    DefaultConditionIds = seq.DefaultConditionIds ?? new List<string>(),
                    ExecutionMode = seq.ExecutionMode,
                    Timeout = seq.Timeout,
                    AllowPartialSuccess = seq.AllowPartialSuccess
                });
            }

            AddSeqLog($"✓ 已加载 {Sequences.Count} 个复合操作");

            if (Sequences.Count > 0 && SelectedSequence == null)
            {
                SelectedSequence = Sequences[0];
            }
        }

        #endregion

        #region 私有方法 - 条件选择器

        private void LoadConditionGroups()
        {
            ConditionGroups.Clear();

            try
            {
                var groups = ConditionCatalog.GetGroups();
                foreach (var groupName in groups)
                {
                    var conditions = ConditionCatalog.GetByGroup(groupName);
                    var group = new ConditionGroupViewModel
                    {
                        GroupName = groupName,
                        Conditions = new ObservableCollection<ConditionSelectItem>(
                            conditions.Select(c => new ConditionSelectItem
                            {
                                ConditionId = c.ConditionId,
                                DisplayName = c.DisplayName,
                                Description = c.Description,
                                RelatedHandlerId = c.RelatedHandlerId,
                                IsActive = c.CheckAction?.Invoke() ?? false
                            }))
                    };
                    ConditionGroups.Add(group);
                }

                ConditionStatusText = $"已加载 {ConditionGroups.Sum(g => g.Conditions.Count)} 个条件";
            }
            catch (Exception ex)
            {
                ConditionStatusText = $"加载条件失败: {ex.Message}";
            }
        }

        private void LoadQuickActions()
        {
            QuickActions.Clear();

            foreach (var action in QuickActionCatalog.GetAll())
            {
                QuickActions.Add(new QuickActionItem
                {
                    ActionId = action.ActionId,
                    DisplayName = action.DisplayName,
                    Description = action.Description,
                    ConditionIds = action.ConditionIds,
                    ExpectedHandlerIds = action.ExpectedHandlerIds
                });
            }
        }

        private void ApplySelectedConditions()
        {
            var selectedIds = GetSelectedConditionIds();
            if (selectedIds.Count == 0)
            {
                ConditionStatusText = "请先选择条件";
                return;
            }

            foreach (var id in selectedIds)
            {
                ConditionCatalog.Apply(id);
            }

            RefreshConditionStatus();
            ConditionStatusText = $"已应用 {selectedIds.Count} 个条件";
            AddSeqLog($"✓ 已应用条件: {string.Join(", ", selectedIds)}");
        }

        private void ClearSelectedConditions()
        {
            var selectedIds = GetSelectedConditionIds();
            if (selectedIds.Count == 0)
            {
                ConditionStatusText = "请先选择条件";
                return;
            }

            foreach (var id in selectedIds)
            {
                ConditionCatalog.Clear(id);
            }

            RefreshConditionStatus();
            ConditionStatusText = $"已清除 {selectedIds.Count} 个条件";
            AddSeqLog($"✗ 已清除条件: {string.Join(", ", selectedIds)}");
        }

        private void ClearAllConditions()
        {
            ConditionCatalog.ClearAll();
            RefreshConditionStatus();
            ConditionStatusText = "已清除所有条件";
            AddSeqLog("✗ 已清除所有条件");
        }

        private void RefreshConditionStatus()
        {
            foreach (var group in ConditionGroups)
            {
                foreach (var condition in group.Conditions)
                {
                    condition.IsActive = ConditionCatalog.Check(condition.ConditionId);
                }
            }

            OnPropertyChanged(nameof(SelectedConditionCount));
        }

        private List<string> GetSelectedConditionIds()
        {
            return ConditionGroups
                .SelectMany(g => g.Conditions)
                .Where(c => c.IsSelected)
                .Select(c => c.ConditionId)
                .ToList();
        }

        private void ExecuteQuickAction(string actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return;

            var action = QuickActionCatalog.Get(actionId);
            if (action == null)
            {
                ConditionStatusText = $"未找到快捷操作: {actionId}";
                return;
            }

            action.Apply();
            RefreshConditionStatus();
            ConditionStatusText = $"已执行: {action.DisplayName}";
            AddSeqLog($"⚡ 快捷操作: {action.DisplayName}");
        }

        #endregion

        #region 私有方法 - 序列执行

        private async Task ExecuteSelectedSequenceAsync()
        {
            if (SelectedSequence == null || _sequenceExecutor == null) return;

            // 启用调试模式，不改变真实生产状态
            bool previousDebugMode = _handlerExecutor?.IsDebugMode ?? false;
            if (_handlerExecutor != null)
            {
                _handlerExecutor.IsDebugMode = true;
                AddSeqLog("ℹ️ [调试] 已启用调试模式（不改变生产状态）");
            }

            // ★ 新增：真正应用缓存的条件
            if (_pendingConditionIds.Count > 0)
            {
                AddSeqLog($"⚡ 正在应用 {_pendingConditionIds.Count} 个预设条件...");
                foreach (var conditionId in _pendingConditionIds)
                {
                    ConditionCatalog.Apply(conditionId);
                }
                AddSeqLog($"✓ 条件已生效: {string.Join(", ", _pendingConditionIds)}");
                _pendingConditionIds.Clear();
            }


            IsSequenceExecuting = true;
            SeqProgressPercent = 0;
            SeqStatusText = $"正在执行: {SelectedSequence.SequenceName}";
            _seqCurrentCts = new CancellationTokenSource();

            foreach (var item in ProgressItems)
            {
                item.Status = HandlerProgressStatus.Pending;
                item.Duration = TimeSpan.Zero;
            }

            AddSeqLog($"▶ 开始执行序列: {SelectedSequence.SequenceId}");

            

            try
            {
                var result = await _sequenceExecutor.ExecuteAsync(SelectedSequence.SequenceId, _seqCurrentCts.Token);

                if (result.Success)
                {
                    AddSeqLog($"✓ 序列执行成功: {result.Message}");
                    SeqStatusText = $"完成: {result.Message}";
                }
                else
                {
                    AddSeqLog($"✗ 序列执行失败: {result.Message}");
                    SeqStatusText = $"失败: {result.Message}";
                }

                AddSeqLog($"  总耗时: {result.Duration.TotalSeconds:F2}秒");
            }
            catch (OperationCanceledException)
            {
                AddSeqLog("⏹ 序列执行已取消");
                SeqStatusText = "已取消";
            }
            catch (Exception ex)
            {
                AddSeqLog($"❌ 序列执行异常: {ex.Message}");
                SeqStatusText = $"异常: {ex.Message}";
            }
            finally
            {
                // 恢复调试模式状态
                if (_handlerExecutor != null)
                {
                    _handlerExecutor.IsDebugMode = previousDebugMode;
                    AddSeqLog("ℹ️ [调试] 已恢复调试模式状态");
                }

                IsSequenceExecuting = false;
                _seqCurrentCts?.Dispose();
                _seqCurrentCts = null;
            }
        }

        private void CancelSequenceExecution()
        {
            // 1. 取消序列执行（如果正在执行）
            if (_seqCurrentCts != null && !_seqCurrentCts.IsCancellationRequested)
            {
                _seqCurrentCts.Cancel();
                _sequenceExecutor?.Cancel();
                AddSeqLog("⏹ 正在取消序列执行...");
            }

            // 2. ★ 触发系统停止（如果系统正在运行）
            // 独立判断，不依赖 _seqCurrentCts 状态
            if (_productionContext?.IsRunning ?? false)
            {
                FlagCondition.SetFlag("System_Stop_Request", true);
                AddSeqLog("ℹ️ 已触发系统停止流程");
            }
        }

        /// <summary>
        /// ★ 修改：清理机器人连接和AR程序
        /// 
        /// 执行顺序：
        /// 1. 停止AR程序
        /// 2. 断开伺服使能
        /// 3. 停止TCP服务器
        /// 4. 清理所有Flag条件
        /// 
        /// 注意：不断开Modbus连接（由DeviceManager管理）
        /// </summary>
        private async void CleanupRobotTcpConnection()
        {
            try
            {
                var deviceManager = _serviceProvider?.GetService<IDeviceManager>();
                var robot = deviceManager?.Get<IRobotDevice>("Robot");

                if (robot == null)
                {
                    AddSeqLog("ℹ️ 机器人设备未注册，跳过清理");
                    return;
                }

                if (!robot.IsConnected)
                {
                    AddSeqLog("ℹ️ 机器人未连接，跳过清理");
                    // 即使未连接，也清理Flag
                    robot.ClearAllRobotFlags();
                    return;
                }

                AddSeqLog("ℹ️ 正在停止AR程序并清理资源...");

                // 使用统一的清理方法
                var result = await robot.StopAndCleanupAsync();

                if (result)
                {
                    AddSeqLog("✅ AR程序已停止，资源已清理");
                }
                else
                {
                    AddSeqLog("⚠️ 清理过程中部分操作失败，请检查机器人状态");
                }
            }
            catch (Exception ex)
            {
                AddSeqLog($"⚠️ 清理设备连接失败: {ex.Message}");

                // 异常情况下也尝试清理Flag
                try
                {
                    var deviceManager = _serviceProvider?.GetService<IDeviceManager>();
                    var robot = deviceManager?.Get<IRobotDevice>("Robot");
                    robot?.ClearAllRobotFlags();
                }
                catch { }
            }
        }

        private void UpdateProgressItems()
        {
            ProgressItems.Clear();

            if (SelectedSequence == null) return;

            foreach (var handlerId in SelectedSequence.HandlerIds)
            {
                ProgressItems.Add(new HandlerProgressItem
                {
                    HandlerId = handlerId,
                    Status = HandlerProgressStatus.Pending
                });
            }
        }

        /// <summary>
        /// 更新推荐条件列表
        /// </summary>
        private void UpdateRecommendedConditions()
        {
            RecommendedConditions.Clear();

            if (SelectedSequence == null || SelectedSequence.DefaultConditionIds == null)
            {
                OnPropertyChanged(nameof(HasRecommendedConditions));
                return;
            }

            foreach (var conditionId in SelectedSequence.DefaultConditionIds)
            {
                var condition = ConditionCatalog.Get(conditionId);
                if (condition != null)
                {
                    RecommendedConditions.Add(new RecommendedConditionItem
                    {
                        ConditionId = condition.ConditionId,
                        DisplayName = condition.DisplayName,
                        Description = condition.Description,
                        Group = condition.Group,
                        IsActive = condition.CheckAction?.Invoke() ?? false
                    });
                }
            }

            OnPropertyChanged(nameof(HasRecommendedConditions));
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// 应用推荐条件
        /// </summary>
        private void ApplyRecommendedConditions()
        {
            if (SelectedSequence == null || !HasRecommendedConditions) return;

            // ★ 改为：只缓存条件ID，不立即设置Flag
            _pendingConditionIds.Clear();

            foreach (var item in RecommendedConditions)
            {
                _pendingConditionIds.Add(item.ConditionId);
                item.IsActive = true;  // UI显示为"已预设"状态
            }

            ConditionStatusText = $"已预设 {_pendingConditionIds.Count} 个条件（点击一键执行生效）";
            AddSeqLog($"✓ 已预设条件: {string.Join(", ", _pendingConditionIds)}");

            // 不调用 RefreshConditionStatus()，因为Flag还没真正设置
        }

        private void OnSequenceStarted(object sender, SequenceStartedEventArgs e)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                foreach (var item in ProgressItems)
                {
                    item.Status = HandlerProgressStatus.Running;
                }

                AddSeqLog($"  → 已启动 {e.ExpectedHandlers?.Count ?? 0} 个Handler执行");
            });
        }
        /// <summary>
        /// ★ 新增：生产状态变化事件处理
        /// 当系统启动/停止时刷新按钮状态
        /// </summary>
        private void OnProductionStateChanged(object sender, ProductionStateChangedEventArgs e)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                // 刷新按钮可用状态
                OnPropertyChanged(nameof(CanExecuteSequence));
                OnPropertyChanged(nameof(CanCancelSequence));
                CommandManager.InvalidateRequerySuggested();

                // 可选：记录状态变化日志
                if (e.IsRunning && !e.WasRunning)
                {
                    AddSeqLog("ℹ️ 系统已启动运行");
                }
                else if (!e.IsRunning && e.WasRunning)
                {
                    AddSeqLog("ℹ️ 系统已停止运行");
                }
            });
        }

        private void OnSeqHandlerCompleted(object sender, HandlerCompletedEventArgs e)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var item = ProgressItems.FirstOrDefault(p => p.HandlerId == e.Result.HandlerId);
                if (item != null)
                {
                    item.Status = e.Result.Success ? HandlerProgressStatus.Success : HandlerProgressStatus.Failed;
                    item.Duration = e.Result.Duration;
                }

                var completedCount = ProgressItems.Count(p => p.Status == HandlerProgressStatus.Success || p.Status == HandlerProgressStatus.Failed);
                SeqProgressPercent = ProgressItems.Count > 0 ? (completedCount * 100 / ProgressItems.Count) : 0;

                var statusIcon = e.Result.Success ? "✓" : "✗";
                AddSeqLog($"  {statusIcon} [{e.Result.HandlerId}] {e.Result.Message} ({e.Result.Duration.TotalMilliseconds:F0}ms)");
            });
        }

        private void OnTaskCompleted(object sender, TaskCompletedEventArgs e)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                OnPropertyChanged(nameof(CanExecuteSequence));
                OnPropertyChanged(nameof(CanCancelSequence));
                CommandManager.InvalidateRequerySuggested();
            });
        }

        private void OnAllTasksCompleted(object sender, EventArgs e)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                OnPropertyChanged(nameof(CanExecuteSequence));
                OnPropertyChanged(nameof(CanCancelSequence));
                CommandManager.InvalidateRequerySuggested();
            });
        }

        private void OnSequenceCompleted(object sender, SequenceCompletedEventArgs e)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                SeqProgressPercent = 100;
            });
        }

        private void AddSeqLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}";

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                SeqExecutionLogs.Insert(0, logEntry);

                while (SeqExecutionLogs.Count > 200)
                {
                    SeqExecutionLogs.RemoveAt(SeqExecutionLogs.Count - 1);
                }
            });
        }

        #endregion

        #region ★ 新增：条件诊断日志输出

        /// <summary>
        /// ★ 输出所有Handler的条件诊断到日志
        /// </summary>
        private void ShowConditionDiagnostics()
        {
            if (_handlerExecutor == null)
            {
                AddSeqLog("⚠️ HandlerExecutor 未初始化");
                return;
            }

            try
            {
                AddSeqLog("═══════════ 条件诊断 ═══════════");

                var evaluations = _handlerExecutor.GetAllConditionEvaluations();

                // 按优先级排序
                var sorted = evaluations
                    .OrderByDescending(e => GetHandlerPriority(e.HandlerId))
                    .ToList();

                // 分组输出
                var triggered = sorted.Where(e => e.Result.IsSatisfied).ToList();
                var blocked = sorted.Where(e => !e.Result.IsSatisfied).ToList();

                if (triggered.Any())
                {
                    AddSeqLog("【将触发】");
                    foreach (var eval in triggered)
                    {
                        OutputHandlerDiagnostics(eval);
                    }
                }

                if (blocked.Any())
                {
                    AddSeqLog("【未触发】");
                    foreach (var eval in blocked)
                    {
                        OutputHandlerDiagnostics(eval);
                    }
                }

                AddSeqLog($"═══════════ 共 {evaluations.Count} 个Handler ═══════════");
            }
            catch (Exception ex)
            {
                AddSeqLog($"⚠️ 诊断失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 输出单个Handler的条件诊断树
        /// </summary>
        private void OutputHandlerDiagnostics(HandlerEvaluationInfo eval)
        {
            var status = eval.Result.IsSatisfied ? "✓" : "✗";
            var statusText = eval.Result.IsSatisfied ? "触发" : "未触发";
            AddSeqLog($"[{eval.HandlerId}] ({status} {statusText})");

            // 输出条件树
            OutputConditionTree(eval.Result, "  ", true);
        }

        /// <summary>
        /// 递归输出条件树
        /// </summary>
        private void OutputConditionTree(ConditionEvaluationResult result, string indent, bool isLast)
        {
            var prefix = isLast ? "└─" : "├─";
            var childIndent = indent + (isLast ? "   " : "│  ");
            var status = result.IsSatisfied ? "✓" : "✗";

            if (result.IsComposite)
            {
                // 复合条件 (And/Or)
                var satisfied = result.SatisfiedChildCount;
                var total = result.TotalChildCount;
                AddSeqLog($"{indent}{prefix} [{status}] {result.ConditionType} ({satisfied}/{total})");

                // 递归输出子条件
                for (int i = 0; i < result.Children.Count; i++)
                {
                    var child = result.Children[i];
                    var isChildLast = (i == result.Children.Count - 1);

                    // 标记触发原因或阻塞原因
                    var annotation = GetConditionAnnotation(result, child, i);
                    OutputConditionTreeWithAnnotation(child, childIndent, isChildLast, annotation);
                }
            }
            else
            {
                // 简单条件
                var desc = result.Description;
                if (!string.IsNullOrEmpty(result.ExpectedValue) && result.ExpectedValue != "N/A")
                {
                    AddSeqLog($"{indent}{prefix} [{status}] {desc}");
                    AddSeqLog($"{indent}      期望: {result.ExpectedValue}, 实际: {result.ActualValue}");
                }
                else
                {
                    AddSeqLog($"{indent}{prefix} [{status}] {desc}");
                }
            }
        }

        /// <summary>
        /// 带注解的条件树输出
        /// </summary>
        private void OutputConditionTreeWithAnnotation(ConditionEvaluationResult result, string indent, bool isLast, string annotation)
        {
            var prefix = isLast ? "└─" : "├─";
            var childIndent = indent + (isLast ? "   " : "│  ");
            var status = result.IsSatisfied ? "✓" : "✗";

            if (result.IsComposite)
            {
                var satisfied = result.SatisfiedChildCount;
                var total = result.TotalChildCount;
                AddSeqLog($"{indent}{prefix} [{status}] {result.ConditionType} ({satisfied}/{total}){annotation}");

                for (int i = 0; i < result.Children.Count; i++)
                {
                    var child = result.Children[i];
                    var isChildLast = (i == result.Children.Count - 1);
                    var childAnnotation = GetConditionAnnotation(result, child, i);
                    OutputConditionTreeWithAnnotation(child, childIndent, isChildLast, childAnnotation);
                }
            }
            else
            {
                var desc = result.Description;
                if (!string.IsNullOrEmpty(result.ExpectedValue) && result.ExpectedValue != "N/A")
                {
                    AddSeqLog($"{indent}{prefix} [{status}] {desc}{annotation}");
                    AddSeqLog($"{indent}      期望: {result.ExpectedValue}, 实际: {result.ActualValue}");
                }
                else
                {
                    AddSeqLog($"{indent}{prefix} [{status}] {desc}{annotation}");
                }
            }
        }

        /// <summary>
        /// 获取条件注解（触发原因/阻塞）
        /// </summary>
        private string GetConditionAnnotation(ConditionEvaluationResult parent, ConditionEvaluationResult child, int index)
        {
            // Or条件中第一个满足的标记为触发原因
            if ((parent.ConditionType == "Or" || parent.ConditionType == "Any") && child.IsSatisfied)
            {
                // 检查是否是第一个满足的
                bool isFirstSatisfied = true;
                for (int i = 0; i < index; i++)
                {
                    if (parent.Children[i].IsSatisfied)
                    {
                        isFirstSatisfied = false;
                        break;
                    }
                }
                if (isFirstSatisfied)
                    return " ← 触发原因";
            }

            // And条件中不满足的标记为阻塞
            if ((parent.ConditionType == "And" || parent.ConditionType == "All") && !child.IsSatisfied)
            {
                // 检查是否是第一个不满足的
                bool isFirstBlocked = true;
                for (int i = 0; i < index; i++)
                {
                    if (!parent.Children[i].IsSatisfied)
                    {
                        isFirstBlocked = false;
                        break;
                    }
                }
                if (isFirstBlocked)
                    return " ← 阻塞";
            }

            return "";
        }

        /// <summary>
        /// 获取Handler优先级
        /// </summary>
        private int GetHandlerPriority(string handlerId)
        {
            var handler = _handlerRegistry?.Get(handlerId);
            return handler?.Priority ?? 0;
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {

            if (_sequenceExecutor != null)
            {
                _sequenceExecutor.SequenceStarted -= OnSequenceStarted;
                _sequenceExecutor.HandlerCompleted -= OnSeqHandlerCompleted;
                _sequenceExecutor.SequenceCompleted -= OnSequenceCompleted;
                _sequenceExecutor.Dispose();
            }
            if (_taskManager != null)
            {
                _taskManager.TaskCompleted -= OnTaskCompleted;
                _taskManager.AllTasksCompleted -= OnAllTasksCompleted;
            }
            // ★ 新增：取消生产上下文事件订阅
            if (_productionContext != null)
            {
                _productionContext.StateChanged -= OnProductionStateChanged;
            }

            _seqCurrentCts?.Cancel();
            _seqCurrentCts?.Dispose();

            // ★ 新增：确保 TCP 连接被清理
            CleanupRobotTcpConnection();
        }

        #endregion
    }

    #region 辅助类 - 条件选择器

    public class ConditionGroupViewModel : INotifyPropertyChanged
    {
        private bool _isExpanded = true;

        public string GroupName { get; set; }

        public ObservableCollection<ConditionSelectItem> Conditions { get; set; }
            = new ObservableCollection<ConditionSelectItem>();

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class ConditionSelectItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isActive;

        public string ConditionId { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string RelatedHandlerId { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusIndicator));
                }
            }
        }

        public string StatusIndicator => IsActive ? "●" : "○";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class QuickActionItem
    {
        public string ActionId { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public List<string> ConditionIds { get; set; } = new List<string>();
        public List<string> ExpectedHandlerIds { get; set; } = new List<string>();

        public string ConditionsDisplay => string.Join(", ", ConditionIds);
        public string HandlersDisplay => string.Join(", ", ExpectedHandlerIds);
    }

    #endregion

    #region 辅助类 - 序列执行

    public class SequenceItem : INotifyPropertyChanged
    {
        public string SequenceId { get; set; }
        public string SequenceName { get; set; }
        public string Description { get; set; }
        public List<string> HandlerIds { get; set; } = new List<string>();
        public List<string> DefaultConditionIds { get; set; } = new List<string>();
        public SequenceExecutionMode ExecutionMode { get; set; }
        public TimeSpan Timeout { get; set; }
        public bool AllowPartialSuccess { get; set; }

        public string DisplayName => $"{SequenceId} - {SequenceName}";

        public int HandlerCount => HandlerIds?.Count ?? 0;

        public string HandlerList => HandlerIds != null ? string.Join(", ", HandlerIds) : "";

        /// <summary>
        /// 是否有推荐条件
        /// </summary>
        public bool HasDefaultConditions => DefaultConditionIds != null && DefaultConditionIds.Count > 0;

        /// <summary>
        /// 执行模式显示文本
        /// </summary>
        public string ExecutionModeText
        {
            get
            {
                switch (ExecutionMode)
                {
                    case SequenceExecutionMode.SingleRun: return "单次执行";
                    case SequenceExecutionMode.Loop: return "循环执行";
                    default: return "正常模式";
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum HandlerProgressStatus
    {
        Pending,
        Running,
        Success,
        Failed
    }

    public class HandlerProgressItem : INotifyPropertyChanged
    {
        private HandlerProgressStatus _status;
        private TimeSpan _duration;

        public string HandlerId { get; set; }

        public HandlerProgressStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public TimeSpan Duration
        {
            get => _duration;
            set
            {
                if (_duration != value)
                {
                    _duration = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DurationText));
                }
            }
        }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case HandlerProgressStatus.Pending: return "等待中";
                    case HandlerProgressStatus.Running: return "运行中...";
                    case HandlerProgressStatus.Success: return "成功";
                    case HandlerProgressStatus.Failed: return "失败";
                    default: return "未知";
                }
            }
        }

        public Color StatusColor
        {
            get
            {
                switch (Status)
                {
                    case HandlerProgressStatus.Pending: return Colors.Gray;
                    case HandlerProgressStatus.Running: return Colors.Orange;
                    case HandlerProgressStatus.Success: return Colors.LimeGreen;
                    case HandlerProgressStatus.Failed: return Colors.Red;
                    default: return Colors.Gray;
                }
            }
        }

        public string DurationText
        {
            get
            {
                if (Duration == TimeSpan.Zero && Status != HandlerProgressStatus.Success && Status != HandlerProgressStatus.Failed)
                    return "-";
                return $"{Duration.TotalMilliseconds:F0}ms";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 推荐条件项
    /// </summary>
    public class RecommendedConditionItem : INotifyPropertyChanged
    {
        private bool _isActive;

        public string ConditionId { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Group { get; set; }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusIndicator));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public string StatusIndicator => IsActive ? "●" : "○";

        public Color StatusColor => IsActive ? Colors.LimeGreen : Colors.Gray;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    #endregion
}