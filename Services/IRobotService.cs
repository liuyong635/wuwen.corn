using SeedCut.Framework.Services.Handlers;
using SeedCut.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SeedCut.Services
{
    /// <summary>
    /// 机器人坐标数据
    /// </summary>
    public class RobotCoordinates
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float C { get; set; }

        public override string ToString()
        {
            return $"X:{X:F2}, Y:{Y:F2}, Z:{Z:F2}, C:{C:F2}";
        }
    }

    /// <summary>
    /// 机器人连接状态变化事件参数
    /// </summary>
    public class ConnectionStatusChangedEventArgs : EventArgs
    {
        public bool IsConnected { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// 机器人坐标更新事件参数
    /// </summary>
    public class CoordinatesUpdatedEventArgs : EventArgs
    {
        public RobotCoordinates Coordinates { get; set; }
    }

    /// <summary>
    /// 机器人命令事件参数
    /// </summary>
    public class RobotCommandEventArgs : EventArgs
    {
        /// <summary>
        /// 命令字符串
        /// </summary>
        public string Command { get; set; }

        /// <summary>
        /// 接收时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 解析后的命令类型
        /// </summary>
        public RobotCommandType CommandType { get; set; }
    }

    

    /// <summary>
    /// 机器人服务接口
    /// </summary>
    public interface IRobotService : IDisposable
    {
        #region 属性

        /// <summary>
        /// 是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 当前配置
        /// </summary>
        RobotConfig Config { get; }

        /// <summary>
        /// 当前坐标
        /// </summary>
        RobotCoordinates CurrentCoordinates { get; }

        /// <summary>
        /// 伺服是否使能
        /// </summary>
        bool IsServoEnabled { get; }

        /// <summary>
        /// AR客户端是否已连接
        /// </summary>
        bool IsArClientConnected { get; }

        /// <summary>
        /// TCP服务器是否在运行
        /// </summary>
        bool IsTcpServerRunning { get; }
        #endregion

        #region 事件

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        event EventHandler<ConnectionStatusChangedEventArgs> ConnectionStatusChanged;

        /// <summary>
        /// 坐标更新事件
        /// </summary>
        event EventHandler<CoordinatesUpdatedEventArgs> CoordinatesUpdated;

        /// <summary>
        /// 收到AR程序命令
        /// </summary>
        event EventHandler<RobotCommandEventArgs> CommandReceived;

        /// <summary>
        /// AR程序连接状态变化
        /// </summary>
        event EventHandler<bool> ArClientConnectionChanged;

        #endregion

        #region 连接管理

        /// <summary>
        /// 连接机器人
        /// </summary>
        Task<(bool success, string message)> ConnectAsync();

        /// <summary>
        /// 断开连接
        /// </summary>
        Task<(bool success, string message)> DisconnectAsync();

        #endregion

        #region 基本控制

        /// <summary>
        /// 读取笛卡尔坐标
        /// </summary>
        Task<RobotCoordinates> ReadCoordinatesAsync();

        /// <summary>
        /// 获取伺服使能状态
        /// </summary>
        Task<bool> GetServoEnableAsync();

        /// <summary>
        /// 设置伺服使能
        /// </summary>
        Task<(bool success, string message)> SetServoEnableAsync(bool enable);

        #endregion

        #region AR 程序控制

        /// <summary>
        /// 运行机器人程序
        /// </summary>
        Task<(bool success, string message)> RunARAsync();

        /// <summary>
        /// 暂停机器人程序
        /// </summary>
        Task<(bool success, string message)> PauseARAsync();

        /// <summary>
        /// 停止机器人程序
        /// </summary>
        Task<(bool success, string message)> StopARAsync();

        /// <summary>
        /// 复位机器人
        /// </summary>
        Task<(bool success, string message)> ResetRobotAsync();

        #endregion

        #region JOG 运动控制

        /// <summary>
        /// JOG 运动（X, Y, Z, C 轴）
        /// </summary>
        /// <param name="axis">轴（0=X+, 1=X-, 2=Y+, 3=Y-, 4=Z+, 5=Z-, 6=C+, 7=C-）</param>
        Task<(bool success, string message)> JogAsync(int axis);

        /// <summary>
        /// 停止 JOG 运动
        /// </summary>
        Task<(bool success, string message)> StopJogAsync();

        #endregion

        #region 配置管理

        /// <summary>
        /// 重新加载配置
        /// </summary>
        void ReloadConfig();

        /// <summary>
        /// 保存配置
        /// </summary>
        void SaveConfig();

        #endregion

        #region TCP服务器管理

        /// <summary>
        /// 启动TCP服务器（用于AR程序通信）
        /// </summary>
        /// <param name="port">监听端口，默认6000</param>
        Task<(bool success, string message)> StartTcpServerAsync(int port = 6000);




        /// <summary>
        /// 停止TCP服务器
        /// </summary>
        void StopTcpServer();

        /// <summary>
        /// 发送命令到AR程序
        /// </summary>
        /// <param name="command">命令字符串</param>
        Task<bool> SendCommandAsync(string command);

        /// <summary>
        /// 发送坐标数据到AR程序
        /// 格式：X0;Y0;A0;X1;Y1;A1;...
        /// </summary>
        Task<bool> SendCoordinatesAsync(List<VisionCoordinate> coordinates);

        #endregion


    }

    #region 视觉坐标数据结构

    /// <summary>
    /// 视觉坐标数据
    /// </summary>
    public class VisionCoordinate
    {
        public int Index { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Angle { get; set; }
        public DateTime Timestamp { get; set; }

        public override string ToString()
        {
            return string.Format("{0:F2};{1:F2};{2:F1}", X, Y, Angle);
        }
    }

    #endregion
}