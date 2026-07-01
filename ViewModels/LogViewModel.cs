using SeedCut.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// 日志页面的ViewModel
    /// </summary>
    public class LogViewModel : INotifyPropertyChanged
    {
        #region 事件

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region 私有字段

        private ObservableCollection<TaskLog> _taskLogs;
        private TaskLog _selectedTaskLog;

        #endregion

        #region 属性

        /// <summary>
        /// 任务日志列表
        /// </summary>
        public ObservableCollection<TaskLog> TaskLogs
        {
            get => _taskLogs;
            set
            {
                if (_taskLogs != value)
                {
                    _taskLogs = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 选中的任务日志
        /// </summary>
        public TaskLog SelectedTaskLog
        {
            get => _selectedTaskLog;
            set
            {
                if (_selectedTaskLog != value)
                {
                    _selectedTaskLog = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region 命令

        public ICommand OpenFileCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }

        #endregion

        #region 构造函数

        public LogViewModel()
        {
            TaskLogs = new ObservableCollection<TaskLog>();

            // 初始化命令
            OpenFileCommand = new RelayCommand(OnOpenFile, CanOpenFile);
            RefreshCommand = new RelayCommand(OnRefresh);
            ExportCommand = new RelayCommand(OnExport, CanExport);

            // 加载示例数据
            LoadSampleData();
        }

        #endregion

        #region 命令处理

        private bool CanOpenFile()
        {
            return SelectedTaskLog != null && !string.IsNullOrEmpty(SelectedTaskLog.DataPath);
        }

        private void OnOpenFile()
        {
            if (SelectedTaskLog == null || string.IsNullOrEmpty(SelectedTaskLog.DataPath))
            {
                MessageBox.Show("请先选择一条任务记录", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // TODO: 实际实现时，这里应该打开对应的数据文件或文件夹
            try
            {
                // 示例：如果路径存在，打开文件夹
                if (Directory.Exists(Path.GetDirectoryName(SelectedTaskLog.DataPath)))
                {
                    Process.Start("explorer.exe", $"/select,\"{SelectedTaskLog.DataPath}\"");
                }
                else
                {
                    MessageBox.Show($"文件路径不存在：{SelectedTaskLog.DataPath}", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开文件失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnRefresh()
        {
            // TODO: 实现从数据库或文件加载日志数据
            LoadSampleData();
            MessageBox.Show("数据已刷新", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private bool CanExport()
        {
            return TaskLogs != null && TaskLogs.Count > 0;
        }

        private void OnExport()
        {
            // TODO: 实现导出功能（导出为Excel或CSV）
            MessageBox.Show("导出功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region 方法

        /// <summary>
        /// 加载示例数据
        /// </summary>
        private void LoadSampleData()
        {
            TaskLogs.Clear();

            // 添加示例数据
            for (int i = 0; i < 12; i++)
            {
                TaskLogs.Add(new TaskLog
                {
                    TaskNumber = "DA5749960-70",
                    TaskQuantity = 1000,
                    SeedCode = "001",
                    CutArea = 32,
                    StartTime = DateTime.Parse("2025-11-01 10:00:00"),
                    EndTime = DateTime.Parse("2025-11-01 14:00:00"),
                    AlarmInfo = "这是报警信息 ...",
                    DataPath = "375049467-57067"
                });
            }
        }

        /// <summary>
        /// 添加新的任务日志
        /// </summary>
        public void AddTaskLog(TaskLog taskLog)
        {
            if (taskLog != null)
            {
                TaskLogs.Insert(0, taskLog); // 新记录插入到最前面
            }
        }

        /// <summary>
        /// 清除所有日志
        /// </summary>
        public void ClearLogs()
        {
            if (MessageBox.Show("确定要清除所有日志吗？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                TaskLogs.Clear();
            }
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