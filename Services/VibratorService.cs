using Modbus.Device;
using SeedCut.Framework.Models;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Models;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Services
{
    /// <summary>
    /// 振动盘服务 - 使用请求队列版本
    /// 
    /// 【重要改动】
    /// 所有 Modbus 操作通过 ModbusRequestQueue 串行执行，
    /// 彻底解决状态轮询与控制操作的竞争问题。
    /// 
    /// 【改动点】
    /// 1. 新增 _requestQueue 字段
    /// 2. 所有 _modbusMaster.XXXAsync() 改为 _requestQueue.EnqueueXXXAsync()
    /// 3. ConnectAsync 中初始化队列
    /// 4. DisconnectAsync/Dispose 中释放队列
    /// </summary>
    public class VibratorService : IVibratorService
    {
        #region 私有字段

        private readonly VibratorConfig _config;
        private readonly ILogService _logService;
        private readonly IAlarmService _alarmService;

        private IModbusMaster _modbusMaster;
        private TcpClient _tcpClient;
        private Timer _statusTimer;

        // ★ 新增：Modbus 请求队列
        private ModbusRequestQueue _requestQueue;

        // 报警来源标识（用于报警恢复）
        private const string ALARM_SOURCE_CONNECTION = "Vibrator_Connection";
        private const string ALARM_SOURCE_COMMUNICATION = "Vibrator_Communication";
        private const string ALARM_SOURCE_LIGHT_A = "Vibrator_LightA";
        private const string ALARM_SOURCE_LIGHT_B = "Vibrator_LightB";
        private const string ALARM_SOURCE_FEED = "Vibrator_Feed";
        private const string ALARM_SOURCE_VIBRATION = "Vibrator_Vibration";
        private const string ALARM_SOURCE_POUR_DOOR = "Vibrator_PourDoor";

        // 连续通信失败计数器（用于状态轮询）
        private int _statusPollFailCount = 0;
        private const int MAX_STATUS_POLL_FAIL_COUNT = 5;

        #endregion

        #region 属性

        public VibratorConfig Config => _config;
        public bool IsConnected { get; private set; }

        // 设备状态
        public bool IsLightAOn { get; private set; }
        public bool IsLightBOn { get; private set; }
        public bool IsFeeding { get; private set; }
        public bool IsVibrating { get; private set; }
        public bool IsPourDoorOpen { get; private set; }

        #endregion

        #region 事件

        public event EventHandler<bool> ConnectionChanged;
        public event EventHandler<string> StatusChanged;
        public event EventHandler<string> ErrorOccurred;

        #endregion

        #region 构造函数

        /// <summary>
        /// 推荐构造函数 - 带日志和报警服务
        /// </summary>
        public VibratorService(ILogService logService, IAlarmService alarmService = null)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _alarmService = alarmService;
            _config = VibratorConfig.Load();

            _logService.Information("[振动盘] 服务已初始化 (IP: {IP}, Port: {Port})",
                _config.IPAddress, _config.Port);
        }

        /// <summary>
        /// 默认构造函数（兼容旧代码，无日志服务）
        /// </summary>
        public VibratorService()
        {
            _logService = null;
            _alarmService = null;
            _config = VibratorConfig.Load();
        }

        #endregion

        #region 连接管理

        public async Task<bool> ConnectAsync()
        {
            try
            {
                if (IsConnected)
                {
                    OnStatusChanged("设备已连接");
                    return true;
                }

                _logService?.Information("[振动盘] 开始连接 {IP}:{Port}...",
                    _config.IPAddress, _config.Port);

                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(_config.IPAddress, _config.Port);

                _modbusMaster = ModbusIpMaster.CreateIp(_tcpClient);

                // ★ 新增：创建请求队列并设置 ModbusMaster
                _requestQueue = new ModbusRequestQueue(_logService);
                _requestQueue.SetModbusMaster(_modbusMaster, _config.StationId);

                IsConnected = true;
                _statusPollFailCount = 0;

                // 恢复连接相关报警
                _alarmService?.RecoverAlarmBySource(ALARM_SOURCE_CONNECTION);

                ConnectionChanged?.Invoke(this, true);
                OnStatusChanged("振动盘连接成功");

                _logService?.Information("[振动盘] 连接成功");

                // ★ 改动：延迟1秒再启动状态轮询，避免与初始化操作冲突
                _statusTimer = new Timer(async _ => await RefreshStatusAsync(),
                    null, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500));

                return true;
            }
            catch (Exception ex)
            {
                IsConnected = false;

                var errorMsg = $"连接失败: {ex.Message}";
                _logService?.Error(ex, "[振动盘] {Error}", errorMsg);

                // 触发连接报警
                _alarmService?.TriggerAlarm(
                    name: "振动盘连接失败",
                    level: AlarmLevel.Error,
                    description: errorMsg,
                    sourceModule: "振动盘",
                    sourceIdentifier: ALARM_SOURCE_CONNECTION,
                    showPopup: true);

                OnError(errorMsg);
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                _logService?.Information("[振动盘] 正在断开连接...");

                _statusTimer?.Dispose();
                _statusTimer = null;

                // ★ 新增：释放请求队列
                _requestQueue?.Dispose();
                _requestQueue = null;

                if (_tcpClient?.Connected == true)
                {
                    _tcpClient.Close();
                }

                _modbusMaster = null;
                _tcpClient = null;

                IsConnected = false;
                _statusPollFailCount = 0;

                ConnectionChanged?.Invoke(this, false);
                OnStatusChanged("振动盘已断开连接");

                _logService?.Information("[振动盘] 已断开连接");

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                var errorMsg = $"断开连接失败: {ex.Message}";
                _logService?.Error(ex, "[振动盘] {Error}", errorMsg);
                OnError(errorMsg);
            }
        }

        #endregion

        #region 设备控制 - 光源

        public async Task<bool> SetLightAAsync(bool turnOn)
        {
            if (!IsConnected || _requestQueue == null)
            {
                _logService?.Warning("[振动盘] 设备未连接，无法控制光源A");
                return false;
            }

            try
            {
                ushort value = (ushort)(turnOn ? 0xFF00 : 0x0000);

                // ★ 改用队列执行
                var success = await _requestQueue.EnqueueWriteAsync(
                    _config.LightAAddress,
                    value,
                    $"光源A {(turnOn ? "开启" : "关闭")}");

                if (success)
                {
                    IsLightAOn = turnOn;
                    _logService?.Debug("[振动盘] 光源A已{State}", turnOn ? "打开" : "关闭");
                    OnStatusChanged($"光源A已{(turnOn ? "打开" : "关闭")}");
                    _alarmService?.RecoverAlarmBySource(ALARM_SOURCE_LIGHT_A);
                }
                else
                {
                    _logService?.Warning("[振动盘] 光源A控制失败");
                }

                return success;
            }
            catch (Exception ex)
            {
                var errorMsg = $"光源A控制失败: {ex.Message}";
                _logService?.Error(ex, "[振动盘] {Error}", errorMsg);

                _alarmService?.TriggerAlarm(
                    name: "光源A控制失败",
                    level: AlarmLevel.Warning,
                    description: errorMsg,
                    sourceModule: "振动盘",
                    sourceIdentifier: ALARM_SOURCE_LIGHT_A,
                    showPopup: false);

                OnError(errorMsg);
                return false;
            }
        }

        public async Task<bool> SetLightBAsync(bool turnOn)
        {
            if (!IsConnected || _requestQueue == null)
            {
                _logService?.Warning("[振动盘] 设备未连接，无法控制光源B");
                return false;
            }

            try
            {
                ushort value = (ushort)(turnOn ? 0xFF00 : 0x0000);

                // ★ 改用队列执行
                var success = await _requestQueue.EnqueueWriteAsync(
                    _config.LightBAddress,
                    value,
                    $"光源B {(turnOn ? "开启" : "关闭")}");

                if (success)
                {
                    IsLightBOn = turnOn;
                    _logService?.Debug("[振动盘] 光源B已{State}", turnOn ? "打开" : "关闭");
                    OnStatusChanged($"光源B已{(turnOn ? "打开" : "关闭")}");
                    _alarmService?.RecoverAlarmBySource(ALARM_SOURCE_LIGHT_B);
                }

                return success;
            }
            catch (Exception ex)
            {
                var errorMsg = $"光源B控制失败: {ex.Message}";
                _logService?.Error(ex, "[振动盘] {Error}", errorMsg);

                _alarmService?.TriggerAlarm(
                    name: "光源B控制失败",
                    level: AlarmLevel.Warning,
                    description: errorMsg,
                    sourceModule: "振动盘",
                    sourceIdentifier: ALARM_SOURCE_LIGHT_B,
                    showPopup: false);

                OnError(errorMsg);
                return false;
            }
        }

        public async Task<bool> ToggleLightAAsync()
        {
            return await SetLightAAsync(!IsLightAOn);
        }

        public async Task<bool> ToggleLightBAsync()
        {
            return await SetLightBAsync(!IsLightBOn);
        }

        public async Task<bool> ReadLightAAsync()
        {
            if (!IsConnected || _requestQueue == null) return false;

            try
            {
                // ★ 改用队列执行
                var result = await _requestQueue.EnqueueReadHoldingAsync(
                    _config.LightAAddress,
                    1,
                    "读取光源A状态");

                if (result != null && result.Length > 0)
                {
                    IsLightAOn = result[0] == 0xFF00;
                    _logService?.Verbose("[振动盘] 读取光源A状态: {State}", IsLightAOn ? "开" : "关");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logService?.Warning("[振动盘] 读取光源A状态失败: {Error}", ex.Message);
                OnError($"读取光源A失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ReadLightBAsync()
        {
            if (!IsConnected || _requestQueue == null) return false;

            try
            {
                // ★ 改用队列执行
                var result = await _requestQueue.EnqueueReadHoldingAsync(
                    _config.LightBAddress,
                    1,
                    "读取光源B状态");

                if (result != null && result.Length > 0)
                {
                    IsLightBOn = result[0] == 0xFF00;
                    _logService?.Verbose("[振动盘] 读取光源B状态: {State}", IsLightBOn ? "开" : "关");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logService?.Warning("[振动盘] 读取光源B状态失败: {Error}", ex.Message);
                OnError($"读取光源B失败: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region 设备控制 - 抖料/进料

        public async Task<bool> StartFeedAsync()
        {
            if (!IsConnected || _requestQueue == null)
            {
                _logService?.Warning("[振动盘] 设备未连接，无法执行抖料");
                return false;
            }

            const int maxRetries = 3;
            int retryCount = 0;

            _logService?.Information("[振动盘] 开始执行抖料流程");

            while (retryCount < maxRetries)
            {
                try
                {
                    // ★ 改用队列执行：开始抖料
                    var startSuccess = await _requestQueue.EnqueueWriteAsync(
                        _config.BoxFeedAddress,
                        0xFF00,
                        "开始抖料");

                    if (!startSuccess)
                    {
                        retryCount++;
                        continue;
                    }

                    IsFeeding = true;
                    _logService?.Debug("[振动盘] 抖料开始，等待 {Duration}ms", _config.FeedDuration);

                    await Task.Delay(_config.FeedDuration);

                    // 停止抖料（带重试）
                    if (await StopFeedWithRetryAsync())
                    {
                        _logService?.Information("[振动盘] 抖料流程完成");
                        _alarmService?.RecoverAlarmBySource(ALARM_SOURCE_FEED);
                        OnStatusChanged("抖料完成");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logService?.Warning("[振动盘] 抖料失败（尝试 {Retry}/{MaxRetry}）: {Error}",
                        retryCount + 1, maxRetries, ex.Message);
                    OnError($"抖料失败（尝试 {retryCount + 1}/{maxRetries}）: {ex.Message}");
                }

                retryCount++;
                if (retryCount < maxRetries)
                {
                    await Task.Delay(100);
                }
            }

            var errorMsg = "抖料失败，已达到最大重试次数";
            _logService?.Error("[振动盘] {Error}", errorMsg);

            _alarmService?.TriggerAlarm(
                name: "振动盘抖料失败",
                level: AlarmLevel.Error,
                description: errorMsg,
                sourceModule: "振动盘",
                sourceIdentifier: ALARM_SOURCE_FEED,
                showPopup: true);

            OnError(errorMsg);
            return false;
        }

        private async Task<bool> StopFeedWithRetryAsync()
        {
            const int maxRetries = 5;
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                try
                {
                    // ★ 改用队列执行
                    var success = await _requestQueue.EnqueueWriteAsync(
                        _config.BoxFeedAddress,
                        0x0000,
                        "停止抖料");

                    if (success)
                    {
                        IsFeeding = false;
                        _logService?.Debug("[振动盘] 抖料已停止");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logService?.Warning("[振动盘] 停止抖料失败（尝试 {Retry}/{MaxRetry}）: {Error}",
                        retryCount + 1, maxRetries, ex.Message);
                }

                retryCount++;
                if (retryCount < maxRetries)
                {
                    await Task.Delay(300);
                }
            }

            _logService?.Error("[振动盘] 停止抖料失败，已达最大重试次数");
            return false;
        }

        public async Task<bool> StopFeedAsync()
        {
            if (!IsConnected || _requestQueue == null)
            {
                _logService?.Warning("[振动盘] 设备未连接，无法停止抖料");
                return false;
            }

            return await StopFeedWithRetryAsync();
        }

        #endregion

        #region 设备控制 - 振动

        public async Task<bool> StartVibrationAsync()
        {
            if (!IsConnected || _requestQueue == null)
            {
                _logService?.Warning("[振动盘] 设备未连接，无法启动振动");
                return false;
            }

            try
            {
                // ★ 改用队列执行：振动组合1 - 0x001E
                var success = await _requestQueue.EnqueueWriteAsync(
                    _config.VibrationAddress,
                    0x001E,
                    "启动振动(组合1)");

                if (success)
                {
                    IsVibrating = true;
                    _logService?.Information("[振动盘] 振动盘启动（组合1）");
                    OnStatusChanged("振动盘启动（组合1）");
                    _alarmService?.RecoverAlarmBySource(ALARM_SOURCE_VIBRATION);
                }

                return success;
            }
            catch (Exception ex)
            {
                var errorMsg = $"启动振动失败: {ex.Message}";
                _logService?.Error(ex, "[振动盘] {Error}", errorMsg);

                _alarmService?.TriggerAlarm(
                    name: "振动盘启动失败",
                    level: AlarmLevel.Error,
                    description: errorMsg,
                    sourceModule: "振动盘",
                    sourceIdentifier: ALARM_SOURCE_VIBRATION,
                    showPopup: true);

                OnError(errorMsg);
                return false;
            }
        }

        public async Task<bool> StartVibrationGroup2Async()
        {
            if (!IsConnected || _requestQueue == null)
            {
                _logService?.Warning("[振动盘] 设备未连接，无法启动振动组合2");
                return false;
            }

            try
            {
                // ★ 改用队列执行：振动组合2 - 0x001D
                var success = await _requestQueue.EnqueueWriteAsync(
                    _config.VibrationAddress,
                    0x001D,
                    "启动振动(组合2)");

                if (success)
                {
                    IsVibrating = true;
                    _logService?.Information("[振动盘] 振动盘启动（组合2）");
                    OnStatusChanged("振动盘启动（组合2）");
                    _alarmService?.RecoverAlarmBySource(ALARM_SOURCE_VIBRATION);
                }

                return success;
            }
            catch (Exception ex)
            {
                var errorMsg = $"启动振动组合2失败: {ex.Message}";
                _logService?.Error(ex, "[振动盘] {Error}", errorMsg);

                _alarmService?.TriggerAlarm(
                    name: "振动盘启动失败（组合2）",
                    level: AlarmLevel.Error,
                    description: errorMsg,
                    sourceModule: "振动盘",
                    sourceIdentifier: ALARM_SOURCE_VIBRATION,
                    showPopup: true);

                OnError(errorMsg);
                return false;
            }
        }

        public async Task<bool> StopVibrationAsync()
        {
            if (!IsConnected || _requestQueue == null)
            {
                _logService?.Warning("[振动盘] 设备未连接，无法停止振动");
                return false;
            }

            try
            {
                // ★ 改用队列执行
                var success = await _requestQueue.EnqueueWriteAsync(
                    _config.VibrationAddress,
                    0x0000,
                    "停止振动");

                if (success)
                {
                    IsVibrating = false;
                    _logService?.Information("[振动盘] 振动盘停止");
                    OnStatusChanged("振动盘停止");
                }

                return success;
            }
            catch (Exception ex)
            {
                var errorMsg = $"停止振动失败: {ex.Message}";
                _logService?.Error(ex, "[振动盘] {Error}", errorMsg);
                OnError(errorMsg);
                return false;
            }
        }

        #endregion

        #region 设备控制 - 排料门

        public async Task<bool> SetPourDoorAsync(bool open)
        {
            if (!IsConnected || _requestQueue == null)
            {
                _logService?.Warning("[振动盘] 设备未连接，无法控制排料门");
                return false;
            }

            try
            {
                ushort value = (ushort)(open ? 0xFF00 : 0x0000);

                // ★ 改用队列执行
                var success = await _requestQueue.EnqueueWriteAsync(
                    _config.PourDoorAddress,
                    value,
                    $"排料门 {(open ? "打开" : "关闭")}");

                if (success)
                {
                    IsPourDoorOpen = open;
                    _logService?.Debug("[振动盘] 排料口已{State}", open ? "打开" : "关闭");
                    OnStatusChanged($"排料口已{(open ? "打开" : "关闭")}");
                    _alarmService?.RecoverAlarmBySource(ALARM_SOURCE_POUR_DOOR);
                }

                return success;
            }
            catch (Exception ex)
            {
                var errorMsg = $"排料口控制失败: {ex.Message}";
                _logService?.Error(ex, "[振动盘] {Error}", errorMsg);

                _alarmService?.TriggerAlarm(
                    name: "排料口控制失败",
                    level: AlarmLevel.Warning,
                    description: errorMsg,
                    sourceModule: "振动盘",
                    sourceIdentifier: ALARM_SOURCE_POUR_DOOR,
                    showPopup: false);

                OnError(errorMsg);
                return false;
            }
        }

        public async Task<bool> TogglePourDoorAsync()
        {
            return await SetPourDoorAsync(!IsPourDoorOpen);
        }

        #endregion

        #region 状态读取

        public async Task<ushort> ReadStateAsync()
        {
            if (!IsConnected || _requestQueue == null) return 0;

            try
            {
                // ★ 改用队列执行
                var result = await _requestQueue.EnqueueReadHoldingAsync(
                    _config.StateAddress,
                    1,
                    "读取设备状态");

                if (result != null && result.Length > 0)
                {
                    // 读取成功，重置失败计数
                    _statusPollFailCount = 0;
                    _alarmService?.RecoverAlarmBySource(ALARM_SOURCE_COMMUNICATION);
                    return result[0];
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logService?.Warning("[振动盘] 读取状态失败: {Error}", ex.Message);
                OnError($"读取状态失败: {ex.Message}");
                return 0;
            }
        }

        public async Task RefreshStatusAsync()
        {
            if (!IsConnected || _requestQueue == null) return;

            try
            {
                var state = await ReadStateAsync();

                // 解析状态位
                // bit 0: 振动盘运动状态 (bDiskRunState)
                IsVibrating = (state & 0x01) != 0;

                // bit 1: 抖料仓运动状态 (bBoxRunState)
                IsFeeding = (state & 0x02) != 0;

                // 读取成功，重置失败计数
                _statusPollFailCount = 0;
            }
            catch (Exception ex)
            {
                _statusPollFailCount++;

                _logService?.Debug("[振动盘] 状态轮询失败（{FailCount}/{MaxFail}）: {Error}",
                    _statusPollFailCount, MAX_STATUS_POLL_FAIL_COUNT, ex.Message);

                // 连续失败超过阈值时触发报警
                if (_statusPollFailCount >= MAX_STATUS_POLL_FAIL_COUNT)
                {
                    _logService?.Warning("[振动盘] 状态轮询连续失败，可能存在通信问题");

                    _alarmService?.TriggerAlarm(
                        name: "振动盘通信异常",
                        level: AlarmLevel.Warning,
                        description: $"状态轮询连续失败 {_statusPollFailCount} 次",
                        sourceModule: "振动盘",
                        sourceIdentifier: ALARM_SOURCE_COMMUNICATION,
                        showPopup: false);

                    // 重置计数，避免持续报警
                    _statusPollFailCount = 0;
                }
            }
        }

        #endregion

        #region 事件触发

        private void OnStatusChanged(string message)
        {
            StatusChanged?.Invoke(this, message);
            _logService?.Verbose("[振动盘] 状态: {Message}", message);
        }

        private void OnError(string message)
        {
            ErrorOccurred?.Invoke(this, message);
            _logService?.Warning("[振动盘] 错误: {Message}", message);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _logService?.Information("[振动盘] 正在释放资源...");

            _statusTimer?.Dispose();

            // ★ 新增：释放请求队列
            _requestQueue?.Dispose();
            _requestQueue = null;

            _tcpClient?.Close();
            _modbusMaster = null;

            _logService?.Information("[振动盘] 服务已释放");
        }

        #endregion
    }
}