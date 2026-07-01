using Microsoft.Extensions.DependencyInjection;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Services.Connection;
using SeedCut.Services.DeviceAdapter;
using System;
using System.Collections.Generic;

namespace SeedCut.Services.Camera
{
    /// <summary>
    /// 相机服务工厂 - 用于创建和管理多个相机实例
    /// 支持三种级别的相机访问：
    /// 1. HikCameraInstance - 纯相机操作（无重连）
    /// 3. HikCameraDevice - 实现 ICameraDevice（供 DeviceManager 使用，内置重连）
    /// </summary>
    public class HikCameraServiceFactory : IDisposable
    {
        private readonly HikCameraSDKManager _sdkManager;
        private readonly HikCameraConfig _config;
        private readonly ILogService _logService;

        // 各层级实例缓存
        private readonly Dictionary<string, HikCameraInstance> _instances;
        private readonly Dictionary<string, HikCameraConnectAdapter> _adapters;

        private bool _disposed;

        #region 属性

        /// <summary>
        /// 全局配置
        /// </summary>
        public HikCameraConfig Config => _config;

        /// <summary>
        /// SDK管理器
        /// </summary>
        public HikCameraSDKManager SDKManager => _sdkManager;

        /// <summary>
        /// 已创建的相机实例（底层）
        /// </summary>
        public IReadOnlyDictionary<string, HikCameraInstance> Instances => _instances;

        /// <summary>
        /// 已创建的相机适配器
        /// </summary>
        public IReadOnlyDictionary<string, HikCameraConnectAdapter> Adapters => _adapters;

        

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建相机服务工厂
        /// </summary>
        public HikCameraServiceFactory()
            : this(null)
        {
        }

        /// <summary>
        /// 创建相机服务工厂（带日志服务）
        /// </summary>
        /// <param name="logService">日志服务（可选，用于断线重连日志）</param>
        public HikCameraServiceFactory(ILogService logService)
        {
            _sdkManager = HikCameraSDKManager.Instance;
            _config = HikCameraConfig.Load();
            _logService = logService;

            _instances = new Dictionary<string, HikCameraInstance>(StringComparer.OrdinalIgnoreCase);
            _adapters = new Dictionary<string, HikCameraConnectAdapter>(StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region SDK 初始化

        /// <summary>
        /// 初始化SDK
        /// </summary>
        public bool InitializeSDK()
        {
            return _sdkManager.InitializeSDK();
        }

        #endregion

        #region 创建相机实例（底层）

        /// <summary>
        /// 创建或获取相机实例（HikCameraInstance）
        /// 用于直接操作相机，不带重连功能
        /// </summary>
        /// <param name="cameraId">相机ID（如 "Camera_Disk"）</param>
        /// <returns>相机实例</returns>
        public HikCameraInstance CreateInstance(string cameraId)
        {
            if (string.IsNullOrEmpty(cameraId))
                throw new ArgumentNullException(nameof(cameraId));

            // 检查缓存
            if (_instances.TryGetValue(cameraId, out var existing))
            {
                return existing;
            }

            // 从配置获取
            var config = _config.GetCameraConfig(cameraId);
            if (config == null)
            {
                throw new ArgumentException(string.Format("未找到相机配置: {0}", cameraId));
            }

            // 创建实例
            var instance = _sdkManager.CreateCameraInstance(config);
            _instances[cameraId] = instance;

            return instance;
        }

        /// <summary>
        /// 获取相机实例
        /// </summary>
        public HikCameraInstance GetInstance(string cameraId)
        {
            if (_instances.TryGetValue(cameraId, out var instance))
            {
                return instance;
            }
            return null;
        }

        #endregion

        #region 创建相机适配器

        /// <summary>
        /// 创建或获取相机适配器（HikCameraDeviceAdapter）
        /// 实现 IConnectableDevice，可用于 ConnectionManager/ConnectionMonitorService
        /// </summary>
        /// <param name="cameraId">相机ID</param>
        /// <returns>相机适配器</returns>
        public HikCameraConnectAdapter CreateAdapter(string cameraId)
        {
            if (string.IsNullOrEmpty(cameraId))
                throw new ArgumentNullException(nameof(cameraId));

            // 检查缓存
            if (_adapters.TryGetValue(cameraId, out var existing))
            {
                return existing;
            }

            // 先确保 Instance 存在
            var instance = CreateInstance(cameraId);

            // 创建适配器
            var adapter = new HikCameraConnectAdapter(instance);
            _adapters[cameraId] = adapter;

            return adapter;
        }

        /// <summary>
        /// 获取相机适配器
        /// </summary>
        public HikCameraConnectAdapter GetAdapter(string cameraId)
        {
            if (_adapters.TryGetValue(cameraId, out var adapter))
            {
                return adapter;
            }
            return null;
        }

        #endregion

        #region 创建相机设备（推荐使用）

        

        

        

        #endregion

        #region 批量创建

        /// <summary>
        /// 创建所有配置中的相机实例（底层）
        /// </summary>
        public void CreateAllInstances()
        {
            if (_config.Cameras == null)
                return;

            foreach (var cameraConfig in _config.Cameras)
            {
                if (!string.IsNullOrEmpty(cameraConfig.CameraId))
                {
                    CreateInstance(cameraConfig.CameraId);
                }
            }
        }

        

        #endregion

        #region 兼容方法（保持向后兼容）

        
        

     

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            

            // Adapter 不需要显式释放（无 IDisposable）
            _adapters.Clear();

            // Instance 由 SDKManager 管理，这里只清空引用
            _instances.Clear();

            _disposed = true;
        }

        #endregion
    }

    /// <summary>
    /// DI 扩展方法
    /// </summary>
    public static class HikCameraServiceExtensions
    {
        /// <summary>
        /// 注册海康相机多实例服务
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddHikCameraServices(this IServiceCollection services)
        {
            // 1. 注册SDK管理器（单例）
            services.AddSingleton(sp => HikCameraSDKManager.Instance);

            // 2. 注册配置（单例）
            services.AddSingleton(sp => HikCameraConfig.Load());

            // 3. 注册工厂（单例，注入日志服务）
            services.AddSingleton<HikCameraServiceFactory>(sp =>
            {
                var logService = sp.GetService<ILogService>();
                return new HikCameraServiceFactory(logService);
            });

            return services;
        }

   

        
    }
}