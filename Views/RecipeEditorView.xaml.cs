using Microsoft.Extensions.DependencyInjection;
using SeedCut.Framework.Services.Interfaces;
using SeedCut.ViewModels.Recipe;
using System;
using System.Windows.Controls;

namespace SeedCut.Views.Recipe
{
    /// <summary>
    /// RecipeEditorView.xaml 的交互逻辑
    /// </summary>
    public partial class RecipeEditorView : UserControl
    {
        private readonly RecipeEditorViewModel _viewModel;

        /// <summary>
        /// 通过DI容器创建视图
        /// </summary>
        public RecipeEditorView(IServiceProvider serviceProvider)
        {
            InitializeComponent();

            var recipeService = serviceProvider.GetRequiredService<IRecipeService>();
            _viewModel = new RecipeEditorViewModel(recipeService);
            this.DataContext = _viewModel;
        }

        /// <summary>
        /// 直接传入RecipeService创建视图（备用）
        /// </summary>
        public RecipeEditorView(IRecipeService recipeService)
        {
            InitializeComponent();

            _viewModel = new RecipeEditorViewModel(recipeService);
            this.DataContext = _viewModel;
        }
    }
}
