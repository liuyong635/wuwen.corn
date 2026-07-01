using System;
using System.IO;
using System.Xml.Serialization;

namespace SeedCut.Models.PLCModule
{
    /// <summary>
    /// PLC功能开关配置 - 控制不同功能的启用/禁用
    /// 这样设计可以在不修改代码的情况下，通过配置文件切换功能模式
    /// </summary>
    [XmlRoot("PLCFeatureFlags")]
    public class PLCFeatureFlags
    {
        #region 功能开关

        /// <summary>
        /// 是否启用UI功能（生成UI控件）
        /// </summary>
        public bool EnableUI { get; set; } = true;

        /// <summary>
        /// 是否启用报警功能（监控报警地址）
        /// </summary>
        public bool EnableAlarm { get; set; } = true;

        /// <summary>
        /// 是否启用数据日志功能
        /// </summary>
        public bool EnableDataLogging { get; set; } = true;

        /// <summary>
        /// 是否仅用于代码生成模式（不运行任何实时功能）
        /// </summary>
        public bool CodeGenerationModeOnly { get; set; } = false;

        #endregion

        #region UI相关配置

        /// <summary>
        /// UI自动刷新间隔（毫秒）
        /// </summary>
        public int UIRefreshInterval { get; set; } = 500;

        /// <summary>
        /// 是否显示隐藏地址（ShowInUI=false的地址）
        /// </summary>
        public bool ShowHiddenAddresses { get; set; } = false;

        #endregion

        #region 报警相关配置

        /// <summary>
        /// 报警扫描间隔（毫秒）
        /// </summary>
        public int AlarmScanInterval { get; set; } = 200;

        /// <summary>
        /// 报警历史最大保留数量
        /// </summary>
        public int MaxAlarmHistoryCount { get; set; } = 1000;

        #endregion

        #region 数据源配置

        /// <summary>
        /// 使用CSV配置文件（推荐）
        /// </summary>
        public bool UseCsvConfiguration { get; set; } = true;

        /// <summary>
        /// CSV配置文件路径（相对于程序根目录）
        /// </summary>
        public string CsvConfigPath { get; set; } = "Config/PLCAddresses.csv";

        /// <summary>
        /// 是否使用代码定义的模块（传统方式）
        /// </summary>
        public bool UseCodeDefinedModules { get; set; } = false;

        #endregion

        #region 静态方法：加载和保存配置

        private static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Config",
            "PLCFeatureFlags.xml"
        );

        /// <summary>
        /// 加载配置文件
        /// </summary>
        public static PLCFeatureFlags Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var serializer = new XmlSerializer(typeof(PLCFeatureFlags));
                    using (var reader = new StreamReader(ConfigPath))
                    {
                        var config = (PLCFeatureFlags)serializer.Deserialize(reader);
                        System.Diagnostics.Debug.WriteLine("✓ 已加载PLC功能配置");
                        return config;
                    }
                }
                else
                {
                    var defaultConfig = CreateDefault();
                    defaultConfig.Save();
                    System.Diagnostics.Debug.WriteLine("✓ 已创建默认PLC功能配置");
                    return defaultConfig;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ 加载PLC功能配置失败: {ex.Message}");
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

                var serializer = new XmlSerializer(typeof(PLCFeatureFlags));
                using (var writer = new StreamWriter(ConfigPath))
                {
                    serializer.Serialize(writer, this);
                }
                System.Diagnostics.Debug.WriteLine("✓ 已保存PLC功能配置");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"✗ 保存PLC功能配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建默认配置
        /// </summary>
        public static PLCFeatureFlags CreateDefault()
        {
            return new PLCFeatureFlags
            {
                EnableUI = true,
                EnableAlarm = true,
                EnableDataLogging = true,
                CodeGenerationModeOnly = false,
                UIRefreshInterval = 500,
                ShowHiddenAddresses = false,
                AlarmScanInterval = 200,
                MaxAlarmHistoryCount = 1000,
                UseCsvConfiguration = true,
                CsvConfigPath = "Config/PLCAddresses.csv",
                UseCodeDefinedModules = false
            };
        }

        #endregion

        #region 便捷方法

        /// <summary>
        /// 打印当前配置
        /// </summary>
        public void PrintConfiguration()
        {
            System.Diagnostics.Debug.WriteLine("");
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine("PLC 功能配置");
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine($"✓ UI功能: {(EnableUI ? "启用" : "禁用")}");
            System.Diagnostics.Debug.WriteLine($"✓ 报警功能: {(EnableAlarm ? "启用" : "禁用")}");
            System.Diagnostics.Debug.WriteLine($"✓ 数据日志: {(EnableDataLogging ? "启用" : "禁用")}");
            System.Diagnostics.Debug.WriteLine($"✓ 仅代码生成模式: {(CodeGenerationModeOnly ? "是" : "否")}");
            System.Diagnostics.Debug.WriteLine($"✓ 使用CSV配置: {(UseCsvConfiguration ? "是" : "否")}");
            System.Diagnostics.Debug.WriteLine($"✓ CSV路径: {CsvConfigPath}");
            System.Diagnostics.Debug.WriteLine($"✓ 使用代码定义模块: {(UseCodeDefinedModules ? "是" : "否")}");
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine("");
        }

        #endregion
    }
}