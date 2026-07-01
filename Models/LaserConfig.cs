using System;
using System.IO;
using System.Xml.Serialization;

namespace SeedCut.Models
{
    /// <summary>
    /// 激光器配置（支持XML文件配置）
    /// </summary>
    [XmlRoot("LaserConfig")]
    public class LaserConfig
    {
        // ========== 基础配置 ==========

        /// <summary>
        /// 激光器IP地址（激光器连接到上位机时的地址）
        /// </summary>
        [XmlElement("IpAddress")]
        public string IpAddress { get; set; }

        /// <summary>
        /// TCP服务器监听端口
        /// </summary>
        [XmlElement("Port")]
        public int Port { get; set; }

        /// <summary>
        /// 通信超时时间（毫秒）
        /// </summary>
        [XmlElement("TimeoutMs")]
        public int TimeoutMs { get; set; }

        /// <summary>
        /// 自动发送Mark指令
        /// </summary>
        [XmlElement("AutoSendMark")]
        public bool AutoSendMark { get; set; }

        /// <summary>
        /// 指令队列最大长度
        /// </summary>
        [XmlElement("MaxQueueLength")]
        public int MaxQueueLength { get; set; }

        // ========== 工作区域安全配置 ==========

        /// <summary>
        /// 工作区域最小X坐标（毫米）
        /// </summary>
        [XmlElement("WorkAreaMinX")]
        public double WorkAreaMinX { get; set; }

        /// <summary>
        /// 工作区域最大X坐标（毫米）
        /// </summary>
        [XmlElement("WorkAreaMaxX")]
        public double WorkAreaMaxX { get; set; }

        /// <summary>
        /// 工作区域最小Y坐标（毫米）
        /// </summary>
        [XmlElement("WorkAreaMinY")]
        public double WorkAreaMinY { get; set; }

        /// <summary>
        /// 工作区域最大Y坐标（毫米）
        /// </summary>
        [XmlElement("WorkAreaMaxY")]
        public double WorkAreaMaxY { get; set; }

        /// <summary>
        /// 启用坐标范围检查（安全开关）
        /// </summary>
        [XmlElement("EnableCoordinateCheck")]
        public bool EnableCoordinateCheck { get; set; }

        // ========== 计算属性（不序列化） ==========

        /// <summary>
        /// 工作区域宽度（毫米）
        /// </summary>
        [XmlIgnore]
        public double WorkAreaWidth => WorkAreaMaxX - WorkAreaMinX;

        /// <summary>
        /// 工作区域高度（毫米）
        /// </summary>
        [XmlIgnore]
        public double WorkAreaHeight => WorkAreaMaxY - WorkAreaMinY;

        /// <summary>
        /// 工作区域中心X坐标
        /// </summary>
        [XmlIgnore]
        public double WorkAreaCenterX => (WorkAreaMinX + WorkAreaMaxX) / 2;

        /// <summary>
        /// 工作区域中心Y坐标
        /// </summary>
        [XmlIgnore]
        public double WorkAreaCenterY => (WorkAreaMinY + WorkAreaMaxY) / 2;

        // ========== 配置文件路径 ==========

        private static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Config",
            "LaserConfig.xml"
        );

        // ========== 配置文件加载与保存 ==========

        /// <summary>
        /// 加载配置（从XML文件，如果不存在则创建默认配置）
        /// </summary>
        public static LaserConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var serializer = new XmlSerializer(typeof(LaserConfig));
                    using (var reader = new StreamReader(ConfigPath))
                    {
                        var config = (LaserConfig)serializer.Deserialize(reader);
                        System.Diagnostics.Debug.WriteLine($"[LaserConfig] 从文件加载配置: {ConfigPath}");
                        return config;
                    }
                }
                else
                {
                    // 文件不存在，创建默认配置并保存
                    var defaultConfig = CreateDefaultConfig();
                    defaultConfig.Save();
                    System.Diagnostics.Debug.WriteLine($"[LaserConfig] 配置文件不存在，已创建默认配置: {ConfigPath}");
                    return defaultConfig;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LaserConfig] 加载配置失败: {ex.Message}，使用默认配置");
                return CreateDefaultConfig();
            }
        }

        /// <summary>
        /// 保存配置到XML文件
        /// </summary>
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var serializer = new XmlSerializer(typeof(LaserConfig));
                using (var writer = new StreamWriter(ConfigPath))
                {
                    serializer.Serialize(writer, this);
                }
                System.Diagnostics.Debug.WriteLine($"[LaserConfig] 配置已保存: {ConfigPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LaserConfig] 保存配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建默认配置
        /// </summary>
        public static LaserConfig CreateDefaultConfig()
        {
            return new LaserConfig
            {
                // 基础配置 - 端口改为8001，与C++保持一致
                IpAddress = "127.0.0.1",
                Port = 8001,  // ← 与C++的startTCPServerLaser(8001)保持一致
                TimeoutMs = 5000,
                AutoSendMark = false,
                MaxQueueLength = 100,

                // 工作区域配置（根据你的激光器实际参数修改这里）
                WorkAreaMinX = -100,
                WorkAreaMaxX = 100,
                WorkAreaMinY = -100,
                WorkAreaMaxY = 100,

                // 安全检查开关
                EnableCoordinateCheck = true
            };
        }

        /// <summary>
        /// 创建测试配置（小范围，更安全）
        /// </summary>
        public static LaserConfig CreateTestConfig()
        {
            return new LaserConfig
            {
                IpAddress = "127.0.0.1",
                Port = 8001,
                TimeoutMs = 5000,
                AutoSendMark = false,
                MaxQueueLength = 100,

                // 测试用小范围工作区域
                WorkAreaMinX = -50,
                WorkAreaMaxX = 50,
                WorkAreaMinY = -50,
                WorkAreaMaxY = 50,

                EnableCoordinateCheck = true
            };
        }

        /// <summary>
        /// 创建生产配置（大范围）
        /// </summary>
        public static LaserConfig CreateProductionConfig()
        {
            return new LaserConfig
            {
                IpAddress = "127.0.0.1",
                Port = 8001,
                TimeoutMs = 5000,
                AutoSendMark = true,  // 生产环境可以启用自动打标
                MaxQueueLength = 100,

                // 生产环境工作区域（根据实际激光器参数设置）
                WorkAreaMinX = -150,
                WorkAreaMaxX = 150,
                WorkAreaMinY = -150,
                WorkAreaMaxY = 150,

                EnableCoordinateCheck = true
            };
        }

        // ========== 验证方法 ==========

        /// <summary>
        /// 验证配置是否合法
        /// </summary>
        public bool Validate(out string error)
        {
            error = null;

            // 验证端口
            if (Port < 1 || Port > 65535)
            {
                error = "端口号必须在1-65535之间";
                return false;
            }

            // 验证超时时间
            if (TimeoutMs < 0)
            {
                error = "超时时间不能为负数";
                return false;
            }

            // 验证工作区域X范围
            if (WorkAreaMinX >= WorkAreaMaxX)
            {
                error = "工作区域X范围错误：最小值必须小于最大值";
                return false;
            }

            // 验证工作区域Y范围
            if (WorkAreaMinY >= WorkAreaMaxY)
            {
                error = "工作区域Y范围错误：最小值必须小于最大值";
                return false;
            }

            // 验证工作区域面积
            if (WorkAreaWidth <= 0 || WorkAreaHeight <= 0)
            {
                error = "工作区域面积必须大于0";
                return false;
            }

            // 验证队列长度
            if (MaxQueueLength < 1)
            {
                error = "队列长度必须至少为1";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 获取配置摘要（用于日志）
        /// </summary>
        public override string ToString()
        {
            return $"LaserConfig [Port={Port}, WorkArea=X[{WorkAreaMinX},{WorkAreaMaxX}] Y[{WorkAreaMinY},{WorkAreaMaxY}], " +
                   $"Size={WorkAreaWidth}x{WorkAreaHeight}mm, AutoMark={AutoSendMark}, CoordinateCheck={EnableCoordinateCheck}]";
        }
    }
}