using SeedCut.Models.PLCModule;
using System;

namespace SeedCut.Services
{
    /// <summary>
    /// PLC服务接口
    /// </summary>
    public interface IPLCService : IDisposable
    {
        /// <summary>
        /// PLC连接配置
        /// </summary>
        PLCConnectionConfig ConnectionConfig { get; }

        /// <summary>
        /// PLC地址注册表
        /// </summary>
        PLCAddressRegistry AddressRegistry { get; }

        // ✅ 添加这个事件
        event EventHandler<PLCConnectionStateChangedEventArgs> ConnectionStateChanged;

        /// <summary>
        /// 是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 连接PLC
        /// </summary>
        bool Connect();

        /// <summary>
        /// 断开PLC
        /// </summary>
        void Disconnect();

        #region 基础读写方法

        byte[] ReadBytes(int dataBlock, int startAddress, int length);
        bool WriteBytes(int dataBlock, int startAddress, byte[] data);
        bool ReadBit(int dataBlock, int startAddress, int bitPosition);
        bool WriteBit(int dataBlock, int startAddress, int bitPosition, bool value);
        float ReadFloat(int dataBlock, int startAddress);
        bool WriteFloat(int dataBlock, int startAddress, float value);
        short ReadInt16(int dataBlock, int startAddress);
        int ReadInt32(int dataBlock, int startAddress);
        bool WriteInt16(int dataBlock, int startAddress, short value);
        bool WriteInt32(int dataBlock, int startAddress, int value);
        #endregion

        #region 高级方法

        bool PulseBit(int dataBlock, int startAddress, int bitPosition, int delayMs = 20);
        bool ToggleBit(int dataBlock, int startAddress, int bitPosition);

        #endregion

        #region 按名称读写

        bool WriteBitByName(string addressName, bool value);
        bool PulseBitByName(string addressName, int delayMs = 20);
        bool ToggleBitByName(string addressName);
        bool WriteFloatByName(string addressName, float value);
        bool WriteInt16ByName(string addressName, short value);
        bool ReadBitByName(string addressName);
        float ReadFloatByName(string addressName);
        short ReadInt16ByName(string addressName);

        #endregion
        #region ✅ 新增：支持 PLCAddressItem 的读写方法（支持 I/Q/M/DB）

        /// <summary>
        /// 读取位值（支持 I/Q/M/DB 区）
        /// </summary>
        bool ReadBit(PLCAddressItem address);

        /// <summary>
        /// 写入位值（支持 I/Q/M/DB 区）
        /// </summary>
        bool WriteBit(PLCAddressItem address, bool value);
        bool WriteHeartbitDirect(int dataBlock, int startAddress, int bitPosition, bool value);

        #endregion

    }

    /// <summary>
    /// PLC连接配置
    /// </summary>
    public class PLCConnectionConfig
    {
        public string DeviceName { get; set; } = "Siemens S7 PLC";
        public string IPAddress { get; set; } = "192.168.0.11";
        public int Port { get; set; } = 102;
        public int Rack { get; set; } = 0;
        public int Slot { get; set; } = 1;
        public S7PLCType PLCType { get; set; } = S7PLCType.S7300;
        public string Description { get; set; }
    }

    public enum S7PLCType
    {
        S7200,
        S7300,
        S7400,
        S71200,
        S71500
    }
    public class PLCConnectionStateChangedEventArgs : EventArgs
    {
        public bool IsConnected { get; set; }
        public string Message { get; set; }
        public DateTime ChangeTime { get; set; }

        public PLCConnectionStateChangedEventArgs(bool isConnected, string message = null)
        {
            IsConnected = isConnected;
            Message = message;
            ChangeTime = DateTime.Now;
        }
    }
}