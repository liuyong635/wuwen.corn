using SeedCut.Models;
using System;
using System.Threading.Tasks;

namespace SeedCut.Services
{
    public interface IVibratorService : IDisposable
    {
        // 配置
        VibratorConfig Config { get; }

        // 连接状态
        bool IsConnected { get; }

        // 设备状态
        bool IsLightAOn { get; }
        bool IsLightBOn { get; }
        bool IsFeeding { get; }
        bool IsVibrating { get; }
        bool IsPourDoorOpen { get; }

        // 事件
        event EventHandler<bool> ConnectionChanged;
        event EventHandler<string> StatusChanged;
        event EventHandler<string> ErrorOccurred;

        // 连接控制
        Task<bool> ConnectAsync();
        Task DisconnectAsync();

        // ✅ 改动3：添加明确设置状态的方法（匹配C++逻辑）
        Task<bool> SetLightAAsync(bool turnOn);
        Task<bool> SetLightBAsync(bool turnOn);
        Task<bool> SetPourDoorAsync(bool open);

        // 设备控制
        Task<bool> ToggleLightAAsync();
        Task<bool> ToggleLightBAsync();
        Task<bool> StartFeedAsync();
        Task<bool> StopFeedAsync();
        Task<bool> StartVibrationAsync();
        Task<bool> StopVibrationAsync();
        Task<bool> TogglePourDoorAsync();

        // ✅ 改动4：添加振动组合2（匹配C++的runGroup2）
        Task<bool> StartVibrationGroup2Async();

        // ✅ 改动5：添加读取光源状态的方法（匹配C++的getLightA/B）
        Task<bool> ReadLightAAsync();
        Task<bool> ReadLightBAsync();

        // 状态读取
        Task<ushort> ReadStateAsync();
        Task RefreshStatusAsync();
    }
}