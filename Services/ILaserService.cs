using SeedCut.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SeedCut.Services
{
    /// <summary>
    /// 激光器服务接口
    /// </summary>
    public interface ILaserService : IDisposable
    {
        /// <summary>
        /// 配置信息
        /// </summary>
        LaserConfig Config { get; }

        /// <summary>
        /// 是否已连接（激光器是否已连接到上位机）
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 是否正在打标
        /// </summary>
        bool IsMarking { get; }

        /// <summary>
        /// 当前打标进度（0-100）
        /// </summary>
        int MarkProgress { get; }

        /// <summary>
        /// 指令队列长度
        /// </summary>
        int QueueLength { get; }

        // ========== 事件 ==========

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        event EventHandler<bool> ConnectionChanged;

        /// <summary>
        /// 激光器响应事件
        /// </summary>
        event EventHandler<LaserResponseEventArgs> ResponseReceived;

        /// <summary>
        /// 打标完成事件
        /// </summary>
        event EventHandler<LaserMarkFinishedEventArgs> MarkFinished;

        /// <summary>
        /// 状态变化事件（用于UI更新）
        /// </summary>
        event EventHandler<string> StatusChanged;

        /// <summary>
        /// 错误事件
        /// </summary>
        event EventHandler<string> ErrorOccurred;

        // ========== 连接管理 ==========

        /// <summary>
        /// 启动TCP服务器（等待激光器连接）
        /// </summary>
        Task<bool> StartServerAsync();

        /// <summary>
        /// 断开连接
        /// </summary>
        Task DisconnectAsync();

        // ========== 基础指令 ==========

        /// <summary>
        /// 发送自定义指令
        /// </summary>
        Task<bool> SendCommandAsync(string command);

        /// <summary>
        /// 开始打标
        /// </summary>
        Task<bool> MarkAsync();

        /// <summary>
        /// 停止打标
        /// </summary>
        Task<bool> StopMarkAsync();

        /// <summary>
        /// 查询打标进度
        /// </summary>
        Task<int> GetMarkProgressAsync();

        // ========== 文档管理 ==========

        /// <summary>
        /// 加载HSD文档
        /// </summary>
        Task<bool> LoadHsdFileAsync(string filePath);

        /// <summary>
        /// 切换文档（按ID）
        /// </summary>
        Task<bool> SwitchDocumentByIdAsync(int docId);

        /// <summary>
        /// 切换文档（按名称）
        /// </summary>
        Task<bool> SwitchDocumentByNameAsync(string docName);

        // ========== 图形操作 ==========

        /// <summary>
        /// 添加多边形（切割路径）
        /// </summary>
        /// <param name="coordinates">坐标字符串，格式: "x1,y1,x2,y2:x3,y3,x4,y4;..."</param>
        Task<bool> AddLinesAsync(string coordinates);

        /// <summary>
        /// 添加填充图形
        /// </summary>
        Task<bool> AddAreasAsync(string coordinates);

        // ========== 参数设置 ==========

        /// <summary>
        /// 修改条码内容
        /// </summary>
        Task<bool> ChangeTextAsync(int barcodeId, string content);

        /// <summary>
        /// 设置旋转参数
        /// </summary>
        Task<bool> SetRotateAsync(int layerId, float angle, float centerX, float centerY);

        /// <summary>
        /// 设置偏移参数
        /// </summary>
        Task<bool> SetOffsetAsync(int layerId, float offsetX, float offsetY);

        /// <summary>
        /// 打标指定图层
        /// </summary>
        Task<bool> MarkLayerAsync(int layerId);

        /// <summary>
        /// 设置激光能量
        /// </summary>
        Task<bool> SetLaserPowerAsync(int layerId, int power);

        // ========== 队列管理 ==========

        /// <summary>
        /// 添加指令到队列
        /// </summary>
        void EnqueueCommand(string command);

        /// <summary>
        /// 清空指令队列
        /// </summary>
        void ClearQueue();

        /// <summary>
        /// 获取队列中的所有指令
        /// </summary>
        List<string> GetQueuedCommands();
    }

    /// <summary>
    /// 激光器响应事件参数
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
    /// 打标完成事件参数
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
    /// 激光器响应类型
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