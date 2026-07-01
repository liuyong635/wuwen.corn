using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Models.PLCModule;
using SeedCut.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Signals
{
    #region 信号值类型

    /// <summary>
    /// 信号值（支持多种数据类型）
    /// </summary>
    public class SignalValue
    {
        public PLCAddressType DataType { get; set; }
        public bool BoolValue { get; set; }
        public short Int16Value { get; set; }
        public float FloatValue { get; set; }

        /// <summary>
        /// 获取通用值
        /// </summary>
        public object Value
        {
            get
            {
                switch (DataType)
                {
                    case PLCAddressType.Bit: return BoolValue;
                    case PLCAddressType.Int16: return Int16Value;
                    case PLCAddressType.Float: return FloatValue;
                    default: return null;
                }
            }
        }

        /// <summary>
        /// 检查值是否发生变化
        /// </summary>
        public bool HasChanged(SignalValue other, float floatThreshold = 0.001f)
        {
            if (other == null || DataType != other.DataType)
                return true;

            switch (DataType)
            {
                case PLCAddressType.Bit:
                    return BoolValue != other.BoolValue;
                case PLCAddressType.Int16:
                    return Int16Value != other.Int16Value;
               
                case PLCAddressType.Float:
                    return Math.Abs(FloatValue - other.FloatValue) > floatThreshold;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 克隆
        /// </summary>
        public SignalValue Clone()
        {
            return new SignalValue
            {
                DataType = DataType,
                BoolValue = BoolValue,
                Int16Value = Int16Value,
                FloatValue = FloatValue
            };
        }

        public override string ToString()
        {
            switch (DataType)
            {
                case PLCAddressType.Bit: return BoolValue ? "1" : "0";
                case PLCAddressType.Int16: return Int16Value.ToString();
                case PLCAddressType.Float: return FloatValue.ToString("F3");
                default: return "N/A";
            }
        }
    }

    #endregion

    #region 事件参数

    /// <summary>
    /// 通用信号值变化事件参数（支持所有类型）
    /// </summary>
    public class SignalValueChangedEventArgs : EventArgs
    {
        public string SignalName { get; set; }
        public PLCAddressType DataType { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    #endregion

    /// <summary>
    /// PLC信号监控器
    /// 
    /// ★ 增强版：同时实现 ISignalMonitor 和 ISignalAccessor
    /// 
    /// 功能：
    /// 1. 从 PLCAddressRegistry 获取地址信息
    /// 2. 支持别名映射（代码中用英文名，映射到CSV中文名）
    /// 3. 支持 Bit/Int16/Float 多种数据类型
    /// 4. 手动注册需要监控的信号
    /// 5. 轮询检测信号变化并触发事件
    /// 6. ★ 新增：提供信号读写能力（ISignalAccessor）
    /// 
    /// 使用方式：
    /// 1. 通过 DI 注入
    /// 2. 调用 LoadAliasConfig() 加载别名配置（可选）
    /// 3. 调用 Register() 注册需要监控的信号
    /// 4. 订阅 PLC 连接事件，连接后自动启动
    /// 5. ★ 新增：Handler 通过 ctx.Signal 访问读写方法
    /// </summary>
    public class PlcSignalMonitor : ISignalMonitor, ISignalAccessor, IDisposable
    {
        #region 私有字段

        private readonly IPLCService _plcService;
        private readonly PLCAddressRegistry _addressRegistry;
        private readonly ILogService _logService;

        // 别名映射：别名 -> CSV地址名
        private readonly ConcurrentDictionary<string, string> _aliasMap;

        // 已注册的信号：信号名(或别名) -> PLCAddressItem
        private readonly ConcurrentDictionary<string, PLCAddressItem> _registeredSignals;

        // 信号当前值：信号名 -> SignalValue
        private readonly ConcurrentDictionary<string, SignalValue> _signalValues;

        // 轮询控制
        private readonly CancellationTokenSource _cts;
        private Thread _pollingThread;
        private volatile bool _isRunning;
        private bool _disposed;

        #endregion

        #region 属性

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 轮询间隔（毫秒）
        /// </summary>
        public int PollingIntervalMs { get; set; } = 100;

        /// <summary>
        /// Float 比较阈值
        /// </summary>
        public float FloatCompareThreshold { get; set; } = 0.001f;

        /// <summary>
        /// 已注册的信号数量
        /// </summary>
        public int RegisteredCount => _registeredSignals.Count;

        /// <summary>
        /// 别名数量
        /// </summary>
        public int AliasCount => _aliasMap.Count;

        /// <summary>
        /// ★ 新增：PLC 是否已连接
        /// </summary>
        public bool IsPlcConnected => _plcService?.IsConnected ?? false;

        #endregion

        #region 事件

        /// <summary>
        /// Bit 信号变化事件（与 ISignalMonitor 接口兼容）
        /// </summary>
        public event EventHandler<SignalChangedEventArgs> SignalChanged;

        /// <summary>
        /// 通用信号值变化事件（支持所有类型）
        /// </summary>
        public event EventHandler<SignalValueChangedEventArgs> SignalValueChanged;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建 PLC 信号监控器
        /// </summary>
        public PlcSignalMonitor(
            IPLCService plcService,
            PLCAddressRegistry addressRegistry,
            ILogService logService = null)
        {
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
            _addressRegistry = addressRegistry ?? throw new ArgumentNullException(nameof(addressRegistry));
            _logService = logService;

            _aliasMap = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _registeredSignals = new ConcurrentDictionary<string, PLCAddressItem>(StringComparer.OrdinalIgnoreCase);
            _signalValues = new ConcurrentDictionary<string, SignalValue>(StringComparer.OrdinalIgnoreCase);
            _cts = new CancellationTokenSource();

            // 订阅 PLC 连接状态事件
            _plcService.ConnectionStateChanged += OnPLCConnectionStateChanged;
            // ★ 新增：自动加载别名配置
            AutoLoadAliasConfig();
            LogInfo("PLC 信号监控器已创建");
        }

        #endregion

        #region 别名配置

        /// <summary>
        /// 自动加载别名配置
        /// 优先从配置文件加载，如果不存在则使用默认配置
        /// </summary>
        private void AutoLoadAliasConfig()
        {
            try
            {
                // 尝试从配置文件加载
                var configPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Config",
                    "SignalAliases.json");

                if (System.IO.File.Exists(configPath))
                {
                    LoadAliasConfig(configPath);
                    LogInfo("已从配置文件加载别名: {0}", configPath);
                }
                else
                {
                    // 使用默认配置
                    var defaultConfig = SignalAliasConfig.CreateDefaultConfig();
                    RegisterAliases(defaultConfig.Aliases);

                    // 可选：保存默认配置供用户参考
                    defaultConfig.SaveToFile(configPath);

                    LogInfo("已加载默认别名配置 ({0} 个映射)", defaultConfig.Aliases.Count);
                }
            }
            catch (Exception ex)
            {
                // 加载失败时使用默认配置
                LogWarning("加载别名配置失败: {0}，使用默认配置", ex.Message);

                var defaultConfig = SignalAliasConfig.CreateDefaultConfig();
                RegisterAliases(defaultConfig.Aliases);
            }
        }

        /// <summary>
        /// 从 JSON 文件加载别名配置
        /// </summary>
        public void LoadAliasConfig(string jsonFilePath)
        {
            var config = SignalAliasConfig.LoadFromFile(jsonFilePath);

            foreach (var kvp in config.Aliases)
            {
                _aliasMap[kvp.Key] = kvp.Value;
            }

            LogInfo("已加载 {0} 个别名映射", config.Aliases.Count);
        }

        /// <summary>
        /// 手动注册别名
        /// </summary>
        public void RegisterAlias(string alias, string csvAddressName)
        {
            if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(csvAddressName))
                return;

            _aliasMap[alias] = csvAddressName;
            LogDebug("注册别名: {0} -> {1}", alias, csvAddressName);
        }

        /// <summary>
        /// 批量注册别名
        /// </summary>
        public void RegisterAliases(Dictionary<string, string> aliases)
        {
            if (aliases == null) return;

            foreach (var kvp in aliases)
            {
                RegisterAlias(kvp.Key, kvp.Value);
            }
        }

        /// <summary>
        /// 解析信号名（如果是别名则返回CSV名，否则原样返回）
        /// ★ 实现 ISignalAccessor.ResolveAlias
        /// </summary>
        public string ResolveAlias(string aliasOrName)
        {
            if (string.IsNullOrEmpty(aliasOrName))
                return aliasOrName;

            // 如果是别名，返回映射的CSV名
            if (_aliasMap.TryGetValue(aliasOrName, out var csvName))
                return csvName;

            // 否则原样返回
            return aliasOrName;
        }

        // 保持原方法名兼容
        private string ResolveSignalName(string signalName) => ResolveAlias(signalName);

        #endregion

        #region 信号注册

        /// <summary>
        /// 注册信号（支持别名或CSV名）
        /// </summary>
        public void Register(string signalName)
        {
            if (string.IsNullOrEmpty(signalName))
                return;

            // 解析真实地址名
            var csvName = ResolveSignalName(signalName);

            // 从注册表获取地址
            var address = _addressRegistry.GetAddress(csvName);
            if (address == null)
            {
                LogWarning("信号注册失败，地址不存在: {0} (解析后: {1})", signalName, csvName);
                return;
            }

            // 注册（使用原始名称作为key，方便后续使用）
            _registeredSignals[signalName] = address;

            // 初始化信号值
            _signalValues[signalName] = new SignalValue
            {
                DataType = address.DataType,
                BoolValue = false,
                Int16Value = 0,
                FloatValue = 0
            };

            LogDebug("已注册信号: {0} -> {1} ({2})", signalName, address.GetSiemensAddress(), address.DataType);
        }

        /// <summary>
        /// 注册信号（指定PLC地址，用于 ISignalMonitor 接口兼容）
        /// </summary>
        public void Register(string signalName, string plcAddress)
        {
            // 在CSV驱动模式下，plcAddress 参数被忽略
            // 如果需要，可以将其作为别名处理
            if (!string.IsNullOrEmpty(plcAddress) && plcAddress != signalName)
            {
                RegisterAlias(signalName, plcAddress);
            }
            Register(signalName);
        }

        /// <summary>
        /// 批量注册信号
        /// </summary>
        public void Register(params string[] signalNames)
        {
            if (signalNames == null) return;

            foreach (var name in signalNames)
            {
                Register(name);
            }
        }

        /// <summary>
        /// 取消注册信号
        /// </summary>
        public void Unregister(string signalName)
        {
            _registeredSignals.TryRemove(signalName, out _);
            _signalValues.TryRemove(signalName, out _);
        }

        /// <summary>
        /// 清除所有注册的信号
        /// </summary>
        public void ClearRegistrations()
        {
            _registeredSignals.Clear();
            _signalValues.Clear();
        }

        #endregion

        #region ISignalMonitor - 信号读取（缓存值）

        /// <summary>
        /// 获取 Bit 信号值（从缓存读取）
        /// </summary>
        public bool GetSignal(string signalName, bool defaultValue = false)
        {
            if (_signalValues.TryGetValue(signalName, out var value))
            {
                if (value.DataType == PLCAddressType.Bit)
                    return value.BoolValue;

                // 非Bit类型，转换为bool
                return value.Int16Value != 0 || Math.Abs(value.FloatValue) > FloatCompareThreshold;
            }
            return defaultValue;
        }

        /// <summary>
        /// 获取 Int16 信号值（从缓存读取）
        /// </summary>
        public short GetInt16(string signalName, short defaultValue = 0)
        {
            if (_signalValues.TryGetValue(signalName, out var value))
            {
                return value.Int16Value;
            }
            return defaultValue;
        }

        /// <summary>
        /// 获取 Float 信号值（从缓存读取）
        /// </summary>
        public float GetFloat(string signalName, float defaultValue = 0)
        {
            if (_signalValues.TryGetValue(signalName, out var value))
            {
                return value.FloatValue;
            }
            return defaultValue;
        }

        /// <summary>
        /// 获取信号值（通用，从缓存读取）
        /// </summary>
        public SignalValue GetValue(string signalName)
        {
            _signalValues.TryGetValue(signalName, out var value);
            return value?.Clone();
        }

        /// <summary>
        /// 检查信号是否已注册
        /// ★ 实现 ISignalAccessor.HasSignal
        /// </summary>
        public bool HasSignal(string aliasOrName)
        {
            // 先检查是否已注册
            if (_registeredSignals.ContainsKey(aliasOrName))
                return true;

            // 再检查是否能解析到有效地址
            var csvName = ResolveAlias(aliasOrName);
            return _addressRegistry.GetAddress(csvName) != null;
        }

        /// <summary>
        /// 获取所有 Bit 信号（ISignalMonitor 接口兼容）
        /// </summary>
        public IReadOnlyDictionary<string, bool> GetAllSignals()
        {
            return _signalValues
                .Where(kvp => kvp.Value.DataType == PLCAddressType.Bit)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.BoolValue);
        }

        /// <summary>
        /// 获取所有信号值
        /// </summary>
        public IReadOnlyDictionary<string, SignalValue> GetAllValues()
        {
            return _signalValues.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone());
        }

        #endregion

        #region ★ ISignalAccessor - 信号读写（直接读写 PLC）

        /// <summary>
        /// ★ 读取位信号（直接读 PLC，支持别名）
        /// </summary>
        public bool ReadBit(string aliasOrName)
        {
            var address = GetAddressItem(aliasOrName);
            if (address == null)
            {
                LogWarning("ReadBit 失败，地址不存在: {0}", aliasOrName);
                return false;
            }

            if (address.DataType != PLCAddressType.Bit)
            {
                LogWarning("ReadBit 失败，{0} 不是 Bit 类型", aliasOrName);
                return false;
            }

            if (!IsPlcConnected)
            {
                LogWarning("ReadBit 失败，PLC 未连接");
                return false;
            }

            try
            {
                return _plcService.ReadBit(address);
            }
            catch (Exception ex)
            {
                LogError(ex, "ReadBit 异常: {0}", aliasOrName);
                return false;
            }
        }

        /// <summary>
        /// ★ 写入位信号（支持别名）
        /// </summary>
        public void WriteBit(string aliasOrName, bool value)
        {
            var address = GetAddressItem(aliasOrName);
            if (address == null)
            {
                LogWarning("WriteBit 失败，地址不存在: {0}", aliasOrName);
                return;
            }

            if (address.DataType != PLCAddressType.Bit)
            {
                LogWarning("WriteBit 失败，{0} 不是 Bit 类型", aliasOrName);
                return;
            }

            if (!address.Writable)
            {
                LogWarning("WriteBit 失败，{0} 不可写", aliasOrName);
                return;
            }

            if (!IsPlcConnected)
            {
                LogWarning("WriteBit 失败，PLC 未连接");
                return;
            }

            try
            {
                _plcService.WriteBit(address, value);
                LogDebug("WriteBit: {0} = {1}", aliasOrName, value);
            }
            catch (Exception ex)
            {
                LogError(ex, "WriteBit 异常: {0}", aliasOrName);
            }
        }

        /// <summary>
        /// ★ 写入脉冲信号（支持别名）
        /// </summary>
        public void WriteBitPulse(string aliasOrName, int durationMs = 50)
        {
            var address = GetAddressItem(aliasOrName);
            if (address == null)
            {
                LogWarning("WriteBitPulse 失败，地址不存在: {0}", aliasOrName);
                return;
            }

            if (!IsPlcConnected)
            {
                LogWarning("WriteBitPulse 失败，PLC 未连接");
                return;
            }

            try
            {
                _plcService.WriteBit(address, true);
                Thread.Sleep(durationMs);
                _plcService.WriteBit(address, false);
                LogDebug("WriteBitPulse: {0} ({1}ms)", aliasOrName, durationMs);
            }
            catch (Exception ex)
            {
                LogError(ex, "WriteBitPulse 异常: {0}", aliasOrName);
            }
        }

        /// <summary>
        /// ★ 异步写入脉冲信号
        /// </summary>
        public async Task WriteBitPulseAsync(string aliasOrName, int durationMs = 50, CancellationToken ct = default)
        {
            var address = GetAddressItem(aliasOrName);
            if (address == null)
            {
                LogWarning("WriteBitPulseAsync 失败，地址不存在: {0}", aliasOrName);
                return;
            }

            if (!IsPlcConnected)
            {
                LogWarning("WriteBitPulseAsync 失败，PLC 未连接");
                return;
            }

            try
            {
                _plcService.WriteBit(address, true);
                await Task.Delay(durationMs, ct);
                _plcService.WriteBit(address, false);
                LogDebug("WriteBitPulseAsync: {0} ({1}ms)", aliasOrName, durationMs);
            }
            catch (OperationCanceledException)
            {
                // 取消时确保信号复位
                try { _plcService.WriteBit(address, false); } catch { }
                throw;
            }
            catch (Exception ex)
            {
                LogError(ex, "WriteBitPulseAsync 异常: {0}", aliasOrName);
            }
        }

        /// <summary>
        /// ★ 切换位信号
        /// </summary>
        public void ToggleBit(string aliasOrName)
        {
            bool current = ReadBit(aliasOrName);
            WriteBit(aliasOrName, !current);
        }

        /// <summary>
        /// ★ 读取 Int16 信号（直接读 PLC）
        /// </summary>
        public short ReadInt16(string aliasOrName)
        {
            var address = GetAddressItem(aliasOrName);
            if (address == null)
            {
                LogWarning("ReadInt16 失败，地址不存在: {0}", aliasOrName);
                return 0;
            }

            if (address.DataType != PLCAddressType.Int16)
            {
                LogWarning("ReadInt16 失败，{0} 不是 Int16 类型", aliasOrName);
                return 0;
            }

            if (!IsPlcConnected)
            {
                LogWarning("ReadInt16 失败，PLC 未连接");
                return 0;
            }

            try
            {
                return _plcService.ReadInt16(address.DBNumber, address.StartAddress);
            }
            catch (Exception ex)
            {
                LogError(ex, "ReadInt16 异常: {0}", aliasOrName);
                return 0;
            }
        }

        /// <summary>
        /// ★ 写入 Int16 信号
        /// </summary>
        public void WriteInt16(string aliasOrName, short value)
        {
            var address = GetAddressItem(aliasOrName);
            if (address == null)
            {
                LogWarning("WriteInt16 失败，地址不存在: {0}", aliasOrName);
                return;
            }

            if (!IsPlcConnected)
            {
                LogWarning("WriteInt16 失败，PLC 未连接");
                return;
            }

            try
            {
                _plcService.WriteInt16(address.DBNumber, address.StartAddress, value);
                LogDebug("WriteInt16: {0} = {1}", aliasOrName, value);
            }
            catch (Exception ex)
            {
                LogError(ex, "WriteInt16 异常: {0}", aliasOrName);
            }
        }

        /// <summary>
        /// ★ 读取 Float 信号（直接读 PLC）
        /// </summary>
        public float ReadFloat(string aliasOrName)
        {
            var address = GetAddressItem(aliasOrName);
            if (address == null)
            {
                LogWarning("ReadFloat 失败，地址不存在: {0}", aliasOrName);
                return 0;
            }

            if (address.DataType != PLCAddressType.Float)
            {
                LogWarning("ReadFloat 失败，{0} 不是 Float 类型", aliasOrName);
                return 0;
            }

            if (!IsPlcConnected)
            {
                LogWarning("ReadFloat 失败，PLC 未连接");
                return 0;
            }

            try
            {
                return _plcService.ReadFloat(address.DBNumber, address.StartAddress);
            }
            catch (Exception ex)
            {
                LogError(ex, "ReadFloat 异常: {0}", aliasOrName);
                return 0;
            }
        }

        /// <summary>
        /// ★ 写入 Float 信号
        /// </summary>
        public void WriteFloat(string aliasOrName, float value)
        {
            var address = GetAddressItem(aliasOrName);
            if (address == null)
            {
                LogWarning("WriteFloat 失败，地址不存在: {0}", aliasOrName);
                return;
            }

            if (!IsPlcConnected)
            {
                LogWarning("WriteFloat 失败，PLC 未连接");
                return;
            }

            try
            {
                _plcService.WriteFloat(address.DBNumber, address.StartAddress, value);
                LogDebug("WriteFloat: {0} = {1}", aliasOrName, value);
            }
            catch (Exception ex)
            {
                LogError(ex, "WriteFloat 异常: {0}", aliasOrName);
            }
        }

        /// <summary>
        /// 获取 PLCAddressItem（支持别名解析）
        /// </summary>
        private PLCAddressItem GetAddressItem(string aliasOrName)
        {
            var csvName = ResolveAlias(aliasOrName);
            return _addressRegistry.GetAddress(csvName);
        }

        #endregion

        #region 轮询控制

        /// <summary>
        /// 启动信号监控
        /// </summary>
        public void Start()
        {
            if (_isRunning)
            {
                LogDebug("信号监控已在运行中");
                return;
            }

            if (_registeredSignals.Count == 0)
            {
                LogWarning("没有注册任何信号，无法启动监控");
                return;
            }

            _isRunning = true;
            _pollingThread = new Thread(PollingThreadProc)
            {
                IsBackground = true,
                Name = "PLC-Signal-Polling-Thread"
            };
            _pollingThread.Start();

            LogInfo("PLC 信号监控已启动 (间隔: {0}ms, 监控 {1} 个信号)", PollingIntervalMs, _registeredSignals.Count);
        }

        /// <summary>
        /// 停止信号监控
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;

            if (_pollingThread != null && _pollingThread.IsAlive)
            {
                _pollingThread.Join(2000);
            }

            LogInfo("PLC 信号监控已停止");
        }

        private void PollingThreadProc()
        {
            while (_isRunning && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (_plcService.IsConnected)
                    {
                        RefreshAllSignals();
                    }
                    Thread.Sleep(PollingIntervalMs);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogError(ex, "信号轮询异常");
                    Thread.Sleep(500);
                }
            }
        }

        /// <summary>
        /// 手动刷新所有信号
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default)
        {
            if (_plcService.IsConnected)
            {
                RefreshAllSignals();
            }
            return Task.CompletedTask;
        }

        private void RefreshAllSignals()
        {
            foreach (var kvp in _registeredSignals)
            {
                var signalName = kvp.Key;
                var address = kvp.Value;

                try
                {
                    ReadAndUpdateSignal(signalName, address);
                }
                catch (Exception ex)
                {
                    LogDebug("读取信号 {0} 失败: {1}", signalName, ex.Message);
                }
            }
        }

        private void ReadAndUpdateSignal(string signalName, PLCAddressItem address)
        {
            // 读取新值
            var newValue = new SignalValue { DataType = address.DataType };

            switch (address.DataType)
            {
                case PLCAddressType.Bit:
                    newValue.BoolValue = _plcService.ReadBit(address);
                    break;

                case PLCAddressType.Int16:
                    newValue.Int16Value = _plcService.ReadInt16(address.DBNumber, address.StartAddress);
                    break;

                case PLCAddressType.Float:
                    newValue.FloatValue = _plcService.ReadFloat(address.DBNumber, address.StartAddress);
                    break;

                default:
                    return;
            }

            // 获取旧值
            var oldValue = _signalValues.GetOrAdd(signalName, _ => new SignalValue { DataType = address.DataType });

            // 检查是否变化
            if (newValue.HasChanged(oldValue, FloatCompareThreshold))
            {
                // 更新值
                _signalValues[signalName] = newValue;

                // 触发事件
                RaiseSignalChanged(signalName, address.DataType, oldValue, newValue);
            }
        }

        private void RaiseSignalChanged(string signalName, PLCAddressType dataType, SignalValue oldValue, SignalValue newValue)
        {
            // 触发通用事件
            SignalValueChanged?.Invoke(this, new SignalValueChangedEventArgs
            {
                SignalName = signalName,
                DataType = dataType,
                OldValue = oldValue.Value,
                NewValue = newValue.Value
            });

            // 如果是 Bit 类型，触发兼容事件
            if (dataType == PLCAddressType.Bit)
            {
                SignalChanged?.Invoke(this, new SignalChangedEventArgs
                {
                    SignalName = signalName,
                    OldValue = oldValue.BoolValue,
                    NewValue = newValue.BoolValue
                });
            }

            LogDebug("信号变化: {0} = {1} -> {2}", signalName, oldValue, newValue);
        }

        #endregion

        #region ISignalAccessor - 等待信号

        /// <summary>
        /// ★ 等待 Bit 信号达到指定值
        /// </summary>
        public async Task<bool> WaitForBitAsync(string aliasOrName, bool expectedValue, TimeSpan timeout, CancellationToken ct = default)
        {
            // 先检查当前值
            if (ReadBit(aliasOrName) == expectedValue)
                return true;

            var startTime = DateTime.Now;

            while (!ct.IsCancellationRequested)
            {
                if (ReadBit(aliasOrName) == expectedValue)
                    return true;

                if (DateTime.Now - startTime > timeout)
                {
                    LogWarning("WaitForBitAsync 超时: {0}={1}", aliasOrName, expectedValue);
                    return false;
                }

                await Task.Delay(50, ct);
            }

            return false;
        }

        /// <summary>
        /// ★ 等待 Int16 信号达到指定范围
        /// </summary>
        public async Task<bool> WaitForInt16InRangeAsync(string aliasOrName, short minValue, short maxValue, TimeSpan timeout, CancellationToken ct = default)
        {
            var startTime = DateTime.Now;

            while (!ct.IsCancellationRequested)
            {
                var value = ReadInt16(aliasOrName);
                if (value >= minValue && value <= maxValue)
                    return true;

                if (DateTime.Now - startTime > timeout)
                    return false;

                await Task.Delay(50, ct);
            }

            return false;
        }

        /// <summary>
        /// ★ 等待 Float 信号达到指定范围
        /// </summary>
        public async Task<bool> WaitForFloatInRangeAsync(string aliasOrName, float minValue, float maxValue, TimeSpan timeout, CancellationToken ct = default)
        {
            var startTime = DateTime.Now;

            while (!ct.IsCancellationRequested)
            {
                var value = ReadFloat(aliasOrName);
                if (value >= minValue && value <= maxValue)
                    return true;

                if (DateTime.Now - startTime > timeout)
                    return false;

                await Task.Delay(50, ct);
            }

            return false;
        }

        /// <summary>
        /// 等待 Bit 信号达到指定值（ISignalMonitor 接口兼容）
        /// </summary>
        public async Task<bool> WaitForSignalAsync(string signalName, bool expectedValue, TimeSpan timeout, CancellationToken ct = default)
        {
            return await WaitForBitAsync(signalName, expectedValue, timeout, ct);
        }

        /// <summary>
        /// 等待信号边沿
        /// </summary>
        public async Task<bool> WaitForEdgeAsync(string signalName, SignalEdge edge, TimeSpan timeout, CancellationToken ct = default)
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(timeout);

                var tcs = new TaskCompletionSource<bool>();

                void Handler(object s, SignalChangedEventArgs e)
                {
                    if (e.SignalName != signalName)
                        return;

                    var match = edge == SignalEdge.Both ||
                               (edge == SignalEdge.Rising && e.NewValue) ||
                               (edge == SignalEdge.Falling && !e.NewValue);

                    if (match)
                        tcs.TrySetResult(true);
                }

                SignalChanged += Handler;
                try
                {
                    using (cts.Token.Register(() => tcs.TrySetResult(false)))
                    {
                        return await tcs.Task;
                    }
                }
                finally
                {
                    SignalChanged -= Handler;
                }
            }
        }

        #endregion

        #region PLC 连接状态处理

        private void OnPLCConnectionStateChanged(object sender, PLCConnectionStateChangedEventArgs e)
        {
            if (e.IsConnected)
            {
                LogInfo("检测到 PLC 已连接");

                // 如果有已注册的信号，自动启动监控
                if (_registeredSignals.Count > 0 && !_isRunning)
                {
                    Start();
                }
            }
            else
            {
                LogWarning("检测到 PLC 已断开");
                Stop();
            }
        }

        #endregion

        #region 日志辅助

        private void LogInfo(string message, params object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            _logService?.Information("[PlcSignalMonitor] " + formatted);
            System.Diagnostics.Debug.WriteLine("[PlcSignalMonitor] " + formatted);
        }

        private void LogDebug(string message, params object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            _logService?.Debug("[PlcSignalMonitor] " + formatted);
            System.Diagnostics.Debug.WriteLine("[PlcSignalMonitor] " + formatted);
        }

        private void LogWarning(string message, params object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            _logService?.Warning("[PlcSignalMonitor] ⚠️ " + formatted);
            System.Diagnostics.Debug.WriteLine("[PlcSignalMonitor] ⚠️ " + formatted);
        }

        private void LogError(Exception ex, string message, params object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            _logService?.Error(ex, "[PlcSignalMonitor] " + formatted);
            System.Diagnostics.Debug.WriteLine("[PlcSignalMonitor] ❌ " + formatted + ": " + ex?.Message);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            // 取消订阅事件
            _plcService.ConnectionStateChanged -= OnPLCConnectionStateChanged;

            // 停止轮询
            Stop();

            // 取消令牌
            _cts.Cancel();
            _cts.Dispose();

            // 清理数据
            _registeredSignals.Clear();
            _signalValues.Clear();
            _aliasMap.Clear();

            _disposed = true;

            LogInfo("PLC 信号监控器已释放");
        }

        #endregion
    }
}