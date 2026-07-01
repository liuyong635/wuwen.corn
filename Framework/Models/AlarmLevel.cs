using System;

namespace SeedCut.Framework.Models
{
    /// <summary>
    /// 报警级别枚举
    /// </summary>
    public enum AlarmLevel
    {
        /// <summary>
        /// 信息（蓝色）
        /// </summary>
        Info = 0,

        /// <summary>
        /// 警告（黄色/橙色）
        /// </summary>
        Warning = 1,

        /// <summary>
        /// 错误（红色）- 默认级别
        /// </summary>
        Error = 2,

        /// <summary>
        /// 严重（深红色）
        /// </summary>
        Critical = 3
    }

    /// <summary>
    /// 报警级别扩展方法
    /// </summary>
    public static class AlarmLevelExtensions
    {
        /// <summary>
        /// 获取显示名称
        /// </summary>
        public static string GetDisplayName(this AlarmLevel level)
        {
            switch (level)
            {
                case AlarmLevel.Info: return "信息";
                case AlarmLevel.Warning: return "警告";
                case AlarmLevel.Error: return "错误";
                case AlarmLevel.Critical: return "严重";
                default: return "未知";
            }
        }

        /// <summary>
        /// 获取前景色代码
        /// </summary>
        public static string GetColorCode(this AlarmLevel level)
        {
            switch (level)
            {
                case AlarmLevel.Info: return "#1976D2";      // 蓝色
                case AlarmLevel.Warning: return "#E65100";   // 橙色
                case AlarmLevel.Error: return "#C62828";     // 红色
                case AlarmLevel.Critical: return "#B71C1C";  // 深红色
                default: return "#333333";
            }
        }

        /// <summary>
        /// 获取背景色代码
        /// </summary>
        public static string GetBackgroundColorCode(this AlarmLevel level)
        {
            switch (level)
            {
                case AlarmLevel.Info: return "#E3F2FD";      // 浅蓝
                case AlarmLevel.Warning: return "#FFF3E0";   // 浅橙
                case AlarmLevel.Error: return "#FFEBEE";     // 浅红
                case AlarmLevel.Critical: return "#FFCDD2";  // 深浅红
                default: return "#FFFFFF";
            }
        }

        /// <summary>
        /// 获取图标
        /// </summary>
        public static string GetIcon(this AlarmLevel level)
        {
            switch (level)
            {
                case AlarmLevel.Info: return "ℹ️";
                case AlarmLevel.Warning: return "⚠️";
                case AlarmLevel.Error: return "❌";
                case AlarmLevel.Critical: return "🚨";
                default: return "❓";
            }
        }

        /// <summary>
        /// 是否需要显示弹窗（默认 Error 及以上显示）
        /// </summary>
        public static bool ShouldShowPopup(this AlarmLevel level)
        {
            return level >= AlarmLevel.Error;
        }

        /// <summary>
        /// 是否需要播放声音（默认 Critical 播放）
        /// </summary>
        public static bool ShouldPlaySound(this AlarmLevel level)
        {
            return level >= AlarmLevel.Critical;
        }
    }
}