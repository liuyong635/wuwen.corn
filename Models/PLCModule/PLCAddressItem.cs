using SeedCut.Framework.Models;
using System;
using System.Collections.Generic;
using System.Net;

namespace SeedCut.Models.PLCModule
{
    /// <summary>
    /// PLC地址数据类型枚举
    /// </summary>
    public enum PLCAddressType
    {
        Bit,
        Byte,
        Int16,
        Int32,
        Float,
        Time
    }

    /// <summary>
    /// ✅ 新增：PLC内存区域类型枚举
    /// </summary>
    public enum PLCMemoryArea
    {
        /// <summary>
        /// M区 - 内部标志位（默认）
        /// </summary>
        Marker = 0,

        /// <summary>
        /// I区 - 物理输入
        /// </summary>
        Input = 1,

        /// <summary>
        /// Q区 - 物理输出
        /// </summary>
        Output = 2,

        /// <summary>
        /// DB区 - 数据块
        /// </summary>
        DataBlock = 3
    }

    /// <summary>
    /// PLC地址配置项 - 集成地址信息和UI配置
    /// ✅ 增强版：支持I/Q/M/DB四种内存区域
    /// </summary>
    public class PLCAddressItem
    {
        #region 地址基本信息

        /// <summary>
        /// 地址名称（唯一标识）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 描述信息
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 数据块编号（仅DB区使用，其他区域忽略此值）
        /// </summary>
        public int DBNumber { get; set; } = 0;

        /// <summary>
        /// ✅ 新增：内存区域类型（I/Q/M/DB）
        /// </summary>
        public PLCMemoryArea MemoryArea { get; set; } = PLCMemoryArea.Marker;

        /// <summary>
        /// 起始地址
        /// </summary>
        public int StartAddress { get; set; }

        /// <summary>
        /// 位位置（-1表示非位类型）
        /// </summary>
        public int BitPosition { get; set; } = -1;

        /// <summary>
        /// 数据类型
        /// </summary>
        public PLCAddressType DataType { get; set; }

        /// <summary>
        /// 是否可写
        /// </summary>
        public bool Writable { get; set; } = true;

        /// <summary>
        /// 是否可读
        /// </summary>
        public bool Readable { get; set; } = true;

        /// <summary>
        /// 分组（模块名称）
        /// </summary>
        public string Group { get; set; }

        /// <summary>
        /// 子分组（区域名称）
        /// </summary>
        public string SubGroup { get; set; }

        #endregion


        #region 新增报警相关属性（全部使用默认值，不影响现有代码）

        /// <summary>
        /// 是否为报警信号
        /// 默认false，现有地址不受影响
        /// </summary>
        public bool IsAlarmSignal { get; set; } = false;

        /// <summary>
        /// 报警触发条件：true=信号为1时报警，false=信号为0时报警
        /// 默认true（大多数报警信号是高电平触发）
        /// </summary>
        public bool AlarmTriggerOnHigh { get; set; } = true;

        /// <summary>
        /// 报警级别（仅当IsAlarmSignal=true时有效）
        /// 默认Error（红色），可选：Warning（黄色）、Critical（深红色）
        /// </summary>
        public AlarmLevel AlarmLevel { get; set; } = AlarmLevel.Error;

        /// <summary>
        /// 是否显示报警弹窗
        /// 默认true
        /// </summary>
        public bool AlarmShowPopup { get; set; } = true;

        /// <summary>
        /// 是否播放报警声音
        /// 默认false
        /// </summary>
        public bool AlarmPlaySound { get; set; } = false;

        /// <summary>
        /// 报警建议处理方式
        /// </summary>
        public string AlarmSuggestedAction { get; set; }

        #endregion

        #region UI配置

        /// <summary>
        /// 是否在UI上显示
        /// </summary>
        public bool ShowInUI { get; set; } = false;

        /// <summary>
        /// UI控件类型
        /// </summary>
        public ControlType ControlType { get; set; } = ControlType.Indicator;

        /// <summary>
        /// UI显示标签（如果为空则使用Name）
        /// </summary>
        public string UILabel { get; set; }

        /// <summary>
        /// 工具提示
        /// </summary>
        public string Tooltip { get; set; }

        /// <summary>
        /// 是否只读（UI层面）
        /// </summary>
        public bool IsReadOnly { get; set; } = false;

        /// <summary>
        /// UI排序顺序（同一区域内）
        /// </summary>
        public int UIOrder { get; set; } = 0;

        /// <summary>
        /// UI样式Key（可选）
        /// </summary>
        public string StyleKey { get; set; }

        /// <summary>
        /// 额外的UI配置（用于特殊控件）
        /// </summary>
        public Dictionary<string, object> ExtraUIConfig { get; set; } = new Dictionary<string, object>();

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取实际的UI标签
        /// </summary>
        public string GetUILabel() => string.IsNullOrEmpty(UILabel) ? Name : UILabel;

        /// <summary>
        /// ✅ 修改：获取完整的西门子地址表示（支持I/Q/M/DB）
        /// </summary>
        public string GetSiemensAddress()
        {
            string areaPrefix;
            switch (MemoryArea)
            {
                case PLCMemoryArea.Input:
                    areaPrefix = "I";
                    break;
                case PLCMemoryArea.Output:
                    areaPrefix = "Q";
                    break;
                case PLCMemoryArea.DataBlock:
                    areaPrefix = $"DB{DBNumber}.DBX";
                    break;
                case PLCMemoryArea.Marker:
                default:
                    areaPrefix = "M";
                    break;
            }

            switch (DataType)
            {
                case PLCAddressType.Bit:
                    return $"%{areaPrefix}{StartAddress}.{BitPosition}";
                case PLCAddressType.Byte:
                    if (MemoryArea == PLCMemoryArea.DataBlock)
                        return $"%DB{DBNumber}.DBB{StartAddress}";
                    return $"%{areaPrefix}B{StartAddress}";
                case PLCAddressType.Int16:
                    if (MemoryArea == PLCMemoryArea.DataBlock)
                        return $"%DB{DBNumber}.DBW{StartAddress}";
                    return $"%{areaPrefix}W{StartAddress}";
                case PLCAddressType.Int32:
                case PLCAddressType.Float:
                    if (MemoryArea == PLCMemoryArea.DataBlock)
                        return $"%DB{DBNumber}.DBD{StartAddress}";
                    return $"%{areaPrefix}D{StartAddress}";
                default:
                    return $"%{areaPrefix}{StartAddress}";
            }
        }

        #endregion

        #region 静态工厂方法

        /// <summary>
        /// ✅ 增强版：创建位地址（支持指定内存区域）
        /// </summary>
        public static PLCAddressItem CreateBit(
            string name,
            string group,
            string subGroup,
            int dbNumber,
            int startAddress,
            int bitPosition,
            bool writable = true,
            bool showInUI = false,
            ControlType controlType = ControlType.Indicator,
            string uiLabel = null,
            string tooltip = null,
            int uiOrder = 0,
            Dictionary<string, object> extraUIConfig = null,
            PLCMemoryArea memoryArea = PLCMemoryArea.Marker)  // ✅ 新增参数
        {
            return new PLCAddressItem
            {
                Name = name,
                Description = name,
                Group = group,
                SubGroup = subGroup,
                DBNumber = dbNumber,
                MemoryArea = memoryArea,  // ✅ 设置内存区域
                StartAddress = startAddress,
                BitPosition = bitPosition,
                DataType = PLCAddressType.Bit,
                Writable = writable,
                Readable = true,
                ShowInUI = showInUI,
                ControlType = controlType,
                UILabel = uiLabel,
                Tooltip = tooltip ?? name,
                IsReadOnly = !writable,
                UIOrder = uiOrder,
                ExtraUIConfig = extraUIConfig ?? new Dictionary<string, object>()
            };
        }

        /// <summary>
        /// 创建浮点数地址
        /// </summary>
        public static PLCAddressItem CreateFloat(
            string name,
            string group,
            string subGroup,
            int dbNumber,
            int startAddress,
            bool writable = true,
            bool showInUI = false,
            ControlType controlType = ControlType.NumberDisplay,
            string uiLabel = null,
            string tooltip = null,
            int uiOrder = 0,
            Dictionary<string, object> extraUIConfig = null,
            PLCMemoryArea memoryArea = PLCMemoryArea.Marker)
        {
            return new PLCAddressItem
            {
                Name = name,
                Description = name,
                Group = group,
                SubGroup = subGroup,
                DBNumber = dbNumber,
                MemoryArea = memoryArea,
                StartAddress = startAddress,
                BitPosition = -1,
                DataType = PLCAddressType.Float,
                Writable = writable,
                Readable = true,
                ShowInUI = showInUI,
                ControlType = controlType,
                UILabel = uiLabel,
                Tooltip = tooltip ?? name,
                IsReadOnly = !writable,
                UIOrder = uiOrder,
                ExtraUIConfig = extraUIConfig ?? new Dictionary<string, object>()
            };
        }

        /// <summary>
        /// 创建整数地址
        /// </summary>
        public static PLCAddressItem CreateInt16(
            string name,
            string group,
            string subGroup,
            int dbNumber,
            int startAddress,
            bool writable = true,
            bool showInUI = false,
            ControlType controlType = ControlType.NumberDisplay,
            string uiLabel = null,
            string tooltip = null,
            int uiOrder = 0,
            Dictionary<string, object> extraUIConfig = null,
            PLCMemoryArea memoryArea = PLCMemoryArea.Marker)
        {
            return new PLCAddressItem
            {
                Name = name,
                Description = name,
                Group = group,
                SubGroup = subGroup,
                DBNumber = dbNumber,
                MemoryArea = memoryArea,
                StartAddress = startAddress,
                BitPosition = -1,
                DataType = PLCAddressType.Int16,
                Writable = writable,
                Readable = true,
                ShowInUI = showInUI,
                ControlType = controlType,
                UILabel = uiLabel,
                Tooltip = tooltip ?? name,
                IsReadOnly = !writable,
                UIOrder = uiOrder,
                ExtraUIConfig = extraUIConfig ?? new Dictionary<string, object>()
            };
        }

        /// <summary>
        /// 创建Int32地址
        /// </summary>
        public static PLCAddressItem CreateInt32(
            string name,
            string group,
            string subGroup,
            int dbNumber,
            int startAddress,
            bool writable = true,
            bool showInUI = false,
            ControlType controlType = ControlType.NumberDisplay,
            string uiLabel = null,
            string tooltip = null,
            int uiOrder = 0,
            Dictionary<string, object> extraUIConfig = null,
            PLCMemoryArea memoryArea = PLCMemoryArea.Marker)
        {
            return new PLCAddressItem
            {
                Name = name,
                Description = name,
                Group = group,
                SubGroup = subGroup,
                DBNumber = dbNumber,
                MemoryArea = memoryArea,
                StartAddress = startAddress,
                BitPosition = -1,
                DataType = PLCAddressType.Int32,
                Writable = writable,
                Readable = true,
                ShowInUI = showInUI,
                ControlType = controlType,
                UILabel = uiLabel,
                Tooltip = tooltip ?? name,
                IsReadOnly = !writable,
                UIOrder = uiOrder,
                ExtraUIConfig = extraUIConfig ?? new Dictionary<string, object>()
            };
        }

        /// <summary>
        /// 创建Byte地址
        /// </summary>
        public static PLCAddressItem CreateByte(
            string name,
            string group,
            string subGroup,
            int dbNumber,
            int startAddress,
            bool writable = true,
            bool showInUI = false,
            ControlType controlType = ControlType.NumberDisplay,
            string uiLabel = null,
            string tooltip = null,
            int uiOrder = 0,
            Dictionary<string, object> extraUIConfig = null,
            PLCMemoryArea memoryArea = PLCMemoryArea.Marker)
        {
            return new PLCAddressItem
            {
                Name = name,
                Description = name,
                Group = group,
                SubGroup = subGroup,
                DBNumber = dbNumber,
                MemoryArea = memoryArea,
                StartAddress = startAddress,
                BitPosition = -1,
                DataType = PLCAddressType.Byte,
                Writable = writable,
                Readable = true,
                ShowInUI = showInUI,
                ControlType = controlType,
                UILabel = uiLabel,
                Tooltip = tooltip ?? name,
                IsReadOnly = !writable,
                UIOrder = uiOrder,
                ExtraUIConfig = extraUIConfig ?? new Dictionary<string, object>()
            };
        }

        #endregion


        public PLCAddressItem()
        {
        }





        #region 静态工厂方法（可选，方便创建报警信号）



        /// <summary>
        /// 创建报警信号地址（信号=1时触发报警）
        /// 适用于：报警标志、故障信号、急停信号、极限信号等
        /// </summary>
        public static PLCAddressItem CreateAlarmHigh(
            string name,
            string group,
            string subGroup,
            int dbNumber,
            int startAddress,
            int bitPosition,
            AlarmLevel level = AlarmLevel.Error,
            string description = null,
            string suggestedAction = null,
            PLCMemoryArea memoryArea = PLCMemoryArea.Marker)
        {
            return new PLCAddressItem
            {
                Name = name,
                Description = description ?? name,
                Group = group,
                SubGroup = subGroup,
                DBNumber = dbNumber,
                MemoryArea = memoryArea,
                StartAddress = startAddress,
                BitPosition = bitPosition,
                DataType = PLCAddressType.Bit,
                Writable = false,
                Readable = true,
                ShowInUI = false,
                IsReadOnly = true,
                // 报警配置
                IsAlarmSignal = true,
                AlarmTriggerOnHigh = true,  // 信号=1时触发
                AlarmLevel = level,
                AlarmShowPopup = level >= AlarmLevel.Error,
                AlarmPlaySound = level >= AlarmLevel.Critical,
                AlarmSuggestedAction = suggestedAction
            };
        }

        /// <summary>
        /// 创建报警信号地址（信号=0时触发报警）
        /// 适用于：气压正常、通讯心跳、安全门关闭、设备就绪等"正常信号"
        /// </summary>
        public static PLCAddressItem CreateAlarmLow(
            string name,
            string group,
            string subGroup,
            int dbNumber,
            int startAddress,
            int bitPosition,
            AlarmLevel level = AlarmLevel.Error,
            string description = null,
            string suggestedAction = null,
            PLCMemoryArea memoryArea = PLCMemoryArea.Marker)
        {
            return new PLCAddressItem
            {
                Name = name,
                Description = description ?? name,
                Group = group,
                SubGroup = subGroup,
                DBNumber = dbNumber,
                MemoryArea = memoryArea,
                StartAddress = startAddress,
                BitPosition = bitPosition,
                DataType = PLCAddressType.Bit,
                Writable = false,
                Readable = true,
                ShowInUI = false,
                IsReadOnly = true,
                // 报警配置
                IsAlarmSignal = true,
                AlarmTriggerOnHigh = false,  // 信号=0时触发
                AlarmLevel = level,
                AlarmShowPopup = level >= AlarmLevel.Error,
                AlarmPlaySound = level >= AlarmLevel.Critical,
                AlarmSuggestedAction = suggestedAction
            };
        }

        #endregion

        public override string ToString()
        {
            var alarmInfo = IsAlarmSignal ? $" [报警:{(AlarmTriggerOnHigh ? "高" : "低")}触发]" : "";
            return $"{Name} ({GetSiemensAddress()}){alarmInfo}";
        }
    }
}