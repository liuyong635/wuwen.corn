using System;
using System.Threading.Tasks;

namespace SeedCut.Services
{
    /// <summary>
    /// Vision Master 视觉服务接口
    /// </summary>
    public interface IVisionService : IDisposable
    {
        /// <summary>
        /// 当前是否已加载方案
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>
        /// 当前方案路径
        /// </summary>
        string CurrentSolutionPath { get; }

        /// <summary>
        /// 加载方案
        /// </summary>
        Task<VisionResult> LoadSolutionAsync(string path, string password = "");

        /// <summary>
        /// 保存方案
        /// </summary>
        Task<VisionResult> SaveSolutionAsync();

        /// <summary>
        /// 执行视觉流程
        /// </summary>
        Task<VisionResult> ProcessVisionAsync();

        /// <summary>
        /// 关闭当前方案
        /// </summary>
        void CloseSolution();

        /// <summary>
        /// 获取方案版本
        /// </summary>
        Task<string> GetSolutionVersionAsync(string path, string password = "");

        /// <summary>
        /// 获取方案路径
        /// </summary>
        string GetSolutionPath();

        /// <summary>
        /// 检查方案是否有密码
        /// </summary>
        Task<bool> HasPasswordAsync(string path);

        /// <summary>
        /// 方案加载完成事件
        /// </summary>
        event EventHandler<VisionResult> LoadCompleted;

        /// <summary>
        /// 视觉处理完成事件
        /// </summary>
        event EventHandler<VisionResult> ProcessCompleted;
    }

    /// <summary>
    /// 视觉处理结果
    /// </summary>
    public class VisionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int ElapsedMilliseconds { get; set; }
        public Exception Exception { get; set; }

        public VisionResult(bool success, string message, int elapsed = 0)
        {
            Success = success;
            Message = message;
            ElapsedMilliseconds = elapsed;
        }

        public static VisionResult FromSuccess(string message, int elapsed = 0)
        {
            return new VisionResult(true, message, elapsed);
        }

        public static VisionResult FromError(string message, Exception ex = null)
        {
            return new VisionResult(false, message, 0) { Exception = ex };
        }
    }
}