using System;
using System.IO;
using System.Xml.Serialization;

namespace SeedCut.Models
{
    /// <summary>
    /// PLC专用配置（继承S7配置，因为使用西门子S7系列PLC）
    /// </summary>
    [XmlRoot("PLCConfig")]
    public class PLCConfig : S7DeviceConfig
    {
        #region 静态方法：加载和保存配置

        private static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Config",
            "PLCConfig.xml"
        );

        public static PLCConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var serializer = new XmlSerializer(typeof(PLCConfig));
                    using (var reader = new StreamReader(ConfigPath))
                    {
                        return (PLCConfig)serializer.Deserialize(reader);
                    }
                }
                else
                {
                    var defaultConfig = CreateDefault();
                    defaultConfig.Save();
                    return defaultConfig;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载PLC配置失败: {ex.Message}");
                return CreateDefault();
            }
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var serializer = new XmlSerializer(typeof(PLCConfig));
                using (var writer = new StreamWriter(ConfigPath))
                {
                    serializer.Serialize(writer, this);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存PLC配置失败: {ex.Message}");
            }
        }

        public static PLCConfig CreateDefault()
        {
            var config = new PLCConfig
            {
                DeviceName = "Siemens S7-300 PLC",
                IPAddress = "192.168.0.11",
                Port = 102,
                Rack = 0,
                Slot = 1,
                PLCType = S7PLCType.S7300,
                Description = "主控PLC"
            };

            return config;
        }

        #endregion
    }
}