using SeedCut.Models;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SeedCut.Views.Converters
{
    /// <summary>
    /// 将用户类型与目标类型进行比较，返回Visibility
    /// </summary>
    public class UserTypeToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return Visibility.Collapsed;

            UserType selectedType;
            UserType targetEnum;

            // 解析当前值
            if (value is UserType ut)
            {
                selectedType = ut;
            }
            else if (value is string valueStr && Enum.TryParse(valueStr, out UserType parsedValue))
            {
                selectedType = parsedValue;
            }
            else
            {
                return Visibility.Collapsed;
            }

            // 解析参数（目标类型）
            if (parameter is UserType paramType)
            {
                targetEnum = paramType;
            }
            else if (parameter is string paramStr && Enum.TryParse(paramStr, out UserType parsedParam))
            {
                targetEnum = parsedParam;
            }
            else
            {
                return Visibility.Collapsed;
            }

            return selectedType == targetEnum ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}