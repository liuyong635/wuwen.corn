using System;
using System.Collections.Generic;
using System.IO;

namespace SeedCut.Framework.Services.Signals
{
    /// <summary>
    /// 信号别名配置
    /// 用于将代码中使用的英文名映射到CSV中的中文地址名
    /// </summary>
    public class SignalAliasConfig
    {
        /// <summary>
        /// 配置版本
        /// </summary>
        public string Version { get; set; } = "1.1";

        /// <summary>
        /// 配置描述
        /// </summary>
        public string Description { get; set; } = "信号别名映射配置";

        /// <summary>
        /// 别名映射表
        /// Key: 代码中使用的别名
        /// Value: CSV中的地址名称
        /// </summary>
        public Dictionary<string, string> Aliases { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 从JSON文件加载配置
        /// </summary>
        public static SignalAliasConfig LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"[SignalAliasConfig] 配置文件不存在: {filePath}，将创建默认配置");
                var defaultConfig = CreateDefaultConfig();
                defaultConfig.SaveToFile(filePath);
                return defaultConfig;
            }

            try
            {
                var json = File.ReadAllText(filePath);
                return ParseJson(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SignalAliasConfig] 加载配置失败: {ex.Message}");
                return CreateDefaultConfig();
            }
        }

        /// <summary>
        /// 保存配置到JSON文件
        /// </summary>
        public void SaveToFile(string filePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = ToJson();
                File.WriteAllText(filePath, json);
                System.Diagnostics.Debug.WriteLine($"[SignalAliasConfig] 配置已保存: {filePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SignalAliasConfig] 保存配置失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建默认配置（包含实际使用的信号别名映射）
        /// </summary>
        public static SignalAliasConfig CreateDefaultConfig()
        {
            var config = new SignalAliasConfig
            {
                Version = "1.1",
                Description = "信号别名映射配置 - 将代码中的英文名映射到CSV中的中文地址名",
                Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };

            // ========== 自动程序变量 (地址1000-1013) ==========
            // 地址1000
            config.Aliases["ROB_SafetyToDevice"] = "ROB给设备安全信号";           // 1000.0
            config.Aliases["Device_SafetyToROB"] = "设备给ROB安全信号";           // 1000.1
            config.Aliases["Device_RequestROBDrop"] = "设备请求ROB放料信号";      // 1000.2
            config.Aliases["Auto_StartCircularAxis"] = "自动时启动环形轴条件";    // 1000.4
            config.Aliases["CyclicStop"] = "周期性停机";                          // 1000.5
            config.Aliases["Shield_ROB"] = "屏蔽ROB";                             // 1000.6
            config.Aliases["Station1_DropComplete"] = "工位1-放料工位完成";       // 1000.7

            // 地址1001
            config.Aliases["Station2_TransitComplete"] = "工位2-过度工位完成";    // 1001.0
            config.Aliases["Station3_PhotoComplete"] = "工位3-拍照工位完成";      // 1001.1
            config.Aliases["Station4_TransitComplete"] = "工位4-过度工位完成";    // 1001.2
            config.Aliases["Station5_LaserCutComplete"] = "工位5-激光切割工位完成"; // 1001.3
            config.Aliases["Station6_TransitComplete"] = "工位6-过度工位完成";    // 1001.4
            config.Aliases["Station7_UnloadLargeComplete"] = "工位7-下大料工位完成"; // 1001.5
            config.Aliases["Station8_TransitComplete"] = "工位8-过度工位完成";    // 1001.6

            // ========== 地址1002 - ★ 环形轴初始化 & 激光视觉相关信号 ==========
            config.Aliases["CircularAxis_Init"] = "环形导轨轴初始化";             // M1002.0 ★
            config.Aliases["Device_RecvDropRequest"] = "设备收到请求放料信号";    // 1002.1
            config.Aliases["WaitROB_DropComplete"] = "等待机器人放料完成";        // 1002.2
            config.Aliases["LaserPhoto_Request"] = "拍照请求信号";                // 1002.3
            config.Aliases["LaserPhoto_Complete"] = "拍照完成信号";               // 1002.4
            config.Aliases["LaserCut_RequestSig"] = "激光切割请求信号";           // 1002.5
            config.Aliases["LaserCut_Complete"] = "激光切割完成信号";             // 1002.6
            config.Aliases["CircularAxis_InitInProgress"] = "环形导轨轴初始化中"; // M1002.7 ★ 新增

            // ========== 地址1003 - ★ 环形轴超时 ==========
            config.Aliases["CircularAxis_InitTimeout"] = "环形导轨轴初始化超时";  // M1003.0 ★ 新增
            config.Aliases["DryRun_Test"] = "空跑测试";                           // 1003.4
            config.Aliases["UpperPC_Heartbeat"] = "上位机心跳检测";               // 1003.6
            config.Aliases["UpperPC_Disconnected"] = "上位机通讯中断";            // 1003.7

            // ========== 地址1012 - ★ 上位机控制 ==========
            config.Aliases["Servo_Offline"] = "伺服有掉线";                       // 1012.0
            config.Aliases["UpperPC_Start"] = "上位机-启动";                      // M1012.1 ★ 新增
            config.Aliases["UpperPC_Reset"] = "上位机-复位";                      // 1012.2
            config.Aliases["UpperPC_CyclicStopFlag"] = "上位机-周期性停机标志";   // 1012.3
            config.Aliases["UpperPC_CloseCyclicStop"] = "上位机-关闭周期性停机";  // 1012.4
            config.Aliases["LargeTray_PhotoRequest"] = "大料盘落料触发拍照";          // M1012.5
            config.Aliases["LargeTray_PhotoComplete"] = "大料盘落料触发拍照完成";         // M1012.6
            config.Aliases["SmallTray_PhotoRequest"] = "小料盘落料触发拍照";          // M1012.7

            // 地址1013
            config.Aliases["SmallTray_PhotoComplete"] = "小料盘落料触发拍照完成";         // M1013.0
            config.Aliases["DropPos_LiftCylinder_Manual"] = "放料位升降气缸手动"; // 1013.1
            config.Aliases["DropPos_StretchCylinder_Manual"] = "放料位拉伸气缸手动"; // 1013.2
            config.Aliases["DropPos_LiftCylinder_Alarm"] = "放料位升降气缸报警";  // 1013.3
            config.Aliases["LaserCut_BlockCylinder_Alarm"] = "激光切割挡料气缸报警"; // 1013.4

            // ========== 大小料盘摆盘 (地址3428, 3454, 3458) ==========
            // 地址3424
            config.Aliases["SmallTray_LayoutComplete"] = "小料盘摆盘完成";        // 3424.0
            config.Aliases["SmallTray_LayoutReady"] = "小料盘摆盘已准备好";       // 3424.1

            // 地址3428 - 小料盘
            config.Aliases["WaitSmallTray_FeedInPlace"] = "等待小料盘进料到位";   // 3428.0
            config.Aliases["SmallTray_DetectDirRequest"] = "小料盘检测方向请求";  // 3428.1
            config.Aliases["SmallTray_DirCorrect"] = "小料盘方向正确";            // 3428.2
            config.Aliases["SmallTray_DirError"] = "小料盘方向错误";              // 3428.3
            config.Aliases["Manual_SelectOutTray"] = "人工选择出料盘";            // 3428.4
            config.Aliases["SmallTray_ScanRequest"] = "小料盘请求扫码";           // 3428.5
            config.Aliases["Debug_Mode"] = "调试模式";                            // 3428.6
            config.Aliases["SmallTray_SingleStep"] = "小料盘单步启动";            // 3428.7

            // 地址3454 - 大料盘
            config.Aliases["WaitLargeTray_FeedInPlace"] = "等待大料盘进料到位";   // 3454.0
            config.Aliases["LargeTray_DetectDirRequest"] = "大料盘检测方向请求";  // 3454.1
            config.Aliases["LargeTray_DirCorrect"] = "大料盘方向正确";            // 3454.2
            config.Aliases["LargeTray_DirError"] = "大料盘方向错误";              // 3454.3

            // 地址3458 - 大料盘
            config.Aliases["LargeTray_ScanRequest"] = "大料盘请求扫码";           // 3458.0
            config.Aliases["LargeTray_SingleStep"] = "大料盘单步启动";            // 3458.1

            // ========== 柔爪控制信号 ==========
            config.Aliases["Material_Detect"] = "柔性夹爪有料检测";
            config.Aliases["Gripper_Control"] = "机器人柔性夹爪";

            // ========== 环形轴其他状态（预留） ==========
            config.Aliases["CircularAxis_InitComplete"] = "环形轴初始化完成";     // 预留
            config.Aliases["CircularAxis_HomingInProgress"] = "环形轴回原点中";   // 预留
            config.Aliases["Vibrator_PourDoor"] = "震动盘排料";   // 预留


            // ========== 地址1030 - ★ 新增：转盘信号 ==========
            config.Aliases["Turntable_MoveComplete"] = "上位机-移动完成";  // M1030.4

            System.Diagnostics.Debug.WriteLine($"[SignalAliasConfig] 已创建默认配置，共 {config.Aliases.Count} 个别名映射");
            return config;
        }

        /// <summary>
        /// 创建示例配置文件（保持兼容）
        /// </summary>
        public static void CreateSampleConfig(string filePath)
        {
            var config = CreateDefaultConfig();
            config.SaveToFile(filePath);
            System.Diagnostics.Debug.WriteLine($"[SignalAliasConfig] 已创建示例配置: {filePath}");
        }

        #region JSON 解析（简单实现，避免依赖 Newtonsoft.Json）

        private static SignalAliasConfig ParseJson(string json)
        {
            var config = new SignalAliasConfig();

            try
            {
                // 提取 Version
                var versionMatch = System.Text.RegularExpressions.Regex.Match(json, @"""Version""\s*:\s*""([^""]+)""");
                if (versionMatch.Success)
                    config.Version = versionMatch.Groups[1].Value;

                // 提取 Description
                var descMatch = System.Text.RegularExpressions.Regex.Match(json, @"""Description""\s*:\s*""([^""]+)""");
                if (descMatch.Success)
                    config.Description = descMatch.Groups[1].Value;

                // 提取 Aliases 块
                var aliasesMatch = System.Text.RegularExpressions.Regex.Match(json, @"""Aliases""\s*:\s*\{([^}]+)\}", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (aliasesMatch.Success)
                {
                    var aliasesContent = aliasesMatch.Groups[1].Value;
                    var pairMatches = System.Text.RegularExpressions.Regex.Matches(aliasesContent, @"""([^""]+)""\s*:\s*""([^""]+)""");

                    foreach (System.Text.RegularExpressions.Match pairMatch in pairMatches)
                    {
                        var alias = pairMatch.Groups[1].Value;
                        var csvName = pairMatch.Groups[2].Value;
                        config.Aliases[alias] = csvName;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[SignalAliasConfig] 已加载 {config.Aliases.Count} 个别名映射");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SignalAliasConfig] JSON解析异常: {ex.Message}");
            }

            return config;
        }

        private string ToJson()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"Version\": \"{Version}\",");
            sb.AppendLine($"  \"Description\": \"{Description}\",");
            sb.AppendLine("  \"Aliases\": {");

            var aliases = new List<string>(Aliases.Keys);
            for (int i = 0; i < aliases.Count; i++)
            {
                var alias = aliases[i];
                var csvName = Aliases[alias];
                var comma = i < aliases.Count - 1 ? "," : "";
                sb.AppendLine($"    \"{alias}\": \"{csvName}\"{comma}");
            }

            sb.AppendLine("  }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        #endregion
    }
}