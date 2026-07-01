using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace SeedCut.Services
{
    /// <summary>
    /// 相机服务接口
    /// </summary>
    public interface ICameraService : INotifyPropertyChanged, IDisposable
    {
        // ========== 属性 ==========
        List<string> CameraList { get; }
        bool IsConnected { get; }
        bool IsCapturing { get; }
        int FrameCount { get; }
        string CurrentResolution { get; }
        double CurrentFps { get; }
        string SavePath { get; set; }
        BitmapSource CurrentImage { get; }

        // ========== 事件 ==========
        event EventHandler<string> ErrorOccurred;
        event EventHandler<string> MessageReceived;
        event EventHandler ImageReceived;

        // ========== 方法 ==========
        bool InitializeSDK();
        void RefreshCameraList();
        Task<bool> ConnectCameraAsync(int cameraIndex);
        void DisconnectCamera();
        Task<bool> StartCaptureAsync();
        void StopCapture();
        Task<bool> CaptureOnceAsync();
        void SetExposureTime(double exposureTime);
        void SetGain(double gain);
        void SetFrameRate(double frameRate);
        void SetPixelFormat(string format);
        void ApplySettings(double exposure, double gain, double fps, string format);
        Task<bool> SaveImageAsync();
        Task<bool> SaveImageToPathAsync(string filePath);
    }
}