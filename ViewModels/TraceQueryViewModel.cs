using SeedCut.Framework.Services.Traceability;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// 追溯查询视图模型
    /// </summary>
    public class TraceQueryViewModel : INotifyPropertyChanged
    {
        #region 私有字段

        private readonly ITraceService _traceService;

        private string _searchKeyword;
        private DateTime _startDate = DateTime.Today;
        private DateTime _endDate = DateTime.Today.AddDays(1);
        private bool _onlyDuplicates;
        private bool _isLoading;
        private string _statusMessage;

        private TrayPairing _selectedPairing;
        private TraceStatistics _todayStatistics;

        #endregion

        #region 构造函数

        public TraceQueryViewModel(ITraceService traceService)
        {
            _traceService = traceService ?? throw new ArgumentNullException(nameof(traceService));

            // 初始化集合
            QueryResults = new ObservableCollection<TrayPairing>();
            DuplicateList = new ObservableCollection<DuplicateInfo>();

            // 初始化命令 - 使用AsyncRelayCommand处理异步操作
            SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsLoading);
            SearchByTimeRangeCommand = new AsyncRelayCommand(SearchByTimeRangeAsync, () => !IsLoading);
            ViewDuplicatesCommand = new AsyncRelayCommand(LoadDuplicatesAsync, () => !IsLoading);
            RefreshStatisticsCommand = new AsyncRelayCommand(RefreshStatisticsAsync, () => !IsLoading);
            ExportCommand = new RelayCommand(Export, () => QueryResults.Count > 0);
            ClearCommand = new RelayCommand(ClearResults);

            // 加载今日统计
            _ = RefreshStatisticsAsync();
        }

        /// <summary>
        /// 设计时构造函数（用于XAML预览）
        /// </summary>
        public TraceQueryViewModel()
        {
            QueryResults = new ObservableCollection<TrayPairing>();
            DuplicateList = new ObservableCollection<DuplicateInfo>();

            // 设计时数据
            TodayStatistics = new TraceStatistics
            {
                TotalPairings = 1234,
                DuplicateCount = 3,
                StatDate = DateTime.Today
            };
        }

        #endregion

        #region 属性

        /// <summary>
        /// 搜索关键词（条码或追溯号）
        /// </summary>
        public string SearchKeyword
        {
            get => _searchKeyword;
            set => SetProperty(ref _searchKeyword, value);
        }

        /// <summary>
        /// 开始日期
        /// </summary>
        public DateTime StartDate
        {
            get => _startDate;
            set => SetProperty(ref _startDate, value);
        }

        /// <summary>
        /// 结束日期
        /// </summary>
        public DateTime EndDate
        {
            get => _endDate;
            set => SetProperty(ref _endDate, value);
        }

        /// <summary>
        /// 仅显示重复记录
        /// </summary>
        public bool OnlyDuplicates
        {
            get => _onlyDuplicates;
            set => SetProperty(ref _onlyDuplicates, value);
        }

        /// <summary>
        /// 是否正在加载
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
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
        /// 查询结果列表
        /// </summary>
        public ObservableCollection<TrayPairing> QueryResults { get; }

        /// <summary>
        /// 重复条码列表
        /// </summary>
        public ObservableCollection<DuplicateInfo> DuplicateList { get; }

        /// <summary>
        /// 选中的配对记录
        /// </summary>
        public TrayPairing SelectedPairing
        {
            get => _selectedPairing;
            set => SetProperty(ref _selectedPairing, value);
        }

        /// <summary>
        /// 今日统计
        /// </summary>
        public TraceStatistics TodayStatistics
        {
            get => _todayStatistics;
            set => SetProperty(ref _todayStatistics, value);
        }

        #endregion

        #region 命令

        public ICommand SearchCommand { get; }
        public ICommand SearchByTimeRangeCommand { get; }
        public ICommand ViewDuplicatesCommand { get; }
        public ICommand RefreshStatisticsCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ClearCommand { get; }

        #endregion

        #region 方法

        /// <summary>
        /// 按关键词搜索
        /// </summary>
        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchKeyword))
            {
                StatusMessage = "请输入条码或追溯号";
                return;
            }

            if (_traceService == null)
            {
                StatusMessage = "追溯服务不可用";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = "正在查询...";

                var results = await _traceService.QueryAsync(SearchKeyword.Trim());

                QueryResults.Clear();
                foreach (var item in results)
                {
                    QueryResults.Add(item);
                }

                StatusMessage = $"找到 {results.Count} 条记录";

                if (results.Count == 1)
                {
                    SelectedPairing = results[0];
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"查询失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 按时间范围搜索
        /// </summary>
        private async Task SearchByTimeRangeAsync()
        {
            if (_traceService == null)
            {
                StatusMessage = "追溯服务不可用";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = "正在查询...";

                var results = await _traceService.QueryByTimeRangeAsync(
                    StartDate,
                    EndDate,
                    OnlyDuplicates);

                QueryResults.Clear();
                foreach (var item in results)
                {
                    QueryResults.Add(item);
                }

                StatusMessage = $"找到 {results.Count} 条记录";
            }
            catch (Exception ex)
            {
                StatusMessage = $"查询失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 加载重复条码列表
        /// </summary>
        private async Task LoadDuplicatesAsync()
        {
            if (_traceService == null)
            {
                StatusMessage = "追溯服务不可用";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = "正在加载重复记录...";

                var duplicates = await _traceService.GetDuplicatesAsync();

                DuplicateList.Clear();
                foreach (var item in duplicates)
                {
                    DuplicateList.Add(item);
                }

                StatusMessage = $"今日重复条码: {duplicates.Count} 个";
            }
            catch (Exception ex)
            {
                StatusMessage = $"加载失败: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 刷新统计信息
        /// </summary>
        private async Task RefreshStatisticsAsync()
        {
            if (_traceService == null) return;

            try
            {
                TodayStatistics = await _traceService.GetTodayStatisticsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"刷新统计失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 导出数据
        /// </summary>
        private void Export()
        {
            // TODO: 实现导出功能
            MessageBox.Show("导出功能暂未实现", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 清除结果
        /// </summary>
        private void ClearResults()
        {
            QueryResults.Clear();
            SelectedPairing = null;
            SearchKeyword = string.Empty;
            StatusMessage = string.Empty;
        }

        /// <summary>
        /// 按条码查询（用于从重复列表点击跳转）
        /// </summary>
        public async Task SearchByBarcodeAsync(string barcode)
        {
            SearchKeyword = barcode;
            await SearchAsync();
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}