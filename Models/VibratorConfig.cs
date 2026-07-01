using System;
using System.IO;
using System.Xml.Serialization;

namespace SeedCut.Models
{
    /// <summary>
    /// 振动盘配置（继承通用 Modbus 配置）
    /// </summary>
    [XmlRoot("VibratorConfig")]
    public class VibratorConfig : ModbusDeviceConfig
    {
        /// <summary>
        /// 振动强度（0-100）
        /// </summary>
        [XmlElement("VibrationIntensity")]
        public int VibrationIntensity { get; set; } = 50;

        /// <summary>
        /// 抖料时间（毫秒）
        /// </summary>
        [XmlElement("FeedDuration")]
        public int FeedDuration { get; set; } = 500;

        /// <summary>
        /// 抖料后等待时间（毫秒）- 匹配C++: 1000ms
        /// </summary>
        [XmlElement("PostFeedDelay")]
        public int PostFeedDelay { get; set; } = 1000;  // ✅ 新增

        /// <summary>
        /// 抖料时是否同时启动振动 - 匹配C++行为
        /// </summary>
        [XmlElement("VibrateDuringFeed")]
        public bool VibrateDuringFeed { get; set; } = true;  // ✅ 新增

        #region 常用地址快速访问

        [XmlIgnore]
        public ushort StateAddress => GetAddressValue("State");

        [XmlIgnore]
        public ushort BoxFeedAddress => GetAddressValue("BoxFeed");

        [XmlIgnore]
        public ushort LightAAddress => GetAddressValue("LightA");

        [XmlIgnore]
        public ushort LightBAddress => GetAddressValue("LightB");

        [XmlIgnore]
        public ushort PourDoorAddress => GetAddressValue("PourDoor");

        [XmlIgnore]
        public ushort VibrationAddress => GetAddressValue("Vibration");

        #endregion

        #region 配置文件加载与保存

        private static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Config",
            "VibratorConfig.xml"
        );

        public static VibratorConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var serializer = new XmlSerializer(typeof(VibratorConfig));
                    using (var reader = new StreamReader(ConfigPath))
                    {
                        return (VibratorConfig)serializer.Deserialize(reader);
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
                System.Diagnostics.Debug.WriteLine($"加载振动盘配置失败: {ex.Message}");
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

                var serializer = new XmlSerializer(typeof(VibratorConfig));
                using (var writer = new StreamWriter(ConfigPath))
                {
                    serializer.Serialize(writer, this);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存振动盘配置失败: {ex.Message}");
            }
        }

        public static VibratorConfig CreateDefault()
        {
            var config = new VibratorConfig
            {
                DeviceName = "Vibrator Disk",
                IPAddress = "192.168.0.20",
                Port = 502,
                StationId = 2
            };

            // ✅ 改动1：State改为InputRegister，ByteOrder改为BigEndian
            config.Addresses.Add(new ModbusAddress
            {
                Name = "State",
                RegisterType = ModbusRegisterType.HoldingRegister,  // ✅ 修改
                Address = 0x0001,
                DataType = ModbusDataType.UInt16,
                ByteOrder = "BigEndian",  // ✅ 修改
                Description = "设备状态（只读）"
            });

            // ✅ 改动2：所有地址ByteOrder改为BigEndian
            config.Addresses.Add(new ModbusAddress
            {
                Name = "BoxFeed",
                RegisterType = ModbusRegisterType.HoldingRegister,
                Address = 0x0005,
                DataType = ModbusDataType.UInt16,
                ByteOrder = "BigEndian",  // ✅ 修改
                Description = "送料仓运动"
            });

            config.Addresses.Add(new ModbusAddress
            {
                Name = "LightA",
                RegisterType = ModbusRegisterType.HoldingRegister,
                Address = 0x0006,
                DataType = ModbusDataType.UInt16,
                ByteOrder = "BigEndian",  // ✅ 修改
                Description = "光源A"
            });

            config.Addresses.Add(new ModbusAddress
            {
                Name = "LightB",
                RegisterType = ModbusRegisterType.HoldingRegister,
                Address = 0x0007,
                DataType = ModbusDataType.UInt16,
                ByteOrder = "BigEndian",  // ✅ 修改
                Description = "光源B"
            });

            config.Addresses.Add(new ModbusAddress
            {
                Name = "PourDoor",
                RegisterType = ModbusRegisterType.HoldingRegister,
                Address = 0x000A,
                DataType = ModbusDataType.UInt16,
                ByteOrder = "BigEndian",  // ✅ 修改
                Description = "排料仓门"
            });

            config.Addresses.Add(new ModbusAddress
            {
                Name = "Vibration",
                RegisterType = ModbusRegisterType.HoldingRegister,
                Address = 0x000F,
                DataType = ModbusDataType.UInt16,
                ByteOrder = "BigEndian",  // ✅ 修改
                Description = "振动盘运动"
            });

            return config;
        }

        #endregion
    }
}