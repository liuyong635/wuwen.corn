using System.Runtime.InteropServices;

namespace SeedCut.Services.HM
{
    /// <summary>
    /// 坐标点结构
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct structUdmPos
    {
        /// <summary>
        /// X坐标 (mm)
        /// </summary>
        public float x;

        /// <summary>
        /// Y坐标 (mm)
        /// </summary>
        public float y;

        /// <summary>
        /// Z坐标 (mm)，2D打标时设为0
        /// </summary>
        public float z;

        /// <summary>
        /// 预留参数，设为0
        /// </summary>
        public float a;

        /// <summary>
        /// 创建2D坐标点
        /// </summary>
        public static structUdmPos Create2D(float x, float y)
        {
            return new structUdmPos { x = x, y = y, z = 0, a = 0 };
        }

        /// <summary>
        /// 创建3D坐标点
        /// </summary>
        public static structUdmPos Create3D(float x, float y, float z)
        {
            return new structUdmPos { x = x, y = y, z = z, a = 0 };
        }

        public override string ToString()
        {
            return string.Format("({0:F2}, {1:F2}, {2:F2})", x, y, z);
        }
    }

    /// <summary>
    /// 打标参数结构
    /// 注意：结构体布局必须与DLL中的定义完全一致
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MarkParameter
    {
        #region 振镜参数

        /// <summary>
        /// 打标速度 (mm/s)
        /// </summary>
        public uint MarkSpeed;

        /// <summary>
        /// 跳转速度 (mm/s)
        /// </summary>
        public uint JumpSpeed;

        /// <summary>
        /// 打标延时 (us)
        /// </summary>
        public uint MarkDelay;

        /// <summary>
        /// 跳转延时 (us)
        /// </summary>
        public uint JumpDelay;

        /// <summary>
        /// 转弯延时 (us)
        /// </summary>
        public uint PolygonDelay;

        /// <summary>
        /// 打标次数
        /// </summary>
        public uint MarkCount;

        #endregion

        #region 激光参数

        /// <summary>
        /// 开激光延时 (us)
        /// </summary>
        public float LaserOnDelay;

        /// <summary>
        /// 关激光延时 (us)
        /// </summary>
        public float LaserOffDelay;

        /// <summary>
        /// 首脉冲抑制延时 (us)
        /// </summary>
        public float FPKDelay;

        /// <summary>
        /// 首脉冲抑制长度 (us)
        /// </summary>
        public float FPKLength;

        /// <summary>
        /// 出光Q频率延时 (us)
        /// </summary>
        public float QDelay;

        /// <summary>
        /// 出光占空比 (0~1)
        /// </summary>
        public float DutyCycle;

        /// <summary>
        /// 出光频率 (kHz)
        /// </summary>
        public float Frequency;

        /// <summary>
        /// 不出光Q频率 (kHz)
        /// </summary>
        public float StandbyFrequency;

        /// <summary>
        /// 不出光Q占空比 (0~1)
        /// </summary>
        public float StandbyDutyCycle;

        /// <summary>
        /// 激光能量 (0~100, 50表示50%)
        /// </summary>
        public float LaserPower;

        #endregion

        #region 模式设置

        /// <summary>
        /// 模拟量模式：0=不使用，1=使用模拟量控制能量(0~10V)
        /// </summary>
        public uint AnalogMode;

        /// <summary>
        /// SPI激光器波形号 (0~63)
        /// </summary>
        public uint Waveform;

        /// <summary>
        /// MOPA脉宽模式：0=不开启，1=开启
        /// </summary>
        public uint PulseWidthMode;

        /// <summary>
        /// MOPA脉宽值 (ns)
        /// </summary>
        public uint PulseWidth;

        #endregion

        /// <summary>
        /// 创建默认参数
        /// </summary>
        public static MarkParameter CreateDefault()
        {
            return new MarkParameter
            {
                // 振镜参数
                MarkSpeed = 1000,
                JumpSpeed = 3000,
                MarkDelay = 100,
                JumpDelay = 100,
                PolygonDelay = 50,
                MarkCount = 1,

                // 激光参数
                LaserOnDelay = 10,
                LaserOffDelay = 10,
                FPKDelay = 0,
                FPKLength = 0,
                QDelay = 0,
                DutyCycle = 0.5f,
                Frequency = 50,
                StandbyFrequency = 50,
                StandbyDutyCycle = 0.02f,
                LaserPower = 50,

                // 模式设置
                AnalogMode = 0,
                Waveform = 0,
                PulseWidthMode = 0,
                PulseWidth = 0
            };
        }

        public override string ToString()
        {
            return string.Format(
                "MarkPara[Speed={0}mm/s, Power={1}%, Freq={2}kHz, Count={3}]",
                MarkSpeed, LaserPower, Frequency, MarkCount);
        }
    }

    

    /// <summary>
    /// HM激光器连接状态
    /// </summary>
    public enum HM_ConnectionStatus
    {
        /// <summary>
        /// 已连接
        /// </summary>
        Connected = 0,

        /// <summary>
        /// 可连接（设备就绪）
        /// </summary>
        Ready = 1,

        /// <summary>
        /// 不可用
        /// </summary>
        NotAvailable = 2
    }
}
