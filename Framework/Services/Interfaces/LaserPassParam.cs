using SeedCut.Services.HM;

namespace SeedCut.Framework.Services.Interfaces
{
    /// <summary>
    /// 单次激光切割Pass的可调参数
    /// 
    /// 设计思路：
    /// - 只暴露配方中需要逐次调整的3个核心参数（速度/频率/能量）
    /// - 其余参数（延时、占空比等）沿用 HM_LaserConfig 中工程师校准的基础值
    /// - 通过 ToMarkParameter(baseParam) 合并为完整的 MarkParameter
    /// </summary>
    public class LaserPassParam
    {
        /// <summary>
        /// 打标速度 (mm/s)
        /// </summary>
        public uint MarkSpeed { get; set; } = 1000;

        /// <summary>
        /// 频率 (kHz)
        /// </summary>
        public float Frequency { get; set; } = 50f;

        /// <summary>
        /// 能量 (0~100%)
        /// </summary>
        public float LaserPower { get; set; } = 50f;

        /// <summary>
        /// 基于基础 MarkParameter 生成完整图层参数。
        /// 只覆盖3个可调字段，其余沿用基础值（struct 值拷贝，不影响原始对象）。
        /// </summary>
        /// <param name="baseParam">来自 HM_LaserConfig.ToMarkParameter() 的基础参数</param>
        /// <returns>填充了本Pass参数的完整 MarkParameter</returns>
        public MarkParameter ToMarkParameter(MarkParameter baseParam)
        {
            var result = baseParam;  // struct 值拷贝
            result.MarkSpeed = MarkSpeed;
            result.Frequency = Frequency;
            result.StandbyFrequency = Frequency;  // 待机频率跟随出光频率
            result.LaserPower = LaserPower;
            return result;
        }

        public override string ToString()
        {
            return string.Format("Pass[Speed={0}mm/s, Freq={1}kHz, Power={2}%]",
                MarkSpeed, Frequency, LaserPower);
        }
    }

    /// <summary>
    /// 多Pass打标请求参数
    /// </summary>
    public class MultiPassMarkRequest
    {
        /// <summary>
        /// 坐标字符串（与现有 AddLines[...] 格式一致）
        /// </summary>
        public string Coordinates { get; set; }

        /// <summary>
        /// 每个Pass的参数数组，长度 = 切割次数 (1~5)
        /// </summary>
        public LaserPassParam[] PassParams { get; set; }

        /// <summary>
        /// Pass之间的等待时间(ms)，0=不等待。
        /// 写入 UDM_Wait 指令，由控制卡硬件计时。
        /// </summary>
        public int IntervalMs { get; set; }
    }
}
