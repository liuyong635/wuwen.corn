using System;

namespace SeedCut.Framework.Services.Interfaces
{
    /// <summary>
    /// 激光器状态枚举
    /// </summary>
    public enum LaserState
    {
        /// <summary>
        /// 空闲状态
        /// </summary>
        Idle,

        /// <summary>
        /// 连接中
        /// </summary>
        Connecting,

        /// <summary>
        /// 正在打标
        /// </summary>
        Marking,

        /// <summary>
        /// 打标暂停
        /// </summary>
        Paused,

        /// <summary>
        /// 红光预览中
        /// </summary>
        Previewing,

        /// <summary>
        /// 错误状态
        /// </summary>
        Error
    }

    /// <summary>
    /// 激光器响应事件参数（Framework层定义）
    /// </summary>
    public class LaserResponseEventArgs : EventArgs
    {
        /// <summary>
        /// 响应内容
        /// </summary>
        public string Response { get; set; }

        /// <summary>
        /// 接收时间
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 响应类型
        /// </summary>
        public LaserResponseType ResponseType { get; set; }

        /// <summary>
        /// 对应的原始指令（如果有）
        /// </summary>
        public string OriginalCommand { get; set; }
    }

    /// <summary>
    /// 打标完成事件参数（Framework层定义）
    /// </summary>
    public class LaserMarkFinishedEventArgs : EventArgs
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 完成时间
        /// </summary>
        public DateTime FinishTime { get; set; }

        /// <summary>
        /// 打标耗时（毫秒）
        /// </summary>
        public long ElapsedMs { get; set; }
    }

    /// <summary>
    /// 激光器响应类型（Framework层定义）
    /// </summary>
    public enum LaserResponseType
    {
        /// <summary>
        /// 未知
        /// </summary>
        Unknown,

        /// <summary>
        /// OK - 指令接收成功
        /// </summary>
        OK,

        /// <summary>
        /// FAILED - 指令失败
        /// </summary>
        FAILED,

        /// <summary>
        /// FINISH - 打标完成
        /// </summary>
        FINISH,

        /// <summary>
        /// 进度值（数字）
        /// </summary>
        Progress
    }
}