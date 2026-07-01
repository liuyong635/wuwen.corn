using SeedCut.Services.HM;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace SeedCut.Models
{
    /// <summary>
    /// HM激光器（GMC控制卡）配置（增强版）
    /// 
    /// 新增功能：
    /// 1. 实现 INotifyPropertyChanged，支持数据绑定实时更新
    /// 2. 添加 ConfigChanged 事件，通知其他组件配置已更改
    /// 3. 参数修改后自动标记为"已修改"
    /// 4. 支持配置验证
    /// </summary>
    [XmlRoot("HM_LaserConfig")]
    public class HM_LaserConfig : INotifyPropertyChanged
    {
        #region 事件

        /// <summary>
        /// 属性变更事件（用于UI数据绑定）
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 配置变更事件（用于通知其他组件）
        /// 在保存配置后触发
        /// </summary>
        public event EventHandler ConfigChanged;

        /// <summary>
        /// 配置保存前事件
        /// </summary>
        public event EventHandler<CancelEventArgs> ConfigSaving;



        #endregion

        #region 私有字段

        private string _ipAddress;
        private int _connectionTimeoutMs;
        private int _downloadTimeoutMs;
        private int _markTimeoutMs;

        private uint _markSpeed;
        private uint _jumpSpeed;
        private uint _markDelay;
        private uint _jumpDelay;
        private uint _polygonDelay;
        private uint _markCount;

        private float _laserOnDelay;
        private float _laserOffDelay;
        private float _fpkDelay;
        private float _fpkLength;
        private float _frequency;
        private float _dutyCycle;
        private float _laserPower;
        private float _standbyFrequency;
        private float _standbyDutyCycle;

        private double _workAreaMinX;
        private double _workAreaMaxX;
        private double _workAreaMinY;
        private double _workAreaMaxY;
        private bool _enableCoordinateCheck;

        private int _protocol;
        private int _dimensional;
        private bool _jumpToZeroAfterMark;
        private bool _useAnalogPower;
        private uint _waveform;
        private bool _enableMOPA;
        private uint _mopaPulseWidth;
        private float _fillLineSpacing;

        #endregion

        #region 连接配置

        [XmlElement("IpAddress")]
        public string IpAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }

        [XmlElement("ConnectionTimeoutMs")]
        public int ConnectionTimeoutMs
        {
            get => _connectionTimeoutMs;
            set => SetProperty(ref _connectionTimeoutMs, value);
        }

        [XmlElement("DownloadTimeoutMs")]
        public int DownloadTimeoutMs
        {
            get => _downloadTimeoutMs;
            set => SetProperty(ref _downloadTimeoutMs, value);
        }

        [XmlElement("MarkTimeoutMs")]
        public int MarkTimeoutMs
        {
            get => _markTimeoutMs;
            set => SetProperty(ref _markTimeoutMs, value);
        }

        #endregion

        #region 振镜参数

        [XmlElement("MarkSpeed")]
        public uint MarkSpeed
        {
            get => _markSpeed;
            set => SetProperty(ref _markSpeed, value);
        }

        [XmlElement("JumpSpeed")]
        public uint JumpSpeed
        {
            get => _jumpSpeed;
            set => SetProperty(ref _jumpSpeed, value);
        }

        [XmlElement("MarkDelay")]
        public uint MarkDelay
        {
            get => _markDelay;
            set => SetProperty(ref _markDelay, value);
        }

        [XmlElement("JumpDelay")]
        public uint JumpDelay
        {
            get => _jumpDelay;
            set => SetProperty(ref _jumpDelay, value);
        }

        [XmlElement("PolygonDelay")]
        public uint PolygonDelay
        {
            get => _polygonDelay;
            set => SetProperty(ref _polygonDelay, value);
        }

        [XmlElement("MarkCount")]
        public uint MarkCount
        {
            get => _markCount;
            set => SetProperty(ref _markCount, value);
        }

        #endregion

        #region 激光参数

        [XmlElement("LaserOnDelay")]
        public float LaserOnDelay
        {
            get => _laserOnDelay;
            set => SetProperty(ref _laserOnDelay, value);
        }

        [XmlElement("LaserOffDelay")]
        public float LaserOffDelay
        {
            get => _laserOffDelay;
            set => SetProperty(ref _laserOffDelay, value);
        }

        [XmlElement("FPKDelay")]
        public float FPKDelay
        {
            get => _fpkDelay;
            set => SetProperty(ref _fpkDelay, value);
        }

        [XmlElement("FPKLength")]
        public float FPKLength
        {
            get => _fpkLength;
            set => SetProperty(ref _fpkLength, value);
        }

        [XmlElement("Frequency")]
        public float Frequency
        {
            get => _frequency;
            set => SetProperty(ref _frequency, value);
        }

        [XmlElement("DutyCycle")]
        public float DutyCycle
        {
            get => _dutyCycle;
            set => SetProperty(ref _dutyCycle, Math.Max(0, Math.Min(1, value)));
        }

        [XmlElement("LaserPower")]
        public float LaserPower
        {
            get => _laserPower;
            set => SetProperty(ref _laserPower, Math.Max(0, Math.Min(100, value)));
        }

        [XmlElement("StandbyFrequency")]
        public float StandbyFrequency
        {
            get => _standbyFrequency;
            set => SetProperty(ref _standbyFrequency, value);
        }

        [XmlElement("StandbyDutyCycle")]
        public float StandbyDutyCycle
        {
            get => _standbyDutyCycle;
            set => SetProperty(ref _standbyDutyCycle, Math.Max(0, Math.Min(1, value)));
        }

        #endregion

        #region 工作区域配置

        [XmlElement("WorkAreaMinX")]
        public double WorkAreaMinX
        {
            get => _workAreaMinX;
            set => SetProperty(ref _workAreaMinX, value);
        }

        [XmlElement("WorkAreaMaxX")]
        public double WorkAreaMaxX
        {
            get => _workAreaMaxX;
            set => SetProperty(ref _workAreaMaxX, value);
        }

        [XmlElement("WorkAreaMinY")]
        public double WorkAreaMinY
        {
            get => _workAreaMinY;
            set => SetProperty(ref _workAreaMinY, value);
        }

        [XmlElement("WorkAreaMaxY")]
        public double WorkAreaMaxY
        {
            get => _workAreaMaxY;
            set => SetProperty(ref _workAreaMaxY, value);
        }

        [XmlElement("EnableCoordinateCheck")]
        public bool EnableCoordinateCheck
        {
            get => _enableCoordinateCheck;
            set => SetProperty(ref _enableCoordinateCheck, value);
        }

        #endregion

        #region 高级配置

        [XmlElement("Protocol")]
        public int Protocol
        {
            get => _protocol;
            set => SetProperty(ref _protocol, value);
        }

        [XmlElement("Dimensional")]
        public int Dimensional
        {
            get => _dimensional;
            set => SetProperty(ref _dimensional, value);
        }

        [XmlElement("JumpToZeroAfterMark")]
        public bool JumpToZeroAfterMark
        {
            get => _jumpToZeroAfterMark;
            set => SetProperty(ref _jumpToZeroAfterMark, value);
        }

        [XmlElement("UseAnalogPower")]
        public bool UseAnalogPower
        {
            get => _useAnalogPower;
            set => SetProperty(ref _useAnalogPower, value);
        }

        [XmlElement("Waveform")]
        public uint Waveform
        {
            get => _waveform;
            set => SetProperty(ref _waveform, Math.Min(63, value));
        }

        [XmlElement("EnableMOPA")]
        public bool EnableMOPA
        {
            get => _enableMOPA;
            set => SetProperty(ref _enableMOPA, value);
        }

        [XmlElement("MOPAPulseWidth")]
        public uint MOPAPulseWidth
        {
            get => _mopaPulseWidth;
            set => SetProperty(ref _mopaPulseWidth, value);
        }

        /// <summary>
        /// 填充线间距 (mm)
        /// </summary>
        [XmlElement("FillLineSpacing")]
        public float FillLineSpacing
        {
            get => _fillLineSpacing;
            set => SetProperty(ref _fillLineSpacing, Math.Max(0.01f, value));
        }

        #endregion

        #region 计算属性（不序列化）

        [XmlIgnore]
        public double WorkAreaWidth => WorkAreaMaxX - WorkAreaMinX;

        [XmlIgnore]
        public double WorkAreaHeight => WorkAreaMaxY - WorkAreaMinY;

        [XmlIgnore]
        public double WorkAreaCenterX => (WorkAreaMinX + WorkAreaMaxX) / 2;

        [XmlIgnore]
        public double WorkAreaCenterY => (WorkAreaMinY + WorkAreaMaxY) / 2;

        #endregion

        #region 配置文件管理

        private static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Config",
            "HM_LaserConfig.xml"
        );

        /// <summary>
        /// 加载配置
        /// </summary>
        public static HM_LaserConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var serializer = new XmlSerializer(typeof(HM_LaserConfig));
                    using (var reader = new StreamReader(ConfigPath))
                    {
                        var config = (HM_LaserConfig)serializer.Deserialize(reader);
                        System.Diagnostics.Debug.WriteLine($"[HM_LaserConfig] 从文件加载配置: {ConfigPath}");
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HM_LaserConfig] 加载配置失败: {ex.Message}");
            }

            var defaultConfig = CreateDefault();
            defaultConfig.Save();
            System.Diagnostics.Debug.WriteLine($"[HM_LaserConfig] 已创建默认配置: {ConfigPath}");
            return defaultConfig;
        }

        /// <summary>
        /// 保存配置到XML文件
        /// </summary>
        public bool Save()
        {
            try
            {
                // 触发保存前事件，允许取消
                var cancelArgs = new CancelEventArgs();
                ConfigSaving?.Invoke(this, cancelArgs);
                if (cancelArgs.Cancel)
                {
                    System.Diagnostics.Debug.WriteLine("[HM_LaserConfig] 保存被取消");
                    return false;
                }

                var directory = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var serializer = new XmlSerializer(typeof(HM_LaserConfig));
                using (var writer = new StreamWriter(ConfigPath))
                {
                    serializer.Serialize(writer, this);
                }

                System.Diagnostics.Debug.WriteLine($"[HM_LaserConfig] 配置已保存: {ConfigPath}");

                // 触发配置变更事件
                ConfigChanged?.Invoke(this, EventArgs.Empty);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HM_LaserConfig] 保存配置失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 创建默认配置
        /// </summary>
        public static HM_LaserConfig CreateDefault()
        {
            return new HM_LaserConfig
            {
                // 连接配置
                _ipAddress = "192.168.0.227",
                _connectionTimeoutMs = 5000,
                _downloadTimeoutMs = 10000,
                _markTimeoutMs = 120000,

                // 振镜参数
                _markSpeed = 72,
                _jumpSpeed = 2000,
                _markDelay = 0,
                _jumpDelay = 0,
                _polygonDelay = 0,
                _markCount = 3,

                // 激光参数（CO2激光器）
                _laserOnDelay = 500,
                _laserOffDelay = 110,
                _fpkDelay = 0,
                _fpkLength = 0,
                _frequency = 5,
                _dutyCycle = 0.5f,
                _laserPower = 5,
                _standbyFrequency = 0,
                _standbyDutyCycle = 0,

                // 工作区域
                _workAreaMinX = -100,
                _workAreaMaxX = 100,
                _workAreaMinY = -100,
                _workAreaMaxY = 100,
                _enableCoordinateCheck = true,

                // 高级配置
                _protocol = HM_Constants.Protocol_XY2_100,
                _dimensional = HM_Constants.Dimensional_2D,
                _jumpToZeroAfterMark = true,
                _useAnalogPower = false,
                _waveform = 0,
                _enableMOPA = false,
                _mopaPulseWidth = 0,

                // 填充参数
                _fillLineSpacing = 0.1f
            };
        }

        /// <summary>
        /// 创建测试配置（小范围，低功率）
        /// </summary>
        public static HM_LaserConfig CreateTestConfig()
        {
            var config = CreateDefault();
            config.LaserPower = 5;
            config.MarkSpeed = 500;
            config.WorkAreaMinX = -50;
            config.WorkAreaMaxX = 50;
            config.WorkAreaMinY = -50;
            config.WorkAreaMaxY = 50;
            return config;
        }

        #endregion

        #region 转换方法

        /// <summary>
        /// 转换为 MarkParameter 结构（供DLL使用）
        /// </summary>
        public MarkParameter ToMarkParameter()
        {
            return new MarkParameter
            {
                MarkSpeed = MarkSpeed,
                JumpSpeed = JumpSpeed,
                MarkDelay = MarkDelay,
                JumpDelay = JumpDelay,
                PolygonDelay = PolygonDelay,
                MarkCount = MarkCount,

                LaserOnDelay = LaserOnDelay,
                LaserOffDelay = LaserOffDelay,
                FPKDelay = FPKDelay,
                FPKLength = FPKLength,
                QDelay = 0,
                DutyCycle = DutyCycle,
                Frequency = Frequency,
                StandbyFrequency = StandbyFrequency,
                StandbyDutyCycle = StandbyDutyCycle,
                LaserPower = LaserPower,

                AnalogMode = UseAnalogPower ? 1u : 0u,
                Waveform = Waveform,
                PulseWidthMode = EnableMOPA ? 1u : 0u,
                PulseWidth = MOPAPulseWidth
            };
        }

        #endregion

        #region 验证方法

        /// <summary>
        /// 验证配置是否合法
        /// </summary>
        public bool Validate(out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(IpAddress))
            {
                error = "IP地址不能为空";
                return false;
            }

            var parts = IpAddress.Split('.');
            if (parts.Length != 4)
            {
                error = "IP地址格式错误";
                return false;
            }

            if (WorkAreaMinX >= WorkAreaMaxX)
            {
                error = "工作区域X范围错误：最小值必须小于最大值";
                return false;
            }

            if (WorkAreaMinY >= WorkAreaMaxY)
            {
                error = "工作区域Y范围错误：最小值必须小于最大值";
                return false;
            }

            if (LaserPower < 0 || LaserPower > 100)
            {
                error = "激光功率必须在 0~100 之间";
                return false;
            }

            if (Frequency <= 0)
            {
                error = "出光频率必须大于0";
                return false;
            }

            if (DutyCycle < 0 || DutyCycle > 1)
            {
                error = "占空比必须在 0~1 之间";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 检查坐标是否在工作区域内
        /// </summary>
        public bool IsInWorkArea(double x, double y)
        {
            if (!EnableCoordinateCheck)
                return true;

            return x >= WorkAreaMinX && x <= WorkAreaMaxX &&
                   y >= WorkAreaMinY && y <= WorkAreaMaxY;
        }

        #endregion

        #region INotifyPropertyChanged 实现

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion

        #region 复制方法

        /// <summary>
        /// 从另一个配置复制所有值
        /// </summary>
        public void CopyFrom(HM_LaserConfig source)
        {
            if (source == null) return;

            IpAddress = source.IpAddress;
            ConnectionTimeoutMs = source.ConnectionTimeoutMs;
            DownloadTimeoutMs = source.DownloadTimeoutMs;
            MarkTimeoutMs = source.MarkTimeoutMs;

            MarkSpeed = source.MarkSpeed;
            JumpSpeed = source.JumpSpeed;
            MarkDelay = source.MarkDelay;
            JumpDelay = source.JumpDelay;
            PolygonDelay = source.PolygonDelay;
            MarkCount = source.MarkCount;

            LaserOnDelay = source.LaserOnDelay;
            LaserOffDelay = source.LaserOffDelay;
            FPKDelay = source.FPKDelay;
            FPKLength = source.FPKLength;
            Frequency = source.Frequency;
            DutyCycle = source.DutyCycle;
            LaserPower = source.LaserPower;
            StandbyFrequency = source.StandbyFrequency;
            StandbyDutyCycle = source.StandbyDutyCycle;

            WorkAreaMinX = source.WorkAreaMinX;
            WorkAreaMaxX = source.WorkAreaMaxX;
            WorkAreaMinY = source.WorkAreaMinY;
            WorkAreaMaxY = source.WorkAreaMaxY;
            EnableCoordinateCheck = source.EnableCoordinateCheck;

            Protocol = source.Protocol;
            Dimensional = source.Dimensional;
            JumpToZeroAfterMark = source.JumpToZeroAfterMark;
            UseAnalogPower = source.UseAnalogPower;
            Waveform = source.Waveform;
            EnableMOPA = source.EnableMOPA;
            MOPAPulseWidth = source.MOPAPulseWidth;
            FillLineSpacing = source.FillLineSpacing;

        }

        /// <summary>
        /// 创建配置的副本
        /// </summary>
        public HM_LaserConfig Clone()
        {
            var clone = new HM_LaserConfig();
            clone.CopyFrom(this);
            return clone;
        }

        #endregion

        public override string ToString()
        {
            return $"HM_LaserConfig [IP={IpAddress}, Speed={MarkSpeed}mm/s, Power={LaserPower}%, Freq={Frequency}kHz, Area={WorkAreaWidth}x{WorkAreaHeight}mm]";
        }
    }
}
