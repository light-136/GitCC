// ============================================================
// 文件：RecipeViewModel.cs
// 用途：配方管理页面ViewModel
// 设计思路：
//   配方（Recipe）是半导体设备的核心数据：
//   - 每种产品对应一组加工参数
//   - 包含运动点位、视觉参数、工艺参数
//   - 支持导入/导出、版本管理
//   此ViewModel提供配方的CRUD操作和参数编辑。
// ============================================================

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;

namespace SmartSemiCon.UI.ViewModels
{
    /// <summary>
    /// 配方管理页面ViewModel。
    /// </summary>
    public partial class RecipeViewModel : ObservableObject
    {
        private readonly IRecipeService _recipeService;
        private readonly ILogService _logService;

        [ObservableProperty]
        private string _currentRecipeName = "";

        [ObservableProperty]
        private string _newRecipeName = "";

        [ObservableProperty]
        private string _recipeDescription = "";

        [ObservableProperty]
        private RecipeData? _selectedRecipe;

        /// <summary>配方列表</summary>
        public ObservableCollection<string> RecipeNames { get; } = new();

        /// <summary>当前配方参数（Key-Value列表）</summary>
        public ObservableCollection<RecipeParameterEntry> Parameters { get; } = new();

        public RecipeViewModel(IRecipeService recipeService, ILogService logService)
        {
            _recipeService = recipeService;
            _logService = logService;
            _ = RefreshRecipeListAsync();
        }

        /// <summary>刷新配方列表</summary>
        [RelayCommand]
        private async Task RefreshRecipeListAsync()
        {
            var names = await _recipeService.GetRecipeListAsync();
            RecipeNames.Clear();
            foreach (var name in names)
                RecipeNames.Add(name);
        }

        /// <summary>加载选中配方</summary>
        [RelayCommand]
        private async Task LoadRecipe(string? name)
        {
            if (string.IsNullOrEmpty(name)) return;
            var recipe = await _recipeService.LoadAsync(name);
            if (recipe == null) return;

            SelectedRecipe = recipe;
            CurrentRecipeName = recipe.Name;
            RecipeDescription = recipe.Description ?? "";

            Parameters.Clear();
            foreach (var kv in recipe.Parameters)
                Parameters.Add(new RecipeParameterEntry { Key = kv.Key, Value = kv.Value?.ToString() ?? "" });

            _logService.Log(LogLevel.Info, "配方管理", $"加载配方: {name}");
        }

        /// <summary>保存当前配方</summary>
        [RelayCommand]
        private async Task SaveRecipe()
        {
            if (SelectedRecipe == null) return;

            SelectedRecipe.Description = RecipeDescription;
            SelectedRecipe.Parameters.Clear();
            foreach (var p in Parameters)
                SelectedRecipe.Parameters[p.Key] = p.Value;

            SelectedRecipe.ModifiedAt = DateTime.Now;
            await _recipeService.SaveAsync(SelectedRecipe);
            _logService.Log(LogLevel.Info, "配方管理", $"保存配方: {SelectedRecipe.Name}");
        }

        /// <summary>新建配方</summary>
        [RelayCommand]
        private async Task CreateRecipe()
        {
            if (string.IsNullOrWhiteSpace(NewRecipeName)) return;

            var recipe = new RecipeData
            {
                Name = NewRecipeName,
                Description = "新建配方",
                Version = "1.0",
                CreatedAt = DateTime.Now,
                ModifiedAt = DateTime.Now,
                CreatedBy = "admin"
            };
            await _recipeService.SaveAsync(recipe);
            await RefreshRecipeListAsync();
            NewRecipeName = "";
            _logService.Log(LogLevel.Info, "配方管理", $"新建配方: {recipe.Name}");
        }

        /// <summary>删除选中配方</summary>
        [RelayCommand]
        private async Task DeleteRecipe()
        {
            if (string.IsNullOrEmpty(CurrentRecipeName)) return;
            await _recipeService.DeleteAsync(CurrentRecipeName);
            SelectedRecipe = null;
            CurrentRecipeName = "";
            Parameters.Clear();
            await RefreshRecipeListAsync();
            _logService.Log(LogLevel.Info, "配方管理", "配方已删除");
        }

        /// <summary>添加参数</summary>
        [RelayCommand]
        private void AddParameter()
        {
            Parameters.Add(new RecipeParameterEntry { Key = "新参数", Value = "0" });
        }
    }

    /// <summary>配方参数条目 — 用于UI绑定</summary>
    public class RecipeParameterEntry : ObservableObject
    {
        private string _key = "";
        private string _value = "";

        public string Key
        {
            get => _key;
            set => SetProperty(ref _key, value);
        }

        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }
    }
}
