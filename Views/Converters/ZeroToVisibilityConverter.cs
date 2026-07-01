using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SeedCut.Views.Converters
{
    /// <summary>
    /// 数值为0时显示，否则隐藏
    /// 用于集合计数为0时显示"暂无数据"提示
    /// </summary>
    public class ZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return Visibility.Visible;

            if (value is int intValue)
            {
                return intValue == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (value is double doubleValue)
            {
                return Math.Abs(doubleValue) < 0.001 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (value is long longValue)
            {
                return longValue == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            // 对于集合类型，尝试获取Count属性
            var type = value.GetType();
            var countProperty = type.GetProperty("Count");
            if (countProperty != null)
            {
                var count = (int)countProperty.GetValue(value);
                return count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("ZeroToVisibilityConverter 不支持反向转换");
        }
    }
}