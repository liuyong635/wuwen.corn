using System.Xml.Serialization;

namespace SeedCut.Models
{
    /// <summary>
    /// S7 PLC类型
    /// </summary>
    public enum S7PLCType
    {
        S7200 = 0,
        S7300 = 10,
        S7400 = 20,
        S71200 = 30,
        S71500 = 40
    }

    /// <summary>
    /// S7 设备配置基类（针对西门子S7系列）
    /// </summary>
    [XmlRoot("S7DeviceConfig")]
    public class S7DeviceConfig : DeviceConfig
    {
        /// <summary>
        /// PLC类型
        /// </summary>
        [XmlElement("PLCType")]
        public S7PLCType PLCType { get; set; } = S7PLCType.S7300;

        /// <summary>
        /// 机架号
        /// </summary>
        [XmlElement("Rack")]
        public int Rack { get; set; } = 0;

        /// <summary>
        /// 槽号
        /// </summary>
        [XmlElement("Slot")]
        public int Slot { get; set; } = 1;

        public S7DeviceConfig()
        {
            // S7通信使用102端口
            Port = 102;
        }
    }
}