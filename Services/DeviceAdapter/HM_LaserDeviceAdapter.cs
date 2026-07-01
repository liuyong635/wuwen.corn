using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.Models;
using SeedCut.Services.HM;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// ★ 别名：解决 Framework 层与 Services 层同名类的冲突
// ILaserDevice 接口要求的是 Framework 版本
using FrameworkLaserResponseEventArgs = SeedCut.Framework.Services.Interfaces.LaserResponseEventArgs;
using FrameworkLaserMarkFinishedEventArgs = SeedCut.Framework.Services.Interfaces.LaserMarkFinishedEventArgs;

namespace SeedCut.Services.DeviceAdapter
{
    /// <summary>
    /// HM激光器设备适配器 — 实现 ILaserDevice 接口
    /// 
    /// 职责：
    /// 1. 桥接 ILaserDevice 接口与 HM_LaserService（SDK方案）
    /// 2. 实现多Pass打标接口（利用UDM图层机制）
    /// 3. 提供连接管理和状态转发
    /// 
    /// ★ v2 改动：新增 AddLinesAndMarkMultiPassAsync 实现
    /// </summary>
    public class HM_LaserDeviceAdapter : ILaserDevice
    {
        #region 私有字段

        private readonly HM_LaserService _service;
        private readonly ILogService _logService;
        private LaserState _laserState = LaserState.Idle;
        private string _lastError;
        private bool _disposed;

        #endregion

        #region IDevice 属性

        public string DeviceId => "HM_Laser";
        public string DeviceName => "HM激光器";
        public DeviceType DeviceType => DeviceType.Laser;

        public DeviceConnectionState ConnectionState =>
            _service.IsConnected ? DeviceConnectionState.Connected : DeviceConnectionState.Disconnected;

        public bool IsConnected => _service.IsConnected;
        public string LastError => _lastError;

        #endregion

        #region ILaserDevice 属性

        public LaserState LaserState
        {
            get => _laserState;
            private set
            {
                if (_laserState != value)
                {
                    _laserState = value;
                    StatusChanged?.Invoke(this, string.Format("激光状态: {0}", value));
                }
            }
        }

        public bool IsMarking => _service.IsMarking;
        public int MarkProgress => _service.MarkProgress;
        public bool IsServerStarted => _service.IsConnected;
        public int QueueLength => 0; // HM方案不使用命令队列

        #endregion

        #region 事件

        public event EventHandler<DeviceConnectionChangedEventArgs> ConnectionChanged;
        public event EventHandler<DeviceErrorEventArgs> ErrorOccurred;
        public event EventHandler<FrameworkLaserResponseEventArgs> ResponseReceived;
        public event EventHandler<FrameworkLaserMarkFinishedEventArgs> MarkFinished;
        public event EventHandler<string> StatusChanged;

        #endregion

        #region 构造函数

        public HM_LaserDeviceAdapter(HM_LaserService service, ILogService logService)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));

            // 订阅 HM_LaserService 事件
            _service.ConnectionChanged += OnServiceConnectionChanged;
            _service.MarkFinished += OnServiceMarkFinished;
            _service.ProgressChanged += OnServiceProgressChanged;
            _service.StatusChanged += OnServiceStatusChanged;
            _service.ErrorOccurred += OnServiceError;

            _logService.Information("[HM_LaserAdapter] 适配器已创建");
        }

        #endregion

        #region IDevice 方法

        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            try
            {
                LaserState = LaserState.Connecting;
                var result = await _service.ConnectAsync();

                LaserState = result ? LaserState.Idle : LaserState.Error;
                return result;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                LaserState = LaserState.Error;
                RaiseError(ex.Message);
                return false;
            }
        }

        public Task DisconnectAsync()
        {
            _service.Disconnect();
            LaserState = LaserState.Idle;
            return Task.CompletedTask;
        }

        public Task<bool> ResetAsync()
        {
            if (IsMarking)
            {
                _service.StopMark();
            }
            LaserState = LaserState.Idle;
            _lastError = null;
            return Task.FromResult(true);
        }

        public Task<bool> CheckHealthAsync(CancellationToken ct = default)
        {
            return Task.FromResult(_service.IsConnected);
        }

        #endregion

        #region TCP服务器管理（HM方案不使用TCP，提供空实现）

        public Task<bool> StartServerAsync(int port = 0, CancellationToken ct = default)
        {
            // HM方案通过IP直连控制卡，不需要TCP服务器
            return ConnectAsync(ct);
        }

        public Task StopServerAsync()
        {
            return DisconnectAsync();
        }

        #endregion

        #region 基础指令

        public async Task<bool> SendCommandAsync(string command, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("激光器未连接");
                return false;
            }

            // HM方案：SendCommand = 解析坐标 + 生成UDM + 下载 + 打标
            LaserState = LaserState.Marking;
            var result = await _service.AddLinesAndMarkAsync(command, ct);
            if (!result)
            {
                LaserState = LaserState.Idle;
            }
            return result;
        }

        public Task<bool> MarkAsync(CancellationToken ct = default)
        {
            // HM方案中打标已集成在 SendCommandAsync / AddLinesAndMarkAsync 中
            return Task.FromResult(true);
        }

        public Task<bool> StopMarkAsync(CancellationToken ct = default)
        {
            _service.StopMark();
            LaserState = LaserState.Idle;
            return Task.FromResult(true);
        }

        public Task<bool> PauseMarkAsync(CancellationToken ct = default)
        {
            var result = _service.PauseMark();
            if (result) LaserState = LaserState.Paused;
            return Task.FromResult(result);
        }

        public Task<bool> ResumeMarkAsync(CancellationToken ct = default)
        {
            var result = _service.ResumeMark();
            if (result) LaserState = LaserState.Marking;
            return Task.FromResult(result);
        }

        public Task<bool> RedLightPreviewAsync(CancellationToken ct = default)
        {
            var result = _service.SetRedLight(true);
            if (result) LaserState = LaserState.Previewing;
            return Task.FromResult(result);
        }

        public Task<bool> StopRedLightAsync(CancellationToken ct = default)
        {
            var result = _service.SetRedLight(false);
            if (result) LaserState = LaserState.Idle;
            return Task.FromResult(result);
        }

        #endregion

        #region 文件和图层操作

        public Task<bool> LoadFileAsync(string filePath, CancellationToken ct = default)
        {
            // HM方案使用UDM内存缓冲区，不加载外部文件
            RaiseError("HM方案不支持加载外部文件");
            return Task.FromResult(false);
        }

        public Task<bool> ClearContentAsync(CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> SetOffsetAsync(int layerId, float offsetX, float offsetY, CancellationToken ct = default)
        {
            var result = _service.SetOffset(offsetX, offsetY);
            return Task.FromResult(result);
        }

        public Task<bool> MarkLayerAsync(int layerId, CancellationToken ct = default)
        {
            // HM方案中图层打标由UDM文件控制
            return Task.FromResult(true);
        }

        public Task<bool> SetLaserPowerAsync(int layerId, int power, CancellationToken ct = default)
        {
            // HM方案中能量在UDM图层参数中设置
            return Task.FromResult(true);
        }

        #endregion

        #region 线段切割（核心功能）

        public Task<bool> AddCutLineAsync(string coordinates, CancellationToken ct = default)
        {
            return SendCommandAsync(coordinates, ct);
        }

        public Task<bool> AddCutLineAndMarkAsync(string coordinates, CancellationToken ct = default)
        {
            return SendCommandAsync(coordinates, ct);
        }

        public Task<bool> AddCutLinesAsync(IEnumerable<(float x1, float y1, float x2, float y2)> lines, CancellationToken ct = default)
        {
            throw new NotImplementedException("请使用坐标字符串格式");
        }

        /// <summary>
        /// ★★★ 核心新增：多Pass打标 ★★★
        /// 
        /// 将 LaserPassParam[] 转换为 MarkParameter[]，
        /// 基于 HM_LaserConfig 的基础参数，只覆盖速度/频率/能量，
        /// 然后调用 HM_LaserService 的多图层打标方法。
        /// </summary>
        public async Task<bool> AddLinesAndMarkMultiPassAsync(
            MultiPassMarkRequest request,
            CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("激光器未连接");
                return false;
            }

            if (request == null || request.PassParams == null || request.PassParams.Length == 0)
            {
                RaiseError("多Pass请求参数无效");
                return false;
            }

            if (string.IsNullOrEmpty(request.Coordinates))
            {
                RaiseError("坐标数据为空");
                return false;
            }

            try
            {
                // 1. 获取基础 MarkParameter（来自 HM_LaserConfig 的工程校准值）
                MarkParameter baseParam = _service.Config.ToMarkParameter();

                // 2. 构建 MarkParameter 数组（每个Pass覆盖速度/频率/能量）
                var layerParams = new MarkParameter[request.PassParams.Length];
                for (int i = 0; i < request.PassParams.Length; i++)
                {
                    layerParams[i] = request.PassParams[i].ToMarkParameter(baseParam);
                }

                _logService.Information(
                    "[HM_LaserAdapter] 多Pass打标: {PassCount}个Pass, 间隔{Interval}ms",
                    layerParams.Length, request.IntervalMs);

                // 3. 调用 HM_LaserService 多Pass方法
                LaserState = LaserState.Marking;

                var result = await _service.AddLinesAndMarkMultiPassAsync(
                    request.Coordinates,
                    layerParams,
                    request.IntervalMs,
                    ct);

                if (!result)
                {
                    LaserState = LaserState.Idle;
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                LaserState = LaserState.Idle;
                throw;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                LaserState = LaserState.Error;
                RaiseError(string.Format("多Pass打标异常: {0}", ex.Message));
                return false;
            }
        }

        #endregion

        #region 区域填充

        public async Task<bool> FillAreaAsync(string areaCoordinates, CancellationToken ct = default)
        {
            if (!IsConnected)
            {
                RaiseError("激光器未连接");
                return false;
            }

            LaserState = LaserState.Marking;
            var result = await _service.FillAreaAndMarkAsync(areaCoordinates, ct);
            if (!result) LaserState = LaserState.Idle;
            return result;
        }

        #endregion

        #region 队列管理（HM方案不使用命令队列）

        public void EnqueueCommand(string command) { }
        public void ClearQueue() { }
        public List<string> GetQueuedCommands() => new List<string>();

        #endregion

        #region 安全检查

        public bool IsInWorkArea(float x, float y)
        {
            return _service.Config.IsInWorkArea(x, y);
        }

        public (double minX, double minY, double maxX, double maxY) GetCoordinateBounds(string coordinates)
        {
            // 简单实现：返回工作区域边界
            return (_service.Config.WorkAreaMinX, _service.Config.WorkAreaMinY,
                    _service.Config.WorkAreaMaxX, _service.Config.WorkAreaMaxY);
        }

        #endregion

        #region 事件处理

        private void OnServiceConnectionChanged(object sender, bool connected)
        {
            ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs
            {
                DeviceId = DeviceId,
                DeviceName = DeviceName,
                OldState = connected ? DeviceConnectionState.Disconnected : DeviceConnectionState.Connected,
                NewState = connected ? DeviceConnectionState.Connected : DeviceConnectionState.Disconnected
            });
        }

        private void OnServiceMarkFinished(object sender, HM_MarkFinishedEventArgs e)
        {
            LaserState = LaserState.Idle;
            MarkFinished?.Invoke(this, new FrameworkLaserMarkFinishedEventArgs
            {
                Success = e.Success,
                Message = e.Message,
                ElapsedMs = e.ElapsedMs,
                FinishTime = DateTime.Now
            });
        }

        private void OnServiceProgressChanged(object sender, int progress)
        {
            // 进度通过 MarkProgress 属性暴露
        }

        private void OnServiceStatusChanged(object sender, string status)
        {
            StatusChanged?.Invoke(this, status);
        }

        private void OnServiceError(object sender, string error)
        {
            _lastError = error;
            LaserState = LaserState.Error;
            RaiseError(error);
        }

        private void RaiseError(string message)
        {
            ErrorOccurred?.Invoke(this, new DeviceErrorEventArgs
            {
                DeviceId = DeviceId,
                ErrorMessage = message
            });
            _logService?.Error("[HM_LaserAdapter] {Error}", message);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            if (IsMarking)
            {
                try { _service.StopMark(); }
                catch { }
            }

            // 取消订阅事件
            _service.ConnectionChanged -= OnServiceConnectionChanged;
            _service.MarkFinished -= OnServiceMarkFinished;
            _service.ProgressChanged -= OnServiceProgressChanged;
            _service.StatusChanged -= OnServiceStatusChanged;
            _service.ErrorOccurred -= OnServiceError;

            _disposed = true;
            _logService?.Information("[HM_LaserAdapter] 适配器已释放");
        }

        #endregion
    }
}