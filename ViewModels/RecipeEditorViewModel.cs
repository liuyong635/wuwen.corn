using SeedCut.Framework.Models;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace SeedCut.ViewModels.Recipe
{
    /// <summary>
    /// 配方编辑器主ViewModel
    /// </summary>
    public class RecipeEditorViewModel : INotifyPropertyChanged
    {
        #region 私有字段

        private readonly IRecipeService _recipeService;
        private string _selectedRecipeName;
        private RecipeGroupViewModel _selectedGroup;
        private string _statusMessage;

        #endregion

        #region 属性

        /// <summary>
        /// 可用配方列表
        /// </summary>
        public ObservableCollection<string> AvailableRecipes { get; }

        /// <summary>
        /// 当前选中的配方名称
        /// </summary>
        public string SelectedRecipeName
        {
            get => _selectedRecipeName;
            set
            {
                if (_selectedRecipeName != value)
                {
                    _selectedRecipeName = value;
                    OnPropertyChanged();
                    SwitchRecipe(value);
                }
            }
        }

        /// <summary>
        /// 分组列表
        /// </summary>
        public ObservableCollection<RecipeGroupViewModel> Groups { get; }

        /// <summary>
        /// 当前选中的分组
        /// </summary>
        public RecipeGroupViewModel SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (_selectedGroup != value)
                {
                    _selectedGroup = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSelectedGroup));
                }
            }
        }

        /// <summary>
        /// 是否有选中的分组
        /// </summary>
        public bool HasSelectedGroup => SelectedGroup != null;

        /// <summary>
        /// 是否有未保存的修改
        /// </summary>
        public bool HasUnsavedChanges => _recipeService?.HasUnsavedChanges ?? false;

        /// <summary>
        /// 状态消息
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 新配方名称（用于创建配方对话框）
        /// </summary>
        public string NewRecipeName { get; set; }

        #endregion

        #region 命令

        public ICommand SaveCommand { get; }
        public ICommand DiscardCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand CreateRecipeCommand { get; }
        public ICommand DeleteRecipeCommand { get; }

        #endregion

        #region 构造函数

        public RecipeEditorViewModel(IRecipeService recipeService)
        {
            _recipeService = recipeService ?? throw new ArgumentNullException(nameof(recipeService));

            AvailableRecipes = new ObservableCollection<string>();
            Groups = new ObservableCollection<RecipeGroupViewModel>();

            // 初始化命令
            SaveCommand = new RelayCommand(ExecuteSave, CanExecuteSave);
            DiscardCommand = new RelayCommand(ExecuteDiscard, CanExecuteDiscard);
            RefreshCommand = new RelayCommand(ExecuteRefresh);
            CreateRecipeCommand = new RelayCommand(ExecuteCreateRecipe, CanExecuteCreateRecipe);
            DeleteRecipeCommand = new RelayCommand(ExecuteDeleteRecipe, CanExecuteDeleteRecipe);

            // 订阅事件
            _recipeService.ParameterAutoCreated += OnParameterAutoCreated;
            _recipeService.ParameterModified += OnParameterModified;
            _recipeService.RecipeChanged += OnRecipeChanged;

            // 加载数据
            LoadRecipeList();
            LoadGroups();

            _selectedRecipeName = _recipeService.CurrentRecipeName;
            UpdateStatus("已加载配方: " + _selectedRecipeName);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 刷新界面（外部调用）
        /// </summary>
        public void Refresh()
        {
            LoadRecipeList();
            LoadGroups();
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 加载配方列表
        /// </summary>
        private void LoadRecipeList()
        {
            AvailableRecipes.Clear();
            foreach (var recipe in _recipeService.AvailableRecipes)
            {
                AvailableRecipes.Add(recipe);
            }
        }

        /// <summary>
        /// 加载分组列表
        /// </summary>
        private void LoadGroups()
        {
            Groups.Clear();
            foreach (var group in _recipeService.GetAllGroups())
            {
                Groups.Add(new RecipeGroupViewModel(group, _recipeService));
            }

            // 默认选中第一个分组
            if (Groups.Count > 0 && SelectedGroup == null)
            {
                SelectedGroup = Groups[0];
            }
        }

        /// <summary>
        /// 切换配方
        /// </summary>
        private void SwitchRecipe(string recipeName)
        {
            if (string.IsNullOrWhiteSpace(recipeName)) return;

            if (_recipeService.HasUnsavedChanges)
            {
                var result = MessageBox.Show(
                    "当前配方有未保存的修改，是否放弃修改并切换？",
                    "确认",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    // 恢复选择
                    _selectedRecipeName = _recipeService.CurrentRecipeName;
                    OnPropertyChanged(nameof(SelectedRecipeName));
                    return;
                }
            }

            if (_recipeService.SwitchRecipe(recipeName))
            {
                LoadGroups();
                UpdateStatus("已切换到配方: " + recipeName);
            }
            else
            {
                UpdateStatus("切换配方失败: " + recipeName);
            }

            OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        /// <summary>
        /// 更新状态消息
        /// </summary>
        private void UpdateStatus(string message)
        {
            StatusMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
        }

        #endregion

        #region 命令实现

        private void ExecuteSave(object parameter)
        {
            if (_recipeService.Save())
            {
                UpdateStatus("保存成功");
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
            else
            {
                UpdateStatus("保存失败");
                MessageBox.Show("保存配方失败，请检查文件权限。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanExecuteSave(object parameter)
        {
            return _recipeService?.HasUnsavedChanges ?? false;
        }

        private void ExecuteDiscard(object parameter)
        {
            var result = MessageBox.Show(
                "确定要放弃所有未保存的修改吗？",
                "确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _recipeService.DiscardChanges();
                LoadGroups();
                UpdateStatus("已放弃修改");
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        }

        private bool CanExecuteDiscard(object parameter)
        {
            return _recipeService?.HasUnsavedChanges ?? false;
        }

        private void ExecuteRefresh(object parameter)
        {
            _recipeService.RefreshRecipeList();
            LoadRecipeList();
            LoadGroups();
            UpdateStatus("已刷新");
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        private void ExecuteCreateRecipe(object parameter)
        {
            var name = parameter as string ?? NewRecipeName;
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("请输入配方名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_recipeService.CreateRecipe(name))
            {
                LoadRecipeList();
                UpdateStatus("创建配方成功: " + name);
                NewRecipeName = string.Empty;
                OnPropertyChanged(nameof(NewRecipeName));
            }
            else
            {
                MessageBox.Show("创建配方失败，名称可能已存在或包含非法字符。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool CanExecuteCreateRecipe(object parameter)
        {
            var name = parameter as string ?? NewRecipeName;
            return !string.IsNullOrWhiteSpace(name);
        }

        private void ExecuteDeleteRecipe(object parameter)
        {
            var name = parameter as string ?? SelectedRecipeName;
            if (string.IsNullOrWhiteSpace(name)) return;

            if (name.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("不允许删除Default配方", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (name.Equals(_recipeService.CurrentRecipeName, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("不允许删除当前正在使用的配方", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"确定要删除配方 [{name}] 吗？此操作不可恢复。",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                if (_recipeService.DeleteRecipe(name))
                {
                    LoadRecipeList();
                    UpdateStatus("删除配方成功: " + name);
                }
                else
                {
                    MessageBox.Show("删除配方失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private bool CanExecuteDeleteRecipe(object parameter)
        {
            var name = parameter as string ?? SelectedRecipeName;
            return !string.IsNullOrWhiteSpace(name) &&
                   !name.Equals("Default", StringComparison.OrdinalIgnoreCase) &&
                   !name.Equals(_recipeService.CurrentRecipeName, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 参数自动创建时刷新UI
        /// </summary>
        private void OnParameterAutoCreated(object sender, ParameterAutoCreatedEventArgs e)
        {
            // 在UI线程刷新
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (e.IsNewGroup)
                {
                    // 新分组，重新加载分组列表
                    LoadGroups();
                }
                else
                {
                    // 已有分组，只刷新参数列表
                    var groupVm = Groups.FirstOrDefault(g =>
                        g.GroupId.Equals(e.GroupId, StringComparison.OrdinalIgnoreCase));
                    groupVm?.RefreshParameters();
                }

                OnPropertyChanged(nameof(HasUnsavedChanges));
                UpdateStatus($"自动创建参数: [{e.GroupId}].{e.Key}");
            });
        }

        /// <summary>
        /// 参数修改时更新状态
        /// </summary>
        private void OnParameterModified(object sender, ParameterModifiedEventArgs e)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                OnPropertyChanged(nameof(HasUnsavedChanges));
            });
        }

        /// <summary>
        /// 配方切换时刷新UI
        /// </summary>
        private void OnRecipeChanged(object sender, RecipeChangedEventArgs e)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                _selectedRecipeName = e.CurrentRecipeName;
                OnPropertyChanged(nameof(SelectedRecipeName));
                LoadGroups();
                OnPropertyChanged(nameof(HasUnsavedChanges));
            });
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
    /// 分组ViewModel
    /// </summary>
    public class RecipeGroupViewModel : INotifyPropertyChanged
    {
        private readonly RecipeGroup _group;
        private readonly IRecipeService _recipeService;

        public RecipeGroupViewModel(RecipeGroup group, IRecipeService recipeService)
        {
            _group = group ?? throw new ArgumentNullException(nameof(group));
            _recipeService = recipeService;

            Parameters = new ObservableCollection<RecipeParameterViewModel>();
            LoadParameters();
        }

        #region 属性

        public string GroupId => _group.GroupId;

        public string GroupName
        {
            get => _group.GroupName;
            set
            {
                if (_group.GroupName != value)
                {
                    _recipeService.UpdateGroupMeta(_group.GroupId, groupName: value);
                    OnPropertyChanged();
                }
            }
        }

        public int SortOrder
        {
            get => _group.SortOrder;
            set
            {
                if (_group.SortOrder != value)
                {
                    _recipeService.UpdateGroupMeta(_group.GroupId, sortOrder: value);
                    OnPropertyChanged();
                }
            }
        }

        public bool IsAutoCreated => _group.IsAutoCreated;

        /// <summary>
        /// 显示文本（带自动创建标记）
        /// </summary>
        public string DisplayText => IsAutoCreated ? $"{GroupName} *" : GroupName;

        public ObservableCollection<RecipeParameterViewModel> Parameters { get; }

        #endregion

        #region 方法

        private void LoadParameters()
        {
            Parameters.Clear();
            foreach (var param in _group.Parameters)
            {
                Parameters.Add(new RecipeParameterViewModel(param, _group.GroupId, _recipeService));
            }
        }

        public void RefreshParameters()
        {
            LoadParameters();
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
    /// 参数ViewModel
    /// </summary>
    public class RecipeParameterViewModel : INotifyPropertyChanged
    {
        private readonly RecipeParameter _parameter;
        private readonly string _groupId;
        private readonly IRecipeService _recipeService;

        public RecipeParameterViewModel(RecipeParameter parameter, string groupId, IRecipeService recipeService)
        {
            _parameter = parameter ?? throw new ArgumentNullException(nameof(parameter));
            _groupId = groupId;
            _recipeService = recipeService;
        }

        #region 属性

        public string Key => _parameter.Key;

        public string Name
        {
            get => _parameter.Name;
            set
            {
                if (_parameter.Name != value)
                {
                    _recipeService.UpdateParameterMeta(_groupId, _parameter.Key, name: value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(NeedMetaInput));
                }
            }
        }

        public string ValueType => _parameter.ValueType;

        public string Value
        {
            get => _parameter.Value;
            set
            {
                if (_parameter.Value != value)
                {
                    _recipeService.SetValue(_groupId, _parameter.Key, value);
                    OnPropertyChanged();
                }
            }
        }

        public string Unit
        {
            get => _parameter.Unit;
            set
            {
                if (_parameter.Unit != value)
                {
                    _recipeService.UpdateParameterMeta(_groupId, _parameter.Key, unit: value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(NeedMetaInput));
                }
            }
        }

        public string Description
        {
            get => _parameter.Description;
            set
            {
                if (_parameter.Description != value)
                {
                    _recipeService.UpdateParameterMeta(_groupId, _parameter.Key, description: value);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(NeedMetaInput));
                }
            }
        }

        /// <summary>
        /// 是否自动创建的参数（需要用户补充元数据）
        /// </summary>
        public bool IsAutoCreated => _parameter.IsAutoCreated;

        /// <summary>
        /// 是否需要用户补充元数据
        /// </summary>
        public bool NeedMetaInput => _parameter.IsAutoCreated;

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName => IsAutoCreated ? $"{Name} *" : Name;

        /// <summary>
        /// 值类型显示文本
        /// </summary>
        public string ValueTypeDisplay
        {
            get
            {
                switch (ValueType?.ToLowerInvariant())
                {
                    case "int": return "整数";
                    case "double": return "小数";
                    case "float": return "单精度";
                    case "bool": return "布尔";
                    case "string": return "字符串";
                    case "int[]": return "整数数组";
                    case "double[]": return "小数数组";
                    case "float[]": return "单精度数组";
                    default: return ValueType ?? "未知";
                }
            }
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

    #region RelayCommand

    /// <summary>
    /// 简单的命令实现
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
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
            return _canExecute?.Invoke(parameter) ?? true;
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }
    }

    #endregion
}
