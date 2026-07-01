using SeedCut.Framework.Core;
using SeedCut.Services.Connection;
using System;

namespace SeedCut.Services
{
    /// <summary>
    /// 激光器服务适配器 - 将LaserService适配到连接监控系统
    /// </summary>
    public class LaserServiceConnectAdapter : IConnectableDevice
    {
        private readonly ILaserService _laserService;

        public string DeviceId => "Laser_Main";
        public string DeviceName => "激光打标机";
        public DeviceType DeviceType => DeviceType.Other;

        public bool IsConnected => _laserService.IsConnected;

        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

        public LaserServiceConnectAdapter(ILaserService laserService)
        {
            _laserService = laserService ?? throw new ArgumentNullException(nameof(laserService));

            // 订阅激光器服务的连接状态事件
            _laserService.ConnectionChanged += OnLaserConnectionChanged;
        }

        private void OnLaserConnectionChanged(object sender, bool isConnected)
        {
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(
                isConnected,
                isConnected ? "激光器已连接" : "激光器已断开"
            ));
        }

        public bool Connect()
        {
            try
            {
                // 激光器是被动连接（上位机启动Server等待），所以这里只启动服务器
                var task = _laserService.StartServerAsync();
                task.Wait(); // 同步等待
                return task.Result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"激光器连接失败: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                var task = _laserService.DisconnectAsync();
                task.Wait();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"激光器断开失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 心跳检测：检查TCP连接是否仍然有效
        /// </summary>
        public bool CheckConnection()
        {
            // 激光器的连接检测依赖于TCP连接状态
            // LaserService内部已通过NetworkStream.CanRead检测
            return _laserService.IsConnected;
        }

        public ReconnectionConfig GetReconnectionConfig()
        {
            // 激光器专用重连配置
            return new ReconnectionConfig
            {
                EnableAutoReconnect = false,  // 激光器是主动连接，不需要上位机自动重连
                MaxRetryCount = 0,
                InitialRetryDelayMs = 0,
                MaxRetryDelayMs = 0,
                Strategy = ReconnectionStrategy.Fixed,
                HeartbeatIntervalMs = 1000,  // 心跳检测间隔1秒
                ConnectionTimeoutMs = 10000,
                AutoConnectOnStartup = true  // 启动时自动开启Server
            };
        }
    }
}