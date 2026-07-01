using System;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Interfaces
{
    /// <summary>
    /// 信号访问接口
    /// 
    /// 提供统一的 PLC 信号读写能力，支持别名解析。
    /// 
    /// 设计说明：
    /// 1. Handler 中使用英文别名（如 "Material_Detect"）
    /// 2. 接口实现自动解析为 CSV 中的中文名（如 "物料检测传感器"）
    /// 3. 最终通过 PLCAddressRegistry 获取实际地址并读写
    /// 
    /// 与 ISignalMonitor 的区别：
    /// - ISignalMonitor: 主要用于信号变化监控，GetSignal() 读取的是缓存值
    /// - ISignalAccessor: 用于主动读写 PLC，ReadBit() 直接读取 PLC 当前值
    /// 
    /// 使用方式：
    /// ```csharp
    /// // 在 Handler 中通过 context 获取
    /// var signal = ctx.Signal;
    /// 
    /// // 读取信号（支持别名）
    /// bool hasMaterial = signal.ReadBit("Material_Detect");
    /// 
    /// // 写入信号（支持别名）
    /// signal.WriteBit("Gripper_Clamp", true);
    /// 
    /// // 写入脉冲
    /// signal.WriteBitPulse("UpperPC_Start", 500);
    /// 
    /// // 等待信号
    /// await signal.WaitForBitAsync("LaserPhoto_Complete", true, TimeSpan.FromSeconds(10), ct);
    /// ```
    /// </summary>
    public interface ISignalAccessor
    {
        #region 位操作

        /// <summary>
        /// 读取位信号（直接读 PLC，支持别名）
        /// </summary>
        /// <param name="aliasOrName">信号别名或 CSV 地址名</param>
        /// <returns>信号值</returns>
        bool ReadBit(string aliasOrName);

        /// <summary>
        /// 写入位信号（支持别名）
        /// </summary>
        /// <param name="aliasOrName">信号别名或 CSV 地址名</param>
        /// <param name="value">要写入的值</param>
        void WriteBit(string aliasOrName, bool value);

        /// <summary>
        /// 写入脉冲信号（支持别名）
        /// 先写入 true，延时后写入 false
        /// </summary>
        /// <param name="aliasOrName">信号别名或 CSV 地址名</param>
        /// <param name="durationMs">脉冲持续时间（毫秒）</param>
        void WriteBitPulse(string aliasOrName, int durationMs = 50);

        /// <summary>
        /// 异步写入脉冲信号（支持别名）
        /// </summary>
        Task WriteBitPulseAsync(string aliasOrName, int durationMs = 50, CancellationToken ct = default);

        /// <summary>
        /// 切换位信号（取反）
        /// </summary>
        /// <param name="aliasOrName">信号别名或 CSV 地址名</param>
        void ToggleBit(string aliasOrName);

        #endregion

        #region 整数操作

        /// <summary>
        /// 读取 Int16 信号（支持别名）
        /// </summary>
        short ReadInt16(string aliasOrName);

        /// <summary>
        /// 写入 Int16 信号（支持别名）
        /// </summary>
        void WriteInt16(string aliasOrName, short value);

        #endregion

        #region 浮点数操作

        /// <summary>
        /// 读取 Float 信号（支持别名）
        /// </summary>
        float ReadFloat(string aliasOrName);

        /// <summary>
        /// 写入 Float 信号（支持别名）
        /// </summary>
        void WriteFloat(string aliasOrName, float value);

        #endregion

        #region 等待信号

        /// <summary>
        /// 等待位信号达到指定值
        /// </summary>
        /// <param name="aliasOrName">信号别名或 CSV 地址名</param>
        /// <param name="expectedValue">期望的值</param>
        /// <param name="timeout">超时时间</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>是否在超时前达到期望值</returns>
        Task<bool> WaitForBitAsync(string aliasOrName, bool expectedValue, TimeSpan timeout, CancellationToken ct = default);

        /// <summary>
        /// 等待 Int16 信号达到指定范围
        /// </summary>
        Task<bool> WaitForInt16InRangeAsync(string aliasOrName, short minValue, short maxValue, TimeSpan timeout, CancellationToken ct = default);

        /// <summary>
        /// 等待 Float 信号达到指定范围
        /// </summary>
        Task<bool> WaitForFloatInRangeAsync(string aliasOrName, float minValue, float maxValue, TimeSpan timeout, CancellationToken ct = default);

        #endregion

        #region 辅助方法

        /// <summary>
        /// 检查信号是否存在（别名或地址名是否有效）
        /// </summary>
        bool HasSignal(string aliasOrName);

        /// <summary>
        /// 解析别名，返回实际的 CSV 地址名
        /// </summary>
        string ResolveAlias(string aliasOrName);

        #endregion
    }
}