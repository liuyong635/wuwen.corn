using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SeedCut.Models.PLCModule;
using SeedCut.ViewModels.PLCModule;

namespace SeedCut.Views.PLCModule
{
    /// <summary>
    /// UI控件工厂 - 支持两种按钮模式:Jog(点动)和Toggle(切换)
    /// ✅ 最终版本:修复了所有已知问题
    /// </summary>
    public static class PLCControlFactory
    {
        public static FrameworkElement CreateControl(PLCControlItemViewModel viewModel)
        {
            if (viewModel == null)
                return null;

            switch (viewModel.ControlType)
            {
                case ControlType.ToggleButton:
                case ControlType.JogButton:
                    return CreateButton(viewModel);

                case ControlType.Indicator:
                    return CreateIndicator(viewModel);

                case ControlType.NumberInput:
                    return CreateNumberInput(viewModel);

                case ControlType.NumberDisplay:
                    return CreateNumberDisplay(viewModel);

                case ControlType.Slider:
                    return CreateSlider(viewModel);

                case ControlType.TextDisplay:
                    return CreateTextDisplay(viewModel);

                default:
                    return CreateDefaultControl(viewModel);
            }
        }

        /// <summary>
        /// ✅ 创建按钮 - 完整版本，带鼠标捕获和robust的Jog处理
        /// </summary>
        private static Button CreateButton(PLCControlItemViewModel viewModel)
        {
            var button = new Button
            {
                Content = viewModel.Label,
                ToolTip = GetButtonTooltip(viewModel),
                MinWidth = 120,
                MinHeight = 40,
                Margin = new Thickness(5),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };

            if (viewModel.ButtonMode == ButtonMode.Jog)
            {
                // ✅ 绑定MouseDownCommand到CommandProperty以支持IsEnabled自动管理
                button.SetBinding(Button.CommandProperty, new Binding("MouseDownCommand")
                {
                    Source = viewModel
                });

                // ✅ Jog模式：使用鼠标捕获确保可靠的按下/松开
                bool isPressed = false;  // 状态标志

                button.PreviewMouseDown += (sender, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed &&
                        button.IsEnabled &&
                        !isPressed)  // 防止重复触发
                    {
                        isPressed = true;
                        button.CaptureMouse();  // ✅ 捕获鼠标，确保能收到松开事件

                        System.Diagnostics.Debug.WriteLine($"[Jog] MouseDown - {viewModel.Label} (Time: {DateTime.Now:HH:mm:ss.fff})");

                        // 视觉反馈：按钮变深色
                        button.Background = new SolidColorBrush(Color.FromRgb(25, 118, 210));

                        if (viewModel.MouseDownCommand.CanExecute(null))
                        {
                            viewModel.MouseDownCommand.Execute(null);
                        }
                        e.Handled = true;  // 阻止事件继续传递
                    }
                };

                button.PreviewMouseUp += (sender, e) =>
                {
                    if (isPressed)  // 只在已按下的情况下处理松开
                    {
                        isPressed = false;
                        button.ReleaseMouseCapture();  // ✅ 释放鼠标捕获

                        System.Diagnostics.Debug.WriteLine($"[Jog] MouseUp - {viewModel.Label} (Time: {DateTime.Now:HH:mm:ss.fff})");

                        // 恢复原色
                        button.Background = new SolidColorBrush(Color.FromRgb(33, 150, 243));

                        if (viewModel.MouseUpCommand.CanExecute(null))
                        {
                            viewModel.MouseUpCommand.Execute(null);
                        }
                        e.Handled = true;
                    }
                };

                // ✅ 额外处理：鼠标离开按钮时也要停止（双重保险）
                button.MouseLeave += (sender, e) =>
                {
                    if (isPressed)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Jog] MouseLeave (while pressed) - {viewModel.Label} - Force Release");

                        isPressed = false;
                        button.ReleaseMouseCapture();

                        // 恢复原色
                        button.Background = new SolidColorBrush(Color.FromRgb(33, 150, 243));

                        if (viewModel.MouseUpCommand.CanExecute(null))
                        {
                            viewModel.MouseUpCommand.Execute(null);
                        }
                    }
                };

                // Jog按钮基础样式：蓝色
                button.Background = new SolidColorBrush(Color.FromRgb(33, 150, 243));
                button.Foreground = Brushes.White;
            }
            else // Toggle模式
            {
                // Toggle模式：绑定ClickCommand
                button.SetBinding(Button.CommandProperty, new Binding("ClickCommand")
                {
                    Source = viewModel
                });

                // Toggle按钮根据状态变色
                var binding = new Binding("IsActive")
                {
                    Source = viewModel,
                    Converter = new BoolToColorConverter()
                };
                button.SetBinding(Button.BackgroundProperty, binding);
            }

            // ✅ 应用统一的按钮样式(包含禁用状态)
            ApplyButtonStyle(button);

            return button;
        }

        /// <summary>
        /// ✅ 应用按钮样式(处理禁用状态)
        /// </summary>
        private static void ApplyButtonStyle(Button button)
        {
            var style = new Style(typeof(Button));

            // 基础设置
            style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));

            // 创建控件模板
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ButtonBorder";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetValue(Border.PaddingProperty, new Thickness(12, 8, 12, 8));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;

            // ✅ 禁用触发器(最重要)
            var disabledTrigger = new Trigger
            {
                Property = Button.IsEnabledProperty,
                Value = false
            };
            disabledTrigger.Setters.Add(new Setter(
                Border.BackgroundProperty,
                new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                "ButtonBorder"
            ));
            disabledTrigger.Setters.Add(new Setter(
                Button.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(130, 130, 130))
            ));
            disabledTrigger.Setters.Add(new Setter(
                Button.OpacityProperty,
                0.6
            ));
            disabledTrigger.Setters.Add(new Setter(
                Button.CursorProperty,
                Cursors.No
            ));
            template.Triggers.Add(disabledTrigger);

            style.Setters.Add(new Setter(Button.TemplateProperty, template));
            button.Style = style;
        }

        private static string GetButtonTooltip(PLCControlItemViewModel viewModel)
        {
            var modeDesc = viewModel.ButtonMode == ButtonMode.Jog
                ? "点动模式: 按住按钮时运行,松开时停止"
                : "切换模式: 第一次点击启动,第二次点击停止";
            return $"{viewModel.Tooltip}\n\n{modeDesc}";
        }

        private static FrameworkElement CreateIndicator(PLCControlItemViewModel viewModel)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(5)
            };

            var indicator = new Ellipse
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(0, 0, 8, 0),
                Stroke = Brushes.Gray,
                StrokeThickness = 1
            };

            var binding = new Binding("IsActive")
            {
                Source = viewModel,
                Converter = new BoolToIndicatorColorConverter()
            };
            indicator.SetBinding(System.Windows.Shapes.Shape.FillProperty, binding);

            var label = new TextBlock
            {
                Text = viewModel.Label,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13
            };

            panel.Children.Add(indicator);
            panel.Children.Add(label);
            panel.ToolTip = viewModel.Tooltip;

            return panel;
        }

        /// <summary>
        /// ✅ 数字输入框 - 修复了IsEnabled绑定问题
        /// </summary>
        private static FrameworkElement CreateNumberInput(PLCControlItemViewModel viewModel)
        {
            var panel = new StackPanel { Margin = new Thickness(5) };

            // 标签
            var label = new TextBlock
            {
                Text = viewModel.Label,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = new SolidColorBrush(Color.FromRgb(66, 66, 66))
            };

            // 输入面板
            var inputPanel = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

            // ✅ 确认按钮 - 绑定Command才能自动启用/禁用
            var button = new Button
            {
                Content = "✓",
                Width = 32,
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };

            // ✅ 关键修复:绑定Command到CommandProperty
            button.SetBinding(Button.CommandProperty, new Binding("UpdateCommand")
            {
                Source = viewModel
            });

            DockPanel.SetDock(button, Dock.Right);
            inputPanel.Children.Add(button);

            // 输入框
            var textBox = new TextBox
            {
                MinWidth = 100,
                Height = 32,
                FontSize = 14,
                Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var inputBinding = new Binding("InputValue")
            {
                Source = viewModel,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            textBox.SetBinding(TextBox.TextProperty, inputBinding);

            // ✅ 关键修复:监听Command的CanExecuteChanged事件来更新IsEnabled
            if (viewModel.UpdateCommand != null)
            {
                // 初始状态
                textBox.IsEnabled = viewModel.UpdateCommand.CanExecute(null);

                // 监听Command状态变化
                viewModel.UpdateCommand.CanExecuteChanged += (sender, e) =>
                {
                    // 在UI线程上更新IsEnabled
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        textBox.IsEnabled = viewModel.UpdateCommand.CanExecute(null);
                    }));
                };
            }

            textBox.KeyDown += (sender, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    if (viewModel.UpdateCommand.CanExecute(null))
                    {
                        viewModel.UpdateCommand.Execute(null);
                    }
                }
            };

            inputPanel.Children.Add(textBox);

            // 单位
            if (viewModel.Config.ExtraConfig.TryGetValue("Unit", out var unit))
            {
                var unitLabel = new TextBlock
                {
                    Text = unit.ToString(),
                    FontSize = 12,
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray
                };
                DockPanel.SetDock(unitLabel, Dock.Right);
                inputPanel.Children.Insert(1, unitLabel);
            }

            // 当前值显示
            var currentValuePanel = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };

            var currentLabel = new TextBlock
            {
                Text = "当前值: ",
                FontSize = 11,
                Foreground = Brushes.Gray
            };
            DockPanel.SetDock(currentLabel, Dock.Left);
            currentValuePanel.Children.Add(currentLabel);

            var currentValueText = new TextBlock
            {
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                FontWeight = FontWeights.Bold
            };

            var displayBinding = new Binding("DisplayValue") { Source = viewModel };
            currentValueText.SetBinding(TextBlock.TextProperty, displayBinding);

            currentValuePanel.Children.Add(currentValueText);

            panel.Children.Add(label);
            panel.Children.Add(inputPanel);
            panel.Children.Add(currentValuePanel);
            panel.ToolTip = viewModel.Tooltip;

            return panel;
        }

        private static FrameworkElement CreateNumberDisplay(PLCControlItemViewModel viewModel)
        {
            var panel = new StackPanel { Margin = new Thickness(5) };

            var label = new TextBlock
            {
                Text = viewModel.Label,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = new SolidColorBrush(Color.FromRgb(66, 66, 66))
            };

            var valueText = new TextBlock
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243))
            };

            var binding = new Binding("DisplayValue") { Source = viewModel };
            valueText.SetBinding(TextBlock.TextProperty, binding);

            panel.Children.Add(label);
            panel.Children.Add(valueText);
            panel.ToolTip = viewModel.Tooltip;

            return panel;
        }

        private static FrameworkElement CreateSlider(PLCControlItemViewModel viewModel)
        {
            var panel = new StackPanel { Margin = new Thickness(5) };

            var label = new TextBlock
            {
                Text = viewModel.Label,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var slider = new Slider { MinWidth = 150, Height = 20 };

            if (viewModel.Config.ExtraConfig.TryGetValue("MinValue", out var minValue))
                slider.Minimum = Convert.ToDouble(minValue);
            if (viewModel.Config.ExtraConfig.TryGetValue("MaxValue", out var maxValue))
                slider.Maximum = Convert.ToDouble(maxValue);

            var binding = new Binding("Value")
            {
                Source = viewModel,
                Mode = BindingMode.TwoWay
            };
            slider.SetBinding(Slider.ValueProperty, binding);

            panel.Children.Add(label);
            panel.Children.Add(slider);

            return panel;
        }

        private static FrameworkElement CreateTextDisplay(PLCControlItemViewModel viewModel)
        {
            var panel = new StackPanel { Margin = new Thickness(5) };

            var label = new TextBlock
            {
                Text = viewModel.Label,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            };

            var valueText = new TextBlock { FontSize = 14 };

            var binding = new Binding("Value") { Source = viewModel };
            valueText.SetBinding(TextBlock.TextProperty, binding);

            panel.Children.Add(label);
            panel.Children.Add(valueText);

            return panel;
        }

        private static FrameworkElement CreateDefaultControl(PLCControlItemViewModel viewModel)
        {
            return new TextBlock
            {
                Text = $"{viewModel.Label}: {viewModel.ControlType}",
                Margin = new Thickness(5)
            };
        }
    }

    #region 值转换器

    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isActive && isActive)
                return new SolidColorBrush(Color.FromRgb(76, 175, 80));
            return new SolidColorBrush(Color.FromRgb(189, 189, 189));
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToIndicatorColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isActive && isActive)
                return new SolidColorBrush(Color.FromRgb(76, 175, 80));
            return new SolidColorBrush(Color.FromRgb(224, 224, 224));
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    #endregion
}