using System;
using System.IO;
using System.Xml.Serialization;

namespace SeedCut.Models
{
    /// <summary>
    /// 机器人专用配置（继承通用 Modbus 配置）
    /// </summary>
    [XmlRoot("RobotConfig")]
    public class RobotConfig : ModbusDeviceConfig
    {
        /// <summary>
        /// 机器人型号
        /// </summary>
        [XmlElement("RobotModel")]
        public string RobotModel { get; set; } = "EPSON";

        /// <summary>
        /// JOG 速度（mm/s）
        /// </summary>
        [XmlElement("JogSpeed")]
        public float JogSpeed { get; set; } = 10.0f;

        /// <summary>
        /// 自动运行速度（mm/s）
        /// </summary>
        [XmlElement("AutoSpeed")]
        public float AutoSpeed { get; set; } = 50.0f;

        /// <summary>
        /// 位置精度（mm）
        /// </summary>
        [XmlElement("PositionAccuracy")]
        public float PositionAccuracy { get; set; } = 0.01f;

        #region 常用地址快速访问属性

        /// <summary>
        /// 输出地址
        /// </summary>
        [XmlIgnore]
        public ushort OutAddress => GetAddressValue("Out");

        /// <summary>
        /// 设置 AR 地址
        /// </summary>
        [XmlIgnore]
        public ushort SetARAddress => GetAddressValue("SetAR");

        /// <summary>
        /// 笛卡尔坐标地址
        /// </summary>
        [XmlIgnore]
        public ushort CartesianCoordinatesAddress => GetAddressValue("CartesianCoordinates");

        /// <summary>
        /// 伺服使能地址
        /// </summary>
        [XmlIgnore]
        public ushort ServoEnableAddress => GetAddressValue("ServoEnable");

        /// <summary>
        /// JOG XYZC 地址
        /// </summary>
        [XmlIgnore]
        public ushort JogXYZCAddress => GetAddressValue("JogXYZC");

        #endregion

        #region 静态方法：加载和保存配置

        private static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Config",
            "RobotConfig.xml"
        );

        /// <summary>
        /// 加载配置文件
        /// </summary>
        public static RobotConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var serializer = new XmlSerializer(typeof(RobotConfig));
                    using (var reader = new StreamReader(ConfigPath))
                    {
                        return (RobotConfig)serializer.Deserialize(reader);
                    }
                }
                else
                {
                    // 创建默认配置
                    var defaultConfig = CreateDefault();
                    defaultConfig.Save();
                    return defaultConfig;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载机器人配置失败: {ex.Message}");
                return CreateDefault();
            }
        }

        /// <summary>
        /// 保存配置文件
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

                var serializer = new XmlSerializer(typeof(RobotConfig));
                using (var writer = new StreamWriter(ConfigPath))
                {
                    serializer.Serialize(writer, this);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存机器人配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建默认配置（对应 Qt 代码中的地址）
        /// </summary>
        public static RobotConfig CreateDefault()
        {
            var config = new RobotConfig
            {
                DeviceName = "EPSON Robot",
                IPAddress = "192.168.0.123",
                Port = 502,
                StationId = 1,
                RobotModel = "EPSON"
            };

            // 添加地址映射（对应 robotcontrol.h 中的定义）
            config.Addresses.Add(new ModbusAddress
            {
                Name = "Out",
                RegisterType = ModbusRegisterType.HoldingRegister,
                Address = 0x0204,
                DataType = ModbusDataType.UInt16,
                Description = "输出寄存器"
            });

            config.Addresses.Add(new ModbusAddress
            {
                Name = "SetAR",
                RegisterType = ModbusRegisterType.HoldingRegister,
                Address = 0x0214,
                DataType = ModbusDataType.UInt16,
                Description = "设置 AR（运行/暂停/停止/复位）"
            });

            config.Addresses.Add(new ModbusAddress
            {
                Name = "CartesianCoordinates",
                RegisterType = ModbusRegisterType.HoldingRegister,
                Address = 0x0215,
                DataType = ModbusDataType.Float,
                Length = 10, // X, Y, Z, C 各占 2 个寄存器 + 额外数据
                Description = "笛卡尔坐标（X, Y, Z, C）"
            });

            config.Addresses.Add(new ModbusAddress
            {
                Name = "ServoEnable",
                RegisterType = ModbusRegisterType.HoldingRegister,
                Address = 0x0235,
                DataType = ModbusDataType.UInt16,
                Description = "伺服使能"
            });

            config.Addresses.Add(new ModbusAddress
            {
                Name = "JogXYZC",
                RegisterType = ModbusRegisterType.HoldingRegister,
                Address = 0x0238,
                DataType = ModbusDataType.UInt16,
                Description = "JOG 运动控制（X, Y, Z, C 轴）"
            });

            return config;
        }

        #endregion
    }
}