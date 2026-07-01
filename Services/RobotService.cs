using Modbus.Device;
using SeedCut.Framework.Services.Handlers;
using SeedCut.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Services
{
    /// <summary>
    /// 机器人服务实现
    /// 双通道架构：
    /// 1. Modbus TCP (端口502) - 底层控制（伺服、AR程序、JOG）
    /// 2. TCP Socket (端口6000) - 运行时协调（与AR程序通信）
    /// </summary>
    public class RobotService : IRobotService
    {
        #region 字段

        // Modbus TCP 相关
        private TcpClient _modbusTcpClient;
        private IModbusMaster _modbusMaster;
        private RobotConfig _config;
        private RobotCoordinates _currentCoordinates;
        private bool _isConnected;
        private bool _isServoEnabled;
        private Task _listenTask;  // ★ 新增：保存监听任务引用

        // TCP 服务器相关（用于AR程序通信）
        private TcpListener _tcpServer;
        private TcpClient _arClient;
        private NetworkStream _arStream;
        private bool _isTcpServerRunning;
        private bool _isArClientConnected;
        private CancellationTokenSource _tcpServerCts;
        private readonly int _defaultTcpPort = 6000;

        // 线程安全
        private readonly object _lockObj = new object();
        private readonly object _tcpLockObj = new object();
        private StringBuilder _receiveBuffer = new StringBuilder();  // ★ 新增：接收缓冲区

        #endregion

        #region 属性

        public bool IsConnected
        {
            get { lock (_lockObj) { return _isConnected; } }
            private set
            {
                lock (_lockObj)
                {
                    if (_isConnected != value)
                    {
                        _isConnected = value;
                        OnConnectionStatusChanged(value, value ? "已连接" : "已断开");
                    }
                }
            }
        }

        public bool IsTcpServerRunning
        {
            get { lock (_tcpLockObj) { return _isTcpServerRunning; } }
            private set { lock (_tcpLockObj) { _isTcpServerRunning = value; } }
        }

        public bool IsArClientConnected
        {
            get { lock (_tcpLockObj) { return _isArClientConnected; } }
            private set
            {
                lock (_tcpLockObj)
                {
                    if (_isArClientConnected != value)
                    {
                        _isArClientConnected = value;
                        ArClientConnectionChanged?.Invoke(this, value);
                    }
                }
            }
        }

        public RobotConfig Config => _config;

        public RobotCoordinates CurrentCoordinates
        {
            get { lock (_lockObj) { return _currentCoordinates; } }
            private set
            {
                lock (_lockObj)
                {
                    _currentCoordinates = value;
                    OnCoordinatesUpdated(value);
                }
            }
        }

        public bool IsServoEnabled
        {
            get { lock (_lockObj) { return _isServoEnabled; } }
            private set { lock (_lockObj) { _isServoEnabled = value; } }
        }

        #endregion

        #region 事件

        public event EventHandler<ConnectionStatusChangedEventArgs> ConnectionStatusChanged;
        public event EventHandler<CoordinatesUpdatedEventArgs> CoordinatesUpdated;
        public event EventHandler<RobotCommandEventArgs> CommandReceived;
        public event EventHandler<bool> ArClientConnectionChanged;

        #endregion

        #region 构造函数

        public RobotService()
        {
            _config = RobotConfig.Load();
            _currentCoordinates = new RobotCoordinates();
        }

        #endregion

        #region Modbus 连接管理

        public async Task<(bool success, string message)> ConnectAsync()
        {
            if (IsConnected)
            {
                return (false, "设备已连接，请勿重复连接");
            }

            try
            {
                _modbusTcpClient = new TcpClient();
                _modbusTcpClient.ReceiveTimeout = _config.ReadWriteTimeout;
                _modbusTcpClient.SendTimeout = _config.ReadWriteTimeout;

                var connectTask = _modbusTcpClient.ConnectAsync(_config.IPAddress, _config.Port);
                var timeoutTask = Task.Delay(_config.ConnectTimeout);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _modbusTcpClient?.Close();
                    return (false, "连接超时");
                }

                if (!_modbusTcpClient.Connected)
                {
                    return (false, "连接失败");
                }

                _modbusMaster = ModbusIpMaster.CreateIp(_modbusTcpClient);
                _modbusMaster.Transport.ReadTimeout = _config.ReadWriteTimeout;
                _modbusMaster.Transport.WriteTimeout = _config.ReadWriteTimeout;

                IsConnected = true;

                System.Diagnostics.Debug.WriteLine(string.Format("✅ 机器人Modbus连接成功: {0}:{1}", _config.IPAddress, _config.Port));

                // 连接成功后读取初始状态
                try
                {
                    await Task.Delay(200);
                    await GetServoEnableAsync();
                    await ReadCoordinatesAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ 读取初始状态失败: " + ex.Message);
                }

                return (true, "机器人连接成功");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("❌ 机器人连接失败: " + ex.Message);
                _modbusTcpClient?.Close();
                _modbusTcpClient = null;
                _modbusMaster = null;
                return (false, "连接失败: " + ex.Message);
            }
        }

        public async Task<(bool success, string message)> DisconnectAsync()
        {
            if (!IsConnected)
            {
                return (false, "设备未连接，无需断开");
            }

            try
            {
                if (IsServoEnabled)
                {
                    await SetServoEnableAsync(false);
                }

                _modbusMaster?.Dispose();
                _modbusMaster = null;
                _modbusTcpClient?.Close();
                _modbusTcpClient = null;

                IsConnected = false;

                System.Diagnostics.Debug.WriteLine("✅ 机器人已断开连接");
                return (true, "机器人已断开连接");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("❌ 断开连接异常: " + ex.Message);
                return (false, "断开连接异常: " + ex.Message);
            }
        }

        #endregion

        #region TCP 服务器管理

        /// <summary>
        /// 启动TCP服务器，用于接收AR程序的命令
        /// </summary>
        public async Task<(bool success, string message)> StartTcpServerAsync(int port = 6000)
        {
            if (IsTcpServerRunning)
            {
                return (false, "TCP服务器已在运行");
            }

            try
            {
                _tcpServerCts = new CancellationTokenSource();
                _tcpServer = new TcpListener(IPAddress.Any, port);

                // ★ 关键：设置端口重用，避免 TIME_WAIT 问题
                _tcpServer.Server.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true);

                _tcpServer.Start();
                IsTcpServerRunning = true;

                System.Diagnostics.Debug.WriteLine(string.Format("✅ TCP服务器已启动，监听端口: {0}", port));

                // ★ 保存任务引用
                _listenTask = Task.Run(() => ListenForClientAsync(_tcpServerCts.Token));

                return (true, string.Format("TCP服务器已启动，监听端口: {0}", port));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("❌ TCP服务器启动失败: " + ex.Message);
                return (false, "TCP服务器启动失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 停止TCP服务器
        /// </summary>
        public void StopTcpServer()
        {
            if (!IsTcpServerRunning && _tcpServer == null)
            {
                return;
            }

            System.Diagnostics.Debug.WriteLine("🛑 正在停止TCP服务器...");

            try
            {
                // 1. 取消监听
                try { _tcpServerCts?.Cancel(); } catch { }

                // 2. 停止服务器（会使 AcceptTcpClientAsync 抛出异常从而退出）
                try { _tcpServer?.Stop(); } catch { }

                // 3. 等待监听任务结束（带超时）
                if (_listenTask != null)
                {
                    try
                    {
                        if (!_listenTask.Wait(TimeSpan.FromSeconds(3)))
                        {
                            System.Diagnostics.Debug.WriteLine("⚠️ 监听任务超时未结束");
                        }
                    }
                    catch (AggregateException) { }
                    catch (TaskCanceledException) { }
                }

                // 4. 关闭客户端连接（顺序：先流再客户端）
                try { _arStream?.Close(); } catch { }
                try { _arStream?.Dispose(); } catch { }
                try { _arClient?.Close(); } catch { }
                try { _arClient?.Dispose(); } catch { }

                // 5. 释放 CTS
                try { _tcpServerCts?.Dispose(); } catch { }

                // 6. 置空所有引用
                _arStream = null;
                _arClient = null;
                _tcpServer = null;
                _tcpServerCts = null;
                _listenTask = null;

                // 7. 更新状态
                IsArClientConnected = false;
                IsTcpServerRunning = false;

                System.Diagnostics.Debug.WriteLine("✅ TCP服务器已完全停止");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ 停止TCP服务器异常: " + ex.Message);
            }
        }

        /// <summary>
        /// 监听客户端连接
        /// </summary>
        private async Task ListenForClientAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && IsTcpServerRunning)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("⏳ 等待AR程序连接...");

                    // 等待客户端连接
                    _arClient = await _tcpServer.AcceptTcpClientAsync();
                    _arStream = _arClient.GetStream();
                    IsArClientConnected = true;

                    var remoteEndPoint = _arClient.Client.RemoteEndPoint as IPEndPoint;
                    System.Diagnostics.Debug.WriteLine(string.Format("✅ AR程序已连接: {0}:{1}",
                        remoteEndPoint?.Address, remoteEndPoint?.Port));

                    // 开始接收数据
                    await ReceiveDataAsync(ct);
                }
                catch (ObjectDisposedException)
                {
                    // 服务器已停止
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ 客户端连接异常: " + ex.Message);
                    IsArClientConnected = false;

                    // 短暂延迟后重试
                    await Task.Delay(1000, ct);
                }
            }
        }

        /// <summary>
        /// 接收AR程序发送的数据（★ 重构：用 \n 分隔符拆包）
        /// </summary>
        private async Task ReceiveDataAsync(CancellationToken ct)
        {
            byte[] buffer = new byte[4096];

            // 清空上一次残留
            _receiveBuffer.Clear();

            while (!ct.IsCancellationRequested && IsArClientConnected)
            {
                try
                {
                    int bytesRead = await _arStream.ReadAsync(buffer, 0, buffer.Length, ct);

                    if (bytesRead == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ AR程序断开连接");
                        IsArClientConnected = false;
                        break;
                    }

                    string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    _receiveBuffer.Append(data);

                    // 按 \n 提取完整消息
                    string bufStr = _receiveBuffer.ToString();
                    int newlinePos;

                    while ((newlinePos = bufStr.IndexOf('\n')) >= 0)
                    {
                        // 取出一条完整消息（去掉可能的 \r）
                        string msg = bufStr.Substring(0, newlinePos).TrimEnd('\r');
                        bufStr = bufStr.Substring(newlinePos + 1);

                        if (string.IsNullOrWhiteSpace(msg))
                            continue;

                        System.Diagnostics.Debug.WriteLine("📥 收到AR命令: " + msg);

                        // 触发事件（传递原始消息字符串）
                        var cmdType = ParseCommandType(msg);
                        var args = new RobotCommandEventArgs
                        {
                            Command = msg,
                            Timestamp = DateTime.Now,
                            CommandType = cmdType
                        };

                        CommandReceived?.Invoke(this, args);
                    }

                    // 把剩余不完整数据放回缓冲区
                    _receiveBuffer.Clear();
                    _receiveBuffer.Append(bufStr);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ 接收数据异常: " + ex.Message);
                    IsArClientConnected = false;
                    break;
                }
            }
        }

        /// <summary>
        /// 解析命令类型（★ 重构：支持新协议命令）
        /// </summary>
        private RobotCommandType ParseCommandType(string command)
        {
            if (string.IsNullOrEmpty(command))
                return RobotCommandType.Unknown;

            // 带参数的命令：用 StartsWith 匹配
            if (command.StartsWith("AT_CATCH:"))
                return RobotCommandType.AtCatch;
            if (command.StartsWith("ERR:"))
                return RobotCommandType.Error;

            // 无参数命令：精确匹配
            switch (command)
            {
                case "WAIT_INIT": return RobotCommandType.WaitInit;
                case "READY": return RobotCommandType.Ready;
                case "CLAMP": return RobotCommandType.Clamp;
                case "GRABBED": return RobotCommandType.Grabbed;
                case "CHECK": return RobotCommandType.Check;
                case "FLY_CAPTURE": return RobotCommandType.FlyCapture;
                case "RELEASE": return RobotCommandType.Release;
                case "PLACED": return RobotCommandType.Placed;
                case "BATCH_DONE": return RobotCommandType.BatchDone;
                default: return RobotCommandType.Unknown;
            }
        }
        /// <summary>
        /// 发送命令到AR程序
        /// </summary>
        public async Task<bool> SendCommandAsync(string command)
        {
            if (!IsArClientConnected || _arStream == null)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ AR程序未连接，无法发送命令");
                return false;
            }

            try
            {
                // ★ 确保命令以 \n 结尾（AR端用 \n 作为消息分隔符）
                if (!command.EndsWith("\n"))
                    command = command + "\n";

                byte[] data = Encoding.UTF8.GetBytes(command);
                await _arStream.WriteAsync(data, 0, data.Length);
                await _arStream.FlushAsync();

                System.Diagnostics.Debug.WriteLine("📤 发送命令: " + command.TrimEnd('\n'));
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("❌ 发送命令失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 发送坐标数据到AR程序
        /// 格式：X0;Y0;A0;X1;Y1;A1;...
        /// </summary>
        public async Task<bool> SendCoordinatesAsync(List<VisionCoordinate> coordinates)
        {
            if (coordinates == null || coordinates.Count == 0)
            {
                return false;
            }

            var sb = new StringBuilder();
            foreach (var coord in coordinates)
            {
                sb.AppendFormat("{0:F2};{1:F2};{2:F1};", coord.X, coord.Y, coord.Angle);
            }

            // 移除最后的分号
            if (sb.Length > 0)
            {
                sb.Length--;
            }

            return await SendCommandAsync(sb.ToString());
        }

        #endregion

        #region 基本控制（Modbus）

        public async Task<RobotCoordinates> ReadCoordinatesAsync()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("请先连接机器人");
            }

            try
            {
                ushort startAddress = _config.CartesianCoordinatesAddress;
                ushort[] registers = await _modbusMaster.ReadHoldingRegistersAsync(
                    _config.StationId, startAddress, 10);

                float x = CombineLittleEndian(registers[0], registers[1]);
                float y = CombineLittleEndian(registers[2], registers[3]);
                float z = CombineLittleEndian(registers[4], registers[5]);
                float c = CombineLittleEndian(registers[6], registers[7]);

                var coordinates = new RobotCoordinates
                {
                    X = x,
                    Y = y,
                    Z = z,
                    C = c
                };

                CurrentCoordinates = coordinates;
                return coordinates;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("❌ 读取坐标失败: " + ex.Message);
                throw;
            }
        }

        public async Task<bool> GetServoEnableAsync()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("请先连接机器人");
            }

            try
            {
                ushort[] registers = await _modbusMaster.ReadHoldingRegistersAsync(
                    _config.StationId, _config.ServoEnableAddress, 1);

                bool enabled = registers[0] == 1;
                IsServoEnabled = enabled;

                System.Diagnostics.Debug.WriteLine(string.Format("📡 读取伺服状态: 0x{0:X4} = {1}",
                    registers[0], enabled ? "已使能" : "未使能"));

                return enabled;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("❌ 读取伺服状态失败: " + ex.Message);
                throw;
            }
        }

        public async Task<(bool success, string message)> SetServoEnableAsync(bool enable)
        {
            if (!IsConnected)
            {
                return (false, "请先连接机器人");
            }

            try
            {
                ushort value = (ushort)(enable ? 1 : 0);
                await _modbusMaster.WriteSingleRegisterAsync(
                    _config.StationId, _config.ServoEnableAddress, value);

                IsServoEnabled = enable;

                string msg = enable ? "伺服使能已打开" : "伺服使能已关闭";
                System.Diagnostics.Debug.WriteLine("✅ " + msg);
                return (true, msg);
            }
            catch (Exception ex)
            {
                string msg = "设置伺服使能失败: " + ex.Message;
                System.Diagnostics.Debug.WriteLine("❌ " + msg);
                return (false, msg);
            }
        }

        #endregion

        #region AR 程序控制

        public async Task<(bool success, string message)> RunARAsync()
        {
            return await WriteARCommandAsync(0x0001, "运行机器人程序");
        }

        public async Task<(bool success, string message)> PauseARAsync()
        {
            return await WriteARCommandAsync(0x0002, "暂停机器人程序");
        }

        public async Task<(bool success, string message)> StopARAsync()
        {
            return await WriteARCommandAsync(0x0003, "停止机器人程序");
        }

        public async Task<(bool success, string message)> ResetRobotAsync()
        {
            return await WriteARCommandAsync(0x0004, "复位机器人");
        }

        private async Task<(bool success, string message)> WriteARCommandAsync(ushort command, string action)
        {
            if (!IsConnected)
            {
                return (false, "请先连接机器人");
            }

            try
            {
                await _modbusMaster.WriteMultipleRegistersAsync(
                    _config.StationId,
                    _config.SetARAddress,
                    new ushort[] { command });

                System.Diagnostics.Debug.WriteLine("✅ " + action + "成功");
                return (true, action + "成功");
            }
            catch (Exception ex)
            {
                string msg = action + "失败: " + ex.Message;
                System.Diagnostics.Debug.WriteLine("❌ " + msg);
                return (false, msg);
            }
        }

        #endregion

        #region JOG 运动控制

        public async Task<(bool success, string message)> JogAsync(int axis)
        {
            if (!IsConnected)
            {
                return (false, "请先连接机器人");
            }

            if (!IsServoEnabled)
            {
                return (false, "请先打开伺服使能");
            }

            try
            {
                ushort jogCommand = (ushort)axis;
                await _modbusMaster.WriteSingleRegisterAsync(
                    _config.StationId, _config.JogXYZCAddress, jogCommand);

                string axisName = GetAxisName(axis);
                System.Diagnostics.Debug.WriteLine("✅ JOG 运动: " + axisName);
                return (true, "JOG 运动: " + axisName);
            }
            catch (Exception ex)
            {
                string msg = "JOG 运动失败: " + ex.Message;
                System.Diagnostics.Debug.WriteLine("❌ " + msg);
                return (false, msg);
            }
        }

        public async Task<(bool success, string message)> StopJogAsync()
        {
            if (!IsConnected)
            {
                return (false, "请先连接机器人");
            }

            try
            {
                await _modbusMaster.WriteSingleRegisterAsync(
                    _config.StationId, _config.JogXYZCAddress, 0);

                System.Diagnostics.Debug.WriteLine("✅ 停止 JOG 运动");
                return (true, "停止 JOG 运动");
            }
            catch (Exception ex)
            {
                string msg = "停止 JOG 失败: " + ex.Message;
                System.Diagnostics.Debug.WriteLine("❌ " + msg);
                return (false, msg);
            }
        }

        private string GetAxisName(int axis)
        {
            switch (axis)
            {
                case 1: return "X+";
                case 2: return "X-";
                case 3: return "Y+";
                case 4: return "Y-";
                case 5: return "Z+";
                case 6: return "Z-";
                case 7: return "C+";
                case 8: return "C-";
                default: return "未知";
            }
        }

        #endregion

        #region 配置管理

        public void ReloadConfig()
        {
            _config = RobotConfig.Load();
            System.Diagnostics.Debug.WriteLine("✅ 配置已重新加载");
        }

        public void SaveConfig()
        {
            _config.Save();
            System.Diagnostics.Debug.WriteLine("✅ 配置已保存");
        }

        #endregion

        #region 辅助方法

        private float CombineLittleEndian(ushort low, ushort high)
        {
            uint intRep = ((uint)high << 16) | low;
            byte[] bytes = BitConverter.GetBytes(intRep);
            return BitConverter.ToSingle(bytes, 0);
        }

        private void OnConnectionStatusChanged(bool isConnected, string message)
        {
            ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs
            {
                IsConnected = isConnected,
                Message = message
            });
        }

        private void OnCoordinatesUpdated(RobotCoordinates coordinates)
        {
            CoordinatesUpdated?.Invoke(this, new CoordinatesUpdatedEventArgs
            {
                Coordinates = coordinates
            });
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            StopTcpServer();
            DisconnectAsync().Wait();
        }

        #endregion
    }
}
