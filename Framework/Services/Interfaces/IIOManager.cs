using SeedCut.Framework.Core;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Interfaces
{
    /// <summary>
    /// IO管理器接口
    /// 管理PLC IO点位映射、提供友好的命名访问
    /// </summary>
    public interface IIOManager : IDisposable
    {
        #region 属性

        /// <summary>
        /// 注册的IO点数量
        /// </summary>
        int RegisteredCount { get; }

        /// <summary>
        /// 是否正在轮询
        /// </summary>
        bool IsPolling { get; }

        /// <summary>
        /// 轮询间隔(ms)
        /// </summary>
        int PollingIntervalMs { get; set; }

        #endregion

        #region 事件

        /// <summary>
        /// IO值变化事件
        /// </summary>
        event EventHandler<IOChangedEventArgs> IOChanged;

        /// <summary>
        /// IO批量更新事件
        /// </summary>
        event EventHandler<IOBatchUpdatedEventArgs> BatchUpdated;

        #endregion

        #region IO注册

        /// <summary>
        /// 注册IO点位
        /// </summary>
        void Register(IOPointDefinition definition);

        /// <summary>
        /// 批量注册IO点位
        /// </summary>
        void Register(IEnumerable<IOPointDefinition> definitions);

        /// <summary>
        /// 从PLC地址注册表自动加载
        /// </summary>
        void LoadFromAddressRegistry();

        /// <summary>
        /// 注销IO点位
        /// </summary>
        void Unregister(string ioName);

        /// <summary>
        /// 获取所有IO定义
        /// </summary>
        IReadOnlyList<IOPointDefinition> GetAllDefinitions();

        /// <summary>
        /// 获取指定分组的IO定义
        /// </summary>
        IReadOnlyList<IOPointDefinition> GetDefinitionsByGroup(string group);

        #endregion

        #region 轮询控制

        /// <summary>
        /// 启动IO轮询
        /// </summary>
        void StartPolling();

        /// <summary>
        /// 停止IO轮询
        /// </summary>
        void StopPolling();

        /// <summary>
        /// 手动刷新所有IO
        /// </summary>
        Task RefreshAllAsync(CancellationToken ct = default);

        #endregion

        #region 读取操作

        /// <summary>
        /// 读取位IO（按名称）
        /// </summary>
        bool ReadBit(string ioName);

        /// <summary>
        /// 读取整数IO（按名称）
        /// </summary>
        int ReadInt(string ioName);

        /// <summary>
        /// 读取浮点数IO（按名称）
        /// </summary>
        float ReadFloat(string ioName);

        /// <summary>
        /// 读取泛型IO
        /// </summary>
        T Read<T>(string ioName);

        /// <summary>
        /// 异步读取位IO
        /// </summary>
        Task<bool> ReadBitAsync(string ioName, CancellationToken ct = default);

        /// <summary>
        /// 批量读取位IO
        /// </summary>
        Dictionary<string, bool> ReadBits(params string[] ioNames);

        /// <summary>
        /// 批量读取整数IO
        /// </summary>
        Dictionary<string, int> ReadInts(params string[] ioNames);

        /// <summary>
        /// 获取缓存的IO值（不触发PLC读取）
        /// </summary>
        object GetCachedValue(string ioName);

        /// <summary>
        /// 获取所有缓存的IO值
        /// </summary>
        IReadOnlyDictionary<string, object> GetAllCachedValues();

        #endregion

        #region 写入操作

        /// <summary>
        /// 写入位IO（按名称）
        /// </summary>
        bool WriteBit(string ioName, bool value);

        /// <summary>
        /// 写入整数IO（按名称）
        /// </summary>
        bool WriteInt(string ioName, int value);

        /// <summary>
        /// 写入浮点数IO（按名称）
        /// </summary>
        bool WriteFloat(string ioName, float value);

        /// <summary>
        /// 异步写入位IO
        /// </summary>
        Task<bool> WriteBitAsync(string ioName, bool value, CancellationToken ct = default);

        /// <summary>
        /// 脉冲输出
        /// </summary>
        bool PulseBit(string ioName, int durationMs = 50);

        /// <summary>
        /// 切换位IO状态
        /// </summary>
        bool ToggleBit(string ioName);

        /// <summary>
        /// 批量写入位IO
        /// </summary>
        bool WriteBits(Dictionary<string, bool> values);

        #endregion

        #region 等待操作

        /// <summary>
        /// 等待IO变为指定值
        /// </summary>
        Task<bool> WaitForValueAsync(string ioName, bool expectedValue, TimeSpan timeout, CancellationToken ct = default);

        /// <summary>
        /// 等待IO上升沿
        /// </summary>
        Task<bool> WaitForRisingEdgeAsync(string ioName, TimeSpan timeout, CancellationToken ct = default);

        /// <summary>
        /// 等待IO下降沿
        /// </summary>
        Task<bool> WaitForFallingEdgeAsync(string ioName, TimeSpan timeout, CancellationToken ct = default);

        /// <summary>
        /// 等待多个IO全部为指定值
        /// </summary>
        Task<bool> WaitForAllAsync(Dictionary<string, bool> conditions, TimeSpan timeout, CancellationToken ct = default);

        /// <summary>
        /// 等待任意一个IO为指定值
        /// </summary>
        Task<(bool success, string triggeredIO)> WaitForAnyAsync(Dictionary<string, bool> conditions, TimeSpan timeout, CancellationToken ct = default);

        #endregion

        #region 查询

        /// <summary>
        /// 检查IO点是否存在
        /// </summary>
        bool Contains(string ioName);

        /// <summary>
        /// 获取IO定义
        /// </summary>
        IOPointDefinition GetDefinition(string ioName);

        /// <summary>
        /// 按分组获取IO名称
        /// </summary>
        IReadOnlyList<string> GetIONamesByGroup(string group);

        /// <summary>
        /// 获取所有分组名称
        /// </summary>
        IReadOnlyList<string> GetAllGroups();

        /// <summary>
        /// 搜索IO点（支持模糊匹配）
        /// </summary>
        IReadOnlyList<IOPointDefinition> Search(string keyword);

        #endregion

        #region 监控面板数据

        /// <summary>
        /// 获取监控面板数据
        /// </summary>
        IOMonitorPanelData GetMonitorPanelData(string group = null);

        #endregion
    }

    /// <summary>
    /// IO点位定义
    /// </summary>
    public class IOPointDefinition
    {
        /// <summary>
        /// IO名称（唯一标识）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 显示标签
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 分组
        /// </summary>
        public string Group { get; set; }

        /// <summary>
        /// 子分组
        /// </summary>
        public string SubGroup { get; set; }

        /// <summary>
        /// 数据类型
        /// </summary>
        public IODataType DataType { get; set; } = IODataType.Bit;

        /// <summary>
        /// PLC数据块编号
        /// </summary>
        public int DBNumber { get; set; } = 3;

        /// <summary>
        /// PLC起始地址
        /// </summary>
        public int StartAddress { get; set; }

        /// <summary>
        /// 位位置（仅Bit类型）
        /// </summary>
        public int BitPosition { get; set; }

        /// <summary>
        /// 是否可写
        /// </summary>
        public bool IsWritable { get; set; } = false;

        /// <summary>
        /// 是否在UI中显示
        /// </summary>
        public bool ShowInUI { get; set; } = true;

        /// <summary>
        /// UI排序顺序
        /// </summary>
        public int UIOrder { get; set; }

        /// <summary>
        /// 单位
        /// </summary>
        public string Unit { get; set; }

        /// <summary>
        /// 小数位数（仅浮点数）
        /// </summary>
        public int DecimalPlaces { get; set; } = 2;

        /// <summary>
        /// 最小值
        /// </summary>
        public double? MinValue { get; set; }

        /// <summary>
        /// 最大值
        /// </summary>
        public double? MaxValue { get; set; }

        /// <summary>
        /// 获取完整的PLC地址
        /// </summary>
        public string GetPLCAddress()
        {
            string prefix = DBNumber == 3 ? "M" : $"DB{DBNumber}.";
            switch (DataType)
            {
                case IODataType.Bit:
                    return $"{prefix}{StartAddress}.{BitPosition}";
                case IODataType.Int16:
                    return $"{prefix}W{StartAddress}";
                case IODataType.Int32:
                case IODataType.Float:
                    return $"{prefix}D{StartAddress}";
                default:
                    return $"{prefix}{StartAddress}";
            }
        }
    }

    /// <summary>
    /// IO数据类型
    /// </summary>
    public enum IODataType
    {
        Bit,
        Byte,
        Int16,
        Int32,
        Float
    }

    /// <summary>
    /// IO监控面板数据
    /// </summary>
    public class IOMonitorPanelData
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public int TotalCount { get; set; }
        public int InputCount { get; set; }
        public int OutputCount { get; set; }
        public List<IOGroupData> Groups { get; set; } = new List<IOGroupData>();
    }

    /// <summary>
    /// IO分组数据
    /// </summary>
    public class IOGroupData
    {
        public string GroupName { get; set; }
        public List<IOPointData> Points { get; set; } = new List<IOPointData>();
    }

    /// <summary>
    /// IO点位数据
    /// </summary>
    public class IOPointData
    {
        public string Name { get; set; }
        public string Label { get; set; }
        public string Address { get; set; }
        public object Value { get; set; }
        public IODataType DataType { get; set; }
        public bool IsWritable { get; set; }
        public string Unit { get; set; }
    }
}