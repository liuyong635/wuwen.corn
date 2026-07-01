using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;

namespace SeedCut.Models
{
    /// <summary>
    /// Modbus 数据类型
    /// </summary>
    public enum ModbusDataType
    {
        Bool,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Float,
        String
    }

    /// <summary>
    /// Modbus 寄存器类型
    /// </summary>
    public enum ModbusRegisterType
    {
        Coil,              // 线圈 (可读写)
        DiscreteInput,     // 离散输入 (只读)
        HoldingRegister,   // 保持寄存器 (可读写)
        InputRegister      // 输入寄存器 (只读)
    }

    /// <summary>
    /// 单个 Modbus 地址配置
    /// </summary>
    [XmlRoot("ModbusAddress")]
    public class ModbusAddress
    {
        /// <summary>
        /// 地址名称（用于代码中引用）
        /// </summary>
        [XmlAttribute("Name")]
        public string Name { get; set; }

        /// <summary>
        /// 寄存器类型
        /// </summary>
        [XmlAttribute("Type")]
        public ModbusRegisterType RegisterType { get; set; }

        /// <summary>
        /// 起始地址
        /// </summary>
        [XmlAttribute("Address")]
        public ushort Address { get; set; }

        /// <summary>
        /// 数据类型
        /// </summary>
        [XmlAttribute("DataType")]
        public ModbusDataType DataType { get; set; }

        /// <summary>
        /// 寄存器数量（Float 需要 2 个寄存器）
        /// </summary>
        [XmlAttribute("Length")]
        public ushort Length { get; set; } = 1;

        /// <summary>
        /// 描述信息
        /// </summary>
        [XmlAttribute("Description")]
        public string Description { get; set; }

        /// <summary>
        /// 字节序（LittleEndian/BigEndian）
        /// </summary>
        [XmlAttribute("ByteOrder")]
        public string ByteOrder { get; set; } = "LittleEndian";
    }

    /// <summary>
    /// Modbus 设备配置（针对使用Modbus协议的设备）
    /// </summary>
    [XmlRoot("ModbusDeviceConfig")]
    public class ModbusDeviceConfig : DeviceConfig
    {
        /// <summary>
        /// 从站号
        /// </summary>
        [XmlElement("StationId")]
        public byte StationId { get; set; } = 1;

        /// <summary>
        /// 地址映射表
        /// </summary>
        [XmlArray("Addresses")]
        [XmlArrayItem("Address")]
        public List<ModbusAddress> Addresses { get; set; } = new List<ModbusAddress>();

        /// <summary>
        /// 根据名称获取地址
        /// </summary>
        public ModbusAddress GetAddress(string name)
        {
            return Addresses.FirstOrDefault(a => a.Name == name);
        }

        /// <summary>
        /// 根据名称获取地址值(简化版)
        /// </summary>
        public ushort GetAddressValue(string name)
        {
            var addr = GetAddress(name);
            return addr?.Address ?? 0;
        }
    }
}