using System;
using System.Globalization;
using System.Windows.Data;

namespace SeedCut.Views.Converters
{
    /// <summary>
    /// 将进度值（0-100）转换为宽度百分比字符串或实际宽度
    /// 支持两种模式：
    /// 1. 单值转换器：直接将进度值用于Width绑定
    /// 2. 多值转换器：计算实际像素宽度
    /// </summary>
    public class ProgressToWidthConverter : IValueConverter, IMultiValueConverter
    {
        // 单值转换器实现
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double progress)
            {
                // 如果有参数，作为最大宽度使用
                if (parameter is double maxWidth)
                {
                    return (progress / 100.0) * maxWidth;
                }
                // 否则直接返回进度值作为宽度
                return progress * 6.4; // 假设最大宽度640px
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        // 多值转换器实现
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is double currentValue &&
                values[1] is double maximum)
            {
                // 如果有第三个值，作为容器宽度
                double containerWidth = 640; // 默认宽度
                if (values.Length >= 3 && values[2] is double actualWidth)
                {
                    containerWidth = actualWidth;
                }

                if (maximum <= 0) return 0.0;
                return (currentValue / maximum) * containerWidth;
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}