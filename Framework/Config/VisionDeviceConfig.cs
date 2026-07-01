using System;
using System.IO;
using System.Xml.Serialization;

namespace SeedCut.Framework.Config
{
    /// <summary>
    /// Vision 设备配置
    /// </summary>
    [XmlRoot("VisionDeviceConfig")]
    public class VisionDeviceConfig
    {
        #region 默认值常量

        private const string DefaultSolutionPath = @"D:\wwkj\project\振动盘玉米.sol";
        private const string DefaultPassword = "";
        private const string DefaultConfigFileName = "VisionDevice.config";

        #endregion

        #region 配置属性

        /// <summary>
        /// 方案文件路径
        /// </summary>
        [XmlElement("SolutionPath")]
        public string SolutionPath { get; set; } = DefaultSolutionPath;

        /// <summary>
        /// 方案密码（可选）
        /// </summary>
        [XmlElement("Password")]
        public string Password { get; set; } = DefaultPassword;

        /// <summary>
        /// 连接时是否自动加载方案
        /// </summary>
        [XmlElement("AutoLoadOnConnect")]
        public bool AutoLoadOnConnect { get; set; } = true;

        #endregion

        #region 静态工厂方法

        /// <summary>
        /// 创建默认配置
        /// </summary>
        public static VisionDeviceConfig CreateDefault()
        {
            return new VisionDeviceConfig();
        }

        /// <summary>
        /// 从 XML 文件加载配置，文件不存在则返回默认配置
        /// </summary>
        /// <param name="configPath">配置文件路径，为空则使用默认路径</param>
        public static VisionDeviceConfig Load(string configPath = null)
        {
            var path = configPath ?? GetDefaultConfigPath();

            if (!File.Exists(path))
            {
                var config = CreateDefault();
                // 首次运行时生成默认配置文件
                config.Save(path);
                return config;
            }

            try
            {
                var serializer = new XmlSerializer(typeof(VisionDeviceConfig));
                using (var reader = new StreamReader(path))
                {
                    var config = (VisionDeviceConfig)serializer.Deserialize(reader);
                    return config ?? CreateDefault();
                }
            }
            catch (Exception)
            {
                // 配置文件损坏时返回默认配置
                return CreateDefault();
            }
        }

        /// <summary>
        /// 保存配置到 XML 文件
        /// </summary>
        /// <param name="configPath">配置文件路径，为空则使用默认路径</param>
        public void Save(string configPath = null)
        {
            var path = configPath ?? GetDefaultConfigPath();

            try
            {
                // 确保目录存在
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var serializer = new XmlSerializer(typeof(VisionDeviceConfig));
                using (var writer = new StreamWriter(path))
                {
                    serializer.Serialize(writer, this);
                }
            }
            catch (Exception)
            {
                // 保存失败时静默处理
            }
        }

        #endregion

        #region 私有方法

        private static string GetDefaultConfigPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "Config", DefaultConfigFileName);
        }

        #endregion
    }
}