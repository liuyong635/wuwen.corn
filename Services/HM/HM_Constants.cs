using System;

namespace SeedCut.Services.HM
{
    /// <summary>
    /// HM激光器相关常量定义
    /// </summary>
    public static class HM_Constants
    {
        #region 返回值定义

        /// <summary>
        /// 操作成功
        /// </summary>
        public const int HM_OK = 0x00000000;

        /// <summary>
        /// 操作失败
        /// </summary>
        public const int HM_FAILED = 0x00000001;

        #endregion

        #region 连接状态定义

        /// <summary>
        /// 已连接
        /// </summary>
        public const int HM_DEV_Connect = 0x00000000;

        /// <summary>
        /// 可连接（设备就绪）
        /// </summary>
        public const int HM_DEV_Ready = 0x00000001;

        /// <summary>
        /// 不可用
        /// </summary>
        public const int HM_DEV_NotAvailable = 0x00000002;

        #endregion

        #region 工作状态定义

        /// <summary>
        /// 就绪
        /// </summary>
        public const int HM_WorkStatus_Ready = 1;

        /// <summary>
        /// 运行中
        /// </summary>
        public const int HM_WorkStatus_Run = 2;

        /// <summary>
        /// 报警
        /// </summary>
        public const int HM_WorkStatus_Alarm = 3;

        #endregion

        #region Windows消息定义

        /// <summary>
        /// 设备状态更新 / 搜索到IP
        /// </summary>
        public const int HM_MSG_DeviceStatusUpdate = 5991;

        /// <summary>
        /// 文件下载完成
        /// </summary>
        public const int HM_MSG_StreamEnd = 6012;

        /// <summary>
        /// 打标完成
        /// </summary>
        public const int HM_MSG_MarkOver = 6035;

        /// <summary>
        /// 打标进度查询响应
        /// </summary>
        public const int HM_MSG_QueryExecProcess = 6037;

        #endregion

        #region 协议定义

        /// <summary>
        /// SPI协议
        /// </summary>
        public const int Protocol_SPI = 0;

        /// <summary>
        /// XY2-100协议
        /// </summary>
        public const int Protocol_XY2_100 = 1;

        /// <summary>
        /// SL2协议
        /// </summary>
        public const int Protocol_SL2 = 2;

        /// <summary>
        /// 2D打标
        /// </summary>
        public const int Dimensional_2D = 0;

        /// <summary>
        /// 3D打标
        /// </summary>
        public const int Dimensional_3D = 1;

        #endregion

        #region 默认网络配置

        /// <summary>
        /// 控制卡默认IP地址
        /// </summary>
        public const string DefaultIpAddress = "192.168.0.227";

        /// <summary>
        /// 控制卡上电时临时IP（避免使用）
        /// </summary>
        public const string TempIpAddress = "192.168.0.226";

        /// <summary>
        /// 电脑IP地址范围起始
        /// </summary>
        public const string PcIpRangeStart = "192.168.0.2";

        /// <summary>
        /// 电脑IP地址范围结束
        /// </summary>
        public const string PcIpRangeEnd = "192.168.0.123";

        #endregion
    }

    /// <summary>
    /// HM激光器工作状态枚举
    /// </summary>
    public enum HM_WorkStatus
    {
        /// <summary>
        /// 未知状态（通讯失败）
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// 就绪
        /// </summary>
        Ready = 1,

        /// <summary>
        /// 运行中
        /// </summary>
        Running = 2,

        /// <summary>
        /// 报警
        /// </summary>
        Alarm = 3
    }

    /// <summary>
    /// HM激光器连接状态枚举
    /// </summary>
    public enum HM_ConnectStatus
    {
        /// <summary>
        /// 已连接
        /// </summary>
        Connected = 0,

        /// <summary>
        /// 可连接（设备就绪）
        /// </summary>
        Ready = 1,

        /// <summary>
        /// 不可用
        /// </summary>
        NotAvailable = 2
    }
}