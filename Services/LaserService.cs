using SeedCut.Framework.Services.Interfaces;
using SeedCut.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Services
{
    /// <summary>
    /// 激光器服务 - 兼容 C# 7.3
    /// 架构：上位机作为TCP Server，激光器作为Client主动连接
    /// </summary>
    public sealed class LaserService : ILaserService, IDisposable
    {
        private readonly ILogService _log;
        private readonly LaserConfig _config;

        // TCP 通信
        private TcpListener _listener;
        private TcpClient _client;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;

        // 指令队列
        private readonly ConcurrentQueue<string> _commandQueue = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _commandHistory = new ConcurrentQueue<string>();

        // 状态 - 使用volatile确保多线程可见性
        private volatile bool _isServerRunning;
        private volatile bool _isConnected;
        private volatile bool _isMarking;
        private int _markProgress;
        private string _lastCommand = string.Empty;
        private readonly Stopwatch _markTimer = new Stopwatch();
        private readonly object _lock = new object();

        #region 公开属性

        public LaserConfig Config { get { return _config; } }
        public bool IsServerRunning { get { return _isServerRunning; } }
        public bool IsConnected { get { return _isConnected; } }
        public bool IsMarking { get { return _isMarking; } }
        public int MarkProgress { get { return _markProgress; } }
        public int QueueLength { get { return _commandQueue.Count; } }

        #endregion

        #region 事件

        public event EventHandler<bool> ConnectionChanged;
        public event EventHandler<LaserResponseEventArgs> ResponseReceived;
        public event EventHandler<LaserMarkFinishedEventArgs> MarkFinished;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<string> ErrorOccurred;

        #endregion

        #region 构造函数

        public LaserService(ILogService logService)
        {
            _log = logService ?? throw new ArgumentNullException(nameof(logService));
            _config = LaserConfig.Load();

            LogInfo("激光器服务初始化完成");
            LogInfo(string.Format("配置: {0}", _config));
        }

        #endregion

        #region 服务器启动/停止

        /// <summary>
        /// 启动TCP服务器
        /// </summary>
        public async Task<bool> StartServerAsync()
        {
            if (_isServerRunning)
            {
                LogInfo("服务器已在运行");
                return true;
            }

            try
            {
                // 检查端口是否被占用
                if (IsPortInUse(_config.Port))
                {
                    var error = string.Format("端口 {0} 已被占用，请检查是否有其他程序使用此端口", _config.Port);
                    LogError(error);
                    RaiseError(error);
                    return false;
                }

                _cts = new CancellationTokenSource();

                // 创建并启动监听器
                _listener = new TcpListener(IPAddress.Any, _config.Port);
                _listener.Start();
                _isServerRunning = true;

                LogInfo(string.Format("TCP服务器启动成功，监听端口: {0}", _config.Port));
                LogInfo(string.Format("本机IP地址: {0}", GetLocalIPAddresses()));
                RaiseStatus(string.Format("服务器已启动，监听端口 {0}，等待激光器连接...", _config.Port));

                // 启动接受连接的任务
                var token = _cts.Token;
                Task.Run(() => AcceptConnectionsAsync(token));

                return true;
            }
            catch (SocketException ex)
            {
                var error = string.Format("启动服务器失败: {0} (错误码: {1})", ex.Message, ex.ErrorCode);
                LogError(error);
                RaiseError(error);
                return false;
            }
            catch (Exception ex)
            {
                LogError(string.Format("启动服务器异常: {0}", ex));
                RaiseError(string.Format("启动失败: {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        /// 停止服务器并断开连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            LogInfo("正在停止服务器...");

            _isServerRunning = false;
            _isConnected = false;
            _isMarking = false;

            // 取消所有任务
            try
            {
                _cts?.Cancel();
            }
            catch { }

            // 关闭流和客户端
            CloseConnection();

            // 停止监听器
            try
            {
                _listener?.Stop();
            }
            catch { }

            _listener = null;

            try
            {
                _cts?.Dispose();
            }
            catch { }
            _cts = null;

            LogInfo("服务器已停止");
            RaiseStatus("服务器已停止");
            RaiseConnectionChanged(false);

            await Task.CompletedTask;
        }

        #endregion

        #region 连接管理

        /// <summary>
        /// 接受客户端连接
        /// </summary>
        private async Task AcceptConnectionsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isServerRunning)
            {
                try
                {
                    LogInfo("等待激光器连接...");

                    // 等待客户端连接
                    _client = await _listener.AcceptTcpClientAsync();

                    if (token.IsCancellationRequested)
                        break;

                    // 配置客户端
                    _client.NoDelay = true;
                    _client.ReceiveTimeout = _config.TimeoutMs;
                    _client.SendTimeout = _config.TimeoutMs;

                    _stream = _client.GetStream();
                    _isConnected = true;

                    var remoteEP = _client.Client.RemoteEndPoint as IPEndPoint;
                    var remoteInfo = remoteEP != null
                        ? string.Format("{0}:{1}", remoteEP.Address, remoteEP.Port)
                        : "未知";

                    LogInfo(string.Format("激光器已连接: {0}", remoteInfo));
                    RaiseStatus(string.Format("激光器已连接 ({0})", remoteInfo));
                    RaiseConnectionChanged(true);

                    // 启动数据接收任务
                    await ReceiveDataAsync(token);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    // 监听器已停止
                    break;
                }
                catch (Exception ex)
                {
                    if (_isServerRunning && !token.IsCancellationRequested)
                    {
                        LogError(string.Format("接受连接异常: {0}", ex.Message));
                        await Task.Delay(1000);
                    }
                }
            }
        }

        /// <summary>
        /// 接收数据
        /// </summary>
        private async Task ReceiveDataAsync(CancellationToken token)
        {
            var buffer = new byte[4096];
            var messageBuilder = new StringBuilder();

            while (!token.IsCancellationRequested && _isConnected && _stream != null)
            {
                try
                {
                    // 兼容旧版本的ReadAsync
                    var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, token);

                    if (bytesRead == 0)
                    {
                        LogInfo("激光器断开连接（收到0字节）");
                        break;
                    }

                    var data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    messageBuilder.Append(data);

                    // 处理消息
                    var messages = messageBuilder.ToString();
                    var lines = messages.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            LogDebug(string.Format("收到: {0}", trimmed));
                            ProcessResponse(trimmed);
                        }
                    }

                    messageBuilder.Clear();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException ex)
                {
                    LogError(string.Format("网络读取错误: {0}", ex.Message));
                    break;
                }
                catch (Exception ex)
                {
                    LogError(string.Format("接收数据异常: {0}", ex.Message));
                    break;
                }
            }

            // 连接断开处理
            HandleDisconnection();
        }

        /// <summary>
        /// 处理连接断开
        /// </summary>
        private void HandleDisconnection()
        {
            _isConnected = false;
            _isMarking = false;
            _markProgress = 0;

            CloseConnection();

            LogInfo("激光器连接已断开");
            RaiseStatus("激光器连接已断开");
            RaiseConnectionChanged(false);
        }

        /// <summary>
        /// 关闭当前连接
        /// </summary>
        private void CloseConnection()
        {
            try
            {
                _stream?.Close();
                _stream?.Dispose();
            }
            catch { }
            _stream = null;

            try
            {
                _client?.Close();
                _client?.Dispose();
            }
            catch { }
            _client = null;
        }

        #endregion

        #region 响应处理

        /// <summary>
        /// 处理激光器响应
        /// </summary>
        private void ProcessResponse(string response)
        {
            var type = ParseResponseType(response);

            // 触发事件
            RaiseResponseReceived(response, type);

            switch (type)
            {
                case LaserResponseType.OK:
                    HandleOkResponse();
                    break;

                case LaserResponseType.FINISH:
                    HandleFinishResponse();
                    break;

                case LaserResponseType.FAILED:
                    HandleFailedResponse();
                    break;

                case LaserResponseType.Progress:
                    int progress;
                    if (int.TryParse(response, out progress))
                    {
                        _markProgress = progress;
                    }
                    break;
            }
        }

        private void HandleOkResponse()
        {
            if (_config.AutoSendMark &&
                _lastCommand != "Mark;" &&
                _lastCommand != "StopMark;")
            {
                LogInfo("收到OK，自动发送Mark指令");
                Task.Run(() => MarkAsync());
            }
        }

        private void HandleFinishResponse()
        {
            _markTimer.Stop();
            _isMarking = false;
            _markProgress = 100;

            LogInfo(string.Format("打标完成，耗时: {0}ms", _markTimer.ElapsedMilliseconds));
            RaiseStatus("打标完成");

            var handler = MarkFinished;
            if (handler != null)
            {
                handler(this, new LaserMarkFinishedEventArgs
                {
                    Success = true,
                    Message = "打标完成",
                    FinishTime = DateTime.Now,
                    ElapsedMs = _markTimer.ElapsedMilliseconds
                });
            }
        }

        private void HandleFailedResponse()
        {
            _markTimer.Stop();
            _isMarking = false;

            LogError(string.Format("指令执行失败: {0}", _lastCommand));
            RaiseError(string.Format("指令执行失败: {0}", _lastCommand));

            var handler = MarkFinished;
            if (handler != null)
            {
                handler(this, new LaserMarkFinishedEventArgs
                {
                    Success = false,
                    Message = "指令执行失败",
                    FinishTime = DateTime.Now,
                    ElapsedMs = _markTimer.ElapsedMilliseconds
                });
            }
        }

        private LaserResponseType ParseResponseType(string response)
        {
            if (response == "OK")
                return LaserResponseType.OK;
            if (response == "FINISH")
                return LaserResponseType.FINISH;
            if (response == "FAILED")
                return LaserResponseType.FAILED;

            int dummy;
            if (int.TryParse(response, out dummy))
                return LaserResponseType.Progress;

            return LaserResponseType.Unknown;
        }

        #endregion

        #region 发送指令

        /// <summary>
        /// 发送指令
        /// </summary>
        public async Task<bool> SendCommandAsync(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                RaiseError("指令不能为空");
                return false;
            }

            if (!_isConnected || _stream == null || !_stream.CanWrite)
            {
                RaiseError("激光器未连接");
                return false;
            }

            try
            {
                var data = Encoding.UTF8.GetBytes(command);
                // 兼容旧版本的WriteAsync
                await _stream.WriteAsync(data, 0, data.Length);
                await _stream.FlushAsync();

                _lastCommand = command;

                // 记录历史
                _commandHistory.Enqueue(command);
                while (_commandHistory.Count > 100)
                {
                    string removed;
                    _commandHistory.TryDequeue(out removed);
                }

                LogDebug(string.Format("发送: {0}", command));
                RaiseStatus(string.Format("→ {0}", command));

                return true;
            }
            catch (Exception ex)
            {
                LogError(string.Format("发送指令失败: {0}", ex.Message));
                RaiseError(string.Format("发送失败: {0}", ex.Message));
                return false;
            }
        }

        #endregion

        #region 打标控制

        public async Task<bool> MarkAsync()
        {
            if (!_isConnected)
            {
                RaiseError("激光器未连接");
                return false;
            }

            if (_isMarking)
            {
                LogInfo("打标任务已在进行中");
                return false;
            }

            _isMarking = true;
            _markProgress = 0;
            _markTimer.Restart();

            var success = await SendCommandAsync("Mark;");

            if (!success)
            {
                _isMarking = false;
                _markTimer.Stop();
            }
            else
            {
                RaiseStatus("开始打标...");
            }

            return success;
        }

        public async Task<bool> StopMarkAsync()
        {
            if (!_isConnected)
            {
                RaiseError("激光器未连接");
                return false;
            }

            var success = await SendCommandAsync("StopMark;");

            if (success)
            {
                _isMarking = false;
                _markTimer.Stop();
                RaiseStatus("已停止打标");
            }

            return success;
        }

        public async Task<int> GetMarkProgressAsync()
        {
            if (_isConnected)
            {
                await SendCommandAsync("ReqMarkProgress;");
            }
            return _markProgress;
        }

        #endregion

        #region 图形操作

        public async Task<bool> AddLinesAsync(string coordinates)
        {
            string error;
            if (!ValidateCoordinates(coordinates, out error))
            {
                RaiseError(error);
                return false;
            }

            return await SendCommandAsync(string.Format("AddLines[{0}]", coordinates));
        }

        public async Task<bool> AddAreasAsync(string coordinates)
        {
            string error;
            if (!ValidateCoordinates(coordinates, out error))
            {
                RaiseError(error);
                return false;
            }

            return await SendCommandAsync(string.Format("AddAreas[{0}]", coordinates));
        }

        /// <summary>
        /// 验证坐标
        /// </summary>
        private bool ValidateCoordinates(string coordinates, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(coordinates))
            {
                error = "坐标不能为空";
                return false;
            }

            var segments = coordinates.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
            {
                var values = segment.Split(',');

                if (values.Length != 4)
                {
                    error = string.Format("每个线段需要4个坐标值，实际得到 {0} 个", values.Length);
                    return false;
                }

                for (int i = 0; i < values.Length; i += 2)
                {
                    double x, y;
                    if (!double.TryParse(values[i], out x) ||
                        !double.TryParse(values[i + 1], out y))
                    {
                        error = "坐标值必须是有效数字";
                        return false;
                    }

                    // 范围检查
                    if (_config.EnableCoordinateCheck)
                    {
                        if (x < _config.WorkAreaMinX || x > _config.WorkAreaMaxX ||
                            y < _config.WorkAreaMinY || y > _config.WorkAreaMaxY)
                        {
                            error = string.Format("坐标 ({0},{1}) 超出工作区域 [{2}~{3}, {4}~{5}]",
                                x, y,
                                _config.WorkAreaMinX, _config.WorkAreaMaxX,
                                _config.WorkAreaMinY, _config.WorkAreaMaxY);
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        #endregion

        #region 文档和参数设置

        public Task<bool> LoadHsdFileAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                RaiseError(string.Format("文件不存在: {0}", filePath));
                return Task.FromResult(false);
            }
            return SendCommandAsync(string.Format("LoadHsdFile;{0};", filePath));
        }

        public Task<bool> SwitchDocumentByIdAsync(int docId)
        {
            return SendCommandAsync(string.Format("SwitchFromMultiFiles;{0};", docId));
        }

        public Task<bool> SwitchDocumentByNameAsync(string docName)
        {
            return SendCommandAsync(string.Format("SwitchDocFile;{0};", docName));
        }

        public Task<bool> ChangeTextAsync(int barcodeId, string content)
        {
            return SendCommandAsync(string.Format("ChangeText;{0};{1};", barcodeId, content));
        }

        public Task<bool> SetRotateAsync(int layerId, float angle, float centerX, float centerY)
        {
            return SendCommandAsync(string.Format("SetRotate;{0};{1};{2};{3};", layerId, angle, centerX, centerY));
        }

        public Task<bool> SetOffsetAsync(int layerId, float offsetX, float offsetY)
        {
            return SendCommandAsync(string.Format("SetOffset;{0};{1};{2};", layerId, offsetX, offsetY));
        }

        public Task<bool> MarkLayerAsync(int layerId)
        {
            return SendCommandAsync(string.Format("MarkLayer;{0};", layerId));
        }

        public Task<bool> SetLaserPowerAsync(int layerId, int power)
        {
            if (power < 0 || power > 100)
            {
                RaiseError("功率必须在 0-100 之间");
                return Task.FromResult(false);
            }
            return SendCommandAsync(string.Format("SetLaserPara_Power;{0};{1};", layerId, power));
        }

        #endregion

        #region 队列管理

        public void EnqueueCommand(string command)
        {
            if (_commandQueue.Count >= _config.MaxQueueLength)
            {
                string removed;
                _commandQueue.TryDequeue(out removed);
                LogInfo("队列已满，旧指令被丢弃");
            }
            _commandQueue.Enqueue(command);
        }

        public void ClearQueue()
        {
            while (!_commandQueue.IsEmpty)
            {
                string removed;
                _commandQueue.TryDequeue(out removed);
            }
            LogInfo("队列已清空");
            RaiseStatus("队列已清空");
        }

        public List<string> GetQueuedCommands()
        {
            return _commandQueue.ToList();
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 检查端口是否被占用
        /// </summary>
        private bool IsPortInUse(int port)
        {
            try
            {
                var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
                var tcpListeners = ipGlobalProperties.GetActiveTcpListeners();

                foreach (var endpoint in tcpListeners)
                {
                    if (endpoint.Port == port)
                        return true;
                }
                return false;
            }
            catch
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Any, port);
                    listener.Start();
                    listener.Stop();
                    return false;
                }
                catch
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// 获取本机IP地址
        /// </summary>
        private string GetLocalIPAddresses()
        {
            try
            {
                var addresses = Dns.GetHostAddresses(Dns.GetHostName())
                    .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                    .Select(ip => ip.ToString());
                return string.Join(", ", addresses);
            }
            catch
            {
                return "未知";
            }
        }

        public Tuple<double, double, double, double> GetCoordinateBounds(string coordinates)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            var segments = coordinates.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                var values = segment.Split(',');
                for (int i = 0; i < values.Length; i += 2)
                {
                    double x, y;
                    if (double.TryParse(values[i], out x) &&
                        double.TryParse(values[i + 1], out y))
                    {
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }

            return Tuple.Create(minX, minY, maxX, maxY);
        }

        #endregion

        #region 日志和事件

        private void LogInfo(string message)
        {
            _log.Information("[Laser] {Message}", message);
        }

        private void LogDebug(string message)
        {
            _log.Debug("[Laser] {Message}", message);
        }

        private void LogError(string message)
        {
            _log.Error("[Laser] {Message}", message);
        }

        private void RaiseStatus(string message)
        {
            var handler = StatusChanged;
            if (handler != null)
            {
                handler(this, message);
            }
            Debug.WriteLine(string.Format("[Laser] {0}", message));
        }

        private void RaiseError(string message)
        {
            var handler = ErrorOccurred;
            if (handler != null)
            {
                handler(this, message);
            }
            Debug.WriteLine(string.Format("[Laser Error] {0}", message));
        }

        private void RaiseConnectionChanged(bool connected)
        {
            var handler = ConnectionChanged;
            if (handler != null)
            {
                handler(this, connected);
            }
        }

        private void RaiseResponseReceived(string response, LaserResponseType type)
        {
            var handler = ResponseReceived;
            if (handler != null)
            {
                handler(this, new LaserResponseEventArgs
                {
                    Response = response,
                    Timestamp = DateTime.Now,
                    ResponseType = type,
                    OriginalCommand = _lastCommand
                });
            }
        }

        #endregion

        #region 资源释放

        public void Dispose()
        {
            try
            {
                DisconnectAsync().Wait(3000);
            }
            catch { }
        }

        #endregion
    }
}