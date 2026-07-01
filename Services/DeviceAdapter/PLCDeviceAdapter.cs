using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Models.PLCModule;
using SeedCut.Services.Connection;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Services.DeviceAdapter
{
    /// <summary>
    /// PLC设备适配器 - 实现 IPlcDevice 接口
    /// 职责：
    /// 1. 实现 IDevice/IPlcDevice 接口，供 DeviceManager 统一管理
    /// 2. 通过 PLCServiceConnectAdapter 实现连接管理
    /// 3. 通过 IPLCService 进行实际PLC操作
    /// </summary>
    public class PLCDeviceAdapter : IPlcDevice
    {
        #region 私有字段

        private readonly PLCServiceConnectAdapter _adapter;
        private readonly IPLCService _plcService;
        private readonly ConnectionManager _connectionManager;
        private readonly ILogService _logService;

        private DeviceConnectionState _connectionState = DeviceConnectionState.Disconnected;
        private string _lastError;
        private bool _disposed;

        // 配置
        private readonly PLCDeviceConfig _deviceConfig;

        #endregion

        #region IDevice 属性

        /// <summary>
        /// 设备唯一标识
        /// </summary>
        public string DeviceId => "PLC";

        /// <summary>
        /// 设备显示名称
        /// </summary>
        public string DeviceName => _adapter.DeviceName;

        /// <summary>
        /// 设备类型
        /// </summary>
        public Framework.Core.DeviceType DeviceType => Framework.Core.DeviceType.PLC;

        /// <summary>
        /// 连接状态
        /// </summary>
        public DeviceConnectionState ConnectionState
        {
            get => _connectionState;
            private set
            {
                if (_connectionState != value)
                {
                    var oldState = _connectionState;
                    _connectionState = value;
                    RaiseConnectionChanged(oldState, value);
                }
            }
        }

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _adapter.IsConnected;

        /// <summary>
        /// 最后错误信息
        /// </summary>
        public string LastError => _lastError;

        #endregion

        #region 扩展属性

        /// <summary>
        /// 是否正在重连
        /// </summary>
        public bool IsReconnecting => _connectionManager?.IsReconnecting ?? false;

        /// <summary>
        /// 当前重试次数
        /// </summary>
        public int CurrentRetryCount => _connectionManager?.CurrentRetryCount ?? 0;

        /// <summary>
        /// PLC配置
        /// </summary>
        public PLCDeviceConfig DeviceConfig => _deviceConfig;

        /// <summary>
        /// 获取底层 Service（用于高级操作或测试）
        /// </summary>
        public IPLCService Service => _plcService;

        /// <summary>
        /// 获取地址注册表
        /// </summary>
        public PLCAddressRegistry AddressRegistry => _plcService.AddressRegistry;

        #endregion

        #region 事件

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event EventHandler<DeviceConnectionChangedEventArgs> ConnectionChanged;

        /// <summary>
        /// 错误发生事件
        /// </summary>
        public event EventHandler<DeviceErrorEventArgs> ErrorOccurred;

        /// <summary>
        /// 重连尝试事件
        /// </summary>
        public event EventHandler<ReconnectionAttemptEventArgs> ReconnectionAttempt;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建PLC设备（带日志服务，支持连接管理）
        /// </summary>
        public PLCDeviceAdapter(
            PLCServiceConnectAdapter adapter,
            IPLCService plcService,
            ILogService logService,
            PLCDeviceConfig config = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _deviceConfig = config ?? new PLCDeviceConfig();

            // 创建连接管理器（内含心跳检测逻辑）
            _connectionManager = new ConnectionManager(_adapter, _logService);

            // 订阅事件
            SubscribeEvents();

            _logService.Information("[{DeviceName}] PLC设备已创建", DeviceName);
        }

        /// <summary>
        /// 创建PLC设备（无日志服务，无连接管理）
        /// 用于简单测试场景
        /// </summary>
        public PLCDeviceAdapter(
            PLCServiceConnectAdapter adapter,
            IPLCService plcService)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
            _logService = null;
            _connectionManager = null;
            _deviceConfig = new PLCDeviceConfig();

            // 订阅基本事件（不包含重连事件）
            SubscribeBasicEvents();
        }

        #endregion

        #region IDevice 方法

        /// <summary>
        /// 异步连接设备
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            try
            {
                ConnectionState = DeviceConnectionState.Connecting;

                bool result;
                if (_connectionManager != null)
                {
                    // 使用连接管理器连接（带心跳支持）
                    result = await _connectionManager.ConnectAsync();
                }
                else
                {
                    // 直接使用适配器连接（无心跳支持）
                    result = await Task.Run(() => _adapter.Connect(), ct);
                }

                ConnectionState = result
                    ? DeviceConnectionState.Connected
                    : DeviceConnectionState.Error;

                return result;
            }
            catch (OperationCanceledException)
            {
                ConnectionState = DeviceConnectionState.Disconnected;
                return false;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                ConnectionState = DeviceConnectionState.Error;
                RaiseError(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 异步断开设备
        /// ✅ 修复：确保即使出错也能更新状态
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                if (_connectionManager != null)
                {
                    // 使用连接管理器断开（会停止心跳）
                    _connectionManager.Disconnect();
                }
                else
                {
                    // 直接使用适配器断开
                    _adapter.Disconnect();
                }

                ConnectionState = DeviceConnectionState.Disconnected;

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _logService?.Error(ex, "[{DeviceName}] 断开连接异常", DeviceName);
                // ✅ 修复：即使出错也要更新状态
                ConnectionState = DeviceConnectionState.Disconnected;
            }
        }

        /// <summary>
        /// 重置设备
        /// </summary>
        public async Task<bool> ResetAsync()
        {
            try
            {
                _lastError = null;
                _logService?.Information("[{DeviceName}] 设备已重置", DeviceName);
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                RaiseError(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 健康检查
        /// </summary>
        public Task<bool> CheckHealthAsync(CancellationToken ct = default)
        {
            var isHealthy = _adapter.CheckConnection();
            return Task.FromResult(isHealthy);
        }

        #endregion

        #region IPlcDevice 方法 - 按地址字符串操作（推荐方式）

        /// <summary>
        /// 解析西门子地址字符串
        /// 支持格式: DB1.DBX0.0, DB1.DBD0, DB1.DBW0, M0.0, MW0, MD0
        /// </summary>
        private (int dbNumber, int startAddress, int bitPosition, PLCAddressType dataType) ParseAddress(string address)
        {
            // DB块位地址: DB1.DBX0.0
            var dbxMatch = Regex.Match(address, @"^DB(\d+)\.DBX(\d+)\.(\d+)$", RegexOptions.IgnoreCase);
            if (dbxMatch.Success)
            {
                return (
                    int.Parse(dbxMatch.Groups[1].Value),
                    int.Parse(dbxMatch.Groups[2].Value),
                    int.Parse(dbxMatch.Groups[3].Value),
                    PLCAddressType.Bit
                );
            }

            // DB块双字地址: DB1.DBD0
            var dbdMatch = Regex.Match(address, @"^DB(\d+)\.DBD(\d+)$", RegexOptions.IgnoreCase);
            if (dbdMatch.Success)
            {
                return (
                    int.Parse(dbdMatch.Groups[1].Value),
                    int.Parse(dbdMatch.Groups[2].Value),
                    0,
                    PLCAddressType.Float
                );
            }

            // DB块字地址: DB1.DBW0
            var dbwMatch = Regex.Match(address, @"^DB(\d+)\.DBW(\d+)$", RegexOptions.IgnoreCase);
            if (dbwMatch.Success)
            {
                return (
                    int.Parse(dbwMatch.Groups[1].Value),
                    int.Parse(dbwMatch.Groups[2].Value),
                    0,
                    PLCAddressType.Int16
                );
            }

            // M区位地址: M0.0
            var mxMatch = Regex.Match(address, @"^M(\d+)\.(\d+)$", RegexOptions.IgnoreCase);
            if (mxMatch.Success)
            {
                return (
                    3, // M区使用DB3
                    int.Parse(mxMatch.Groups[1].Value),
                    int.Parse(mxMatch.Groups[2].Value),
                    PLCAddressType.Bit
                );
            }

            // M区双字地址: MD0
            var mdMatch = Regex.Match(address, @"^MD(\d+)$", RegexOptions.IgnoreCase);
            if (mdMatch.Success)
            {
                return (
                    3,
                    int.Parse(mdMatch.Groups[1].Value),
                    0,
                    PLCAddressType.Float
                );
            }

            // M区字地址: MW0
            var mwMatch = Regex.Match(address, @"^MW(\d+)$", RegexOptions.IgnoreCase);
            if (mwMatch.Success)
            {
                return (
                    3,
                    int.Parse(mwMatch.Groups[1].Value),
                    0,
                    PLCAddressType.Int16
                );
            }

            throw new ArgumentException($"无效的地址格式: {address}");
        }

        // 位操作
        public bool ReadBit(string address)
        {
            var (db, start, bit, _) = ParseAddress(address);
            return ReadBit(db, start, bit);
        }

        public void WriteBit(string address, bool value)
        {
            var (db, start, bit, _) = ParseAddress(address);
            WriteBit(db, start, bit, value);
        }

        public void WriteBitPulse(string address, int durationMs = 50)
        {
            var (db, start, bit, _) = ParseAddress(address);
            PulseBit(db, start, bit, durationMs);
        }

        public async Task<bool> ReadBitAsync(string address, CancellationToken ct = default)
        {
            return await Task.Run(() => ReadBit(address), ct);
        }

        public async Task WriteBitAsync(string address, bool value, CancellationToken ct = default)
        {
            await Task.Run(() => WriteBit(address, value), ct);
        }

        // 整数操作
        public short ReadInt16(string address)
        {
            var (db, start, _, _) = ParseAddress(address);
            return ReadInt16(db, start);
        }

        public void WriteInt16(string address, short value)
        {
            var (db, start, _, _) = ParseAddress(address);
            WriteInt16(db, start, value);
        }

        public int ReadInt32(string address)
        {
            var (db, start, _, _) = ParseAddress(address);
            // 读取4字节作为Int32
            var bytes = ReadBytes(db, start, 4);
            if (bytes == null || bytes.Length < 4) return 0;
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }

        public void WriteInt32(string address, int value)
        {
            var (db, start, _, _) = ParseAddress(address);
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            WriteBytes(db, start, bytes);
        }

        // 浮点数操作
        public float ReadFloat(string address)
        {
            var (db, start, _, _) = ParseAddress(address);
            return ReadFloat(db, start);
        }

        public void WriteFloat(string address, float value)
        {
            var (db, start, _, _) = ParseAddress(address);
            WriteFloat(db, start, value);
        }

        // 字符串操作
        public string ReadString(string address, int length)
        {
            var (db, start, _, _) = ParseAddress(address);
            var bytes = ReadBytes(db, start, length);
            if (bytes == null) return string.Empty;
            return System.Text.Encoding.ASCII.GetString(bytes).TrimEnd('\0');
        }

        public void WriteString(string address, string value, int maxLength)
        {
            var (db, start, _, _) = ParseAddress(address);
            var bytes = new byte[maxLength];
            var strBytes = System.Text.Encoding.ASCII.GetBytes(value ?? string.Empty);
            Array.Copy(strBytes, bytes, Math.Min(strBytes.Length, maxLength));
            WriteBytes(db, start, bytes);
        }

        // 批量操作
        public bool[] ReadBits(string startAddress, int count)
        {
            var (db, start, bit, _) = ParseAddress(startAddress);
            var result = new bool[count];
            for (int i = 0; i < count; i++)
            {
                int currentBit = bit + i;
                int byteOffset = currentBit / 8;
                int bitOffset = currentBit % 8;
                result[i] = ReadBit(db, start + byteOffset, bitOffset);
            }
            return result;
        }

        public short[] ReadInt16Array(string startAddress, int count)
        {
            var (db, start, _, _) = ParseAddress(startAddress);
            var result = new short[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = ReadInt16(db, start + i * 2);
            }
            return result;
        }

        public void WriteInt16Array(string startAddress, short[] values)
        {
            var (db, start, _, _) = ParseAddress(startAddress);
            for (int i = 0; i < values.Length; i++)
            {
                WriteInt16(db, start + i * 2, values[i]);
            }
        }

        #endregion

        #region IPlcDevice 方法 - 按DB/地址/位操作（兼容现有PLCService）

        // 位操作
        public bool ReadBit(int dataBlock, int startAddress, int bitPosition)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return false;
            }

            try
            {
                return _plcService.ReadBit(dataBlock, startAddress, bitPosition);
            }
            catch (Exception ex)
            {
                RaiseError($"读取位失败: {ex.Message}");
                return false;
            }
        }

        public bool WriteBit(int dataBlock, int startAddress, int bitPosition, bool value)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return false;
            }

            try
            {
                return _plcService.WriteBit(dataBlock, startAddress, bitPosition, value);
            }
            catch (Exception ex)
            {
                RaiseError($"写入位失败: {ex.Message}");
                return false;
            }
        }

        public bool PulseBit(int dataBlock, int startAddress, int bitPosition, int delayMs = 20)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return false;
            }

            try
            {
                return _plcService.PulseBit(dataBlock, startAddress, bitPosition, delayMs);
            }
            catch (Exception ex)
            {
                RaiseError($"脉冲触发失败: {ex.Message}");
                return false;
            }
        }

        public bool ToggleBit(int dataBlock, int startAddress, int bitPosition)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return false;
            }

            try
            {
                return _plcService.ToggleBit(dataBlock, startAddress, bitPosition);
            }
            catch (Exception ex)
            {
                RaiseError($"切换位失败: {ex.Message}");
                return false;
            }
        }

        // 数值操作
        public float ReadFloat(int dataBlock, int startAddress)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return 0;
            }

            try
            {
                return _plcService.ReadFloat(dataBlock, startAddress);
            }
            catch (Exception ex)
            {
                RaiseError($"读取浮点数失败: {ex.Message}");
                return 0;
            }
        }

        public bool WriteFloat(int dataBlock, int startAddress, float value)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return false;
            }

            try
            {
                return _plcService.WriteFloat(dataBlock, startAddress, value);
            }
            catch (Exception ex)
            {
                RaiseError($"写入浮点数失败: {ex.Message}");
                return false;
            }
        }

        public short ReadInt16(int dataBlock, int startAddress)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return 0;
            }

            try
            {
                return _plcService.ReadInt16(dataBlock, startAddress);
            }
            catch (Exception ex)
            {
                RaiseError($"读取整数失败: {ex.Message}");
                return 0;
            }
        }

        public bool WriteInt16(int dataBlock, int startAddress, short value)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return false;
            }

            try
            {
                return _plcService.WriteInt16(dataBlock, startAddress, value);
            }
            catch (Exception ex)
            {
                RaiseError($"写入整数失败: {ex.Message}");
                return false;
            }
        }

        // 字节操作
        public byte[] ReadBytes(int dataBlock, int startAddress, int length)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return null;
            }

            try
            {
                return _plcService.ReadBytes(dataBlock, startAddress, length);
            }
            catch (Exception ex)
            {
                RaiseError($"读取字节失败: {ex.Message}");
                return null;
            }
        }

        public bool WriteBytes(int dataBlock, int startAddress, byte[] data)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return false;
            }

            try
            {
                return _plcService.WriteBytes(dataBlock, startAddress, data);
            }
            catch (Exception ex)
            {
                RaiseError($"写入字节失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region IPlcDevice 方法 - 按名称操作（通过AddressRegistry）

        public bool ReadBitByName(string addressName)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return false;
            }

            try
            {
                return _plcService.ReadBitByName(addressName);
            }
            catch (Exception ex)
            {
                RaiseError($"按名称读取位失败: {ex.Message}");
                return false;
            }
        }

        public bool WriteBitByName(string addressName, bool value)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return false;
            }

            try
            {
                return _plcService.WriteBitByName(addressName, value);
            }
            catch (Exception ex)
            {
                RaiseError($"按名称写入位失败: {ex.Message}");
                return false;
            }
        }

        public bool PulseBitByName(string addressName, int delayMs = 20)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return false;
            }

            try
            {
                return _plcService.PulseBitByName(addressName, delayMs);
            }
            catch (Exception ex)
            {
                RaiseError($"按名称脉冲触发失败: {ex.Message}");
                return false;
            }
        }

        public bool ToggleBitByName(string addressName)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return false;
            }

            try
            {
                return _plcService.ToggleBitByName(addressName);
            }
            catch (Exception ex)
            {
                RaiseError($"按名称切换位失败: {ex.Message}");
                return false;
            }
        }

        public float ReadFloatByName(string addressName)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return 0;
            }

            try
            {
                return _plcService.ReadFloatByName(addressName);
            }
            catch (Exception ex)
            {
                RaiseError($"按名称读取浮点数失败: {ex.Message}");
                return 0;
            }
        }

        public bool WriteFloatByName(string addressName, float value)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return false;
            }

            try
            {
                return _plcService.WriteFloatByName(addressName, value);
            }
            catch (Exception ex)
            {
                RaiseError($"按名称写入浮点数失败: {ex.Message}");
                return false;
            }
        }

        public short ReadInt16ByName(string addressName)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return 0;
            }

            try
            {
                return _plcService.ReadInt16ByName(addressName);
            }
            catch (Exception ex)
            {
                RaiseError($"按名称读取整数失败: {ex.Message}");
                return 0;
            }
        }

        public bool WriteInt16ByName(string addressName, short value)
        {
            if (!IsConnected)
            {
                RaiseError("PLC未连接");
                return false;
            }

            try
            {
                return _plcService.WriteInt16ByName(addressName, value);
            }
            catch (Exception ex)
            {
                RaiseError($"按名称写入整数失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 事件订阅

        private void SubscribeEvents()
        {
            // 订阅基本事件
            SubscribeBasicEvents();

            // 订阅连接管理器事件
            if (_connectionManager != null)
            {
                _connectionManager.ConnectionStateChanged += OnConnectionManagerStateChanged;
                _connectionManager.ReconnectionAttempt += OnReconnectionAttempt;
            }
        }

        private void SubscribeBasicEvents()
        {
            // 订阅适配器连接状态事件
            _adapter.ConnectionStateChanged += OnAdapterConnectionStateChanged;

            // 订阅PLC服务事件
            _plcService.ConnectionStateChanged += OnPLCServiceConnectionChanged;
        }

        private void UnsubscribeEvents()
        {
            // 取消订阅适配器事件
            _adapter.ConnectionStateChanged -= OnAdapterConnectionStateChanged;

            // 取消订阅PLC服务事件
            _plcService.ConnectionStateChanged -= OnPLCServiceConnectionChanged;

            // 取消订阅连接管理器事件
            if (_connectionManager != null)
            {
                _connectionManager.ConnectionStateChanged -= OnConnectionManagerStateChanged;
                _connectionManager.ReconnectionAttempt -= OnReconnectionAttempt;
            }
        }

        #endregion

        #region 事件处理

        private void OnAdapterConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            // 仅当没有连接管理器时，直接响应适配器状态变化
            if (_connectionManager == null)
            {
                ConnectionState = e.IsConnected
                    ? DeviceConnectionState.Connected
                    : DeviceConnectionState.Disconnected;
            }
        }

        private void OnConnectionManagerStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            // 通过连接管理器更新状态
            if (e.IsConnected)
            {
                ConnectionState = DeviceConnectionState.Connected;
            }
            else if (_connectionManager.IsReconnecting)
            {
                ConnectionState = DeviceConnectionState.Reconnecting;
            }
            else
            {
                ConnectionState = DeviceConnectionState.Disconnected;
            }
        }

        private void OnReconnectionAttempt(object sender, ReconnectionAttemptEventArgs e)
        {
            // 更新状态
            if (!e.Success && !e.IsMaxRetryReached)
            {
                ConnectionState = DeviceConnectionState.Reconnecting;
            }
            else if (e.IsMaxRetryReached)
            {
                ConnectionState = DeviceConnectionState.Error;
                _lastError = "达到最大重连次数";
            }

            // 转发事件
            ReconnectionAttempt?.Invoke(this, e);
        }

        private void OnPLCServiceConnectionChanged(object sender, PLCConnectionStateChangedEventArgs e)
        {
            if (_connectionManager == null)
            {
                ConnectionState = e.IsConnected
                    ? DeviceConnectionState.Connected
                    : DeviceConnectionState.Disconnected;
            }
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
            _lastError = message;
            ErrorOccurred?.Invoke(this, new DeviceErrorEventArgs
            {
                DeviceId = DeviceId,
                DeviceName = DeviceName,
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

            // 取消订阅事件
            UnsubscribeEvents();

            // 释放连接管理器
            _connectionManager?.Dispose();

            // 注意：不要释放 _adapter 和 _plcService
            // 它们的生命周期由工厂管理

            _disposed = true;

            _logService?.Information("[{DeviceName}] PLC设备已释放", DeviceName);
        }

        #endregion
    }

    #region 配置类

    /// <summary>
    /// PLC设备配置
    /// </summary>
    public class PLCDeviceConfig
    {
        /// <summary>
        /// 是否启用安全检查
        /// </summary>
        public bool EnableSafetyCheck { get; set; } = true;

        /// <summary>
        /// 连接超时（毫秒）
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 读写超时（毫秒）
        /// </summary>
        public int ReadWriteTimeoutMs { get; set; } = 500;

        /// <summary>
        /// 是否启用缓存
        /// </summary>
        public bool EnableCache { get; set; } = true;

        /// <summary>
        /// 轮询间隔（毫秒）
        /// </summary>
        public int PollingIntervalMs { get; set; } = 500;
    }

    #endregion
}