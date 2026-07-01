using Microsoft.Extensions.DependencyInjection;
using SeedCut.Framework.Config;
using SeedCut.Framework.Core;
using SeedCut.Framework.Core.SlotSeedTracker;
using SeedCut.Framework.Services.Alarm;
using SeedCut.Framework.Services.Devices;
using SeedCut.Framework.Services.Execution;
using SeedCut.Framework.Services.Handlers;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Framework.Services.Logging;
using SeedCut.Framework.Services.Monitor;
using SeedCut.Framework.Services.Queues;
using SeedCut.Framework.Services.Recipe;
using SeedCut.Framework.Services.Signals;
using SeedCut.Framework.Services.Traceability;
using SeedCut.Models;
using SeedCut.Models.PLCModule;
using SeedCut.Services;
using SeedCut.Services.Camera;
using SeedCut.Services.Connection;
using SeedCut.Services.DeviceAdapter;
using SeedCut.ViewModels;
using SeedCut.ViewModels.PLCModule;
using SeedCut.ViewModels.Recipe;
using SeedCut.Views;
using SeedCut.Views.PLCModule;
using SeedCut.Views.Recipe;
using System;
using System.Windows;

namespace SeedCut
{
    public partial class App : Application
    {
        private IServiceProvider _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {


            base.OnStartup(e);

            // 配置依赖注入
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            _serviceProvider = serviceCollection.BuildServiceProvider();

            // ========== 获取配置和初始化服务 ==========
            var deviceConfig = _serviceProvider.GetRequiredService<DeviceConnectionConfig>();
            var deviceInitService = _serviceProvider.GetRequiredService<DeviceInitializationService>();
            var deviceManager = _serviceProvider.GetRequiredService<IDeviceManager>();  // 新增

            // 输出当前模式
            LogStartupMode(deviceConfig, deviceInitService);

            // ========== 初始化SDK和基础服务 ==========
            InitializeCameraSDK();
            InitializeConnectionMonitor();
            InitializeAlarmService();

            // ========================================
            // ★★★ 添加这段代码 - 显式初始化信号监控和Handler执行器 ★★★
            // ========================================
            InitializeSignalMonitorAndHandlerExecutor();

            // ========== 创建并注册所有 DeviceAdapter（先创建！） ==========
            var createResults = deviceInitService.CreateAndRegisterDeviceAdapters(deviceManager);

            // ========== 注册模块检查（后检查！） ==========
            RegisterModuleChecks(deviceInitService);

            

            // ========== 根据模式决定是否显示Loading页面 ==========
            if (deviceConfig.IsDebugMode && deviceConfig.DebugSettings.SkipLoadingPage)
            {
                // 调试模式且配置跳过Loading页面，直接显示登录/主窗口
                System.Diagnostics.Debug.WriteLine("⚠️ [调试模式] 跳过Loading页面");
                ShowLoginWindow();
            }
            else
            {
                // 显示加载窗口
                var loadingWindow = _serviceProvider.GetRequiredService<LoadingWindow>();
                loadingWindow.Show();
            }
        }

        /// <summary>
        /// ★★★ 新增方法：显式初始化信号监控和Handler执行器 ★★★
        /// 
        /// 解决问题：DI 容器的延迟实例化导致 PlcSignalMonitor 构造函数从未被调用
        /// 原因：AddSingleton 只有在服务被请求时才会创建实例
        /// </summary>
        private void InitializeSignalMonitorAndHandlerExecutor()
        {
            try
            {
                var logService = _serviceProvider.GetService<ILogService>();
                logService?.Information("========== 初始化信号监控和Handler执行器 ==========");

                // ★★★ 关键修复：先实例化 PLCModuleFactory，触发 CSV 加载 ★★★
                var moduleFactory = _serviceProvider.GetRequiredService<PLCModuleFactory>();
                var plcService = _serviceProvider.GetRequiredService<IPLCService>();
                logService?.Information("✓ PLCModuleFactory 已初始化，已注册地址: {0} 个",
                    plcService.AddressRegistry.Addresses.Count);

                // ★ 关键：显式获取 PlcSignalMonitor，触发其构造函数
                // 这会调用 PlcSignalMonitor 构造函数，进而调用 AutoLoadAliasConfig()
                var signalMonitor = _serviceProvider.GetRequiredService<PlcSignalMonitor>();
                logService?.Information("✓ PlcSignalMonitor 已初始化，别名数量: {0}", signalMonitor.AliasCount);

                // ★ 显式获取 HandlerExecutor，确保 Handler 被注册
                var handlerExecutor = _serviceProvider.GetRequiredService<HandlerExecutor>();
                logService?.Information("✓ HandlerExecutor 已初始化");

                // 可选：输出已加载的别名数量用于调试
                System.Diagnostics.Debug.WriteLine($"[App] PlcSignalMonitor 已初始化:");
                System.Diagnostics.Debug.WriteLine($"  - 已注册地址: {plcService.AddressRegistry.Addresses.Count} 个");
                System.Diagnostics.Debug.WriteLine($"  - 别名数量: {signalMonitor.AliasCount}");
                System.Diagnostics.Debug.WriteLine($"  - PLC连接状态: {(signalMonitor.IsPlcConnected ? "已连接" : "未连接")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 初始化信号监控失败: {ex.Message}");

                var logService = _serviceProvider.GetService<ILogService>();
                logService?.Error(ex, "初始化信号监控和Handler执行器失败");
            }
        }

        /// <summary>
        /// 输出启动模式日志
        /// </summary>
        private void LogStartupMode(DeviceConnectionConfig config, DeviceInitializationService initService)
        {
            var separator = new string('=', 60);

            System.Diagnostics.Debug.WriteLine(separator);
            System.Diagnostics.Debug.WriteLine("           SeedCut 设备连接模式");
            System.Diagnostics.Debug.WriteLine(separator);

#if DEBUG
            System.Diagnostics.Debug.WriteLine("编译模式: DEBUG");
#else
            System.Diagnostics.Debug.WriteLine("编译模式: RELEASE");
#endif

            System.Diagnostics.Debug.WriteLine($"配置模式: {config.ConnectionMode}");
            System.Diagnostics.Debug.WriteLine($"有效模式: {initService.EffectiveMode}");

            if (initService.IsDebugMode)
            {
                System.Diagnostics.Debug.WriteLine("");
                System.Diagnostics.Debug.WriteLine("⚠️  当前为调试模式，部分设备检查将被跳过");
                System.Diagnostics.Debug.WriteLine("⚠️  如需测试完整连接，请修改配置文件或使用Release编译");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("");
                System.Diagnostics.Debug.WriteLine("✓  当前为生产模式，所有关键设备必须连接成功");
            }

            System.Diagnostics.Debug.WriteLine(separator);

            // 输出设备配置摘要
            System.Diagnostics.Debug.WriteLine("设备配置摘要:");
            foreach (var device in config.GetEnabledDevices())
            {
                var skipFlag = config.ShouldSkipDevice(device.DeviceId, initService.EffectiveMode) ? "[跳过]" : "";
                var criticalFlag = device.IsCritical ? "[关键]" : "";
                System.Diagnostics.Debug.WriteLine($"  - {device.DisplayName} {criticalFlag} {skipFlag}");
            }
            System.Diagnostics.Debug.WriteLine(separator);
        }

        /// <summary>
        /// 显示登录窗口（跳过Loading时使用）
        /// </summary>
        private void ShowLoginWindow()
        {
            var loginWindow = _serviceProvider.GetRequiredService<LoginWindow>();
            loginWindow.Show();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<PLCAddressRegistry>();
            services.AddSingleton<PLCModuleFactory>();

            // ================================================================
            //                    配置和初始化服务（新增）
            // ================================================================

            // 注册设备连接配置（单例）
            services.AddSingleton(sp => DeviceConnectionConfig.Load());

            // ================================================================
            //                    基础服务
            // ================================================================

            services.AddSingleton<ISystemMonitor, SystemMonitor>();
            services.AddSingleton<ILogService, SerilogService>();
            services.AddSingleton<IAlarmService, AlarmService>();

            services.AddSingleton<ITraceService>(sp =>
            {
                var logService = sp.GetService<ILogService>();
                return new TraceService(logService);
            });

            // ★ 注册种子追踪器（单例）
            services.AddSingleton<ISlotSeedTracker, SlotSeedTracker>();


            // ================================================================
            //                    设备服务
            // ================================================================

            services.AddSingleton<IVisionService, VisionMasterService>();
            // 注册振动盘服务（新版本）
            services.AddSingleton<IVibratorService>(sp =>
            {
                var logService = sp.GetRequiredService<ILogService>();
                var alarmService = sp.GetService<IAlarmService>(); // 可选
                return new VibratorService(logService, alarmService);
            });
            services.AddSingleton<IRobotService, RobotService>();
            services.AddSingleton<IPLCService, PLCService>();
            services.AddSingleton<ILaserService, LaserService>();
            // HM 激光器服务（单例）
            services.AddSingleton<HM_LaserService>(sp =>
            {
                var logService = sp.GetService<ILogService>();
                return new HM_LaserService(logService);
            });
            // ================================================================
            //                    多相机架构注册
            // ================================================================

            // 相机配置（单例）
            services.AddSingleton<HikCameraConfig>(sp => HikCameraConfig.Load());

            // 相机工厂（单例）
            services.AddSingleton<HikCameraServiceFactory>();

            // 默认相机（振动盘相机）
            services.AddSingleton<ICameraService>(sp =>
            {
                var factory = sp.GetRequiredService<HikCameraServiceFactory>();
                return factory.CreateInstance("Camera_Disk");
            });

            // ================================================================
            //                    设备初始化服务（新增）
            // ================================================================

            services.AddSingleton<DeviceInitializationService>(sp =>
            {
                var config = sp.GetRequiredService<DeviceConnectionConfig>();
                var logService = sp.GetService<ILogService>();
                return new DeviceInitializationService(config, sp, logService);
            });
            services.AddSingleton<IDeviceManager, DeviceManager>();

            // ================================================================
            //                    连接监控服务
            // ================================================================

            services.AddSingleton<ConnectionMonitorService>();

            // PLC 连接适配器 - 注册具体类型
            services.AddSingleton<PLCServiceConnectAdapter>(sp =>
            {
                var plcService = sp.GetRequiredService<IPLCService>();
                var logService = sp.GetService<ILogService>();
                return new PLCServiceConnectAdapter(plcService, logService);
            });

            // 激光器连接适配器 - 注册具体类型
            services.AddSingleton<LaserServiceConnectAdapter>(sp =>
            {
                var laserService = sp.GetRequiredService<ILaserService>();
                return new LaserServiceConnectAdapter(laserService);
            });

            // 机器人连接适配器
            services.AddSingleton<RobotServiceConnectAdapter>(sp =>
            {
                var robotService = sp.GetRequiredService<IRobotService>();
                var logService = sp.GetService<ILogService>();
                return new RobotServiceConnectAdapter(robotService, logService);
            });

            // 振动盘连接适配器
            services.AddSingleton<VibratorServiceConnectAdapter>(sp =>
            {
                var vibratorService = sp.GetRequiredService<IVibratorService>();
                var logService = sp.GetService<ILogService>();
                return new VibratorServiceConnectAdapter(vibratorService, logService);
            });

            // HM 激光器连接适配器（单例，用于断线重连）
            services.AddSingleton<HM_LaserServiceConnectAdapter>(sp =>
            {
                var hmService = sp.GetRequiredService<HM_LaserService>();
                return new HM_LaserServiceConnectAdapter(hmService);
            });

            services.AddSingleton<PLCAlarmMonitor>(sp =>
            {
                var plcService = sp.GetRequiredService<IPLCService>();
                var alarmService = sp.GetRequiredService<IAlarmService>();
                var logService = sp.GetService<ILogService>();
                var addressRegistry = plcService.AddressRegistry;
                return new PLCAlarmMonitor(plcService, alarmService, addressRegistry, logService);
            });

            // ================================================================
            //                    配方服务（新增）
            // ================================================================

            // 注册 RecipeService（单例）
            services.AddSingleton<IRecipeService>(sp =>
            {
                var logService = sp.GetService<ILogService>();
                return new RecipeService(logService);
            });

            

            


            // ================================================================
            //                    视图模型
            // ================================================================

            services.AddTransient<LoginViewModel>();
            services.AddTransient<MainViewModel>(sp =>
            {
                var deviceManager = sp.GetRequiredService<IDeviceManager>();
                var deviceInitService = sp.GetRequiredService<DeviceInitializationService>();
                var alarmViewModel = sp.GetRequiredService<AlarmViewModel>();
                return new MainViewModel(sp, deviceManager, deviceInitService, alarmViewModel);
            });

            services.AddTransient<AdminViewModel>();
            services.AddTransient<VisionViewModel>();
            services.AddTransient<CameraViewModel>();
            services.AddTransient<VibratorViewModel>();
            services.AddTransient<RobotViewModel>();
            services.AddTransient<MainPLCViewModel>();
            services.AddSingleton<AlarmViewModel>();
            services.AddTransient<LaserDebugViewModel>();
            services.AddSingleton<HandlerDebugViewModel>();
            // HM 激光器调试 ViewModel
            services.AddTransient<HM_LaserDebugViewModel>(sp =>
            {
                var hmService = sp.GetRequiredService<HM_LaserService>();
                return new HM_LaserDebugViewModel(hmService);
            });
            // ================================================================
            //                    视图模型（带初始化服务注入）
            // ================================================================

            // LoadingViewModel 注入初始化服务
            services.AddTransient<LoadingViewModel>(sp =>
            {
                var systemMonitor = sp.GetRequiredService<ISystemMonitor>();
                var deviceConfig = sp.GetRequiredService<DeviceConnectionConfig>();
                return new LoadingViewModel(systemMonitor);
            });

            services.AddTransient<RecipeEditorViewModel>(sp =>
            {
                var recipeService = sp.GetRequiredService<IRecipeService>();
                return new RecipeEditorViewModel(recipeService);
            });
            // ================================================================
            //                    Handler调试模块（新增）
            // ================================================================

            // 生产上下文
            services.AddSingleton<IProductionContext, ProductionContext>();

            // 数据流管理器（如果还没注册）
            services.AddSingleton<IDataFlowManager, DataFlowManager>();

            // 工位管理器（如果还没注册）
            services.AddSingleton<ITaskExecutionManager, TaskExecutionManager>();

            // 注册 PlcSignalMonitor（单例）
            // ★ PlcSignalMonitor 同时实现 ISignalMonitor 和 ISignalAccessor
            services.AddSingleton<PlcSignalMonitor>(sp =>
            {
                var plcService = sp.GetRequiredService<IPLCService>();
                var addressRegistry = plcService.AddressRegistry;
                var logService = sp.GetService<ILogService>();

                var monitor = new PlcSignalMonitor(plcService, addressRegistry, logService);

                // ★ 加载信号别名配置
                try
                {
                    var aliasConfigPath = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Config",
                        "signal_aliases.json");

                    if (System.IO.File.Exists(aliasConfigPath))
                    {
                        monitor.LoadAliasConfig(aliasConfigPath);
                        logService?.Information("[App] 已加载信号别名配置: {0}", aliasConfigPath);
                    }
                    else
                    {
                        logService?.Warning("[App] 信号别名配置文件不存在: {0}", aliasConfigPath);
                    }
                }
                catch (Exception ex)
                {
                    logService?.Error(ex, "[App] 加载信号别名配置失败");
                }

                return monitor;
            });

            // 注册 ISignalMonitor 接口（指向同一个实例）
            services.AddSingleton<ISignalMonitor>(sp => sp.GetRequiredService<PlcSignalMonitor>());

            // ★ 新增：注册 ISignalAccessor 接口（指向同一个实例）
            services.AddSingleton<ISignalAccessor>(sp => sp.GetRequiredService<PlcSignalMonitor>());



            // Handler注册中心
            services.AddSingleton<IHandlerRegistry>(sp =>
            {
                var logService = sp.GetService<ILogService>();
                var registry = new HandlerRegistry(logService);
                registry.RegisterAll();  // 自动注册所有Handler
                return registry;
            });

            // Handler执行器
            services.AddSingleton<HandlerExecutor>(sp =>
            {
                var productionContext = sp.GetRequiredService<IProductionContext>();
                var deviceManager = sp.GetRequiredService<IDeviceManager>();
                var stationManager = sp.GetRequiredService<ITaskExecutionManager>();
                var dataFlowManager = sp.GetRequiredService<IDataFlowManager>();
                var logService = sp.GetRequiredService<ILogService>();

                var executor = new HandlerExecutor(
                    productionContext,
                    deviceManager,
                    stationManager,
                    dataFlowManager,
                    logService,
                    sp);  // ★ 传入服务提供者

                // ★ 绑定 PlcSignalMonitor（同时作为 ISignalMonitor 和 ISignalAccessor）
                var signalMonitor = sp.GetService<PlcSignalMonitor>();
                if (signalMonitor != null)
                {
                    executor.BindSignalMonitor(signalMonitor);
                    logService.Information("[App] HandlerExecutor 已绑定 PlcSignalMonitor (支持 ISignalAccessor)");
                }
                else
                {
                    // 降级：使用 FlagCondition 作为信号源（调试模式）
                    executor.BindSignalGetter(signalName =>
                        SeedCut.Framework.Services.Conditions.FlagCondition.GetFlag(signalName));
                    logService.Warning("[App] PlcSignalMonitor 未注册，使用 FlagCondition 作为信号源");
                }

                // 注册来自Registry的所有Handler
                var registry = sp.GetRequiredService<IHandlerRegistry>();
                foreach (var handler in registry.GetAll())
                {
                    executor.Register(handler);
                }

                return executor;
            });

            // 执行控制器（可选，如果需要暂停/超时控制）
            services.AddSingleton<ExecutionController>(sp =>
            {
                var productionContext = sp.GetRequiredService<IProductionContext>();
                var logService = sp.GetRequiredService<ILogService>();
                var connectionMonitor = sp.GetService<ConnectionMonitorService>();

                return new ExecutionController(productionContext, logService, connectionMonitor);
            });

            // ================================================================
            //                    窗口和视图
            // ================================================================

            services.AddTransient<LoadingWindow>(sp => new LoadingWindow(
                sp.GetRequiredService<ISystemMonitor>(),
                sp,
                sp.GetRequiredService<DeviceInitializationService>()
            ));
            services.AddTransient<LoginWindow>();
            services.AddTransient<MainWindow>();
            services.AddTransient<AdminView>();
            services.AddTransient<VisionView>();
            services.AddTransient<CameraView>();
            services.AddTransient<RobotView>();
            services.AddTransient<VibratorView>();
            services.AddTransient<MainPLCView>();
            services.AddTransient<AlarmHistoryView>();
            services.AddTransient<AlarmPopupWindow>();
            services.AddTransient<LaserDebugView>();
            services.AddTransient<HM_LaserDebugView>();
            // 注册 RecipeEditorView（Transient，每次请求新实例）
            services.AddTransient<RecipeEditorView>(sp =>
            {
                var recipeService = sp.GetRequiredService<IRecipeService>();
                return new RecipeEditorView(recipeService);
            });

            services.AddTransient<HandlerDebugView>();
            //services.AddTransient<HandlerDebugViewModel>();

        }

        /// <summary>
        /// 初始化相机SDK
        /// </summary>
        private void InitializeCameraSDK()
        {
            try
            {
                var factory = _serviceProvider.GetRequiredService<HikCameraServiceFactory>();

                if (factory.InitializeSDK())
                {
                    System.Diagnostics.Debug.WriteLine("✓ 海康相机SDK初始化成功");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("❌ 海康相机SDK初始化失败");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 相机SDK初始化异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 初始化报警服务
        /// </summary>
        private void InitializeAlarmService()
        {
            try
            {
                var alarmService = _serviceProvider.GetRequiredService<IAlarmService>();
                _serviceProvider.GetRequiredService<PLCAlarmMonitor>();
                System.Diagnostics.Debug.WriteLine("✓ 报警服务初始化完成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 报警服务初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 注册所有需要检查的模块（核心变更）
        /// 使用DeviceInitializationService统一管理
        /// </summary>
        private void RegisterModuleChecks(DeviceInitializationService initService)
        {
            var systemMonitor = _serviceProvider.GetRequiredService<ISystemMonitor>();

            // 使用初始化服务注册所有模块检查
            // 这会根据配置和当前模式（Debug/Release）自动决定：
            // 1. 哪些设备需要检查
            // 2. 哪些设备是关键的
            // 3. 哪些设备在调试模式下跳过
            initService.RegisterModuleChecks(systemMonitor);
        }

        /// <summary>
        /// 初始化连接监控系统
        /// </summary>
        private void InitializeConnectionMonitor()
        {
            try
            {
                var deviceConfig = _serviceProvider.GetRequiredService<DeviceConnectionConfig>();
                var monitorService = _serviceProvider.GetRequiredService<ConnectionMonitorService>();
                var logService = _serviceProvider.GetRequiredService<ILogService>();

                logService.Information("正在初始化连接监控系统...");

                // 1. 注册PLC设备
                if (ShouldRegisterDevice(deviceConfig, "PLC"))
                {
                    // ✅ 修复：从 DI 获取单例
                    var plcAdapter = _serviceProvider.GetRequiredService<PLCServiceConnectAdapter>();
                    monitorService.RegisterDevice(plcAdapter, ReconnectionConfig.CreateDefaultForPLC());
                    logService.Information("✓ PLC设备已注册到连接监控");
                }

                // 2. 注册激光器设备
                if (ShouldRegisterDevice(deviceConfig, "Laser"))
                {
                    // ✅ 修复：从 DI 获取单例
                    var laserAdapter = _serviceProvider.GetRequiredService<LaserServiceConnectAdapter>();
                    monitorService.RegisterDevice(laserAdapter, laserAdapter.GetReconnectionConfig());
                    logService.Information("✓ 激光器设备已注册到连接监控");
                }

                // 3. 注册机器人设备
                if (ShouldRegisterDevice(deviceConfig, "Robot"))
                {
                    // ✅ 修复：从 DI 获取单例
                    var robotAdapter = _serviceProvider.GetRequiredService<RobotServiceConnectAdapter>();
                    monitorService.RegisterDevice(robotAdapter, robotAdapter.GetReconnectionConfig());
                    logService.Information("✓ 机器人设备已注册到连接监控");
                }

                // 4. 注册振动盘设备
                if (ShouldRegisterDevice(deviceConfig, "Vibrator"))
                {
                    // ✅ 修复：从 DI 获取单例
                    var vibratorAdapter = _serviceProvider.GetRequiredService<VibratorServiceConnectAdapter>();
                    monitorService.RegisterDevice(vibratorAdapter, vibratorAdapter.GetReconnectionConfig());
                    logService.Information("✓ 振动盘设备已注册到连接监控");
                }

                // 5. 注册相机设备（相机没有在 DI 中注册 ConnectAdapter，保持原逻辑）
                RegisterCamerasToMonitor(deviceConfig, monitorService, logService);

                // 6. 订阅全局事件
                monitorService.DeviceStatusChanged += OnGlobalDeviceStatusChanged;
                monitorService.DeviceReconnecting += OnGlobalDeviceReconnecting;

                logService.Information("✓ 连接监控系统初始化完成");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 连接监控系统初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 判断是否应该注册设备到监控
        /// </summary>
        private bool ShouldRegisterDevice(DeviceConnectionConfig config, string deviceId)
        {
            var device = config.GetDeviceConfig(deviceId);
            if (device == null || !device.Enabled)
                return false;

            // 调试模式下，如果设备设置了SkipInDebug，不注册到监控
            if (config.IsDebugMode && device.SkipInDebug)
            {
                System.Diagnostics.Debug.WriteLine($"[调试模式] 跳过注册 {device.DisplayName} 到连接监控");
                return false;
            }

            return true;
        }

        /// <summary>
        /// 注册相机到连接监控
        /// </summary>
        private void RegisterCamerasToMonitor(
            DeviceConnectionConfig deviceConfig,
            ConnectionMonitorService monitorService,
            ILogService logService)
        {
            var cameraFactory = _serviceProvider.GetRequiredService<HikCameraServiceFactory>();
            var cameraIds = new[] { "Camera_Disk", "Camera_Laser", "Camera_Small", "Camera_Large" };

            foreach (var cameraId in cameraIds)
            {
                if (!ShouldRegisterDevice(deviceConfig, cameraId))
                    continue;

                var camera = cameraFactory.GetInstance(cameraId);
                if (camera != null)
                {
                    var adapter = new HikCameraConnectAdapter(camera);
                    monitorService.RegisterDevice(adapter);

                    var deviceInfo = deviceConfig.GetDeviceConfig(cameraId);
                    logService.Information("✓ {0}已注册到连接监控", deviceInfo?.DisplayName ?? cameraId);
                }
            }
        }

        private void OnGlobalDeviceStatusChanged(object sender, DeviceConnectionStatusChangedEventArgs e)
        {
            var logService = _serviceProvider.GetService<ILogService>();

            if (e.IsConnected)
            {
                logService?.Information("[{DeviceName}] 连接成功", e.Status.DeviceName);
            }
            else
            {
                logService?.Warning("[{DeviceName}] 连接断开: {Message}",
                    e.Status.DeviceName, e.Message);
            }

            // 机器人断线时的特殊处理
            if (e.Status.DeviceType == DeviceType.Robot && !e.IsConnected)
            {
                var robotAdapter = _serviceProvider.GetService<RobotServiceConnectAdapter>();
                if (robotAdapter?.WasServoEnabledBeforeDisconnect == true)
                {
                    logService?.Warning("[机器人] ⚠️ 断线前伺服处于使能状态，请注意安全！");
                }
            }

            // 振动盘断线时的特殊处理
            if (e.Status.DeviceType == DeviceType.Vibrator && !e.IsConnected)
            {
                var vibratorAdapter = _serviceProvider.GetService<VibratorServiceConnectAdapter>();
                if (vibratorAdapter?.WasVibratingBeforeDisconnect == true ||
                    vibratorAdapter?.WasFeedingBeforeDisconnect == true)
                {
                    logService?.Warning("[振动盘] ⚠️ 断线前设备正在运行，请检查设备状态！");
                }
            }
        }
        
        private void OnGlobalDeviceReconnecting(object sender, ReconnectionAttemptEventArgs e)
        {
            if (e.Success)
            {
                System.Diagnostics.Debug.WriteLine($"✓ [{e.DeviceName}] 重连成功");
            }
            else if (e.IsMaxRetryReached)
            {
                System.Diagnostics.Debug.WriteLine($"✗ [{e.DeviceName}] 达到最大重试次数");

                if (e.DeviceName.Contains("机器人"))
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ 机器人重连失败，请检查：");
                    System.Diagnostics.Debug.WriteLine($"   1. 机器人控制器是否开机");
                    System.Diagnostics.Debug.WriteLine($"   2. 网络连接是否正常");
                    System.Diagnostics.Debug.WriteLine($"   3. IP地址配置是否正确");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"⟳ [{e.DeviceName}] 重连中 (第{e.RetryCount}次)");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                var visionService = _serviceProvider?.GetService<IVisionService>();
                visionService?.Dispose();

                var cameraFactory = _serviceProvider?.GetService<HikCameraServiceFactory>();
                cameraFactory?.Dispose();

                var robotService = _serviceProvider?.GetService<IRobotService>();
                robotService?.Dispose();

                var vibratorService = _serviceProvider?.GetService<IVibratorService>();
                vibratorService?.Dispose();

                var laserService = _serviceProvider?.GetService<ILaserService>();
                laserService?.Dispose();

                var traceService = _serviceProvider?.GetService<ITraceService>();
                traceService?.Dispose();

                var monitorService = _serviceProvider?.GetService<ConnectionMonitorService>();
                if (monitorService != null)
                {
                    monitorService.DeviceStatusChanged -= OnGlobalDeviceStatusChanged;
                    monitorService.DeviceReconnecting -= OnGlobalDeviceReconnecting;
                    monitorService.Dispose();
                }

                // 释放初始化服务
                var initService = _serviceProvider?.GetService<DeviceInitializationService>();
                initService?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"退出时清理资源异常: {ex.Message}");
            }

            base.OnExit(e);
        }
    }
}