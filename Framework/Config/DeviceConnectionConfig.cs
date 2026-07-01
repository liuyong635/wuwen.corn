using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeedCut.Framework.Config
{
    /// <summary>
    /// 连接模式枚举
    /// </summary>
    public enum ConnectionMode
    {
        /// <summary>
        /// 生产模式：强制连接所有启用的设备，关键设备失败则阻止启动
        /// </summary>
        Production,

        /// <summary>
        /// 调试模式：跳过设备连接检查，允许无设备启动
        /// </summary>
        Debug,

        /// <summary>
        /// 选择性模式：只连接启用的设备，忽略关键性标记
        /// </summary>
        Selective
    }

    /// <summary>
    /// 单个设备的连接配置
    /// </summary>
    public class DeviceConnectionItem
    {
        /// <summary>
        /// 设备ID（如 "PLC", "Robot", "Camera_Disk"）
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// 设备显示名称
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 是否启用该设备
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// 是否为关键设备（生产模式下，关键设备连接失败会阻止启动）
        /// </summary>
        public bool IsCritical { get; set; } = false;

        /// <summary>
        /// 在调试模式下是否跳过检查（true=跳过检查直接通过）
        /// </summary>
        public bool SkipInDebug { get; set; } = true;

        /// <summary>
        /// 连接超时时间（毫秒）
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// 是否在Loading阶段自动连接
        /// </summary>
        public bool AutoConnectOnStartup { get; set; } = true;
    }

    /// <summary>
    /// 调试模式设置
    /// </summary>
    public class DebugSettings
    {
        /// <summary>
        /// 是否跳过Loading页面（直接进入主界面）
        /// </summary>
        public bool SkipLoadingPage { get; set; } = false;

        /// <summary>
        /// 是否自动通过所有关键检查
        /// </summary>
        public bool AutoPassCriticalCheck { get; set; } = true;

        /// <summary>
        /// 调试模式下的默认连接超时（毫秒）
        /// </summary>
        public int ConnectionTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// 是否显示调试模式警告条
        /// </summary>
        public bool ShowDebugWarning { get; set; } = true;
    }

    /// <summary>
    /// 设备连接配置（主配置类）
    /// </summary>
    public class DeviceConnectionConfig
    {
        private static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Config",
            "DeviceConnectionConfig.json"
        );

        /// <summary>
        /// 配置的连接模式（注意：Release模式下会被强制覆盖为Production）
        /// </summary>
        public ConnectionMode ConnectionMode { get; set; } = ConnectionMode.Production;

        /// <summary>
        /// 在Debug编译时是否强制使用生产模式
        /// </summary>
        public bool ForceProductionInDebug { get; set; } = false;

        /// <summary>
        /// 设备配置列表
        /// </summary>
        public List<DeviceConnectionItem> Devices { get; set; } = new List<DeviceConnectionItem>();

        /// <summary>
        /// 调试设置
        /// </summary>
        public DebugSettings DebugSettings { get; set; } = new DebugSettings();

        /// <summary>
        /// 全局连接超时（毫秒）
        /// </summary>
        public int GlobalConnectionTimeoutMs { get; set; } = 10000;

        /// <summary>
        /// 是否启用并行连接（同时连接多个设备）
        /// </summary>
        public bool EnableParallelConnection { get; set; } = false;

        #region 加载/保存

        /// <summary>
        /// 加载配置
        /// </summary>
        public static DeviceConnectionConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        Converters = { new JsonStringEnumConverter() }
                    };
                    return JsonSerializer.Deserialize<DeviceConnectionConfig>(json, options) ?? CreateDefault();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceConnectionConfig] 加载配置失败: {ex.Message}");
            }

            var config = CreateDefault();
            config.Save();
            return config;
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Converters = { new JsonStringEnumConverter() }
                };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DeviceConnectionConfig] 保存配置失败: {ex.Message}");
            }
        }

        #endregion

        #region 创建默认配置

        /// <summary>
        /// 创建默认配置
        /// </summary>
        public static DeviceConnectionConfig CreateDefault()
        {
            return new DeviceConnectionConfig
            {
                // 默认使用调试模式（方便开发）
                ConnectionMode = ConnectionMode.Debug,
                ForceProductionInDebug = false,
                GlobalConnectionTimeoutMs = 10000,
                EnableParallelConnection = false,

                DebugSettings = new DebugSettings
                {
                    SkipLoadingPage = false,
                    AutoPassCriticalCheck = true,
                    ConnectionTimeoutMs = 3000,
                    ShowDebugWarning = true
                },

                Devices = new List<DeviceConnectionItem>
                {
                    // ========== 核心SDK/驱动 ==========
                    new DeviceConnectionItem
                    {
                        DeviceId = "VisionSDK",
                        DisplayName = "海康VM SDK",
                        Enabled = true,
                        IsCritical = true,
                        SkipInDebug = false,  // SDK检查即使在调试模式也执行
                        AutoConnectOnStartup = true
                    },
                    new DeviceConnectionItem
                    {
                        DeviceId = "VisionDongle",
                        DisplayName = "海康VM加密狗",
                        Enabled = true,
                        IsCritical = true,
                        SkipInDebug = false,  // 加密狗检查即使在调试模式也执行
                        AutoConnectOnStartup = true
                    },

                    // ========== PLC设备 ==========
                    new DeviceConnectionItem
                    {
                        DeviceId = "PLC",
                        DisplayName = "西门子PLC",
                        Enabled = true,
                        IsCritical = true,
                        SkipInDebug = false,
                        ConnectionTimeoutMs = 5000,
                        AutoConnectOnStartup = true
                    },

                    // ========== 机器人设备 ==========
                    new DeviceConnectionItem
                    {
                        DeviceId = "Robot",
                        DisplayName = "SCARA机器人",
                        Enabled = true,
                        IsCritical = true,
                        SkipInDebug = false,
                        ConnectionTimeoutMs = 5000,
                        AutoConnectOnStartup = true
                    },

                    // ========== 视觉系统 ==========
                    new DeviceConnectionItem
                    {
                        DeviceId = "Vision",
                        DisplayName = "视觉系统",
                        Enabled = true,
                        IsCritical = true,
                        SkipInDebug = false,
                        ConnectionTimeoutMs = 10000,
                        AutoConnectOnStartup = true
                    },

                    
                    // ========== 激光设备 ==========
                    new DeviceConnectionItem
                    {
                        DeviceId = "HM_Laser",
                        DisplayName = "HM激光器",
                        Enabled = true,
                        IsCritical = false,
                        SkipInDebug = false,
                        ConnectionTimeoutMs = 5000,
                        AutoConnectOnStartup = true
                    },
                    // ========== 振动盘设备 ==========
                    new DeviceConnectionItem
                    {
                        DeviceId = "Vibrator",
                        DisplayName = "振动盘设备",
                        Enabled = true,
                        IsCritical = false,
                        SkipInDebug = false,
                        ConnectionTimeoutMs = 5000,
                        AutoConnectOnStartup = true
                    },

                    //// ========== 相机设备（4个） ==========
                    //new DeviceConnectionItem
                    //{
                    //    DeviceId = "Camera_Disk",
                    //    DisplayName = "振动盘相机",
                    //    Enabled = true,
                    //    IsCritical = false,
                    //    SkipInDebug = false,
                    //    ConnectionTimeoutMs = 5000,
                    //    AutoConnectOnStartup = true
                    //},
                    //new DeviceConnectionItem
                    //{
                    //    DeviceId = "Camera_Laser",
                    //    DisplayName = "激光视觉相机",
                    //    Enabled = true,
                    //    IsCritical = false,
                    //    SkipInDebug = false,
                    //    ConnectionTimeoutMs = 5000,
                    //    AutoConnectOnStartup = true
                    //},
                    //new DeviceConnectionItem
                    //{
                    //    DeviceId = "Camera_Small",
                    //    DisplayName = "小料盘相机",
                    //    Enabled = true,
                    //    IsCritical = false,
                    //    SkipInDebug = false,
                    //    ConnectionTimeoutMs = 5000,
                    //    AutoConnectOnStartup = true
                    //},
                    //new DeviceConnectionItem
                    //{
                    //    DeviceId = "Camera_Large",
                    //    DisplayName = "大料盘相机",
                    //    Enabled = true,
                    //    IsCritical = false,
                    //    SkipInDebug = false,
                    //    ConnectionTimeoutMs = 5000,
                    //    AutoConnectOnStartup = true
                    //},




                    new  DeviceConnectionItem
                    {
                        DeviceId = "Scanner_Small",
                        DisplayName = "小料盘扫码器",
                        Enabled = true,
                        IsCritical = false,
                        SkipInDebug = true,
                        ConnectionTimeoutMs = 5000,
                        AutoConnectOnStartup = true
                    },


                    new DeviceConnectionItem
                    {
                        DeviceId = "Scanner_Large",
                        DisplayName = "大料盘扫码器",
                        Enabled = true,
                        IsCritical = false,
                        SkipInDebug = true,
                        ConnectionTimeoutMs = 5000,
                        AutoConnectOnStartup = true
                    },
                }
            };
        }

        #endregion

        #region 查询方法

        /// <summary>
        /// 根据设备ID获取配置
        /// </summary>
        public DeviceConnectionItem GetDeviceConfig(string deviceId)
        {
            return Devices?.Find(d =>
                string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 获取所有启用的设备
        /// </summary>
        public List<DeviceConnectionItem> GetEnabledDevices()
        {
            return Devices?.FindAll(d => d.Enabled) ?? new List<DeviceConnectionItem>();
        }

        /// <summary>
        /// 获取所有关键设备
        /// </summary>
        public List<DeviceConnectionItem> GetCriticalDevices()
        {
            return Devices?.FindAll(d => d.Enabled && d.IsCritical) ?? new List<DeviceConnectionItem>();
        }

        /// <summary>
        /// 获取需要在Loading阶段连接的设备
        /// </summary>
        public List<DeviceConnectionItem> GetAutoConnectDevices()
        {
            return Devices?.FindAll(d => d.Enabled && d.AutoConnectOnStartup) ?? new List<DeviceConnectionItem>();
        }

        /// <summary>
        /// 检查设备是否应该跳过（调试模式）
        /// </summary>
        public bool ShouldSkipDevice(string deviceId, ConnectionMode effectiveMode)
        {
            if (effectiveMode != ConnectionMode.Debug)
                return false;

            var device = GetDeviceConfig(deviceId);
            return device?.SkipInDebug ?? false;
        }

        /// <summary>
        /// 获取有效的连接模式（考虑编译模式）
        /// </summary>
        public ConnectionMode GetEffectiveMode()
        {
#if DEBUG
            // Debug编译时，检查是否强制使用生产模式
            if (ForceProductionInDebug)
            {
                return ConnectionMode.Production;
            }
            return ConnectionMode;
#else
            // Release编译时，强制使用生产模式
            return ConnectionMode.Production;
#endif
        }

        /// <summary>
        /// 是否为调试模式
        /// </summary>
        public bool IsDebugMode => GetEffectiveMode() == ConnectionMode.Debug;

        /// <summary>
        /// 是否为生产模式
        /// </summary>
        public bool IsProductionMode => GetEffectiveMode() == ConnectionMode.Production;

        #endregion
    }
}