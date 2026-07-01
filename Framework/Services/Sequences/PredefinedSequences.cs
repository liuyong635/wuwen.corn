using SeedCut.Framework.Services.Conditions;
using SeedCut.Framework.Services.Handlers;
using System;
using System.Collections.Generic;

namespace SeedCut.Framework.Services.Sequences
{
    /// <summary>
    /// 预定义序列
    /// 
    /// 【重要变更】
    /// 现在使用 ExpectedHandler 定义期望的Handler和触发条件
    /// 序列执行时会自动设置这些条件，让Handler自然触发
    /// </summary>
    public static class PredefinedSequences
    {
        /// <summary>
        /// 获取所有预定义序列
        /// ★ 修改：调整顺序，FullTest 排在第一位（作为默认选项）
        /// </summary>
        public static IEnumerable<HandlerSequence> GetAll()
        {
            // ★ FullTest 排在第一位，作为默认首选
            yield return FullTestSequence;
            yield return LaserTestSequence;      // ★ 新增：激光测试序列
            yield return VibratorFeedSequence;

            // 在此添加更多序列...
        }

        #region 完整测试序列

        /// <summary>
        /// 完整测试序列（扩展版）
        /// 
        /// 执行流程：
        /// 1. SystemStartup → 使能伺服、运行AR程序
        /// 2. RobotCommand → 收到WAIT_INIT后发送INIT（自动触发）
        /// 3. DiskVision → 收到READY后自动触发
        /// 4. BarcodeScan → 条码扫描（小托盘/大托盘扫码请求触发）
        /// 5. SmallTrayVision → 小料盘视觉检测
        /// 6. SmallTrayDirection → 小料盘方向检测
        /// 7. LargeTrayVision → 大料盘视觉检测
        /// 8. LargeTrayDirection → 大料盘方向检测
        /// 9. LaserVision → 收到激光拍照请求后自动触发（PLC信号 1002.3）
        /// 10. LaserCut → 收到激光切割请求后自动触发（PLC信号 1002.5）
        /// 11. SystemShutdown → 停止AR程序、清理资源（序列结束）
        /// 
        /// 触发说明：
        /// - 手动设置: System_Start_Request
        /// - 自动触发: Robot_WaitInit_Received, Robot_Ready_Received (由TCP命令设置)
        /// - 托盘触发: SmallTray_PhotoRequest, LargeTray_PhotoRequest 等 (由PLC信号设置)
        /// - 激光触发: LaserPhoto_Request, LaserCut_RequestSig (由PLC信号设置)
        /// - 结束清理: System_Stop_Request (由序列自动设置)
        /// 
        /// ★ 失败策略：StopAndCleanup（任何Handler失败即停止并清理）
        /// </summary>
        public static HandlerSequence FullTestSequence => new HandlerSequence
        {
            SequenceId = "FullTest",
            SequenceName = "完整测试流程",
            Description = "执行完整测试（启动→命令→视觉→托盘→激光→停止清理）",

            // ★ 默认条件配置（推荐给用户）
            DefaultConditionIds = new List<string>
            {
                "System_Start_Request",      // 启动入口（手动设置）
            },

            // ★ 单次执行模式
            ExecutionMode = SequenceExecutionMode.SingleRun,

            // ★ 失败策略：失败即停止并清理
            FailurePolicy = SequenceFailurePolicy.StopAndCleanup,

            ExpectedHandlers = new List<ExpectedHandler>
            {
                // 1. 系统启动（入口点）
                new ExpectedHandler
                {
                    HandlerId = "SystemStartup",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },


                // 1.5 转盘监控（高优先级，确保转盘状态同步）
                new ExpectedHandler
                {
                    HandlerId = "TurntableMonitor",
                    IsRequired = false,  // 非必须，信号驱动
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1   // 无限制，每次转盘转动都会触发
                },

                // 2. 机器人命令处理（自动触发 - WAIT_INIT）
                new ExpectedHandler
                {
                    HandlerId = "RobotAction",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // 3. 振动盘视觉
                new ExpectedHandler
                {
                    HandlerId = "DiskVibrate",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },
                new ExpectedHandler
                {
                    HandlerId = "VisionCoordinate",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // ★ 4. 条码扫描（PLC信号触发 - 小托盘/大托盘扫码请求）
                new ExpectedHandler
                {
                    HandlerId = "BarcodeScan",
                    IsRequired = false,  // 可选，不是所有流程都需要扫码
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // ★ 5. 小料盘视觉处理（PLC信号 SmallTray_PhotoRequest 触发）
                new ExpectedHandler
                {
                    HandlerId = "SmallTrayVision",
                    IsRequired = false,  // 可选
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // ★ 6. 小料盘方向检测（PLC信号 SmallTray_DetectDirRequest 触发）
                new ExpectedHandler
                {
                    HandlerId = "SmallTrayDirection",
                    IsRequired = false,  // 可选
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // ★ 7. 大料盘视觉处理（PLC信号 LargeTray_PhotoRequest 触发）
                new ExpectedHandler
                {
                    HandlerId = "LargeTrayVision",
                    IsRequired = false,  // 可选
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // ★ 8. 大料盘方向检测（PLC信号 LargeTray_DetectDirRequest 触发）
                new ExpectedHandler
                {
                    HandlerId = "LargeTrayDirection",
                    IsRequired = false,  // 可选
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // 9. 激光视觉处理（PLC信号 LaserPhoto_Request 触发）
                // ★ 由 PLC 地址 1002.3 (拍照请求信号) 自动触发
                new ExpectedHandler
                {
                    HandlerId = "LaserVision",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // 10. 激光切割（PLC信号 LaserCut_RequestSig 触发）
                // ★ 由 PLC 地址 1002.5 (激光切割请求信号) 自动触发
                new ExpectedHandler
                {
                    HandlerId = "LaserCut",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // ★ 11. 系统停止（序列结束时清理 - ExecuteAtEnd自动触发）
                new ExpectedHandler
                {
                    HandlerId = "SystemShutdown",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = 1,
                    ExecuteAtEnd = true  // ★ 关键：在其他Handler完成后自动触发
                },
                 new ExpectedHandler
                {
                    HandlerId = "StopWorkHander",
                    IsRequired = false,  
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },
            },

            Timeout = TimeSpan.FromMinutes(5),  // ★ 增加超时时间（激光切割可能需要更长时间）
            AllowPartialSuccess = false,

            OnBeforeExecute = ctx =>
            {
                // 清除所有忙碌标志
                // ★ 新增：清除停止请求标志（防止残留导致 SystemShutdown 提前触发）
                FlagCondition.SetFlag("System_Stop_Request", false);
                FlagCondition.SetFlag("SystemStartup_Busy", false);
                FlagCondition.SetFlag("RobotAction_Busy", false);
                FlagCondition.SetFlag("VisionCoordinate_Busy", false);
                FlagCondition.SetFlag("DiskVibrate_Busy", false);
                FlagCondition.SetFlag("NeedVibrate", false);
                FlagCondition.SetFlag("Vibrate_Complete", false);

                FlagCondition.SetFlag("BarcodeScan_Busy", false);           // ★ 新增
                FlagCondition.SetFlag("SmallTrayVision_Busy", false);       // ★ 新增
                FlagCondition.SetFlag("SmallTrayDirection_Busy", false);    // ★ 新增
                FlagCondition.SetFlag("LargeTrayVision_Busy", false);       // ★ 新增
                FlagCondition.SetFlag("LargeTrayDirection_Busy", false);    // ★ 新增
                FlagCondition.SetFlag("LaserVision_Busy", false);
                FlagCondition.SetFlag("LaserCut_Busy", false);
                FlagCondition.SetFlag("SystemShutdown_Busy", false);
                FlagCondition.SetFlag("TurntableMonitor_Busy", false);  // ★ 新增
                FlagCondition.SetFlag("WorkAbort", false);
                FlagCondition.SetFlag("WorkAborting", false);
                LaserCutHandler.DropFailureCount = 0;

            },

            OnAfterExecute = (ctx, result) =>
            {
                ctx.Set("FullTestCompleted", result.Success);
                ctx.Set("FullTestTime", result.Duration);

                // 记录失败信息
                if (!result.Success && !string.IsNullOrEmpty(result.FailedHandlerId))
                {
                    ctx.Set("FailedHandlerId", result.FailedHandlerId);
                    ctx.Set("FailureReason", result.FailureReason);
                }

                // ★ 注释：不再需要手动设置System_Stop_Request
                // 因为SequenceExecutor现在会在失败时自动触发ExecuteAtEnd Handler
            }
        };

        #endregion

        //#region 完整测试序列

        ///// <summary>
        ///// 完整测试序列（扩展版）
        ///// 
        ///// 执行流程：
        ///// 1. SystemStartup → 使能伺服、运行AR程序
        ///// 2. RobotCommand → 收到WAIT_INIT后发送INIT（自动触发）
        ///// 3. DiskVision → 收到READY后自动触发
        ///// 4. BarcodeScan → 条码扫描（小托盘/大托盘扫码请求触发）
        ///// 5. SmallTrayVision → 小料盘视觉检测
        ///// 6. SmallTrayDirection → 小料盘方向检测
        ///// 7. LargeTrayVision → 大料盘视觉检测
        ///// 8. LargeTrayDirection → 大料盘方向检测
        ///// 9. LaserVision → 收到激光拍照请求后自动触发（PLC信号 1002.3）
        ///// 10. LaserCut → 收到激光切割请求后自动触发（PLC信号 1002.5）
        ///// 11. SystemShutdown → 停止AR程序、清理资源（序列结束）
        ///// 
        ///// 触发说明：
        ///// - 手动设置: System_Start_Request
        ///// - 自动触发: Robot_WaitInit_Received, Robot_Ready_Received (由TCP命令设置)
        ///// - 托盘触发: SmallTray_PhotoRequest, LargeTray_PhotoRequest 等 (由PLC信号设置)
        ///// - 激光触发: LaserPhoto_Request, LaserCut_RequestSig (由PLC信号设置)
        ///// - 结束清理: System_Stop_Request (由序列自动设置)
        ///// 
        ///// ★ 失败策略：StopAndCleanup（任何Handler失败即停止并清理）
        ///// </summary>
        //public static HandlerSequence FullTestSequence => new HandlerSequence
        //{
        //    SequenceId = "FullTest",
        //    SequenceName = "完整测试流程",
        //    Description = "执行完整测试（启动→命令→视觉→托盘→激光→停止清理）",

        //    // ★ 默认条件配置（推荐给用户）
        //    DefaultConditionIds = new List<string>
        //    {
        //        "System_Start_Request",      // 启动入口（手动设置）
        //    },

        //    // ★ 单次执行模式
        //    ExecutionMode = SequenceExecutionMode.SingleRun,

        //    // ★ 失败策略：失败即停止并清理
        //    FailurePolicy = SequenceFailurePolicy.StopAndCleanup,

        //    ExpectedHandlers = new List<ExpectedHandler>
        //    {
        //        // 1. 系统启动（入口点）
        //        new ExpectedHandler
        //        {
        //            HandlerId = "SystemStartup",
        //            IsRequired = true,
        //            TriggerConditionIds = new List<string> { },
        //            MaxExecutions = -1
        //        },


        //        // 1.5 转盘监控（高优先级，确保转盘状态同步）
        //        new ExpectedHandler
        //        {
        //            HandlerId = "TurntableMonitor",
        //            IsRequired = false,  // 非必须，信号驱动
        //            TriggerConditionIds = new List<string> { },
        //            MaxExecutions = -1   // 无限制，每次转盘转动都会触发
        //        },

        //        // 2. 机器人命令处理（自动触发 - WAIT_INIT）
        //        new ExpectedHandler
        //        {
        //            HandlerId = "RobotCommand",
        //            IsRequired = true,
        //            TriggerConditionIds = new List<string> { },
        //            MaxExecutions = -1
        //        },

        //        // 3. 振动盘视觉
        //        new ExpectedHandler
        //        {
        //            HandlerId = "DiskVision",
        //            IsRequired = true,
        //            TriggerConditionIds = new List<string> { },
        //            MaxExecutions = -1
        //        },

        //        // ★ 4. 条码扫描（PLC信号触发 - 小托盘/大托盘扫码请求）
        //        new ExpectedHandler
        //        {
        //            HandlerId = "BarcodeScan",
        //            IsRequired = false,  // 可选，不是所有流程都需要扫码
        //            TriggerConditionIds = new List<string> { },
        //            MaxExecutions = -1
        //        },

        //        // ★ 5. 小料盘视觉处理（PLC信号 SmallTray_PhotoRequest 触发）
        //        new ExpectedHandler
        //        {
        //            HandlerId = "SmallTrayVision",
        //            IsRequired = false,  // 可选
        //            TriggerConditionIds = new List<string> { },
        //            MaxExecutions = -1
        //        },

        //        // ★ 6. 小料盘方向检测（PLC信号 SmallTray_DetectDirRequest 触发）
        //        new ExpectedHandler
        //        {
        //            HandlerId = "SmallTrayDirection",
        //            IsRequired = false,  // 可选
        //            TriggerConditionIds = new List<string> { },
        //            MaxExecutions = -1
        //        },

        //        // ★ 7. 大料盘视觉处理（PLC信号 LargeTray_PhotoRequest 触发）
        //        new ExpectedHandler
        //        {
        //            HandlerId = "LargeTrayVision",
        //            IsRequired = false,  // 可选
        //            TriggerConditionIds = new List<string> { },
        //            MaxExecutions = -1
        //        },

        //        // ★ 8. 大料盘方向检测（PLC信号 LargeTray_DetectDirRequest 触发）
        //        new ExpectedHandler
        //        {
        //            HandlerId = "LargeTrayDirection",
        //            IsRequired = false,  // 可选
        //            TriggerConditionIds = new List<string> { },
        //            MaxExecutions = -1
        //        },

        //        // 9. 激光视觉处理（PLC信号 LaserPhoto_Request 触发）
        //        // ★ 由 PLC 地址 1002.3 (拍照请求信号) 自动触发
        //        new ExpectedHandler
        //        {
        //            HandlerId = "LaserVision",
        //            IsRequired = true,
        //            TriggerConditionIds = new List<string> { },
        //            MaxExecutions = -1
        //        },

        //        // 10. 激光切割（PLC信号 LaserCut_RequestSig 触发）
        //        // ★ 由 PLC 地址 1002.5 (激光切割请求信号) 自动触发
        //        new ExpectedHandler
        //        {
        //            HandlerId = "LaserCut",
        //            IsRequired = true,
        //            TriggerConditionIds = new List<string> { },
        //            MaxExecutions = -1
        //        },

        //        // ★ 11. 系统停止（序列结束时清理 - ExecuteAtEnd自动触发）
        //        new ExpectedHandler
        //        {
        //            HandlerId = "SystemShutdown",
        //            IsRequired = true,
        //            TriggerConditionIds = new List<string> { },
        //            MaxExecutions = 1,
        //            ExecuteAtEnd = true  // ★ 关键：在其他Handler完成后自动触发
        //        }
        //    },

        //    Timeout = TimeSpan.FromMinutes(5),  // ★ 增加超时时间（激光切割可能需要更长时间）
        //    AllowPartialSuccess = false,

        //    OnBeforeExecute = ctx =>
        //    {
        //        // 清除所有忙碌标志
        //        // ★ 新增：清除停止请求标志（防止残留导致 SystemShutdown 提前触发）
        //        FlagCondition.SetFlag("System_Stop_Request", false);
        //        FlagCondition.SetFlag("SystemStartup_Busy", false);
        //        FlagCondition.SetFlag("RobotCommand_Busy", false);
        //        FlagCondition.SetFlag("DiskVision_Busy", false);
        //        FlagCondition.SetFlag("BarcodeScan_Busy", false);           // ★ 新增
        //        FlagCondition.SetFlag("SmallTrayVision_Busy", false);       // ★ 新增
        //        FlagCondition.SetFlag("SmallTrayDirection_Busy", false);    // ★ 新增
        //        FlagCondition.SetFlag("LargeTrayVision_Busy", false);       // ★ 新增
        //        FlagCondition.SetFlag("LargeTrayDirection_Busy", false);    // ★ 新增
        //        FlagCondition.SetFlag("LaserVision_Busy", false);
        //        FlagCondition.SetFlag("LaserCut_Busy", false);
        //        FlagCondition.SetFlag("SystemShutdown_Busy", false);
        //        FlagCondition.SetFlag("TurntableMonitor_Busy", false);  // ★ 新增

        //    },

        //    OnAfterExecute = (ctx, result) =>
        //    {
        //        ctx.Set("FullTestCompleted", result.Success);
        //        ctx.Set("FullTestTime", result.Duration);

        //        // 记录失败信息
        //        if (!result.Success && !string.IsNullOrEmpty(result.FailedHandlerId))
        //        {
        //            ctx.Set("FailedHandlerId", result.FailedHandlerId);
        //            ctx.Set("FailureReason", result.FailureReason);
        //        }

        //        // ★ 注释：不再需要手动设置System_Stop_Request
        //        // 因为SequenceExecutor现在会在失败时自动触发ExecuteAtEnd Handler
        //    }
        //};

        //#endregion

        #region 激光测试序列

        /// <summary>
        /// 激光测试序列（从 FullTest 精简而来）
        /// 
        /// 执行流程：
        /// 1. SystemStartup → 使能伺服、运行AR程序
        /// 2. RobotCommand → 收到WAIT_INIT后发送INIT（自动触发）
        /// 3. LaserVision → 收到激光拍照请求后自动触发（PLC信号 1002.3）
        /// 4. LaserCut → 收到激光切割请求后自动触发（PLC信号 1002.5）
        /// 5. SystemShutdown → 停止AR程序、清理资源（序列结束）
        /// 
        /// 说明：
        /// - 仅包含激光相关Handler，不包含振动盘上料和托盘处理
        /// - 适用于单独测试激光切割功能
        /// 
        /// 触发说明：
        /// - 手动设置: System_Start_Request
        /// - 激光触发: LaserPhoto_Request, LaserCut_RequestSig (由PLC信号设置)
        /// - 结束清理: System_Stop_Request (由序列自动设置)
        /// 
        /// ★ 失败策略：StopAndCleanup（任何Handler失败即停止并清理）
        /// </summary>
        public static HandlerSequence LaserTestSequence => new HandlerSequence
        {
            SequenceId = "LaserTest",
            SequenceName = "激光测试流程",
            Description = "执行激光测试（启动→命令→激光视觉→激光切割→停止清理）",

            // ★ 默认条件配置（推荐给用户）
            DefaultConditionIds = new List<string>
            {
                "System_Start_Request",      // 启动入口（手动设置）
            },

            // ★ 单次执行模式
            ExecutionMode = SequenceExecutionMode.SingleRun,

            // ★ 失败策略：失败即停止并清理
            FailurePolicy = SequenceFailurePolicy.StopAndCleanup,

            ExpectedHandlers = new List<ExpectedHandler>
            {
                // 1. 系统启动（入口点）
                new ExpectedHandler
                {
                    HandlerId = "SystemStartup",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // 1.5 转盘监控（高优先级，确保转盘状态同步）
                new ExpectedHandler
                {
                    HandlerId = "TurntableMonitor",
                    IsRequired = false,  // 非必须，信号驱动
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1   // 无限制，每次转盘转动都会触发
                },

                // 2. 机器人命令处理（自动触发 - WAIT_INIT）
                new ExpectedHandler
                {
                    HandlerId = "RobotCommand",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // 3. 激光视觉处理（PLC信号 LaserPhoto_Request 触发）
                // ★ 由 PLC 地址 1002.3 (拍照请求信号) 自动触发
                new ExpectedHandler
                {
                    HandlerId = "LaserVision",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // 4. 激光切割（PLC信号 LaserCut_RequestSig 触发）
                // ★ 由 PLC 地址 1002.5 (激光切割请求信号) 自动触发
                new ExpectedHandler
                {
                    HandlerId = "LaserCut",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // ★ 5. 系统停止（序列结束时清理 - ExecuteAtEnd自动触发）
                new ExpectedHandler
                {
                    HandlerId = "SystemShutdown",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = 1,
                    ExecuteAtEnd = true  // ★ 关键：在其他Handler完成后自动触发
                }
            },

            Timeout = TimeSpan.FromMinutes(3),
            AllowPartialSuccess = false,

            OnBeforeExecute = ctx =>
            {
                // 清除所有忙碌标志
                FlagCondition.SetFlag("System_Stop_Request", false);
                FlagCondition.SetFlag("SystemStartup_Busy", false);
                FlagCondition.SetFlag("RobotCommand_Busy", false);
                FlagCondition.SetFlag("LaserVision_Busy", false);
                FlagCondition.SetFlag("LaserCut_Busy", false);
                FlagCondition.SetFlag("SystemShutdown_Busy", false);
                FlagCondition.SetFlag("TurntableMonitor_Busy", false);  // ★ 新增
                FlagCondition.SetFlag("WorkAbort",false);
                FlagCondition.SetFlag("WorkAborting", false);
                LaserCutHandler.DropFailureCount = 0;
            },

            OnAfterExecute = (ctx, result) =>
            {
                ctx.Set("LaserTestCompleted", result.Success);
                ctx.Set("LaserTestTime", result.Duration);

                // 记录失败信息
                if (!result.Success && !string.IsNullOrEmpty(result.FailedHandlerId))
                {
                    ctx.Set("FailedHandlerId", result.FailedHandlerId);
                    ctx.Set("FailureReason", result.FailureReason);
                }
            }
        };

        #endregion

        #region 振动盘上料流程

        /// <summary>
        /// 振动盘上料流程
        /// 
        /// 执行流程：
        /// 1. SystemStartup → 使能伺服、运行AR程序
        /// 2. RobotCommand → 收到WAIT_INIT后发送INIT（自动触发）
        /// 3. DiskVision → 收到READY后自动触发
        /// 4. SystemShutdown → 停止AR程序、清理资源（序列结束）
        /// 
        /// 说明：
        /// - 仅包含振动盘上料相关Handler，不包含激光切割流程
        /// - 适用于单独测试振动盘上料功能
        /// 
        /// 触发说明：
        /// - 手动设置: System_Start_Request
        /// - 自动触发: Robot_WaitInit_Received, Robot_Ready_Received (由TCP命令设置)
        /// - 结束清理: System_Stop_Request (由序列自动设置)
        /// 
        /// ★ 失败策略：StopAndCleanup（任何Handler失败即停止并清理）
        /// </summary>
        public static HandlerSequence VibratorFeedSequence => new HandlerSequence
        {
            SequenceId = "VibratorFeed",
            SequenceName = "振动盘上料流程",
            Description = "执行振动盘上料测试（启动→命令→视觉→停止清理）",

            // ★ 默认条件配置（推荐给用户）
            DefaultConditionIds = new List<string>
            {
                "System_Start_Request",      // 启动入口（手动设置）
            },

            // ★ 单次执行模式
            ExecutionMode = SequenceExecutionMode.SingleRun,

            // ★ 失败策略：失败即停止并清理
            FailurePolicy = SequenceFailurePolicy.StopAndCleanup,

            ExpectedHandlers = new List<ExpectedHandler>
            {
                // 1. 系统启动（入口点）
                new ExpectedHandler
                {
                    HandlerId = "SystemStartup",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },
                // 1.5 转盘监控（高优先级，确保转盘状态同步）
                new ExpectedHandler
                {
                    HandlerId = "TurntableMonitor",
                    IsRequired = false,  // 非必须，信号驱动
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1   // 无限制，每次转盘转动都会触发
                },
                // 2. 机器人命令处理（自动触发 - WAIT_INIT）
                new ExpectedHandler
                {
                    HandlerId = "RobotCommand",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // 3. 振动盘视觉
                new ExpectedHandler
                {
                    HandlerId = "DiskVision",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = -1
                },

                // ★ 4. 系统停止（序列结束时清理 - ExecuteAtEnd自动触发）
                new ExpectedHandler
                {
                    HandlerId = "SystemShutdown",
                    IsRequired = true,
                    TriggerConditionIds = new List<string> { },
                    MaxExecutions = 1,
                    ExecuteAtEnd = true  // ★ 关键：在其他Handler完成后自动触发
                }
            },

            Timeout = TimeSpan.FromMinutes(3),
            AllowPartialSuccess = false,

            OnBeforeExecute = ctx =>
            {
                // 清除所有忙碌标志
                // ★ 新增：清除停止请求标志（防止残留导致 SystemShutdown 提前触发）
                FlagCondition.SetFlag("System_Stop_Request", false);
                FlagCondition.SetFlag("SystemStartup_Busy", false);
                FlagCondition.SetFlag("RobotCommand_Busy", false);
                FlagCondition.SetFlag("DiskVision_Busy", false);
                FlagCondition.SetFlag("SystemShutdown_Busy", false);
                FlagCondition.SetFlag("TurntableMonitor_Busy", false);  // ★ 新增

            },

            OnAfterExecute = (ctx, result) =>
            {
                ctx.Set("VibratorFeedCompleted", result.Success);
                ctx.Set("VibratorFeedTime", result.Duration);

                // 记录失败信息
                if (!result.Success && !string.IsNullOrEmpty(result.FailedHandlerId))
                {
                    ctx.Set("FailedHandlerId", result.FailedHandlerId);
                    ctx.Set("FailureReason", result.FailureReason);
                }

                // ★ 注释：不再需要手动设置System_Stop_Request
                // 因为SequenceExecutor现在会在失败时自动触发ExecuteAtEnd Handler
            }
        };

        #endregion
    }

    #region 序列扩展方法

    /// <summary>
    /// 序列流式API扩展方法
    /// </summary>
    public static class HandlerSequenceExtensions
    {
        /// <summary>
        /// 添加必须执行的Handler
        /// </summary>
        public static HandlerSequence Expect(this HandlerSequence sequence, string handlerId, params string[] triggerConditionIds)
        {
            sequence.ExpectedHandlers.Add(new ExpectedHandler
            {
                HandlerId = handlerId,
                IsRequired = true,
                TriggerConditionIds = new List<string>(triggerConditionIds)
            });
            return sequence;
        }

        /// <summary>
        /// 添加可选执行的Handler
        /// </summary>
        public static HandlerSequence Optional(this HandlerSequence sequence, string handlerId, params string[] triggerConditionIds)
        {
            sequence.ExpectedHandlers.Add(new ExpectedHandler
            {
                HandlerId = handlerId,
                IsRequired = false,
                TriggerConditionIds = new List<string>(triggerConditionIds)
            });
            return sequence;
        }

        /// <summary>
        /// 设置超时时间
        /// </summary>
        public static HandlerSequence WithTimeout(this HandlerSequence sequence, TimeSpan timeout)
        {
            sequence.Timeout = timeout;
            return sequence;
        }

        /// <summary>
        /// 设置失败策略
        /// </summary>
        public static HandlerSequence WithFailurePolicy(this HandlerSequence sequence, SequenceFailurePolicy policy)
        {
            sequence.FailurePolicy = policy;
            return sequence;
        }

        /// <summary>
        /// 设置执行模式
        /// </summary>
        public static HandlerSequence WithExecutionMode(this HandlerSequence sequence, SequenceExecutionMode mode)
        {
            sequence.ExecutionMode = mode;
            return sequence;
        }

        /// <summary>
        /// 设置允许部分成功
        /// </summary>
        public static HandlerSequence AllowPartial(this HandlerSequence sequence)
        {
            sequence.AllowPartialSuccess = true;
            return sequence;
        }

        /// <summary>
        /// 设置默认条件
        /// </summary>
        public static HandlerSequence WithDefaultConditions(this HandlerSequence sequence, params string[] conditionIds)
        {
            sequence.DefaultConditionIds = new List<string>(conditionIds);
            return sequence;
        }
    }

    #endregion

    #region UI绑定辅助类

    /// <summary>
    /// 序列选择项（供UI绑定）
    /// </summary>
    public class SequenceSelectionItem
    {
        public string SequenceId { get; set; }
        public string SequenceName { get; set; }
        public string Description { get; set; }
        public List<string> HandlerIds { get; set; } = new List<string>();
        public List<string> ConditionIds { get; set; } = new List<string>();
        public List<string> DefaultConditionIds { get; set; } = new List<string>();
        public bool IsSelected { get; set; }
        public string FailurePolicyText { get; set; }

        public static SequenceSelectionItem FromSequence(HandlerSequence sequence)
        {
            return new SequenceSelectionItem
            {
                SequenceId = sequence.SequenceId,
                SequenceName = sequence.SequenceName,
                Description = sequence.Description,
                HandlerIds = sequence.ExpectedHandlers.ConvertAll(h => h.HandlerId),
                ConditionIds = sequence.GetAllTriggerConditionIds() as List<string>
                    ?? new List<string>(sequence.GetAllTriggerConditionIds()),
                DefaultConditionIds = sequence.DefaultConditionIds ?? new List<string>(),
                FailurePolicyText = GetFailurePolicyText(sequence.FailurePolicy)
            };
        }

        private static string GetFailurePolicyText(SequenceFailurePolicy policy)
        {
            switch (policy)
            {
                case SequenceFailurePolicy.Continue:
                    return "继续执行";
                case SequenceFailurePolicy.StopOnFirstFailure:
                    return "失败即停止";
                case SequenceFailurePolicy.StopAndCleanup:
                    return "失败停止并清理";
                default:
                    return "未知";
            }
        }
    }

    /// <summary>
    /// 条件选择项（供UI绑定）
    /// </summary>
    public class ConditionSelectionItem
    {
        public string ConditionId { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Group { get; set; }
        public string RelatedHandlerId { get; set; }
        public bool IsSelected { get; set; }
        public bool IsActive { get; set; }
        public bool IsRecommended { get; set; }

        public static ConditionSelectionItem FromCondition(ConditionItem condition)
        {
            return new ConditionSelectionItem
            {
                ConditionId = condition.ConditionId,
                DisplayName = condition.DisplayName,
                Description = condition.Description,
                Group = condition.Group,
                RelatedHandlerId = condition.RelatedHandlerId,
                IsActive = condition?.CheckAction?.Invoke() ?? false
            };
        }

        public void Refresh()
        {
            var condition = ConditionCatalog.Get(ConditionId);
            IsActive = condition?.CheckAction?.Invoke() ?? false;
        }
    }

    #endregion
}