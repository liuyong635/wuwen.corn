using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SeedCut.Views
{
    /// <summary>
    /// 将null值转换为Visibility的转换器
    /// null或空字符串 -> Collapsed
    /// 非null值 -> Visible
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// 是否反转结果
        /// true: null -> Visible, 非null -> Collapsed
        /// false: null -> Collapsed, 非null -> Visible (默认)
        /// </summary>
        public bool IsInverted { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNull = value == null;

            // 如果是字符串，也检查是否为空
            if (value is string str)
            {
                isNull = string.IsNullOrWhiteSpace(str);
            }

            // 检查parameter是否为"Invert"
            bool invert = IsInverted;
            if (parameter is string paramStr &&
                paramStr.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            {
                invert = !invert;
            }

            if (invert)
            {
                return isNull ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                return isNull ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 将布尔值转换为Visibility的转换器
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        /// <summary>
        /// 是否反转结果
        /// </summary>
        public bool IsInverted { get; set; } = false;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool boolValue = false;

            if (value is bool b)
            {
                boolValue = b;
            }

            // 检查parameter是否为"Invert"
            bool invert = IsInverted;
            if (parameter is string paramStr &&
                paramStr.Equals("Invert", StringComparison.OrdinalIgnoreCase))
            {
                invert = !invert;
            }

            if (invert)
            {
                boolValue = !boolValue;
            }

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                bool result = visibility == Visibility.Visible;
                return IsInverted ? !result : result;
            }
            return false;
        }
    }
}