using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace SeedCut.Services.Connection
{
    /// <summary>
    /// 连接监控服务 - 集中管理所有设备的连接状态
    /// </summary>
    public class ConnectionMonitorService : IDisposable
    {
        private readonly ILogService _logService;
        private readonly ConcurrentDictionary<string, ConnectionManager> _connectionManagers;
        private readonly ConcurrentDictionary<string, DeviceConnectionStatus> _deviceStatuses;
        private bool _disposed = false;

        /// <summary>
        /// 设备连接状态变化事件（供UI订阅）
        /// </summary>
        public event EventHandler<DeviceConnectionStatusChangedEventArgs> DeviceStatusChanged;

        /// <summary>
        /// 设备重连尝试事件（供UI订阅）
        /// </summary>
        public event EventHandler<ReconnectionAttemptEventArgs> DeviceReconnecting;

        public ConnectionMonitorService(ILogService logService)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _connectionManagers = new ConcurrentDictionary<string, ConnectionManager>();
            _deviceStatuses = new ConcurrentDictionary<string, DeviceConnectionStatus>();

            _logService.Information("连接监控服务已初始化");
        }

        #region 设备注册

        /// <summary>
        /// 注册设备并创建连接管理器
        /// </summary>
        public void RegisterDevice(IConnectableDevice device, ReconnectionConfig config = null)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));

            if (_connectionManagers.ContainsKey(device.DeviceId))
            {
                _logService.Warning("[{DeviceName}] 设备已注册，跳过重复注册", device.DeviceName);
                return;
            }

            // 创建连接管理器
            var manager = new ConnectionManager(device, _logService, config);

            // 订阅事件
            manager.ConnectionStateChanged += OnManagerConnectionStateChanged;
            manager.ReconnectionAttempt += OnManagerReconnectionAttempt;

            // 添加到字典
            _connectionManagers[device.DeviceId] = manager;

            // 初始化设备状态
            var status = new DeviceConnectionStatus
            {
                DeviceId = device.DeviceId,
                DeviceName = device.DeviceName,
                DeviceType = device.DeviceType,
                IsConnected = device.IsConnected,
                LastUpdateTime = DateTime.Now
            };
            _deviceStatuses[device.DeviceId] = status;

            _logService.Information("[{DeviceName}] 设备已注册到连接监控服务", device.DeviceName);
        }

        /// <summary>
        /// 取消注册设备
        /// </summary>
        public void UnregisterDevice(string deviceId)
        {
            if (_connectionManagers.TryRemove(deviceId, out var manager))
            {
                manager.ConnectionStateChanged -= OnManagerConnectionStateChanged;
                manager.ReconnectionAttempt -= OnManagerReconnectionAttempt;
                manager.Dispose();

                _deviceStatuses.TryRemove(deviceId, out _);

                _logService.Information("设备 {DeviceId} 已从连接监控服务注销", deviceId);
            }
        }

        #endregion

        #region 连接控制

        /// <summary>
        /// 连接指定设备
        /// </summary>
        public async System.Threading.Tasks.Task<bool> ConnectDeviceAsync(string deviceId)
        {
            if (_connectionManagers.TryGetValue(deviceId, out var manager))
            {
                return await manager.ConnectAsync();
            }

            _logService.Warning("未找到设备: {DeviceId}", deviceId);
            return false;
        }

        /// <summary>
        /// 断开指定设备
        /// </summary>
        public void DisconnectDevice(string deviceId)
        {
            if (_connectionManagers.TryGetValue(deviceId, out var manager))
            {
                manager.Disconnect();
            }
            else
            {
                _logService.Warning("未找到设备: {DeviceId}", deviceId);
            }
        }

        /// <summary>
        /// 连接所有设备
        /// </summary>
        public async System.Threading.Tasks.Task ConnectAllDevicesAsync()
        {
            var tasks = _connectionManagers.Values
                .Select(manager => manager.ConnectAsync())
                .ToList();

            await System.Threading.Tasks.Task.WhenAll(tasks);
            _logService.Information("已尝试连接所有注册设备");
        }

        /// <summary>
        /// 断开所有设备
        /// </summary>
        public void DisconnectAllDevices()
        {
            foreach (var manager in _connectionManagers.Values)
            {
                manager.Disconnect();
            }
            _logService.Information("已断开所有设备连接");
        }

        #endregion

        #region 状态查询

        /// <summary>
        /// 获取所有设备状态
        /// </summary>
        public List<DeviceConnectionStatus> GetAllDeviceStatuses()
        {
            return _deviceStatuses.Values.ToList();
        }

        /// <summary>
        /// 获取指定设备状态
        /// </summary>
        public DeviceConnectionStatus GetDeviceStatus(string deviceId)
        {
            _deviceStatuses.TryGetValue(deviceId, out var status);
            return status;
        }

        /// <summary>
        /// 获取所有已连接的设备
        /// </summary>
        public List<DeviceConnectionStatus> GetConnectedDevices()
        {
            return _deviceStatuses.Values
                .Where(s => s.IsConnected)
                .ToList();
        }

        /// <summary>
        /// 获取所有未连接的设备
        /// </summary>
        public List<DeviceConnectionStatus> GetDisconnectedDevices()
        {
            return _deviceStatuses.Values
                .Where(s => !s.IsConnected)
                .ToList();
        }

        /// <summary>
        /// 获取正在重连的设备
        /// </summary>
        public List<DeviceConnectionStatus> GetReconnectingDevices()
        {
            return _deviceStatuses.Values
                .Where(s => s.IsReconnecting)
                .ToList();
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 连接管理器状态变化处理
        /// </summary>
        private void OnManagerConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            if (sender is ConnectionManager manager)
            {
                // 找到对应的设备ID
                var deviceId = _connectionManagers.FirstOrDefault(kvp => kvp.Value == manager).Key;

                if (deviceId != null && _deviceStatuses.TryGetValue(deviceId, out var status))
                {
                    // 更新状态
                    status.IsConnected = e.IsConnected;
                    status.LastUpdateTime = DateTime.Now;
                    status.StatusMessage = e.Message;
                    status.IsReconnecting = manager.IsReconnecting;
                    status.RetryCount = manager.CurrentRetryCount;

                    // 触发事件
                    DeviceStatusChanged?.Invoke(this, new DeviceConnectionStatusChangedEventArgs
                    {
                        DeviceId = deviceId,
                        Status = status,
                        IsConnected = e.IsConnected,
                        Message = e.Message
                    });

                    _logService.Debug("[{DeviceName}] 状态更新: {Status}",
                        status.DeviceName, e.IsConnected ? "已连接" : "已断开");
                }
            }
        }

        /// <summary>
        /// 重连尝试处理
        /// </summary>
        private void OnManagerReconnectionAttempt(object sender, ReconnectionAttemptEventArgs e)
        {
            // 找到对应的设备ID
            var deviceId = _connectionManagers.FirstOrDefault(kvp => kvp.Value == sender).Key;

            if (deviceId != null && _deviceStatuses.TryGetValue(deviceId, out var status))
            {
                status.IsReconnecting = !e.Success && !e.IsMaxRetryReached;
                status.RetryCount = e.RetryCount;
                status.LastUpdateTime = DateTime.Now;

                // 转发事件
                DeviceReconnecting?.Invoke(this, e);
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            foreach (var manager in _connectionManagers.Values)
            {
                manager.ConnectionStateChanged -= OnManagerConnectionStateChanged;
                manager.ReconnectionAttempt -= OnManagerReconnectionAttempt;
                manager.Dispose();
            }

            _connectionManagers.Clear();
            _deviceStatuses.Clear();

            _disposed = true;
        }

        #endregion
    }

    /// <summary>
    /// 设备连接状态
    /// </summary>
    public class DeviceConnectionStatus
    {
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public DeviceType DeviceType { get; set; }
        public bool IsConnected { get; set; }
        public bool IsReconnecting { get; set; }
        public int RetryCount { get; set; }
        public string StatusMessage { get; set; }
        public DateTime LastUpdateTime { get; set; }

        /// <summary>
        /// 获取状态显示文本
        /// </summary>
        public string GetStatusText()
        {
            if (IsConnected)
                return "✓ 已连接";

            if (IsReconnecting)
                return $"⟳ 重连中 (第{RetryCount}次)";

            return "✗ 未连接";
        }
    }

    /// <summary>
    /// 设备连接状态变化事件参数
    /// </summary>
    public class DeviceConnectionStatusChangedEventArgs : EventArgs
    {
        public string DeviceId { get; set; }
        public DeviceConnectionStatus Status { get; set; }
        public bool IsConnected { get; set; }
        public string Message { get; set; }
    }
}