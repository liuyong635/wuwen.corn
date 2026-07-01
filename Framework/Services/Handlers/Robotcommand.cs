using System;
using System.Collections.Concurrent;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// 机器人命令类型枚举
    /// 
    /// 对应 AR → C# 的所有命令类型
    /// </summary>
    public enum RobotCommandType
    {
        Unknown = 0,

        /// <summary>请求初始化 (WAIT_INIT)</summary>
        WaitInit,

        /// <summary>就绪 (READY)</summary>
        Ready,

        /// <summary>到达抓料位 (AT_CATCH:n)</summary>
        AtCatch,

        /// <summary>夹爪请求 (CLAMP)</summary>
        Clamp,

        /// <summary>抓取完成 (GRABBED)</summary>
        Grabbed,

        /// <summary>请求物料检测 (CHECK)</summary>
        Check,

        /// <summary>飞拍已触发 — 仅日志 (FLY_CAPTURE)</summary>
        FlyCapture,

        /// <summary>飞拍偏差日志 (FLY_OFFSET;x;y;c;status)</summary>
        FlyOffset,

        /// <summary>松爪请求 (RELEASE)</summary>
        Release,

        /// <summary>放料完成 (PLACED)</summary>
        Placed,

        /// <summary>全部完成 (BATCH_DONE)</summary>
        BatchDone,

        /// <summary>错误 (ERR:msg)</summary>
        Error
    }

    /// <summary>
    /// 机器人命令数据模型
    /// 
    /// 由 TCP 接收层解析后推入 DataFlow 队列，
    /// 由 RobotActionHandler 按优先级消费。
    /// </summary>
    public class RobotCommand
    {
        /// <summary>命令类型</summary>
        public RobotCommandType Type { get; set; }

        /// <summary>原始消息文本</summary>
        public string RawMessage { get; set; }

        /// <summary>命令携带的数据（如 AT_CATCH 的点索引、ERR 的错误消息）</summary>
        public string Data { get; set; }

        /// <summary>接收时间戳</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 命令优先级（数字越大越优先处理）
        /// </summary>
        public int Priority
        {
            get
            {
                switch (Type)
                {
                    case RobotCommandType.Check: return 100;      // 传感器查询最快响应
                    case RobotCommandType.Clamp: return 95;       // IO操作快速响应
                    case RobotCommandType.Release: return 95;
                    case RobotCommandType.AtCatch: return 90;     // 需回复GRAB
                    case RobotCommandType.Error: return 80;       // 错误需要及时处理
                    case RobotCommandType.Grabbed: return 50;     // 状态上报
                    case RobotCommandType.Placed: return 50;
                    case RobotCommandType.Ready: return 40;
                    case RobotCommandType.BatchDone: return 40;
                    case RobotCommandType.FlyCapture: return 30;  // 仅日志，低优先级
                    case RobotCommandType.FlyOffset: return 30;   // 仅日志，低优先级
                    case RobotCommandType.WaitInit: return 30;
                    default: return 0;
                }
            }
        }

        /// <summary>
        /// 从原始 TCP 消息解析命令
        /// </summary>
        public static RobotCommand Parse(string message)
        {
            if (string.IsNullOrEmpty(message))
                return new RobotCommand { Type = RobotCommandType.Unknown, RawMessage = message, Timestamp = DateTime.Now };

            var cmd = new RobotCommand
            {
                RawMessage = message,
                Timestamp = DateTime.Now
            };

            // 带参数的命令
            if (message.StartsWith("AT_CATCH:"))
            {
                cmd.Type = RobotCommandType.AtCatch;
                cmd.Data = message.Substring(9); // 点索引
            }
            else if (message.StartsWith("ERR:"))
            {
                cmd.Type = RobotCommandType.Error;
                cmd.Data = message.Substring(4); // 错误消息
            }
            else if (message.StartsWith("FLY_OFFSET;"))
            {
                cmd.Type = RobotCommandType.FlyOffset;
                cmd.Data = message.Substring(11); // "x;y;c;status"
            }
            else
            {
                // 无参数命令
                switch (message)
                {
                    case "WAIT_INIT": cmd.Type = RobotCommandType.WaitInit; break;
                    case "READY": cmd.Type = RobotCommandType.Ready; break;
                    case "CLAMP": cmd.Type = RobotCommandType.Clamp; break;
                    case "GRABBED": cmd.Type = RobotCommandType.Grabbed; break;
                    case "CHECK": cmd.Type = RobotCommandType.Check; break;
                    case "FLY_CAPTURE": cmd.Type = RobotCommandType.FlyCapture; break;
                    case "RELEASE": cmd.Type = RobotCommandType.Release; break;
                    case "PLACED": cmd.Type = RobotCommandType.Placed; break;
                    case "BATCH_DONE": cmd.Type = RobotCommandType.BatchDone; break;
                    default: cmd.Type = RobotCommandType.Unknown; cmd.Data = message; break;
                }
            }

            return cmd;
        }

        #region 共享命令队列

        /// <summary>
        /// 共享命令队列
        /// TCP接收层（RobotDeviceAdapter）推入，RobotActionHandler 消费
        /// 
        /// 为什么用静态队列：
        ///   RobotDeviceAdapter 没有 IDataFlow 引用，
        ///   用静态 ConcurrentQueue 最简单，无需改 DI 注册。
        /// </summary>
        private static readonly ConcurrentQueue<RobotCommand> _sharedQueue
            = new ConcurrentQueue<RobotCommand>();

        /// <summary>推入命令</summary>
        public static void Enqueue(RobotCommand cmd)
        {
            _sharedQueue.Enqueue(cmd);
        }

        /// <summary>取出命令</summary>
        public static bool TryDequeue(out RobotCommand cmd)
        {
            return _sharedQueue.TryDequeue(out cmd);
        }

        /// <summary>清空队列</summary>
        public static void ClearQueue()
        {
            while (_sharedQueue.TryDequeue(out _)) { }
        }

        /// <summary>队列是否为空</summary>
        public static bool IsQueueEmpty => _sharedQueue.IsEmpty;

        #endregion
    }
}