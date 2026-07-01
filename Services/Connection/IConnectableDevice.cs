using SeedCut.Framework.Core;
using System;

namespace SeedCut.Services.Connection
{
    /// <summary>
    /// 可连接设备接口 - 所有需要断线重连的设备都实现此接口
    /// </summary>
    public interface IConnectableDevice
    {
        /// <summary>
        /// 设备唯一标识符
        /// </summary>
        string DeviceId { get; }

        /// <summary>
        /// 设备显示名称
        /// </summary>
        string DeviceName { get; }

        /// <summary>
        /// 设备类型
        /// </summary>
        DeviceType DeviceType { get; }

        /// <summary>
        /// 是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

        /// <summary>
        /// 连接设备（同步方法）
        /// </summary>
        /// <returns>连接是否成功</returns>
        bool Connect();

        /// <summary>
        /// 断开设备
        /// </summary>
        void Disconnect();

        /// <summary>
        /// 检查连接状态（心跳检测）
        /// </summary>
        /// <returns>设备是否在线</returns>
        bool CheckConnection();

        /// <summary>
        /// 获取重连配置（可选，用于覆盖默认配置）
        /// </summary>
        ReconnectionConfig GetReconnectionConfig();
    }

    

    /// <summary>
    /// 连接状态变化事件参数
    /// </summary>
    public class ConnectionStateChangedEventArgs : EventArgs
    {
        public bool IsConnected { get; set; }
        public string Message { get; set; }
        public DateTime ChangeTime { get; set; }
        public Exception Exception { get; set; }

        public ConnectionStateChangedEventArgs(bool isConnected, string message = null, Exception ex = null)
        {
            IsConnected = isConnected;
            Message = message;
            ChangeTime = DateTime.Now;
            Exception = ex;
        }
    }
}