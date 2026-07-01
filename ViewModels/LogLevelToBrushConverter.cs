using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// 日志级别到颜色的转换器
    /// </summary>
    public class LogLevelToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is LogLevel level)
            {
                switch (level)
                {
                    case LogLevel.Info:
                        return new SolidColorBrush(Color.FromRgb(102, 102, 102)); // #666666
                    case LogLevel.Success:
                        return new SolidColorBrush(Color.FromRgb(39, 174, 96));  // #27AE60 绿色
                    case LogLevel.Warning:
                        return new SolidColorBrush(Color.FromRgb(243, 156, 18)); // #F39C12 橙色
                    case LogLevel.Error:
                        return new SolidColorBrush(Color.FromRgb(231, 76, 60));  // #E74C3C 红色
                    
                    default:
                        return Brushes.Black;
                }
            }

            return Brushes.Black;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}