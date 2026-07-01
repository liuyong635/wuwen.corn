using MvCameraControl;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedCut.Services.Camera
{
    /// <summary>
    /// 海康相机SDK管理器（单例）
    /// 职责：
    /// 1. SDK 初始化/释放（全局只做一次）
    /// 2. 设备枚举
    /// 3. 创建相机实例
    /// </summary>
    public class HikCameraSDKManager : IDisposable
    {
        private static readonly Lazy<HikCameraSDKManager> _instance =
            new Lazy<HikCameraSDKManager>(() => new HikCameraSDKManager());

        private bool _isInitialized;
        private bool _disposed;
        private List<IDeviceInfo> _deviceInfoList;
        private readonly object _lockObject = new object();

        // 已创建的相机实例（用于管理生命周期）
        private readonly Dictionary<string, HikCameraInstance> _cameraInstances;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static HikCameraSDKManager Instance => _instance.Value;

        /// <summary>
        /// SDK是否已初始化
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// 枚举到的设备列表
        /// </summary>
        public IReadOnlyList<IDeviceInfo> DeviceInfoList => _deviceInfoList?.AsReadOnly();

        /// <summary>
        /// 已创建的相机实例
        /// </summary>
        public IReadOnlyDictionary<string, HikCameraInstance> CameraInstances => _cameraInstances;

        /// <summary>
        /// SDK初始化/释放事件
        /// </summary>
        public event EventHandler<bool> SDKStateChanged;

        /// <summary>
        /// 设备列表更新事件
        /// </summary>
        public event EventHandler DeviceListUpdated;

        private HikCameraSDKManager()
        {
            _deviceInfoList = new List<IDeviceInfo>();
            _cameraInstances = new Dictionary<string, HikCameraInstance>(StringComparer.OrdinalIgnoreCase);
        }

        #region SDK管理

        /// <summary>
        /// 初始化SDK
        /// </summary>
        /// <returns>是否成功</returns>
        public bool InitializeSDK()
        {
            lock (_lockObject)
            {
                if (_isInitialized)
                    return true;

                try
                {
                    SDKSystem.Initialize();
                    _isInitialized = true;
                    System.Diagnostics.Debug.WriteLine("[HikCameraSDK] SDK初始化成功");
                    SDKStateChanged?.Invoke(this, true);
                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HikCameraSDK] SDK初始化失败: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// 释放SDK
        /// </summary>
        public void FinalizeSDK()
        {
            lock (_lockObject)
            {
                if (!_isInitialized)
                    return;

                try
                {
                    // 先断开所有相机
                    foreach (var camera in _cameraInstances.Values.ToList())
                    {
                        try
                        {
                            camera.DisconnectCamera();
                            camera.Dispose();
                        }
                        catch { }
                    }
                    _cameraInstances.Clear();

                    SDKSystem.Finalize();
                    _isInitialized = false;
                    System.Diagnostics.Debug.WriteLine("[HikCameraSDK] SDK已释放");
                    SDKStateChanged?.Invoke(this, false);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HikCameraSDK] SDK释放失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 检查SDK是否可用（静态方法，供启动检查使用）
        /// </summary>
        public static bool CheckSDKAvailable()
        {
            try
            {
                // 尝试初始化SDK检查是否可用
                SDKSystem.Initialize();
                SDKSystem.Finalize();
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 设备枚举

        /// <summary>
        /// 枚举所有相机设备
        /// </summary>
        /// <returns>设备信息列表</returns>
        public List<IDeviceInfo> EnumerateDevices()
        {
            lock (_lockObject)
            {
                if (!_isInitialized)
                {
                    System.Diagnostics.Debug.WriteLine("[HikCameraSDK] SDK未初始化，无法枚举设备");
                    return new List<IDeviceInfo>();
                }

                try
                {
                    DeviceTLayerType enumTLayerType =
                        DeviceTLayerType.MvGigEDevice |
                        DeviceTLayerType.MvUsbDevice |
                        DeviceTLayerType.MvGenTLGigEDevice |
                        DeviceTLayerType.MvGenTLCXPDevice |
                        DeviceTLayerType.MvGenTLCameraLinkDevice |
                        DeviceTLayerType.MvGenTLXoFDevice;

                    int nRet = DeviceEnumerator.EnumDevices(enumTLayerType, out _deviceInfoList);

                    if (nRet != MvError.MV_OK)
                    {
                        System.Diagnostics.Debug.WriteLine($"[HikCameraSDK] 枚举设备失败: 0x{nRet:X8}");
                        _deviceInfoList = new List<IDeviceInfo>();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[HikCameraSDK] 检测到 {_deviceInfoList?.Count ?? 0} 个相机");
                    }

                    DeviceListUpdated?.Invoke(this, EventArgs.Empty);
                    return _deviceInfoList ?? new List<IDeviceInfo>();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HikCameraSDK] 枚举设备异常: {ex.Message}");
                    _deviceInfoList = new List<IDeviceInfo>();
                    return _deviceInfoList;
                }
            }
        }

        /// <summary>
        /// 获取设备显示名称列表
        /// </summary>
        public List<string> GetDeviceDisplayNames()
        {
            var list = new List<string>();

            if (_deviceInfoList == null || _deviceInfoList.Count == 0)
            {
                list.Add("未检测到相机");
                return list;
            }

            for (int i = 0; i < _deviceInfoList.Count; i++)
            {
                IDeviceInfo deviceInfo = _deviceInfoList[i];
                string displayName;

                if (!string.IsNullOrEmpty(deviceInfo.UserDefinedName))
                {
                    displayName = $"[{i}] {deviceInfo.TLayerType}: {deviceInfo.UserDefinedName} ({deviceInfo.SerialNumber})";
                }
                else
                {
                    displayName = $"[{i}] {deviceInfo.TLayerType}: {deviceInfo.ManufacturerName} {deviceInfo.ModelName} ({deviceInfo.SerialNumber})";
                }

                list.Add(displayName);
            }

            return list;
        }

        /// <summary>
        /// 根据索引获取设备信息
        /// </summary>
        public IDeviceInfo GetDeviceInfo(int index)
        {
            if (_deviceInfoList == null || index < 0 || index >= _deviceInfoList.Count)
                return null;
            return _deviceInfoList[index];
        }

        /// <summary>
        /// 根据序列号查找设备信息
        /// </summary>
        public IDeviceInfo FindDeviceBySerialNumber(string serialNumber)
        {
            if (string.IsNullOrEmpty(serialNumber) || _deviceInfoList == null)
                return null;

            return _deviceInfoList.FirstOrDefault(d =>
                string.Equals(d.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 根据用户自定义名称查找设备信息
        /// </summary>
        public IDeviceInfo FindDeviceByUserDefinedName(string userDefinedName)
        {
            if (string.IsNullOrEmpty(userDefinedName) || _deviceInfoList == null)
                return null;

            return _deviceInfoList.FirstOrDefault(d =>
                string.Equals(d.UserDefinedName, userDefinedName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 根据配置查找设备信息（优先序列号，其次用户自定义名称）
        /// </summary>
        public IDeviceInfo FindDeviceByConfig(CameraInstanceConfig config)
        {
            if (config == null)
                return null;

            // 优先使用序列号匹配
            if (!string.IsNullOrEmpty(config.SerialNumber))
            {
                var device = FindDeviceBySerialNumber(config.SerialNumber);
                if (device != null)
                    return device;
            }

            // 其次使用用户自定义名称匹配
            if (!string.IsNullOrEmpty(config.UserDefinedName))
            {
                var device = FindDeviceByUserDefinedName(config.UserDefinedName);
                if (device != null)
                    return device;
            }

            return null;
        }

        #endregion

        #region 相机实例管理

        /// <summary>
        /// 创建相机实例
        /// </summary>
        /// <param name="config">相机配置</param>
        /// <returns>相机实例</returns>
        public HikCameraInstance CreateCameraInstance(CameraInstanceConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (string.IsNullOrEmpty(config.CameraId))
                throw new ArgumentException("CameraId 不能为空");

            lock (_lockObject)
            {
                // 检查是否已存在
                if (_cameraInstances.TryGetValue(config.CameraId, out var existing))
                {
                    System.Diagnostics.Debug.WriteLine($"[HikCameraSDK] 返回已存在的相机实例: {config.CameraId}");
                    return existing;
                }

                // 创建新实例
                var instance = new HikCameraInstance(config, this);
                _cameraInstances[config.CameraId] = instance;

                System.Diagnostics.Debug.WriteLine($"[HikCameraSDK] 创建相机实例: {config.CameraId} ({config.DisplayName})");
                return instance;
            }
        }

        /// <summary>
        /// 获取相机实例
        /// </summary>
        public HikCameraInstance GetCameraInstance(string cameraId)
        {
            if (string.IsNullOrEmpty(cameraId))
                return null;

            lock (_lockObject)
            {
                _cameraInstances.TryGetValue(cameraId, out var instance);
                return instance;
            }
        }

        /// <summary>
        /// 移除相机实例
        /// </summary>
        public void RemoveCameraInstance(string cameraId)
        {
            lock (_lockObject)
            {
                if (_cameraInstances.TryGetValue(cameraId, out var instance))
                {
                    instance.Dispose();
                    _cameraInstances.Remove(cameraId);
                    System.Diagnostics.Debug.WriteLine($"[HikCameraSDK] 移除相机实例: {cameraId}");
                }
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            FinalizeSDK();
            _disposed = true;
        }

        #endregion
    }
}