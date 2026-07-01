using SeedCut.Framework.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Interfaces
{
    // ============================================================
    // 注意：ILogService 已移至单独的 ILogService.cs 文件
    // 注意：IAlarmService 已移至单独的 IAlarmService.cs 文件
    // ============================================================

    #region 信号监控接口

    /// <summary>
    /// 信号监控器接口
    /// </summary>
    public interface ISignalMonitor : IDisposable
    {
        bool IsRunning { get; }
        int PollingIntervalMs { get; set; }

        event EventHandler<SignalChangedEventArgs> SignalChanged;

        void Register(string signalName);
        void Register(string signalName, string plcAddress);
        void Register(params string[] signalNames);
        void Unregister(string signalName);

        bool GetSignal(string signalName, bool defaultValue = false);
        bool HasSignal(string signalName);
        IReadOnlyDictionary<string, bool> GetAllSignals();

        void Start();
        void Stop();
        Task RefreshAsync(CancellationToken ct = default(CancellationToken));

        Task<bool> WaitForSignalAsync(string signalName, bool expectedValue, TimeSpan timeout, CancellationToken ct = default(CancellationToken));
        Task<bool> WaitForEdgeAsync(string signalName, SignalEdge edge, TimeSpan timeout, CancellationToken ct = default(CancellationToken));
    }

    #endregion

    #region 工位管理器接口

    /// <summary>
    /// 任务执行管理器接口
    ///     
    /// 职责：
    /// 1. 管理 Handler 的异步执行（启动、追踪、取消）
    /// 2. 防止同一 Handler 重复执行（通过 TaskId 判断）
    /// 3. 控制最大并发执行数
    /// 4. 提供任务完成通知事件
    /// 
    /// 典型使用场景：
    /// - HandlerExecutor 调用 TryStart 启动 Handler
    /// - 通过 IsRunning 检查 Handler 是否正在执行
    /// - 通过 TaskCompleted 事件触发下一轮评估
    /// </summary>
    public interface ITaskExecutionManager : IDisposable
    {
        #region 属性

        /// <summary>
        /// 最大并发执行数
        /// </summary>
        int MaxConcurrency { get; }

        /// <summary>
        /// 当前正在执行的任务数
        /// </summary>
        int RunningCount { get; }

        #endregion

        #region 事件

        /// <summary>
        /// 任务完成事件
        /// 当任何一个任务执行完成时触发
        /// </summary>
        event EventHandler<TaskCompletedEventArgs> TaskCompleted;

        /// <summary>
        /// 所有任务完成事件
        /// 当所有任务都执行完毕时触发
        /// </summary>
        event EventHandler AllTasksCompleted;

        #endregion

        #region 状态查询

        /// <summary>
        /// 检查指定任务是否正在执行
        /// </summary>
        /// <param name="taskId">任务ID（通常是 HandlerId）</param>
        /// <returns>是否正在执行</returns>
        bool IsRunning(string taskId);

        /// <summary>
        /// 获取指定任务的执行状态
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <returns>任务状态</returns>
        TaskExecutionStatus GetStatus(string taskId);

        /// <summary>
        /// 获取所有正在执行的任务ID列表
        /// </summary>
        /// <returns>正在执行的任务ID列表</returns>
        IReadOnlyList<string> GetRunningTasks();

        #endregion

        #region 任务控制

        /// <summary>
        /// 尝试启动任务
        /// </summary>
        /// <param name="taskId">任务唯一标识（通常是 HandlerId）</param>
        /// <param name="action">要执行的异步操作</param>
        /// <param name="taskName">任务显示名称（可选）</param>
        /// <returns>是否成功启动（如果任务已在执行或达到并发上限则返回false）</returns>
        bool TryStart(string taskId, Func<CancellationToken, Task<ValueTuple<bool, string>>> action, string taskName = null);

        /// <summary>
        /// 取消指定任务
        /// </summary>
        /// <param name="taskId">任务ID</param>
        void Cancel(string taskId);

        /// <summary>
        /// 取消所有正在执行的任务
        /// </summary>
        void CancelAll();

        #endregion

        #region 等待方法

        /// <summary>
        /// 等待所有任务完成
        /// </summary>
        /// <param name="ct">取消令牌</param>
        Task WaitAllAsync(CancellationToken ct = default(CancellationToken));

        /// <summary>
        /// 等待所有任务完成（带超时）
        /// </summary>
        /// <param name="timeout">超时时间</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>是否在超时前完成所有任务</returns>
        Task<bool> WaitAllAsync(TimeSpan timeout, CancellationToken ct = default(CancellationToken));

        #endregion
    }

    #endregion

    #region 数据管道接口

    /// <summary>
    /// 数据管道接口
    /// </summary>
    public interface IDataPipeline<T>
    {
        string PipelineId { get; }
        int Count { get; }
        bool IsEmpty { get; }

        event EventHandler<T> DataAvailable;

        void Push(T data);
        bool TryPop(out T data);
        bool TryPeek(out T data);

        Task<T> WaitForDataAsync(CancellationToken ct = default(CancellationToken));
        Task<ValueTuple<bool, T>> WaitForDataAsync(TimeSpan timeout, CancellationToken ct = default(CancellationToken));

        void Clear();
    }

    /// <summary>
    /// 数据流管理器接口
    /// </summary>
    public interface IDataFlowManager : IDisposable
    {
        IDataPipeline<T> GetOrCreate<T>(string pipelineId);
        IDataPipeline<T> Get<T>(string pipelineId);
        bool Has(string pipelineId);
        void Remove(string pipelineId);
        void ClearAll();
        IEnumerable<string> GetAllIds();
    }

    #endregion

    #region 条件接口

    /// <summary>
    /// 触发条件接口
    /// </summary>
    public interface ITriggerCondition
    {
        bool IsSatisfied(IConditionContext context);
        string Description { get; }
    }

    /// <summary>
    /// 条件上下文接口
    /// </summary>
    public interface IConditionContext
    {
        IProductionContext Context { get; }
        bool GetSignal(string name, bool defaultValue = false);
        bool IsStationBusy(string stationId);
    }

    #endregion

    #region 处理器接口

    /// <summary>
    /// 信号处理器接口
    /// </summary>
    public interface ISignalHandler
    {
        string HandlerId { get; }
        string HandlerName { get; }
        ITriggerCondition TriggerCondition { get; }
        int Priority { get; }
        bool IsEnabled { get; set; }

        bool CanExecute(IConditionContext context);
        Task<ValueTuple<bool, string>> HandleAsync(IHandlerContext context, CancellationToken ct);
    }

    /// <summary>
    /// 处理器上下文接口
    /// 
    /// ★ 修改：新增 Signal 属性，用于信号读写（支持别名）
    /// </summary>
    public interface IHandlerContext
    {
        IProductionContext Production { get; }
        IDeviceManager Devices { get; }
        IDataFlowManager DataFlow { get; }

        /// <summary>
        /// ★ 新增：信号访问器
        /// 
        /// 提供 PLC 信号的读写能力，支持别名解析。
        /// 
        /// 使用示例：
        /// ```csharp
        /// // 读取信号（支持别名）
        /// bool hasMaterial = ctx.Signal.ReadBit("Material_Detect");
        /// 
        /// // 写入信号（支持别名）
        /// ctx.Signal.WriteBit("Gripper_Clamp", true);
        /// 
        /// // 写入脉冲
        /// ctx.Signal.WriteBitPulse("UpperPC_Start", 500);
        /// 
        /// // 等待信号
        /// await ctx.Signal.WaitForBitAsync("LaserPhoto_Complete", true, TimeSpan.FromSeconds(10), ct);
        /// ```
        /// 
        /// 注意：
        /// - 优先使用此属性进行 PLC 信号操作
        /// - 支持英文别名（如 "Material_Detect"）自动解析为 CSV 中文名
        /// - GetPlc() 返回的 IPlcDevice 不支持别名，使用时需传入西门子格式地址
        /// </summary>
        ISignalAccessor Signal { get; }

        T GetDevice<T>(string id) where T : class, IDevice;
        IPlcDevice GetPlc(string id = "PLC");
        IRobotDevice GetRobot(string id = "Robot");
        IVisionDevice GetVision(string id = "Vision");
        IScannerDevice GetScanner(string id = "Scanner");
        ILaserDevice GetLaser(string id = "HM_Laser");
        IVibratorDevice GetVibrator(string id = "Vibrator");

        void SetFlag<T>(string key, T value);
        T GetFlag<T>(string key, T defaultValue = default(T));
        bool RemoveFlag(string key);

        // ★★★ 新增方法 ★★★
        /// <summary>
        /// 获取注册的服务实例
        /// 
        /// 用于获取非设备类型的服务（如 ITraceService、IAlarmService 等）
        /// 
        /// 使用示例：
        /// ```csharp
        /// var traceService = ctx.GetService<ITraceService>();
        /// if (traceService != null)
        /// {
        ///     await traceService.CreatePairingAsync(...);
        /// }
        /// ```
        /// 
        /// 注意：
        /// - 此方法用于获取数据服务、业务服务等非硬件设备
        /// - 硬件设备请继续使用 GetDevice<T>() 或 GetPlc() 等方法
        /// - 如果服务未注册，返回 null
        /// </summary>
        /// <typeparam name="T">服务类型</typeparam>
        /// <returns>服务实例，未注册则返回 null</returns>
        T GetService<T>() where T : class;
    }

    /// <summary>
    /// 处理器执行器接口
    /// </summary>
    public interface IHandlerExecutor : IDisposable
    {
        event EventHandler<HandlerExecutedEventArgs> HandlerExecuted;
        event EventHandler<Exception> Error;

        void Register(ISignalHandler handler);
        void Register(params ISignalHandler[] handlers);
        void Unregister(string handlerId);
        IReadOnlyList<ISignalHandler> GetHandlers();

        void EvaluateAndExecute();
    }

    #endregion

    #region 系统监控接口

    /// <summary>
    /// 系统监控器接口
    /// </summary>
    public interface ISystemMonitor : INotifyPropertyChanged
    {
        int TotalModules { get; }
        int CheckedModules { get; }
        int Progress { get; }
        bool AllCriticalPassed { get; }

        event EventHandler<ModuleCheckedEventArgs> ModuleChecked;
        event EventHandler<bool> CheckCompleted;

        void RegisterModule(string moduleName, Func<ModuleCheckResult> checkFunc, bool isCritical = true);
        Task StartCheckAsync();
        ModuleCheckResult GetModuleResult(string moduleName);
        List<string> GetFailedCriticalModules();
        string GetSummary();
    }

    /// <summary>
    /// 模块检查结果
    /// </summary>
    public class ModuleCheckResult
    {
        public string ModuleName { get; set; }
        public bool IsCritical { get; set; }
        public ModuleStatus Status { get; set; }
        public string Message { get; set; }

        public ModuleCheckResult()
        {
            Status = ModuleStatus.NotChecked;
        }

        public ModuleCheckResult(string moduleName, bool isCritical)
        {
            ModuleName = moduleName;
            IsCritical = isCritical;
            Status = ModuleStatus.NotChecked;
        }
    }

    /// <summary>
    /// 模块状态
    /// </summary>
    public enum ModuleStatus
    {
        NotChecked,
        Checking,
        Success,
        Failed,
        Warning
    }

    #endregion

    #region 可选接口（预留扩展）

    

    /// <summary>
    /// 用户角色
    /// </summary>
    public enum UserRole
    {
        Operator = 0,
        Technician = 1,
        Engineer = 2,
        Administrator = 3
    }

    
    


    #endregion
}