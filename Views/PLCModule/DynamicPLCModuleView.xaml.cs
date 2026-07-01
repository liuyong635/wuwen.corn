using System;
using System.Windows;
using System.Windows.Controls;
using SeedCut.Models.PLCModule;
using SeedCut.ViewModels.PLCModule;

namespace SeedCut.Views.PLCModule
{
    /// <summary>
    /// DynamicPLCModuleView.xaml 的交互逻辑
    /// 负责根据ViewModel动态生成UI控件
    /// </summary>
    public partial class DynamicPLCModuleView : UserControl
    {
        public DynamicPLCModuleView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is PLCModuleViewModel moduleViewModel)
            {
                BuildDynamicUI(moduleViewModel);
            }
        }

        /// <summary>
        /// 根据ViewModel动态构建UI
        /// </summary>
        private void BuildDynamicUI(PLCModuleViewModel moduleViewModel)
        {
            // 清空现有内容
            var mainPanel = (Content as ScrollViewer)?.Content as StackPanel;
            if (mainPanel == null) return;

            // 保留标题部分，清除动态内容
            while (mainPanel.Children.Count > 2)
            {
                mainPanel.Children.RemoveAt(2);
            }

            // 为每个控制区域生成UI
            foreach (var section in moduleViewModel.ControlSections)
            {
                var sectionContainer = CreateSectionContainer(section);
                mainPanel.Children.Add(sectionContainer);
            }
        }

        /// <summary>
        /// 创建区域容器
        /// </summary>
        private Border CreateSectionContainer(PLCControlSectionViewModel section)
        {
            var border = new Border
            {
                Style = TryFindResource("SectionContainerStyle") as Style,
                Child = new StackPanel()
            };

            var stackPanel = border.Child as StackPanel;

            // 区域标题
            var title = new TextBlock
            {
                Text = section.Title,
                Style = TryFindResource("SectionTitleStyle") as Style
            };
            stackPanel.Children.Add(title);

            // 根据区域类型创建相应的布局
            Panel contentPanel = CreateContentPanel(section);
            stackPanel.Children.Add(contentPanel);

            return border;
        }

        /// <summary>
        /// 根据区域类型创建内容面板
        /// </summary>
        private Panel CreateContentPanel(PLCControlSectionViewModel section)
        {
            Panel contentPanel;

            switch (section.Type)
            {
                case SectionType.ButtonControl:
                    contentPanel = CreateButtonControlPanel(section);
                    break;

                case SectionType.ParameterSetting:
                    contentPanel = CreateParameterPanel(section);
                    break;

                case SectionType.StatusDisplay:
                    contentPanel = CreateStatusPanel(section);
                    break;

                case SectionType.Mixed:
                default:
                    contentPanel = CreateMixedPanel(section);
                    break;
            }

            return contentPanel;
        }

        /// <summary>
        /// 创建按钮控制面板（WrapPanel布局）
        /// </summary>
        private Panel CreateButtonControlPanel(PLCControlSectionViewModel section)
        {
            var wrapPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal
            };

            foreach (var item in section.ControlItems)
            {
                var control = PLCControlFactory.CreateControl(item);
                if (control != null)
                {
                    wrapPanel.Children.Add(control);
                }
            }

            return wrapPanel;
        }

        /// <summary>
        /// 创建参数设置面板（Grid布局）
        /// </summary>
        private Panel CreateParameterPanel(PLCControlSectionViewModel section)
        {
            var grid = new Grid();

            // 根据配置的列数创建列定义
            int columns = section.Layout.Columns;
            for (int i = 0; i < columns; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
            }

            // 计算需要的行数
            int rows = (int)Math.Ceiling((double)section.ControlItems.Count / columns);
            for (int i = 0; i < rows; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });
            }

            // 添加控件到Grid
            for (int i = 0; i < section.ControlItems.Count; i++)
            {
                var item = section.ControlItems[i];
                var control = PLCControlFactory.CreateControl(item);

                if (control != null)
                {
                    int row = i / columns;
                    int col = i % columns;

                    Grid.SetRow(control, row);
                    Grid.SetColumn(control, col);

                    control.Margin = new Thickness(
                        section.Layout.ColumnSpacing / 2,
                        section.Layout.RowSpacing / 2,
                        section.Layout.ColumnSpacing / 2,
                        section.Layout.RowSpacing / 2);

                    grid.Children.Add(control);
                }
            }

            return grid;
        }

        /// <summary>
        /// 创建状态显示面板（WrapPanel布局，更紧凑）
        /// </summary>
        private Panel CreateStatusPanel(PLCControlSectionViewModel section)
        {
            var wrapPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal
            };

            foreach (var item in section.ControlItems)
            {
                var control = PLCControlFactory.CreateControl(item);
                if (control != null)
                {
                    wrapPanel.Children.Add(control);
                }
            }

            return wrapPanel;
        }

        /// <summary>
        /// 创建混合面板（StackPanel布局）
        /// </summary>
        private Panel CreateMixedPanel(PLCControlSectionViewModel section)
        {
            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            foreach (var item in section.ControlItems)
            {
                var control = PLCControlFactory.CreateControl(item);
                if (control != null)
                {
                    stackPanel.Children.Add(control);
                }
            }

            return stackPanel;
        }
    }
}