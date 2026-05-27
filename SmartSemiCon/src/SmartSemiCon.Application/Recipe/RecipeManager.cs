// ============================================================
// 文件：RecipeManager.cs
// 用途：配方管理服务
// 设计思路：
//   配方（Recipe）是半导体设备的工艺参数集合。
//   切换不同产品时，只需切换配方即可加载全部参数。
//   配方保存为JSON文件，便于备份和传输。
// ============================================================

using System.Text.Json;
using SmartSemiCon.Domain.Events;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;

namespace SmartSemiCon.Application.Recipe
{
    /// <summary>
    /// 配方管理器 — 管理工艺参数的加载/保存/切换。
    /// </summary>
    public class RecipeManager : IRecipeService
    {
        private readonly string _recipePath;
        private readonly IEventBus _eventBus;
        private readonly ILogService _logService;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>当前配方</summary>
        public RecipeData? CurrentRecipe { get; private set; }

        /// <summary>配方切换事件</summary>
        public event EventHandler<string>? RecipeChanged;

        public RecipeManager(IEventBus eventBus, ILogService logService, string recipePath = "Recipes")
        {
            _eventBus = eventBus;
            _logService = logService;
            _recipePath = recipePath;
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            Directory.CreateDirectory(_recipePath);
        }

        /// <summary>加载配方。</summary>
        public async Task<RecipeData?> LoadAsync(string name)
        {
            var filePath = Path.Combine(_recipePath, $"{name}.json");
            if (!File.Exists(filePath)) return null;

            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<RecipeData>(json, _jsonOptions);
        }

        /// <summary>保存配方。</summary>
        public async Task<bool> SaveAsync(RecipeData recipe)
        {
            try
            {
                recipe.ModifiedAt = DateTime.Now;
                var filePath = Path.Combine(_recipePath, $"{recipe.Name}.json");
                var json = JsonSerializer.Serialize(recipe, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json);

                _logService.Log(Domain.Enums.LogLevel.Info, "配方管理",
                    $"配方已保存: {recipe.Name}");
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>删除配方。</summary>
        public Task<bool> DeleteAsync(string name)
        {
            var filePath = Path.Combine(_recipePath, $"{name}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        /// <summary>获取所有配方名称。</summary>
        public Task<List<string>> GetRecipeListAsync()
        {
            var files = Directory.GetFiles(_recipePath, "*.json");
            var names = files.Select(f => Path.GetFileNameWithoutExtension(f)).ToList();
            return Task.FromResult(names);
        }

        /// <summary>切换当前配方。</summary>
        public async Task<bool> SwitchRecipeAsync(string name)
        {
            var recipe = await LoadAsync(name);
            if (recipe == null) return false;

            var previousName = CurrentRecipe?.Name;
            CurrentRecipe = recipe;

            _eventBus.Publish(new RecipeChangedEvent
            {
                PreviousRecipeName = previousName,
                CurrentRecipeName = name,
                Source = "配方管理"
            });

            RecipeChanged?.Invoke(this, name);
            _logService.Log(Domain.Enums.LogLevel.Info, "配方管理",
                $"配方已切换: {previousName} → {name}");

            return true;
        }
    }
}
