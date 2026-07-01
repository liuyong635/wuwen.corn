using System;

namespace SeedCut.Models
{
    /// <summary>
    /// 任务日志模型
    /// </summary>
    public class TaskLog
    {
        /// <summary>
        /// 任务编号
        /// </summary>
        public string TaskNumber { get; set; }

        /// <summary>
        /// 任务量
        /// </summary>
        public int TaskQuantity { get; set; }

        /// <summary>
        /// 种子代号
        /// </summary>
        public string SeedCode { get; set; }

        /// <summary>
        /// 切割面积(cm²)
        /// </summary>
        public double CutArea { get; set; }

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 报警信息
        /// </summary>
        public string AlarmInfo { get; set; }

        /// <summary>
        /// 数据路径
        /// </summary>
        public string DataPath { get; set; }

        /// <summary>
        /// 显示用的开始时间
        /// </summary>
        public string StartTimeDisplay => StartTime.ToString("yyyy-MM-dd HH:mm:ss");

        /// <summary>
        /// 显示用的结束时间
        /// </summary>
        public string EndTimeDisplay => EndTime.ToString("yyyy-MM-dd HH:mm:ss");
    }
}