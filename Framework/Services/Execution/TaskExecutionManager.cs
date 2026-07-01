using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Execution
{
    /// <summary>
    /// 任务执行管理器
    /// 

    /// 此类用于管理 Handler 的异步执行
    /// 
    /// 职责：
    /// 1. 管理 Handler 的异步执行（启动、追踪、取消）
    /// 2. 防止同一 Handler 重复执行（通过 TaskId/HandlerId 判断）
    /// 3. 控制最大并发执行数
    /// 4. 提供任务完成通知事件
    /// 
    /// 典型使用场景：
    /// - HandlerExecutor 调用 TryStart 启动 Handler 异步执行
    /// - 通过 IsRunning 检查 Handler 是否正在执行，防止重入
    /// - 通过 TaskCompleted 事件触发下一轮条件评估
    /// 
    /// 使用示例：
    /// <code>
    /// // 启动 Handler 执行
    /// var started = taskManager.TryStart(
    ///     handler.HandlerId,
    ///     async ct => await handler.HandleAsync(context, ct),
    ///     handler.HandlerName);
    /// 
    /// // 检查 Handler 是否正在执行
    /// if (taskManager.IsRunning("SmallTrayVision"))
    /// {
    ///     // Handler 正在执行中，跳过本次触发
    /// }
    /// 
    /// // 等待所有 Handler 完成
    /// await taskManager.WaitAllAsync();
    /// </code>
    /// </summary>
    public class TaskExecutionManager : ITaskExecutionManager
    {
        #region 私有字段

        /// <summary>
        /// 任务字典，Key = TaskId (通常是 HandlerId)
        /// </summary>
        private readonly ConcurrentDictionary<string, TaskInfo> _tasks = new ConcurrentDictionary<string, TaskInfo>();

        /// <summary>
        /// 并发控制信号量
        /// </summary>
        private readonly SemaphoreSlim _concurrencyLimiter;

        /// <summary>
        /// 是否已释放
        /// </summary>
        private bool _disposed;

        #endregion

        #region 公共属性

        /// <summary>
        /// 最大并发执行数
        /// </summary>
        public int MaxConcurrency { get; }

        /// <summary>
        /// 当前正在执行的任务数
        /// </summary>
        public int RunningCount => _tasks.Count(s => s.Value.Status == TaskExecutionStatus.Running);

        #endregion

        #region 事件

        /// <summary>
        /// 任务完成事件
        /// 当任何一个任务执行完成时触发（无论成功、失败或取消）
        /// HandlerExecutor 通过此事件触发下一轮条件评估
        /// </summary>
        public event EventHandler<TaskCompletedEventArgs> TaskCompleted;

        /// <summary>
        /// 所有任务完成事件
        /// 当所有任务都执行完毕时触发
        /// </summary>
        public event EventHandler AllTasksCompleted;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建任务执行管理器
        /// </summary>
        /// <param name="maxConcurrency">最大并发数，默认不限制</param>
        public TaskExecutionManager(int maxConcurrency = int.MaxValue)
        {
            MaxConcurrency = maxConcurrency;
            _concurrencyLimiter = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        }

        #endregion

        #region 状态查询

        /// <summary>
        /// 检查指定任务是否正在执行
        /// </summary>
        /// <param name="taskId">任务ID（通常是 HandlerId）</param>
        /// <returns>是否正在执行</returns>
        public bool IsRunning(string taskId)
            => _tasks.TryGetValue(taskId, out var info) && info.Status == TaskExecutionStatus.Running;

        /// <summary>
        /// 获取指定任务的执行状态
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <returns>任务状态，如果任务不存在则返回 Idle</returns>
        public TaskExecutionStatus GetStatus(string taskId)
            => _tasks.TryGetValue(taskId, out var info) ? info.Status : TaskExecutionStatus.Idle;

        /// <summary>
        /// 获取所有正在执行的任务ID列表
        /// </summary>
        /// <returns>正在执行的任务ID列表</returns>
        public IReadOnlyList<string> GetRunningTasks()
            => _tasks.Where(s => s.Value.Status == TaskExecutionStatus.Running).Select(s => s.Key).ToList();

        #endregion

        #region 任务控制

        /// <summary>
        /// 尝试启动任务
        /// 
        /// 如果任务已在执行中或达到最大并发数，则返回 false
        /// 任务会以 fire-and-forget 方式异步执行，但内部会追踪状态并触发完成事件
        /// </summary>
        /// <param name="taskId">任务唯一标识（通常是 HandlerId）</param>
        /// <param name="action">要执行的异步操作</param>
        /// <param name="taskName">任务显示名称（可选，用于日志和事件）</param>
        /// <returns>是否成功启动任务</returns>
        public bool TryStart(string taskId, Func<CancellationToken, Task<ValueTuple<bool, string>>> action, string taskName = null)
        {
            // 检查是否已在执行或达到并发上限
            if (IsRunning(taskId) || RunningCount >= MaxConcurrency)
                return false;

            var cts = new CancellationTokenSource();
            var info = new TaskInfo
            {
                TaskId = taskId,
                TaskName = taskName ?? taskId,
                Status = TaskExecutionStatus.Running,
                Cts = cts,
                StartTime = DateTime.Now
            };

            // 尝试添加任务记录
            if (!_tasks.TryAdd(taskId, info))
                return false;

            // 启动异步执行（fire-and-forget）
            _ = ExecuteTaskAsync(info, action);
            return true;
        }

        /// <summary>
        /// 执行任务的核心方法
        /// </summary>
        private async Task ExecuteTaskAsync(TaskInfo info, Func<CancellationToken, Task<ValueTuple<bool, string>>> action)
        {
            var stopwatch = Stopwatch.StartNew();
            bool success = false;
            string message = "";

            try
            {
                // 等待并发许可
                await _concurrencyLimiter.WaitAsync(info.Cts.Token);
                try
                {
                    // 执行实际任务
                    var result = await action(info.Cts.Token);
                    success = result.Item1;
                    message = result.Item2;
                    info.Status = success ? TaskExecutionStatus.Completed : TaskExecutionStatus.Failed;
                }
                finally
                {
                    _concurrencyLimiter.Release();
                }
            }
            catch (OperationCanceledException)
            {
                info.Status = TaskExecutionStatus.Cancelled;
                message = "已取消";
            }
            catch (Exception ex)
            {
                info.Status = TaskExecutionStatus.Failed;
                message = ex.Message;
            }
            finally
            {
                stopwatch.Stop();

                // ★ 修复：先清理任务记录，再触发事件
                // 这样 TaskCompleted 触发 EvaluateAndExecute() 时，
                // IsRunning 已经返回 false，不会跳过刚完成的 Handler
                _tasks.TryRemove(info.TaskId, out _);
                info.Cts.Dispose();

                // 触发任务完成事件（此时 IsRunning 已经是 false）
                TaskCompleted?.Invoke(this, new TaskCompletedEventArgs
                {
                    TaskId = info.TaskId,
                    TaskName = info.TaskName,
                    Success = success,
                    Message = message,
                    Duration = stopwatch.Elapsed
                });

                // 检查是否所有任务都已完成
                if (RunningCount == 0)
                    AllTasksCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 取消指定任务
        /// </summary>
        /// <param name="taskId">任务ID</param>
        public void Cancel(string taskId)
        {
            if (_tasks.TryGetValue(taskId, out var info))
                info.Cts.Cancel();
        }

        /// <summary>
        /// 取消所有正在执行的任务
        /// </summary>
        public void CancelAll()
        {
            foreach (var info in _tasks.Values)
                info.Cts.Cancel();
        }

        #endregion

        #region 等待方法

        /// <summary>
        /// 等待所有任务完成
        /// </summary>
        /// <param name="ct">取消令牌</param>
        public async Task WaitAllAsync(CancellationToken ct = default)
        {
            while (RunningCount > 0 && !ct.IsCancellationRequested)
                await Task.Delay(50, ct);
        }

        /// <summary>
        /// 等待所有任务完成（带超时）
        /// </summary>
        /// <param name="timeout">超时时间</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>是否在超时前完成所有任务</returns>
        public async Task<bool> WaitAllAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(timeout);

                try
                {
                    await WaitAllAsync(cts.Token);
                    return RunningCount == 0;
                }
                catch (OperationCanceledException)
                {
                    return RunningCount == 0;
                }
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 取消所有任务
            CancelAll();

            // 释放所有 CancellationTokenSource
            foreach (var info in _tasks.Values)
                info.Cts.Dispose();

            _tasks.Clear();
            _concurrencyLimiter.Dispose();
        }

        #endregion

        #region 内部类

        /// <summary>
        /// 任务信息（内部使用）
        /// </summary>
        private class TaskInfo
        {
            /// <summary>任务ID</summary>
            public string TaskId { get; set; }

            /// <summary>任务名称</summary>
            public string TaskName { get; set; }

            /// <summary>执行状态</summary>
            public TaskExecutionStatus Status { get; set; }

            /// <summary>取消令牌源</summary>
            public CancellationTokenSource Cts { get; set; }

            /// <summary>开始时间</summary>
            public DateTime StartTime { get; set; }
        }

        #endregion
    }
}