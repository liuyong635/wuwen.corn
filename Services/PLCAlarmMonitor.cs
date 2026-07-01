using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SeedCut.Framework.Models;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Models.PLCModule;

namespace SeedCut.Services
{
    /// <summary>
    /// PLC 报警监控器
    /// 
    /// 职责：轮询 PLC 信号，检测报警触发/恢复，调用 IAlarmService
    /// 
    /// 调用方式：
    /// 1. 在 DI 容器中注册为 Singleton
    /// 2. 在应用启动时通过 serviceProvider.GetRequiredService 获取实例（触发构造函数）
    /// 3. 构造函数中订阅 IPLCService.ConnectionStateChanged 事件
    /// 4. PLC 连接成功时自动启动轮询，断开时自动停止
    /// 
    /// 无需手动调用 StartPolling/StopPolling，除非有特殊需求
    /// </summary>
    public class PLCAlarmMonitor : IDisposable
    {
        private readonly IPLCService _plcService;
        private readonly IAlarmService _alarmService;
        private readonly PLCAddressRegistry _addressRegistry;
        private readonly ILogService _logService;

        // 报警地址缓存
        private readonly Dictionary<string, PLCAddressItem> _alarmAddresses;

        // 上一次信号状态（用于边沿检测）
        private readonly Dictionary<string, bool> _lastSignalStates;

        // 轮询线程
        private Thread _pollingThread;
        private volatile bool _isPolling;
        private int _pollingInterval = 500;

        // 初始化标志
        private bool _isInitialized;
        private bool _disposed;

        #region 属性

        /// <summary>
        /// 轮询间隔（毫秒）
        /// </summary>
        public int PollingInterval
        {
            get { return _pollingInterval; }
            set
            {
                if (value >= 100 && value <= 10000)
                {
                    _pollingInterval = value;
                }
            }
        }

        /// <summary>
        /// 是否正在轮询
        /// </summary>
        public bool IsPolling
        {
            get { return _isPolling; }
        }

        /// <summary>
        /// 已注册的报警地址数量
        /// </summary>
        public int AlarmAddressCount
        {
            get { return _alarmAddresses.Count; }
        }

        /// <summary>
        /// 是否已初始化
        /// </summary>
        public bool IsInitialized
        {
            get { return _isInitialized; }
        }

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建 PLC 报警监控器
        /// 
        /// 构造函数会自动：
        /// 1. 订阅 PLC 连接状态事件
        /// 2. 如果 PLC 已连接，立即扫描地址并启动轮询
        /// </summary>
        public PLCAlarmMonitor(
            IPLCService plcService,
            IAlarmService alarmService,
            PLCAddressRegistry addressRegistry,
            ILogService logService = null)
        {
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
            _alarmService = alarmService ?? throw new ArgumentNullException(nameof(alarmService));
            _addressRegistry = addressRegistry ?? throw new ArgumentNullException(nameof(addressRegistry));
            _logService = logService;

            _alarmAddresses = new Dictionary<string, PLCAddressItem>(StringComparer.OrdinalIgnoreCase);
            _lastSignalStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            // ★ 订阅 PLC 连接状态事件 - 自动启停轮询
            _plcService.ConnectionStateChanged += OnPLCConnectionStateChanged;

            LogInfo("PLC 报警监控器已创建");

            // ★ 如果 PLC 已经连接，立即初始化并启动
            if (_plcService.IsConnected)
            {
                LogInfo("检测到 PLC 已连接，立即初始化");
                Initialize();
            }
        }

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化报警监控器
        /// 扫描报警地址并启动轮询（如果 PLC 已连接）
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;

            ScanAlarmAddresses();

            if (_plcService.IsConnected && _alarmAddresses.Count > 0)
            {
                StartPolling();
            }

            _isInitialized = true;
            LogInfo("PLC 报警监控器初始化完成");
        }

        #endregion

        #region 报警地址扫描

        /// <summary>
        /// 扫描报警地址（从 PLCAddressRegistry 中筛选 IsAlarmSignal=true 的地址）
        /// </summary>
        public void ScanAlarmAddresses()
        {
            _alarmAddresses.Clear();
            _lastSignalStates.Clear();

            var allAddresses = _addressRegistry.Addresses;
            int count = 0;

            foreach (var address in allAddresses)
            {
                if (address.DataType == PLCAddressType.Bit && address.IsAlarmSignal)
                {
                    _alarmAddresses[address.Name] = address;
                    _lastSignalStates[address.Name] = false;
                    count++;

                    var triggerType = address.AlarmTriggerOnHigh ? "高触发(=1报警)" : "低触发(=0报警)";
                    System.Diagnostics.Debug.WriteLine(string.Format(
                        "✓ 已注册报警: {0} | {1} | {2} | {3}",
                        address.Name, address.GetSiemensAddress(), triggerType, address.AlarmLevel));
                }
            }

            LogInfo("报警地址扫描完成，共注册 {0} 个报警信号", count);
        }

        /// <summary>
        /// 获取所有已注册的报警地址
        /// </summary>
        public IReadOnlyList<PLCAddressItem> GetAlarmAddresses()
        {
            return _alarmAddresses.Values.ToList().AsReadOnly();
        }

        #endregion

        #region PLC 连接状态处理（自动启停）

        private void OnPLCConnectionStateChanged(object sender, PLCConnectionStateChangedEventArgs e)
        {
            try
            {
                if (e.IsConnected)
                {
                    LogInfo("检测到 PLC 已连接");

                    // 扫描报警地址（如果尚未扫描）
                    if (_alarmAddresses.Count == 0)
                    {
                        ScanAlarmAddresses();
                    }

                    // ★ 自动启动轮询
                    if (_alarmAddresses.Count > 0)
                    {
                        StartPolling();
                    }
                    else
                    {
                        LogWarning("没有配置报警地址，跳过轮询启动");
                    }
                }
                else
                {
                    LogWarning("检测到 PLC 已断开");

                    // ★ 自动停止轮询
                    StopPolling();

                    // 可选：触发一个 PLC 断线报警
                    _alarmService.TriggerAlarm(
                        name: "PLC通讯中断",
                        level: AlarmLevel.Critical,
                        description: "与 PLC 的通讯已断开",
                        sourceModule: "PLC通讯",
                        sourceIdentifier: "_PLC_DISCONNECT_",
                        showPopup: true
                    );
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "处理 PLC 连接状态变化时发生异常");
            }
        }

        #endregion

        #region 轮询控制

        /// <summary>
        /// 启动报警轮询
        /// 通常不需要手动调用，会在 PLC 连接成功时自动启动
        /// </summary>
        public void StartPolling()
        {
            if (_isPolling)
            {
                LogInfo("轮询已在运行中");
                return;
            }

            if (_alarmAddresses.Count == 0)
            {
                LogWarning("没有报警地址，无法启动轮询");
                return;
            }

            _isPolling = true;
            _pollingThread = new Thread(PollingThreadProc)
            {
                IsBackground = true,
                Name = "PLC-Alarm-Polling-Thread"
            };
            _pollingThread.Start();

            LogInfo("PLC 报警轮询已启动 (间隔: {0}ms, 监控 {1} 个信号)",
                _pollingInterval, _alarmAddresses.Count);
        }

        /// <summary>
        /// 停止报警轮询
        /// 通常不需要手动调用，会在 PLC 断开时自动停止
        /// </summary>
        public void StopPolling()
        {
            if (!_isPolling) return;

            _isPolling = false;

            if (_pollingThread != null && _pollingThread.IsAlive)
            {
                _pollingThread.Join(2000);
            }

            LogInfo("PLC 报警轮询已停止");
        }

        private void PollingThreadProc()
        {
            while (_isPolling)
            {
                try
                {
                    if (_plcService.IsConnected)
                    {
                        CheckAllAlarms();
                    }
                    Thread.Sleep(_pollingInterval);
                }
                catch (Exception ex)
                {
                    LogError(ex, "报警轮询异常");
                    Thread.Sleep(1000);
                }
            }
        }

        private void CheckAllAlarms()
        {
            foreach (var kvp in _alarmAddresses)
            {
                var addressItem = kvp.Value;

                try
                {
                    // 读取 PLC 信号状态
                    bool currentState = _plcService.ReadBit(
                        addressItem.DBNumber,
                        addressItem.StartAddress,
                        addressItem.BitPosition);

                    // 判断是否触发（根据高/低触发配置）
                    bool isTriggered = addressItem.AlarmTriggerOnHigh ? currentState : !currentState;

                    // 获取上一次状态
                    bool lastTriggered;
                    _lastSignalStates.TryGetValue(addressItem.Name, out lastTriggered);

                    // 上升沿 - 触发报警
                    if (isTriggered && !lastTriggered)
                    {
                        TriggerPLCAlarm(addressItem);
                    }
                    // 下降沿 - 恢复报警
                    else if (!isTriggered && lastTriggered)
                    {
                        RecoverPLCAlarm(addressItem);
                    }

                    // 更新状态
                    _lastSignalStates[addressItem.Name] = isTriggered;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(string.Format(
                        "✗ 检测报警 {0} 失败: {1}", addressItem.Name, ex.Message));
                }
            }
        }

        private void TriggerPLCAlarm(PLCAddressItem addressItem)
        {
            _alarmService.TriggerAlarm(
                name: addressItem.Name,
                level: addressItem.AlarmLevel,
                description: addressItem.Description ?? addressItem.Name,
                sourceModule: addressItem.Group ?? "PLC",
                sourceIdentifier: addressItem.Name,
                showPopup: addressItem.AlarmShowPopup
            );
        }

        private void RecoverPLCAlarm(PLCAddressItem addressItem)
        {
            _alarmService.RecoverAlarmBySource(addressItem.Name);

            // 如果是 PLC 断线报警恢复（PLC 重新连接时）
            // 这里不需要特殊处理，因为 PLC 断线报警使用固定的 sourceIdentifier
        }

        #endregion

        #region 日志辅助方法

        private void LogInfo(string message, params object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            if (_logService != null)
            {
                _logService.Information(formatted);
            }
            System.Diagnostics.Debug.WriteLine("[PLCAlarmMonitor] " + formatted);
        }

        private void LogWarning(string message, params object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            if (_logService != null)
            {
                _logService.Warning(formatted);
            }
            System.Diagnostics.Debug.WriteLine("[PLCAlarmMonitor] ⚠️ " + formatted);
        }

        private void LogError(Exception ex, string message, params object[] args)
        {
            var formatted = args.Length > 0 ? string.Format(message, args) : message;
            if (_logService != null)
            {
                _logService.Error(ex, formatted);
            }
            System.Diagnostics.Debug.WriteLine("[PLCAlarmMonitor] ❌ " + formatted + ": " + ex.Message);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                // 取消订阅事件
                if (_plcService != null)
                {
                    _plcService.ConnectionStateChanged -= OnPLCConnectionStateChanged;
                }

                StopPolling();
                _alarmAddresses.Clear();
                _lastSignalStates.Clear();

                _disposed = true;
                LogInfo("PLC 报警监控器已释放");
            }
        }

        #endregion
    }
}