using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SeedCut.Framework.Models;
using SeedCut.Models;  // 添加此行以支持 AlarmLevel

namespace SeedCut.Models.PLCModule
{
    /// <summary>
    /// CSV行数据模型 - 完整版，包含PLCAddressItem的所有属性
    /// ✅ 增强版：支持I/Q/M/DB内存区域
    /// </summary>
    public class PLCAddressCsvRow
    {
        // ========== 模块信息 ==========
        public string ModuleId { get; set; }
        public string ModuleName { get; set; }
        public int SortOrder { get; set; }

        // ========== 地址基本信息 ==========
        public string AddressName { get; set; }
        public int DBNumber { get; set; }
        public int StartAddress { get; set; }
        public PLCMemoryArea MemoryArea { get; set; } = PLCMemoryArea.Marker;  // ✅ 新增
        public string DataType { get; set; }
        public int BitPosition { get; set; }
        public bool Writable { get; set; }
        public bool Readable { get; set; } = true;
        public string Description { get; set; }

        // ========== UI配置 ==========
        public bool ShowInUI { get; set; }
        public string SubGroup { get; set; }
        public int UIOrder { get; set; }
        public string ControlType { get; set; }
        public string UILabel { get; set; }
        public string Tooltip { get; set; }
        public string StyleKey { get; set; }

        // ========== UI额外配置（数值控件用） ==========
        public string Unit { get; set; }
        public int DecimalPlaces { get; set; }
        public double MinValue { get; set; }
        public double MaxValue { get; set; }

        // ========== 报警相关属性 ==========
        public bool IsAlarmSignal { get; set; } = false;
        public bool AlarmTriggerOnHigh { get; set; } = true;
        public string AlarmLevel { get; set; } = "Error";
        public bool AlarmShowPopup { get; set; } = true;
        public bool AlarmPlaySound { get; set; } = false;
        public string AlarmSuggestedAction { get; set; }
    }

    /// <summary>
    /// PLC配置CSV导入器
    /// ✅ 增强版：支持解析I/Q前缀的StartAddress
    /// 
    /// StartAddress格式：
    /// - "2700"  → M区，地址2700（默认，向后兼容）
    /// - "I0"    → I区（输入），地址0
    /// - "Q6"    → Q区（输出），地址6
    /// - "DB1.0" → DB1数据块，地址0（可选支持）
    /// </summary>
    public class PLCConfigImporter
    {
        private readonly string _csvFilePath;
        private readonly PLCFeatureFlags _featureFlags;

        public PLCConfigImporter(string csvFilePath, PLCFeatureFlags featureFlags = null)
        {
            _csvFilePath = csvFilePath ?? throw new ArgumentNullException(nameof(csvFilePath));
            _featureFlags = featureFlags ?? PLCFeatureFlags.CreateDefault();
        }

        /// <summary>
        /// 从CSV文件导入所有配置
        /// </summary>
        public List<PLCAddressCsvRow> ImportFromCsv()
        {
            var rows = new List<PLCAddressCsvRow>();

            if (!File.Exists(_csvFilePath))
            {
                System.Diagnostics.Debug.WriteLine($"✗ CSV文件不存在: {_csvFilePath}");
                return rows;
            }

            try
            {
                using (var reader = new StreamReader(_csvFilePath))
                {
                    // 读取表头
                    var headerLine = reader.ReadLine();
                    if (string.IsNullOrEmpty(headerLine))
                    {
                        System.Diagnostics.Debug.WriteLine("✗ CSV文件为空");
                        return rows;
                    }

                    var headers = headerLine.Split(',');
                    var headerMap = new Dictionary<string, int>();
                    for (int i = 0; i < headers.Length; i++)
                    {
                        headerMap[headers[i].Trim()] = i;
                    }

                    // 读取数据行
                    int lineNumber = 1;
                    while (!reader.EndOfStream)
                    {
                        lineNumber++;
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            var values = ParseCsvLine(line);
                            var row = ParseRow(values, headerMap);
                            if (row != null)
                            {
                                rows.Add(row);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"✗ 解析CSV第{lineNumber}行失败: {ex.Message}");
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"✓ 成功导入 {rows.Count} 条PLC地址配置");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ 读取CSV文件失败: {ex.Message}");
            }

            return rows;
        }

        /// <summary>
        /// 解析CSV行（处理引号内的逗号）
        /// </summary>
        private List<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            var current = "";
            var inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    values.Add(current.Trim());
                    current = "";
                }
                else
                {
                    current += c;
                }
            }
            values.Add(current.Trim());

            return values;
        }

        /// <summary>
        /// ✅ 新增：解析带前缀的地址字符串
        /// 返回 (内存区域, 地址数值)
        /// 
        /// 示例：
        /// - "2700"  → (Marker, 2700)
        /// - "I0"    → (Input, 0)
        /// - "Q6"    → (Output, 6)
        /// </summary>
        private (PLCMemoryArea area, int address) ParseAddressWithPrefix(string addressStr)
        {
            if (string.IsNullOrWhiteSpace(addressStr))
                return (PLCMemoryArea.Marker, 0);

            addressStr = addressStr.Trim().ToUpper();

            // 检查I前缀（输入）
            if (addressStr.StartsWith("I"))
            {
                var numPart = addressStr.Substring(1);
                if (int.TryParse(numPart, out var addr))
                {
                    return (PLCMemoryArea.Input, addr);
                }
            }

            // 检查Q前缀（输出）
            if (addressStr.StartsWith("Q"))
            {
                var numPart = addressStr.Substring(1);
                if (int.TryParse(numPart, out var addr))
                {
                    return (PLCMemoryArea.Output, addr);
                }
            }

            // 检查M前缀（标志位，显式指定）
            if (addressStr.StartsWith("M"))
            {
                var numPart = addressStr.Substring(1);
                if (int.TryParse(numPart, out var addr))
                {
                    return (PLCMemoryArea.Marker, addr);
                }
            }

            // 无前缀，默认为M区（向后兼容）
            if (int.TryParse(addressStr, out var defaultAddr))
            {
                return (PLCMemoryArea.Marker, defaultAddr);
            }

            // 解析失败
            System.Diagnostics.Debug.WriteLine($"⚠️ 无法解析地址: {addressStr}，使用默认值M0");
            return (PLCMemoryArea.Marker, 0);
        }

        /// <summary>
        /// 解析单行数据
        /// </summary>
        private PLCAddressCsvRow ParseRow(List<string> values, Dictionary<string, int> headerMap)
        {
            string GetValue(string columnName)
            {
                return headerMap.ContainsKey(columnName) && values.Count > headerMap[columnName]
                    ? values[headerMap[columnName]]
                    : string.Empty;
            }

            int GetInt(string columnName, int defaultValue = 0)
            {
                var value = GetValue(columnName);
                return int.TryParse(value, out var result) ? result : defaultValue;
            }

            double GetDouble(string columnName, double defaultValue = 0)
            {
                var value = GetValue(columnName);
                return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
                    ? result : defaultValue;
            }

            bool GetBool(string columnName, bool defaultValue = false)
            {
                var value = GetValue(columnName).ToUpper();
                if (value == "TRUE" || value == "1" || value == "YES") return true;
                if (value == "FALSE" || value == "0" || value == "NO") return false;
                return defaultValue;
            }

            // ✅ 解析StartAddress（支持I/Q前缀）
            var startAddressStr = GetValue("StartAddress");
            var (memoryArea, startAddress) = ParseAddressWithPrefix(startAddressStr);

            return new PLCAddressCsvRow
            {
                // 模块信息
                ModuleId = GetValue("ModuleId"),
                ModuleName = GetValue("ModuleName"),
                SortOrder = GetInt("SortOrder", 999),

                // 地址基本信息
                AddressName = GetValue("AddressName"),
                DBNumber = GetInt("DBNumber"),
                StartAddress = startAddress,           // ✅ 使用解析后的地址
                MemoryArea = memoryArea,               // ✅ 使用解析后的内存区域
                DataType = GetValue("DataType"),
                BitPosition = GetInt("BitPosition"),
                Writable = GetBool("Writable", true),
                Readable = GetBool("Readable", true),
                Description = GetValue("Description"),

                // UI配置
                ShowInUI = GetBool("ShowInUI"),
                SubGroup = GetValue("SubGroup"),
                UIOrder = GetInt("UIOrder"),
                ControlType = GetValue("ControlType"),
                UILabel = GetValue("UILabel"),
                Tooltip = GetValue("Tooltip"),
                StyleKey = GetValue("StyleKey"),

                // UI额外配置
                Unit = GetValue("Unit"),
                DecimalPlaces = GetInt("DecimalPlaces"),
                MinValue = GetDouble("MinValue"),
                MaxValue = GetDouble("MaxValue"),

                // 报警相关属性
                IsAlarmSignal = GetBool("IsAlarmSignal", false),
                AlarmTriggerOnHigh = GetBool("AlarmTriggerOnHigh", true),
                AlarmLevel = string.IsNullOrEmpty(GetValue("AlarmLevel")) ? "Error" : GetValue("AlarmLevel"),
                AlarmShowPopup = GetBool("AlarmShowPopup", true),
                AlarmPlaySound = GetBool("AlarmPlaySound", false),
                AlarmSuggestedAction = GetValue("AlarmSuggestedAction")
            };
        }

        /// <summary>
        /// 将CSV行转换为PLCAddressItem（完整版）
        /// ✅ 增强版：支持内存区域
        /// </summary>
        public PLCAddressItem ConvertToPLCAddressItem(PLCAddressCsvRow row)
        {
            var addressGroup = row.ModuleName; // 使用模块名称作为Group
            PLCAddressItem address = null;

            // 根据DataType创建不同类型的地址
            switch (row.DataType.ToLower())
            {
                case "bit":
                case "bool":
                    address = PLCAddressItem.CreateBit(
                        name: row.AddressName,
                        group: addressGroup,
                        subGroup: row.SubGroup,
                        dbNumber: row.DBNumber,
                        startAddress: row.StartAddress,
                        bitPosition: row.BitPosition,
                        writable: row.Writable,
                        showInUI: row.ShowInUI && _featureFlags.EnableUI,
                        controlType: ParseControlType(row.ControlType),
                        uiLabel: row.UILabel,
                        tooltip: row.Tooltip,
                        uiOrder: row.UIOrder,
                        extraUIConfig: CreateExtraConfig(row),
                        memoryArea: row.MemoryArea  // ✅ 传递内存区域
                    );
                    break;

                case "int16":
                case "int":
                case "uint16":
                case "uint":
                    address = PLCAddressItem.CreateInt16(
                        name: row.AddressName,
                        group: addressGroup,
                        subGroup: row.SubGroup,
                        dbNumber: row.DBNumber,
                        startAddress: row.StartAddress,
                        writable: row.Writable,
                        showInUI: row.ShowInUI && _featureFlags.EnableUI,
                        controlType: ParseControlType(row.ControlType),
                        uiLabel: row.UILabel,
                        tooltip: row.Tooltip,
                        uiOrder: row.UIOrder,
                        extraUIConfig: CreateExtraConfig(row),
                        memoryArea: row.MemoryArea
                    );
                    break;

                case "int32":
                    address = PLCAddressItem.CreateInt32(
                        name: row.AddressName,
                        group: addressGroup,
                        subGroup: row.SubGroup,
                        dbNumber: row.DBNumber,
                        startAddress: row.StartAddress,
                        writable: row.Writable,
                        showInUI: row.ShowInUI && _featureFlags.EnableUI,
                        controlType: ParseControlType(row.ControlType),
                        uiLabel: row.UILabel,
                        tooltip: row.Tooltip,
                        uiOrder: row.UIOrder,
                        extraUIConfig: CreateExtraConfig(row),
                        memoryArea: row.MemoryArea
                    );
                    break;

                case "byte":
                    address = PLCAddressItem.CreateByte(
                        name: row.AddressName,
                        group: addressGroup,
                        subGroup: row.SubGroup,
                        dbNumber: row.DBNumber,
                        startAddress: row.StartAddress,
                        writable: row.Writable,
                        showInUI: row.ShowInUI && _featureFlags.EnableUI,
                        controlType: ParseControlType(row.ControlType),
                        uiLabel: row.UILabel,
                        tooltip: row.Tooltip,
                        uiOrder: row.UIOrder,
                        extraUIConfig: CreateExtraConfig(row),
                        memoryArea: row.MemoryArea
                    );
                    break;

                case "float":
                case "real":
                    address = PLCAddressItem.CreateFloat(
                        name: row.AddressName,
                        group: addressGroup,
                        subGroup: row.SubGroup,
                        dbNumber: row.DBNumber,
                        startAddress: row.StartAddress,
                        writable: row.Writable,
                        showInUI: row.ShowInUI && _featureFlags.EnableUI,
                        controlType: ParseControlType(row.ControlType),
                        uiLabel: row.UILabel,
                        tooltip: row.Tooltip,
                        uiOrder: row.UIOrder,
                        extraUIConfig: CreateExtraConfig(row),
                        memoryArea: row.MemoryArea
                    );
                    break;

                default:
                    System.Diagnostics.Debug.WriteLine($"⚠️ 未知数据类型: {row.DataType} for {row.AddressName}");
                    break;
            }

            // 应用额外的属性
            if (address != null)
            {
                // 应用Readable属性
                address.Readable = row.Readable;

                // 应用Description
                if (!string.IsNullOrEmpty(row.Description))
                {
                    address.Description = row.Description;
                }

                // 应用StyleKey
                if (!string.IsNullOrEmpty(row.StyleKey))
                {
                    address.StyleKey = row.StyleKey;
                }

                // 应用报警相关属性
                if (_featureFlags.EnableAlarm && row.IsAlarmSignal)
                {
                    address.IsAlarmSignal = true;
                    address.AlarmTriggerOnHigh = row.AlarmTriggerOnHigh;
                    address.AlarmLevel = ParseAlarmLevel(row.AlarmLevel);
                    address.AlarmShowPopup = row.AlarmShowPopup;
                    address.AlarmPlaySound = row.AlarmPlaySound;

                    if (!string.IsNullOrEmpty(row.AlarmSuggestedAction))
                    {
                        address.AlarmSuggestedAction = row.AlarmSuggestedAction;
                    }
                }
            }

            return address;
        }

        /// <summary>
        /// 解析报警级别
        /// </summary>
        private AlarmLevel ParseAlarmLevel(string levelStr)
        {
            if (string.IsNullOrEmpty(levelStr))
                return AlarmLevel.Error;

            if (Enum.TryParse<AlarmLevel>(levelStr, true, out var level))
                return level;

            return AlarmLevel.Error;
        }

        /// <summary>
        /// 解析控件类型
        /// </summary>
        private ControlType ParseControlType(string controlTypeStr)
        {
            if (Enum.TryParse<ControlType>(controlTypeStr, true, out var controlType))
            {
                return controlType;
            }
            return ControlType.TextDisplay;
        }

        /// <summary>
        /// 创建额外UI配置
        /// </summary>
        private Dictionary<string, object> CreateExtraConfig(PLCAddressCsvRow row)
        {
            var config = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(row.Unit))
                config["Unit"] = row.Unit;

            if (row.DecimalPlaces >= 0)
                config["DecimalPlaces"] = row.DecimalPlaces;

            if (row.MinValue != 0 || row.MaxValue != 0)
            {
                config["MinValue"] = row.MinValue;
                config["MaxValue"] = row.MaxValue;
            }

            return config;
        }

        /// <summary>
        /// 按模块分组
        /// </summary>
        public Dictionary<string, List<PLCAddressCsvRow>> GroupByModule(List<PLCAddressCsvRow> rows)
        {
            return rows.GroupBy(r => r.ModuleId)
                      .ToDictionary(g => g.Key, g => g.ToList());
        }
    }
}