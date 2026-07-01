using System.Collections.Generic;

namespace SeedCut.Framework.Services.Interfaces
{
    /// <summary>
    /// Handler注册中心接口
    /// </summary>
    public interface IHandlerRegistry
    {
        /// <summary>
        /// 注册单个Handler
        /// </summary>
        void Register(ISignalHandler handler);

        /// <summary>
        /// 注册所有Handler（手动列表）
        /// </summary>
        void RegisterAll();

        /// <summary>
        /// 获取所有已注册的Handler
        /// </summary>
        IReadOnlyList<ISignalHandler> GetAll();

        /// <summary>
        /// 根据ID获取Handler
        /// </summary>
        ISignalHandler Get(string handlerId);

        /// <summary>
        /// 检查Handler是否已注册
        /// </summary>
        bool IsRegistered(string handlerId);

        /// <summary>
        /// 获取Handler数量
        /// </summary>
        int Count { get; }
    }
}
