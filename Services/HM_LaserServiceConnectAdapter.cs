using SeedCut.Framework.Core;
using SeedCut.Services.Connection;
using SeedCut.Services.HM;
using System;

namespace SeedCut.Services
{
    /// <summary>
    /// HM激光器服务适配器 - 将HM_LaserService适配到连接监控系统
    /// 
    /// 架构说明：
    /// - 实现 IConnectableDevice 接口，接入 ConnectionManager 断线重连机制
    /// - HM方案是Client模式（主动连接控制卡），需要自动重连
    /// - 通过查询控制卡工作状态实现心跳检测
    /// 
    /// 与TCP方案的区别：
    /// ┌──────────────┬─────────────────────┬─────────────────────┐
    /// │              │ TCP激光器            │ HM激光器(GMC)        │
    /// ├──────────────┼─────────────────────┼─────────────────────┤
    /// │ 连接模式      │ Server（被动等待）   │ Client（主动连接）   │
    /// │ 自动重连      │ 不需要              │ 需要                │
    /// │ 心跳检测      │ TCP连接状态         │ 查询工作状态         │
    /// │ 网络协议      │ TCP Socket          │ 以太网 + 厂商协议    │
    /// └──────────────┴─────────────────────┴─────────────────────┘
    /// </summary>
    public class HM_LaserServiceConnectAdapter : IConnectableDevice
    {
        #region 私有字段

        private readonly HM_LaserService _service;
        private bool _disposed;

        #endregion

        #region IConnectableDevice 属性

        /// <summary>
        /// 设备唯一标识
        /// </summary>
        public string DeviceId => "HM_Laser";

        /// <summary>
        /// 设备显示名称
        /// </summary>
        public string DeviceName => "HM激光器(GMC)";

        /// <summary>
        /// 设备类型
        /// </summary>
        public DeviceType DeviceType => DeviceType.Laser;

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _service?.IsConnected ?? false;

        #endregion

        #region 事件

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建HM激光器连接适配器
        /// </summary>
        /// <param name="service">HM激光器服务</param>
        public HM_LaserServiceConnectAdapter(HM_LaserService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));

            // 订阅服务的连接状态事件
            _service.ConnectionChanged += OnServiceConnectionChanged;

            System.Diagnostics.Debug.WriteLine("[HM_LaserConnectAdapter] 适配器已创建");
        }

        #endregion

        #region IConnectableDevice 方法

        /// <summary>
        /// 连接设备（同步方法，供ConnectionManager调用）
        /// </summary>
        /// <returns>是否连接成功</returns>
        public bool Connect()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[HM_LaserConnectAdapter] 开始连接...");

                // HM方案是主动连接控制卡
                var task = _service.ConnectAsync();
                
                // 同步等待（ConnectionManager要求同步方法）
                // 使用 GetAwaiter().GetResult() 避免 AggregateException 包装
                var result = task.GetAwaiter().GetResult();

                System.Diagnostics.Debug.WriteLine(
                    "[HM_LaserConnectAdapter] 连接{0}", result ? "成功" : "失败");

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[HM_LaserConnectAdapter] 连接异常: {0}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[HM_LaserConnectAdapter] 断开连接...");
                _service.Disconnect();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[HM_LaserConnectAdapter] 断开异常: {0}", ex.Message);
            }
        }

        /// <summary>
        /// 心跳检测：检查与控制卡的连接是否有效
        /// 
        /// 实现方式：
        /// 1. 首先检查服务层的连接状态
        /// 2. 然后通过查询工作状态验证通讯是否正常
        /// 
        /// 注意：
        /// - 此方法会被 ConnectionManager 的心跳线程频繁调用
        /// - 需要快速返回，避免阻塞
        /// - 查询工作状态是轻量级操作，不会影响打标
        /// </summary>
        /// <returns>设备是否在线</returns>
        public bool CheckConnection()
        {
            // 快速检查：服务层状态
            if (!_service.IsConnected)
            {
                return false;
            }

            try
            {
                // 深度检查：查询控制卡工作状态
                // HM_GetWorkStatus 是轻量级查询，响应很快
                var status = _service.GetWorkStatus();

                // Unknown 表示通讯失败
                return status != HM_WorkStatus.Unknown;
            }
            catch
            {
                // 查询异常说明连接已断开
                return false;
            }
        }

        /// <summary>
        /// 获取重连配置
        /// </summary>
        /// <returns>HM激光器专用重连配置</returns>
        public ReconnectionConfig GetReconnectionConfig()
        {
            return ReconnectionConfig.CreateDefaultForHM_Laser();
        }

        #endregion

        #region 事件处理

        /// <summary>
        /// 服务连接状态变化处理
        /// </summary>
        private void OnServiceConnectionChanged(object sender, bool isConnected)
        {
            System.Diagnostics.Debug.WriteLine(
                "[HM_LaserConnectAdapter] 连接状态变化: {0}", isConnected ? "已连接" : "已断开");

            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(
                isConnected,
                isConnected ? "HM激光器已连接" : "HM激光器已断开"
            ));
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            // 取消订阅事件
            if (_service != null)
            {
                _service.ConnectionChanged -= OnServiceConnectionChanged;
            }

            _disposed = true;
            System.Diagnostics.Debug.WriteLine("[HM_LaserConnectAdapter] 适配器已释放");
        }

        #endregion
    }
}
