using System;
using System.Collections.Generic;

namespace SeedCut.Framework.Core
{
    #region 设备相关事件

    /// <summary>
    /// 设备连接状态变化事件参数
    /// </summary>
    public class DeviceConnectionChangedEventArgs : EventArgs
    {
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public DeviceConnectionState OldState { get; set; }
        public DeviceConnectionState NewState { get; set; }
        public bool IsConnected { get { return NewState == DeviceConnectionState.Connected; } }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }

        public DeviceConnectionChangedEventArgs()
        {
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// 设备错误事件参数
    /// </summary>
    public class DeviceErrorEventArgs : EventArgs
    {
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsCritical { get; set; }

        public DeviceErrorEventArgs()
        {
            Timestamp = DateTime.Now;
        }
    }

    #endregion

    #region 机器人相关事件

    /// <summary>
    /// 机器人状态变化事件参数
    /// </summary>
    public class RobotStateChangedEventArgs : EventArgs
    {
        public RobotState OldState { get; set; }
        public RobotState NewState { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }

        public RobotStateChangedEventArgs()
        {
            Timestamp = DateTime.Now;
        }
    }

    #endregion

    #region 信号相关事件

    /// <summary>
    /// 信号变化事件参数
    /// </summary>
    public class SignalChangedEventArgs : EventArgs
    {
        public string SignalName { get; set; }
        public bool OldValue { get; set; }
        public bool NewValue { get; set; }
        public SignalEdge Edge { get { return NewValue ? SignalEdge.Rising : SignalEdge.Falling; } }
        public DateTime Timestamp { get; set; }

        public SignalChangedEventArgs()
        {
            Timestamp = DateTime.Now;
        }
    }

    #endregion

    #region Task相关事件

    /// <summary>
    /// 任务完成事件参数
    /// 当 Handler 执行完成时，通过此参数传递执行结果信息
    /// </summary>
    public class TaskCompletedEventArgs : EventArgs
    {
        /// <summary>
        /// 任务ID（通常是 HandlerId）
        /// </summary>
        public string TaskId { get; set; }

        /// <summary>
        /// 任务名称（通常是 HandlerName）
        /// </summary>
        public string TaskName { get; set; }

        /// <summary>
        /// 是否执行成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 执行结果消息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 执行耗时
        /// </summary>
        public TimeSpan Duration { get; set; }
    }


    #endregion

    #region 处理器相关事件

    /// <summary>
    /// 处理器执行完成事件参数
    /// </summary>
    public class HandlerExecutedEventArgs : EventArgs
    {
        public string HandlerId { get; set; }
        public string HandlerName { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime ExecutedTime { get; set; }

        public HandlerExecutedEventArgs()
        {
            ExecutedTime = DateTime.Now;
        }

        public HandlerExecutedEventArgs(string handlerId, string handlerName, bool success, string message, TimeSpan duration)
        {
            HandlerId = handlerId;
            HandlerName = handlerName;
            Success = success;
            Message = message;
            Duration = duration;
            ExecutedTime = DateTime.Now;
        }
    }

    #endregion

    // ============================================================
    // 注意：报警相关事件参数已移至 IAlarmService.cs
    // - AlarmTriggeredEventArgs
    // - AlarmRecoveredEventArgs
    // ============================================================

    #region 激光器相关事件

    /// <summary>
    /// 激光器响应事件参数
    /// </summary>
    public class LaserResponseEventArgs : EventArgs
    {
        public string Response { get; set; }
        public string ResponseType { get; set; }
        public string OriginalCommand { get; set; }
        public DateTime Timestamp { get; set; }

        public LaserResponseEventArgs()
        {
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// 激光打标完成事件参数
    /// </summary>
    public class LaserMarkFinishedEventArgs : EventArgs
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public DateTime FinishTime { get; set; }
        public long ElapsedMs { get; set; }

        public LaserMarkFinishedEventArgs()
        {
            FinishTime = DateTime.Now;
        }
    }

    #endregion

    #region TCP通信相关事件

    /// <summary>
    /// TCP数据接收事件参数
    /// </summary>
    public class TcpDataReceivedEventArgs : EventArgs
    {
        public string ConnectionId { get; set; }
        public string RemoteEndpoint { get; set; }
        public byte[] RawData { get; set; }
        public string TextData { get; set; }
        public DateTime Timestamp { get; set; }

        public TcpDataReceivedEventArgs()
        {
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// TCP客户端连接事件参数
    /// </summary>
    public class TcpClientConnectedEventArgs : EventArgs
    {
        public string ConnectionId { get; set; }
        public string RemoteEndpoint { get; set; }
        public DateTime ConnectedTime { get; set; }

        public TcpClientConnectedEventArgs()
        {
            ConnectedTime = DateTime.Now;
        }
    }

    /// <summary>
    /// TCP客户端断开事件参数
    /// </summary>
    public class TcpClientDisconnectedEventArgs : EventArgs
    {
        public string ConnectionId { get; set; }
        public string RemoteEndpoint { get; set; }
        public string Reason { get; set; }
        public DateTime DisconnectedTime { get; set; }

        public TcpClientDisconnectedEventArgs()
        {
            DisconnectedTime = DateTime.Now;
        }
    }

    #endregion

    #region IO相关事件

    /// <summary>
    /// IO点位变化事件参数
    /// </summary>
    public class IOChangedEventArgs : EventArgs
    {
        public string IOName { get; set; }
        public string Address { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }
        public DateTime Timestamp { get; set; }

        public IOChangedEventArgs()
        {
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// IO批量更新事件参数
    /// </summary>
    public class IOBatchUpdatedEventArgs : EventArgs
    {
        public Dictionary<string, object> UpdatedValues { get; set; }
        public DateTime Timestamp { get; set; }

        public IOBatchUpdatedEventArgs()
        {
            UpdatedValues = new Dictionary<string, object>();
            Timestamp = DateTime.Now;
        }
    }

    #endregion

    #region 配置相关事件

    /// <summary>
    /// 配置变更事件参数
    /// </summary>
    public class ConfigChangedEventArgs : EventArgs
    {
        public string Section { get; set; }
        public string Key { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }
        public DateTime Timestamp { get; set; }

        public ConfigChangedEventArgs()
        {
            Timestamp = DateTime.Now;
        }
    }

    #endregion

    #region 振动盘相关事件

    /// <summary>
    /// 振动盘状态变化事件参数
    /// </summary>
    public class VibratorStateChangedEventArgs : EventArgs
    {
        public VibratorState OldState { get; set; }
        public VibratorState NewState { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }

        public VibratorStateChangedEventArgs()
        {
            Timestamp = DateTime.Now;
        }
    }

    #endregion

    #region 系统监控相关事件

    /// <summary>
    /// 模块检查事件参数
    /// </summary>
    public class ModuleCheckedEventArgs : EventArgs
    {
        public string ModuleName { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public bool IsCritical { get; set; }
    }

    #endregion
}