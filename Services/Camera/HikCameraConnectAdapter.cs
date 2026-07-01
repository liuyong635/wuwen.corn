using SeedCut.Framework.Core;
using SeedCut.Services.Connection;
using System;

namespace SeedCut.Services.Camera
{
    /// <summary>
    /// 海康相机设备适配器 - 将 HikCameraInstance 适配到连接监控系统
    /// 实现 IConnectableDevice 接口，支持断线重连
    /// </summary>
    public class HikCameraConnectAdapter : IConnectableDevice
    {
        private readonly HikCameraInstance _cameraInstance;
        private readonly ReconnectionConfig _reconnectionConfig;

        #region IConnectableDevice 属性

        public string DeviceId => _cameraInstance.CameraId;

        public string DeviceName => _cameraInstance.DisplayName;

        public DeviceType DeviceType => DeviceType.Camera;

        public bool IsConnected => _cameraInstance.IsConnected;

        #endregion

        #region 事件

        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建相机适配器
        /// </summary>
        /// <param name="cameraInstance">相机实例</param>
        public HikCameraConnectAdapter(HikCameraInstance cameraInstance)
        {
            _cameraInstance = cameraInstance ?? throw new ArgumentNullException(nameof(cameraInstance));

            // 根据相机配置创建重连配置
            _reconnectionConfig = CreateReconnectionConfig(_cameraInstance.Config);

            // 订阅相机连接状态变化事件
            _cameraInstance.InternalConnectionStateChanged += OnCameraConnectionStateChanged;
        }

        #endregion

        #region IConnectableDevice 方法

        /// <summary>
        /// 连接相机
        /// </summary>
        public bool Connect()
        {
            try
            {
                // 使用自动连接（根据配置的序列号或用户自定义名称）
                var task = _cameraInstance.AutoConnectAsync();
                task.Wait();
                return task.Result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{DeviceName}] 连接失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 断开相机
        /// </summary>
        public void Disconnect()
        {
            try
            {
                _cameraInstance.DisconnectCamera();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{DeviceName}] 断开失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 心跳检测
        /// </summary>
        public bool CheckConnection()
        {
            return _cameraInstance.CheckConnection();
        }

        /// <summary>
        /// 获取重连配置
        /// </summary>
        public ReconnectionConfig GetReconnectionConfig()
        {
            return _reconnectionConfig;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 相机连接状态变化处理
        /// </summary>
        private void OnCameraConnectionStateChanged(object sender, bool isConnected)
        {
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(
                isConnected,
                isConnected ? $"{DeviceName} 已连接" : $"{DeviceName} 已断开"
            ));
        }

        /// <summary>
        /// 根据相机配置创建重连配置
        /// </summary>
        private ReconnectionConfig CreateReconnectionConfig(CameraInstanceConfig config)
        {
            return new ReconnectionConfig
            {
                EnableAutoReconnect = config?.EnableAutoReconnect ?? true,
                MaxRetryCount = config?.MaxReconnectCount ?? 10,
                InitialRetryDelayMs = 3000,
                MaxRetryDelayMs = 30000,
                Strategy = ReconnectionStrategy.ExponentialBackoff,
                HeartbeatIntervalMs = config?.HeartbeatIntervalMs ?? 1000,
                ConnectionTimeoutMs = 5000,
                AutoConnectOnStartup = config?.AutoConnectOnStartup ?? false
            };
        }

        #endregion

        #region 静态工厂方法

        /// <summary>
        /// 为相机实例创建适配器
        /// </summary>
        public static HikCameraConnectAdapter CreateAdapter(HikCameraInstance cameraInstance)
        {
            return new HikCameraConnectAdapter(cameraInstance);
        }

        #endregion
    }
}