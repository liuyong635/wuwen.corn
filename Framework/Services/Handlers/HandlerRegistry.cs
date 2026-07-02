using SeedCut.Framework.Services.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// Handler注册中心实现
    /// 负责管理所有SignalHandler的注册和查询
    /// </summary>
    public class HandlerRegistry : IHandlerRegistry
    {
        private readonly List<ISignalHandler> _handlers = new List<ISignalHandler>();
        private readonly object _lock = new object();
        private readonly ILogService _logService;

        public int Count
        {
            get { lock (_lock) { return _handlers.Count; } }
        }

        public HandlerRegistry(ILogService logService = null)
        {
            _logService = logService;
        }

        /// <summary>
        /// 注册单个Handler
        /// </summary>
        public void Register(ISignalHandler handler)
        {
            if (handler == null) return;

            lock (_lock)
            {
                // 移除同ID的旧Handler
                _handlers.RemoveAll(h => h.HandlerId == handler.HandlerId);
                _handlers.Add(handler);

                // 按优先级排序（高优先级在前）
                _handlers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            }

            _logService?.Debug("[HandlerRegistry] 已注册: {0} (优先级: {1})",
                handler.HandlerId, handler.Priority);
        }

        /// <summary>
        /// 注册所有Handler（手动列表）
        /// </summary>
        public void RegisterAll()
        {
            _logService?.Information("[HandlerRegistry] 开始注册所有Handler...");

            var handlers = this.GetType().Assembly.GetTypes().Where(type => !type.IsAbstract && type.IsClass && type.IsSubclassOf(typeof(SignalHandlerBase))).ToList();
         
            foreach (var handler in handlers)
            {
                var h = System.Activator.CreateInstance(handler) as ISignalHandler;
                if(h != null)
                {
                    Register(h);
                }
            }

            // ============================================================
            //// 手动注册所有Handler
            //// 按功能模块分组，方便维护
            //// ============================================================

            //// 初始化类Handler
            //Register(new CircularAxisInitHandler());

            //// 机器人相关Handler
            ////Register(new RobotCommandHandler());
            //Register(new SystemStartupHandler());
            //Register(new VisionCoordinateHandler());
            //Register(new RobotActionHandler());
            //Register(new DiskVibrateHandler());

            //// 视觉相关Handler
            //Register(new DiskVisionHandler());
            //Register(new LaserVisionHandler());
            //Register(new LargeTrayVisionHandler());
            //Register(new SmallTrayVisionHandler());


            //// 方向检测Handler
            //Register(new SmallTrayDirectionHandler());
            //Register(new LargeTrayDirectionHandler());

            //// 激光切割Handler
            //Register(new LaserCutHandler());


            //// 条码扫描Handler
            //Register(new BarcodeScanHandler());
            //Register(new SystemShutdownHandler());



            //Register(new TurntableMonitorHandler());



            _logService?.Information("[HandlerRegistry] 注册完成，共 {0} 个Handler", Count);
        }

        /// <summary>
        /// 获取所有已注册的Handler
        /// </summary>
        public IReadOnlyList<ISignalHandler> GetAll()
        {
            lock (_lock)
            {
                return _handlers.ToList();
            }
        }

        /// <summary>
        /// 根据ID获取Handler
        /// </summary>
        public ISignalHandler Get(string handlerId)
        {
            lock (_lock)
            {
                return _handlers.FirstOrDefault(h => h.HandlerId == handlerId);
            }
        }

        /// <summary>
        /// 检查Handler是否已注册
        /// </summary>
        public bool IsRegistered(string handlerId)
        {
            lock (_lock)
            {
                return _handlers.Any(h => h.HandlerId == handlerId);
            }
        }
    }
}
