using SeedCut.Framework.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Interfaces
{
    /// <summary>
    /// 激光设备接口
    /// 参考现有LaserService实现，上位机作为TCP Server，激光器作为Client主动连接
    /// 
    /// ★ v2 改动：新增 AddLinesAndMarkMultiPassAsync 方法
    /// </summary>
    public interface ILaserDevice : IDevice
    {
        #region 状态属性

        /// <summary>
        /// 激光器状态
        /// </summary>
        LaserState LaserState { get; }

        /// <summary>
        /// 是否正在打标
        /// </summary>
        bool IsMarking { get; }

        /// <summary>
        /// 打标进度 (0-100)
        /// </summary>
        int MarkProgress { get; }

        /// <summary>
        /// TCP服务器是否已启动
        /// </summary>
        bool IsServerStarted { get; }

        /// <summary>
        /// 指令队列长度
        /// </summary>
        int QueueLength { get; }

        #endregion

        #region 事件

        /// <summary>
        /// 激光器响应事件
        /// </summary>
        event EventHandler<LaserResponseEventArgs> ResponseReceived;

        /// <summary>
        /// 打标完成事件
        /// </summary>
        event EventHandler<LaserMarkFinishedEventArgs> MarkFinished;

        /// <summary>
        /// 状态变化事件
        /// </summary>
        event EventHandler<string> StatusChanged;

        #endregion

        #region TCP服务器管理

        /// <summary>
        /// 启动TCP服务器，等待激光器连接
        /// </summary>
        Task<bool> StartServerAsync(int port = 0, CancellationToken ct = default);

        /// <summary>
        /// 停止TCP服务器
        /// </summary>
        Task StopServerAsync();

        #endregion

        #region 基础指令

        /// <summary>
        /// 发送原始指令
        /// </summary>
        Task<bool> SendCommandAsync(string command, CancellationToken ct = default);

        /// <summary>
        /// 开始打标
        /// </summary>
        Task<bool> MarkAsync(CancellationToken ct = default);

        /// <summary>
        /// 停止打标
        /// </summary>
        Task<bool> StopMarkAsync(CancellationToken ct = default);

        /// <summary>
        /// 暂停打标
        /// </summary>
        Task<bool> PauseMarkAsync(CancellationToken ct = default);

        /// <summary>
        /// 继续打标
        /// </summary>
        Task<bool> ResumeMarkAsync(CancellationToken ct = default);

        /// <summary>
        /// 红光预览
        /// </summary>
        Task<bool> RedLightPreviewAsync(CancellationToken ct = default);

        /// <summary>
        /// 停止红光
        /// </summary>
        Task<bool> StopRedLightAsync(CancellationToken ct = default);

        #endregion

        #region 文件和图层操作

        /// <summary>
        /// 加载打标文件
        /// </summary>
        Task<bool> LoadFileAsync(string filePath, CancellationToken ct = default);

        /// <summary>
        /// 清空打标内容
        /// </summary>
        Task<bool> ClearContentAsync(CancellationToken ct = default);

        /// <summary>
        /// 设置图层偏移
        /// </summary>
        Task<bool> SetOffsetAsync(int layerId, float offsetX, float offsetY, CancellationToken ct = default);

        /// <summary>
        /// 打标指定图层
        /// </summary>
        Task<bool> MarkLayerAsync(int layerId, CancellationToken ct = default);

        /// <summary>
        /// 设置激光能量
        /// </summary>
        Task<bool> SetLaserPowerAsync(int layerId, int power, CancellationToken ct = default);

        #endregion

        #region 线段切割（核心功能）

        /// <summary>
        /// 添加切割线段
        /// 坐标格式: "x1,y1,x2,y2" 或 "x1,y1,x2,y2;x3,y3,x4,y4"
        /// </summary>
        Task<bool> AddCutLineAsync(string coordinates, CancellationToken ct = default);

        /// <summary>
        /// 添加切割线段并立即打标
        /// </summary>
        Task<bool> AddCutLineAndMarkAsync(string coordinates, CancellationToken ct = default);

        /// <summary>
        /// 批量添加切割线段
        /// </summary>
        Task<bool> AddCutLinesAsync(IEnumerable<(float x1, float y1, float x2, float y2)> lines, CancellationToken ct = default);

        /// <summary>
        /// ★ 多Pass打标：同一图形以不同参数执行N次（1~5次）
        /// 
        /// HM控制卡实现：利用SDK图层机制，一次下载+一次打标指令，
        /// 控制卡硬件层面按图层顺序依次执行，时序精确无通信延迟。
        /// </summary>
        /// <param name="request">多Pass请求参数（坐标、逐次参数、间隔时间）</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>是否全部成功</returns>
        Task<bool> AddLinesAndMarkMultiPassAsync(MultiPassMarkRequest request, CancellationToken ct = default);

        #endregion

        #region 队列管理

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

        #endregion

        #region 安全检查

        /// <summary>
        /// 检查坐标是否在工作区域内
        /// </summary>
        bool IsInWorkArea(float x, float y);

        /// <summary>
        /// 获取坐标边界框
        /// </summary>
        (double minX, double minY, double maxX, double maxY) GetCoordinateBounds(string coordinates);

        #endregion


        /// <summary>
        /// 区域填充打标
        /// 输入格式: AddAreas[x1,y1,x2,y2:x3,y3,x4,y4:...]
        /// 线段首尾相连构成闭环多边形，内部用蛇形路径填充
        /// </summary>
        Task<bool> FillAreaAsync(string areaCoordinates, CancellationToken ct = default);
    }

    /// <summary>
    /// 激光设备配置
    /// </summary>
    public class LaserDeviceConfig
    {
        public int Port { get; set; } = 2000;
        public int CommandTimeoutMs { get; set; } = 5000;
        public int MaxQueueLength { get; set; } = 100;
        public bool AutoSendMark { get; set; } = true;

        // 工作区域
        public double WorkAreaMinX { get; set; } = -50;
        public double WorkAreaMaxX { get; set; } = 50;
        public double WorkAreaMinY { get; set; } = -50;
        public double WorkAreaMaxY { get; set; } = 50;

        // 安全设置
        public bool EnableSafetyCheck { get; set; } = true;
        public bool EnableCoordinateValidation { get; set; } = true;
    }
}