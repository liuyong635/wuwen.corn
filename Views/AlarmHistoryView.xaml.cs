using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SeedCut.Views
{
    /// <summary>
    /// 报警历史视图
    /// </summary>
    public partial class AlarmHistoryView : UserControl
    {
        public AlarmHistoryView()
        {
            InitializeComponent();

            // ✅ 添加调试：检查 DataContext
            this.Loaded += (s, e) =>
            {
                Debug.WriteLine($"🔍 AlarmHistoryView Loaded");
                Debug.WriteLine($"   DataContext: {this.DataContext?.GetType().Name ?? "NULL"}");

                if (this.DataContext != null)
                {
                    var vm = this.DataContext as SeedCut.ViewModels.AlarmViewModel;
                    if (vm != null)
                    {
                        Debug.WriteLine($"   ✓ AlarmViewModel found");
                        Debug.WriteLine($"   - HasActiveAlarms: {vm.HasActiveAlarms}");
                        Debug.WriteLine($"   - ActiveAlarms.Count: {vm.ActiveAlarms.Count}");
                        Debug.WriteLine($"   - AlarmHistory.Count: {vm.AlarmHistory.Count}");
                    }
                    else
                    {
                        Debug.WriteLine($"   ❌ DataContext is not AlarmViewModel!");
                    }
                }
            };
        }
    }

    #region 转换器

    /// <summary>
    /// 布尔值转状态背景色转换器
    /// </summary>
    public class BoolToStatusBgConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Debug.WriteLine($"🔍 BoolToStatusBgConverter.Convert: value={value}");

            if (value is bool isAcknowledged)
            {
                var color = isAcknowledged ? "#4CAF50" : "#FF9800";
                Debug.WriteLine($"   → Returning: {color}");
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            }

            Debug.WriteLine($"   → Returning: Gray (fallback)");
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// ✅ 修复：真正的反转布尔值可见性转换器
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Debug.WriteLine($"🔍 InverseBoolToVisibilityConverter.Convert: value={value}");

            if (value is bool boolValue)
            {
                // ✅ 修复：InverseBoolToVisibility 应该默认就是反转的
                var result = boolValue ? Visibility.Collapsed : Visibility.Visible;
                Debug.WriteLine($"   → Returning: {result}");
                return result;
            }

            Debug.WriteLine($"   → Returning: Collapsed (fallback)");
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 字符串到可见性转换器
    /// </summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Debug.WriteLine($"🔍 StringToVisibilityConverter.Convert: value={value}");

            if (value is string stringValue)
            {
                bool hasValue = !string.IsNullOrEmpty(stringValue);
                var result = hasValue ? Visibility.Visible : Visibility.Collapsed;
                Debug.WriteLine($"   → hasValue={hasValue}, Returning: {result}");
                return result;
            }

            Debug.WriteLine($"   → Returning: Collapsed (fallback)");
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 字符串到颜色转换器
    /// </summary>
    public class StringToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Debug.WriteLine($"🔍 StringToColorConverter.Convert: value={value}");

            if (value is string colorString && !string.IsNullOrEmpty(colorString))
            {
                try
                {
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorString));
                    Debug.WriteLine($"   → Returning: {colorString}");
                    return brush;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"   ❌ Color conversion failed: {ex.Message}");
                    return new SolidColorBrush(Colors.Gray);
                }
            }

            Debug.WriteLine($"   → Returning: Gray (fallback)");
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    #endregion
}