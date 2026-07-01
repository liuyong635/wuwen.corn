using SeedCut.Framework.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Interfaces
{
    /// <summary>
    /// 振动盘设备接口
    /// 参考现有VibratorService实现，使用Modbus TCP通信
    /// </summary>
    public interface IVibratorDevice : IDevice
    {
        #region 状态属性

        /// <summary>
        /// 振动盘状态
        /// </summary>
        VibratorState VibratorState { get; }

        /// <summary>
        /// 光源A是否开启
        /// </summary>
        bool IsLightAOn { get; }

        /// <summary>
        /// 光源B是否开启
        /// </summary>
        bool IsLightBOn { get; }

        /// <summary>
        /// 是否正在进料（抖料）
        /// </summary>
        bool IsFeeding { get; }

        /// <summary>
        /// 是否正在振动
        /// </summary>
        bool IsVibrating { get; }

        /// <summary>
        /// 排料门是否打开
        /// </summary>
        bool IsPourDoorOpen { get; }

        #endregion

        #region 事件

        /// <summary>
        /// 状态变化事件
        /// </summary>
        event EventHandler<VibratorStateChangedEventArgs> StateChanged;

        #endregion

        #region 光源控制

        /// <summary>
        /// 设置光源A状态
        /// </summary>
        Task<bool> SetLightAAsync(bool turnOn, CancellationToken ct = default);

        /// <summary>
        /// 设置光源B状态
        /// </summary>
        Task<bool> SetLightBAsync(bool turnOn, CancellationToken ct = default);

        /// <summary>
        /// 切换光源A状态
        /// </summary>
        Task<bool> ToggleLightAAsync(CancellationToken ct = default);

        /// <summary>
        /// 切换光源B状态
        /// </summary>
        Task<bool> ToggleLightBAsync(CancellationToken ct = default);

        /// <summary>
        /// 读取光源A状态
        /// </summary>
        Task<bool> ReadLightAAsync(CancellationToken ct = default);

        /// <summary>
        /// 读取光源B状态
        /// </summary>
        Task<bool> ReadLightBAsync(CancellationToken ct = default);

        #endregion

        #region 进料控制（抖料）

        /// <summary>
        /// 开始进料（抖料）
        /// </summary>
        Task<bool> StartFeedAsync(CancellationToken ct = default);

        /// <summary>
        /// 停止进料
        /// </summary>
        Task<bool> StopFeedAsync(CancellationToken ct = default);

        /// <summary>
        /// 执行一次进料周期
        /// </summary>
        Task<bool> FeedOnceAsync(int durationMs = 0, CancellationToken ct = default);

        #endregion

        #region 振动控制

        /// <summary>
        /// 启动振动（组合1）
        /// </summary>
        Task<bool> StartVibrationAsync(CancellationToken ct = default);

        /// <summary>
        /// 启动振动（组合2）
        /// </summary>
        Task<bool> StartVibrationGroup2Async(CancellationToken ct = default);

        /// <summary>
        /// 停止振动
        /// </summary>
        Task<bool> StopVibrationAsync(CancellationToken ct = default);

        /// <summary>
        /// 设置振动频率
        /// </summary>
        Task<bool> SetVibrationFrequencyAsync(int frequency, CancellationToken ct = default);

        /// <summary>
        /// 设置振动强度
        /// </summary>
        Task<bool> SetVibrationIntensityAsync(int intensity, CancellationToken ct = default);

        #endregion

        #region 排料门控制

        /// <summary>
        /// 设置排料门状态
        /// </summary>
        Task<bool> SetPourDoorAsync(bool open, CancellationToken ct = default);

        /// <summary>
        /// 切换排料门状态
        /// </summary>
        Task<bool> TogglePourDoorAsync(CancellationToken ct = default);

        #endregion

        #region 状态读取

        /// <summary>
        /// 读取设备状态
        /// </summary>
        Task<ushort> ReadStateAsync(CancellationToken ct = default);

        /// <summary>
        /// 刷新设备状态
        /// </summary>
        Task RefreshStatusAsync(CancellationToken ct = default);

        #endregion

        #region 复合操作

        /// <summary>
        /// 执行完整的进料流程（开光源->抖料->振动->关光源）
        /// </summary>
        Task<bool> ExecuteFeedCycleAsync(VibratorFeedCycleConfig config = null, CancellationToken ct = default);

        /// <summary>
        /// 执行排料流程（停止振动->打开排料门->延时->关闭排料门）
        /// </summary>
        Task<bool> ExecutePourCycleAsync(int pourDurationMs = 2000, CancellationToken ct = default);

        #endregion
    }

    /// <summary>
    /// 振动盘设备配置
    /// </summary>
    public class VibratorDeviceConfig
    {
        public string IPAddress { get; set; } = "192.168.1.100";
        public int Port { get; set; } = 502;
        public byte StationId { get; set; } = 1;

        // Modbus地址配置
        public ushort LightAAddress { get; set; } = 0x0000;
        public ushort LightBAddress { get; set; } = 0x0001;
        public ushort BoxFeedAddress { get; set; } = 0x0002;
        public ushort VibrationAddress { get; set; } = 0x0003;
        public ushort PourDoorAddress { get; set; } = 0x0004;
        public ushort StateAddress { get; set; } = 0x0000;

        // 时间配置
        public int FeedDuration { get; set; } = 3000;      // 默认抖料时间(ms)
        public int VibrationDuration { get; set; } = 5000;  // 默认振动时间(ms)
        public int StatusPollingInterval { get; set; } = 500; // 状态轮询间隔(ms)
    }

    /// <summary>
    /// 进料周期配置
    /// </summary>
    public class VibratorFeedCycleConfig
    {
        public bool EnableLightA { get; set; } = true;
        public bool EnableLightB { get; set; } = false;
        public int FeedDurationMs { get; set; } = 3000;
        public int VibrationDurationMs { get; set; } = 5000;
        public bool UseVibrationGroup2 { get; set; } = false;
        public int StabilizationDelayMs { get; set; } = 500;
    }
}