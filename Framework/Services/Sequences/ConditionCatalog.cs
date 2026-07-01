using System;
using System.Collections.Generic;
using System.Linq;

namespace SeedCut.Framework.Services.Sequences
{
    #region 条件项定义

    /// <summary>
    /// 可选条件项
    /// 用于UI展示和选择
    /// </summary>
    public class ConditionItem
    {
        /// <summary>
        /// 条件唯一ID
        /// </summary>
        public string ConditionId { get; set; }

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 分组（用于UI分类显示）
        /// </summary>
        public string Group { get; set; }

        /// <summary>
        /// 条件类型
        /// </summary>
        public ConditionType Type { get; set; }

        /// <summary>
        /// 关联的Handler ID（如果有）
        /// </summary>
        public string RelatedHandlerId { get; set; }

        /// <summary>
        /// 应用条件的动作
        /// </summary>
        public Action ApplyAction { get; set; }

        /// <summary>
        /// 清除条件的动作
        /// </summary>
        public Action ClearAction { get; set; }

        /// <summary>
        /// 检查条件当前是否满足
        /// </summary>
        public Func<bool> CheckAction { get; set; }
    }

    /// <summary>
    /// 条件类型
    /// </summary>
    public enum ConditionType
    {
        /// <summary>
        /// 标志条件 (Flag)
        /// </summary>
        Flag,

        /// <summary>
        /// 信号条件 (PLC Signal)
        /// </summary>
        Signal,

        /// <summary>
        /// 状态条件 (System State)
        /// </summary>
        State,

        /// <summary>
        /// 复合条件
        /// </summary>
        Composite
    }

    #endregion

    #region 条件目录

    /// <summary>
    /// 条件目录
    /// 
    /// 集中管理所有可用的条件定义
    /// UI从此目录中选择条件进行组合
    /// 
    /// 使用示例:
    /// <code>
    /// // 获取所有条件
    /// var allConditions = ConditionCatalog.GetAll();
    /// 
    /// // 按分组获取
    /// var robotConditions = ConditionCatalog.GetByGroup("机器人");
    /// 
    /// // 获取单个条件
    /// var condition = ConditionCatalog.Get("Robot_Ready");
    /// 
    /// // 应用条件
    /// condition.ApplyAction?.Invoke();
    /// </code>
    /// </summary>
    public static class ConditionCatalog
    {
        private static readonly Dictionary<string, ConditionItem> _conditions
            = new Dictionary<string, ConditionItem>();

        private static bool _initialized = false;

        #region 初始化

        /// <summary>
        /// 确保目录已初始化
        /// </summary>
        private static void EnsureInitialized()
        {
            if (_initialized) return;

            lock (_conditions)
            {
                if (_initialized) return;

                RegisterAllConditions();
                _initialized = true;
            }
        }

        /// <summary>
        /// 注册所有预定义条件
        /// </summary>
        private static void RegisterAllConditions()
        {
            // ============================================================
            // 系统启动相关条件
            // ============================================================
            Register(new ConditionItem
            {
                ConditionId = "System_Start_Request",
                DisplayName = "系统启动请求",
                Description = "请求启动系统（使能伺服、运行AR程序）",
                Group = "系统",
                Type = ConditionType.Flag,
                RelatedHandlerId = "SystemStartup",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("System_Start_Request", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("System_Start_Request", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("System_Start_Request")
            });

            Register(new ConditionItem
            {
                ConditionId = "SystemStartup_Busy",
                DisplayName = "系统启动中",
                Description = "系统启动Handler正在执行",
                Group = "状态",
                Type = ConditionType.Flag,
                RelatedHandlerId = "SystemStartup",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("SystemStartup_Busy", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("SystemStartup_Busy", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("SystemStartup_Busy")
            });

            // ============================================================
            // 机器人相关条件
            // ============================================================
            Register(new ConditionItem
            {
                ConditionId = "Robot_Ready",
                DisplayName = "机器人就绪",
                Description = "机器人初始化完成，发送READY信号",
                Group = "机器人",
                Type = ConditionType.Flag,
                RelatedHandlerId = "DiskVision",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("Robot_Ready_Received", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("Robot_Ready_Received", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("Robot_Ready_Received")
            });

            Register(new ConditionItem
            {
                ConditionId = "Robot_BatchComplete",
                DisplayName = "批次完成",
                Description = "机器人完成一批次抓取，请求新坐标",
                Group = "机器人",
                Type = ConditionType.Flag,
                RelatedHandlerId = "DiskVision",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("Robot_BatchComplete_Received", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("Robot_BatchComplete_Received", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("Robot_BatchComplete_Received")
            });

            Register(new ConditionItem
            {
                ConditionId = "Vision_NoPoints",
                DisplayName = "无可抓点",
                Description = "视觉检测无可抓取点位",
                Group = "机器人",
                Type = ConditionType.Flag,
                RelatedHandlerId = "DiskVision",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("Vision_NoPoints_Received", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("Vision_NoPoints_Received", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("Vision_NoPoints_Received")
            });

            Register(new ConditionItem
            {
                ConditionId = "Robot_Command_Ready",
                DisplayName = "机器人命令就绪",
                Description = "有待发送的机器人命令",
                Group = "机器人",
                Type = ConditionType.Flag,
                RelatedHandlerId = "RobotCommand",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("Robot_Command_Ready", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("Robot_Command_Ready", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("Robot_Command_Ready")
            });

            Register(new ConditionItem
            {
                ConditionId = "Robot_WaitInit_Received",
                DisplayName = "收到WAIT_INIT",
                Description = "机器人AR程序发送的初始化请求（自动触发）",
                Group = "机器人",
                Type = ConditionType.Flag,
                RelatedHandlerId = "RobotCommand",
                ApplyAction = () =>
                {
                    Conditions.FlagCondition.SetFlag("Robot_WaitInit_Received", true);
                    Conditions.FlagCondition.SetFlag("Robot_Command_Pending", true);
                },
                ClearAction = () => Conditions.FlagCondition.SetFlag("Robot_WaitInit_Received", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("Robot_WaitInit_Received")
            });

            Register(new ConditionItem
            {
                ConditionId = "Robot_Command_Pending",
                DisplayName = "命令待处理",
                Description = "有待处理的机器人命令",
                Group = "机器人",
                Type = ConditionType.Flag,
                RelatedHandlerId = "RobotCommand",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("Robot_Command_Pending", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("Robot_Command_Pending", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("Robot_Command_Pending")
            });

            Register(new ConditionItem
            {
                ConditionId = "RobotCommand_Busy",
                DisplayName = "命令处理中",
                Description = "RobotCommandHandler正在执行",
                Group = "状态",
                Type = ConditionType.Flag,
                RelatedHandlerId = "RobotCommand",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("RobotCommand_Busy", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("RobotCommand_Busy", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("RobotCommand_Busy")
            });

            // ============================================================
            // 初始化相关条件
            // ============================================================
            Register(new ConditionItem
            {
                ConditionId = "CircularAxisInit_Request",
                DisplayName = "环形轴初始化请求",
                Description = "请求执行环形轴初始化",
                Group = "初始化",
                Type = ConditionType.Flag,
                RelatedHandlerId = "CircularAxisInit",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("CircularAxisInit_Request", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("CircularAxisInit_Request", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("CircularAxisInit_Request")
            });

            // ============================================================
            // 视觉相关条件
            // ============================================================
            Register(new ConditionItem
            {
                ConditionId = "LaserVision_Request",
                DisplayName = "激光视觉请求",
                Description = "请求执行激光视觉检测",
                Group = "视觉",
                Type = ConditionType.Flag,
                RelatedHandlerId = "LaserVision",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("LaserVision_Request", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("LaserVision_Request", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("LaserVision_Request")
            });

            Register(new ConditionItem
            {
                ConditionId = "LargeTrayVision_Request",
                DisplayName = "大托盘视觉请求",
                Description = "请求执行大托盘视觉检测",
                Group = "视觉",
                Type = ConditionType.Flag,
                RelatedHandlerId = "LargeTrayVision",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("LargeTrayVision_Request", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("LargeTrayVision_Request", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("LargeTrayVision_Request")
            });

            Register(new ConditionItem
            {
                ConditionId = "SmallTrayVision_Request",
                DisplayName = "小托盘视觉请求",
                Description = "请求执行小托盘视觉检测",
                Group = "视觉",
                Type = ConditionType.Flag,
                RelatedHandlerId = "SmallTrayVision",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("SmallTrayVision_Request", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("SmallTrayVision_Request", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("SmallTrayVision_Request")
            });

            // ============================================================
            // 方向检测条件
            // ============================================================
            Register(new ConditionItem
            {
                ConditionId = "SmallTrayDirection_Request",
                DisplayName = "小托盘方向检测请求",
                Description = "请求执行小托盘方向检测",
                Group = "方向检测",
                Type = ConditionType.Flag,
                RelatedHandlerId = "SmallTrayDirection",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("SmallTrayDirection_Request", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("SmallTrayDirection_Request", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("SmallTrayDirection_Request")
            });

            Register(new ConditionItem
            {
                ConditionId = "LargeTrayDirection_Request",
                DisplayName = "大托盘方向检测请求",
                Description = "请求执行大托盘方向检测",
                Group = "方向检测",
                Type = ConditionType.Flag,
                RelatedHandlerId = "LargeTrayDirection",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("LargeTrayDirection_Request", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("LargeTrayDirection_Request", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("LargeTrayDirection_Request")
            });

            // ============================================================
            // 激光切割条件
            // ============================================================
            Register(new ConditionItem
            {
                ConditionId = "LaserCut_Request",
                DisplayName = "激光切割请求",
                Description = "请求执行激光切割",
                Group = "激光",
                Type = ConditionType.Flag,
                RelatedHandlerId = "LaserCut",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("LaserCut_Request", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("LaserCut_Request", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("LaserCut_Request")
            });

            // ============================================================
            // 条码扫描条件
            // ============================================================
            Register(new ConditionItem
            {
                ConditionId = "BarcodeScan_Request",
                DisplayName = "条码扫描请求",
                Description = "请求执行条码扫描",
                Group = "扫描",
                Type = ConditionType.Flag,
                RelatedHandlerId = "BarcodeScan",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("BarcodeScan_Request", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("BarcodeScan_Request", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("BarcodeScan_Request")
            });

            // ============================================================
            // 忙碌状态条件（用于检查，通常不手动设置）
            // ============================================================
            Register(new ConditionItem
            {
                ConditionId = "DiskVision_Busy",
                DisplayName = "振动盘视觉忙碌",
                Description = "振动盘视觉正在执行中",
                Group = "状态",
                Type = ConditionType.Flag,
                RelatedHandlerId = "DiskVision",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("DiskVision_Busy", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("DiskVision_Busy", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("DiskVision_Busy")
            });

            Register(new ConditionItem
            {
                ConditionId = "CircularAxisInit_Busy",
                DisplayName = "环形轴初始化忙碌",
                Description = "环形轴初始化正在执行中",
                Group = "状态",
                Type = ConditionType.Flag,
                RelatedHandlerId = "CircularAxisInit",
                ApplyAction = () => Conditions.FlagCondition.SetFlag("CircularAxisInit_Busy", true),
                ClearAction = () => Conditions.FlagCondition.SetFlag("CircularAxisInit_Busy", false),
                CheckAction = () => Conditions.FlagCondition.GetFlag("CircularAxisInit_Busy")
            });
        }

        #endregion

        #region 注册方法

        /// <summary>
        /// 注册条件
        /// </summary>
        public static void Register(ConditionItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.ConditionId))
                return;

            _conditions[item.ConditionId] = item;
        }

        /// <summary>
        /// 注册自定义Flag条件（简化方法）
        /// </summary>
        public static void RegisterFlag(
            string conditionId,
            string displayName,
            string flagName,
            string group = "自定义",
            string relatedHandlerId = null)
        {
            Register(new ConditionItem
            {
                ConditionId = conditionId,
                DisplayName = displayName,
                Description = string.Format("Flag: {0}", flagName),
                Group = group,
                Type = ConditionType.Flag,
                RelatedHandlerId = relatedHandlerId,
                ApplyAction = () => Conditions.FlagCondition.SetFlag(flagName, true),
                ClearAction = () => Conditions.FlagCondition.SetFlag(flagName, false),
                CheckAction = () => Conditions.FlagCondition.GetFlag(flagName)
            });
        }

        #endregion

        #region 查询方法

        /// <summary>
        /// 获取所有条件
        /// </summary>
        public static IReadOnlyList<ConditionItem> GetAll()
        {
            EnsureInitialized();
            return _conditions.Values.ToList();
        }

        /// <summary>
        /// 按分组获取条件
        /// </summary>
        public static IReadOnlyList<ConditionItem> GetByGroup(string group)
        {
            EnsureInitialized();
            return _conditions.Values
                .Where(c => c.Group == group)
                .ToList();
        }

        /// <summary>
        /// 获取所有分组名称
        /// </summary>
        public static IReadOnlyList<string> GetGroups()
        {
            EnsureInitialized();
            return _conditions.Values
                .Select(c => c.Group)
                .Distinct()
                .OrderBy(g => g)
                .ToList();
        }

        /// <summary>
        /// 获取单个条件
        /// </summary>
        public static ConditionItem Get(string conditionId)
        {
            EnsureInitialized();
            _conditions.TryGetValue(conditionId, out var item);
            return item;
        }

        /// <summary>
        /// 根据关联的Handler获取条件
        /// </summary>
        public static IReadOnlyList<ConditionItem> GetByHandler(string handlerId)
        {
            EnsureInitialized();
            return _conditions.Values
                .Where(c => c.RelatedHandlerId == handlerId)
                .ToList();
        }

        /// <summary>
        /// 获取可触发指定Handler的条件
        /// </summary>
        public static IReadOnlyList<ConditionItem> GetTriggerConditionsFor(string handlerId)
        {
            EnsureInitialized();

            // 返回关联到此Handler的非Busy条件
            return _conditions.Values
                .Where(c => c.RelatedHandlerId == handlerId
                         && !c.ConditionId.EndsWith("_Busy"))
                .ToList();
        }

        #endregion

        #region 操作方法

        /// <summary>
        /// 应用条件
        /// </summary>
        public static bool Apply(string conditionId)
        {
            var item = Get(conditionId);
            if (item?.ApplyAction == null) return false;

            item.ApplyAction();
            return true;
        }

        /// <summary>
        /// 批量应用条件
        /// </summary>
        public static void Apply(params string[] conditionIds)
        {
            foreach (var id in conditionIds)
            {
                Apply(id);
            }
        }

        /// <summary>
        /// 清除条件
        /// </summary>
        public static bool Clear(string conditionId)
        {
            var item = Get(conditionId);
            if (item?.ClearAction == null) return false;

            item.ClearAction();
            return true;
        }

        /// <summary>
        /// 批量清除条件
        /// </summary>
        public static void Clear(params string[] conditionIds)
        {
            foreach (var id in conditionIds)
            {
                Clear(id);
            }
        }

        /// <summary>
        /// 清除所有条件
        /// </summary>
        public static void ClearAll()
        {
            EnsureInitialized();
            foreach (var item in _conditions.Values)
            {
                item.ClearAction?.Invoke();
            }
        }

        /// <summary>
        /// 检查条件是否满足
        /// </summary>
        public static bool Check(string conditionId)
        {
            var item = Get(conditionId);
            return item?.CheckAction?.Invoke() ?? false;
        }

        /// <summary>
        /// 获取当前满足的所有条件
        /// </summary>
        public static IReadOnlyList<ConditionItem> GetSatisfiedConditions()
        {
            EnsureInitialized();
            return _conditions.Values
                .Where(c => c.CheckAction?.Invoke() == true)
                .ToList();
        }

        #endregion
    }

    #endregion

    #region 快捷操作定义

    /// <summary>
    /// 快捷操作项
    /// 预定义的条件组合，用于一键设置
    /// </summary>
    public class QuickAction
    {
        /// <summary>
        /// 操作ID
        /// </summary>
        public string ActionId { get; set; }

        /// <summary>
        /// 显示名称
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 要应用的条件ID列表
        /// </summary>
        public List<string> ConditionIds { get; set; } = new List<string>();

        /// <summary>
        /// 期望触发的Handler ID列表
        /// </summary>
        public List<string> ExpectedHandlerIds { get; set; } = new List<string>();

        /// <summary>
        /// 执行快捷操作（设置所有条件）
        /// </summary>
        public void Apply()
        {
            foreach (var conditionId in ConditionIds)
            {
                ConditionCatalog.Apply(conditionId);
            }
        }

        /// <summary>
        /// 清除快捷操作设置的条件
        /// </summary>
        public void Clear()
        {
            foreach (var conditionId in ConditionIds)
            {
                ConditionCatalog.Clear(conditionId);
            }
        }
    }

    /// <summary>
    /// 快捷操作目录
    /// </summary>
    public static class QuickActionCatalog
    {
        private static readonly List<QuickAction> _actions = new List<QuickAction>();
        private static bool _initialized = false;

        private static void EnsureInitialized()
        {
            if (_initialized) return;

            lock (_actions)
            {
                if (_initialized) return;

                RegisterAllActions();
                _initialized = true;
            }
        }

        private static void RegisterAllActions()
        {
            // ============================================================
            // 完整测试流程（4个Handler）
            // ============================================================
            _actions.Add(new QuickAction
            {
                ActionId = "FullTest",
                DisplayName = "完整测试流程",
                Description = "执行4个Handler完整测试（启动→命令→初始化→视觉）",
                ConditionIds = new List<string>
                {
                    "System_Start_Request",
                    "CircularAxisInit_Request"
                },
                ExpectedHandlerIds = new List<string>
                {
                    "SystemStartup",
                    "RobotCommand",
                    "CircularAxisInit",
                    "DiskVision"
                }
            });

            // 仅系统启动（跳过环形轴初始化）
            _actions.Add(new QuickAction
            {
                ActionId = "QuickStart",
                DisplayName = "快速启动",
                Description = "启动系统但跳过环形轴初始化（非首次启动）",
                ConditionIds = new List<string>
                {
                    "System_Start_Request"
                },
                ExpectedHandlerIds = new List<string>
                {
                    "SystemStartup",
                    "RobotCommand",
                    "DiskVision"
                }
            });

            // ============================================================
            // 模拟信号（调试用）
            // ============================================================
            // 模拟初始化完成
            _actions.Add(new QuickAction
            {
                ActionId = "SimulateInit",
                DisplayName = "模拟初始化完成",
                Description = "设置Robot_Ready，触发第一次视觉检测",
                ConditionIds = new List<string> { "Robot_Ready" },
                ExpectedHandlerIds = new List<string> { "DiskVision" }
            });

            // 模拟批次完成
            _actions.Add(new QuickAction
            {
                ActionId = "SimulateBatchComplete",
                DisplayName = "模拟批次完成",
                Description = "设置Robot_BatchComplete，触发新一轮视觉检测",
                ConditionIds = new List<string> { "Robot_BatchComplete" },
                ExpectedHandlerIds = new List<string> { "DiskVision" }
            });

            // 模拟无可抓点
            _actions.Add(new QuickAction
            {
                ActionId = "SimulateNoPoints",
                DisplayName = "模拟无可抓点",
                Description = "设置Vision_NoPoints，触发进料/振动",
                ConditionIds = new List<string> { "Vision_NoPoints" },
                ExpectedHandlerIds = new List<string> { "DiskVision" }
            });

            // 模拟WAIT_INIT（调试RobotCommand用）
            _actions.Add(new QuickAction
            {
                ActionId = "SimulateWaitInit",
                DisplayName = "模拟WAIT_INIT",
                Description = "设置Robot_WaitInit_Received，触发RobotCommand",
                ConditionIds = new List<string> { "Robot_WaitInit_Received" },
                ExpectedHandlerIds = new List<string> { "RobotCommand" }
            });

            // ============================================================
            // 系统初始化（原有）
            // ============================================================
            _actions.Add(new QuickAction
            {
                ActionId = "SystemInit",
                DisplayName = "系统初始化",
                Description = "设置所有初始化条件",
                ConditionIds = new List<string>
                {
                    "CircularAxisInit_Request",
                    "Robot_Ready"
                },
                ExpectedHandlerIds = new List<string>
                {
                    "CircularAxisInit",
                    "DiskVision"
                }
            });
        }

        /// <summary>
        /// 获取所有快捷操作
        /// </summary>
        public static IReadOnlyList<QuickAction> GetAll()
        {
            EnsureInitialized();
            return _actions.ToList();
        }

        /// <summary>
        /// 获取指定快捷操作
        /// </summary>
        public static QuickAction Get(string actionId)
        {
            EnsureInitialized();
            return _actions.FirstOrDefault(a => a.ActionId == actionId);
        }

        /// <summary>
        /// 注册自定义快捷操作
        /// </summary>
        public static void Register(QuickAction action)
        {
            EnsureInitialized();
            _actions.RemoveAll(a => a.ActionId == action.ActionId);
            _actions.Add(action);
        }
    }

    #endregion
}