using System;

namespace SeedCut.Services.Connection
{
    /// <summary>
    /// 重连配置
    /// </summary>
    public class ReconnectionConfig
    {
        /// <summary>
        /// 是否启用自动重连
        /// </summary>
        public bool EnableAutoReconnect { get; set; } = true;

        /// <summary>
        /// 最大重试次数（0表示无限重试）
        /// </summary>
        public int MaxRetryCount { get; set; } = 0;

        /// <summary>
        /// 初始重连间隔（毫秒）
        /// </summary>
        public int InitialRetryDelayMs { get; set; } = 2000;

        /// <summary>
        /// 最大重连间隔（毫秒）
        /// </summary>
        public int MaxRetryDelayMs { get; set; } = 30000;

        /// <summary>
        /// 重连策略
        /// </summary>
        public ReconnectionStrategy Strategy { get; set; } = ReconnectionStrategy.ExponentialBackoff;

        /// <summary>
        /// 心跳检测间隔（毫秒）
        /// </summary>
        public int HeartbeatIntervalMs { get; set; } = 500;

        /// <summary>
        /// 连接超时时间（毫秒）
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 是否在应用启动时自动连接
        /// </summary>
        public bool AutoConnectOnStartup { get; set; } = false;

        /// <summary>
        /// 创建默认PLC配置
        /// </summary>
        public static ReconnectionConfig CreateDefaultForPLC()
        {
            return new ReconnectionConfig
            {
                EnableAutoReconnect = true,
                MaxRetryCount = 0,                    // ✅ 保持：无限重试
                InitialRetryDelayMs = 1000,           // ✅ 保持：1秒后重试
                MaxRetryDelayMs = 3000,               // ✅ 保持：最大3秒
                Strategy = ReconnectionStrategy.Fixed, // ⬅️ 改为固定间隔！
                HeartbeatIntervalMs = 500,            // ✅ 保持：500ms心跳
                ConnectionTimeoutMs = 1000,           // ⬅️ 改为1秒！
                AutoConnectOnStartup = true
            };
        }

        /// <summary>
        /// 创建默认机器人配置
        /// </summary>
        public static ReconnectionConfig CreateDefaultForRobot()
        {
            return new ReconnectionConfig
            {
                EnableAutoReconnect = true,
                MaxRetryCount = 0,
                InitialRetryDelayMs = 5000,
                MaxRetryDelayMs = 60000,
                Strategy = ReconnectionStrategy.LinearBackoff,
                HeartbeatIntervalMs = 500,
                ConnectionTimeoutMs = 10000,
                AutoConnectOnStartup = false
            };
        }

        /// <summary>
        /// 创建默认Modbus设备配置
        /// </summary>
        public static ReconnectionConfig CreateDefaultForModbus()
        {
            return new ReconnectionConfig
            {
                EnableAutoReconnect = true,
                MaxRetryCount = 0,
                InitialRetryDelayMs = 2000,
                MaxRetryDelayMs = 20000,
                Strategy = ReconnectionStrategy.ExponentialBackoff,
                HeartbeatIntervalMs = 500,
                ConnectionTimeoutMs = 3000,
                AutoConnectOnStartup = true
            };
        }

        /// <summary>
        /// 创建默认HM激光器配置（GMC控制卡）
        /// 
        /// 配置说明：
        /// - HM激光器是Client模式，需要主动连接控制卡
        /// - 网络断开后需要自动重连
        /// - 心跳检测通过查询工作状态实现
        /// </summary>
        public static ReconnectionConfig CreateDefaultForHM_Laser()
        {
            return new ReconnectionConfig
            {
                EnableAutoReconnect = true,       // ★ 启用自动重连（与TCP激光器不同）
                MaxRetryCount = 0,                // 无限重试
                InitialRetryDelayMs = 2000,       // 2秒后首次重试
                MaxRetryDelayMs = 10000,          // 最大10秒间隔
                Strategy = ReconnectionStrategy.ExponentialBackoff,  // 指数退避
                HeartbeatIntervalMs = 1000,       // 1秒心跳（查询工作状态）
                ConnectionTimeoutMs = 5000,       // 5秒连接超时
                AutoConnectOnStartup = false      // 不自动连接（由调试界面控制）
            };
        }
    }

    /// <summary>
    /// 重连策略枚举
    /// </summary>
    public enum ReconnectionStrategy
    {
        /// <summary>
        /// 固定间隔重试
        /// </summary>
        Fixed,

        /// <summary>
        /// 线性递增间隔（每次增加固定时间）
        /// </summary>
        LinearBackoff,

        /// <summary>
        /// 指数退避（每次翻倍）
        /// </summary>
        ExponentialBackoff
    }

    /// <summary>
    /// 重连策略计算器
    /// </summary>
    public static class ReconnectionStrategyCalculator
    {
        /// <summary>
        /// 计算下次重连延迟时间
        /// </summary>
        public static int CalculateNextDelay(ReconnectionConfig config, int retryCount)
        {
            int delay;

            switch (config.Strategy)
            {
                case ReconnectionStrategy.Fixed:
                    delay = config.InitialRetryDelayMs;
                    break;

                case ReconnectionStrategy.LinearBackoff:
                    // 线性增长：initialDelay + (retryCount * initialDelay)
                    delay = config.InitialRetryDelayMs * (retryCount + 1);
                    break;

                case ReconnectionStrategy.ExponentialBackoff:
                    // 指数增长：initialD
                    // elay * (2 ^ retryCount)
                    delay = config.InitialRetryDelayMs * (int)Math.Pow(2, retryCount);
                    break;

                default:
                    delay = config.InitialRetryDelayMs;
                    break;
            }

            // 限制在最大延迟范围内
            return Math.Min(delay, config.MaxRetryDelayMs);
        }
    }
}