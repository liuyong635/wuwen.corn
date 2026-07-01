using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Models.PLCModule;
using SeedCut.Services.Connection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SeedCut.Services
{
    /// <summary>
    /// ✅ 优化版：移除嵌套Task.Run，提高心跳稳定性
    /// </summary>
    public class PLCServiceConnectAdapter : IConnectableDevice
    {
        private readonly IPLCService _plcService;
        private readonly ILogService _logService;

        // 心跳地址配置
        private const int HEARTBEAT_WRITE_BYTE = 1003;
        private const int HEARTBEAT_WRITE_BIT = 6;
        private const int COMM_INTERRUPT_BYTE = 1003;
        private const int COMM_INTERRUPT_BIT = 7;
        private const int SERVO_ERROR_BYTE = 1012;
        private const int SERVO_ERROR_BIT = 0;

        // 心跳性能监控
        private readonly Stopwatch _heartbeatStopwatch = new Stopwatch();
        private long _totalHeartbeats = 0;
        private long _failedHeartbeats = 0;
        private long _maxHeartbeatTime = 0;
        private long _avgHeartbeatTime = 0;

        private readonly List<(int ByteAddr, int BitPos, string AxisName)> _axisStatusAddresses = new List<(int, int, string)>
        {
            (7000, 1, "环形导轨轴"),
            (7000, 2, "种子上料轴"),
            (7000, 3, "拍照定位轴"),
            (7000, 4, "激光切割定位轴"),
            (7000, 5, "小料盘Y轴"),
            (7000, 6, "小料盘X轴"),
            (7000, 7, "种子下料轴"),
            (7001, 0, "大料盘Y轴"),
            (7001, 1, "大料盘X轴")
        };

        public bool EnableExtendedHealthCheck { get; set; } = false;
        private int _heartbeatFailCount = 0;
        private const int MAX_HEARTBEAT_FAIL_COUNT = 3;

        public string DeviceId => "PLC_Main";
        public string DeviceName => _plcService.ConnectionConfig?.DeviceName ?? "西门子S7 PLC";
        public DeviceType DeviceType => DeviceType.PLC;
        public bool IsConnected => _plcService.IsConnected;

        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

        public PLCServiceConnectAdapter(IPLCService plcService, ILogService logService = null)
        {
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
            _logService = logService;

            if (_plcService != null)
            {
                _plcService.ConnectionStateChanged += OnPLCConnectionStateChanged;
            }
        }

        public bool Connect()
        {
            return _plcService.Connect();
        }

        public void Disconnect()
        {
            PrintHeartbeatStatistics();
            _plcService.Disconnect();
        }

        /// <summary>
        /// ✅ 优化版心跳检测：移除Task.Run，直接同步调用
        /// </summary>
        public bool CheckConnection()
        {
            if (!_plcService.IsConnected)
            {
                _logService?.Debug("[PLC心跳] 基本连接状态：未连接");
                _heartbeatFailCount = 0;
                OnConnectionStateChanged(false, "PLC基本连接已断开");
                return false;
            }

            _heartbeatStopwatch.Restart();

            try
            {


                // ✅ 直接同步调用，移除Task.Run包装
                bool heartbeatWriteSuccess = WriteHeartbeat();

                _heartbeatStopwatch.Stop();

                // 记录心跳性能
                long elapsedMs = _heartbeatStopwatch.ElapsedMilliseconds;

                _totalHeartbeats++;
                _avgHeartbeatTime = (_avgHeartbeatTime * (_totalHeartbeats - 1) + elapsedMs) / _totalHeartbeats;

                if (elapsedMs > _maxHeartbeatTime)
                {
                    _maxHeartbeatTime = elapsedMs;
                }

                // 如果心跳写入超过100ms，输出警告
                if (elapsedMs > 100)
                {
                    _logService?.Warning("[PLC心跳] 写入耗时较长: {0}ms", elapsedMs);
                }

                if (!heartbeatWriteSuccess)
                {
                    _heartbeatFailCount++;
                    _failedHeartbeats++;
                    _logService?.Warning("[PLC心跳] 写入失败 (耗时: {0}ms, 失败次数: {1}/{2})",
                        elapsedMs, _heartbeatFailCount, MAX_HEARTBEAT_FAIL_COUNT);

                    if (_heartbeatFailCount >= MAX_HEARTBEAT_FAIL_COUNT)
                    {
                        _logService?.Error("[PLC心跳] 连续写入失败，判定为断线");
                        OnConnectionStateChanged(false, "PLC心跳检测失败");
                        return false;
                    }
                    return true;
                }

                // 心跳写入成功，重置失败计数器
                _heartbeatFailCount = 0;

                // 每100次心跳输出一次详细日志
                if (_totalHeartbeats % 100 == 0)
                {
                    _logService?.Verbose("[PLC心跳] 统计 - 总次数: {0}, 失败: {1}, 平均耗时: {2}ms, 最大耗时: {3}ms",
                        _totalHeartbeats, _failedHeartbeats, _avgHeartbeatTime, _maxHeartbeatTime);
                }

                // 读取PLC的通讯中断标志
                bool plcThinkWeAreOffline = CheckCommInterruptFlag();
                if (plcThinkWeAreOffline)
                {
                    _logService?.Warning("[PLC心跳] PLC认为上位机通讯中断 (M1003.7 = 1)");
                }

                // 扩展健康检查（可选）
                if (EnableExtendedHealthCheck)
                {
                    PerformExtendedHealthCheck();
                }

                return true;
            }
            catch (Exception ex)
            {
                _heartbeatFailCount++;
                _failedHeartbeats++;
                _logService?.Error(ex, "[PLC心跳] 检测异常 (失败次数: {FailCount}/{MaxCount})",
                    _heartbeatFailCount, MAX_HEARTBEAT_FAIL_COUNT);

                return _heartbeatFailCount < MAX_HEARTBEAT_FAIL_COUNT;
            }
        }

        private void OnConnectionStateChanged(bool isConnected, string message)
        {
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(
                isConnected,
                message,
                null
            ));
        }

        public ReconnectionConfig GetReconnectionConfig()
        {
            return new ReconnectionConfig
            {
                EnableAutoReconnect = true,
                MaxRetryCount = 10,
                InitialRetryDelayMs = 3000,
                MaxRetryDelayMs = 30000,
                Strategy = ReconnectionStrategy.ExponentialBackoff,
                HeartbeatIntervalMs = 100,  // 500ms心跳
                ConnectionTimeoutMs = 5000,
                AutoConnectOnStartup = true
            };
        }

        #region 私有方法 - 心跳实现

        /// <summary>
        /// ✅ 快速写入心跳（同步方法）
        /// </summary>
        private bool WriteHeartbeat()
        {
            try
            {
                // ✅ 添加超时保护，避免断线时阻塞
                var task = Task.Run(() => _plcService.WriteHeartbitDirect(
                    dataBlock: 3,
                    startAddress: HEARTBEAT_WRITE_BYTE,
                    bitPosition: HEARTBEAT_WRITE_BIT,
                    value: true
                ));

                // 最多等待 300ms
                if (task.Wait(300))
                {
                    return task.Result;
                }

                _logService?.Warning("[PLC心跳] 写入超时 (>300ms)");
                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool CheckCommInterruptFlag()
        {
            try
            {
                return _plcService.ReadBit(
                    dataBlock: 3,
                    startAddress: COMM_INTERRUPT_BYTE,
                    bitPosition: COMM_INTERRUPT_BIT
                );
            }
            catch
            {
                return false;
            }
        }

        private void PerformExtendedHealthCheck()
        {
            try
            {
                bool servoError = _plcService.ReadBit(3, SERVO_ERROR_BYTE, SERVO_ERROR_BIT);
                if (servoError)
                {
                    _logService?.Warning("[PLC健康] ⚠️ 检测到伺服掉线 (M1012.0 = 1)");
                }

                var offlineAxes = new List<string>();
                foreach (var axis in _axisStatusAddresses)
                {
                    bool axisOnline = _plcService.ReadBit(3, axis.ByteAddr, axis.BitPos);
                    if (!axisOnline)
                    {
                        offlineAxes.Add(axis.AxisName);
                    }
                }

                if (offlineAxes.Count > 0)
                {
                    _logService?.Warning("[PLC健康] ⚠️ 以下轴掉线: {OfflineAxes}",
                        string.Join(", ", offlineAxes));
                }
            }
            catch (Exception ex)
            {
                _logService?.Debug("[PLC健康] 扩展检查异常: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// 输出心跳统计信息
        /// </summary>
        private void PrintHeartbeatStatistics()
        {
            if (_totalHeartbeats > 0)
            {
                double successRate = (_totalHeartbeats - _failedHeartbeats) * 100.0 / _totalHeartbeats;
                _logService?.Information(
                    "[PLC心跳] 最终统计 - 总次数: {0}, 失败: {1}, 成功率: {2:F2}%, 平均耗时: {3}ms, 最大耗时: {4}ms",
                    _totalHeartbeats, _failedHeartbeats, successRate, _avgHeartbeatTime, _maxHeartbeatTime);
            }
        }

        #endregion

        private void OnPLCConnectionStateChanged(object sender, PLCConnectionStateChangedEventArgs e)
        {
            if (!e.IsConnected)
            {
                _logService?.Warning("[PLC] 连接已断开");
                _heartbeatFailCount = 0;
            }
            else
            {
                _logService?.Information("[PLC] 连接已恢复");
            }

            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(
                e.IsConnected,
                e.Message,
                null
            ));
        }
    }
}