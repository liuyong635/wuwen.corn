using Microsoft.Extensions.DependencyInjection;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Models;
using SeedCut.Services.DeviceAdapter;
using SeedCut.ViewModels;
using System;

namespace SeedCut.Services
{
    /// <summary>
    /// HM激光器服务的DI注册扩展方法
    /// </summary>
    public static class HM_LaserServiceExtensions
    {
        /// <summary>
        /// 注册HM激光器服务（替换TCP方案）
        /// 
        /// 使用方式：
        /// services.AddHM_LaserService();
        /// </summary>
        public static IServiceCollection AddHM_LaserService(this IServiceCollection services)
        {
            // 注册HM激光器服务
            services.AddSingleton<HM_LaserService>();

            // 注册设备适配器（实现 ILaserDevice 接口）
            services.AddSingleton<ILaserDevice>(sp =>
            {
                var service = sp.GetRequiredService<HM_LaserService>();
                var logService = sp.GetRequiredService<ILogService>();
                return new HM_LaserDeviceAdapter(service, logService);
            });

            // 注册调试界面ViewModel
            services.AddTransient<HM_LaserDebugViewModel>();

            return services;
        }

        /// <summary>
        /// 注册HM激光器服务（使用自定义配置）
        /// 
        /// 使用方式：
        /// services.AddHM_LaserService(config => {
        ///     config.IpAddress = "172.18.34.100";
        ///     config.LaserPower = 80;
        /// });
        /// </summary>
        public static IServiceCollection AddHM_LaserService(
            this IServiceCollection services,
            Action<HM_LaserConfig> configureOptions)
        {
            // 创建并配置
            var config = HM_LaserConfig.CreateDefault();
            configureOptions?.Invoke(config);

            // 注册配置
            services.AddSingleton(config);

            // 注册HM激光器服务（使用配置）
            services.AddSingleton<HM_LaserService>(sp =>
            {
                var logService = sp.GetRequiredService<ILogService>();
                var cfg = sp.GetRequiredService<HM_LaserConfig>();
                return new HM_LaserService(logService, cfg);
            });

            // 注册设备适配器
            services.AddSingleton<ILaserDevice>(sp =>
            {
                var service = sp.GetRequiredService<HM_LaserService>();
                var logService = sp.GetRequiredService<ILogService>();
                return new HM_LaserDeviceAdapter(service, logService);
            });

            // 注册调试界面ViewModel
            services.AddTransient<HM_LaserDebugViewModel>();

            return services;
        }

        
    }
}
