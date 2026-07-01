using SeedCut.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SeedCut.Models.PLCModule
{
    /// <summary>
    /// PLC模块定义接口
    /// </summary>
    public interface IPLCModuleDefinition
    {
        /// <summary>
        /// 获取模块的所有地址配置
        /// </summary>
        List<PLCAddressItem> GetAllAddresses();

        /// <summary>
        /// 创建模块元数据
        /// </summary>
        PLCModuleMetadata CreateModule();
    }
    /// <summary>
    /// 增强版PLC模块工厂 - 支持CSV配置和代码定义双模式
    /// </summary>
    public class PLCModuleFactory
    {
        private readonly IPLCService _plcService;
        private readonly PLCAddressRegistry _addressRegistry;
        private readonly Dictionary<string, PLCModuleMetadata> _moduleRegistry;
        private readonly List<IPLCModuleDefinition> _moduleDefinitions;
        private readonly PLCFeatureFlags _featureFlags;

        public PLCModuleFactory(IPLCService plcService, PLCFeatureFlags featureFlags = null)
        {
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));
            _addressRegistry = plcService.AddressRegistry;
            _moduleRegistry = new Dictionary<string, PLCModuleMetadata>();
            _moduleDefinitions = new List<IPLCModuleDefinition>();
            _featureFlags = featureFlags ?? PLCFeatureFlags.Load();

            // 打印功能配置
            _featureFlags.PrintConfiguration();

            // 根据配置选择加载方式
            if (_featureFlags.UseCsvConfiguration)
            {
                LoadModulesFromCsv();
            }

            if (_featureFlags.UseCodeDefinedModules)
            {
                LoadModulesFromCode();
            }

            // 注册所有模块
            RegisterAllModules();
            LogRegistrationSummary();
        }

        #region CSV配置加载

        /// <summary>
        /// 从CSV文件加载模块
        /// </summary>
        private void LoadModulesFromCsv()
        {
            try
            {
                var csvPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    _featureFlags.CsvConfigPath
                );

                if (!File.Exists(csvPath))
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ CSV配置文件不存在: {csvPath}");
                    System.Diagnostics.Debug.WriteLine($"   请确保文件路径正确，或将 UseCsvConfiguration 设置为 false");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"");
                System.Diagnostics.Debug.WriteLine($"========================================");
                System.Diagnostics.Debug.WriteLine($"从CSV加载PLC配置");
                System.Diagnostics.Debug.WriteLine($"========================================");
                System.Diagnostics.Debug.WriteLine($"CSV路径: {csvPath}");

                var csvModules = CsvDrivenModuleDefinition.CreateFromCsv(csvPath, _featureFlags);

                foreach (var module in csvModules)
                {
                    _moduleDefinitions.Add(module);
                }

                System.Diagnostics.Debug.WriteLine($"✓ 从CSV加载了 {csvModules.Count} 个模块定义");
                System.Diagnostics.Debug.WriteLine($"========================================");
                System.Diagnostics.Debug.WriteLine($"");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ 从CSV加载模块失败: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   {ex.StackTrace}");
            }
        }

        #endregion

        #region 代码定义加载（传统方式）

        /// <summary>
        /// 从代码定义加载模块（传统方式）
        /// </summary>
        private void LoadModulesFromCode()
        {
            System.Diagnostics.Debug.WriteLine($"");
            System.Diagnostics.Debug.WriteLine($"========================================");
            System.Diagnostics.Debug.WriteLine($"从代码定义加载PLC模块");
            System.Diagnostics.Debug.WriteLine($"========================================");

            try
            {
                // 在这里注册所有代码定义的模块
                // _moduleDefinitions.Add(new UpperLargeStationModule());
                // _moduleDefinitions.Add(new InverterCommunicationModule());
                // ... 更多模块

                System.Diagnostics.Debug.WriteLine($"✓ 从代码加载了 {_moduleDefinitions.Count} 个模块定义");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ 从代码加载模块失败: {ex.Message}");
            }

            System.Diagnostics.Debug.WriteLine($"========================================");
            System.Diagnostics.Debug.WriteLine($"");
        }

        #endregion

        #region 模块注册

        /// <summary>
        /// 注册所有模块
        /// </summary>
        private void RegisterAllModules()
        {
            System.Diagnostics.Debug.WriteLine($"");
            System.Diagnostics.Debug.WriteLine($"========================================");
            System.Diagnostics.Debug.WriteLine($"开始注册PLC模块和地址");
            System.Diagnostics.Debug.WriteLine($"========================================");

            foreach (var definition in _moduleDefinitions)
            {
                try
                {
                    // 获取并注册地址
                    var addresses = definition.GetAllAddresses();
                    int registeredCount = 0;
                    int conflictCount = 0;

                    foreach (var addr in addresses)
                    {
                        try
                        {
                            _addressRegistry.AddAddress(addr);
                            registeredCount++;
                        }
                        catch (InvalidOperationException ex)
                        {
                            conflictCount++;
                           // System.Diagnostics.Debug.WriteLine($"⚠️ 地址注册冲突: {addr.Name} - {ex.Message}");
                        }
                    }

                    // 创建模块
                    var module = definition.CreateModule();
                    if (module != null && !string.IsNullOrEmpty(module.ModuleId))
                    {
                        _moduleRegistry[module.ModuleId] = module;
                        System.Diagnostics.Debug.WriteLine($"✓ 已注册模块: {module.DisplayName}");
                        System.Diagnostics.Debug.WriteLine($"   - 地址数量: {addresses.Count}");
                        System.Diagnostics.Debug.WriteLine($"   - 成功注册: {registeredCount}");
                        if (conflictCount > 0)
                            System.Diagnostics.Debug.WriteLine($"   - 冲突跳过: {conflictCount}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"✗ 注册模块失败: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"   {ex.StackTrace}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"========================================");
            System.Diagnostics.Debug.WriteLine($"");
        }

        /// <summary>
        /// 记录注册摘要
        /// </summary>
        private void LogRegistrationSummary()
        {
            System.Diagnostics.Debug.WriteLine($"");
            System.Diagnostics.Debug.WriteLine($"========================================");
            System.Diagnostics.Debug.WriteLine($"PLC模块工厂初始化完成");
            System.Diagnostics.Debug.WriteLine($"========================================");
            System.Diagnostics.Debug.WriteLine($"✓ 已注册模块: {_moduleRegistry.Count} 个");
            System.Diagnostics.Debug.WriteLine($"✓ 已注册地址: {_addressRegistry.Addresses.Count} 个");

            if (_featureFlags.EnableUI)
            {
                System.Diagnostics.Debug.WriteLine($"  - UI显示: {_addressRegistry.GetUIAddresses().Count} 个");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"  - UI功能: 已禁用");
            }

            System.Diagnostics.Debug.WriteLine($"  - 后台使用: {_addressRegistry.GetBackgroundAddresses().Count} 个");

            // 验证地址
            var errors = _addressRegistry.Validate();
            if (errors.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 发现 {errors.Count} 个配置警告:");
                foreach (var error in errors.Take(5))
                {
                    System.Diagnostics.Debug.WriteLine($"   - {error}");
                }
                if (errors.Count > 5)
                {
                    System.Diagnostics.Debug.WriteLine($"   ... 还有 {errors.Count - 5} 个警告");
                }
            }

            System.Diagnostics.Debug.WriteLine($"========================================");
            System.Diagnostics.Debug.WriteLine($"");
        }

        #endregion

        #region 查询方法

        public List<PLCModuleMetadata> GetAllModules()
        {
            return _moduleRegistry.Values
                .Where(m => m.IsEnabled)
                .OrderBy(m => m.SortOrder)
                .ToList();
        }

        public PLCModuleMetadata GetModule(string moduleId)
        {
            return _moduleRegistry.TryGetValue(moduleId, out var module) ? module : null;
        }

        public PLCAddressItem GetAddress(string addressName)
        {
            return _addressRegistry.GetAddress(addressName);
        }

        public List<PLCAddressItem> GetAddressesByGroup(string group)
        {
            return _addressRegistry.GetAddressesByGroup(group);
        }

        public void PrintAddressMap(string group = null)
        {
            _addressRegistry.PrintAddressMap(group);
        }

        public List<string> ValidateAddresses()
        {
            return _addressRegistry.Validate();
        }

        #endregion

        #region 便捷方法

        /// <summary>
        /// 导出当前配置到CSV（用于从代码生成CSV模板）
        /// </summary>
        public void ExportToCsv(string outputPath)
        {
            try
            {
                using (var writer = new StreamWriter(outputPath))
                {
                    // 写入表头
                    writer.WriteLine("ModuleId,ModuleName,SortOrder,AddressName,DBNumber,StartAddress,DataType,BitPosition,Writable,ShowInUI,SubGroup,UIOrder,ControlType,UILabel,Tooltip,Unit,DecimalPlaces,MinValue,MaxValue,Description");

                    // 写入数据
                    foreach (var module in _moduleRegistry.Values.OrderBy(m => m.SortOrder))
                    {
                        var addresses = _addressRegistry.GetAddressesByGroup(module.AddressGroup);

                        foreach (var addr in addresses.OrderBy(a => a.SubGroup).ThenBy(a => a.UIOrder))
                        {
                            // 使用扩展方法安全获取ExtraUIConfig中的值
                            var line = $"{module.ModuleId}," +
                                      $"{module.DisplayName}," +
                                      $"{module.SortOrder}," +
                                      $"{addr.Name}," +
                                      $"{addr.DBNumber}," +
                                      $"{addr.StartAddress}," +
                                      $"{addr.DataType}," +
                                      $"{addr.BitPosition}," +
                                      $"{addr.Writable}," +
                                      $"{addr.ShowInUI}," +
                                      $"{addr.SubGroup}," +
                                      $"{addr.UIOrder}," +
                                      $"{addr.ControlType}," +
                                      $"{addr.GetUILabel()}," +
                                      $"{addr.Tooltip}," +
                                      $"{addr.ExtraUIConfig.GetStringOrDefault("Unit")}," +
                                      $"{addr.ExtraUIConfig.GetIntOrDefault("DecimalPlaces")}," +
                                      $"{addr.ExtraUIConfig.GetDoubleOrDefault("MinValue")}," +
                                      $"{addr.ExtraUIConfig.GetDoubleOrDefault("MaxValue")}," +
                                      $"{addr.Description}";

                            writer.WriteLine(line);
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"✓ 已导出配置到: {outputPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ 导出CSV失败: {ex.Message}");
            }
        }

        #endregion
    }
}