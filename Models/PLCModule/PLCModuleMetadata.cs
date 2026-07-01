using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SeedCut.Models.PLCModule
{
    /// <summary>
    /// PLC模块元数据 - 用于定义一个PLC子页面的结构
    /// </summary>
    public class PLCModuleMetadata : INotifyPropertyChanged
    {
        private bool _isSelected;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 模块唯一标识符
        /// </summary>
        public string ModuleId { get; set; }

        /// <summary>
        /// 模块显示名称
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 模块图标（可选）
        /// </summary>
        public string IconPath { get; set; }

        /// <summary>
        /// 模块描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 地址配置组名（对应PLCAddressConfig中的Group）
        /// </summary>
        public string AddressGroup { get; set; }

        /// <summary>
        /// 控制区域定义列表
        /// </summary>
        public List<PLCControlSection> ControlSections { get; set; } = new List<PLCControlSection>();

        /// <summary>
        /// 是否启用该模块
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 排序顺序
        /// </summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// 是否被选中（用于UI高亮显示）
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// PLC控制区域 - 一个模块可以包含多个控制区域
    /// 例如：点动控制区、使能控制区、位置参数区等
    /// </summary>
    public class PLCControlSection
    {
        /// <summary>
        /// 区域标识符
        /// </summary>
        public string SectionId { get; set; }

        /// <summary>
        /// 区域显示标题
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// 区域类型
        /// </summary>
        public SectionType Type { get; set; }

        /// <summary>
        /// 控制项列表
        /// </summary>
        public List<PLCControlItemConfig> ControlItems { get; set; } = new List<PLCControlItemConfig>();

        /// <summary>
        /// 布局配置
        /// </summary>
        public LayoutConfig Layout { get; set; } = new LayoutConfig();
    }

    /// <summary>
    /// 区域类型枚举
    /// </summary>
    public enum SectionType
    {
        /// <summary>
        /// 按钮控制区（点动、使能、启动等）
        /// </summary>
        ButtonControl,

        /// <summary>
        /// 参数设置区（位置、速度等）
        /// </summary>
        ParameterSetting,

        /// <summary>
        /// 状态显示区（反馈、标志位等）
        /// </summary>
        StatusDisplay,

        /// <summary>
        /// 混合区域（自定义）
        /// </summary>
        Mixed
    }

    /// <summary>
    /// PLC控制项配置
    /// </summary>
    public class PLCControlItemConfig
    {
        /// <summary>
        /// 控制项唯一ID
        /// </summary>
        public string ItemId { get; set; }

        /// <summary>
        /// 显示标签
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// 绑定的PLC地址名称（对应PLCAddress.Name）
        /// </summary>
        public string AddressName { get; set; }

        /// <summary>
        /// UI控件类型
        /// </summary>
        public ControlType ControlType { get; set; }

        /// <summary>
        /// 控件样式（可选，用于覆盖默认样式）
        /// </summary>
        public string StyleKey { get; set; }

        /// <summary>
        /// 是否只读
        /// </summary>
        public bool IsReadOnly { get; set; }

        /// <summary>
        /// 提示信息
        /// </summary>
        public string Tooltip { get; set; }

        /// <summary>
        /// 额外配置（用于特殊控件）
        /// </summary>
        public Dictionary<string, object> ExtraConfig { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// UI控件类型枚举
    /// </summary>
    public enum ControlType
    {
        /// <summary>
        /// 开关按钮（Bool类型）
        /// </summary>
        ToggleButton,

        /// <summary>
        /// Jog按钮（Bool类型，Press动Release停）
        /// </summary>
        JogButton,

        /// <summary>
        /// 指示灯（Bool类型，只读）
        /// </summary>
        Indicator,

        /// <summary>
        /// 数值输入框（Int/Float类型）
        /// </summary>
        NumberInput,

        /// <summary>
        /// 数值显示（Int/Float类型，只读）
        /// </summary>
        NumberDisplay,

        /// <summary>
        /// 滑动条（Int/Float类型）
        /// </summary>
        Slider,

        /// <summary>
        /// 文本显示
        /// </summary>
        TextDisplay,

        /// <summary>
        /// 下拉选择
        /// </summary>
        ComboBox,

        /// <summary>
        /// 自定义控件
        /// </summary>
        Custom
    }

    /// <summary>
    /// 布局配置
    /// </summary>
    public class LayoutConfig
    {
        /// <summary>
        /// 列数（用于网格布局）
        /// </summary>
        public int Columns { get; set; } = 3;

        /// <summary>
        /// 行间距
        /// </summary>
        public double RowSpacing { get; set; } = 10;

        /// <summary>
        /// 列间距
        /// </summary>
        public double ColumnSpacing { get; set; } = 10;

        /// <summary>
        /// 是否自动换行
        /// </summary>
        public bool AutoWrap { get; set; } = true;
    }
}