using MvCameraControl;
using SeedCut.Services.Connection;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SeedCut.Services.Camera
{
    /// <summary>
    /// 海康相机实例 - 单个相机的完整实现
    /// 实现 ICameraService 接口，保证与现有 ViewModel 兼容
    /// </summary>
    public class HikCameraInstance : ICameraService, IDisposable
    {
        private readonly CameraInstanceConfig _config;
        private readonly HikCameraSDKManager _sdkManager;

        private IDevice _device;
        private IDeviceInfo _currentDeviceInfo;
        private bool _isGrabbing;
        private Thread _receiveThread;
        private bool _disposed;

        // 状态属性
        private List<string> _cameraList = new List<string>();
        private bool _isConnected;
        private bool _isCapturing;
        private int _frameCount;
        private string _currentResolution = "0 × 0";
        private double _currentFps;
        private string _savePath;
        private BitmapSource _currentImage;

        // FPS计算
        private System.Timers.Timer _fpsTimer;
        private int _frameCountForFps;
        private readonly object _imageLock = new object();
        private IFrameOut _frameForSave;
        private static readonly SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(1, 1);

        #region 构造函数

        /// <summary>
        /// 创建相机实例
        /// </summary>
        /// <param name="config">相机配置</param>
        /// <param name="sdkManager">SDK管理器</param>
        public HikCameraInstance(CameraInstanceConfig config, HikCameraSDKManager sdkManager)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _sdkManager = sdkManager ?? throw new ArgumentNullException(nameof(sdkManager));

            // 初始化保存路径
            _savePath = string.IsNullOrEmpty(_config.SavePath)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                    "SeedCut_Images",
                    _config.CameraId)
                : _config.SavePath;

            Directory.CreateDirectory(_savePath);

            // 初始化FPS计时器
            _fpsTimer = new System.Timers.Timer(1000);
            _fpsTimer.Elapsed += (s, e) => UpdateFpsCounter();

            _cameraList.Add("请先刷新相机列表");
        }

        #endregion

        #region 相机标识属性

        /// <summary>
        /// 相机唯一标识
        /// </summary>
        public string CameraId => _config.CameraId;

        /// <summary>
        /// 相机显示名称
        /// </summary>
        public string DisplayName => _config.DisplayName;

        /// <summary>
        /// 相机配置
        /// </summary>
        public CameraInstanceConfig Config => _config;

        /// <summary>
        /// 当前连接的设备信息
        /// </summary>
        public IDeviceInfo CurrentDeviceInfo => _currentDeviceInfo;

        #endregion

        #region ICameraService 属性实现

        public List<string> CameraList
        {
            get => _cameraList;
            private set => SetProperty(ref _cameraList, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    // 触发连接状态变化事件（供适配器使用）
                    InternalConnectionStateChanged?.Invoke(this, value);
                }
            }
        }

        public bool IsCapturing
        {
            get => _isCapturing;
            private set => SetProperty(ref _isCapturing, value);
        }

        public int FrameCount
        {
            get => _frameCount;
            private set => SetProperty(ref _frameCount, value);
        }

        public string CurrentResolution
        {
            get => _currentResolution;
            private set => SetProperty(ref _currentResolution, value);
        }

        public double CurrentFps
        {
            get => _currentFps;
            private set => SetProperty(ref _currentFps, value);
        }

        public string SavePath
        {
            get => _savePath;
            set
            {
                if (_savePath != value)
                {
                    _savePath = value;
                    if (!string.IsNullOrEmpty(value))
                    {
                        Directory.CreateDirectory(value);
                    }
                    OnPropertyChanged();
                }
            }
        }

        public BitmapSource CurrentImage
        {
            get
            {
                lock (_imageLock)
                {
                    return _currentImage;
                }
            }
            private set
            {
                lock (_imageLock)
                {
                    if (_currentImage != value)
                    {
                        _currentImage = value;
                        OnPropertyChanged();
                    }
                }
            }
        }

        #endregion

        #region 事件

        public event EventHandler<string> ErrorOccurred;
        public event EventHandler<string> MessageReceived;
        public event EventHandler ImageReceived;
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 内部连接状态变化事件（供 HikCameraDeviceAdapter 使用）
        /// </summary>
        internal event EventHandler<bool> InternalConnectionStateChanged;

        #endregion

        #region ICameraService 方法实现

        /// <summary>
        /// 初始化SDK（委托给SDKManager）
        /// </summary>
        public bool InitializeSDK()
        {
            var result = _sdkManager.InitializeSDK();
            if (result)
            {
                RaiseMessage($"[{DisplayName}] SDK初始化成功");
                RefreshCameraList();
            }
            else
            {
                RaiseError($"[{DisplayName}] SDK初始化失败");
            }
            return result;
        }

        /// <summary>
        /// 刷新相机列表
        /// </summary>
        public void RefreshCameraList()
        {
            if (!_sdkManager.IsInitialized)
            {
                RaiseError("SDK未初始化");
                return;
            }

            _sdkManager.EnumerateDevices();
            CameraList = _sdkManager.GetDeviceDisplayNames();
            RaiseMessage($"[{DisplayName}] 检测到 {_sdkManager.DeviceInfoList?.Count ?? 0} 个相机");
        }

        /// <summary>
        /// 连接相机（通过索引）
        /// </summary>
        public async Task<bool> ConnectCameraAsync(int cameraIndex)
        {
            return await Task.Run(() =>
            {
                var deviceInfo = _sdkManager.GetDeviceInfo(cameraIndex);
                if (deviceInfo == null)
                {
                    RaiseError($"[{DisplayName}] 相机索引无效: {cameraIndex}");
                    return false;
                }
                return ConnectToDevice(deviceInfo);
            });
        }

        /// <summary>
        /// 根据配置自动连接相机
        /// </summary>
        public async Task<bool> AutoConnectAsync()
        {
            // ✅ 关键：获取锁，确保同一时间只有一个相机在连接
            await _connectionSemaphore.WaitAsync();
            try
            {
                return await Task.Run(() =>
                {
                    // 先清理现有资源
                    CleanupDevice();

                    // 枚举设备（现在是安全的，因为有锁保护）
                    _sdkManager.EnumerateDevices();

                    // 根据配置查找设备
                    var deviceInfo = _sdkManager.FindDeviceByConfig(_config);
                    if (deviceInfo == null)
                    {
                        RaiseError($"[{DisplayName}] 未找到匹配的相机 (SN: {_config.SerialNumber}, Name: {_config.UserDefinedName})");
                        return false;
                    }

                    System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 找到设备: SN={deviceInfo.SerialNumber}");

                    return ConnectToDevice(deviceInfo);
                });
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        /// <summary>
        /// 连接到指定设备
        /// </summary>
        private bool ConnectToDevice(IDeviceInfo deviceInfo)
        {
            try
            {
                // ✅ 关键修复：无论 IsConnected 状态如何，都要先清理现有设备资源
                CleanupDevice();

                _currentDeviceInfo = deviceInfo;

                // 创建设备
                try
                {
                    _device = DeviceFactory.CreateDevice(deviceInfo);
                }
                catch (Exception ex)
                {
                    RaiseError($"[{DisplayName}] 创建设备失败: {ex.Message}");
                    _device = null;  // 确保清空
                    return false;
                }

                // 打开设备
                int nRet = _device.Open();
                if (nRet != MvError.MV_OK)
                {
                    RaiseError($"[{DisplayName}] 打开相机失败: 0x{nRet:X8}");
                    _device.Dispose();
                    _device = null;
                    return false;
                }

                // 配置设备
                ConfigureDevice();

                // 应用默认设置
                ApplyDefaultSettings();

                IsConnected = true;
                UpdateCameraInfo();
                RaiseMessage($"[{DisplayName}] 相机连接成功 (SN: {deviceInfo.SerialNumber})");
                return true;
            }
            catch (Exception ex)
            {
                RaiseError($"[{DisplayName}] 连接相机异常: {ex.Message}");
                CleanupDevice();  // 异常时也要清理
                return false;
            }
        }
        /// <summary>
        /// ✅ 新增：清理设备资源（无论连接状态）
        /// </summary>
        private void CleanupDevice()
        {
            try
            {
                if (_isGrabbing)
                {
                    _isGrabbing = false;
                    _fpsTimer?.Stop();

                    if (_receiveThread != null && _receiveThread.IsAlive)
                    {
                        _receiveThread.Join(TimeSpan.FromSeconds(2));
                        _receiveThread = null;
                    }
                }

                if (_device != null)
                {
                    try { _device.StreamGrabber?.StopGrabbing(); } catch { }
                    try { _device.Close(); } catch { }
                    try { _device.Dispose(); } catch { }
                    _device = null;
                }

                _isConnected = false;
                _currentDeviceInfo = null;
                _isCapturing = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 清理设备异常: {ex.Message}");
            }
        }
        /// <summary>
        /// 配置设备参数
        /// </summary>
        private void ConfigureDevice()
        {
            if (_device == null)
                return;

            try
            {
                // 如果是GigE相机，设置最佳包大小
                if (_device is IGigEDevice gigEDevice)
                {
                    int nRet = gigEDevice.GetOptimalPacketSize(out int packetSize);
                    if (nRet == MvError.MV_OK)
                    {
                        nRet = _device.Parameters.SetIntValue("GevSCPSPacketSize", packetSize);
                        if (nRet != MvError.MV_OK)
                        {
                            System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 设置包大小失败: 0x{nRet:X8}");
                        }
                    }
                }
                else if (_device is IUSBDevice usbDevice)
                {
                    // 设置USB同步读写超时时间
                    usbDevice.SetSyncTimeOut(1000);
                }

                // 设置触发模式为关闭（连续采集）
                _device.Parameters.SetEnumValueByString("TriggerMode", "Off");
                _device.Parameters.SetEnumValueByString("AcquisitionMode", "Continuous");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 配置设备异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 应用默认设置
        /// </summary>
        private void ApplyDefaultSettings()
        {
            if (_device == null || !IsConnected)
                return;

            try
            {
                SetExposureTime(_config.DefaultExposure);
                SetGain(_config.DefaultGain);
                SetFrameRate(_config.DefaultFrameRate);

                if (!string.IsNullOrEmpty(_config.PixelFormat))
                {
                    SetPixelFormat(_config.PixelFormat);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 应用默认设置异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 断开相机
        /// </summary>
        public void DisconnectCamera()
        {
            CleanupDevice();

            FrameCount = 0;
            CurrentResolution = "0 × 0";
            CurrentFps = 0.0;
            CurrentImage = null;

            RaiseMessage($"[{DisplayName}] 相机已断开");
        }

        /// <summary>
        /// 开始连续采集
        /// </summary>
        public async Task<bool> StartCaptureAsync()
        {
            if (!IsConnected || IsCapturing)
                return false;

            try
            {
                // 标志位置位true
                _isGrabbing = true;

                // 创建接收线程
                _receiveThread = new Thread(ReceiveThreadProcess)
                {
                    IsBackground = true,
                    Name = $"CameraReceive-{CameraId}"
                };
                _receiveThread.Start();

                // 开始采集
                int nRet = _device.StreamGrabber.StartGrabbing();
                if (nRet != MvError.MV_OK)
                {
                    _isGrabbing = false;
                    _receiveThread.Join();
                    RaiseError($"[{DisplayName}] 开始采集失败: 0x{nRet:X8}");
                    return false;
                }

                IsCapturing = true;
                FrameCount = 0;
                _frameCountForFps = 0;
                _fpsTimer.Start();

                RaiseMessage($"[{DisplayName}] 开始图像采集");
                return true;
            }
            catch (Exception ex)
            {
                _isGrabbing = false;
                RaiseError($"[{DisplayName}] 开始采集异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 停止采集
        /// </summary>
        public void StopCapture()
        {
            if (!IsConnected || !IsCapturing)
                return;

            try
            {
                // 先设置标志位，让线程准备退出
                _isGrabbing = false;
                IsCapturing = false;
                _fpsTimer.Stop();

                // 停止流采集
                if (_device != null)
                {
                    int nRet = _device.StreamGrabber.StopGrabbing();
                    if (nRet != MvError.MV_OK)
                    {
                        System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 停止采集返回: 0x{nRet:X8}");
                    }
                }

                // 等待线程结束
                if (_receiveThread != null && _receiveThread.IsAlive)
                {
                    if (!_receiveThread.Join(TimeSpan.FromSeconds(3)))
                    {
                        System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 警告: 接收线程超时未结束");
                    }
                    _receiveThread = null;
                }

                CurrentFps = 0.0;
                RaiseMessage($"[{DisplayName}] 停止图像采集");
            }
            catch (Exception ex)
            {
                RaiseError($"[{DisplayName}] 停止采集异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 单次采集
        /// </summary>
        public async Task<bool> CaptureOnceAsync()
        {
            if (!IsConnected || IsCapturing)
                return false;

            return await Task.Run(() =>
            {
                try
                {
                    // 开始取流
                    int nRet = _device.StreamGrabber.StartGrabbing();
                    if (nRet != MvError.MV_OK)
                    {
                        RaiseError($"[{DisplayName}] 启动采集失败: 0x{nRet:X8}");
                        return false;
                    }

                    // 获取一帧图像
                    nRet = _device.StreamGrabber.GetImageBuffer(1000, out IFrameOut frameOut);
                    if (nRet == MvError.MV_OK)
                    {
                        ProcessFrame(frameOut);
                        _device.StreamGrabber.FreeImageBuffer(frameOut);
                    }

                    _device.StreamGrabber.StopGrabbing();

                    RaiseMessage($"[{DisplayName}] 执行单次采集");
                    return nRet == MvError.MV_OK;
                }
                catch (Exception ex)
                {
                    RaiseError($"[{DisplayName}] 单次采集异常: {ex.Message}");
                    return false;
                }
            });
        }

        #endregion

        #region 参数设置

        public void SetExposureTime(double exposureTime)
        {
            if (!IsConnected || _device == null)
                return;

            try
            {
                int nRet = _device.Parameters.SetFloatValue("ExposureTime", (float)exposureTime);
                if (nRet != MvError.MV_OK)
                {
                    System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 设置曝光时间失败: 0x{nRet:X8}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 设置曝光时间异常: {ex.Message}");
            }
        }

        public void SetGain(double gain)
        {
            if (!IsConnected || _device == null)
                return;

            try
            {
                int nRet = _device.Parameters.SetFloatValue("Gain", (float)gain);
                if (nRet != MvError.MV_OK)
                {
                    System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 设置增益失败: 0x{nRet:X8}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 设置增益异常: {ex.Message}");
            }
        }

        public void SetFrameRate(double frameRate)
        {
            if (!IsConnected || _device == null)
                return;

            try
            {
                // 先启用帧率控制
                _device.Parameters.SetBoolValue("AcquisitionFrameRateEnable", true);
                int nRet = _device.Parameters.SetFloatValue("AcquisitionFrameRate", (float)frameRate);
                if (nRet != MvError.MV_OK)
                {
                    System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 设置帧率失败: 0x{nRet:X8}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 设置帧率异常: {ex.Message}");
            }
        }

        public void SetPixelFormat(string format)
        {
            if (!IsConnected || _device == null)
                return;

            try
            {
                int nRet = _device.Parameters.SetEnumValueByString("PixelFormat", format);
                if (nRet != MvError.MV_OK)
                {
                    System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 设置像素格式失败: 0x{nRet:X8}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 设置像素格式异常: {ex.Message}");
            }
        }

        public void ApplySettings(double exposure, double gain, double fps, string format)
        {
            SetExposureTime(exposure);
            SetGain(gain);
            SetFrameRate(fps);
            SetPixelFormat(format);
            RaiseMessage($"[{DisplayName}] 参数设置已应用");
        }

        #endregion

        #region 图像保存

        public async Task<bool> SaveImageAsync()
        {
            string fileName = $"{CameraId}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.bmp";
            string filePath = Path.Combine(_savePath, fileName);
            return await SaveImageToPathAsync(filePath);
        }

        public async Task<bool> SaveImageToPathAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    IFrameOut frameToSave;
                    lock (_imageLock)
                    {
                        if (_frameForSave == null)
                        {
                            RaiseError($"[{DisplayName}] 没有可保存的图像");
                            return false;
                        }
                        frameToSave = _frameForSave;
                    }

                    // 确保目录存在
                    string directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // 根据文件扩展名确定保存格式
                    string extension = Path.GetExtension(filePath).ToLower();
                    var imageFormatInfo = new ImageFormatInfo();

                    switch (extension)
                    {
                        case ".jpg":
                        case ".jpeg":
                            imageFormatInfo.FormatType = ImageFormatType.Jpeg;
                            imageFormatInfo.JpegQuality = 80;
                            break;
                        case ".png":
                            imageFormatInfo.FormatType = ImageFormatType.Png;
                            break;
                        case ".tif":
                        case ".tiff":
                            imageFormatInfo.FormatType = ImageFormatType.Tiff;
                            break;
                        case ".bmp":
                        default:
                            imageFormatInfo.FormatType = ImageFormatType.Bmp;
                            break;
                    }

                    // 使用SDK保存图像（按照SDK正确的方法签名）
                    int nRet = _device.ImageSaver.SaveImageToFile(
                        filePath,
                        frameToSave.Image,
                        imageFormatInfo,
                        CFAMethod.Optimal);

                    if (nRet == MvError.MV_OK)
                    {
                        RaiseMessage($"[{DisplayName}] 图像已保存: {filePath}");
                        return true;
                    }
                    else
                    {
                        RaiseError($"[{DisplayName}] 保存图像失败: 0x{nRet:X8}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    RaiseError($"[{DisplayName}] 保存图像异常: {ex.Message}");
                    return false;
                }
            });
        }

        #endregion

        #region 心跳检测（供适配器使用）

        /// <summary>
        /// 检查连接状态（心跳检测）
        /// </summary>
        internal bool CheckConnection()
        {
            if (!IsConnected || _device == null)
                return false;

            try
            {
                // 尝试读取一个基本参数来检查连接
                int nRet = _device.Parameters.GetIntValue("Width", out IIntValue widthValue);
                return nRet == MvError.MV_OK;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 私有方法 - 图像接收与处理

        private void ReceiveThreadProcess()
        {
            while (_isGrabbing)
            {
                try
                {
                    // 使用较短的超时时间，以便更快响应停止信号
                    int nRet = _device.StreamGrabber.GetImageBuffer(100, out IFrameOut frameOut);

                    if (nRet == MvError.MV_OK)
                    {
                        // 再次检查标志位，避免在停止过程中处理图像
                        if (_isGrabbing)
                        {
                            ProcessFrame(frameOut);
                        }
                        _device.StreamGrabber.FreeImageBuffer(frameOut);
                    }
                    else if (nRet == MvError.MV_E_NODATA)
                    {
                        // 超时是正常的，继续循环
                        continue;
                    }
                    else
                    {
                        // 其他错误
                        Thread.Sleep(5);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 接收图像异常: {ex.Message}");
                    Thread.Sleep(10);
                }
            }

            System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 接收线程正常退出");
        }

        private void ProcessFrame(IFrameOut frameOut)
        {
            try
            {
                // 保存帧用于后续保存图像
                lock (_imageLock)
                {
                    _frameForSave?.Dispose();
                    _frameForSave = frameOut.Clone() as IFrameOut;
                }

                // 转换为BitmapSource显示
                var bitmapSource = ConvertToBitmapSource(frameOut);
                if (bitmapSource != null)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        CurrentImage = bitmapSource;
                        FrameCount++;
                        _frameCountForFps++;
                        ImageReceived?.Invoke(this, EventArgs.Empty);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 处理帧异常: {ex.Message}");
            }
        }

        private BitmapSource ConvertToBitmapSource(IFrameOut frameOut)
        {
            try
            {
                IImage image = frameOut.Image;
                int width = (int)image.Width;
                int height = (int)image.Height;
                MvGvspPixelType pixelType = image.PixelType;

                PixelFormat pixelFormat;
                int stride;
                byte[] imageData;

                if (IsMonoPixelType(pixelType))
                {
                    // 黑白格式
                    pixelFormat = PixelFormats.Gray8;
                    stride = width;
                    imageData = new byte[width * height];
                    System.Runtime.InteropServices.Marshal.Copy(image.PixelDataPtr, imageData, 0, imageData.Length);
                }
                else if (IsColorPixelType(pixelType))
                {
                    // 彩色格式 - 转换为RGB
                    pixelFormat = PixelFormats.Rgb24;
                    stride = width * 3;

                    int nRet = _device.PixelTypeConverter.ConvertPixelType(
                        image, out IImage outImage, MvGvspPixelType.PixelType_Gvsp_RGB8_Packed);

                    if (nRet == MvError.MV_OK)
                    {
                        imageData = new byte[width * height * 3];
                        System.Runtime.InteropServices.Marshal.Copy(outImage.PixelDataPtr, imageData, 0, imageData.Length);
                        outImage.Dispose();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 像素格式转换失败: 0x{nRet:X8}");
                        return null;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 不支持的像素格式: {pixelType}");
                    return null;
                }

                var bitmap = BitmapSource.Create(
                    width, height,
                    96, 96,
                    pixelFormat,
                    null,
                    imageData,
                    stride
                );

                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 图像转换异常: {ex.Message}");
                return null;
            }
        }

        private bool IsMonoPixelType(MvGvspPixelType pixelType)
        {
            return pixelType == MvGvspPixelType.PixelType_Gvsp_Mono8 ||
                   pixelType == MvGvspPixelType.PixelType_Gvsp_Mono10 ||
                   pixelType == MvGvspPixelType.PixelType_Gvsp_Mono12 ||
                   pixelType == MvGvspPixelType.PixelType_Gvsp_Mono16;
        }

        private bool IsColorPixelType(MvGvspPixelType pixelType)
        {
            return pixelType == MvGvspPixelType.PixelType_Gvsp_RGB8_Packed ||
                   pixelType == MvGvspPixelType.PixelType_Gvsp_BGR8_Packed ||
                   pixelType == MvGvspPixelType.PixelType_Gvsp_BayerGR8 ||
                   pixelType == MvGvspPixelType.PixelType_Gvsp_BayerRG8 ||
                   pixelType == MvGvspPixelType.PixelType_Gvsp_BayerGB8 ||
                   pixelType == MvGvspPixelType.PixelType_Gvsp_BayerBG8 ||
                   pixelType == MvGvspPixelType.PixelType_Gvsp_BayerGB10 ||
                   pixelType == MvGvspPixelType.PixelType_Gvsp_BayerBG10 ||
                   pixelType == MvGvspPixelType.PixelType_Gvsp_BayerGR10 ||
                   pixelType == MvGvspPixelType.PixelType_Gvsp_BayerRG10;
        }

        private void UpdateCameraInfo()
        {
            if (!IsConnected || _device == null)
                return;

            try
            {
                int nRet = _device.Parameters.GetIntValue("Width", out IIntValue widthValue);
                long width = nRet == MvError.MV_OK ? widthValue.CurValue : 0;

                nRet = _device.Parameters.GetIntValue("Height", out IIntValue heightValue);
                long height = nRet == MvError.MV_OK ? heightValue.CurValue : 0;

                if (width > 0 && height > 0)
                {
                    CurrentResolution = $"{width} × {height}";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{DisplayName}] 更新相机信息异常: {ex.Message}");
            }
        }

        private void UpdateFpsCounter()
        {
            CurrentFps = _frameCountForFps;
            _frameCountForFps = 0;
        }

        #endregion

        #region 事件触发

        private void RaiseError(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] {message}");
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ErrorOccurred?.Invoke(this, message);
            });
        }

        private void RaiseMessage(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] {message}");
            Application.Current?.Dispatcher.Invoke(() =>
            {
                MessageReceived?.Invoke(this, message);
            });
        }

        #endregion

        #region INotifyPropertyChanged

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            DisconnectCamera();

            _fpsTimer?.Stop();
            _fpsTimer?.Dispose();

            lock (_imageLock)
            {
                _frameForSave?.Dispose();
                _frameForSave = null;
            }

            _disposed = true;
        }

        #endregion
    }
}