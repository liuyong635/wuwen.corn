using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedCut.Models.PLCModule
{
    /// <summary>
    /// CSV驱动的PLC模块定义
    /// 这个类从CSV数据中动态创建模块，无需手写每个模块类
    /// </summary>
    public class CsvDrivenModuleDefinition : IPLCModuleDefinition
    {
        private readonly string _moduleId;
        private readonly string _moduleName;
        private readonly int _sortOrder;
        private readonly List<PLCAddressCsvRow> _csvRows;
        private readonly PLCConfigImporter _importer;

        public CsvDrivenModuleDefinition(
            string moduleId,
            string moduleName,
            int sortOrder,
            List<PLCAddressCsvRow> csvRows,
            PLCConfigImporter importer)
        {
            _moduleId = moduleId ?? throw new ArgumentNullException(nameof(moduleId));
            _moduleName = moduleName ?? throw new ArgumentNullException(nameof(moduleName));
            _sortOrder = sortOrder;
            _csvRows = csvRows ?? throw new ArgumentNullException(nameof(csvRows));
            _importer = importer ?? throw new ArgumentNullException(nameof(importer));
        }

        /// <summary>
        /// 获取该模块的所有地址配置
        /// </summary>
        public List<PLCAddressItem> GetAllAddresses()
        {
            var addresses = new List<PLCAddressItem>();

            foreach (var row in _csvRows)
            {
                try
                {
                    var address = _importer.ConvertToPLCAddressItem(row);
                    if (address != null)
                    {
                        addresses.Add(address);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"✗ 转换地址失败 [{row.AddressName}]: {ex.Message}");
                }
            }

            return addresses;
        }

        /// <summary>
        /// 创建模块元数据
        /// </summary>
        public PLCModuleMetadata CreateModule()
        {
            var addresses = GetAllAddresses();

            // 按SubGroup分组，创建控制区域
            var sections = addresses
                .Where(a => a.ShowInUI)
                .GroupBy(a => a.SubGroup)
                .OrderBy(g => GetSubGroupOrder(g.Key))
                .Select(g => new PLCControlSection
                {
                    SectionId = g.Key.ToLower().Replace(" ", "_").Replace("(", "").Replace(")", ""),
                    Title = $"● {g.Key}",
                    Type = DetermineSectionType(g.Key),
                    Layout = GetLayoutConfig(g.Key),
                    ControlItems = g.OrderBy(a => a.UIOrder).Select(addr => new PLCControlItemConfig
                    {
                        ItemId = addr.Name,
                        Label = addr.GetUILabel(),
                        AddressName = addr.Name,
                        ControlType = addr.ControlType,
                        IsReadOnly = addr.IsReadOnly,
                        Tooltip = addr.Tooltip,
                        ExtraConfig = addr.ExtraUIConfig
                    }).ToList()
                }).ToList();

            return new PLCModuleMetadata
            {
                ModuleId = _moduleId,
                DisplayName = _moduleName,
                Description = $"{_moduleName}",
                AddressGroup = _moduleName,
                IsEnabled = true,
                SortOrder = _sortOrder,
                ControlSections = sections
            };
        }

        #region 辅助方法

        /// <summary>
        /// 根据SubGroup确定Section类型
        /// </summary>
        private SectionType DetermineSectionType(string subGroup)
        {
            var lower = subGroup.ToLower();
            if (lower.Contains("控制") || lower.Contains("启动") || lower.Contains("按钮"))
                return SectionType.ButtonControl;
            if (lower.Contains("参数") || lower.Contains("设置"))
                return SectionType.ParameterSetting;
            if (lower.Contains("状态") || lower.Contains("标志") || lower.Contains("反馈"))
                return SectionType.StatusDisplay;
            return SectionType.Mixed;
        }

        /// <summary>
        /// 根据SubGroup获取布局配置
        /// </summary>
        private LayoutConfig GetLayoutConfig(string subGroup)
        {
            var lower = subGroup.ToLower();

            // 点动控制 - 3列
            if (lower.Contains("点动"))
                return new LayoutConfig { Columns = 3, RowSpacing = 10, ColumnSpacing = 15 };

            // 使能控制 - 2列
            if (lower.Contains("使能"))
                return new LayoutConfig { Columns = 2, RowSpacing = 10, ColumnSpacing = 15 };

            // 手动启动 - 3列
            if (lower.Contains("手动") || lower.Contains("启动"))
                return new LayoutConfig { Columns = 3, RowSpacing = 10, ColumnSpacing = 15 };

            // 位置参数 - 3列
            if (lower.Contains("位置"))
                return new LayoutConfig { Columns = 3, RowSpacing = 12, ColumnSpacing = 20 };

            // 速度参数 - 2列
            if (lower.Contains("速度"))
                return new LayoutConfig { Columns = 2, RowSpacing = 12, ColumnSpacing = 20 };

            // 状态标志 - 4列
            if (lower.Contains("状态") || lower.Contains("标志"))
                return new LayoutConfig { Columns = 4, RowSpacing = 10, ColumnSpacing = 15 };

            // 默认 - 2列
            return new LayoutConfig { Columns = 2, RowSpacing = 10, ColumnSpacing = 15 };
        }

        /// <summary>
        /// 获取SubGroup的显示顺序
        /// </summary>
        private int GetSubGroupOrder(string subGroup)
        {
            var lower = subGroup.ToLower();

            // 定义常见SubGroup的顺序
            var orderMap = new Dictionary<string, int>
            {
                { "点动控制", 1 },
                { "使能控制", 2 },
                { "手动启动", 3 },
                { "状态标志", 4 },
                { "位置参数", 5 },
                { "速度参数", 6 },
                { "系统状态", 99 }
            };

            // 尝试精确匹配
            if (orderMap.TryGetValue(subGroup, out var order))
                return order;

            // 尝试部分匹配
            foreach (var kvp in orderMap)
            {
                if (lower.Contains(kvp.Key.ToLower()))
                    return kvp.Value;
            }

            // 默认顺序
            return 50;
        }

        #endregion

        #region 静态工厂方法

        /// <summary>
        /// 从CSV文件创建所有模块定义
        /// </summary>
        public static List<CsvDrivenModuleDefinition> CreateFromCsv(string csvFilePath, PLCFeatureFlags featureFlags = null)
        {
            var modules = new List<CsvDrivenModuleDefinition>();

            try
            {
                var importer = new PLCConfigImporter(csvFilePath, featureFlags);
                var rows = importer.ImportFromCsv();

                if (rows.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ CSV文件中没有数据");
                    return modules;
                }

                // 按模块分组
                var moduleGroups = importer.GroupByModule(rows);

                System.Diagnostics.Debug.WriteLine($"✓ 从CSV中发现 {moduleGroups.Count} 个模块");

                foreach (var moduleGroup in moduleGroups)
                {
                    var moduleId = moduleGroup.Key;
                    var moduleRows = moduleGroup.Value;

                    if (moduleRows.Count == 0)
                        continue;

                    // 获取模块信息（使用第一行的数据）
                    var firstRow = moduleRows[0];
                    var moduleName = firstRow.ModuleName;
                    var sortOrder = firstRow.SortOrder;

                    var module = new CsvDrivenModuleDefinition(
                        moduleId,
                        moduleName,
                        sortOrder,
                        moduleRows,
                        importer
                    );

                    modules.Add(module);
                    System.Diagnostics.Debug.WriteLine($"  ✓ 创建模块: {moduleName} ({moduleId}) - {moduleRows.Count}个地址");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ 从CSV创建模块失败: {ex.Message}");
            }

            return modules;
        }

        #endregion
    }
}