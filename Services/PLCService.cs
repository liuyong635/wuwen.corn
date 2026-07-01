using S7.Net;
using SeedCut.Models.PLCModule;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SeedCut.Services
{
    /// <summary>
    /// ✅ 增强版 PLCService - 支持I/Q/M/DB四种内存区域
    /// 
    /// 核心改动：
    /// 1. 支持Input(I)、Output(Q)、Memory(M)、DataBlock(DB)四种区域
    /// 2. 根据PLCAddressItem.MemoryArea自动选择正确的S7.Net DataType
    /// 3. 保持向后兼容（无MemoryArea属性时默认为M区）
    /// </summary>
    public class PLCService : IPLCService
    {
        private Plc _plc;
        private readonly PLCConnectionConfig _connectionConfig;
        private readonly PLCAddressRegistry _addressRegistry;

        // 缓存相关
        private readonly ConcurrentDictionary<string, CachedValue> _cache;
        private Thread _pollingThread;
        private bool _isPolling;
        private List<AddressBlock> _optimizedBlocks;
        private int _pollingIntervalMs = 500;
        private bool _enableCache = true;

        public event EventHandler<PLCConnectionStateChangedEventArgs> ConnectionStateChanged;
        public PLCConnectionConfig ConnectionConfig => _connectionConfig;
        public PLCAddressRegistry AddressRegistry => _addressRegistry;
        public bool IsConnected => _plc?.IsConnected ?? false;

        public bool EnableCache
        {
            get { return _enableCache; }
            set
            {
                _enableCache = value;
                if (!value)
                {
                    _cache.Clear();
                    System.Diagnostics.Debug.WriteLine("⚠️ 缓存已禁用，降级到原始模式");
                }
            }
        }

        protected virtual void OnConnectionStateChanged(bool isConnected, string message = null)
        {
            try
            {
                ConnectionStateChanged?.Invoke(this,
                    new PLCConnectionStateChangedEventArgs(isConnected, message));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 触发连接状态事件异常: {ex.Message}");
            }
        }

        public int PollingIntervalMs
        {
            get { return _pollingIntervalMs; }
            set
            {
                if (value >= 50 && value <= 5000)
                {
                    _pollingIntervalMs = value;
                    System.Diagnostics.Debug.WriteLine($"⏱️ 轮询间隔设置为: {value}ms");
                }
            }
        }

        public PLCService(PLCConnectionConfig connectionConfig, PLCAddressRegistry addressRegistry)
        {
            _connectionConfig = connectionConfig ?? throw new ArgumentNullException(nameof(connectionConfig));
            _addressRegistry = addressRegistry ?? throw new ArgumentNullException(nameof(addressRegistry));
            _cache = new ConcurrentDictionary<string, CachedValue>();
        }

        /// <summary>
        /// ✅ 优化版：心跳专用的直接写入方法（不需要先读取）
        /// </summary>
        public bool WriteHeartbitDirect(int dataBlock, int startAddress, int bitPosition, bool value)
        {
            try
            {
                if (!IsConnected) return false;

                // ✅ 构造位地址字符串
                var memoryArea = dataBlock == 3 ? PLCMemoryArea.Marker : PLCMemoryArea.DataBlock;
                string s7Address = BuildS7BitAddress(memoryArea, dataBlock, startAddress, bitPosition);

                // ✅ 直接写单个位，不影响同字节的其他位
                _plc.Write(s7Address, value);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"心跳写入失败: {ex.Message}");
                return false;
            }
        }
        public PLCService(PLCAddressRegistry addressRegistry)
            : this(new PLCConnectionConfig(), addressRegistry)
        {
        }

        #region 连接管理

        public bool Connect()
        {
            try
            {
                if (_plc != null && _plc.IsConnected)
                    return true;

                CpuType cpuType = MapPLCType(_connectionConfig.PLCType);
                _plc = new Plc(cpuType, _connectionConfig.IPAddress,
                    (short)_connectionConfig.Rack, (short)_connectionConfig.Slot);

                _plc.ReadTimeout = 500;
                _plc.WriteTimeout = 500;

                // ✅ 使用带超时的连接方式
                var connectTask = System.Threading.Tasks.Task.Run(() => _plc.Open());
                if (!connectTask.Wait(1500))  // 3秒超时
                {
                    System.Diagnostics.Debug.WriteLine("PLC连接超时 (3秒)");
                    try { _plc.Close(); } catch { }
                    _plc = null;
                    return false;
                }

                if (_plc.IsConnected && _enableCache)
                {
                    _optimizedBlocks = null;
                    StartPolling();
                    OnConnectionStateChanged(true, "PLC连接成功");
                }
                TestIORead();
                return _plc.IsConnected;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PLC连接失败: {ex.Message}");
                return false;
            }
        }
        /// <summary>
        /// 测试 I/Q 区读取
        /// </summary>
        public void TestIORead()
        {
            if (!IsConnected)
            {
                System.Diagnostics.Debug.WriteLine("❌ PLC 未连接");
                return;
            }

            try
            {
                // 测试读取 Q3 字节
                System.Diagnostics.Debug.WriteLine("===== 测试 Q 区读取 =====");
                var qBytes = _plc.ReadBytes(DataType.Output, 0, 3, 4);  // 读取 Q3-Q6
                if (qBytes != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Q3-Q6 原始数据: {BitConverter.ToString(qBytes)}");
                    for (int i = 0; i < qBytes.Length; i++)
                    {
                        System.Diagnostics.Debug.WriteLine($"  Q{3 + i} = {qBytes[i]:X2} ({Convert.ToString(qBytes[i], 2).PadLeft(8, '0')})");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ Q 区读取返回 null");
                }

                // 测试读取 I 区
                System.Diagnostics.Debug.WriteLine("===== 测试 I 区读取 =====");
                var iBytes = _plc.ReadBytes(DataType.Input, 0, 0, 4);  // 读取 I0-I3
                if (iBytes != null)
                {
                    System.Diagnostics.Debug.WriteLine($"I0-I3 原始数据: {BitConverter.ToString(iBytes)}");
                    for (int i = 0; i < iBytes.Length; i++)
                    {
                        System.Diagnostics.Debug.WriteLine($"  I{i} = {iBytes[i]:X2} ({Convert.ToString(iBytes[i], 2).PadLeft(8, '0')})");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ I 区读取返回 null");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 异常: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   {ex.StackTrace}");
            }
        }
        public void Disconnect()
        {
            try
            {
                StopPolling();

                if (_plc != null && _plc.IsConnected)
                {
                    _plc.Close();
                    OnConnectionStateChanged(false, "PLC已断开");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PLC断开失败: {ex.Message}");
            }
        }

        private CpuType MapPLCType(S7PLCType plcType)
        {
            switch (plcType)
            {
                case S7PLCType.S7200:
                    return CpuType.S7200;
                case S7PLCType.S7300:
                    return CpuType.S7300;
                case S7PLCType.S7400:
                    return CpuType.S7400;
                case S7PLCType.S71200:
                    return CpuType.S71200;
                case S7PLCType.S71500:
                    return CpuType.S71500;
                default:
                    return CpuType.S7300;
            }
        }

        #endregion

        #region ✅ 核心：内存区域映射

        /// <summary>
        /// ✅ 将PLCMemoryArea转换为S7.Net的DataType
        /// </summary>
        private DataType MapMemoryAreaToDataType(PLCMemoryArea memoryArea)
        {
            switch (memoryArea)
            {
                case PLCMemoryArea.Input:
                    return DataType.Input;
                case PLCMemoryArea.Output:
                    return DataType.Output;
                case PLCMemoryArea.DataBlock:
                    return DataType.DataBlock;
                case PLCMemoryArea.Marker:
                default:
                    return DataType.Memory;
            }
        }

        /// <summary>
        /// ✅ 获取缓存键（包含内存区域信息）
        /// </summary>
        private string GetCacheKey(PLCMemoryArea memoryArea, int dbNumber, int startAddress, int bitPosition)
        {
            return $"{memoryArea}:{dbNumber}:{startAddress}:{bitPosition}";
        }

        /// <summary>
        /// 旧版缓存键（向后兼容）
        /// </summary>
        private string GetCacheKey(int dataBlock, int startAddress, int bitPosition)
        {
            // 向后兼容：dataBlock=3 视为M区
            var area = dataBlock == 3 ? PLCMemoryArea.Marker : PLCMemoryArea.DataBlock;
            return GetCacheKey(area, dataBlock, startAddress, bitPosition);
        }

        #endregion

        #region 后台轮询线程

        private void StartPolling()
        {
            if (_isPolling)
                return;

            _isPolling = true;
            _pollingThread = new Thread(PollingThreadProc)
            {
                IsBackground = true,
                Name = "PLC-Polling-Thread"
            };
            _pollingThread.Start();

            System.Diagnostics.Debug.WriteLine("✅ 后台轮询线程已启动");
        }

        private void StopPolling()
        {
            if (!_isPolling)
                return;

            _isPolling = false;

            if (_pollingThread != null && _pollingThread.IsAlive)
            {
                _pollingThread.Join(1000);
            }

            _cache.Clear();
            _optimizedBlocks = null;
            System.Diagnostics.Debug.WriteLine("⏹️ 后台轮询线程已停止");
        }

        private void PollingThreadProc()
        {
            System.Diagnostics.Debug.WriteLine("▶️ 轮询线程开始运行");

            while (_isPolling)
            {
                try
                {
                    if (!IsConnected)
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ 检测到断线，停止后台轮询");
                        _isPolling = false;
                        _cache.Clear();
                        OnConnectionStateChanged(false, "PLC轮询检测到断线");
                        break;
                    }

                    BatchReadAllBlocks();
                    Thread.Sleep(_pollingIntervalMs);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 轮询线程异常: {ex.Message}");

                    if (!IsConnected)
                    {
                        System.Diagnostics.Debug.WriteLine("⚠️ 异常后检测到断线，停止轮询");
                        _isPolling = false;
                        _cache.Clear();
                        OnConnectionStateChanged(false, "PLC轮询异常断线");
                        break;
                    }

                    Thread.Sleep(1000);
                }
            }

            System.Diagnostics.Debug.WriteLine("⏹️ 轮询线程已退出");
        }

        private int GetAddressEnd(PLCAddressItem addr)
        {
            switch (addr.DataType)
            {
                case PLCAddressType.Bit:
                case PLCAddressType.Byte:
                    return addr.StartAddress;
                case PLCAddressType.Int16:
                    return addr.StartAddress + 1;
                case PLCAddressType.Int32:
                case PLCAddressType.Float:
                    return addr.StartAddress + 3;
                default:
                    return addr.StartAddress;
            }
        }

        /// <summary>
        /// ✅ 修改：根据MemoryArea分组构建地址块
        /// </summary>
        private List<AddressBlock> BuildOptimalBlocks()
        {
            var blocks = new List<AddressBlock>();

            // ✅ 修复：I/Q/M 区忽略 DBNumber 进行分组
            var addressGroups = _addressRegistry.Addresses
                .Where(a => a.Readable)
                .Where(a => !(a.MemoryArea == PLCMemoryArea.Marker && a.StartAddress == 1003))
                .GroupBy(a => new
                {
                    a.MemoryArea,
                    DBNumber = (a.MemoryArea == PLCMemoryArea.DataBlock) ? a.DBNumber : 0
                })
                .ToList();

            foreach (var group in addressGroups)
            {
                var memoryArea = group.Key.MemoryArea;
                var dbNumber = group.Key.DBNumber;

                var addresses = group.OrderBy(a => a.StartAddress).ToList();

                if (addresses.Count == 0) continue;

                var mergedBlocks = MergeAddressRanges(addresses, memoryArea, dbNumber);
                blocks.AddRange(mergedBlocks);
            }

            // 打印优化结果
            int totalBytes = blocks.Sum(b => b.Length);
            System.Diagnostics.Debug.WriteLine($"✅ 动态生成 {blocks.Count} 个读取块，总计 {totalBytes} 字节");
            foreach (var b in blocks)
            {
                var areaName = GetAreaDisplayName(b.MemoryArea, b.DBNumber);
                System.Diagnostics.Debug.WriteLine($"   [{areaName}] {b.StartAddress} ~ {b.StartAddress + b.Length - 1}");
            }

            return blocks;
        }

        private string GetAreaDisplayName(PLCMemoryArea area, int dbNumber)
        {
            switch (area)
            {
                case PLCMemoryArea.Input: return "I";
                case PLCMemoryArea.Output: return "Q";
                case PLCMemoryArea.DataBlock: return $"DB{dbNumber}";
                default: return "M";
            }
        }

        private List<AddressBlock> MergeAddressRanges(List<PLCAddressItem> addresses, PLCMemoryArea memoryArea, int dbNumber)
        {
            var blocks = new List<AddressBlock>();

            if (addresses.Count == 0) return blocks;

            const int MAX_GAP = 50;
            const int MAX_BLOCK_SIZE = 200;
            const int PADDING = 4;

            int blockStart = addresses[0].StartAddress;
            int blockEnd = GetAddressEnd(addresses[0]);

            for (int i = 1; i < addresses.Count; i++)
            {
                var addr = addresses[i];
                int addrEnd = GetAddressEnd(addr);

                int gap = addr.StartAddress - blockEnd;
                int newBlockSize = addrEnd - blockStart + PADDING;

                if (gap <= MAX_GAP && newBlockSize <= MAX_BLOCK_SIZE)
                {
                    blockEnd = Math.Max(blockEnd, addrEnd);
                }
                else
                {
                    blocks.Add(new AddressBlock
                    {
                        MemoryArea = memoryArea,
                        DBNumber = dbNumber,
                        StartAddress = blockStart,
                        Length = blockEnd - blockStart + PADDING
                    });

                    blockStart = addr.StartAddress;
                    blockEnd = addrEnd;
                }
            }

            blocks.Add(new AddressBlock
            {
                MemoryArea = memoryArea,
                DBNumber = dbNumber,
                StartAddress = blockStart,
                Length = blockEnd - blockStart + PADDING
            });

            return blocks;
        }

        private void BatchReadAllBlocks()
        {
            if (_optimizedBlocks == null || _optimizedBlocks.Count == 0)
            {
                _optimizedBlocks = BuildOptimalBlocks();

                if (_optimizedBlocks.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ 没有需要轮询的地址");
                    return;
                }
            }

            foreach (var block in _optimizedBlocks)
            {
                try
                {
                    BatchReadBlock(block);
                }
                catch (Exception ex)
                {
                    var areaName = GetAreaDisplayName(block.MemoryArea, block.DBNumber);
                    System.Diagnostics.Debug.WriteLine($"⚠️ 读取块失败 [{areaName}.{block.StartAddress}]: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// ✅ 修改：批量读取支持不同内存区域
        /// </summary>
        private void BatchReadBlock(AddressBlock block)
        {
            byte[] buffer = ReadBytesInternal(block.MemoryArea, block.DBNumber, block.StartAddress, block.Length);

            if (buffer == null || buffer.Length < block.Length)
                return;

            long timestamp = DateTime.Now.Ticks;

            foreach (var addr in _addressRegistry.Addresses)
            {
                // ✅ 检查内存区域和地址范围
                if (addr.MemoryArea != block.MemoryArea)
                    continue;

                // ✅ 修复：只有 DB 区才检查 DBNumber
                if (addr.MemoryArea == PLCMemoryArea.DataBlock && addr.DBNumber != block.DBNumber)
                    continue;

                if (addr.StartAddress < block.StartAddress ||
                    addr.StartAddress >= block.StartAddress + block.Length)
                    continue;

                int offset = addr.StartAddress - block.StartAddress;

                try
                {
                    object value = null;

                    switch (addr.DataType)
                    {
                        case PLCAddressType.Bit:
                            if (offset < buffer.Length)
                            {
                                value = (buffer[offset] & (1 << addr.BitPosition)) != 0;
                            }
                            break;

                        case PLCAddressType.Byte:
                            if (offset < buffer.Length)
                            {
                                value = buffer[offset];
                            }
                            break;

                        case PLCAddressType.Int16:
                            if (offset + 1 < buffer.Length)
                            {
                                byte[] temp = new byte[2];
                                Array.Copy(buffer, offset, temp, 0, 2);
                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(temp);
                                value = BitConverter.ToInt16(temp, 0);
                            }
                            break;

                        case PLCAddressType.Int32:
                            if (offset + 3 < buffer.Length)
                            {
                                byte[] temp = new byte[4];
                                Array.Copy(buffer, offset, temp, 0, 4);
                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(temp);
                                value = BitConverter.ToInt32(temp, 0);
                            }
                            break;

                        case PLCAddressType.Float:
                            if (offset + 3 < buffer.Length)
                            {
                                byte[] temp = new byte[4];
                                Array.Copy(buffer, offset, temp, 0, 4);
                                if (BitConverter.IsLittleEndian)
                                    Array.Reverse(temp);
                                value = BitConverter.ToSingle(temp, 0);
                            }
                            break;
                    }

                    if (value != null)
                    {
                        string cacheKey = GetCacheKey(addr.MemoryArea, addr.DBNumber, addr.StartAddress, addr.BitPosition);
                        _cache[cacheKey] = new CachedValue { Value = value, Timestamp = timestamp };
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 解析地址 {addr.Name} 失败: {ex.Message}");
                }
            }
        }

        #endregion

        #region 缓存辅助方法

        private bool TryGetFromCache(string cacheKey, out object value)
        {
            value = null;

            if (!_enableCache)
                return false;

            CachedValue cached;
            if (_cache.TryGetValue(cacheKey, out cached))
            {
                // 缓存有效期检查（可选，这里设置5秒）
                long now = DateTime.Now.Ticks;
                long age = (now - cached.Timestamp) / TimeSpan.TicksPerMillisecond;

                if (age < 5000)  // 5秒内有效
                {
                    value = cached.Value;
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region ✅ 核心读写方法（支持内存区域）

        /// <summary>
        /// ✅ 内部方法：根据内存区域读取字节
        /// </summary>
        private byte[] ReadBytesInternal(PLCMemoryArea memoryArea, int dbNumber, int startAddress, int length)
        {
            try
            {
                if (!IsConnected) return null;

                var dataType = MapMemoryAreaToDataType(memoryArea);



                // ✅ 关键修改：I/Q/M区dbNumber必须为0，只有DB区才用实际的dbNumber
                int actualDbNumber = (memoryArea == PLCMemoryArea.DataBlock) ? dbNumber : 0;

                return _plc.ReadBytes(dataType, actualDbNumber, startAddress, length);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取字节失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ✅ 内部方法：根据内存区域写入字节
        /// </summary>
        private bool WriteBytesInternal(PLCMemoryArea memoryArea, int dbNumber, int startAddress, byte[] data)
        {
            try
            {
                if (!IsConnected) return false;

                var dataType = MapMemoryAreaToDataType(memoryArea);
                // ✅ 加这一行
                int actualDbNumber = (memoryArea == PLCMemoryArea.DataBlock) ? dbNumber : 0;

                _plc.WriteBytes(dataType, actualDbNumber, startAddress, data);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"写入字节失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 旧版ReadBytes（向后兼容，dataBlock=3视为M区）
        /// </summary>
        public byte[] ReadBytes(int dataBlock, int startAddress, int length)
        {
            var memoryArea = dataBlock == 3 ? PLCMemoryArea.Marker : PLCMemoryArea.DataBlock;
            return ReadBytesInternal(memoryArea, dataBlock, startAddress, length);
        }

        /// <summary>
        /// 旧版WriteBytes（向后兼容）
        /// </summary>
        public bool WriteBytes(int dataBlock, int startAddress, byte[] data)
        {
            var memoryArea = dataBlock == 3 ? PLCMemoryArea.Marker : PLCMemoryArea.DataBlock;
            bool success = WriteBytesInternal(memoryArea, dataBlock, startAddress, data);

            if (success && _enableCache)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    for (int bit = 0; bit < 8; bit++)
                    {
                        string key = GetCacheKey(memoryArea, dataBlock, startAddress + i, bit);
                        CachedValue dummy;
                        _cache.TryRemove(key, out dummy);
                    }
                }
            }

            return success;
        }

        public bool ReadBit(int dataBlock, int startAddress, int bitPosition)
        {
            try
            {
                var memoryArea = dataBlock == 3 ? PLCMemoryArea.Marker : PLCMemoryArea.DataBlock;
                string cacheKey = GetCacheKey(memoryArea, dataBlock, startAddress, bitPosition);
                object cachedValue;

                if (TryGetFromCache(cacheKey, out cachedValue) && cachedValue is bool)
                {
                    return (bool)cachedValue;
                }

                if (!IsConnected) return false;
                byte[] data = ReadBytes(dataBlock, startAddress, 1);
                if (data == null || data.Length == 0) return false;

                bool result = (data[0] & (1 << bitPosition)) != 0;

                if (_enableCache)
                {
                    _cache[cacheKey] = new CachedValue { Value = result, Timestamp = DateTime.Now.Ticks };
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取位失败: {ex.Message}");
                return false;
            }
        }

        public bool WriteBit(int dataBlock, int startAddress, int bitPosition, bool value)
        {
            try
            {
                if (!IsConnected) return false;

                var memoryArea = dataBlock == 3 ? PLCMemoryArea.Marker : PLCMemoryArea.DataBlock;
                int actualDbNumber = (memoryArea == PLCMemoryArea.DataBlock) ? dataBlock : 0;

                // 构造 S7 位地址
                string address = (memoryArea == PLCMemoryArea.Marker)
                    ? $"M{startAddress}.{bitPosition}"
                    : $"DB{actualDbNumber}.DBX{startAddress}.{bitPosition}";

                _plc.Write(address, value);

                // 更新缓存
                if (_enableCache)
                {
                    string cacheKey = GetCacheKey(memoryArea, dataBlock, startAddress, bitPosition);
                    _cache[cacheKey] = new CachedValue { Value = value, Timestamp = DateTime.Now.Ticks };
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"写入位失败: {ex.Message}");
                return false;
            }
        }

        public float ReadFloat(int dataBlock, int startAddress)
        {
            try
            {
                var memoryArea = dataBlock == 3 ? PLCMemoryArea.Marker : PLCMemoryArea.DataBlock;
                string cacheKey = GetCacheKey(memoryArea, dataBlock, startAddress, -1);
                object cachedValue;

                if (TryGetFromCache(cacheKey, out cachedValue) && cachedValue is float)
                {
                    return (float)cachedValue;
                }

                if (!IsConnected) return 0;
                byte[] data = ReadBytes(dataBlock, startAddress, 4);
                if (data == null || data.Length < 4) return 0;

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(data);
                }

                float result = BitConverter.ToSingle(data, 0);

                if (_enableCache)
                {
                    _cache[cacheKey] = new CachedValue { Value = result, Timestamp = DateTime.Now.Ticks };
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取浮点数失败: {ex.Message}");
                return 0;
            }
        }

        public bool WriteFloat(int dataBlock, int startAddress, float value)
        {
            try
            {
                if (!IsConnected) return false;
                byte[] data = BitConverter.GetBytes(value);

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(data);
                }

                bool success = WriteBytes(dataBlock, startAddress, data);

                if (success && _enableCache)
                {
                    var memoryArea = dataBlock == 3 ? PLCMemoryArea.Marker : PLCMemoryArea.DataBlock;
                    string cacheKey = GetCacheKey(memoryArea, dataBlock, startAddress, -1);
                    _cache[cacheKey] = new CachedValue { Value = value, Timestamp = DateTime.Now.Ticks };
                }

                return success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"写入浮点数失败: {ex.Message}");
                return false;
            }
        }

        public short ReadInt16(int dataBlock, int startAddress)
        {
            try
            {
                var memoryArea = dataBlock == 3 ? PLCMemoryArea.Marker : PLCMemoryArea.DataBlock;
                string cacheKey = GetCacheKey(memoryArea, dataBlock, startAddress, -1);
                object cachedValue;

                if (TryGetFromCache(cacheKey, out cachedValue) && cachedValue is short)
                {
                    return (short)cachedValue;
                }

                if (!IsConnected) return 0;
                byte[] data = ReadBytes(dataBlock, startAddress, 2);
                if (data == null || data.Length < 2) return 0;

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(data);
                }

                short result = BitConverter.ToInt16(data, 0);

                if (_enableCache)
                {
                    _cache[cacheKey] = new CachedValue { Value = result, Timestamp = DateTime.Now.Ticks };
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取整数失败: {ex.Message}");
                return 0;
            }
        }

        public int ReadInt32(int dataBlock, int startAddress)
        {
            try
            {
                var memoryArea = dataBlock == 3 ? PLCMemoryArea.Marker : PLCMemoryArea.DataBlock;
                string cacheKey = GetCacheKey(memoryArea, dataBlock, startAddress, -1);
                object cachedValue;

                if (TryGetFromCache(cacheKey, out cachedValue) && cachedValue is short)
                {
                    return (short)cachedValue;
                }

                if (!IsConnected) return 0;
                byte[] data = ReadBytes(dataBlock, startAddress, 4);
                if (data == null || data.Length < 4) return 0;

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(data);
                }

                int result = BitConverter.ToInt32(data, 0);

                if (_enableCache)
                {
                    _cache[cacheKey] = new CachedValue { Value = result, Timestamp = DateTime.Now.Ticks };
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取整数失败: {ex.Message}");
                return 0;
            }
        }

        public bool WriteInt16(int dataBlock, int startAddress, short value)
        {
            try
            {
                if (!IsConnected) return false;
                byte[] data = BitConverter.GetBytes(value);

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(data);
                }

                bool success = WriteBytes(dataBlock, startAddress, data);

                if (success && _enableCache)
                {
                    var memoryArea = dataBlock == 3 ? PLCMemoryArea.Marker : PLCMemoryArea.DataBlock;
                    string cacheKey = GetCacheKey(memoryArea, dataBlock, startAddress, -1);
                    _cache[cacheKey] = new CachedValue { Value = value, Timestamp = DateTime.Now.Ticks };
                }

                return success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"写入整数失败: {ex.Message}");
                return false;
            }
        }


        public bool WriteInt32(int dataBlock, int startAddress, int value)
        {
            try
            {
                if (!IsConnected) return false;
                byte[] data = BitConverter.GetBytes(value);

                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(data);
                }

                bool success = WriteBytes(dataBlock, startAddress, data);

                if (success && _enableCache)
                {
                    var memoryArea = dataBlock == 3 ? PLCMemoryArea.Marker : PLCMemoryArea.DataBlock;
                    string cacheKey = GetCacheKey(memoryArea, dataBlock, startAddress, -1);
                    _cache[cacheKey] = new CachedValue { Value = value, Timestamp = DateTime.Now.Ticks };
                }

                return success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"写入整数失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region ✅ 新增：按PLCAddressItem读写（推荐使用）

        /// <summary>
        /// ✅ 新增：根据地址项读取位（自动处理内存区域）
        /// </summary>
        public bool ReadBit(PLCAddressItem address)
        {
            if (address == null || address.DataType != PLCAddressType.Bit)
                return false;

            try
            {
                string cacheKey = GetCacheKey(address.MemoryArea, address.DBNumber, address.StartAddress, address.BitPosition);
                object cachedValue;

                if (TryGetFromCache(cacheKey, out cachedValue) && cachedValue is bool)
                {
                    return (bool)cachedValue;
                }

                if (!IsConnected) return false;

                byte[] data = ReadBytesInternal(address.MemoryArea, address.DBNumber, address.StartAddress, 1);
                if (data == null || data.Length == 0) return false;

                bool result = (data[0] & (1 << address.BitPosition)) != 0;

                if (_enableCache)
                {
                    _cache[cacheKey] = new CachedValue { Value = result, Timestamp = DateTime.Now.Ticks };
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取位失败 [{address.Name}]: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ✅ 新增：根据地址项写入位（自动处理内存区域）
        /// </summary>
        public bool WriteBit(PLCAddressItem address, bool value)
        {
            if (address == null || address.DataType != PLCAddressType.Bit)
                return false;

            try
            {
                if (!IsConnected) return false;

                // 构造 S7 位地址
                string s7Address = BuildS7BitAddress(address.MemoryArea, address.DBNumber,
                                                      address.StartAddress, address.BitPosition);
                _plc.Write(s7Address, value);

                // 更新缓存
                if (_enableCache)
                {
                    string cacheKey = GetCacheKey(address.MemoryArea, address.DBNumber,
                                                  address.StartAddress, address.BitPosition);
                    _cache[cacheKey] = new CachedValue { Value = value, Timestamp = DateTime.Now.Ticks };
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"写入位失败 [{address.Name}]: {ex.Message}");
                return false;
            }
        }
        /// <summary>
        /// 构造 S7 位地址字符串
        /// </summary>
        private string BuildS7BitAddress(PLCMemoryArea memoryArea, int dbNumber, int byteAddr, int bitPos)
        {
            switch (memoryArea)
            {
                case PLCMemoryArea.Input:
                    return $"I{byteAddr}.{bitPos}";
                case PLCMemoryArea.Output:
                    return $"Q{byteAddr}.{bitPos}";
                case PLCMemoryArea.Marker:
                    return $"M{byteAddr}.{bitPos}";
                case PLCMemoryArea.DataBlock:
                    return $"DB{dbNumber}.DBX{byteAddr}.{bitPos}";
                default:
                    return $"M{byteAddr}.{bitPos}";
            }
        }
        #endregion

        #region 高级方法

        public bool PulseBit(int dataBlock, int startAddress, int bitPosition, int delayMs = 20)
        {
            try
            {
                if (!WriteBit(dataBlock, startAddress, bitPosition, true))
                    return false;

                Thread.Sleep(delayMs);

                return WriteBit(dataBlock, startAddress, bitPosition, false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"脉冲触发失败: {ex.Message}");
                return false;
            }
        }

        public bool ToggleBit(int dataBlock, int startAddress, int bitPosition)
        {
            try
            {
                bool current = ReadBit(dataBlock, startAddress, bitPosition);
                return WriteBit(dataBlock, startAddress, bitPosition, !current);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"切换位失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region ✅ 修改：按名称读写（使用新的内存区域支持）

        public bool WriteBitByName(string addressName, bool value)
        {
            var addr = _addressRegistry.GetAddress(addressName);
            if (addr == null || addr.DataType != PLCAddressType.Bit)
            {
                System.Diagnostics.Debug.WriteLine($"地址 {addressName} 不存在或类型不匹配");
                return false;
            }
            return WriteBit(addr, value);  // ✅ 使用新方法
        }

        public bool PulseBitByName(string addressName, int delayMs = 20)
        {
            var addr = _addressRegistry.GetAddress(addressName);
            if (addr == null || addr.DataType != PLCAddressType.Bit)
            {
                System.Diagnostics.Debug.WriteLine($"地址 {addressName} 不存在或类型不匹配");
                return false;
            }

            try
            {
                if (!WriteBit(addr, true))
                    return false;

                Thread.Sleep(delayMs);

                return WriteBit(addr, false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"脉冲触发失败 [{addressName}]: {ex.Message}");
                return false;
            }
        }

        public bool ToggleBitByName(string addressName)
        {
            var addr = _addressRegistry.GetAddress(addressName);
            if (addr == null || addr.DataType != PLCAddressType.Bit)
            {
                System.Diagnostics.Debug.WriteLine($"地址 {addressName} 不存在或类型不匹配");
                return false;
            }

            try
            {
                bool current = ReadBit(addr);
                return WriteBit(addr, !current);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"切换位失败 [{addressName}]: {ex.Message}");
                return false;
            }
        }

        public bool WriteFloatByName(string addressName, float value)
        {
            var addr = _addressRegistry.GetAddress(addressName);
            if (addr == null || addr.DataType != PLCAddressType.Float)
            {
                System.Diagnostics.Debug.WriteLine($"地址 {addressName} 不存在或类型不匹配");
                return false;
            }
            // 对于Float，目前仍使用旧方法（M区和DB区）
            // 如果需要支持I/Q区的Float（较少见），可以扩展
            return WriteFloat(addr.DBNumber, addr.StartAddress, value);
        }

        public bool WriteInt16ByName(string addressName, short value)
        {
            var addr = _addressRegistry.GetAddress(addressName);
            if (addr == null || addr.DataType != PLCAddressType.Int16)
            {
                System.Diagnostics.Debug.WriteLine($"地址 {addressName} 不存在或类型不匹配");
                return false;
            }
            return WriteInt16(addr.DBNumber, addr.StartAddress, value);
        }

        public bool ReadBitByName(string addressName)
        {
            var addr = _addressRegistry.GetAddress(addressName);
            if (addr == null || addr.DataType != PLCAddressType.Bit)
            {
                System.Diagnostics.Debug.WriteLine($"地址 {addressName} 不存在或类型不匹配");
                return false;
            }
            return ReadBit(addr);  // ✅ 使用新方法
        }

        public float ReadFloatByName(string addressName)
        {
            var addr = _addressRegistry.GetAddress(addressName);
            if (addr == null || addr.DataType != PLCAddressType.Float)
            {
                System.Diagnostics.Debug.WriteLine($"地址 {addressName} 不存在或类型不匹配");
                return 0;
            }
            return ReadFloat(addr.DBNumber, addr.StartAddress);
        }

        public short ReadInt16ByName(string addressName)
        {
            var addr = _addressRegistry.GetAddress(addressName);
            if (addr == null || addr.DataType != PLCAddressType.Int16)
            {
                System.Diagnostics.Debug.WriteLine($"地址 {addressName} 不存在或类型不匹配");
                return 0;
            }
            return ReadInt16(addr.DBNumber, addr.StartAddress);
        }

        #endregion

        public void Dispose()
        {
            StopPolling();
            Disconnect();
            _plc = null;
        }

        #region 辅助类

        private class CachedValue
        {
            public object Value { get; set; }
            public long Timestamp { get; set; }
        }

        /// <summary>
        /// ✅ 修改：地址块配置（使用MemoryArea替代IsDB）
        /// </summary>
        private class AddressBlock
        {
            public PLCMemoryArea MemoryArea { get; set; }
            public int DBNumber { get; set; }
            public int StartAddress { get; set; }
            public int Length { get; set; }
        }

        #endregion
    }
}