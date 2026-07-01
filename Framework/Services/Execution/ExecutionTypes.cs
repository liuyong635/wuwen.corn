using System;
using System.Collections.Generic;

namespace SeedCut.Framework.Services.Execution
{
    #region 执行状态枚举

    /// <summary>
    /// 执行控制器状态
    /// </summary>
    public enum ExecutionState
    {
        /// <summary>
        /// 空闲（未启动）
        /// </summary>
        Idle,

        /// <summary>
        /// 运行中
        /// </summary>
        Running,

        /// <summary>
        /// 暂停中
        /// </summary>
        Paused,

        /// <summary>
        /// 停止中（等待当前任务完成）
        /// </summary>
        Stopping,

        /// <summary>
        /// 已停止
        /// </summary>
        Stopped,

        /// <summary>
        /// 急停状态
        /// </summary>
        EmergencyStopped
    }

    /// <summary>
    /// Handler执行状态
    /// </summary>
    public enum HandlerExecutionStatus
    {
        /// <summary>
        /// 成功完成
        /// </summary>
        Success,

        /// <summary>
        /// 执行失败
        /// </summary>
        Failed,

        /// <summary>
        /// 被取消
        /// </summary>
        Cancelled,

        /// <summary>
        /// 超时
        /// </summary>
        Timeout,

        /// <summary>
        /// 设备断线
        /// </summary>
        DeviceDisconnected
    }

    #endregion

    #region 配置类

    /// <summary>
    /// 执行控制器配置
    /// </summary>
    public class ExecutionControllerConfig
    {
        /// <summary>
        /// 默认Handler超时时间（秒）
        /// </summary>
        public int DefaultHandlerTimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// 暂停等待超时时间（秒）- 等待当前任务完成的最长时间
        /// </summary>
        public int PauseWaitTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// 停止等待超时时间（秒）
        /// </summary>
        public int StopWaitTimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// 设备断线时是否取消依赖该设备的Handler
        /// </summary>
        public bool CancelHandlerOnDeviceDisconnect { get; set; } = true;

        /// <summary>
        /// 设备恢复后是否自动触发信号评估
        /// </summary>
        public bool EvaluateOnDeviceReconnect { get; set; } = true;

        /// <summary>
        /// 每个Handler的超时时间配置（秒）
        /// Key: HandlerId, Value: 超时秒数
        /// </summary>
        public Dictionary<string, int> HandlerTimeouts { get; set; } = new Dictionary<string, int>
        {
            ["Heartbeat"] = 1,
            ["RobotFeed"] = 120,
            ["LaserVision"] = 30,
            ["LaserCut"] = 180,
            ["DiskVision"] = 15,
            ["SmallTrayVision"] = 15,
            ["LargeTrayVision"] = 15,
            ["SmallTrayDirection"] = 10,
            ["LargeTrayDirection"] = 10,
            ["CircularAxisInit"] = 90,
            ["VibrateDiskFeed"] = 30,
            ["BarcodeScan"] = 10
        };

        /// <summary>
        /// 获取Handler超时时间
        /// </summary>
        public TimeSpan GetHandlerTimeout(string handlerId)
        {
            if (HandlerTimeouts.TryGetValue(handlerId, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
            return TimeSpan.FromSeconds(DefaultHandlerTimeoutSeconds);
        }
    }

    /// <summary>
    /// 重试配置
    /// </summary>
    public class RetryConfig
    {
        /// <summary>
        /// 默认最大重试次数
        /// </summary>
        public int DefaultMaxRetries { get; set; } = 0;

        /// <summary>
        /// 初始重试延迟（毫秒）
        /// </summary>
        public int InitialRetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// 最大重试延迟（毫秒）
        /// </summary>
        public int MaxRetryDelayMs { get; set; } = 10000;

        /// <summary>
        /// 每个Handler的重试次数配置
        /// </summary>
        public Dictionary<string, int> HandlerRetries { get; set; } = new Dictionary<string, int>
        {
            ["RobotFeed"] = 1,           // 取放料重试1次
            ["LaserVision"] = 1,         // 视觉重试2次
            ["LaserCut"] = 1,            // 切割重试1次
            ["DiskVision"] = 1,
            ["SmallTrayVision"] = 1,
            ["LargeTrayVision"] = 1,
            ["BarcodeScan"] = 1
        };

        /// <summary>
        /// 获取Handler最大重试次数
        /// </summary>
        public int GetMaxRetries(string handlerId)
        {
            if (HandlerRetries.TryGetValue(handlerId, out var retries))
            {
                return retries;
            }
            return DefaultMaxRetries;
        }

        /// <summary>
        /// 计算重试延迟（指数退避）
        /// </summary>
        public int CalculateRetryDelay(int retryCount)
        {
            // 指数退避: delay = initial * 2^(retryCount-1)
            var delay = InitialRetryDelayMs * (int)Math.Pow(2, retryCount - 1);
            return Math.Min(delay, MaxRetryDelayMs);
        }
    }

    #endregion

    #region 事件参数

    /// <summary>
    /// 执行状态变化事件参数
    /// </summary>
    public class ExecutionStateChangedEventArgs : EventArgs
    {
        public ExecutionState OldState { get; set; }
        public ExecutionState NewState { get; set; }
        public string Reason { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Handler超时事件参数
    /// </summary>
    public class HandlerTimeoutEventArgs : EventArgs
    {
        public string HandlerId { get; set; }
        public string HandlerName { get; set; }
        public TimeSpan ConfiguredTimeout { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Handler执行结果事件参数（增强版）
    /// </summary>
    public class HandlerExecutionResultEventArgs : EventArgs
    {
        public string HandlerId { get; set; }
        public string HandlerName { get; set; }
        public HandlerExecutionStatus Status { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public TimeSpan Duration { get; set; }
        public int RetryCount { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    #endregion

    #region 统计类

    /// <summary>
    /// Handler执行统计
    /// </summary>
    public class HandlerStatistics
    {
        public string HandlerId { get; set; }
        public string HandlerName { get; set; }

        // 执行计数
        public int TotalExecutions { get; set; }
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
        public int TimeoutCount { get; set; }
        public int CancelCount { get; set; }
        public int DeviceErrorCount { get; set; }
        public int RetryCount { get; set; }

        // 时间统计
        public TimeSpan TotalDuration { get; set; }
        public TimeSpan MinDuration { get; set; } = TimeSpan.MaxValue;
        public TimeSpan MaxDuration { get; set; } = TimeSpan.Zero;
        public DateTime? LastExecutionTime { get; set; }

        /// <summary>
        /// 成功率
        /// </summary>
        public double SuccessRate => TotalExecutions > 0 ? (double)SuccessCount / TotalExecutions * 100 : 0;

        /// <summary>
        /// 平均执行时间
        /// </summary>
        public TimeSpan AverageDuration => TotalExecutions > 0
            ? TimeSpan.FromTicks(TotalDuration.Ticks / TotalExecutions)
            : TimeSpan.Zero;

        /// <summary>
        /// 记录执行结果
        /// </summary>
        public void RecordExecution(HandlerExecutionStatus status, TimeSpan duration)
        {
            TotalExecutions++;
            TotalDuration += duration;
            LastExecutionTime = DateTime.Now;

            if (duration < MinDuration) MinDuration = duration;
            if (duration > MaxDuration) MaxDuration = duration;

            switch (status)
            {
                case HandlerExecutionStatus.Success:
                    SuccessCount++;
                    break;
                case HandlerExecutionStatus.Failed:
                    FailCount++;
                    break;
                case HandlerExecutionStatus.Timeout:
                    TimeoutCount++;
                    break;
                case HandlerExecutionStatus.Cancelled:
                    CancelCount++;
                    break;
                case HandlerExecutionStatus.DeviceDisconnected:
                    DeviceErrorCount++;
                    break;
            }
        }

        /// <summary>
        /// 重置统计
        /// </summary>
        public void Reset()
        {
            TotalExecutions = 0;
            SuccessCount = 0;
            FailCount = 0;
            TimeoutCount = 0;
            CancelCount = 0;
            DeviceErrorCount = 0;
            RetryCount = 0;
            TotalDuration = TimeSpan.Zero;
            MinDuration = TimeSpan.MaxValue;
            MaxDuration = TimeSpan.Zero;
            LastExecutionTime = null;
        }

        public override string ToString()
        {
            return string.Format("[{0}] 总计:{1} 成功:{2} 失败:{3} 超时:{4} 成功率:{5:F1}% 平均耗时:{6:F0}ms",
                HandlerId, TotalExecutions, SuccessCount, FailCount, TimeoutCount,
                SuccessRate, AverageDuration.TotalMilliseconds);
        }
    }

    #endregion
}