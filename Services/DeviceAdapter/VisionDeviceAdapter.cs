using SeedCut.Framework.Config;
using SeedCut.Framework.Core;
using SeedCut.Framework.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VM.Core;
using VM.PlatformSDKCS;
using IMVSGroupCs;
using IMVSBlobFindModuCs;

namespace SeedCut.Services.DeviceAdapter
{
    /// <summary>
    /// Vision Master 设备适配器
    /// 将 IVisionService 封装为 IVisionDevice，供 DeviceManager 统一管理
    /// 
    /// 连接语义：Connect = 加载方案，Disconnect = 关闭方案
    /// </summary>
    public class VisionDeviceAdapter : IVisionDevice
    {
        private readonly IVisionService _visionService;
        private readonly VisionDeviceConfig _config;
        private byte[] _lastImage;
        private string _lastError;
        private bool _disposed;
        private VmProcedure _lastProcedure;  // 保存最后执行的流程对象

        #region 构造函数

        /// <summary>
        /// 创建 VisionDeviceAdapter（使用默认配置路径）
        /// </summary>
        public VisionDeviceAdapter(string deviceId, IVisionService visionService)
            : this(deviceId, visionService, VisionDeviceConfig.Load())
        {
        }

        /// <summary>
        /// 创建 VisionDeviceAdapter（指定配置文件路径）
        /// </summary>
        public VisionDeviceAdapter(string deviceId, IVisionService visionService, string configPath)
            : this(deviceId, visionService, VisionDeviceConfig.Load(configPath))
        {
        }

        /// <summary>
        /// 创建 VisionDeviceAdapter（直接注入配置）
        /// </summary>
        public VisionDeviceAdapter(string deviceId, IVisionService visionService, VisionDeviceConfig config)
        {
            _visionService = visionService ?? throw new ArgumentNullException(nameof(visionService));
            _config = config ?? VisionDeviceConfig.CreateDefault();
        }

        #endregion

        #region IDevice 属性

        public string DeviceId => "Vision";

        public string DeviceName => "Vision Master";

        public DeviceType DeviceType => DeviceType.Vision;

        /// <summary>
        /// 连接状态：方案已加载视为已连接
        /// </summary>
        public DeviceConnectionState ConnectionState =>
            _visionService.IsLoaded ? DeviceConnectionState.Connected : DeviceConnectionState.Disconnected;

        /// <summary>
        /// 是否已连接：方案已加载视为已连接
        /// </summary>
        public bool IsConnected => _visionService.IsLoaded;

        public string LastError => _lastError;

        #endregion

        #region IDevice 事件

        public event EventHandler<DeviceConnectionChangedEventArgs> ConnectionChanged;
        public event EventHandler<DeviceErrorEventArgs> ErrorOccurred;

        #endregion

        #region IDevice 方法
        public dynamic GetLastProcedure() => _lastProcedure;

        /// <summary>
        /// 连接：根据配置自动加载方案
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            // 如果已加载，直接返回成功
            if (_visionService.IsLoaded)
            {
                return true;
            }

            // 根据配置决定是否自动加载方案
            if (!_config.AutoLoadOnConnect)
            {
                return true;
            }

            // 加载方案
            return await LoadSolutionAsync(_config.SolutionPath, _config.Password, ct);
        }

        /// <summary>
        /// 断开：关闭当前方案
        /// </summary>
        public Task DisconnectAsync()
        {
            CloseSolution();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 重置：关闭并重新准备
        /// </summary>
        public Task<bool> ResetAsync()
        {
            CloseSolution();
            _lastError = null;
            return Task.FromResult(true);
        }

        /// <summary>
        /// 健康检查：检查加密狗状态
        /// </summary>
        public Task<bool> CheckHealthAsync(CancellationToken ct = default)
        {
            try
            {
                var result = VisionMasterService.CheckVisionMasterDongle();
                return Task.FromResult(result.Status == ModuleStatus.Success);
            }
            catch
            {
                return Task.FromResult(false);
            }
        }

        #endregion

        #region IVisionDevice 属性

        public bool IsBusy { get; private set; }

        public bool IsSolutionLoaded => _visionService.IsLoaded;

        /// <summary>
        /// 当前方案路径（从配置获取）
        /// </summary>
        public string CurrentSolutionPath => _config.SolutionPath;

        #endregion

        #region IVisionDevice 方案操作

        public async Task<bool> LoadSolutionAsync(string solutionPath, string password = "", CancellationToken ct = default)
        {
            try
            {
                IsBusy = true;
                var wasConnected = IsConnected;

                var result = await _visionService.LoadSolutionAsync(solutionPath, password);

                if (!result.Success)
                {
                    _lastError = result.Message;
                    RaiseErrorOccurred(result.Message);
                }
                else
                {
                    // 方案加载成功，触发连接状态变更
                    if (!wasConnected)
                    {
                        RaiseConnectionChanged(DeviceConnectionState.Connected);
                    }
                }

                return result.Success;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                RaiseErrorOccurred(ex.Message);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task<bool> SaveSolutionAsync(CancellationToken ct = default)
        {
            if (!IsSolutionLoaded)
            {
                _lastError = "未加载方案";
                return false;
            }

            try
            {
                var result = await _visionService.SaveSolutionAsync();

                if (!result.Success)
                {
                    _lastError = result.Message;
                }

                return result.Success;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
        }

        public void CloseSolution()
        {
            try
            {
                var wasConnected = IsConnected;

                // ★ 新增：停止所有连续执行的流程
                lock (_continuousRunLock)
                {
                    foreach (var procedureName in _continuousRunningProcedures.ToArray())
                    {
                        try
                        {
                            var procedure = VmSolution.Instance[procedureName] as VmProcedure;
                            if (procedure != null)
                            {
                                procedure.ContinuousRunEnable = false;
                            }
                        }
                        catch { /* 忽略单个流程停止失败 */ }
                    }
                    _continuousRunningProcedures.Clear();
                }

                _visionService.CloseSolution();
                _lastImage = null;

                // 方案关闭，触发连接状态变更
                if (wasConnected)
                {
                    RaiseConnectionChanged(DeviceConnectionState.Disconnected);
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
        }

        #endregion

        #region IVisionDevice 执行操作

        public Task<VisionDeviceResult> ExecuteAsync(string procedureName, CancellationToken ct = default)
        {
            return ExecuteAsync(procedureName, null, ct);
        }

        public async Task<VisionDeviceResult> ExecuteAsync(string procedureName, object parameters, CancellationToken ct = default)
        {
            if (!IsSolutionLoaded)
            {
                return VisionDeviceResult.FromError("未加载方案");
            }

            try
            {
                IsBusy = true;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // 获取并执行流程
                if (!string.IsNullOrEmpty(procedureName))
                {
                    var procedure = VmSolution.Instance[procedureName] as VmProcedure;
                    if (procedure == null)
                    {
                        return VisionDeviceResult.Fail(-1, "流程不存在: " + procedureName);
                    }

                    // 设置输入参数
                    SetParameters(procedure, parameters);

                    // 执行流程
                    procedure.Run();

                    sw.Stop();

                    // ★ 关键修改：构建结果时提取 Group 模块输出
                    return BuildResult(procedure, procedureName, sw.Elapsed);
                }
                else
                {
                    // 执行整个方案
                    VmSolution.Instance.SyncRun();
                    sw.Stop();

                    return VisionDeviceResult.Ok(new Dictionary<string, object>
                    {
                        ["ExecutionTime"] = sw.ElapsedMilliseconds
                    });
                }
            }
            catch (VmException vmEx)
            {
                _lastError = string.Format("执行失败 (0x{0:X})", vmEx.errorCode);
                return VisionDeviceResult.Fail(vmEx.errorCode, _lastError);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return VisionDeviceResult.FromError(ex.Message, ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void SetParameters(VmProcedure procedure, object parameters)
        {
            if (parameters == null) return;

            var param = procedure.ModuParams;
            if (param == null) return;

            if (parameters is IDictionary<string, object> dict)
            {
                foreach (var kvp in dict)
                {
                    try
                    {
                        if (kvp.Value is ImageBaseData img)
                        {
                            param.SetInputImage_V2(kvp.Key, img);
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// 构建视觉结果（★ 关键修改：提取 Group 模块输出）
        /// </summary>
        /// <param name="procedure">流程对象</param>
        /// <param name="procedureName">流程名称</param>
        /// <param name="elapsed">执行耗时</param>
        private VisionDeviceResult BuildResult(VmProcedure procedure, string procedureName, TimeSpan elapsed)
        {
            var result = VisionDeviceResult.Ok();
            result.ExecutionTime = elapsed;
            result.Results["ProcedureName"] = procedureName;

            _lastProcedure = procedure;  // 保存引用，供Handler提取

            return result;
        }

        #endregion

        #region IVisionDevice 图像操作

        public async Task<byte[]> TriggerCaptureAsync(CancellationToken ct = default)
        {
            if (!IsSolutionLoaded) return null;

            try
            {
                IsBusy = true;
                VmSolution.Instance.SyncRun();

                // 从第一个流程获取图像
                var names = GetProcedureNames().ToList();
                if (names.Count > 0)
                {
                    var procedure = VmSolution.Instance[names[0]] as VmProcedure;
                    var img = procedure?.ModuResult?.GetOutputImageV2("ImageData0");
                    if (img?.ImageData != null)
                    {
                        _lastImage = img.ImageData;
                        return _lastImage;
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public byte[] GetLastImage() => _lastImage;

        public byte[] GetProcedureImage(string procedureName)
        {
            if (!IsSolutionLoaded || string.IsNullOrEmpty(procedureName))
            {
                return _lastImage;
            }

            try
            {
                var procedure = VmSolution.Instance[procedureName] as VmProcedure;
                var img = procedure?.ModuResult?.GetOutputImageV2("ImageData0");
                return img?.ImageData ?? _lastImage;
            }
            catch
            {
                return _lastImage;
            }
        }

        #endregion

        #region IVisionDevice 流程查询

        public IEnumerable<string> GetProcedureNames()
        {
            if (!IsSolutionLoaded)
            {
                return Enumerable.Empty<string>();
            }

            try
            {
                // 使用 GetAllProcedureObjects 获取流程对象列表
                var procedureList = new List<VmProcedure>();
                VmSolution.Instance.GetAllProcedureObjects(ref procedureList);

                // 从流程对象获取名称
                return procedureList
                    .Where(p => p != null)
                    .Select(p => p.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();
            }
            catch
            {
                return Enumerable.Empty<string>();
            }
        }

        #endregion

        #region 事件触发

        private void RaiseConnectionChanged(DeviceConnectionState newState)
        {
            ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs
            {
                DeviceId = DeviceId,
                DeviceName = DeviceName,
                OldState = newState == DeviceConnectionState.Connected
                    ? DeviceConnectionState.Disconnected
                    : DeviceConnectionState.Connected,
                NewState = newState,
                Message = newState == DeviceConnectionState.Connected
                    ? "方案已加载"
                    : "方案已关闭"
            });
        }

        private void RaiseErrorOccurred(string message)
        {
            ErrorOccurred?.Invoke(this, new DeviceErrorEventArgs
            {
                DeviceId = DeviceId,
                DeviceName = DeviceName,
                ErrorMessage = message,
                IsCritical = false
            });
        }

        #endregion


        #region IVisionDevice 连续执行

        /// <summary>
        /// 记录正在连续执行的流程名称
        /// </summary>
        private readonly HashSet<string> _continuousRunningProcedures = new HashSet<string>();
        private readonly object _continuousRunLock = new object();

        /// <summary>
        /// 启动流程连续执行（硬触发监听模式）
        /// 
        /// 参考 VM SDK 文档：
        /// - vmProcedure.SetContinousRunInterval(500);  // 设置间隔
        /// - vmProcedure.ContinuousRunEnable = true;    // 启动连续执行
        /// </summary>
        public Task<bool> StartContinuousRunAsync(string procedureName, uint intervalMs = 0, CancellationToken ct = default)
        {
            if (!IsSolutionLoaded)
            {
                _lastError = "未加载方案";
                return Task.FromResult(false);
            }

            if (string.IsNullOrEmpty(procedureName))
            {
                _lastError = "流程名称不能为空";
                return Task.FromResult(false);
            }

            try
            {
                // 获取流程对象
                var procedure = VmSolution.Instance[procedureName] as VmProcedure;
                if (procedure == null)
                {
                    _lastError = $"流程 '{procedureName}' 不存在";
                    return Task.FromResult(false);
                }

                // 设置连续执行间隔（如果指定）
                if (intervalMs > 0)
                {
                    procedure.SetContinousRunInterval(intervalMs);
                }

                // 启动连续执行
                procedure.ContinuousRunEnable = true;

                // 记录状态
                lock (_continuousRunLock)
                {
                    _continuousRunningProcedures.Add(procedureName);
                }

                System.Diagnostics.Debug.WriteLine($"[VisionDeviceAdapter] 流程 '{procedureName}' 连续执行已启动");
                return Task.FromResult(true);
            }
            catch (VmException vmEx)
            {
                _lastError = $"启动连续执行失败 (错误码: {vmEx.errorCode:X}): {vmEx.Message}";
                RaiseErrorOccurred(_lastError);
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                _lastError = $"启动连续执行异常: {ex.Message}";
                RaiseErrorOccurred(_lastError);
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 停止流程连续执行
        /// 
        /// 参考 VM SDK 文档：
        /// - vmProcedure.ContinuousRunEnable = false;  // 停止连续执行
        /// </summary>
        public Task<bool> StopContinuousRunAsync(string procedureName, CancellationToken ct = default)
        {
            if (!IsSolutionLoaded)
            {
                _lastError = "未加载方案";
                return Task.FromResult(false);
            }

            if (string.IsNullOrEmpty(procedureName))
            {
                _lastError = "流程名称不能为空";
                return Task.FromResult(false);
            }

            try
            {
                // 获取流程对象
                var procedure = VmSolution.Instance[procedureName] as VmProcedure;
                if (procedure == null)
                {
                    _lastError = $"流程 '{procedureName}' 不存在";
                    return Task.FromResult(false);
                }

                // 停止连续执行
                procedure.ContinuousRunEnable = false;

                // 更新状态
                lock (_continuousRunLock)
                {
                    _continuousRunningProcedures.Remove(procedureName);
                }

                System.Diagnostics.Debug.WriteLine($"[VisionDeviceAdapter] 流程 '{procedureName}' 连续执行已停止");
                return Task.FromResult(true);
            }
            catch (VmException vmEx)
            {
                _lastError = $"停止连续执行失败 (错误码: {vmEx.errorCode:X}): {vmEx.Message}";
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                _lastError = $"停止连续执行异常: {ex.Message}";
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// 检查流程是否正在连续执行中
        /// </summary>
        public bool IsContinuousRunning(string procedureName)
        {
            if (string.IsNullOrEmpty(procedureName))
            {
                return false;
            }

            lock (_continuousRunLock)
            {
                return _continuousRunningProcedures.Contains(procedureName);
            }
        }

        /// <summary>
        /// 停止所有流程的连续执行
        /// </summary>
        public async Task<bool> StopAllContinuousRunAsync(CancellationToken ct = default)
        {
            if (!IsSolutionLoaded)
            {
                return true; // 未加载方案，无需停止
            }

            var allSuccess = true;
            string[] proceduresToStop;

            // 获取需要停止的流程列表（复制，避免遍历时修改）
            lock (_continuousRunLock)
            {
                proceduresToStop = _continuousRunningProcedures.ToArray();
            }

            // 逐个停止
            foreach (var procedureName in proceduresToStop)
            {
                if (ct.IsCancellationRequested) break;

                var result = await StopContinuousRunAsync(procedureName, ct);
                if (!result)
                {
                    allSuccess = false;
                    System.Diagnostics.Debug.WriteLine($"[VisionDeviceAdapter] 停止流程 '{procedureName}' 失败");
                }
            }

            return allSuccess;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                CloseSolution();
            }
            catch { }

            _lastImage = null;
        }

        #endregion
    }
}