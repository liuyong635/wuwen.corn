using System;

namespace SeedCut.Framework.Services.Execution
{
    /// <summary>
    /// 设备断线异常
    /// </summary>
    public class DeviceDisconnectedException : Exception
    {
        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName { get; }

        /// <summary>
        /// 设备ID
        /// </summary>
        public string DeviceId { get; }

        public DeviceDisconnectedException(string deviceName, string deviceId)
            : base(string.Format("设备 [{0}] 已断线", deviceName))
        {
            DeviceName = deviceName;
            DeviceId = deviceId;
        }

        public DeviceDisconnectedException(string deviceName, string deviceId, Exception innerException)
            : base(string.Format("设备 [{0}] 已断线", deviceName), innerException)
        {
            DeviceName = deviceName;
            DeviceId = deviceId;
        }
    }

    /// <summary>
    /// Handler执行超时异常
    /// </summary>
    public class HandlerTimeoutException : Exception
    {
        /// <summary>
        /// Handler ID
        /// </summary>
        public string HandlerId { get; }

        /// <summary>
        /// 配置的超时时间
        /// </summary>
        public TimeSpan ConfiguredTimeout { get; }

        public HandlerTimeoutException(string handlerId, TimeSpan timeout)
            : base(string.Format("Handler [{0}] 执行超时 ({1}秒)", handlerId, timeout.TotalSeconds))
        {
            HandlerId = handlerId;
            ConfiguredTimeout = timeout;
        }
    }

    /// <summary>
    /// 系统暂停异常
    /// </summary>
    public class SystemPausedException : Exception
    {
        public SystemPausedException()
            : base("系统已暂停")
        {
        }

        public SystemPausedException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// 急停异常
    /// </summary>
    public class EmergencyStopException : Exception
    {
        /// <summary>
        /// 急停原因
        /// </summary>
        public string Reason { get; }

        public EmergencyStopException(string reason)
            : base(string.Format("急停: {0}", reason))
        {
            Reason = reason;
        }
    }
}