using Microsoft.Extensions.DependencyInjection;
using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Alarm;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Framework.Services.Logging;
using System;

namespace SeedCut.Framework.Extensions
{
    /// <summary>
    /// 服务集合扩展方法
    /// 用于依赖注入注册
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// 添加SeedCut框架核心服务
        /// </summary>
        public static IServiceCollection AddSeedCutFramework(this IServiceCollection services)
        {
            // 核心服务
            services.AddSingleton<IProductionContext, ProductionContext>();

            return services;
        }

        /// <summary>
        /// 添加日志服务
        /// </summary>
        public static IServiceCollection AddLogService(this IServiceCollection services, string logDirectory = null)
        {
            if (string.IsNullOrEmpty(logDirectory))
            {
                services.AddSingleton<ILogService, SerilogService>();
            }
            else
            {
                services.AddSingleton<ILogService>(sp => new SerilogService(logDirectory));
            }

            return services;
        }

        /// <summary>
        /// 添加报警服务
        /// </summary>
        public static IServiceCollection AddAlarmService(this IServiceCollection services)
        {
            services.AddSingleton<IAlarmService>(sp =>
            {
                var logService = sp.GetRequiredService<ILogService>();
                return new AlarmService(logService);
            });

            return services;
        }

        /// <summary>
        /// 添加所有框架服务
        /// </summary>
        public static IServiceCollection AddSeedCutFullFramework(this IServiceCollection services, Action<FrameworkOptions> configure = null)
        {
            var options = new FrameworkOptions();
            if (configure != null)
            {
                configure(options);
            }

            // 核心服务
            services.AddSeedCutFramework();

            // 日志服务
            if (options.EnableLogService)
            {
                services.AddLogService(options.LogDirectory);
            }

            // 报警服务
            if (options.EnableAlarmService)
            {
                services.AddAlarmService();
            }

            return services;
        }
    }

    /// <summary>
    /// 框架配置选项
    /// </summary>
    public class FrameworkOptions
    {
        /// <summary>
        /// 是否启用日志服务
        /// </summary>
        public bool EnableLogService { get; set; } = true;

        /// <summary>
        /// 日志目录（可选，为空则使用默认目录）
        /// </summary>
        public string LogDirectory { get; set; }

        /// <summary>
        /// 是否启用报警服务
        /// </summary>
        public bool EnableAlarmService { get; set; } = true;
    }
}