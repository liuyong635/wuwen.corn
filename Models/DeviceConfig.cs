using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace SeedCut.Models
{
    /// <summary>
    /// 通用设备配置基类
    /// </summary>
    [XmlRoot("DeviceConfig")]
    public abstract class DeviceConfig
    {
        /// <summary>
        /// 设备名称
        /// </summary>
        [XmlAttribute("DeviceName")]
        public string DeviceName { get; set; }

        /// <summary>
        /// 设备 IP 地址
        /// </summary>
        [XmlElement("IPAddress")]
        public string IPAddress { get; set; } = "192.168.0.1";

        /// <summary>
        /// 端口号
        /// </summary>
        [XmlElement("Port")]
        public int Port { get; set; } = 502;

        /// <summary>
        /// 连接超时时间(毫秒)
        /// </summary>
        [XmlElement("ConnectTimeout")]
        public int ConnectTimeout { get; set; } = 3000;

        /// <summary>
        /// 读写超时时间(毫秒)
        /// </summary>
        [XmlElement("ReadWriteTimeout")]
        public int ReadWriteTimeout { get; set; } = 1000;

        /// <summary>
        /// 设备描述
        /// </summary>
        [XmlElement("Description")]
        public string Description { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        [XmlElement("Enabled")]
        public bool Enabled { get; set; } = true;
    }
}