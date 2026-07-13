using SeedCut.Framework.Services.Interfaces;
using SeedCut.Models;
using SeedCut.Services.HM;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SeedCut.Services
{
    /// <summary>
    /// HM激光器服务（GMC控制卡）
    /// 
    /// 职责：
    /// 1. 封装HM DLL的调用
    /// 2. 管理与控制卡的连接
    /// 3. 处理Windows消息回调
    /// 4. 生成UDM文件并下载执行
    /// 
    /// 架构说明：
    /// - 上位机作为Client，主动连接控制卡
    /// - 通过Windows消息接收DLL的异步回调
    /// - 使用UDM格式生成打标数据
    /// </summary>
    public sealed class HM_LaserService : IDisposable
    {
        #region 私有字段

        private readonly ILogService _log;
        private readonly HM_LaserConfig _config;

        // 消息窗口（用于接收DLL回调消息）
        private MessageWindow _messageWindow;
        private int _ipIndex = -1;

        // 状态标志
        private volatile bool _isInitialized;
        private volatile bool _isConnected;
        private volatile bool _isMarking;
        private volatile bool _isDownloading;
        private int _markProgress;
        private readonly Stopwatch _markTimer = new Stopwatch();

        // 同步事件
        private TaskCompletionSource<bool> _downloadTcs;
        private TaskCompletionSource<bool> _markTcs;
        private readonly object _lock = new object();

        // 释放标志
        private bool _disposed;

        #endregion

        #region 公开属性

        /// <summary>
        /// 配置信息
        /// </summary>
        public HM_LaserConfig Config
        {
            get { return _config; }
        }

        /// <summary>
        /// DLL是否已初始化
        /// </summary>
        public bool IsInitialized
        {
            get { return _isInitialized; }
        }

        /// <summary>
        /// 是否已连接控制卡
        /// </summary>
        public bool IsConnected
        {
            get { return _isConnected; }
        }

        /// <summary>
        /// 是否正在打标
        /// </summary>
        public bool IsMarking
        {
            get { return _isMarking; }
        }

        /// <summary>
        /// 是否正在下载文件
        /// </summary>
        public bool IsDownloading
        {
            get { return _isDownloading; }
        }

        /// <summary>
        /// 打标进度 (0-100)
        /// </summary>
        public int MarkProgress
        {
            get { return _markProgress; }
        }

        /// <summary>
        /// 当前IP索引
        /// </summary>
        public int IpIndex
        {
            get { return _ipIndex; }
        }

        #endregion

        #region 事件

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event EventHandler<bool> ConnectionChanged;

        /// <summary>
        /// 打标进度变化事件
        /// </summary>
        public event EventHandler<int> ProgressChanged;

        /// <summary>
        /// 打标完成事件
        /// </summary>
        public event EventHandler<HM_MarkFinishedEventArgs> MarkFinished;

        /// <summary>
        /// 下载完成事件
        /// </summary>
        public event EventHandler DownloadCompleted;

        /// <summary>
        /// 状态变化事件
        /// </summary>
        public event EventHandler<string> StatusChanged;

        /// <summary>
        /// 错误发生事件
        /// </summary>
        public event EventHandler<string> ErrorOccurred;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建HM激光器服务
        /// </summary>
        /// <param name="logService">日志服务</param>
        public HM_LaserService(ILogService logService)
        {
            _log = logService ?? throw new ArgumentNullException(nameof(logService));
            _config = HM_LaserConfig.Load();

            LogInfo("HM激光器服务初始化");
            LogInfo(_config.ToString());
        }

        /// <summary>
        /// 创建HM激光器服务（使用指定配置）
        /// </summary>
        public HM_LaserService(ILogService logService, HM_LaserConfig config)
        {
            _log = logService ?? throw new ArgumentNullException(nameof(logService));
            _config = config ?? HM_LaserConfig.CreateDefault();

            LogInfo("HM激光器服务初始化（自定义配置）");
            LogInfo(_config.ToString());
        }

        #endregion

        #region 初始化与连接

        /// <summary>
        /// 初始化DLL
        /// 注意：必须在UI线程调用（因为需要创建消息窗口）
        /// </summary>
        public bool Initialize()
        {
            if (_isInitialized)
            {
                LogDebug("DLL已初始化，跳过");
                return true;
            }

            try
            {
                // 检查DLL是否可用
                if (!HM_HashuScanDLL.IsDllAvailable())
                {
                    LogError("HM_HashuScan.dll 不可用，请确保DLL文件在程序目录下");
                    RaiseError("HM_HashuScan.dll 不可用");
                    return false;
                }

                if (!HM_UDM_DLL.IsDllAvailable())
                {
                    LogError("HM_HashuUDM.dll 不可用，请确保DLL文件在程序目录下");
                    RaiseError("HM_HashuUDM.dll 不可用");
                    return false;
                }

                // 创建消息窗口
                _messageWindow = new MessageWindow(this);

                // 初始化DLL
                var result = HM_HashuScanDLL.HM_InitBoard(_messageWindow.Handle);

                if (result == HM_Constants.HM_OK)
                {
                    _isInitialized = true;
                    LogInfo("HM DLL 初始化成功");
                    RaiseStatus("DLL初始化成功");
                    return true;
                }
                else
                {
                    LogError(string.Format("HM DLL 初始化失败，错误码: {0}", result));
                    RaiseError(string.Format("DLL初始化失败，错误码: {0}", result));
                    return false;
                }
            }
            catch (DllNotFoundException ex)
            {
                LogError(string.Format("DLL文件未找到: {0}", ex.Message));
                RaiseError("DLL文件未找到，请检查HM_HashuScan.dll和HM_Comm.dll是否存在");
                return false;
            }
            catch (Exception ex)
            {
                LogError(string.Format("初始化异常: {0}", ex.Message));
                RaiseError(string.Format("初始化异常: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 连接控制卡
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            if (!_isInitialized)
            {
                if (!Initialize())
                {
                    return false;
                }

                
            }

            if (_isConnected)
            {
                LogDebug("已连接，跳过");
                return true;
            }

            try
            {
                LogInfo(string.Format("正在连接控制卡: {0}", _config.IpAddress));
                RaiseStatus(string.Format("正在连接 {0}...", _config.IpAddress));


                // ========== 新增：等待设备出现在搜索列表中 ==========
                int maxWaitMs = 5000;      // 最大等待5秒
                int elapsed = 0;
                int checkInterval = 200;   // 每200ms检查一次

                while (elapsed < maxWaitMs)
                {
                    var index = HM_HashuScanDLL.HM_GetIndexByIpAddr(_config.IpAddress);
                    if (index >= 0)
                    {
                        LogDebug(string.Format("设备已找到，索引: {0}，耗时: {1}ms", index, elapsed));
                        break;
                    }

                    await Task.Delay(checkInterval);
                    elapsed += checkInterval;

                    // 每秒输出一次等待日志
                    if (elapsed % 1000 == 0)
                    {
                        LogDebug(string.Format("等待设备搜索... {0}ms", elapsed));
                    }
                }

                // 通过IP连接
                var result = HM_HashuScanDLL.HM_ConnectByIpStr(_config.IpAddress);

                if (result == HM_Constants.HM_OK)
                {
                    // 获取IP索引
                    _ipIndex = HM_HashuScanDLL.HM_GetIndexByIpAddr(_config.IpAddress);

                    if (_ipIndex < 0)
                    {
                        LogError("获取IP索引失败");
                        RaiseError("获取IP索引失败");
                        return false;
                    }

                    // 检查连接状态
                    var status = HM_HashuScanDLL.HM_GetConnectStatus(_ipIndex);
                    if (status == HM_Constants.HM_DEV_Connect)
                    {
                        _isConnected = true;
                        LogInfo(string.Format("连接成功，IP索引: {0}", _ipIndex));
                        RaiseStatus("已连接");
                        RaiseConnectionChanged(true);
                        return true;
                    }
                    else
                    {
                        LogError(string.Format("连接状态异常: {0}",
                            HM_HashuScanDLL.GetConnectStatusDescription(status)));
                        RaiseError("连接状态异常");
                        return false;
                    }
                }
                else
                {
                    LogError(string.Format("连接失败，错误码: {0}", result));
                    RaiseError(string.Format("连接失败，错误码: {0}", result));
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogError(string.Format("连接异常: {0}", ex.Message));
                RaiseError(string.Format("连接异常: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            if (!_isConnected)
            {
                return;
            }

            try
            {
                // 如果正在打标，先停止
                if (_isMarking)
                {
                    StopMark();
                }

                // 断开连接
                if (_ipIndex >= 0)
                {
                    HM_HashuScanDLL.HM_DisconnectTo(_ipIndex);
                }

                _isConnected = false;
                _ipIndex = -1;

                LogInfo("已断开连接");
                RaiseStatus("已断开");
                RaiseConnectionChanged(false);
            }
            catch (Exception ex)
            {
                LogError(string.Format("断开连接异常: {0}", ex.Message));
            }
        }

        /// <summary>
        /// 获取工作状态
        /// </summary>
        public HM_WorkStatus GetWorkStatus()
        {
            if (!_isConnected || _ipIndex < 0)
            {
                return HM_WorkStatus.Unknown;
            }

            try
            {
                var status = HM_HashuScanDLL.HM_GetWorkStatus(_ipIndex);
                return (HM_WorkStatus)status;
            }
            catch
            {
                return HM_WorkStatus.Unknown;
            }
        }

        #endregion

        #region 核心打标方法

        /// <summary>
        /// 添加切割线并执行打标
        /// 这是供 LaserCutHandler 调用的主要方法
        /// </summary>
        /// <param name="coordinates">坐标字符串，格式: "x1,y1,x2,y2;x3,y3,x4,y4;..."</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>是否成功</returns>
        public async Task<bool> AddLinesAndMarkAsync(string coordinates, CancellationToken ct = default(CancellationToken))
        {
            if (!_isConnected)
            {
                RaiseError("设备未连接");
                return false;
            }

            if (_isMarking)
            {
                RaiseError("正在打标中，请等待完成");
                return false;
            }

            try
            {
                LogInfo("开始处理打标任务");
                _markTimer.Restart();

                // 1. 解析坐标
                var polylines = ParseCoordinates(coordinates);
                if (polylines == null || polylines.Count == 0)
                {
                    LogError("坐标解析失败或无有效坐标");
                    RaiseError("坐标数据无效");
                    return false;
                }

                LogDebug(string.Format("解析完成: {0} 条线段", polylines.Count));

                // 2. 生成UDM文件
                if (!BuildUDMFile(polylines))
                {
                    LogError("生成UDM文件失败");
                    RaiseError("生成打标文件失败");
                    return false;
                }

                // 3. 下载到控制卡
                if (!await DownloadUDMAsync(ct))
                {
                    LogError("下载UDM文件失败");
                    RaiseError("下载打标文件失败");
                    return false;
                }

                // 4. 开始打标
                if (!await StartMarkInternalAsync(ct))
                {
                    LogError("开始打标失败");
                    RaiseError("开始打标失败");
                    return false;
                }

                _markTimer.Stop();
                LogInfo(string.Format("打标任务完成，总耗时: {0}ms", _markTimer.ElapsedMilliseconds));
                return true;
            }
            catch (OperationCanceledException)
            {
                LogInfo("打标任务被取消");
                StopMark();
                throw;
            }
            catch (Exception ex)
            {
                LogError(string.Format("打标任务异常: {0}", ex.Message));
                RaiseError(string.Format("打标异常: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 多Pass打标主方法（HM控制卡图层机制）
        /// 
        /// 将同一图形添加到N个图层（每个图层绑定不同的MarkParameter），
        /// 控制卡按图层顺序依次执行，一次下载+一次打标指令完成全部Pass。
        /// </summary>
        /// <param name="coordinates">坐标字符串，格式同 AddLinesAndMarkAsync</param>
        /// <param name="layerParams">图层参数数组，长度=Pass数(1~5)</param>
        /// <param name="intervalMs">Pass之间等待时间(ms)，0=不等待</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>是否成功</returns>
        public async Task<bool> AddLinesAndMarkMultiPassAsync(
            string coordinates,
            MarkParameter[] layerParams,
            int intervalMs = 0,
            CancellationToken ct = default(CancellationToken))
        {
            if (!_isConnected)
            {
                RaiseError("设备未连接");
                return false;
            }

            while (IsMarking)
            {
                RaiseStatus("正在打标中，请等待完成");
                await Task.Delay(10);
            }

            if (layerParams == null || layerParams.Length == 0)
            {
                RaiseError("图层参数为空");
                return false;
            }

            if (layerParams.Length > 5)
            {
                RaiseError(string.Format("图层数量超限: {0}，最大支持5个", layerParams.Length));
                return false;
            }

            try
            {
                Set3dCorrectionPara();
                LogInfo(string.Format("开始多Pass打标任务: {0}个Pass, 间隔{1}ms",
                    layerParams.Length, intervalMs));
                _markTimer.Restart();

                // 1. 解析坐标（复用现有方法）
                var polylines = ParseCoordinates(coordinates);
                if (polylines == null || polylines.Count == 0)
                {
                    LogError("坐标解析失败或无有效坐标");
                    RaiseError("坐标数据无效");
                    return false;
                }

                LogDebug(string.Format("解析完成: {0} 条线段", polylines.Count));

                // 打印每个Pass的关键参数
                for (int i = 0; i < layerParams.Length; i++)
                {
                    LogInfo(string.Format("  Pass{0}: Speed={1}mm/s, Freq={2}kHz, Power={3}%",
                        i + 1, layerParams[i].MarkSpeed,
                        layerParams[i].Frequency, layerParams[i].LaserPower));
                }

                // 2. 生成多图层UDM文件
                if (!BuildMultiPassUDMFile(polylines, layerParams, intervalMs))
                {
                    LogError("生成多Pass UDM文件失败");
                    RaiseError("生成打标文件失败");
                    return false;
                }

                // 3. 下载到控制卡
                if (!await DownloadUDMAsync(ct))
                {
                    LogError("下载UDM文件失败");
                    RaiseError("下载打标文件失败");
                    return false;
                }

                // 4. 开始打标（一次指令，控制卡自动按图层顺序执行所有Pass）
                if (!await StartMarkInternalAsync(ct))
                {
                    LogError("开始打标失败");
                    RaiseError("开始打标失败");
                    return false;
                }

                //_markTimer.Stop();
                LogInfo(string.Format("多Pass打标任务完成: {0}个Pass, 总耗时: {1}ms",
                    layerParams.Length, _markTimer.ElapsedMilliseconds));
                return true;
            }
            catch (OperationCanceledException)
            {
                LogInfo("多Pass打标任务被取消");
                StopMark();
                throw;
            }
            catch (Exception ex)
            {
                LogError(string.Format("多Pass打标任务异常: {0}", ex.Message));
                RaiseError(string.Format("打标异常: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 停止打标
        /// </summary>
        public void StopMark()
        {
            if (!_isConnected || _ipIndex < 0)
            {
                return;
            }

            try
            {
                HM_HashuScanDLL.HM_StopMark(_ipIndex);
                _isMarking = false;
                _markProgress = 0;

                // 取消等待中的任务
                _markTcs?.TrySetResult(false);

                LogInfo("打标已停止");
                RaiseStatus("打标已停止");
            }
            catch (Exception ex)
            {
                LogError(string.Format("停止打标异常: {0}", ex.Message));
            }
        }

        /// <summary>
        /// 暂停打标
        /// </summary>
        public bool PauseMark()
        {
            if (!_isConnected || !_isMarking || _ipIndex < 0)
            {
                return false;
            }

            try
            {
                var result = HM_HashuScanDLL.HM_PauseMark(_ipIndex);
                if (result == HM_Constants.HM_OK)
                {
                    LogInfo("打标已暂停");
                    RaiseStatus("打标已暂停");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LogError(string.Format("暂停打标异常: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 继续打标
        /// </summary>
        public bool ResumeMark()
        {
            if (!_isConnected || _ipIndex < 0)
            {
                return false;
            }

            try
            {
                var result = HM_HashuScanDLL.HM_ContinueMark(_ipIndex);
                if (result == HM_Constants.HM_OK)
                {
                    LogInfo("打标已继续");
                    RaiseStatus("打标继续中");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LogError(string.Format("继续打标异常: {0}", ex.Message));
                return false;
            }
        }

        #endregion

        #region 振镜控制

        /// <summary>
        /// 振镜跳转到指定位置
        /// </summary>
        public bool JumpTo(float x, float y, float z = 0)
        {
            if (!_isConnected || _ipIndex < 0)
            {
                return false;
            }

            try
            {
                var result = HM_HashuScanDLL.HM_ScannerJump(_ipIndex, x, y, z);
                return result == HM_Constants.HM_OK;
            }
            catch (Exception ex)
            {
                LogError(string.Format("振镜跳转异常: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 开关红光
        /// </summary>
        public bool SetRedLight(bool enable)
        {
            if (!_isConnected || _ipIndex < 0)
            {
                return false;
            }

            try
            {
                var result = HM_HashuScanDLL.HM_SetGuidLaser(_ipIndex, enable);
                if (result == HM_Constants.HM_OK)
                {
                    LogInfo(enable ? "红光已开启" : "红光已关闭");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                LogError(string.Format("红光控制异常: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 设置偏移
        /// </summary>
        public bool SetOffset(float offsetX, float offsetY, float offsetZ = 0)
        {
            if (!_isConnected || _ipIndex < 0)
            {
                return false;
            }

            try
            {
                var result = HM_HashuScanDLL.HM_SetOffset(_ipIndex, offsetX, offsetY, offsetZ);
                return result == HM_Constants.HM_OK;
            }
            catch (Exception ex)
            {
                LogError(string.Format("设置偏移异常: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 设置旋转
        /// </summary>
        public bool SetRotate(float angle, float centerX = 0, float centerY = 0)
        {
            if (!_isConnected || _ipIndex < 0)
            {
                return false;
            }

            try
            {
                var result = HM_HashuScanDLL.HM_SetRotates(_ipIndex, angle, centerX, centerY);
                return result == HM_Constants.HM_OK;
            }
            catch (Exception ex)
            {
                LogError(string.Format("设置旋转异常: {0}", ex.Message));
                return false;
            }
        }

        #endregion

        #region UDM文件生成

        /// <summary>
        /// 生成UDM文件
        /// </summary>
        private bool BuildUDMFile(List<structUdmPos[]> polylines)
        {
            try
            {
                LogDebug("开始生成UDM文件");

                // 1. 新建文件
                HM_UDM_DLL.UDM_NewFile();

                // 2. 开始主程序（不检查返回值，官方示例也不检查）
                HM_UDM_DLL.UDM_Main();

                // 3. 设置协议（不检查返回值）
                HM_UDM_DLL.UDM_SetProtocol(_config.Protocol, _config.Dimensional);

                // 4. 设置图层参数
                var markPara = new MarkParameter[] { _config.ToMarkParameter() };
                HM_UDM_DLL.UDM_SetLayersPara(markPara, 1);

                // 5. 添加图形
                int totalPoints = 0;
                foreach (var polyline in polylines)
                {
                    HM_UDM_DLL.UDM_AddPolyline2D(polyline, polyline.Length, 0);
                    totalPoints += polyline.Length;
                }

                // 6. 回零（可选）
                if (_config.JumpToZeroAfterMark)
                {
                    HM_UDM_DLL.UDM_Jump(0, 0, 0);
                }

                // 7. 结束主程序
                HM_UDM_DLL.UDM_EndMain();

                LogDebug(string.Format("UDM文件生成完成: {0} 条线段, {1} 个点",
                    polylines.Count, totalPoints));

                return true;
            }
            catch (Exception ex)
            {
                LogError(string.Format("生成UDM文件异常: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 生成多图层UDM文件（多Pass不同参数）
        /// 
        /// 核心原理：
        /// - SDK的图层机制：每个图层拥有独立的 MarkParameter
        /// - 将同一图形分别添加到不同图层（layerIndex = 0, 1, 2...）
        /// - 控制卡按添加顺序依次执行，每次使用对应图层的参数
        /// - Pass之间可通过 UDM_Wait 插入硬件级精确等待
        /// </summary>
        /// <param name="polylines">图形线段集合</param>
        /// <param name="layerParams">每个图层的完整参数</param>
        /// <param name="intervalMs">图层之间等待时间(ms)</param>
        /// <returns>是否成功</returns>
        private bool BuildMultiPassUDMFile(
            List<structUdmPos[]> polylines,
            MarkParameter[] layerParams,
            int intervalMs)
        {
            try
            {
                int passCount = layerParams.Length;
                LogDebug(string.Format("开始生成多Pass UDM文件: {0}个图层", passCount));

                // 1. 新建文件
                HM_UDM_DLL.UDM_NewFile();

                // 2. 开始主程序
                HM_UDM_DLL.UDM_Main();

                // 3. 设置协议
                HM_UDM_DLL.UDM_SetProtocol(_config.Protocol, _config.Dimensional);

                // 4. ★ 设置所有图层参数（一次性传入N个图层的参数）
                HM_UDM_DLL.UDM_SetLayersPara(layerParams, passCount);

                // 5. ★ 同一图形添加 passCount 次，每次指定不同的 layerIndex
                int totalPoints = 0;
                for (int pass = 0; pass < passCount; pass++)
                {
                    // Pass之间插入等待（第一次之前不等待）
                    if (pass > 0 && intervalMs > 0)
                    {
                        HM_UDM_DLL.UDM_Wait(intervalMs);
                    }
                    if (Config.Protocol == 1)
                    {
                        // 将所有线段添加到当前图层
                        foreach (var polyline in polylines)
                        {
                            ///每次切割，变化Z轴
                            for (int pIndex = 0; pIndex < polyline.Length; pIndex++)
                            {
                                var p = polyline[pIndex];
                                float z = HM_UDM_DLL.UDM_GetZvalue(p.x, p.y, 0);
                                p.z = z;
                                polyline[pIndex] = p;
                            }


                            HM_UDM_DLL.UDM_AddPolyline3D(polyline, polyline.Length, pass);
                            totalPoints += polyline.Length;
                        }
                    }
                    else
                    {
                        // 将所有线段添加到当前图层
                        foreach (var polyline in polylines)
                        {
                            HM_UDM_DLL.UDM_AddPolyline2D(polyline, polyline.Length, pass);
                            totalPoints += polyline.Length;
                        }
                    }
                    
                }

                // 6. 回零（可选）
                if (_config.JumpToZeroAfterMark)
                {
                    HM_UDM_DLL.UDM_Jump(0, 0, 0);
                }

                // 7. 结束主程序
                HM_UDM_DLL.UDM_EndMain();

                LogDebug(string.Format(
                    "多Pass UDM文件生成完成: {0}个Pass × {1}条线段, 共{2}个点",
                    passCount, polylines.Count, totalPoints));

                return true;
            }
            catch (Exception ex)
            {
                LogError(string.Format("生成多Pass UDM文件异常: {0}", ex.Message));
                return false;
            }
        }


        /// <summary>
        /// 下载UDM到控制卡
        /// </summary>
        private async Task<bool> DownloadUDMAsync(CancellationToken ct)
        {
            try
            {
                LogDebug("开始下载UDM文件到控制卡");

                _downloadTcs = new TaskCompletionSource<bool>();
                _isDownloading = true;

                // 获取UDM缓冲区
                IntPtr pBuffer = IntPtr.Zero;
                int bufferSize = 0;
                var result = HM_UDM_DLL.UDM_GetUDMBuffer(ref pBuffer, ref bufferSize);

                if (result != HM_Constants.HM_OK || pBuffer == IntPtr.Zero || bufferSize <= 0)
                {
                    LogError("获取UDM缓冲区失败");
                    _isDownloading = false;
                    return false;
                }

                LogDebug(string.Format("UDM缓冲区大小: {0} 字节", bufferSize));

                // 下载到控制卡
                result = HM_HashuScanDLL.HM_DownloadMarkFileBuff(
                    _ipIndex,
                    pBuffer,
                    bufferSize,
                    _messageWindow.Handle);

                if (result != HM_Constants.HM_OK)
                {
                    LogError(string.Format("下载UDM失败，错误码: {0}", result));
                    _isDownloading = false;
                    return false;
                }
                do
                {
                    await Task.Delay(10);
                } while (IsDownloading);
                return true;
                //// 等待下载完成消息
                //using (ct.Register(() => _downloadTcs.TrySetCanceled()))
                //{
                //    var timeoutTask = Task.Delay(_config.DownloadTimeoutMs, ct);
                //    var completedTask = await Task.WhenAny(_downloadTcs.Task, timeoutTask);

                //    _isDownloading = false;

                //    if (completedTask == timeoutTask)
                //    {
                //        LogError("下载超时");
                //        return false;
                //    }

                //    return await _downloadTcs.Task;
                //}
            }
            catch (OperationCanceledException)
            {
                _isDownloading = false;
                throw;
            }
            catch (Exception ex)
            {
                LogError(string.Format("下载异常: {0}", ex.Message));
                _isDownloading = false;
                return false;
            }
        }

        /// <summary>
        /// 开始打标（内部方法）
        /// </summary>
        private async Task<bool> StartMarkInternalAsync(CancellationToken ct)
        {
            try
            {
                _markTcs = new TaskCompletionSource<bool>();
                _isMarking = true;
                _markProgress = 0;

                LogDebug("发送打标开始指令");
                RaiseStatus("打标中...");

                var result = HM_HashuScanDLL.HM_StartMark(_ipIndex);
                if (result != HM_Constants.HM_OK)
                {
                    LogError(string.Format("开始打标失败，错误码: {0}", result));
                    _isMarking = false;
                    return false;
                }

                LogInfo("打标开始");
                do
                {
                    await Task.Delay(10);
                } while (IsMarking);
                LogInfo("打标完成");
                return true;
                // 等待打标完成消息
                //using (ct.Register(() =>
                //{
                //    StopMark();
                //    _markTcs.TrySetCanceled();
                //}))
                //{
                //    var timeoutTask = Task.Delay(_config.MarkTimeoutMs, ct);
                //    var completedTask = await Task.WhenAny(_markTcs.Task, timeoutTask);

                //    if (completedTask == timeoutTask)
                //    {
                //        LogError("打标超时");
                //        StopMark();
                //        return false;
                //    }

                //    return await _markTcs.Task;
                //}
            }
            catch (OperationCanceledException)
            {
                _isMarking = false;
                throw;
            }
            catch (Exception ex)
            {
                LogError(string.Format("打标异常: {0}", ex.Message));
                _isMarking = false;
                return false;
            }
        }

        #endregion

        #region 坐标解析

        /// <summary>
        /// 解析坐标字符串（优化版：去除重复点）
        /// </summary>
        private List<structUdmPos[]> ParseCoordinates(string coordinates)
        {
            var result = new List<structUdmPos[]>();

            if (string.IsNullOrWhiteSpace(coordinates))
            {
                return result;
            }

            // 去除前缀
            coordinates = coordinates.Trim();
            if (coordinates.StartsWith("AddLines["))
            {
                coordinates = coordinates.Substring(9).TrimEnd(']');
            }
            else if (coordinates.StartsWith("AddAreas["))
            {
                coordinates = coordinates.Substring(9).TrimEnd(']');
            }
            else
            {
                coordinates = coordinates.TrimEnd(']');
            }

            // 分割多条线段（; 分隔）
            var segments = coordinates.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                var points = new List<structUdmPos>();

                // 解析点（: 分隔的点组）
                var pointGroups = segment.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var group in pointGroups)
                {
                    var values = group.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                    // 每两个值为一个坐标点
                    for (int i = 0; i + 1 < values.Length; i += 2)
                    {
                        float x, y;
                        if (float.TryParse(values[i].Trim(), out x) &&
                            float.TryParse(values[i + 1].Trim(), out y))
                        {
                            // 坐标范围检查
                            if (_config.EnableCoordinateCheck && !_config.IsInWorkArea(x, y))
                            {
                                LogError(string.Format(
                                    "坐标 ({0:F2}, {1:F2}) 超出工作区域", x, y));
                                continue;
                            }

                            var newPoint = structUdmPos.Create2D(x, y);

                            // ★ 去除重复点（与上一个点相同则跳过）
                            if (points.Count > 0)
                            {
                                var lastPoint = points[points.Count - 1];
                                if (Math.Abs(lastPoint.x - x) < 0.001f &&
                                    Math.Abs(lastPoint.y - y) < 0.001f)
                                {
                                    continue; // 跳过重复点
                                }
                            }

                            points.Add(newPoint);
                        }
                    }
                }

                // 至少需要2个点才能构成线段
                if (points.Count >= 2)
                {
                    result.Add(points.ToArray());
                }
            }

            return result;
        }

        #endregion

        #region 消息窗口

        /// <summary>
        /// 隐藏的消息窗口，用于接收DLL回调
        /// </summary>
        private class MessageWindow : Form
        {
            private readonly HM_LaserService _service;

            public MessageWindow(HM_LaserService service)
            {
                _service = service;

                // 隐藏窗口
                this.ShowInTaskbar = false;
                this.FormBorderStyle = FormBorderStyle.None;
                this.Size = new System.Drawing.Size(1, 1);
                this.Location = new System.Drawing.Point(-100, -100);
                this.CreateHandle();
            }

            protected override void WndProc(ref Message m)
            {
                switch (m.Msg)
                {
                    case HM_Constants.HM_MSG_StreamEnd:
                        // 文件下载完成
                        _service.OnDownloadComplete();
                        break;

                    case HM_Constants.HM_MSG_MarkOver:
                        // 打标完成
                        _service.OnMarkComplete(true);
                        break;

                    case HM_Constants.HM_MSG_QueryExecProcess:
                        // 打标进度更新
                        // 使用 ToInt64() 避免64位系统溢出，进度值范围是0-100，转int安全
                        var progress = (int)(m.WParam.ToInt64() & 0xFFFFFFFF);
                        _service.OnProgressUpdate(progress);
                        break;

                    case HM_Constants.HM_MSG_DeviceStatusUpdate:
                        // 设备状态更新
                        // 使用 ToInt64() 获取完整值，再截取低32位
                        var ipIndex = (int)(m.LParam.ToInt64() & 0xFFFFFFFF);
                        var ipAddr = (uint)(m.WParam.ToInt64() & 0xFFFFFFFF);
                        _service.OnDeviceStatusUpdate(ipIndex, ipAddr);
                        break;
                }

                base.WndProc(ref m);
            }
        }

        /// <summary>
        /// 下载完成回调
        /// </summary>
        private void OnDownloadComplete()
        {
            LogDebug("收到下载完成消息");
            _isDownloading = false;
            _downloadTcs?.TrySetResult(true);
            DownloadCompleted?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 打标完成回调
        /// </summary>
        private void OnMarkComplete(bool success)
        {
            _markTimer.Stop();
            _isMarking = false;
            _markProgress = success ? 100 : 0;

            LogInfo(string.Format("打标完成，耗时: {0}ms", _markTimer.ElapsedMilliseconds));
            RaiseStatus("打标完成");

            _markTcs?.TrySetResult(success);

            MarkFinished?.Invoke(this, new HM_MarkFinishedEventArgs
            {
                Success = success,
                ElapsedMs = _markTimer.ElapsedMilliseconds
            });
        }

        /// <summary>
        /// 进度更新回调
        /// </summary>
        private void OnProgressUpdate(int progress)
        {
            _markProgress = progress;
            ProgressChanged?.Invoke(this, progress);
        }

        /// <summary>
        /// 设备状态更新回调
        /// </summary>
        private void OnDeviceStatusUpdate(int ipIndex, uint ipAddr)
        {
            try
            {
                var status = HM_HashuScanDLL.HM_GetDeviceStatus(ipIndex);
                LogDebug(string.Format("设备状态更新: Index={0}, Status={1}",
                    ipIndex, HM_HashuScanDLL.GetConnectStatusDescription(status)));
            }
            catch { }
        }

        #endregion

        #region 日志与事件

        private void LogInfo(string message)
        {
            _log?.Information("[HM_Laser] {Message}", message);
            Debug.WriteLine(string.Format("[HM_Laser] {0}", message));
        }

        private void LogDebug(string message)
        {
            _log?.Debug("[HM_Laser] {Message}", message);
            Debug.WriteLine(string.Format("[HM_Laser Debug] {0}", message));
        }

        private void LogError(string message)
        {
            _log?.Error("[HM_Laser] {Message}", message);
            Debug.WriteLine(string.Format("[HM_Laser Error] {0}", message));
        }

        private void RaiseStatus(string message)
        {
            StatusChanged?.Invoke(this, message);
        }

        private void RaiseError(string message)
        {
            ErrorOccurred?.Invoke(this, message);
        }

        private void RaiseConnectionChanged(bool connected)
        {
            ConnectionChanged?.Invoke(this, connected);
        }

        #endregion


        #region 区域填充

        /// <summary>
        /// 区域填充打标
        /// 输入格式: AddAreas[x1,y1,x2,y2:x3,y3,x4,y4:...]
        /// </summary>
        public async Task<bool> FillAreaAndMarkAsync(string areaCoordinates, CancellationToken ct = default)
        {
            if (!_isConnected)
            {
                RaiseError("设备未连接");
                return false;
            }

            if (_isMarking)
            {
                RaiseError("正在打标中");
                return false;
            }

            try
            {
                LogInfo("开始区域填充打标");
                _markTimer.Restart();

                // 1. 解析闭环多边形顶点
                var polygon = ParsePolygonFromArea(areaCoordinates);
                if (polygon == null || polygon.Count < 3)
                {
                    LogError("解析多边形失败或顶点不足");
                    RaiseError("区域数据无效");
                    return false;
                }

                LogDebug(string.Format("多边形顶点数: {0}", polygon.Count));

                // 2. 生成蛇形填充路径
                var fillPath = GenerateSnakeFillPath(polygon);
                if (fillPath == null || fillPath.Count < 2)
                {
                    LogError("生成填充路径失败");
                    RaiseError("无法生成填充路径");
                    return false;
                }

                LogDebug(string.Format("填充路径点数: {0}", fillPath.Count));

                // 3. 生成UDM
                var polylines = new List<structUdmPos[]> { fillPath.ToArray() };
                if (!BuildUDMFile(polylines))
                {
                    return false;
                }

                // 4. 下载并执行
                if (!await DownloadUDMAsync(ct))
                {
                    return false;
                }

                if (!await StartMarkInternalAsync(ct))
                {
                    return false;
                }

                _markTimer.Stop();
                LogInfo(string.Format("区域填充完成，耗时: {0}ms", _markTimer.ElapsedMilliseconds));
                return true;
            }
            catch (OperationCanceledException)
            {
                StopMark();
                throw;
            }
            catch (Exception ex)
            {
                LogError(string.Format("区域填充异常: {0}", ex.Message));
                RaiseError(string.Format("填充异常: {0}", ex.Message));
                return false;
            }
        }


        public void Set3dCorrectionPara()
        {
            float baseFocal = 296f;

            if (File.Exists("./Config/ParaK3D.txt"))
            {

                string[] lines = File.ReadAllLines("./Config/ParaK3D.txt").Where(str => !string.IsNullOrEmpty(str.Trim())).ToArray();

                double[] paramters = new double[lines.Length - 1];

                for (int i = 1; i < lines.Length; i++)
                {
                    paramters[i - 1] = double.Parse(lines[i].Split(':')[1].Replace(";", ""));
                }
                baseFocal = float.Parse(lines[0].Split(':')[1].Replace(";", ""));

                HM_UDM_DLL.UDM_Set3dCorrectionPara(baseFocal, paramters, paramters.Length);
            }
        }

        /// <summary>
        /// 从AddAreas格式解析闭环多边形顶点
        /// 格式: AddAreas[x1,y1,x2,y2:x3,y3,x4,y4:...]
        /// 每段线的终点是下一段的起点，提取所有唯一顶点
        /// </summary>
        private List<PointF> ParsePolygonFromArea(string areaCoordinates)
        {
            var vertices = new List<PointF>();

            if (string.IsNullOrWhiteSpace(areaCoordinates))
                return vertices;

            // 去除前缀
            var coords = areaCoordinates.Trim();
            if (coords.StartsWith("AddAreas["))
            {
                coords = coords.Substring(9).TrimEnd(']');
            }
            else
            {
                coords = coords.TrimEnd(']');
            }

            // 分割线段组（: 或 ; 分隔）
            var segments = coords.Split(new char[] { ':', ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                var values = segment.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                // 每4个值为一条线段: x1,y1,x2,y2
                for (int i = 0; i + 3 < values.Length; i += 4)
                {
                    float x1, y1, x2, y2;
                    if (float.TryParse(values[i].Trim(), out x1) &&
                        float.TryParse(values[i + 1].Trim(), out y1) &&
                        float.TryParse(values[i + 2].Trim(), out x2) &&
                        float.TryParse(values[i + 3].Trim(), out y2))
                    {
                        // 添加起点（如果是新顶点）
                        var p1 = new PointF(x1, y1);
                        if (vertices.Count == 0 || !IsNearPoint(vertices[vertices.Count - 1], p1))
                        {
                            vertices.Add(p1);
                        }

                        // 添加终点
                        var p2 = new PointF(x2, y2);
                        if (!IsNearPoint(vertices[vertices.Count - 1], p2))
                        {
                            vertices.Add(p2);
                        }
                    }
                }
            }

            // 如果首尾重合，去掉最后一个点
            if (vertices.Count > 1 && IsNearPoint(vertices[0], vertices[vertices.Count - 1]))
            {
                vertices.RemoveAt(vertices.Count - 1);
            }

            return vertices;
        }

        /// <summary>
        /// 判断两点是否接近（距离小于0.001mm）
        /// </summary>
        private bool IsNearPoint(PointF a, PointF b)
        {
            return Math.Abs(a.X - b.X) < 0.001f && Math.Abs(a.Y - b.Y) < 0.001f;
        }

        /// <summary>
        /// 生成蛇形填充路径
        /// 水平扫描线 + 射线法求交点
        /// </summary>
        private List<structUdmPos> GenerateSnakeFillPath(List<PointF> polygon)
        {
            var path = new List<structUdmPos>();

            if (polygon.Count < 3)
                return path;

            float spacing = _config.FillLineSpacing;

            // 1. 计算边界框
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (var p in polygon)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            // 2. 从下到上扫描
            bool leftToRight = true;
            float y = minY + spacing / 2;  // 起始偏移半个间距

            while (y < maxY)
            {
                // 3. 求扫描线与多边形的交点
                var intersections = GetScanLineIntersections(polygon, y);

                if (intersections.Count >= 2)
                {
                    // 排序交点
                    intersections.Sort();

                    // 4. 两两配对生成线段（奇偶规则）
                    for (int i = 0; i + 1 < intersections.Count; i += 2)
                    {
                        float x1 = intersections[i];
                        float x2 = intersections[i + 1];

                        if (leftToRight)
                        {
                            path.Add(structUdmPos.Create2D(x1, y));
                            path.Add(structUdmPos.Create2D(x2, y));
                        }
                        else
                        {
                            path.Add(structUdmPos.Create2D(x2, y));
                            path.Add(structUdmPos.Create2D(x1, y));
                        }
                    }

                    leftToRight = !leftToRight;
                }

                y += spacing;
            }

            return path;
        }

        /// <summary>
        /// 获取水平扫描线与多边形的交点X坐标
        /// </summary>
        private List<float> GetScanLineIntersections(List<PointF> polygon, float y)
        {
            var intersections = new List<float>();
            int n = polygon.Count;

            for (int i = 0; i < n; i++)
            {
                var p1 = polygon[i];
                var p2 = polygon[(i + 1) % n];

                // 边的Y范围检查
                float minEdgeY = Math.Min(p1.Y, p2.Y);
                float maxEdgeY = Math.Max(p1.Y, p2.Y);

                if (y < minEdgeY || y > maxEdgeY)
                    continue;

                // 水平边跳过
                if (Math.Abs(p2.Y - p1.Y) < 0.0001f)
                    continue;

                // 计算交点X
                float t = (y - p1.Y) / (p2.Y - p1.Y);
                float x = p1.X + t * (p2.X - p1.X);

                intersections.Add(x);
            }

            return intersections;
        }

        /// <summary>
        /// 简单的2D点结构
        /// </summary>
        private struct PointF
        {
            public float X;
            public float Y;

            public PointF(float x, float y)
            {
                X = x;
                Y = y;
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            // 停止打标
            if (_isMarking)
            {
                try
                {
                    StopMark();
                }
                catch { }
            }

            // 断开连接
            Disconnect();

            // 释放消息窗口
            if (_messageWindow != null)
            {
                try
                {
                    _messageWindow.Dispose();
                }
                catch { }
                _messageWindow = null;
            }

            _disposed = true;
            LogInfo("HM激光器服务已释放");
        }

        #endregion
    }

    #region 事件参数类

    /// <summary>
    /// HM打标完成事件参数
    /// </summary>
    public class HM_MarkFinishedEventArgs : EventArgs
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
        /// 耗时（毫秒）
        /// </summary>
        public long ElapsedMs { get; set; }
    }

    #endregion
}
