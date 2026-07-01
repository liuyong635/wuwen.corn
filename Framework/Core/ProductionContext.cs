using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SeedCut.Framework.Core
{
    /// <summary>
    /// 生产上下文接口 - 管理生产运行状态
    /// </summary>
    public interface IProductionContext : INotifyPropertyChanged
    {
        /// <summary>
        /// 系统是否正在运行
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// 是否处于暂停状态
        /// </summary>
        bool IsPaused { get; }

        /// <summary>
        /// 是否可以处理信号
        /// </summary>
        bool CanProcessSignals { get; }

        /// <summary>
        /// 是否处于急停状态
        /// </summary>
        bool IsEmergencyStopped { get; }

        /// <summary>
        /// 是否处于手动模式
        /// </summary>
        bool IsManualMode { get; set; }

        /// <summary>
        /// OK计数
        /// </summary>
        int OkCount { get; set; }

        /// <summary>
        /// NG计数
        /// </summary>
        int NgCount { get; set; }

        /// <summary>
        /// 总计数
        /// </summary>
        int TotalCount { get; }

        /// <summary>
        /// 良率
        /// </summary>
        double YieldRate { get; }

        /// <summary>
        /// 当前批次号
        /// </summary>
        string CurrentBatchId { get; set; }

        /// <summary>
        /// 运行开始时间
        /// </summary>
        DateTime? RunStartTime { get; }

        /// <summary>
        /// 运行时长
        /// </summary>
        TimeSpan RunDuration { get; }

        /// <summary>
        /// 状态变更事件
        /// </summary>
        event EventHandler<ProductionStateChangedEventArgs> StateChanged;

        /// <summary>
        /// 启动生产
        /// </summary>
        void Start();

        /// <summary>
        /// 停止生产
        /// </summary>
        void Stop();

        /// <summary>
        /// 暂停生产
        /// </summary>
        void Pause();

        /// <summary>
        /// 恢复生产
        /// </summary>
        void Resume();

        /// <summary>
        /// 触发急停
        /// </summary>
        void EmergencyStop();

        /// <summary>
        /// 复位急停
        /// </summary>
        void ResetEmergency();

        /// <summary>
        /// 重置计数
        /// </summary>
        void ResetCounts();

        /// <summary>
        /// 增加OK计数
        /// </summary>
        void IncrementOk(int count = 1);

        /// <summary>
        /// 增加NG计数
        /// </summary>
        void IncrementNg(int count = 1);

        /// <summary>
        /// 增加错误计数（等同于NG）
        /// </summary>
        void IncrementError(int count = 1);

        /// <summary>
        /// 增加生产计数（等同于OK）
        /// </summary>
        void IncrementCount(int count = 1);
    }

    /// <summary>
    /// 生产状态变更事件参数
    /// </summary>
    public class ProductionStateChangedEventArgs : EventArgs
    {
        public bool WasRunning { get; set; }
        public bool IsRunning { get; set; }
        public bool WasPaused { get; set; }
        public bool IsPaused { get; set; }
        public string Reason { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 生产上下文实现
    /// </summary>
    public class ProductionContext : IProductionContext
    {
        private bool _isRunning;
        private bool _isPaused;
        private bool _isEmergencyStopped;
        private bool _isManualMode;
        private int _okCount;
        private int _ngCount;
        private string _currentBatchId;
        private DateTime? _runStartTime;

        public bool IsRunning
        {
            get => _isRunning;
            private set => SetProperty(ref _isRunning, value);
        }

        public bool IsPaused
        {
            get => _isPaused;
            private set => SetProperty(ref _isPaused, value);
        }

        public bool CanProcessSignals => IsRunning && !IsPaused && !IsEmergencyStopped;

        public bool IsEmergencyStopped
        {
            get => _isEmergencyStopped;
            private set => SetProperty(ref _isEmergencyStopped, value);
        }

        public bool IsManualMode
        {
            get => _isManualMode;
            set => SetProperty(ref _isManualMode, value);
        }

        public int OkCount
        {
            get => _okCount;
            set
            {
                SetProperty(ref _okCount, value);
                OnPropertyChanged(nameof(TotalCount));
                OnPropertyChanged(nameof(YieldRate));
            }
        }

        public int NgCount
        {
            get => _ngCount;
            set
            {
                SetProperty(ref _ngCount, value);
                OnPropertyChanged(nameof(TotalCount));
                OnPropertyChanged(nameof(YieldRate));
            }
        }

        public int TotalCount => OkCount + NgCount;

        public double YieldRate => TotalCount > 0 ? (double)OkCount / TotalCount * 100 : 0;

        public string CurrentBatchId
        {
            get => _currentBatchId;
            set => SetProperty(ref _currentBatchId, value);
        }

        public DateTime? RunStartTime
        {
            get => _runStartTime;
            private set => SetProperty(ref _runStartTime, value);
        }

        public TimeSpan RunDuration => RunStartTime.HasValue ? DateTime.Now - RunStartTime.Value : TimeSpan.Zero;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<ProductionStateChangedEventArgs> StateChanged;

        public void Start()
        {
            if (IsRunning) return;
            if (IsEmergencyStopped)
            {
                throw new InvalidOperationException("急停状态下无法启动");
            }

            var wasRunning = IsRunning;
            var wasPaused = IsPaused;

            IsRunning = true;
            IsPaused = false;
            RunStartTime = DateTime.Now;

            OnStateChanged(wasRunning, IsRunning, wasPaused, IsPaused, "手动启动");
        }

        public void Stop()
        {
            if (!IsRunning) return;

            var wasRunning = IsRunning;
            var wasPaused = IsPaused;

            IsRunning = false;
            IsPaused = false;
            RunStartTime = null;

            OnStateChanged(wasRunning, IsRunning, wasPaused, IsPaused, "手动停止");
        }

        public void Pause()
        {
            if (!IsRunning || IsPaused) return;

            var wasPaused = IsPaused;
            IsPaused = true;

            OnStateChanged(IsRunning, IsRunning, wasPaused, IsPaused, "手动暂停");
        }

        public void Resume()
        {
            if (!IsRunning || !IsPaused) return;

            var wasPaused = IsPaused;
            IsPaused = false;

            OnStateChanged(IsRunning, IsRunning, wasPaused, IsPaused, "手动恢复");
        }

        public void EmergencyStop()
        {
            var wasRunning = IsRunning;
            var wasPaused = IsPaused;

            IsRunning = false;
            IsPaused = false;
            IsEmergencyStopped = true;

            OnStateChanged(wasRunning, IsRunning, wasPaused, IsPaused, "急停触发");
        }

        public void ResetEmergency()
        {
            IsEmergencyStopped = false;
        }

        public void ResetCounts()
        {
            OkCount = 0;
            NgCount = 0;
        }

        public void IncrementOk(int count = 1)
        {
            OkCount += count;
        }

        public void IncrementNg(int count = 1)
        {
            NgCount += count;
        }

        public void IncrementError(int count = 1)
        {
            // 错误计数等同于NG计数
            IncrementNg(count);
        }

        public void IncrementCount(int count = 1)
        {
            // 生产计数等同于OK计数
            IncrementOk(count);
        }

        protected void OnStateChanged(bool wasRunning, bool isRunning, bool wasPaused, bool isPaused, string reason)
        {
            StateChanged?.Invoke(this, new ProductionStateChangedEventArgs
            {
                WasRunning = wasRunning,
                IsRunning = isRunning,
                WasPaused = wasPaused,
                IsPaused = isPaused,
                Reason = reason
            });
        }

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
    }
}