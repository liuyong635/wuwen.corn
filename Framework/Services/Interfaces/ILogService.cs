using System;
using SeedCut.Framework.Models;

namespace SeedCut.Framework.Services.Interfaces
{
    /// <summary>
    /// 日志服务接口
    /// </summary>
    public interface ILogService : IDisposable
    {
        #region 基本日志方法

        /// <summary>
        /// 详细日志（最低级别）
        /// </summary>
        void Verbose(string message, params object[] args);

        /// <summary>
        /// 调试日志
        /// </summary>
        void Debug(string message, params object[] args);

        /// <summary>
        /// 信息日志
        /// </summary>
        void Information(string message, params object[] args);

        /// <summary>
        /// 警告日志
        /// </summary>
        void Warning(string message, params object[] args);

        /// <summary>
        /// 错误日志
        /// </summary>
        void Error(string message, params object[] args);

        /// <summary>
        /// 错误日志（带异常）
        /// </summary>
        void Error(Exception ex, string message, params object[] args);

        /// <summary>
        /// 致命错误日志
        /// </summary>
        void Fatal(string message, params object[] args);

        /// <summary>
        /// 致命错误日志（带异常）
        /// </summary>
        void Fatal(Exception ex, string message, params object[] args);

        #endregion

        #region 报警专用日志方法

        /// <summary>
        /// 记录报警触发
        /// </summary>
        void LogAlarmTriggered(AlarmItem alarm);

        /// <summary>
        /// 记录报警恢复
        /// </summary>
        void LogAlarmRecovered(AlarmItem alarm);

        /// <summary>
        /// 记录报警确认
        /// </summary>
        void LogAlarmAcknowledged(AlarmItem alarm, string userName);

        #endregion

        #region 其他方法

        /// <summary>
        /// 刷新日志缓冲区
        /// </summary>
        void Flush();

        #endregion
    }
}