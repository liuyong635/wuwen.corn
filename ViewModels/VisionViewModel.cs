using Microsoft.Win32;
using SeedCut.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// Vision 页面的 ViewModel - 集成 Vision Master 所有功能
    /// </summary>
    public class VisionViewModel : INotifyPropertyChanged
    {
        private readonly IVisionService _visionService;

        #region 私有字段

        private string _solutionPath = "未选择方案";
        private string _solutionPassword = "";
        private string _statusText = "等待加载";
        private Color _statusColor = Colors.Orange;
        private bool _isLoaded;
        private int _executeCount;
        private int _successCount;
        private int _failCount;
        private string _logText = "等待执行...\n";

        #endregion

        #region 属性

        public string SolutionPath
        {
            get => _solutionPath;
            set
            {
                if (_solutionPath != value)
                {
                    _solutionPath = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string SolutionPassword
        {
            get => _solutionPassword;
            set
            {
                if (_solutionPassword != value)
                {
                    _solutionPassword = value;
                    OnPropertyChanged();
                }
            }
        }

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

        public Color StatusColor
        {
            get => _statusColor;
            set
            {
                if (_statusColor != value)
                {
                    _statusColor = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsLoaded
        {
            get => _isLoaded;
            set
            {
                if (_isLoaded != value)
                {
                    _isLoaded = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public int ExecuteCount
        {
            get => _executeCount;
            set
            {
                if (_executeCount != value)
                {
                    _executeCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public int SuccessCount
        {
            get => _successCount;
            set
            {
                if (_successCount != value)
                {
                    _successCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public int FailCount
        {
            get => _failCount;
            set
            {
                if (_failCount != value)
                {
                    _failCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LogText
        {
            get => _logText;
            set
            {
                if (_logText != value)
                {
                    _logText = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region 命令

        public ICommand SelectFileCommand { get; }
        public ICommand LoadCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand RunTestCommand { get; }
        public ICommand GetVersionCommand { get; }
        public ICommand GetPathCommand { get; }
        public ICommand CheckPasswordCommand { get; }
        public ICommand ClearCommand { get; }

        #endregion

        #region 构造函数

        public VisionViewModel(IVisionService visionService)
        {
            Debug.WriteLine("VisionViewModel 构造函数开始");

            _visionService = visionService ?? throw new ArgumentNullException(nameof(visionService));

            // 订阅服务事件
            _visionService.LoadCompleted += OnLoadCompleted;
            _visionService.ProcessCompleted += OnProcessCompleted;

            // 初始化命令
            try
            {
                SelectFileCommand = new RelayCommand(OnSelectFile);
                Debug.WriteLine("SelectFileCommand 已创建");

                LoadCommand = new RelayCommand(OnLoad, CanLoad);
                SaveCommand = new RelayCommand(OnSave, CanSave);
                CloseCommand = new RelayCommand(OnClose, CanClose);
                RunTestCommand = new RelayCommand(OnRunTest, CanRunTest);
                GetVersionCommand = new RelayCommand(OnGetVersion, CanGetVersion);
                GetPathCommand = new RelayCommand(OnGetPath, CanGetPath);
                CheckPasswordCommand = new RelayCommand(OnCheckPassword, CanCheckPassword);
                ClearCommand = new RelayCommand(OnClear);

                Debug.WriteLine("所有命令已创建");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"命令创建失败: {ex.Message}");
                throw;
            }

            Debug.WriteLine("VisionViewModel 构造函数完成");
        }

        #endregion

        #region 命令处理

        private void OnSelectFile()
        {
            Debug.WriteLine("OnSelectFile 被调用");
            AddLog("[DEBUG] OnSelectFile 开始执行");

            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "选择 Vision Master 方案文件",
                    Filter = "Vision Master 方案 (*.sol)|*.sol|所有文件 (*.*)|*.*",
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() == true)
                {
                    SolutionPath = dialog.FileName;
                    AddLog($"[INFO] 已选择方案: {System.IO.Path.GetFileName(dialog.FileName)}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnSelectFile 异常: {ex.Message}");
                AddLog($"[ERROR] 选择文件异常: {ex.Message}");
            }
        }

        private bool CanLoad()
        {
            return SolutionPath != "未选择方案" && !string.IsNullOrWhiteSpace(SolutionPath);
        }

        private async void OnLoad()
        {
            try
            {
                StatusText = "加载中...";
                StatusColor = Colors.Orange;
                IsLoaded = false;

                AddLog($"[INFO] 开始加载方案...");
                var result = await _visionService.LoadSolutionAsync(SolutionPath, SolutionPassword);
            }
            catch (Exception ex)
            {
                StatusText = $"加载失败";
                StatusColor = (Color)ColorConverter.ConvertFromString("#DC3545");
                AddLog($"[ERROR] 加载异常: {ex.Message}");
            }
        }

        private bool CanSave()
        {
            return IsLoaded;
        }

        private async void OnSave()
        {
            try
            {
                AddLog("[INFO] 保存方案中...");
                var result = await _visionService.SaveSolutionAsync();

                if (result.Success)
                {
                    AddLog($"[SUCCESS] {result.Message}");
                }
                else
                {
                    AddLog($"[ERROR] {result.Message}");
                }
            }
            catch (Exception ex)
            {
                AddLog($"[ERROR] 保存异常: {ex.Message}");
            }
        }

        private bool CanClose()
        {
            return IsLoaded;
        }

        private void OnClose()
        {
            try
            {
                _visionService.CloseSolution();
                IsLoaded = false;
                StatusText = "方案已关闭";
                StatusColor = Colors.Gray;
                SolutionPath = "未选择方案";
                AddLog("[INFO] 方案已关闭");
            }
            catch (Exception ex)
            {
                AddLog($"[ERROR] 关闭异常: {ex.Message}");
            }
        }

        private bool CanRunTest()
        {
            return IsLoaded;
        }

        private async void OnRunTest()
        {
            try
            {
                AddLog("[INFO] 开始执行视觉流程...");
                var result = await _visionService.ProcessVisionAsync();
            }
            catch (Exception ex)
            {
                AddLog($"[ERROR] 执行异常: {ex.Message}");
            }
        }

        private bool CanGetVersion()
        {
            return !string.IsNullOrWhiteSpace(SolutionPath) && SolutionPath != "未选择方案";
        }

        private async void OnGetVersion()
        {
            try
            {
                var version = await _visionService.GetSolutionVersionAsync(SolutionPath, SolutionPassword);
                AddLog($"[INFO] {version}");
            }
            catch (Exception ex)
            {
                AddLog($"[ERROR] 获取版本失败: {ex.Message}");
            }
        }

        private bool CanGetPath()
        {
            return IsLoaded;
        }

        private void OnGetPath()
        {
            var path = _visionService.GetSolutionPath();
            AddLog($"[INFO] 当前方案路径: {path}");
        }

        private bool CanCheckPassword()
        {
            return !string.IsNullOrWhiteSpace(SolutionPath) && SolutionPath != "未选择方案";
        }

        private async void OnCheckPassword()
        {
            try
            {
                var hasPassword = await _visionService.HasPasswordAsync(SolutionPath);
                var msg = hasPassword ? "方案有密码保护" : "方案无密码保护";
                AddLog($"[INFO] {msg}");
            }
            catch (Exception ex)
            {
                AddLog($"[ERROR] 检查密码失败: {ex.Message}");
            }
        }

        private void OnClear()
        {
            LogText = "";
            ExecuteCount = 0;
            SuccessCount = 0;
            FailCount = 0;
        }

        #endregion

        #region 事件处理

        private void OnLoadCompleted(object sender, VisionResult result)
        {
            // ✅ 修复：使用 BeginInvoke 替代 Invoke，避免跨线程死锁
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (result.Success)
                    {
                        StatusText = "方案加载成功";
                        StatusColor = (Color)ColorConverter.ConvertFromString("#28A745");
                        IsLoaded = true;
                        AddLog($"[SUCCESS] {result.Message}");
                    }
                    else
                    {
                        StatusText = "加载失败";
                        StatusColor = (Color)ColorConverter.ConvertFromString("#DC3545");
                        IsLoaded = false;
                        AddLog($"[ERROR] {result.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Vision] 更新加载状态异常: {ex.Message}");
                }
            }));
        }

        private void OnProcessCompleted(object sender, VisionResult result)
        {
            // ✅ 修复：使用 BeginInvoke 替代 Invoke，避免跨线程死锁
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    ExecuteCount++;

                    if (result.Success)
                    {
                        SuccessCount++;
                        AddLog($"[SUCCESS] {result.Message} (耗时: {result.ElapsedMilliseconds}ms)");
                    }
                    else
                    {
                        FailCount++;
                        AddLog($"[FAIL] {result.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Vision] 更新处理状态异常: {ex.Message}");
                }
            }));
        }

        private void AddLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogText += $"[{timestamp}] {message}\n";
            Debug.WriteLine(message);
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            Debug.WriteLine($"属性变更: {propertyName}");
        }

        #endregion
    }
}