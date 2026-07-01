using System;
using System.IO;
using Serilog;
using SeedCut.Framework.Models;
using SeedCut.Framework.Services.Interfaces;

namespace SeedCut.Framework.Services.Logging
{
    /// <summary>
    /// 基于Serilog的日志服务实现
    /// </summary>
    public class SerilogService : ILogService
    {
        private readonly Serilog.ILogger _logger;
        private readonly Serilog.ILogger _alarmLogger;
        private bool _disposed;

        public SerilogService()
        {
            var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDirectory);

            // 主日志记录器
            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    Path.Combine(logDirectory, "app_.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            // 报警专用日志记录器
            _alarmLogger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(logDirectory, "alarm_.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 90,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}")
                .CreateLogger();

            _logger.Information("日志服务已初始化，日志目录: {LogDirectory}", logDirectory);
        }

        /// <summary>
        /// 自定义日志目录的构造函数
        /// </summary>
        public SerilogService(string logDirectory)
        {
            if (string.IsNullOrEmpty(logDirectory))
            {
                logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            }

            Directory.CreateDirectory(logDirectory);

            // 主日志记录器
            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    Path.Combine(logDirectory, "app_.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            // 报警专用日志记录器
            _alarmLogger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(logDirectory, "alarm_.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 90,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}")
                .CreateLogger();

            _logger.Information("日志服务已初始化，日志目录: {LogDirectory}", logDirectory);
        }

        #region 基本日志方法

        public void Verbose(string message, params object[] args)
        {
            _logger.Verbose(message, args);
        }

        public void Debug(string message, params object[] args)
        {
            _logger.Debug(message, args);
        }

        public void Information(string message, params object[] args)
        {
            _logger.Information(message, args);
        }

        public void Warning(string message, params object[] args)
        {
            _logger.Warning(message, args);
        }

        public void Error(string message, params object[] args)
        {
            _logger.Error(message, args);
        }

        public void Error(Exception ex, string message, params object[] args)
        {
            _logger.Error(ex, message, args);
        }

        public void Fatal(string message, params object[] args)
        {
            _logger.Fatal(message, args);
        }

        public void Fatal(Exception ex, string message, params object[] args)
        {
            _logger.Fatal(ex, message, args);
        }

        #endregion

        #region 报警专用日志方法

        public void LogAlarmTriggered(AlarmItem alarm)
        {
            if (alarm == null) return;

            var message = string.Format("[触发] [{0}] {1} | 级别:{2} | 模块:{3} | 来源:{4}",
                alarm.AlarmCode,
                alarm.Name,
                alarm.Level.GetDisplayName(),
                alarm.SourceModule ?? "未知",
                alarm.SourceIdentifier ?? "N/A");

            _alarmLogger.Warning(message);
            _logger.Warning("报警触发: {AlarmCode} - {AlarmName}", alarm.AlarmCode, alarm.Name);
        }

        public void LogAlarmRecovered(AlarmItem alarm)
        {
            if (alarm == null) return;

            var message = string.Format("[恢复] [{0}] {1} | 持续:{2} | 模块:{3}",
                alarm.AlarmCode,
                alarm.Name,
                alarm.DurationText,
                alarm.SourceModule ?? "未知");

            _alarmLogger.Information(message);
            _logger.Information("报警恢复: {AlarmCode} - {AlarmName}, 持续: {Duration}",
                alarm.AlarmCode, alarm.Name, alarm.DurationText);
        }

        public void LogAlarmAcknowledged(AlarmItem alarm, string userName)
        {
            if (alarm == null) return;

            var message = string.Format("[确认] [{0}] {1} | 确认人:{2} | 模块:{3}",
                alarm.AlarmCode,
                alarm.Name,
                userName ?? "Unknown",
                alarm.SourceModule ?? "未知");

            _alarmLogger.Information(message);
            _logger.Information("报警确认: {AlarmCode} - {AlarmName}, 确认人: {UserName}",
                alarm.AlarmCode, alarm.Name, userName);
        }

        #endregion

        public void Flush()
        {
            Log.CloseAndFlush();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Flush();
                _disposed = true;
            }
        }
    }
}