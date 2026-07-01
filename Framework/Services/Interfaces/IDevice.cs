using SeedCut.Framework.Core;
using SeedCut.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Framework.Services.Interfaces
{
    #region 基础设备接口

    /// <summary>
    /// 设备基础接口
    /// </summary>
    public interface IDevice : IDisposable
    {
        string DeviceId { get; }
        string DeviceName { get; }
        DeviceType DeviceType { get; }
        DeviceConnectionState ConnectionState { get; }
        bool IsConnected { get; }
        string LastError { get; }

        event EventHandler<DeviceConnectionChangedEventArgs> ConnectionChanged;
        event EventHandler<DeviceErrorEventArgs> ErrorOccurred;

        Task<bool> ConnectAsync(CancellationToken ct = default);
        Task DisconnectAsync();
        Task<bool> ResetAsync();
        Task<bool> CheckHealthAsync(CancellationToken ct = default);
    }

    #endregion

    #region PLC接口

    /// <summary>
    /// PLC设备接口 - 扩展版
    /// 支持地址字符串解析和西门子S7协议特性
    /// </summary>
    public interface IPlcDevice : IDevice
    {
        #region 按地址字符串操作（推荐方式）

        // 位操作
        bool ReadBit(string address);
        void WriteBit(string address, bool value);
        void WriteBitPulse(string address, int durationMs = 50);
        Task<bool> ReadBitAsync(string address, CancellationToken ct = default);
        Task WriteBitAsync(string address, bool value, CancellationToken ct = default);

        // 整数操作
        short ReadInt16(string address);
        void WriteInt16(string address, short value);
        int ReadInt32(string address);
        void WriteInt32(string address, int value);

        // 浮点数操作
        float ReadFloat(string address);
        void WriteFloat(string address, float value);

        // 字符串操作
        string ReadString(string address, int length);
        void WriteString(string address, string value, int maxLength);

        // 批量操作
        bool[] ReadBits(string startAddress, int count);
        short[] ReadInt16Array(string startAddress, int count);
        void WriteInt16Array(string startAddress, short[] values);

        #endregion

        #region 按DB/地址/位操作（兼容现有PLCService）

        // 位操作
        bool ReadBit(int dataBlock, int startAddress, int bitPosition);
        bool WriteBit(int dataBlock, int startAddress, int bitPosition, bool value);
        bool PulseBit(int dataBlock, int startAddress, int bitPosition, int delayMs = 20);
        bool ToggleBit(int dataBlock, int startAddress, int bitPosition);

        // 数值操作
        float ReadFloat(int dataBlock, int startAddress);
        bool WriteFloat(int dataBlock, int startAddress, float value);
        short ReadInt16(int dataBlock, int startAddress);
        bool WriteInt16(int dataBlock, int startAddress, short value);

        // 字节操作
        byte[] ReadBytes(int dataBlock, int startAddress, int length);
        bool WriteBytes(int dataBlock, int startAddress, byte[] data);

        #endregion

        #region 按名称操作（通过AddressRegistry）

        bool ReadBitByName(string addressName);
        bool WriteBitByName(string addressName, bool value);
        bool PulseBitByName(string addressName, int delayMs = 20);
        bool ToggleBitByName(string addressName);
        float ReadFloatByName(string addressName);
        bool WriteFloatByName(string addressName, float value);
        short ReadInt16ByName(string addressName);
        bool WriteInt16ByName(string addressName, short value);

        #endregion
    }

    #endregion

    #region 机器人接口

    /// <summary>
    /// 机器人设备接口 - 扩展版
    /// 支持TCP通信模式
    /// </summary>
    public interface IRobotDevice : IDevice
    {
        RobotState RobotState { get; }
        RobotPosition CurrentPosition { get; }
        string CurrentProgram { get; }
        bool IsServoOn { get; }
        bool IsProgramRunning { get; }
        bool HasAlarm { get; }

        event EventHandler<RobotStateChangedEventArgs> RobotStateChanged;
        /// <summary>
        /// TCP通信模式 - 收到机器人命令事件
        /// </summary>
        event EventHandler<string> CommandReceived;

        Task<bool> EnableServoAsync(bool enable, CancellationToken ct = default);
        Task<bool> ClearAlarmAsync(CancellationToken ct = default);

        Task<bool> RunProgramAsync(string programName, CancellationToken ct = default);
        Task<bool> RunProgramAndWaitAsync(string programName, TimeSpan timeout, CancellationToken ct = default);
        Task<bool> PauseAsync(CancellationToken ct = default);
        Task<bool> ResumeAsync(CancellationToken ct = default);
        Task<bool> StopAsync(CancellationToken ct = default);
        Task<bool> HomeAsync(CancellationToken ct = default);

        bool ReadInput(int index);
        bool ReadOutput(int index);
        void WriteOutput(int index, bool value);

        int ReadIntVariable(string name);
        void WriteIntVariable(string name, int value);

        #region TCP通信扩展

        /// <summary>
        /// AR程序客户端是否已连接
        /// </summary>
        bool IsArClientConnected { get; }

        /// <summary>
        /// TCP服务器是否正在运行
        /// </summary>
        bool IsTcpServerRunning { get; }

        /// <summary>
        /// 启动TCP服务器（作为Server等待机器人连接）
        /// </summary>
        Task<bool> StartTcpServerAsync(int port, CancellationToken ct = default);

        /// <summary>
        /// 停止TCP服务器
        /// </summary>
        void StopTcpServer();

        /// <summary>
        /// 发送命令给机器人
        /// </summary>
        Task SendCommandAsync(string command, CancellationToken ct = default);

        /// <summary>
        /// 发送坐标数据给机器人AR程序
        /// </summary>
        Task<bool> SendCoordinatesAsync(List<VisionCoordinate> coordinates, CancellationToken ct = default);

        /// <summary>
        /// 停止AR程序并清理所有相关资源（统一清理入口）
        /// 
        /// 执行顺序：
        /// 1. 停止AR程序（Modbus写命令）
        /// 2. 断开伺服使能（安全考虑）
        /// 3. 停止TCP服务器
        /// 4. 清理所有机器人相关Flag条件
        /// 
        /// 注意：不断开Modbus连接（由DeviceManager管理）
        /// </summary>
        /// <param name="ct">取消令牌</param>
        /// <returns>是否成功完成所有清理步骤</returns>
        Task<bool> StopAndCleanupAsync(CancellationToken ct = default);

        /// <summary>
        /// 清理所有机器人相关的Flag条件
        /// </summary>
        void ClearAllRobotFlags();

        #endregion
    }

    /// <summary>
    /// 机器人位置
    /// </summary>
    public class RobotPosition
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float A { get; set; }
        public float B { get; set; }
        public float C { get; set; }

        public RobotPosition() { }

        public RobotPosition(float x, float y, float z, float a = 0, float b = 0, float c = 0)
        {
            X = x; Y = y; Z = z; A = a; B = b; C = c;
        }

        public override string ToString() => $"X:{X:F2}, Y:{Y:F2}, Z:{Z:F2}, A:{A:F2}, B:{B:F2}, C:{C:F2}";
    }

    #endregion

    #region 视觉接口

    /// <summary>
    /// 视觉设备接口 - 扩展版
    /// 支持多流程、方案管理
    /// </summary>
    public interface IVisionDevice : IDevice
    {
        bool IsBusy { get; }
        bool IsSolutionLoaded { get; }
        string CurrentSolutionPath { get; }

        /// <summary>
        /// 加载视觉方案
        /// </summary>
        Task<bool> LoadSolutionAsync(string solutionPath, string password = "", CancellationToken ct = default);

        /// <summary>
        /// 保存视觉方案
        /// </summary>
        Task<bool> SaveSolutionAsync(CancellationToken ct = default);

        /// <summary>
        /// 关闭视觉方案
        /// </summary>
        void CloseSolution();

        /// <summary>
        /// 执行视觉流程
        /// </summary>
        Task<VisionDeviceResult> ExecuteAsync(string procedureName, CancellationToken ct = default);

        /// <summary>
        /// 执行视觉流程（带参数）
        /// </summary>
        Task<VisionDeviceResult> ExecuteAsync(string procedureName, object parameters, CancellationToken ct = default);

        /// <summary>
        /// 触发拍照
        /// </summary>
        Task<byte[]> TriggerCaptureAsync(CancellationToken ct = default);

        /// <summary>
        /// 获取最后一张图像
        /// </summary>
        byte[] GetLastImage();

        /// <summary>
        /// 获取流程图像
        /// </summary>
        byte[] GetProcedureImage(string procedureName);

        /// <summary>
        /// 获取所有流程名称
        /// </summary>
        IEnumerable<string> GetProcedureNames();

        /// <summary>
        /// 获取最后执行的流程对象（供Handler提取业务数据）
        /// </summary>
        dynamic GetLastProcedure();

        #region 连续执行（硬触发监听模式）

        /// <summary>
        /// 启动流程连续执行
        /// 
        /// 用于硬触发模式：流程启动后等待外部触发信号（如相机IO触发），
        /// 每次收到触发信号自动执行一次流程。
        /// 
        /// 典型场景：飞拍偏差检测流程
        /// - 机器人DO18触发相机
        /// - VM流程自动执行偏差计算
        /// - 结果通过TCP发送给机器人
        /// </summary>
        /// <param name="procedureName">流程名称</param>
        /// <param name="intervalMs">连续执行间隔（毫秒），0表示使用默认值</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>是否成功启动</returns>
        Task<bool> StartContinuousRunAsync(string procedureName, uint intervalMs = 0, CancellationToken ct = default);

        /// <summary>
        /// 停止流程连续执行
        /// </summary>
        /// <param name="procedureName">流程名称</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>是否成功停止</returns>
        Task<bool> StopContinuousRunAsync(string procedureName, CancellationToken ct = default);

        /// <summary>
        /// 检查流程是否正在连续执行中
        /// </summary>
        /// <param name="procedureName">流程名称</param>
        /// <returns>是否正在连续执行</returns>
        bool IsContinuousRunning(string procedureName);

        /// <summary>
        /// 停止所有流程的连续执行
        /// </summary>
        /// <param name="ct">取消令牌</param>
        /// <returns>是否成功停止</returns>
        Task<bool> StopAllContinuousRunAsync(CancellationToken ct = default);

        #endregion
    }

    /// <summary>
    /// 视觉结果
    /// </summary>
    public class VisionDeviceResult
    {
        public bool Success { get; set; }
        public int ResultCode { get; set; }
        public string Message { get; set; }
        public Dictionary<string, object> Results { get; set; } = new Dictionary<string, object>();
        public byte[] ImageData { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public Exception Exception { get; set; }

        public double GetDouble(string key, double defaultValue = 0)
        {
            if (Results.TryGetValue(key, out var value))
            {
                if (value is double d) return d;
                if (double.TryParse(value?.ToString(), out var parsed)) return parsed;
            }
            return defaultValue;
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            if (Results.TryGetValue(key, out var value))
            {
                if (value is int i) return i;
                if (int.TryParse(value?.ToString(), out var parsed)) return parsed;
            }
            return defaultValue;
        }

        public string GetString(string key, string defaultValue = null)
            => Results.TryGetValue(key, out var value) ? value?.ToString() ?? defaultValue : defaultValue;

        public bool GetBool(string key, bool defaultValue = false)
        {
            if (Results.TryGetValue(key, out var value))
            {
                if (value is bool b) return b;
                if (bool.TryParse(value?.ToString(), out var parsed)) return parsed;
            }
            return defaultValue;
        }

        public static VisionDeviceResult Ok(Dictionary<string, object> results = null)
            => new VisionDeviceResult { Success = true, ResultCode = 0, Message = "OK", Results = results ?? new Dictionary<string, object>() };

        public static VisionDeviceResult Fail(int code, string message)
            => new VisionDeviceResult { Success = false, ResultCode = code, Message = message };

        public static VisionDeviceResult FromSuccess(string message, int elapsedMs = 0)
            => new VisionDeviceResult { Success = true, ResultCode = 0, Message = message, ExecutionTime = TimeSpan.FromMilliseconds(elapsedMs) };

        public static VisionDeviceResult FromError(string message, Exception ex = null)
            => new VisionDeviceResult { Success = false, ResultCode = -1, Message = message, Exception = ex };
    }

    #endregion

    #region 扫码器接口

    /// <summary>
    /// 扫码器设备接口（扩展版）
    /// 
    /// 修改内容：
    /// - 新增 ScanWithRetryAsync() 方法（无参数版本，使用默认配置）
    /// - 新增 ScanWithRetryAsync(int, TimeSpan) 方法（自定义重试参数版本）
    /// </summary>
    public interface IScannerDevice : IDevice
    {
        #region 原有属性

        /// <summary>
        /// 是否正在扫码
        /// </summary>
        bool IsScanning { get; }

        /// <summary>
        /// 最后一次扫码结果
        /// </summary>
        ScanResult LastResult { get; }

        #endregion

        #region 原有方法

        /// <summary>
        /// 执行扫码（单次，使用默认超时）
        /// </summary>
        Task<ScanResult> ScanAsync(CancellationToken ct = default);

        /// <summary>
        /// 执行扫码（单次，带超时）
        /// </summary>
        Task<ScanResult> ScanAsync(TimeSpan timeout, CancellationToken ct = default);

        #endregion

        #region 新增方法 - ScanWithRetryAsync

        /// <summary>
        /// 执行扫码（带重试，使用设备默认配置）
        /// 
        /// 说明：
        /// - 使用设备配置中的 NoReadRetryCount 和 NoReadRetryIntervalMs
        /// - 当扫码返回 NoRead 时自动重试
        /// - 匹配 C++ mainuser.cpp 的重试行为
        /// </summary>
        /// <param name="ct">取消令牌</param>
        /// <returns>扫码结果</returns>
        Task<ScanResult> ScanWithRetryAsync(CancellationToken ct = default);

        /// <summary>
        /// 执行扫码（带重试，自定义参数）
        /// </summary>
        /// <param name="maxRetries">最大重试次数</param>
        /// <param name="retryInterval">重试间隔</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>扫码结果</returns>
        Task<ScanResult> ScanWithRetryAsync(int maxRetries, TimeSpan retryInterval, CancellationToken ct = default);

        #endregion

        #region 可选：连续扫码方法（如果需要的话）

        // /// <summary>
        // /// 开始连续扫码
        // /// </summary>
        // void StartContinuousScan();

        // /// <summary>
        // /// 停止连续扫码
        // /// </summary>
        // void StopContinuousScan();

        #endregion
    }

    /// <summary>
    /// 扫码结果
    /// </summary>
    public class ScanResult
    {
        public bool Success { get; set; }
        public string Code { get; set; }
        public string CodeType { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public static ScanResult Ok(string code, string type = null)
            => new ScanResult { Success = true, Code = code, CodeType = type };

        public static ScanResult Fail(string error)
            => new ScanResult { Success = false, ErrorMessage = error };
    }

    #endregion

    #region 设备管理器接口

    /// <summary>
    /// 设备管理器接口
    /// </summary>
    public interface IDeviceManager : IDisposable
    {
        IReadOnlyDictionary<string, IDevice> Devices { get; }
        bool AreAllConnected { get; }

        event EventHandler<DeviceConnectionSummary> ConnectionStateChanged;

        void Register(IDevice device);
        void Register(params IDevice[] devices);
        void Unregister(string deviceId);

        T Get<T>(string deviceId) where T : class, IDevice;
        bool TryGet<T>(string deviceId, out T device) where T : class, IDevice;
        IEnumerable<T> GetAll<T>() where T : class, IDevice;

        bool IsConnected(string deviceId);
        Task<bool> ConnectAllAsync(CancellationToken ct = default);
        Task<bool> ConnectAsync(string deviceId, CancellationToken ct = default);
        Task DisconnectAllAsync();

        DeviceConnectionSummary GetConnectionSummary();
    }

    /// <summary>
    /// 设备连接状态摘要
    /// </summary>
    public class DeviceConnectionSummary
    {
        public int TotalCount { get; set; }
        public int ConnectedCount { get; set; }
        public int ErrorCount { get; set; }
        public Dictionary<string, DeviceConnectionState> DeviceStates { get; } = new Dictionary<string, DeviceConnectionState>();
        public bool AllConnected => ConnectedCount == TotalCount && TotalCount > 0;
    }

    #endregion
}