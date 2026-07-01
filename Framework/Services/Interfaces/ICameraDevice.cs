using SeedCut.Framework.Services.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

/// <summary>
/// 相机设备接口 - 继承 IDevice
/// </summary>
public interface ICameraDevice : IDevice
{
    #region 相机特有属性

    /// <summary>
    /// 当前图像
    /// </summary>
    BitmapSource CurrentImage { get; }

    /// <summary>
    /// 是否正在采集
    /// </summary>
    bool IsCapturing { get; }

    /// <summary>
    /// 当前分辨率
    /// </summary>
    string CurrentResolution { get; }

    /// <summary>
    /// 当前帧率
    /// </summary>
    double CurrentFps { get; }

    /// <summary>
    /// 帧计数
    /// </summary>
    int FrameCount { get; }

    /// <summary>
    /// 图像保存路径
    /// </summary>
    string SavePath { get; set; }

    #endregion

    #region 相机操作

    /// <summary>
    /// 开始连续采集
    /// </summary>
    Task<bool> StartCaptureAsync(CancellationToken ct = default);

    /// <summary>
    /// 停止采集
    /// </summary>
    void StopCapture();

    /// <summary>
    /// 单次采集
    /// </summary>
    Task<bool> CaptureOnceAsync(CancellationToken ct = default);

    /// <summary>
    /// 保存当前图像
    /// </summary>
    Task<bool> SaveImageAsync(string path = null);

    #endregion

    #region 参数设置

    /// <summary>
    /// 设置曝光时间（微秒）
    /// </summary>
    void SetExposure(double exposureTime);

    /// <summary>
    /// 设置增益
    /// </summary>
    void SetGain(double gain);

    /// <summary>
    /// 设置帧率
    /// </summary>
    void SetFrameRate(double frameRate);

    #endregion

    #region 事件

    /// <summary>
    /// 图像接收事件
    /// </summary>
    event EventHandler ImageReceived;

    #endregion
}