using Microsoft.Extensions.DependencyInjection;
using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Handlers
{
    /// <summary>
    /// 处理器上下文实现
    /// 
    /// ★ 修改：新增 Signal 属性，用于信号读写（支持别名）
    /// </summary>
    public class HandlerContext : IHandlerContext
    {
        private readonly ConcurrentDictionary<string, object> _flags = new ConcurrentDictionary<string, object>();
        // ★ 新增：服务提供者
        private readonly IServiceProvider _serviceProvider;
        public IProductionContext Production { get; }
        public IDeviceManager Devices { get; }
        public IDataFlowManager DataFlow { get; }

        /// <summary>
        /// ★ 新增：信号访问器
        /// 
        /// 提供 PLC 信号的读写能力，支持别名解析。
        /// </summary>
        public ISignalAccessor Signal { get; }

        /// <summary>
        /// 创建处理器上下文（无信号访问器）
        /// 向后兼容，Signal 为 null
        /// </summary>
        public HandlerContext(IProductionContext production, IDeviceManager devices, IDataFlowManager dataFlow)
            : this(production, devices, dataFlow, null)
        {
        }

        /// <summary>
        /// ★ 新增：创建处理器上下文（带信号访问器）
        /// </summary>
        public HandlerContext(
            IProductionContext production,
            IDeviceManager devices,
            IDataFlowManager dataFlow,
            ISignalAccessor signalAccessor)
        {
            Production = production;
            Devices = devices;
            DataFlow = dataFlow;
            Signal = signalAccessor;
        }

        /// <summary>
        /// ★ 新增：创建处理器上下文（完整版 - 带服务提供者）
        /// </summary>
        /// <param name="production">生产上下文</param>
        /// <param name="devices">设备管理器</param>
        /// <param name="dataFlow">数据流管理器</param>
        /// <param name="signalAccessor">信号访问器</param>
        /// <param name="serviceProvider">服务提供者（用于 GetService）</param>
        public HandlerContext(
            IProductionContext production,
            IDeviceManager devices,
            IDataFlowManager dataFlow,
            ISignalAccessor signalAccessor,
            IServiceProvider serviceProvider)
        {
            Production = production;
            Devices = devices;
            DataFlow = dataFlow;
            Signal = signalAccessor;
            _serviceProvider = serviceProvider;
        }

        public T GetDevice<T>(string id) where T : class, IDevice => Devices.Get<T>(id);

        public IPlcDevice GetPlc(string id = "PLC") => Devices.Get<IPlcDevice>(id);

        public IRobotDevice GetRobot(string id = "Robot") => Devices.Get<IRobotDevice>(id);

        public IVisionDevice GetVision(string id = "Vision") => Devices.Get<IVisionDevice>(id);

        public IScannerDevice GetScanner(string id = "Scanner") => Devices.Get<IScannerDevice>(id);

        public ILaserDevice GetLaser(string id = "Laser") => Devices.Get<ILaserDevice>(id);

        public IVibratorDevice GetVibrator(string id = "Vibrator") => Devices.Get<IVibratorDevice>(id);

        public void SetFlag<T>(string key, T value) => _flags[key] = value;

        public T GetFlag<T>(string key, T defaultValue = default(T))
        {
            if (_flags.TryGetValue(key, out var val) && val is T t) return t;
            return defaultValue;
        }

        public bool RemoveFlag(string key) => _flags.TryRemove(key, out _);

        #region ★ 新增：服务获取方法

        /// <summary>
        /// ★ 新增：获取注册的服务实例
        /// </summary>
        /// <typeparam name="T">服务类型</typeparam>
        /// <returns>服务实例，未注册或不可用则返回 null</returns>
        public T GetService<T>() where T : class
        {
            if (_serviceProvider == null)
                return null;

            try
            {
                // 使用 GetService 而不是 GetRequiredService，避免抛出异常
                return _serviceProvider.GetService(typeof(T)) as T;
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}