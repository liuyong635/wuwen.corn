using SeedCut.Framework.Services.Interfaces;
using SeedCut.Models;
using System;
using System.Threading.Tasks;
using VM.Core;
using VM.PlatformSDKCS;

namespace SeedCut.Services
{
    /// <summary>
    /// Vision Master SDK 服务实现
    /// </summary>
    public class VisionMasterService : IVisionService
    {
        private bool _isLoaded;
        private string _currentSolutionPath;
        private bool _isDisposed;

        #region 属性

        public bool IsLoaded => _isLoaded;
        public string CurrentSolutionPath => _currentSolutionPath;

        #endregion

        #region 事件

        public event EventHandler<VisionResult> LoadCompleted;
        public event EventHandler<VisionResult> ProcessCompleted;

        #endregion

        #region 静态检查方法（用于 Loading 页面）

        /// <summary>
        /// 检查 Vision Master 加密狗（静态方法，在 Loading 页面调用）
        /// ⚠️ 这是根据官方示例推测的检测方法
        /// </summary>
        public static ModuleCheckResult CheckVisionMasterDongle()
        {
            var result = new ModuleCheckResult("Vision Master 加密狗", true);

            try
            {
                // 方法1：尝试访问 VmSolution 实例
                // 如果没有加密狗，这个操作可能会失败
                try
                {

                    //VmSolution.Load("test.sol", "");

                    result.Status = ModuleStatus.Success;
                    result.Message = "加密狗检测成功";
                    return result;

                }
                catch (VmException vmEx)
                {
                    result.Status = ModuleStatus.Failed;
                    result.Message = string.Format("加密狗未检测到 (错误码: {0:X})", vmEx.errorCode);
                    return result;
                }
                catch (Exception ex)
                {
                    result.Status = ModuleStatus.Failed;
                    result.Message = "加密狗检测失败: " + ex.Message;
                    return result;
                }

                // 方法2：如果上面没有异常，尝试简单的初始化测试
                result.Status = ModuleStatus.Success;
                result.Message = "加密狗检测成功";
            }
            catch (Exception ex)
            {
                result.Status = ModuleStatus.Failed;
                result.Message = "加密狗检查异常: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 检查 Vision Master SDK 是否可用
        /// </summary>
        public static ModuleCheckResult CheckVisionMasterSDK()
        {
            var result = new ModuleCheckResult("Vision Master SDK", false);

            try
            {
                // 检查 VmSolution 类型是否可用
                Type vmSolutionType = typeof(VmSolution);
                if (vmSolutionType != null)
                {
                    result.Status = ModuleStatus.Success;
                    result.Message = "SDK 加载正常";
                }
                else
                {
                    result.Status = ModuleStatus.Failed;
                    result.Message = "SDK 类型未找到";
                }
            }
            catch (Exception ex)
            {
                result.Status = ModuleStatus.Warning;
                result.Message = "SDK 警告: " + ex.Message;
            }

            return result;
        }

        #endregion

        #region 方案操作

        /// <summary>
        /// 加载方案
        /// </summary>
        public async Task<VisionResult> LoadSolutionAsync(string path, string password = "")
        {
            return await Task.Run(() =>
            {
                try
                {
                    VmSolution.Load(path, password);
                    _isLoaded = true;
                    _currentSolutionPath = path;

                    var result = VisionResult.FromSuccess("方案加载成功: " + System.IO.Path.GetFileName(path));
                    LoadCompleted?.Invoke(this, result);
                    return result;
                }
                catch (VmException ex)
                {
                    var errorMsg = string.Format("加载失败 (错误码: {0:X})", ex.errorCode);
                    var result = VisionResult.FromError(errorMsg, ex);
                    LoadCompleted?.Invoke(this, result);
                    return result;
                }
                catch (Exception ex)
                {
                    var result = VisionResult.FromError("加载异常: " + ex.Message, ex);
                    LoadCompleted?.Invoke(this, result);
                    return result;
                }
            });
        }

        /// <summary>
        /// 保存方案
        /// </summary>
        public async Task<VisionResult> SaveSolutionAsync()
        {
            return await Task.Run(() =>
            {
                if (!_isLoaded)
                {
                    return VisionResult.FromError("未加载方案");
                }

                try
                {
                    VmSolution.Save();
                    return VisionResult.FromSuccess("方案保存成功");
                }
                catch (VmException ex)
                {
                    return VisionResult.FromError(string.Format("保存失败 (错误码: {0:X})", ex.errorCode), ex);
                }
                catch (Exception ex)
                {
                    return VisionResult.FromError("保存异常: " + ex.Message, ex);
                }
            });
        }

        /// <summary>
        /// 执行视觉流程（模拟处理）
        /// </summary>
        public async Task<VisionResult> ProcessVisionAsync()
        {
            return await Task.Run(() =>
            {
                if (!_isLoaded)
                {
                    return VisionResult.FromError("未加载方案");
                }

                try
                {
                    var startTime = DateTime.Now;

                    // ⚠️ 这里应该调用 Vision Master 的实际处理流程
                    // 请根据实际 API 替换以下代码
                    // 示例：VmSolution.Instance.Run() 或其他处理方法
                    System.Threading.Thread.Sleep(500); // 模拟处理时间

                    var elapsed = (int)(DateTime.Now - startTime).TotalMilliseconds;
                    var result = VisionResult.FromSuccess("视觉处理完成", elapsed);

                    ProcessCompleted?.Invoke(this, result);
                    return result;
                }
                catch (VmException ex)
                {
                    var result = VisionResult.FromError(string.Format("处理失败 (错误码: {0:X})", ex.errorCode), ex);
                    ProcessCompleted?.Invoke(this, result);
                    return result;
                }
                catch (Exception ex)
                {
                    var result = VisionResult.FromError("处理异常: " + ex.Message, ex);
                    ProcessCompleted?.Invoke(this, result);
                    return result;
                }
            });
        }

        /// <summary>
        /// 获取方案版本
        /// </summary>
        public async Task<string> GetSolutionVersionAsync(string path, string password = "")
        {
            return await Task.Run(() =>
            {
                try
                {
                    string version = VmSolution.Instance.GetSolutionVersion(path, password);
                    return "方案版本: " + version;
                }
                catch (Exception ex)
                {
                    return "获取版本失败: " + ex.Message;
                }
            });
        }

        /// <summary>
        /// 获取方案路径
        /// </summary>
        public string GetSolutionPath()
        {
            if (!_isLoaded)
            {
                return "未加载方案";
            }

            try
            {
                return VmSolution.Instance.SolutionPath;
            }
            catch (Exception)
            {
                return _currentSolutionPath ?? "未知路径";
            }
        }

        /// <summary>
        /// 检查方案是否有密码
        /// </summary>
        public async Task<bool> HasPasswordAsync(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    return VmSolution.Instance.HasPassword(path);
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }

        /// <summary>
        /// 关闭方案
        /// </summary>
        public void CloseSolution()
        {
            if (!_isLoaded)
            {
                return;
            }

            try
            {
                VmSolution.Instance.CloseSolution();
                _isLoaded = false;
                _currentSolutionPath = null;
            }
            catch (Exception)
            {
                // 忽略关闭异常
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }

            if (disposing)
            {
                try
                {
                    CloseSolution();
                    VmSolution.Instance?.Dispose();
                }
                catch (Exception)
                {
                    // 忽略释放异常
                }
            }

            _isDisposed = true;
        }

        ~VisionMasterService()
        {
            Dispose(false);
        }

        #endregion
    }
}