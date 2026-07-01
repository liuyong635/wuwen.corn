namespace SeedCut.Framework.Core
{
    /// <summary>
    /// 系统状态
    /// </summary>
    public enum SystemState
    {
        Idle,           // 空闲
        Initializing,   // 初始化中
        Ready,          // 就绪
        Running,        // 运行中
        Paused,         // 暂停
        Stopping,       // 停止中
        Error,          // 错误
        EmergencyStopped // 急停
    }




    /// <summary>
    /// 运行子状态
    /// </summary>
    public enum RunningSubState
    {
        None,
        WaitingForMaterial,  // 等待来料
        MaterialReady,       // 来料就绪
        Processing,          // 加工中
        WaitingForTransfer,  // 等待转移
        Transferring,        // 转移中
        Completing,          // 完成中
        Clearing             // 清料中
    }

    /// <summary>
    /// 任务执行状态
    /// 
    /// 用于追踪 Handler 的执行状态
    /// </summary>
    public enum TaskExecutionStatus
    {
        /// <summary>空闲，未执行</summary>
        Idle,

        /// <summary>正在执行</summary>
        Running,

        /// <summary>执行成功完成</summary>
        Completed,

        /// <summary>执行失败</summary>
        Failed,

        /// <summary>被取消</summary>
        Cancelled
    }

    /// <summary>
    /// 设备类型
    /// </summary>
    public enum DeviceType
    {
        PLC,
        Robot,
        Vision,
        Camera,
        Scanner,
        Laser,
        Vibrator,
        Other
    }

    /// <summary>
    /// 设备连接状态
    /// </summary>
    public enum DeviceConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Disconnecting,
        Error,
        Reconnecting
    }

    /// <summary>
    /// 机器人状态
    /// </summary>
    public enum RobotState
    {
        Unknown,
        Idle,
        Running,
        Paused,
        Stopped,
        Error,
        Alarm,
        Emergency
    }

    /// <summary>
    /// 信号边沿
    /// </summary>
    public enum SignalEdge
    {
        Rising,     // 上升沿 (0->1)
        Falling,    // 下降沿 (1->0)
        Both        // 双边沿
    }

    

    /// <summary>
    /// 振动盘状态
    /// </summary>
    public enum VibratorState
    {
        Unknown,
        Idle,
        Vibrating,
        Feeding,
        Error,
        Disconnected
    }

    /// <summary>
    /// TCP连接角色
    /// </summary>
    public enum TcpRole
    {
        Server,
        Client
    }

    /// <summary>
    /// 配置数据类型
    /// </summary>
    public enum ConfigDataType
    {
        String,
        Int,
        Float,
        Bool,
        DateTime,
        Json
    }



}