using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeedCut.Services.Camera
{
    /// <summary>
    /// 单个相机实例配置
    /// </summary>
    public class CameraInstanceConfig
    {
        /// <summary>
        /// 相机唯一标识（如 "Camera_Photo", "Camera_Laser"）
        /// </summary>
        public string CameraId { get; set; }

        /// <summary>
        /// 相机显示名称
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 相机序列号（用于自动识别匹配）
        /// </summary>
        public string SerialNumber { get; set; }

        /// <summary>
        /// 相机用户自定义名称（海康SDK中设置的UserDefinedName）
        /// </summary>
        public string UserDefinedName { get; set; }

        /// <summary>
        /// 相机IP地址（GigE相机，可选）
        /// </summary>
        public string IPAddress { get; set; }

        /// <summary>
        /// 相机用途描述
        /// </summary>
        public string Purpose { get; set; }

        /// <summary>
        /// 默认曝光时间（微秒）
        /// </summary>
        public double DefaultExposure { get; set; } = 10000;

        /// <summary>
        /// 默认增益
        /// </summary>
        public double DefaultGain { get; set; } = 0;

        /// <summary>
        /// 默认帧率
        /// </summary>
        public double DefaultFrameRate { get; set; } = 30;

        /// <summary>
        /// 像素格式
        /// </summary>
        public string PixelFormat { get; set; } = "Mono8";

        /// <summary>
        /// 是否启用自动重连
        /// </summary>
        public bool EnableAutoReconnect { get; set; } = true;

        /// <summary>
        /// 是否在启动时自动连接
        /// </summary>
        public bool AutoConnectOnStartup { get; set; } = false;

        /// <summary>
        /// 图像保存路径（为空则使用默认路径）
        /// </summary>
        public string SavePath { get; set; }

        /// <summary>
        /// 心跳检测间隔（毫秒）
        /// </summary>
        public int HeartbeatIntervalMs { get; set; } = 1000;

        /// <summary>
        /// 最大重连次数
        /// </summary>
        public int MaxReconnectCount { get; set; } = 10;
    }

    /// <summary>
    /// 海康相机全局配置
    /// </summary>
    public class HikCameraConfig
    {
        private static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Config",
            "HikCameraConfig.json"
        );

        /// <summary>
        /// 相机配置列表
        /// </summary>
        public List<CameraInstanceConfig> Cameras { get; set; } = new List<CameraInstanceConfig>();

        /// <summary>
        /// 全局心跳检测间隔（毫秒）- 可被单个相机配置覆盖
        /// </summary>
        public int DefaultHeartbeatIntervalMs { get; set; } = 1000;

        /// <summary>
        /// 全局最大重连次数 - 可被单个相机配置覆盖
        /// </summary>
        public int DefaultMaxReconnectCount { get; set; } = 10;

        /// <summary>
        /// 初始重连延迟（毫秒）
        /// </summary>
        public int InitialReconnectDelayMs { get; set; } = 3000;

        /// <summary>
        /// 最大重连延迟（毫秒）
        /// </summary>
        public int MaxReconnectDelayMs { get; set; } = 30000;

        /// <summary>
        /// 加载配置
        /// </summary>
        public static HikCameraConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip
                    };
                    return JsonSerializer.Deserialize<HikCameraConfig>(json, options) ?? CreateDefault();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载相机配置失败: {ex.Message}");
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
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存相机配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建默认配置（4个相机：振动盘、激光、小料盘、大料盘）
        /// </summary>
        public static HikCameraConfig CreateDefault()
        {
            return new HikCameraConfig
            {
                DefaultHeartbeatIntervalMs = 1000,
                DefaultMaxReconnectCount = 10,
                InitialReconnectDelayMs = 3000,
                MaxReconnectDelayMs = 30000,
                Cameras = new List<CameraInstanceConfig>
                {
                    new CameraInstanceConfig
                    {
                        CameraId = "Camera_Disk",
                        DisplayName = "振动盘相机",
                        SerialNumber = "DA4841544",  // 请填写实际序列号
                        UserDefinedName = "DiskCamera",
                        Purpose = "振动盘种子检测定位",
                        DefaultExposure = 10000,
                        DefaultGain = 0,
                        DefaultFrameRate = 30,
                        PixelFormat = "Mono8",
                        EnableAutoReconnect = true,
                        AutoConnectOnStartup = true
                    },
                    new CameraInstanceConfig
                    {
                        CameraId = "Camera_Laser",
                        DisplayName = "激光视觉相机",
                        SerialNumber = "DA7147198",  // 请填写实际序列号
                        UserDefinedName = "LaserCamera",
                        Purpose = "激光切割定位检测",
                        DefaultExposure = 8000,
                        DefaultGain = 0,
                        DefaultFrameRate = 30,
                        PixelFormat = "Mono8",
                        EnableAutoReconnect = true,
                        AutoConnectOnStartup = true
                    },
                    new CameraInstanceConfig
                    {
                        CameraId = "Camera_Small",
                        DisplayName = "小料盘相机",
                        SerialNumber = "DA7147332",  // 请填写实际序列号
                        UserDefinedName = "SmallCamera",
                        Purpose = "小料盘视觉检测及方向检测",
                        DefaultExposure = 10000,
                        DefaultGain = 0,
                        DefaultFrameRate = 30,
                        PixelFormat = "Mono8",
                        EnableAutoReconnect = true,
                        AutoConnectOnStartup = true
                    },
                    new CameraInstanceConfig
                    {
                        CameraId = "Camera_Large",
                        DisplayName = "大料盘相机",
                        SerialNumber = "DA7147304",  // 请填写实际序列号
                        UserDefinedName = "LargeCamera",
                        Purpose = "大料盘视觉检测及方向检测",
                        DefaultExposure = 10000,
                        DefaultGain = 0,
                        DefaultFrameRate = 30,
                        PixelFormat = "Mono8",
                        EnableAutoReconnect = true,
                        AutoConnectOnStartup = true
                    }
                }
            };
        }

        /// <summary>
        /// 根据ID获取相机配置
        /// </summary>
        public CameraInstanceConfig GetCameraConfig(string cameraId)
        {
            return Cameras?.Find(c =>
                string.Equals(c.CameraId, cameraId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 根据序列号获取相机配置
        /// </summary>
        public CameraInstanceConfig GetCameraConfigBySerialNumber(string serialNumber)
        {
            return Cameras?.Find(c =>
                string.Equals(c.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase));
        }
    }
}