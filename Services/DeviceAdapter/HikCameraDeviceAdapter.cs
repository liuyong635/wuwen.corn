using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Services.Camera;
using SeedCut.Services.Connection;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SeedCut.Services.DeviceAdapter
{
    /// <summary>
    /// 海康相机设备实现 - 实现 ICameraDevice 接口
    /// 职责：
    /// 1. 实现 IDevice/ICameraDevice 接口，供 DeviceManager 统一管理
    /// 2. 通过 HikCameraDeviceAdapter 实现断线重连
    /// 3. 通过 HikCameraInstance 进行实际相机操作
    /// </summary>
    public class HikCameraDeviceAdapter : ICameraDevice
    {
        #region 私有字段

        private readonly HikCameraConnectAdapter _adapter;
        private readonly HikCameraInstance _instance;
        private readonly ConnectionManager _connectionManager;
        private readonly ILogService _logService;

        private DeviceConnectionState _connectionState = DeviceConnectionState.Disconnected;
        private string _lastError;
        private bool _disposed;

        #endregion

        #region IDevice 属性

        /// <summary>
        /// 设备唯一标识
        /// </summary>
        public string DeviceId => _instance.CameraId;

        /// <summary>
        /// 设备显示名称
        /// </summary>
        public string DeviceName => _instance.DisplayName;

        /// <summary>
        /// 设备类型
        /// </summary>
        public Framework.Core.DeviceType DeviceType => Framework.Core.DeviceType.Camera;

        /// <summary>
        /// 连接状态
        /// </summary>
        public DeviceConnectionState ConnectionState
        {
            get { return _connectionState; }
            private set
            {
                if (_connectionState != value)
                {
                    var oldState = _connectionState;
                    _connectionState = value;
                    RaiseConnectionChanged(oldState, value);
                }
            }
        }

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _adapter.IsConnected;

        /// <summary>
        /// 最后错误信息
        /// </summary>
        public string LastError => _lastError;

        #endregion

        #region ICameraDevice 属性

        /// <summary>
        /// 当前图像
        /// </summary>
        public BitmapSource CurrentImage => _instance.CurrentImage;

        /// <summary>
        /// 是否正在采集
        /// </summary>
        public bool IsCapturing => _instance.IsCapturing;

        /// <summary>
        /// 当前分辨率
        /// </summary>
        public string CurrentResolution => _instance.CurrentResolution;

        /// <summary>
        /// 当前帧率
        /// </summary>
        public double CurrentFps => _instance.CurrentFps;

        /// <summary>
        /// 帧计数
        /// </summary>
        public int FrameCount => _instance.FrameCount;

        /// <summary>
        /// 图像保存路径
        /// </summary>
        public string SavePath
        {
            get { return _instance.SavePath; }
            set { _instance.SavePath = value; }
        }

        #endregion

        #region 扩展属性

        /// <summary>
        /// 是否正在重连
        /// </summary>
        public bool IsReconnecting
        {
            get { return _connectionManager != null && _connectionManager.IsReconnecting; }
        }

        /// <summary>
        /// 当前重试次数
        /// </summary>
        public int CurrentRetryCount
        {
            get { return _connectionManager != null ? _connectionManager.CurrentRetryCount : 0; }
        }

        /// <summary>
        /// 相机配置
        /// </summary>
        public CameraInstanceConfig Config => _instance.Config;

        /// <summary>
        /// 获取底层 Instance（用于高级操作或测试）
        /// </summary>
        public HikCameraInstance Instance => _instance;

        #endregion

        #region 事件

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event EventHandler<DeviceConnectionChangedEventArgs> ConnectionChanged;

        /// <summary>
        /// 错误发生事件
        /// </summary>
        public event EventHandler<DeviceErrorEventArgs> ErrorOccurred;

        /// <summary>
        /// 图像接收事件
        /// </summary>
        public event EventHandler ImageReceived;

        /// <summary>
        /// 重连尝试事件
        /// </summary>
        public event EventHandler<ReconnectionAttemptEventArgs> ReconnectionAttempt;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建海康相机设备（带日志服务，支持断线重连）
        /// </summary>
        /// <param name="adapter">相机适配器（负责连接管理）</param>
        /// <param name="instance">相机实例（负责实际操作）</param>
        /// <param name="logService">日志服务</param>
        public HikCameraDeviceAdapter(
            HikCameraConnectAdapter adapter,
            HikCameraInstance instance,
            ILogService logService)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _instance = instance ?? throw new ArgumentNullException(nameof(instance));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));

            // 创建连接管理器（内含断线重连逻辑）
            _connectionManager = new ConnectionManager(_adapter, _logService);

            // 订阅事件
            SubscribeEvents();

            _logService.Information("[{DeviceName}] 相机设备已创建", DeviceName);
        }

        /// <summary>
        /// 创建海康相机设备（无日志服务，不支持断线重连）
        /// 用于简单测试场景
        /// </summary>
        /// <param name="adapter">相机适配器</param>
        /// <param name="instance">相机实例</param>
        public HikCameraDeviceAdapter(
            HikCameraConnectAdapter adapter,
            HikCameraInstance instance)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _instance = instance ?? throw new ArgumentNullException(nameof(instance));
            _logService = null;
            _connectionManager = null;

            // 订阅基本事件（不包含重连事件）
            SubscribeBasicEvents();
        }

        #endregion

        #region IDevice 方法

        /// <summary>
        /// 异步连接设备
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            try
            {
                ConnectionState = DeviceConnectionState.Connecting;

                bool result;
                if (_connectionManager != null)
                {
                    // 使用连接管理器连接（带重连支持）
                    result = await _connectionManager.ConnectAsync();
                }
                else
                {
                    // 直接使用适配器连接（无重连支持）
                    result = await Task.Run(() => _adapter.Connect(), ct);
                }

                ConnectionState = result
                    ? DeviceConnectionState.Connected
                    : DeviceConnectionState.Error;

                return result;
            }
            catch (OperationCanceledException)
            {
                ConnectionState = DeviceConnectionState.Disconnected;
                return false;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                ConnectionState = DeviceConnectionState.Error;
                RaiseError(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 异步断开设备
        /// </summary>
        public Task DisconnectAsync()
        {
            try
            {
                if (_connectionManager != null)
                {
                    // 使用连接管理器断开（会停止心跳和重连）
                    _connectionManager.Disconnect();
                }
                else
                {
                    // 直接使用适配器断开
                    _adapter.Disconnect();
                }

                ConnectionState = DeviceConnectionState.Disconnected;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                RaiseError(ex.Message);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 重置设备
        /// </summary>
        public async Task<bool> ResetAsync()
        {
            await DisconnectAsync();
            await Task.Delay(500); // 等待设备稳定
            return await ConnectAsync();
        }

        /// <summary>
        /// 健康检查
        /// </summary>
        public Task<bool> CheckHealthAsync(CancellationToken ct = default)
        {
            var isHealthy = _adapter.CheckConnection();
            return Task.FromResult(isHealthy);
        }

        #endregion

        #region ICameraDevice 方法

        /// <summary>
        /// 开始连续采集
        /// </summary>
        public async Task<bool> StartCaptureAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("相机未连接，无法开始采集");
                return false;
            }

            return await _instance.StartCaptureAsync();
        }

        /// <summary>
        /// 停止采集
        /// </summary>
        public void StopCapture()
        {
            _instance.StopCapture();
        }

        /// <summary>
        /// 单次采集
        /// </summary>
        public async Task<bool> CaptureOnceAsync(CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("相机未连接，无法执行单次采集");
                return false;
            }

            return await _instance.CaptureOnceAsync();
        }

        /// <summary>
        /// 保存当前图像
        /// </summary>
        public async Task<bool> SaveImageAsync(string path = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                return await _instance.SaveImageAsync();
            }
            return await _instance.SaveImageToPathAsync(path);
        }

        /// <summary>
        /// 设置曝光时间（微秒）
        /// </summary>
        public void SetExposure(double exposureTime)
        {
            _instance.SetExposureTime(exposureTime);
        }

        /// <summary>
        /// 设置增益
        /// </summary>
        public void SetGain(double gain)
        {
            _instance.SetGain(gain);
        }

        /// <summary>
        /// 设置帧率
        /// </summary>
        public void SetFrameRate(double frameRate)
        {
            _instance.SetFrameRate(frameRate);
        }

        #endregion

        #region 扩展方法

        /// <summary>
        /// 设置像素格式
        /// </summary>
        public void SetPixelFormat(string format)
        {
            _instance.SetPixelFormat(format);
        }

        /// <summary>
        /// 应用相机参数
        /// </summary>
        public void ApplySettings(double exposure, double gain, double fps, string format)
        {
            _instance.ApplySettings(exposure, gain, fps, format);
        }

        /// <summary>
        /// 刷新相机列表
        /// </summary>
        public void RefreshCameraList()
        {
            _instance.RefreshCameraList();
        }

        #endregion

        #region 事件订阅

        private void SubscribeEvents()
        {
            // 订阅基本事件
            SubscribeBasicEvents();

            // 订阅连接管理器事件
            if (_connectionManager != null)
            {
                _connectionManager.ConnectionStateChanged += OnConnectionManagerStateChanged;
                _connectionManager.ReconnectionAttempt += OnReconnectionAttempt;
            }
        }

        private void SubscribeBasicEvents()
        {
            // 订阅适配器连接状态事件
            _adapter.ConnectionStateChanged += OnAdapterConnectionStateChanged;

            // 订阅 Instance 事件
            _instance.ErrorOccurred += OnInstanceError;
            _instance.ImageReceived += OnInstanceImageReceived;
        }

        private void UnsubscribeEvents()
        {
            // 取消订阅适配器事件
            _adapter.ConnectionStateChanged -= OnAdapterConnectionStateChanged;

            // 取消订阅 Instance 事件
            _instance.ErrorOccurred -= OnInstanceError;
            _instance.ImageReceived -= OnInstanceImageReceived;

            // 取消订阅连接管理器事件
            if (_connectionManager != null)
            {
                _connectionManager.ConnectionStateChanged -= OnConnectionManagerStateChanged;
                _connectionManager.ReconnectionAttempt -= OnReconnectionAttempt;
            }
        }

        #endregion

        #region 事件处理

        private void OnAdapterConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            // 仅当没有连接管理器时，直接响应适配器状态变化
            if (_connectionManager == null)
            {
                ConnectionState = e.IsConnected
                    ? DeviceConnectionState.Connected
                    : DeviceConnectionState.Disconnected;
            }
        }

        private void OnConnectionManagerStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            // 通过连接管理器更新状态
            if (e.IsConnected)
            {
                ConnectionState = DeviceConnectionState.Connected;
            }
            else if (_connectionManager.IsReconnecting)
            {
                ConnectionState = DeviceConnectionState.Reconnecting;
            }
            else
            {
                ConnectionState = DeviceConnectionState.Disconnected;
            }
        }

        private void OnReconnectionAttempt(object sender, ReconnectionAttemptEventArgs e)
        {
            // 更新状态
            if (!e.Success && !e.IsMaxRetryReached)
            {
                ConnectionState = DeviceConnectionState.Reconnecting;
            }
            else if (e.IsMaxRetryReached)
            {
                ConnectionState = DeviceConnectionState.Error;
                _lastError = "达到最大重连次数";
            }

            // 转发事件
            ReconnectionAttempt?.Invoke(this, e);
        }

        private void OnInstanceError(object sender, string error)
        {
            _lastError = error;
            RaiseError(error);
        }

        private void OnInstanceImageReceived(object sender, EventArgs e)
        {
            ImageReceived?.Invoke(this, e);
        }

        #endregion

        #region 事件触发

        private void RaiseConnectionChanged(DeviceConnectionState oldState, DeviceConnectionState newState)
        {
            ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs
            {
                DeviceId = DeviceId,
                DeviceName = DeviceName,
                OldState = oldState,
                NewState = newState
            });
        }

        private void RaiseError(string message)
        {
            ErrorOccurred?.Invoke(this, new DeviceErrorEventArgs
            {
                DeviceId = DeviceId,
                ErrorMessage = message
            });

            if (_logService != null)
            {
                _logService.Error("[{DeviceName}] {Error}", DeviceName, message);
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            // 停止采集
            if (IsCapturing)
            {
                StopCapture();
            }

            // 取消订阅事件
            UnsubscribeEvents();

            // 释放连接管理器
            if (_connectionManager != null)
            {
                _connectionManager.Dispose();
            }

            // 注意：不要释放 _adapter 和 _instance
            // 它们的生命周期由工厂管理

            _disposed = true;

            if (_logService != null)
            {
                _logService.Information("[{DeviceName}] 相机设备已释放", DeviceName);
            }
        }

        #endregion
    }
}