using Microsoft.Extensions.DependencyInjection;
using SeedCut.Framework.Config;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Services;
using SeedCut.Services.Camera;
using SeedCut.Services.Connection;
using SeedCut.Services.DeviceAdapter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SeedCut.Services.DeviceAdapter
{
    /// <summary>
    /// 设备初始化结果
    /// </summary>
    public class DeviceInitResult
    {
        public string DeviceId { get; set; }
        public string DisplayName { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public bool WasSkipped { get; set; }
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// 设备初始化服务
    /// 负责根据配置初始化所有设备，支持Debug/Release模式切换
    /// 
    /// 核心职责：
    /// 1. 创建 DeviceAdapter（带断线重连功能）
    /// 2. 注册到 DeviceManager
    /// 3. 向 SystemMonitor 注册模块检查
    /// 4. 在生产模式下执行设备连接
    /// </summary>
    public class DeviceInitializationService : IDisposable
    {
        #region 私有字段

        private readonly DeviceConnectionConfig _config;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogService _logService;
        private readonly ConnectionMode _effectiveMode;
        private ScannerServiceFactory _scannerFactory;
        // 缓存创建的 DeviceAdapter
        private readonly Dictionary<string, IDevice> _deviceAdapters;

        private bool _disposed;

        #endregion

        #region 属性

        /// <summary>
        /// 配置对象
        /// </summary>
        public DeviceConnectionConfig Config => _config;

        /// <summary>
        /// 有效的连接模式
        /// </summary>
        public ConnectionMode EffectiveMode => _effectiveMode;

        /// <summary>
        /// 是否为调试模式
        /// </summary>
        public bool IsDebugMode => _effectiveMode == ConnectionMode.Debug;

        /// <summary>
        /// 是否为生产模式
        /// </summary>
        public bool IsProductionMode => _effectiveMode == ConnectionMode.Production;

        /// <summary>
        /// 已创建的设备适配器
        /// </summary>
        public IReadOnlyDictionary<string, IDevice> DeviceAdapters => _deviceAdapters;

        #endregion

        #region 构造函数

        public DeviceInitializationService(
            DeviceConnectionConfig config,
            IServiceProvider serviceProvider,
            ILogService logService = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logService = logService;
            _deviceAdapters = new Dictionary<string, IDevice>(StringComparer.OrdinalIgnoreCase);

            // 计算有效模式
            _effectiveMode = _config.GetEffectiveMode();

            LogInfo("设备初始化服务已创建，有效模式: {0}", _effectiveMode);
        }

        #endregion

        #region ★★★ 统一创建和注册设备适配器（新增） ★★★

        /// <summary>
        /// 统一创建所有启用设备的 DeviceAdapter 并注册到 DeviceManager
        /// 
        /// 说明：
        /// - 无论调试模式还是生产模式，都会创建所有设备适配器
        /// - 创建适配器 ≠ 连接设备，适配器只是封装层，不占用硬件资源
        /// - 连接逻辑由 ConnectAllDevicesAsync 或用户手动触发
        /// </summary>
        /// <param name="deviceManager">设备管理器</param>
        /// <returns>创建结果列表</returns>
        public List<DeviceInitResult> CreateAndRegisterDeviceAdapters(IDeviceManager deviceManager)
        {
            if (deviceManager == null)
                throw new ArgumentNullException(nameof(deviceManager));

            var results = new List<DeviceInitResult>();

            LogInfo("========== 开始创建设备适配器 ==========");
            LogInfo("当前模式: {0}", _effectiveMode);

            foreach (var device in _config.Devices)
            {
                // 跳过禁用的设备
                if (!device.Enabled)
                {
                    LogDebug("跳过禁用的设备: {0}", device.DisplayName);
                    continue;
                }

                // 跳过非硬件设备（SDK检查、加密狗检查等）
                if (IsNonHardwareDevice(device.DeviceId))
                {
                    LogDebug("跳过非硬件设备: {0}", device.DisplayName);
                    continue;
                }

                // 创建设备适配器
                var result = CreateSingleDeviceAdapter(device);
                results.Add(result);

                if (result.Success)
                {
                    LogInfo("  ✓ {0}: 创建成功", device.DisplayName);
                }
                else
                {
                    LogInfo("  ✗ {0}: {1}", device.DisplayName, result.Message);
                }
            }

            // 注册到 DeviceManager
            LogInfo("--- 注册到 DeviceManager ---");
            foreach (var kvp in _deviceAdapters)
            {
                try
                {
                    deviceManager.Register(kvp.Value);
                    LogDebug("  已注册: {0}", kvp.Key);
                }
                catch (Exception ex)
                {
                    LogError(ex, "  注册失败: {0}", kvp.Key);
                }
            }

            var successCount = results.Count(r => r.Success);
            var failCount = results.Count(r => !r.Success);
            LogInfo("========== 设备适配器创建完成 ==========");
            LogInfo("总计: {0}, 成功: {1}, 失败: {2}", results.Count, successCount, failCount);

            return results;
        }

        /// <summary>
        /// 判断是否为非硬件设备（只需要软件检查，不需要创建Adapter）
        /// </summary>
        private bool IsNonHardwareDevice(string deviceId)
        {
            return deviceId == "VisionSDK" || deviceId == "VisionDongle";
        }

        /// <summary>
        /// 创建单个设备的 DeviceAdapter
        /// </summary>
        private DeviceInitResult CreateSingleDeviceAdapter(DeviceConnectionItem device)
        {
            var result = new DeviceInitResult
            {
                DeviceId = device.DeviceId,
                DisplayName = device.DisplayName
            };

            try
            {
                // 检查是否已存在
                if (_deviceAdapters.ContainsKey(device.DeviceId))
                {
                    result.Success = true;
                    result.Message = "适配器已存在";
                    result.WasSkipped = true;
                    return result;
                }

                // 根据设备ID创建对应的适配器
                IDevice adapter = CreateAdapterByDeviceId(device.DeviceId);

                if (adapter != null)
                {
                    result.Success = true;
                    result.Message = "创建成功";
                }
                else
                {
                    result.Success = false;
                    result.Message = "服务未注册或创建失败";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"异常: {ex.Message}";
                result.Exception = ex;
                LogError(ex, "创建设备适配器异常: {0}", device.DeviceId);
            }

            return result;
        }

        /// <summary>
        /// 根据设备ID创建对应的适配器
        /// </summary>
        private IDevice CreateAdapterByDeviceId(string deviceId)
        {
            // 相机设备特殊处理（多实例，以 Camera_ 开头）
            if (deviceId.StartsWith("Camera_"))
            {
                return CreateCameraAdapterById(deviceId);
            }

            // 扫码器设备特殊处理（以 Scanner_ 开头）
             if (deviceId.StartsWith("Scanner_"))
            {
                return CreateScannerAdapterById(deviceId);
            }

            // 其他设备
            switch (deviceId)
            {
                case "PLC":
                    return CreatePLCDeviceAdapter();
                case "Robot":
                    return CreateRobotDeviceAdapter();
                case "Vision":
                    return CreateVisionDeviceAdapter();
              
                case "HM_Laser":                          
                    return CreateHM_LaserDeviceAdapter();  
                case "Vibrator":
                    return CreateVibratorDeviceAdapter();
                default:
                    return null;
            }
        }

        private ScannerDeviceAdapter CreateScannerAdapterById(string scannerId)
        {
            if (_deviceAdapters.TryGetValue(scannerId, out var existing))
            {
                return existing as ScannerDeviceAdapter;
            }

            // 获取或创建工厂
            if (_scannerFactory == null)
            {
                _scannerFactory = new ScannerServiceFactory(_logService);
            }

            try
            {
                var scanner = _scannerFactory.GetOrCreate(scannerId);
                if (scanner != null)
                {
                    _deviceAdapters[scannerId] = scanner;
                    LogDebug("创建扫码器适配器成功: {0}", scannerId);
                }
                return scanner;
            }
            catch (Exception ex)
            {
                LogError(ex, "创建扫码器适配器失败: {0}", scannerId);
                return null;
            }
        }

        /// <summary>
        /// 根据相机ID创建相机适配器
        /// </summary>
        private HikCameraDeviceAdapter CreateCameraAdapterById(string cameraId)
        {
            if (_deviceAdapters.TryGetValue(cameraId, out var existing))
            {
                return existing as HikCameraDeviceAdapter;
            }

            var cameraFactory = GetService<HikCameraServiceFactory>();
            if (cameraFactory == null)
            {
                LogDebug("HikCameraServiceFactory 未注册，跳过: {0}", cameraId);
                return null;
            }

            try
            {
                var cameraInstance = cameraFactory.CreateInstance(cameraId);
                if (cameraInstance == null)
                {
                    LogDebug("无法创建相机实例: {0}", cameraId);
                    return null;
                }

                return CreateCameraDeviceAdapter(cameraId, cameraInstance);
            }
            catch (Exception ex)
            {
                LogError(ex, "创建相机适配器失败: {0}", cameraId);
                return null;
            }
        }

        #endregion

        #region 模块检查注册

        /// <summary>
        /// 向SystemMonitor注册所有模块检查
        /// 这是核心方法，根据配置和模式决定如何注册检查
        /// </summary>
        public void RegisterModuleChecks(ISystemMonitor systemMonitor)
        {
            if (systemMonitor == null)
                throw new ArgumentNullException(nameof(systemMonitor));

            LogInfo("========== 开始注册模块检查 ==========");
            LogInfo("有效模式: {0}", _effectiveMode);

            foreach (var device in _config.Devices)
            {
                if (!device.Enabled)
                {
                    LogDebug("跳过禁用的设备: {0}", device.DisplayName);
                    continue;
                }

                // 决定是否为关键检查
                bool isCritical = DetermineIsCritical(device);

                // 创建检查函数
                Func<ModuleCheckResult> checkFunc = CreateCheckFunction(device);

                // 注册到SystemMonitor
                systemMonitor.RegisterModule(device.DisplayName, checkFunc, isCritical);

                LogDebug("已注册模块: {0}, 关键={1}", device.DisplayName, isCritical);
            }

            LogInfo("========== 模块检查注册完成 ==========");
        }

        /// <summary>
        /// 决定设备是否作为关键检查
        /// </summary>
        private bool DetermineIsCritical(DeviceConnectionItem device)
        {
            switch (_effectiveMode)
            {
                case ConnectionMode.Production:
                    // 生产模式：使用配置的关键性
                    return device.IsCritical;

                case ConnectionMode.Debug:
                    // 调试模式：如果设备设置了SkipInDebug，则不作为关键
                    if (device.SkipInDebug)
                    {
                        return false;
                    }
                    return device.IsCritical;

                case ConnectionMode.Selective:
                    // 选择性模式：所有设备都不作为关键
                    return false;

                default:
                    return device.IsCritical;
            }
        }

        /// <summary>
        /// 创建设备检查函数
        /// </summary>
        private Func<ModuleCheckResult> CreateCheckFunction(DeviceConnectionItem device)
        {
            return () =>
            {
                // 调试模式下，如果设备设置了SkipInDebug，直接返回成功
                if (_effectiveMode == ConnectionMode.Debug && device.SkipInDebug)
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Success,
                        Message = "[调试模式] 跳过检查",
                        IsCritical = false
                    };
                }

                // 执行实际检查
                return ExecuteDeviceCheck(device);
            };
        }

        /// <summary>
        /// 执行设备检查（已修改：不再创建适配器，只做状态检查）
        /// </summary>
        private ModuleCheckResult ExecuteDeviceCheck(DeviceConnectionItem device)
        {
            try
            {
                switch (device.DeviceId)
                {
                    case "VisionSDK":
                        return CheckVisionSDK(device);

                    case "VisionDongle":
                        return CheckVisionDongle(device);

                    case "PLC":
                        return CheckPLC(device);

                    case "Robot":
                        return CheckRobot(device);

                    case "Vision":
                        return CheckVision(device);

                    case "Laser":
                        return CheckLaser(device);

                    case "Vibrator":
                        return CheckVibrator(device);

                    case "Camera_Disk":
                    case "Camera_Laser":
                    case "Camera_Small":
                    case "Camera_Large":
                        return CheckCamera(device);

                    default:
                        return new ModuleCheckResult
                        {
                            ModuleName = device.DisplayName,
                            Status = ModuleStatus.Warning,
                            Message = "未知设备类型",
                            IsCritical = device.IsCritical
                        };
                }
            }
            catch (Exception ex)
            {
                return new ModuleCheckResult
                {
                    ModuleName = device.DisplayName,
                    Status = ModuleStatus.Failed,
                    Message = $"检查异常: {ex.Message}",
                    IsCritical = device.IsCritical
                };
            }
        }

        #endregion

        #region SDK/加密狗检查（不需要连接）

        private ModuleCheckResult CheckVisionSDK(DeviceConnectionItem device)
        {
            var result = VisionMasterService.CheckVisionMasterSDK();
            result.IsCritical = device.IsCritical;
            return result;
        }

        private ModuleCheckResult CheckVisionDongle(DeviceConnectionItem device)
        {
            var result = VisionMasterService.CheckVisionMasterDongle();
            result.IsCritical = device.IsCritical;
            return result;
        }

        #endregion

        #region PLC检查（已修改：只检查状态，不创建适配器）

        private ModuleCheckResult CheckPLC(DeviceConnectionItem device)
        {
            try
            {
                var plcService = GetService<IPLCService>();
                if (plcService == null)
                {
                    return CreateFailedResult(device, "PLC服务未注册");
                }

                if (plcService.ConnectionConfig == null)
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Warning,
                        Message = "配置文件不存在，已创建默认配置",
                        IsCritical = device.IsCritical
                    };
                }

                // 检查适配器是否存在
                if (!_deviceAdapters.TryGetValue("PLC", out var adapter))
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Warning,
                        Message = $"适配器未创建 (IP: {plcService.ConnectionConfig.IPAddress})",
                        IsCritical = device.IsCritical
                    };
                }

                // 检查连接状态
                if (adapter.IsConnected)
                {
                    return CreateSuccessResult(device,
                        $"已连接 (IP: {plcService.ConnectionConfig.IPAddress})");
                }
                else
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Warning,
                        Message = $"未连接 (IP: {plcService.ConnectionConfig.IPAddress})",
                        IsCritical = device.IsCritical
                    };
                }
            }
            catch (Exception ex)
            {
                return CreateFailedResult(device, $"检查失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建PLC DeviceAdapter（带断线重连）
        /// </summary>
        private PLCDeviceAdapter CreatePLCDeviceAdapter()
        {
            if (_deviceAdapters.TryGetValue("PLC", out var existing))
            {
                return existing as PLCDeviceAdapter;
            }

            var plcService = GetService<IPLCService>();
            if (plcService == null) return null;

            // ✅ 修复：优先从 DI 获取单例
            var connectAdapter = GetService<PLCServiceConnectAdapter>();
            if (connectAdapter == null)
            {
                // 如果 DI 中没有注册，才创建新实例（后备方案）
                LogDebug("PLCServiceConnectAdapter 未在 DI 中注册，创建新实例");
                connectAdapter = new PLCServiceConnectAdapter(plcService, _logService);
            }

            // 创建 DeviceAdapter（内含 ConnectionManager，有断线重连）
            var deviceAdapter = new PLCDeviceAdapter(connectAdapter, plcService, _logService);

            _deviceAdapters["PLC"] = deviceAdapter;
            LogDebug("已创建 PLCDeviceAdapter");

            return deviceAdapter;
        }

        #endregion

        #region Robot检查（已修改：只检查状态，不创建适配器）

        private ModuleCheckResult CheckRobot(DeviceConnectionItem device)
        {
            try
            {
                var robotService = GetService<IRobotService>();
                if (robotService == null)
                {
                    return CreateFailedResult(device, "机器人服务未注册");
                }

                if (robotService.Config == null)
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Warning,
                        Message = "配置文件不存在，已创建默认配置",
                        IsCritical = device.IsCritical
                    };
                }

                // 检查适配器是否存在
                if (!_deviceAdapters.TryGetValue("Robot", out var adapter))
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Warning,
                        Message = $"适配器未创建 (IP: {robotService.Config.IPAddress}:{robotService.Config.Port})",
                        IsCritical = device.IsCritical
                    };
                }

                // 检查连接状态
                if (adapter.IsConnected)
                {
                    return CreateSuccessResult(device,
                        $"已连接 (IP: {robotService.Config.IPAddress}:{robotService.Config.Port})");
                }
                else
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Warning,
                        Message = $"未连接 (IP: {robotService.Config.IPAddress}:{robotService.Config.Port})",
                        IsCritical = device.IsCritical
                    };
                }
            }
            catch (Exception ex)
            {
                return CreateFailedResult(device, $"检查失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建Robot DeviceAdapter（带断线重连）
        /// </summary>
        private RobotDeviceAdapter CreateRobotDeviceAdapter()
        {
            if (_deviceAdapters.TryGetValue("Robot", out var existing))
            {
                return existing as RobotDeviceAdapter;
            }

            var robotService = GetService<IRobotService>();
            if (robotService == null) return null;

            // ✅ 修复：优先从 DI 获取单例
            var connectAdapter = GetService<RobotServiceConnectAdapter>();
            if (connectAdapter == null)
            {
                // 如果 DI 中没有注册，才创建新实例
                LogDebug("RobotServiceConnectAdapter 未在 DI 中注册，创建新实例");
                connectAdapter = new RobotServiceConnectAdapter(robotService, _logService);
            }

            // 创建 DeviceAdapter
            var deviceAdapter = new RobotDeviceAdapter(connectAdapter, robotService, _logService);

            _deviceAdapters["Robot"] = deviceAdapter;
            LogDebug("已创建 RobotDeviceAdapter");

            return deviceAdapter;
        }

        #endregion

        #region Vision检查（已修改：只检查状态，不创建适配器）

        private ModuleCheckResult CheckVision(DeviceConnectionItem device)
        {
            try
            {
                var visionService = GetService<IVisionService>();
                if (visionService == null)
                {
                    return CreateFailedResult(device, "视觉服务未注册");
                }

                // 检查适配器是否存在
                if (!_deviceAdapters.TryGetValue("Vision", out var adapter))
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Warning,
                        Message = "适配器未创建",
                        IsCritical = device.IsCritical
                    };
                }

                return CreateSuccessResult(device, "视觉服务已就绪");
            }
            catch (Exception ex)
            {
                return CreateFailedResult(device, $"检查失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建Vision DeviceAdapter
        /// </summary>
        private VisionDeviceAdapter CreateVisionDeviceAdapter()
        {
            if (_deviceAdapters.TryGetValue("Vision", out var existing))
            {
                return existing as VisionDeviceAdapter;
            }

            var visionService = GetService<IVisionService>();
            if (visionService == null) return null;

            var deviceAdapter = new VisionDeviceAdapter("Vision", visionService);
            _deviceAdapters["Vision"] = deviceAdapter;
            LogDebug("已创建 VisionDeviceAdapter");

            return deviceAdapter;
        }

        #endregion

        #region Laser检查（已修改：只检查状态，不创建适配器）

        private ModuleCheckResult CheckLaser(DeviceConnectionItem device)
        {
            try
            {
                var laserService = GetService<ILaserService>();
                if (laserService == null)
                {
                    return CreateFailedResult(device, "激光服务未注册");
                }

                // 检查适配器是否存在
                if (!_deviceAdapters.TryGetValue("Laser", out var adapter))
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Warning,
                        Message = "适配器未创建",
                        IsCritical = device.IsCritical
                    };
                }

                // 检查连接状态
                if (adapter.IsConnected)
                {
                    return CreateSuccessResult(device, "激光器已连接");
                }
                else
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Warning,
                        Message = "激光器未连接",
                        IsCritical = device.IsCritical
                    };
                }
            }
            catch (Exception ex)
            {
                return CreateFailedResult(device, $"检查失败: {ex.Message}");
            }
        }

    

        #endregion

        /// <summary>
        /// 创建HM_Laser DeviceAdapter（GMC控制卡）
        /// </summary>
        private HM_LaserDeviceAdapter CreateHM_LaserDeviceAdapter()
        {
            // 检查是否已存在
            if (_deviceAdapters.TryGetValue("HM_Laser", out var existing))
            {
                return existing as HM_LaserDeviceAdapter;
            }

            // 1. 获取服务层
            var hmService = GetService<HM_LaserService>();
            if (hmService == null)
            {
                LogDebug("HM_LaserService 未注册，跳过 HM_Laser");
                return null;
            }

            // ★★★ 新增：在UI线程预先初始化DLL和消息窗口 ★★★
            // 此时必定在UI线程（应用启动阶段），可安全创建MessageWindow
            if (!hmService.IsInitialized)
            {
                LogDebug("预初始化 HM_LaserService...");
                if (!hmService.Initialize())
                {
                    LogDebug("HM_LaserService 初始化失败");
                    // 不返回null，仍然创建Adapter，让后续连接时报错
                }
            }


            // 2. 获取或创建连接适配器（用于断线重连）
            var connectAdapter = GetService<HM_LaserServiceConnectAdapter>();
            if (connectAdapter == null)
            {
                LogDebug("HM_LaserServiceConnectAdapter 未在 DI 中注册，创建新实例");
                connectAdapter = new HM_LaserServiceConnectAdapter(hmService);
            }

            // 3. 注册到连接监控服务（启用断线重连）
            var connectionMonitor = GetService<ConnectionMonitorService>();
            if (connectionMonitor != null)
            {
                connectionMonitor.RegisterDevice(connectAdapter);
                LogDebug("HM_Laser 已注册到连接监控服务（断线重连已启用）");
            }
            else
            {
                LogDebug("ConnectionMonitorService 未注册，HM_Laser 断线重连功能不可用");
            }

            // 4. 创建设备适配器（实现 ILaserDevice 接口）
            var deviceAdapter = new HM_LaserDeviceAdapter(hmService, _logService);

            _deviceAdapters["HM_Laser"] = deviceAdapter;
            LogDebug("已创建 HM_LaserDeviceAdapter");

            return deviceAdapter;
        }

        #region Vibrator检查（已修改：只检查状态，不创建适配器）

        private ModuleCheckResult CheckVibrator(DeviceConnectionItem device)
        {
            try
            {
                var vibratorService = GetService<IVibratorService>();
                if (vibratorService == null)
                {
                    return CreateFailedResult(device, "振动盘服务未注册");
                }

                if (vibratorService.Config == null)
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Warning,
                        Message = "配置文件不存在，已创建默认配置",
                        IsCritical = device.IsCritical
                    };
                }

                // 检查适配器是否存在
                if (!_deviceAdapters.TryGetValue("Vibrator", out var adapter))
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Warning,
                        Message = $"适配器未创建 (IP: {vibratorService.Config.IPAddress})",
                        IsCritical = device.IsCritical
                    };
                }

                // 检查连接状态
                if (adapter.IsConnected)
                {
                    return CreateSuccessResult(device,
                        $"已连接 (IP: {vibratorService.Config.IPAddress})");
                }
                else
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Warning,
                        Message = $"未连接 (IP: {vibratorService.Config.IPAddress})",
                        IsCritical = device.IsCritical
                    };
                }
            }
            catch (Exception ex)
            {
                return CreateFailedResult(device, $"检查失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建Vibrator DeviceAdapter（带断线重连）
        /// </summary>
        private VibratorDeviceAdapter CreateVibratorDeviceAdapter()
        {
            if (_deviceAdapters.TryGetValue("Vibrator", out var existing))
            {
                return existing as VibratorDeviceAdapter;
            }

            var vibratorService = GetService<IVibratorService>();
            if (vibratorService == null) return null;

            // ✅ 修复：优先从 DI 获取单例
            var connectAdapter = GetService<VibratorServiceConnectAdapter>();
            if (connectAdapter == null)
            {
                // 如果 DI 中没有注册，才创建新实例
                LogDebug("VibratorServiceConnectAdapter 未在 DI 中注册，创建新实例");
                connectAdapter = new VibratorServiceConnectAdapter(vibratorService, _logService);
            }

            // 创建 DeviceAdapter
            var deviceAdapter = new VibratorDeviceAdapter(connectAdapter, vibratorService, _logService);

            _deviceAdapters["Vibrator"] = deviceAdapter;
            LogDebug("已创建 VibratorDeviceAdapter");

            return deviceAdapter;
        }

        #endregion

        #region Camera检查（已修改：只检查状态，不创建适配器）

        private ModuleCheckResult CheckCamera(DeviceConnectionItem device)
        {
            try
            {
                var cameraFactory = GetService<HikCameraServiceFactory>();
                if (cameraFactory == null)
                {
                    return CreateFailedResult(device, "相机工厂服务未注册");
                }

                // 检查适配器是否存在
                if (!_deviceAdapters.TryGetValue(device.DeviceId, out var adapter))
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Warning,
                        Message = "适配器未创建",
                        IsCritical = device.IsCritical
                    };
                }

                // 检查连接状态
                if (adapter.IsConnected)
                {
                    return CreateSuccessResult(device, "相机已连接");
                }
                else
                {
                    return new ModuleCheckResult
                    {
                        ModuleName = device.DisplayName,
                        Status = ModuleStatus.Warning,
                        Message = "相机未连接",
                        IsCritical = device.IsCritical
                    };
                }
            }
            catch (Exception ex)
            {
                return CreateFailedResult(device, $"检查失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 创建Camera DeviceAdapter（带断线重连）
        /// </summary>
        private HikCameraDeviceAdapter CreateCameraDeviceAdapter(string cameraId, HikCameraInstance instance)
        {
            if (_deviceAdapters.TryGetValue(cameraId, out var existing))
            {
                return existing as HikCameraDeviceAdapter;
            }

            // 创建 ConnectAdapter
            var connectAdapter = new HikCameraConnectAdapter(instance);

            // 创建 DeviceAdapter
            var deviceAdapter = new HikCameraDeviceAdapter(connectAdapter, instance, _logService);

            _deviceAdapters[cameraId] = deviceAdapter;
            LogDebug("已创建 HikCameraDeviceAdapter: {0}", cameraId);

            return deviceAdapter;
        }

        #endregion

        #region DeviceManager注册（保留，但建议使用 CreateAndRegisterDeviceAdapters）

        /// <summary>
        /// 将所有已创建的DeviceAdapter注册到DeviceManager
        /// 注意：建议使用 CreateAndRegisterDeviceAdapters，它会自动注册
        /// </summary>
        public void RegisterToDeviceManager(IDeviceManager deviceManager)
        {
            if (deviceManager == null)
                throw new ArgumentNullException(nameof(deviceManager));

            foreach (var kvp in _deviceAdapters)
            {
                deviceManager.Register(kvp.Value);
                LogDebug("已注册设备到DeviceManager: {0}", kvp.Key);
            }

            LogInfo("共注册 {0} 个设备到DeviceManager", _deviceAdapters.Count);
        }

        /// <summary>
        /// 获取指定的DeviceAdapter
        /// </summary>
        public T GetDeviceAdapter<T>(string deviceId) where T : class, IDevice
        {
            if (_deviceAdapters.TryGetValue(deviceId, out var device))
            {
                return device as T;
            }
            return null;
        }

        #endregion

        #region 辅助方法

        private T GetService<T>() where T : class
        {
            return _serviceProvider.GetService(typeof(T)) as T;
        }

        private ModuleCheckResult CreateSuccessResult(DeviceConnectionItem device, string message)
        {
            return new ModuleCheckResult
            {
                ModuleName = device.DisplayName,
                Status = ModuleStatus.Success,
                Message = message,
                IsCritical = device.IsCritical
            };
        }

        private ModuleCheckResult CreateFailedResult(DeviceConnectionItem device, string message)
        {
            return new ModuleCheckResult
            {
                ModuleName = device.DisplayName,
                Status = ModuleStatus.Failed,
                Message = message,
                IsCritical = device.IsCritical
            };
        }

        #endregion

        #region 连接所有设备（异步）

        /// <summary>
        /// 通过DeviceAdapter连接所有启用的设备（异步）
        /// </summary>
        public async Task<List<DeviceInitResult>> ConnectAllDevicesAsync(
            IProgress<(string deviceName, int progress)> progress = null,
            CancellationToken ct = default)
        {
            var results = new List<DeviceInitResult>();
            var devicesToConnect = _config.GetAutoConnectDevices();

            int total = devicesToConnect.Count;
            int current = 0;

            foreach (var device in devicesToConnect)
            {
                if (ct.IsCancellationRequested)
                    break;

                current++;
                progress?.Report((device.DisplayName, current * 100 / total));

                var result = await ConnectDeviceAsync(device, ct);
                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// 连接单个设备（异步）
        /// </summary>
        private async Task<DeviceInitResult> ConnectDeviceAsync(
            DeviceConnectionItem device,
            CancellationToken ct)
        {
            var result = new DeviceInitResult
            {
                DeviceId = device.DeviceId,
                DisplayName = device.DisplayName
            };

            // 调试模式下跳过
            if (_effectiveMode == ConnectionMode.Debug && device.SkipInDebug)
            {
                result.Success = true;
                result.WasSkipped = true;
                result.Message = "[调试模式] 跳过连接";
                return result;
            }

            try
            {
                // 获取已创建的 DeviceAdapter
                if (_deviceAdapters.TryGetValue(device.DeviceId, out var deviceAdapter))
                {
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        cts.CancelAfter(device.ConnectionTimeoutMs);

                        // 通过 DeviceAdapter 连接（有断线重连功能）
                        bool connected = await deviceAdapter.ConnectAsync(cts.Token);

                        result.Success = connected;
                        result.Message = connected ? "连接成功" : "连接失败";
                    }
                }
                else
                {
                    result.Success = false;
                    result.Message = "设备适配器未创建";
                }
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Message = "连接超时";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"连接失败: {ex.Message}";
                result.Exception = ex;
            }

            return result;
        }

        /// <summary>
        /// 连接指定设备（供调试模式手动调用）
        /// </summary>
        public async Task<DeviceInitResult> ConnectDeviceAsync(string deviceId, CancellationToken ct = default)
        {
            var device = _config.Devices.FirstOrDefault(d => d.DeviceId == deviceId);
            if (device == null)
            {
                return new DeviceInitResult
                {
                    DeviceId = deviceId,
                    Success = false,
                    Message = "设备配置不存在"
                };
            }

            return await ConnectDeviceAsync(device, ct);
        }

        /// <summary>
        /// 断开指定设备（供调试模式手动调用）
        /// </summary>
        public async Task<bool> DisconnectDeviceAsync(string deviceId)
        {
            if (_deviceAdapters.TryGetValue(deviceId, out var adapter))
            {
                await adapter.DisconnectAsync();
                return true;
            }
            return false;
        }

        #endregion

        #region 日志

        private void LogDebug(string message, params object[] args)
        {
            var formatted = string.Format(message, args);
            _logService?.Debug(formatted);
            System.Diagnostics.Debug.WriteLine($"[DeviceInit] {formatted}");
        }

        private void LogInfo(string message, params object[] args)
        {
            var formatted = string.Format(message, args);
            _logService?.Information(formatted);
            System.Diagnostics.Debug.WriteLine($"[DeviceInit] {formatted}");
        }

        private void LogError(Exception ex, string message, params object[] args)
        {
            var formatted = string.Format(message, args);
            _logService?.Error(ex, formatted);
            System.Diagnostics.Debug.WriteLine($"[DeviceInit] ERROR: {formatted} - {ex?.Message}");
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            // 释放所有 DeviceAdapter
            foreach (var kvp in _deviceAdapters)
            {
                try
                {
                    kvp.Value?.Dispose();
                    LogDebug("已释放设备: {0}", kvp.Key);
                }
                catch (Exception ex)
                {
                    LogError(ex, "释放设备失败: {0}", kvp.Key);
                }
            }

            _deviceAdapters.Clear();
            _disposed = true;

            LogInfo("设备初始化服务已释放");
        }

        #endregion
    }

    /// <summary>
    /// DI扩展方法
    /// </summary>
    public static class DeviceInitializationServiceExtensions
    {
        /// <summary>
        /// 注册设备初始化服务
        /// </summary>
        public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddDeviceInitializationService(
            this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
        {
            // 注册配置（单例）
            services.AddSingleton(sp => DeviceConnectionConfig.Load());

            // 注册初始化服务（单例）
            services.AddSingleton<DeviceInitializationService>(sp =>
            {
                var config = (DeviceConnectionConfig)sp.GetService(typeof(DeviceConnectionConfig));
                var logService = sp.GetService(typeof(ILogService)) as ILogService;
                return new DeviceInitializationService(config, sp, logService);
            });

            return services;
        }
    }
}