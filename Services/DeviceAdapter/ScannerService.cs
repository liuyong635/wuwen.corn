using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// 使用别名解决命名空间冲突
using FrameworkDeviceType = SeedCut.Framework.Core.DeviceType;

namespace SeedCut.Services.DeviceAdapter
{
    #region 扫码器配置

    /// <summary>
    /// 扫码器类型
    /// </summary>
    public enum ScannerType
    {
        /// <summary>
        /// 小料盘扫码器
        /// </summary>
        SmallTray,

        /// <summary>
        /// 大料盘扫码器
        /// </summary>
        LargeTray
    }

    /// <summary>
    /// 扫码器配置
    /// 
    /// 与C++配置对照：
    /// - 小料盘: IP 192.168.0.105, 命令端口 6600, 数据端口 6666
    /// - 大料盘: IP 192.168.0.104, 命令端口 6600, 数据端口 6666
    /// </summary>
    public class ScannerDeviceConfig
    {
        /// <summary>
        /// 设备ID
        /// </summary>
        public string DeviceId { get; set; } = "Scanner";

        /// <summary>
        /// 设备名称
        /// </summary>
        public string DeviceName { get; set; } = "条码扫描器";

        /// <summary>
        /// 扫码器类型
        /// </summary>
        public ScannerType ScannerType { get; set; } = ScannerType.SmallTray;

        /// <summary>
        /// 扫码器IP地址
        /// C++: startSmallBarCodeCmd("192.168.0.105", 6600)
        /// C++: startLargeBarCodeCmd("192.168.0.104", 6600)
        /// </summary>
        public string IpAddress { get; set; } = "192.168.0.105";

        /// <summary>
        /// 命令端口（发送start命令）
        /// C++: 6600
        /// </summary>
        public int CommandPort { get; set; } = 6600;

        /// <summary>
        /// 数据端口（接收扫码结果）
        /// C++: startSmallBarCodeRecv("192.168.0.105", 6666)
        /// </summary>
        public int DataPort { get; set; } = 6666;

        /// <summary>
        /// 连接超时（毫秒）
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 单次扫码超时（毫秒）
        /// C++: waitForBytesWritten(3000)
        /// </summary>
        public int ScanTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// NoRead重试次数
        /// C++: updateTime() 中持续重试
        /// </summary>
        public int NoReadRetryCount { get; set; } = 10;

        /// <summary>
        /// NoRead重试间隔（毫秒）
        /// C++: 约1秒（Timer间隔）
        /// </summary>
        public int NoReadRetryIntervalMs { get; set; } = 1000;

        /// <summary>
        /// 扫码触发命令
        /// C++: QString message = "start";
        /// </summary>
        public string TriggerCommand { get; set; } = "start";

        /// <summary>
        /// 接收数据等待时间（毫秒）
        /// 用于处理TCP数据分片
        /// </summary>
        public int DataWaitMs { get; set; } = 50;

        /// <summary>
        /// 创建小料盘扫码器配置
        /// </summary>
        public static ScannerDeviceConfig CreateSmallTray()
        {
            return new ScannerDeviceConfig
            {
                DeviceId = "Scanner_Small",
                DeviceName = "小料盘扫码器",
                ScannerType = ScannerType.SmallTray,
                IpAddress = "192.168.0.105",
                CommandPort = 6600,
                DataPort = 6666
            };
        }

        /// <summary>
        /// 创建大料盘扫码器配置
        /// </summary>
        public static ScannerDeviceConfig CreateLargeTray()
        {
            return new ScannerDeviceConfig
            {
                DeviceId = "Scanner_Large",
                DeviceName = "大料盘扫码器",
                ScannerType = ScannerType.LargeTray,
                IpAddress = "192.168.0.104",
                CommandPort = 6600,
                DataPort = 6666
            };
        }
    }

    #endregion

    #region 扫码器服务接口

    /// <summary>
    /// 扫码器服务接口
    /// 用于底层TCP通信
    /// </summary>
    public interface IScannerService : IDisposable
    {
        /// <summary>
        /// 是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 是否正在扫码
        /// </summary>
        bool IsScanning { get; }

        /// <summary>
        /// 最后一次扫码结果
        /// </summary>
        ScanResult LastResult { get; }

        /// <summary>
        /// 连接状态变更事件
        /// </summary>
        event EventHandler<bool> ConnectionChanged;

        /// <summary>
        /// 扫码结果事件
        /// </summary>
        event EventHandler<ScanResult> CodeScanned;

        /// <summary>
        /// 错误事件
        /// </summary>
        event EventHandler<string> ErrorOccurred;

        /// <summary>
        /// 连接扫码器
        /// </summary>
        Task<bool> ConnectAsync(CancellationToken ct = default);

        /// <summary>
        /// 断开连接
        /// </summary>
        void Disconnect();

        /// <summary>
        /// 执行扫码（单次）
        /// </summary>
        Task<ScanResult> ScanAsync(CancellationToken ct = default);

        /// <summary>
        /// 执行扫码（带超时）
        /// </summary>
        Task<ScanResult> ScanAsync(TimeSpan timeout, CancellationToken ct = default);

        /// <summary>
        /// 执行扫码（带重试，匹配C++行为）
        /// </summary>
        Task<ScanResult> ScanWithRetryAsync(int maxRetries, TimeSpan retryInterval, CancellationToken ct = default);
    }

    #endregion

    #region 扫码器服务实现

    /// <summary>
    /// 扫码器服务实现
    /// 基于TCP双端口通信，匹配C++ mainuser.cpp实现
    /// 
    /// 通信模式：
    /// - 命令端口(6600): 发送 "start" 触发扫码
    /// - 数据端口(6666): 接收扫码结果
    /// 
    /// C++对照：
    /// - startSmallBarCodeCmd(): 连接命令端口
    /// - startSmallBarCodeRecv(): 连接数据端口
    /// - startSmallScan(): 发送 "start" 命令
    /// - onSmallScanDataReady(): 接收扫码结果
    /// </summary>
    public class ScannerService : IScannerService
    {
        #region 私有字段

        private readonly ScannerDeviceConfig _config;
        private TcpClient _cmdClient;
        private TcpClient _dataClient;
        private NetworkStream _cmdStream;
        private NetworkStream _dataStream;

        private volatile bool _isConnected;
        private volatile bool _isScanning;
        private ScanResult _lastResult;
        private bool _disposed;

        private readonly object _scanLock = new object();
        private readonly object _connectLock = new object();

        #endregion

        #region 属性

        public bool IsConnected => _isConnected && (_cmdClient?.Connected ?? false) && (_dataClient?.Connected ?? false);
        public bool IsScanning => _isScanning;
        public ScanResult LastResult => _lastResult;

        #endregion

        #region 事件

        public event EventHandler<bool> ConnectionChanged;
        public event EventHandler<ScanResult> CodeScanned;
        public event EventHandler<string> ErrorOccurred;

        #endregion

        #region 构造函数

        public ScannerService(ScannerDeviceConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        #endregion

        #region 连接管理

        /// <summary>
        /// 连接扫码器（双端口TCP连接）
        /// 
        /// C++对照：
        /// - startSmallBarCodeCmd("192.168.0.105", 6600): 连接命令端口
        /// - startSmallBarCodeRecv("192.168.0.105", 6666): 连接数据端口
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            lock (_connectLock)
            {
                if (_isConnected)
                    return true;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Scanner] 正在连接 {_config.DeviceName} ({_config.IpAddress})...");

                // 清理旧连接
                CleanupConnections();

                // 创建命令Socket
                _cmdClient = new TcpClient();
                _cmdClient.ReceiveTimeout = _config.ScanTimeoutMs;
                _cmdClient.SendTimeout = _config.ConnectionTimeoutMs;

                // 创建数据Socket
                _dataClient = new TcpClient();
                _dataClient.ReceiveTimeout = _config.ScanTimeoutMs;
                _dataClient.SendTimeout = _config.ConnectionTimeoutMs;

                // 连接命令端口和数据端口
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(_config.ConnectionTimeoutMs);

                    // 并行连接两个端口
                    var cmdConnectTask = _cmdClient.ConnectAsync(_config.IpAddress, _config.CommandPort);
                    var dataConnectTask = _dataClient.ConnectAsync(_config.IpAddress, _config.DataPort);

                    // 等待两个连接都完成
                    await Task.WhenAll(cmdConnectTask, dataConnectTask).ConfigureAwait(false);
                }

                _cmdStream = _cmdClient.GetStream();
                _dataStream = _dataClient.GetStream();

                lock (_connectLock)
                {
                    _isConnected = true;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[Scanner] {_config.DeviceName} 连接成功 " +
                    $"(命令端口:{_config.CommandPort}, 数据端口:{_config.DataPort})");

                ConnectionChanged?.Invoke(this, true);
                return true;
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[Scanner] {_config.DeviceName} 连接超时");
                CleanupConnections();
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Scanner] {_config.DeviceName} 连接失败: {ex.Message}");
                CleanupConnections();
                ErrorOccurred?.Invoke(this, $"连接扫码器失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            lock (_connectLock)
            {
                _isConnected = false;
            }

            CleanupConnections();
            ConnectionChanged?.Invoke(this, false);

            System.Diagnostics.Debug.WriteLine($"[Scanner] {_config.DeviceName} 已断开连接");
        }

        private void CleanupConnections()
        {
            try { _cmdStream?.Dispose(); } catch { }
            try { _dataStream?.Dispose(); } catch { }
            try { _cmdClient?.Close(); } catch { }
            try { _dataClient?.Close(); } catch { }

            _cmdStream = null;
            _dataStream = null;
            _cmdClient = null;
            _dataClient = null;
        }

        #endregion

        #region 扫码操作

        /// <summary>
        /// 执行扫码（单次，使用配置的超时）
        /// </summary>
        public Task<ScanResult> ScanAsync(CancellationToken ct = default)
        {
            return ScanAsync(TimeSpan.FromMilliseconds(_config.ScanTimeoutMs), ct);
        }

        /// <summary>
        /// 执行扫码（带超时）
        /// 
        /// C++对照：
        /// - startSmallScan(): 发送 "start" 命令
        /// - onSmallScanDataReady(): 接收结果
        /// </summary>
        public async Task<ScanResult> ScanAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                return ScanResult.Fail("扫码器未连接");
            }

            lock (_scanLock)
            {
                if (_isScanning)
                {
                    return ScanResult.Fail("正在扫码中");
                }
                _isScanning = true;
            }

            try
            {
                // ========================================
                // 发送扫码命令
                // C++: QString message = "start";
                //      smallCmdSocket->write(data);
                // ========================================
                var cmdBytes = Encoding.UTF8.GetBytes(_config.TriggerCommand);
                await _cmdStream.WriteAsync(cmdBytes, 0, cmdBytes.Length, ct).ConfigureAwait(false);
                await _cmdStream.FlushAsync(ct).ConfigureAwait(false);

                System.Diagnostics.Debug.WriteLine($"[Scanner] 已发送扫码命令: {_config.TriggerCommand}");

                // ========================================
                // 接收扫码结果
                // C++: onSmallScanDataReady()
                //      QByteArray data = smallRecvSocket->readAll();
                //      QString receivedString = QString::fromUtf8(data).trimmed();
                // ========================================
                string receivedString = await ReadDataWithTimeoutAsync(timeout, ct).ConfigureAwait(false);

                if (string.IsNullOrEmpty(receivedString))
                {
                    return ScanResult.Fail("未收到扫码结果");
                }

                System.Diagnostics.Debug.WriteLine($"[Scanner] 收到扫码结果: {receivedString}");

                // 创建结果
                var result = CreateScanResult(receivedString);
                _lastResult = result;

                // 触发事件
                CodeScanned?.Invoke(this, result);

                return result;
            }
            catch (OperationCanceledException)
            {
                return ScanResult.Fail("扫码超时或被取消");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Scanner] 扫码异常: {ex.Message}");
                ErrorOccurred?.Invoke(this, $"扫码异常: {ex.Message}");
                return ScanResult.Fail($"扫码异常: {ex.Message}");
            }
            finally
            {
                lock (_scanLock)
                {
                    _isScanning = false;
                }
            }
        }

        /// <summary>
        /// 执行扫码（带重试，匹配C++行为）
        /// 
        /// C++对照：
        /// updateTime() 中的逻辑：
        /// if(bSmallScanning && smallBarCode.compare("NoRead", Qt::CaseInsensitive) == 0)
        ///     startSmallScan();  // 持续重试直到获得有效条码
        /// </summary>
        public async Task<ScanResult> ScanWithRetryAsync(
            int maxRetries,
            TimeSpan retryInterval,
            CancellationToken ct = default)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                System.Diagnostics.Debug.WriteLine($"[Scanner] 第 {attempt}/{maxRetries} 次扫码尝试");

                var result = await ScanAsync(ct).ConfigureAwait(false);

                // 如果成功且不是NoRead，返回结果
                if (result.Success && !IsNoRead(result.Code))
                {
                    System.Diagnostics.Debug.WriteLine($"[Scanner] 扫码成功: {result.Code}");
                    return result;
                }

                // 如果是NoRead，等待后重试
                // C++: if(smallBarCode.compare("NoRead", Qt::CaseInsensitive) == 0) startSmallScan();
                if (IsNoRead(result.Code))
                {
                    System.Diagnostics.Debug.WriteLine($"[Scanner] 收到NoRead，等待 {retryInterval.TotalSeconds}s 后重试...");

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryInterval, ct).ConfigureAwait(false);
                    }
                }
                else if (!result.Success)
                {
                    // 其他失败情况，也重试
                    System.Diagnostics.Debug.WriteLine($"[Scanner] 扫码失败: {result.ErrorMessage}，准备重试...");

                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryInterval, ct).ConfigureAwait(false);
                    }
                }
            }

            // 达到最大重试次数
            return ScanResult.Fail($"达到最大重试次数({maxRetries})，仍未获得有效条码");
        }

        /// <summary>
        /// 读取数据（带超时，处理TCP分片）
        /// 
        /// 增强实现：处理可能的TCP数据分片
        /// </summary>
        private async Task<string> ReadDataWithTimeoutAsync(TimeSpan timeout, CancellationToken ct)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(timeout);

                var buffer = new List<byte>();
                var singleBuffer = new byte[1024];

                try
                {
                    // 首次读取
                    int bytesRead = await _dataStream.ReadAsync(
                        singleBuffer, 0, singleBuffer.Length, cts.Token).ConfigureAwait(false);

                    if (bytesRead == 0)
                    {
                        return null;
                    }

                    buffer.AddRange(singleBuffer.Take(bytesRead));

                    // 检查是否有更多数据（处理分片）
                    while (_dataStream.DataAvailable)
                    {
                        // 短暂延时确保数据到达
                        await Task.Delay(_config.DataWaitMs, cts.Token).ConfigureAwait(false);

                        if (!_dataStream.DataAvailable)
                            break;

                        bytesRead = await _dataStream.ReadAsync(
                            singleBuffer, 0, singleBuffer.Length, cts.Token).ConfigureAwait(false);

                        if (bytesRead == 0)
                            break;

                        buffer.AddRange(singleBuffer.Take(bytesRead));
                    }

                    // C++: QString receivedString = QString::fromUtf8(data).trimmed();
                    return Encoding.UTF8.GetString(buffer.ToArray()).Trim();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Scanner] 读取数据异常: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// 检查是否是NoRead响应
        /// C++: receivedString.compare("NoRead", Qt::CaseInsensitive) == 0
        /// </summary>
        private bool IsNoRead(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return true;

            return string.Equals(code.Trim(), "NoRead", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 创建扫码结果
        /// 
        /// C++对照：
        /// if (receivedString.compare("NoRead", Qt::CaseInsensitive) == 0) {
        ///     qDebug() << "接收到NoRead响应";
        /// } else if (!receivedString.isEmpty()) {
        ///     qDebug() << "接收到有效数据:" << receivedString;
        ///     smallBarCode = receivedString;
        /// }
        /// </summary>
        private ScanResult CreateScanResult(string receivedString)
        {
            if (string.IsNullOrWhiteSpace(receivedString))
            {
                return ScanResult.Fail("扫码结果为空");
            }

            receivedString = receivedString.Trim();

            // 检查是否是NoRead
            if (IsNoRead(receivedString))
            {
                return new ScanResult
                {
                    Success = true,  // 通信成功，但是没有读到码
                    Code = "NoRead",
                    CodeType = null,
                    ErrorMessage = null,
                    Timestamp = DateTime.Now
                };
            }

            // 有效条码
            return new ScanResult
            {
                Success = true,
                Code = receivedString,
                CodeType = DetectCodeType(receivedString),
                ErrorMessage = null,
                Timestamp = DateTime.Now
            };
        }

        /// <summary>
        /// 检测条码类型（简单启发式）
        /// </summary>
        private string DetectCodeType(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "Unknown";

            // 简单的类型检测
            if (code.Length >= 13 && code.All(char.IsDigit))
                return "EAN13";
            if (code.Length == 12 && code.All(char.IsDigit))
                return "UPC";
            if (code.Length == 8 && code.All(char.IsDigit))
                return "EAN8";
            if (code.All(c => char.IsLetterOrDigit(c) || c == '-'))
                return "Code128";

            return "Unknown";
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            Disconnect();
            _disposed = true;

            System.Diagnostics.Debug.WriteLine($"[Scanner] {_config.DeviceName} 已释放");
        }

        #endregion
    }

    #endregion

    #region 扫码器设备适配器

    /// <summary>
    /// 扫码器设备适配器
    /// 实现 IScannerDevice 接口，包装 ScannerService
    /// </summary>
    public class ScannerDeviceAdapter : IScannerDevice
    {
        #region 私有字段

        private readonly ScannerDeviceConfig _config;
        private readonly ScannerService _scannerService;
        private readonly ILogService _logService;

        private DeviceConnectionState _connectionState = DeviceConnectionState.Disconnected;
        private string _lastError;
        private bool _disposed;

        private CancellationTokenSource _continuousScanCts;
        private Task _continuousScanTask;
        private bool _isContinuousScanning;

        #endregion

        #region IDevice 属性

        public string DeviceId => _config.DeviceId;
        public string DeviceName => _config.DeviceName;
        public FrameworkDeviceType DeviceType => FrameworkDeviceType.Scanner;

        public DeviceConnectionState ConnectionState
        {
            get => _connectionState;
            private set
            {
                if (_connectionState != value)
                {
                    var old = _connectionState;
                    _connectionState = value;
                    RaiseConnectionChanged(old, value);
                }
            }
        }

        public bool IsConnected => _scannerService.IsConnected;
        public string LastError => _lastError;

        #endregion

        #region IScannerDevice 属性

        public bool IsScanning => _scannerService.IsScanning;
        public ScanResult LastResult => _scannerService.LastResult;

        #endregion

        #region 事件

        public event EventHandler<DeviceConnectionChangedEventArgs> ConnectionChanged;
        public event EventHandler<DeviceErrorEventArgs> ErrorOccurred;
        public event EventHandler<ScanResult> CodeScanned;

        #endregion

        #region 构造函数

        public ScannerDeviceAdapter(ScannerDeviceConfig config, ILogService logService = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _logService = logService;
            _scannerService = new ScannerService(config);

            SubscribeEvents();
        }

        /// <summary>
        /// 创建小料盘扫码器
        /// </summary>
        public static ScannerDeviceAdapter CreateSmallTray(ILogService logService = null)
        {
            return new ScannerDeviceAdapter(ScannerDeviceConfig.CreateSmallTray(), logService);
        }

        /// <summary>
        /// 创建大料盘扫码器
        /// </summary>
        public static ScannerDeviceAdapter CreateLargeTray(ILogService logService = null)
        {
            return new ScannerDeviceAdapter(ScannerDeviceConfig.CreateLargeTray(), logService);
        }

        #endregion

        #region IDevice 方法

        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            ConnectionState = DeviceConnectionState.Connecting;
            _logService?.Information("[{DeviceName}] 正在连接...", DeviceName);

            var result = await _scannerService.ConnectAsync(ct).ConfigureAwait(false);

            if (result)
            {
                ConnectionState = DeviceConnectionState.Connected;
                _logService?.Information("[{DeviceName}] 连接成功", DeviceName);
            }
            else
            {
                ConnectionState = DeviceConnectionState.Error;
                _lastError = "连接失败";
                _logService?.Warning("[{DeviceName}] 连接失败", DeviceName);
            }

            return result;
        }

        public Task DisconnectAsync()
        {
            _scannerService.Disconnect();
            ConnectionState = DeviceConnectionState.Disconnected;
            _logService?.Information("[{DeviceName}] 已断开连接", DeviceName);
            return Task.CompletedTask;
        }

        public Task<bool> ResetAsync()
        {
            _logService?.Information("[{DeviceName}] 执行复位", DeviceName);
            return Task.FromResult(true);
        }

        public async Task<bool> CheckHealthAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                return await ConnectAsync(ct).ConfigureAwait(false);
            }
            return true;
        }

        #endregion

        #region IScannerDevice 方法

        /// <summary>
        /// 执行扫码（单次）
        /// </summary>
        public Task<ScanResult> ScanAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                return Task.FromResult(ScanResult.Fail("扫码器未连接"));
            }

            return _scannerService.ScanAsync(ct);
        }

        /// <summary>
        /// 执行扫码（带超时）
        /// </summary>
        public Task<ScanResult> ScanAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                return Task.FromResult(ScanResult.Fail("扫码器未连接"));
            }

            return _scannerService.ScanAsync(timeout, ct);
        }

        /// <summary>
        /// 执行扫码（带重试，匹配C++行为）
        /// </summary>
        public Task<ScanResult> ScanWithRetryAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                return Task.FromResult(ScanResult.Fail("扫码器未连接"));
            }

            return _scannerService.ScanWithRetryAsync(
                _config.NoReadRetryCount,
                TimeSpan.FromMilliseconds(_config.NoReadRetryIntervalMs),
                ct);
        }

        /// <summary>
        /// 执行扫码（自定义重试参数）
        /// </summary>
        public Task<ScanResult> ScanWithRetryAsync(int maxRetries, TimeSpan retryInterval, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                return Task.FromResult(ScanResult.Fail("扫码器未连接"));
            }

            return _scannerService.ScanWithRetryAsync(maxRetries, retryInterval, ct);
        }

        /// <summary>
        /// 开始连续扫码
        /// </summary>
        public void StartContinuousScan()
        {
            if (_isContinuousScanning)
                return;

            _isContinuousScanning = true;
            _continuousScanCts = new CancellationTokenSource();

            _continuousScanTask = Task.Run(async () =>
            {
                while (!_continuousScanCts.IsCancellationRequested)
                {
                    try
                    {
                        if (IsConnected)
                        {
                            await _scannerService.ScanAsync(_continuousScanCts.Token).ConfigureAwait(false);
                        }
                        await Task.Delay(100, _continuousScanCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logService?.Error(ex, "[{DeviceName}] 连续扫码异常", DeviceName);
                        await Task.Delay(1000, _continuousScanCts.Token).ConfigureAwait(false);
                    }
                }
            }, _continuousScanCts.Token);

            _logService?.Information("[{DeviceName}] 开始连续扫码", DeviceName);
        }

        /// <summary>
        /// 停止连续扫码
        /// </summary>
        public void StopContinuousScan()
        {
            if (!_isContinuousScanning)
                return;

            try
            {
                _continuousScanCts?.Cancel();
                _continuousScanTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
            finally
            {
                _continuousScanCts?.Dispose();
                _continuousScanCts = null;
                _continuousScanTask = null;
                _isContinuousScanning = false;

                _logService?.Information("[{DeviceName}] 停止连续扫码", DeviceName);
            }
        }

        #endregion

        #region 事件订阅

        private void SubscribeEvents()
        {
            _scannerService.ConnectionChanged += OnServiceConnectionChanged;
            _scannerService.CodeScanned += OnServiceCodeScanned;
            _scannerService.ErrorOccurred += OnServiceErrorOccurred;
        }

        private void UnsubscribeEvents()
        {
            _scannerService.ConnectionChanged -= OnServiceConnectionChanged;
            _scannerService.CodeScanned -= OnServiceCodeScanned;
            _scannerService.ErrorOccurred -= OnServiceErrorOccurred;
        }

        private void OnServiceConnectionChanged(object sender, bool isConnected)
        {
            ConnectionState = isConnected
                ? DeviceConnectionState.Connected
                : DeviceConnectionState.Disconnected;
        }

        private void OnServiceCodeScanned(object sender, ScanResult result)
        {
            CodeScanned?.Invoke(this, result);
        }

        private void OnServiceErrorOccurred(object sender, string error)
        {
            _lastError = error;
            RaiseError(error);
        }

        #endregion

        #region 事件触发

        private void RaiseConnectionChanged(DeviceConnectionState oldState, DeviceConnectionState newState)
        {
            ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs
            {
                DeviceId = DeviceId,
                DeviceName = DeviceName,
                OldState = oldState,
                NewState = newState
            });
        }

        private void RaiseError(string message)
        {
            ErrorOccurred?.Invoke(this, new DeviceErrorEventArgs
            {
                DeviceId = DeviceId,
                ErrorMessage = message
            });

            _logService?.Error("[{DeviceName}] {Error}", DeviceName, message);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            // 停止连续扫码
            StopContinuousScan();

            // 取消订阅事件
            UnsubscribeEvents();

            // 释放服务
            _scannerService?.Dispose();

            _disposed = true;

            _logService?.Information("[{DeviceName}] 扫码器设备已释放", DeviceName);
        }

        #endregion
    }

    #endregion

    #region 扫码器服务工厂

    /// <summary>
    /// 扫码器服务工厂
    /// 用于创建和管理扫码器实例
    /// </summary>
    public class ScannerServiceFactory
    {
        private readonly ILogService _logService;
        private readonly Dictionary<string, ScannerDeviceAdapter> _scanners;

        public ScannerServiceFactory(ILogService logService = null)
        {
            _logService = logService;
            _scanners = new Dictionary<string, ScannerDeviceAdapter>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 获取或创建扫码器
        /// </summary>
        public ScannerDeviceAdapter GetOrCreate(string scannerId)
        {
            if (_scanners.TryGetValue(scannerId, out var existing))
                return existing;

            ScannerDeviceAdapter scanner = null;

            switch (scannerId)
            {
                case "Scanner_Small":
                    scanner = ScannerDeviceAdapter.CreateSmallTray(_logService);
                    break;
                case "Scanner_Large":
                    scanner = ScannerDeviceAdapter.CreateLargeTray(_logService);
                    break;
                default:
                    _logService?.Warning("未知的扫码器ID: {ScannerId}", scannerId);
                    return null;
            }

            if (scanner != null)
            {
                _scanners[scannerId] = scanner;
            }

            return scanner;
        }

        /// <summary>
        /// 获取小料盘扫码器
        /// </summary>
        public ScannerDeviceAdapter GetSmallTrayScanner()
        {
            return GetOrCreate("Scanner_Small");
        }

        /// <summary>
        /// 获取大料盘扫码器
        /// </summary>
        public ScannerDeviceAdapter GetLargeTrayScanner()
        {
            return GetOrCreate("Scanner_Large");
        }

        /// <summary>
        /// 释放所有扫码器
        /// </summary>
        public void DisposeAll()
        {
            foreach (var scanner in _scanners.Values)
            {
                scanner?.Dispose();
            }
            _scanners.Clear();
        }
    }

    #endregion
}