using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Devices
{
    /// <summary>
    /// 设备管理器实现
    /// </summary>
    public class DeviceManager : IDeviceManager
    {
        private readonly ConcurrentDictionary<string, IDevice> _devices;
        private bool _disposed;

        #region 属性

        public IReadOnlyDictionary<string, IDevice> Devices => _devices;

        public bool AreAllConnected => _devices.Count > 0 && _devices.Values.All(d => d.IsConnected);

        #endregion

        #region 事件

        public event EventHandler<DeviceConnectionSummary> ConnectionStateChanged;
        public event EventHandler<DeviceConnectionChangedEventArgs> DeviceConnectionChanged;
        public event EventHandler<DeviceErrorEventArgs> DeviceError;

        #endregion

        public DeviceManager()
        {
            _devices = new ConcurrentDictionary<string, IDevice>(StringComparer.OrdinalIgnoreCase);
        }

        #region 注册/注销

        public void Register(IDevice device)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));

            if (string.IsNullOrEmpty(device.DeviceId))
                throw new ArgumentException("设备ID不能为空");

            _devices[device.DeviceId] = device;

            // 订阅设备事件
            device.ConnectionChanged += OnDeviceConnectionChanged;
            device.ErrorOccurred += OnDeviceError;

            System.Diagnostics.Debug.WriteLine($"[DeviceManager] 已注册设备: {device.DeviceId} ({device.DeviceName})");
        }

        public void Register(params IDevice[] devices)
        {
            if (devices == null)
                throw new ArgumentNullException(nameof(devices));

            foreach (var device in devices)
            {
                Register(device);
            }
        }

        public void Unregister(string deviceId)
        {
            if (_devices.TryRemove(deviceId, out var device))
            {
                device.ConnectionChanged -= OnDeviceConnectionChanged;
                device.ErrorOccurred -= OnDeviceError;

                System.Diagnostics.Debug.WriteLine($"[DeviceManager] 已注销设备: {deviceId}");
            }
        }

        private void OnDeviceConnectionChanged(object sender, DeviceConnectionChangedEventArgs e)
        {
            DeviceConnectionChanged?.Invoke(this, e);
            ConnectionStateChanged?.Invoke(this, GetConnectionSummary());
        }

        private void OnDeviceError(object sender, DeviceErrorEventArgs e)
        {
            DeviceError?.Invoke(this, e);
        }

        #endregion

        #region 获取设备

        public T Get<T>(string deviceId) where T : class, IDevice
        {
            if (_devices.TryGetValue(deviceId, out var device))
            {
                return device as T;
            }
            return null;
        }

        public bool TryGet<T>(string deviceId, out T device) where T : class, IDevice
        {
            device = null;
            if (_devices.TryGetValue(deviceId, out var d))
            {
                device = d as T;
                return device != null;
            }
            return false;
        }

        public IEnumerable<T> GetAll<T>() where T : class, IDevice
        {
            return _devices.Values.OfType<T>();
        }

        #endregion

        #region 连接管理

        public bool IsConnected(string deviceId)
        {
            return _devices.TryGetValue(deviceId, out var device) && device.IsConnected;
        }

        public async Task<bool> ConnectAllAsync(CancellationToken ct = default)
        {
            var tasks = _devices.Values.Select(d => d.ConnectAsync(ct));
            var results = await Task.WhenAll(tasks);
            return results.All(r => r);
        }

        public async Task<bool> ConnectAsync(string deviceId, CancellationToken ct = default)
        {
            var device = Get<IDevice>(deviceId);
            if (device == null)
            {
                return false;
            }
            return await device.ConnectAsync(ct);
        }

        public async Task DisconnectAllAsync()
        {
            var tasks = _devices.Values.Select(d => d.DisconnectAsync());
            await Task.WhenAll(tasks);
        }

        #endregion

        #region 状态查询

        public DeviceConnectionSummary GetConnectionSummary()
        {
            var summary = new DeviceConnectionSummary
            {
                TotalCount = _devices.Count,
                ConnectedCount = _devices.Values.Count(d => d.IsConnected),
                ErrorCount = _devices.Values.Count(d => d.ConnectionState == DeviceConnectionState.Error)
            };

            foreach (var kvp in _devices)
            {
                summary.DeviceStates[kvp.Key] = kvp.Value.ConnectionState;
            }

            return summary;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            foreach (var device in _devices.Values)
            {
                try
                {
                    device.ConnectionChanged -= OnDeviceConnectionChanged;
                    device.ErrorOccurred -= OnDeviceError;
                    device.Dispose();
                }
                catch { }
            }

            _devices.Clear();
            _disposed = true;
        }

        #endregion
    }
}