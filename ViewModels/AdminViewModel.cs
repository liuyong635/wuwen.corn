using Microsoft.Extensions.DependencyInjection;
using SeedCut.Services;
using SeedCut.Views;
using SeedCut.Views.PLCModule;
using SeedCut.Views.Recipe;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace SeedCut.ViewModels
{
    /// <summary>
    /// 管理员页面的ViewModel
    /// </summary>
    public class AdminViewModel : INotifyPropertyChanged
    {
        private readonly IServiceProvider _serviceProvider;
        private string _selectedModule = "Vision";
        private object _currentModuleContent;

        public event PropertyChangedEventHandler PropertyChanged;

        public string SelectedModule
        {
            get => _selectedModule;
            set
            {
                if (_selectedModule != value)
                {
                    _selectedModule = value;
                    OnPropertyChanged();
                }
            }
        }

        public object CurrentModuleContent
        {
            get => _currentModuleContent;
            set
            {
                if (_currentModuleContent != value)
                {
                    _currentModuleContent = value;
                    OnPropertyChanged();
                }
            }
        }

        public AdminViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void NavigateToModule(string moduleName)
        {
            SelectedModule = moduleName;

            switch (moduleName)
            {
                case "Vision":
                    try
                    {
                        CurrentModuleContent = _serviceProvider.GetRequiredService<VisionView>();
                    }
                    catch (Exception ex)
                    {
                        ShowError("Vision", ex);
                    }
                    break;

                case "Camera":
                    try
                    {
                        CurrentModuleContent = _serviceProvider.GetRequiredService<CameraView>();
                    }
                    catch (Exception ex)
                    {
                        ShowError("Camera", ex);
                    }
                    break;

                case "Robot":
                    try
                    {
                        CurrentModuleContent = _serviceProvider.GetRequiredService<RobotView>();
                    }
                    catch (Exception ex)
                    {
                        ShowError("Robot", ex);
                    }
                    break;

                case "Vibrator":
                    try
                    {
                        CurrentModuleContent = _serviceProvider.GetRequiredService<VibratorView>();
                    }
                    catch (Exception ex)
                    {
                        ShowError("Vibrator", ex);
                    }
                    break;

                case "PLC":
                    try
                    {
                        CurrentModuleContent = _serviceProvider.GetRequiredService<MainPLCView>();
                    }
                    catch (Exception ex)
                    {
                        ShowError("PLC", ex);
                    }
                    break;

                case "Laser":
                    try
                    {
                        CurrentModuleContent = _serviceProvider.GetRequiredService<LaserDebugView>();
                    }
                    catch (Exception ex)
                    {
                        ShowError("Laser", ex);
                    }
                    break;
                case "HM_Laser":
                    try
                    {
                        CurrentModuleContent = _serviceProvider.GetRequiredService<HM_LaserDebugView>();
                    }
                    catch (Exception ex)
                    {
                        ShowError("HM_Laser", ex);
                    }
                    break;
                case "HandlerDebug":
                    try
                    {
                        CurrentModuleContent = _serviceProvider.GetRequiredService<HandlerDebugView>();
                    }
                    catch (Exception ex)
                    {
                        ShowError("HandlerDebug", ex);
                    }
                    break;

                case "Recipe":
                    try
                    {
                        CurrentModuleContent = _serviceProvider.GetRequiredService<RecipeEditorView>();
                    }
                    catch (Exception ex)
                    {
                        ShowError("Recipe", ex);
                    }
                    break;
                default:
                    ShowPlaceholder("未知模块");
                    break;
            }
        }

        private void ShowPlaceholder(string message)
        {
            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = message,
                FontSize = 24,
                Foreground = System.Windows.Media.Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var grid = new System.Windows.Controls.Grid();
            grid.Children.Add(textBlock);

            CurrentModuleContent = grid;
        }

        private void ShowError(string moduleName, Exception ex)
        {
            var errorText = $"加载 {moduleName} 模块失败：\n{ex.Message}";
            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = errorText,
                FontSize = 16,
                Foreground = System.Windows.Media.Brushes.Red,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20)
            };

            var grid = new System.Windows.Controls.Grid();
            grid.Children.Add(textBlock);

            CurrentModuleContent = grid;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}