// ============================================================
// 文件：RecipeService.cs
// 层次：应用层 (Application Layer) — 配方管理服务
// 职责：实现 IRecipeService 接口
// ============================================================

using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Enums;
using SmartIndustry.Domain.Events;
using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Interfaces.Repositories;
using System.Text.Json;

namespace SmartIndustry.Application.Recipe
{
    /// <summary>
    /// 配方管理服务实现
    /// </summary>
    public class RecipeService : IRecipeService
    {
        private readonly IRepository<RecipeData> _recipeRepository;
        private readonly IEventBus _eventBus;
        private readonly ILogService _logService;

        public RecipeService(
            IRepository<RecipeData> recipeRepository,
            IEventBus eventBus,
            ILogService logService)
        {
            _recipeRepository = recipeRepository ?? throw new ArgumentNullException(nameof(recipeRepository));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        public async Task<IReadOnlyList<RecipeData>> GetAllRecipes(CancellationToken cancellationToken = default)
        {
            return await _recipeRepository.GetAllAsync(cancellationToken);
        }

        public async Task<RecipeData?> GetRecipe(Guid recipeId, CancellationToken cancellationToken = default)
        {
            return await _recipeRepository.GetByIdAsync(recipeId, cancellationToken);
        }

        public async Task<IReadOnlyList<RecipeData>> GetRecipeVersions(string recipeName,
            CancellationToken cancellationToken = default)
        {
            return await _recipeRepository.FindAsync(r => r.Name == recipeName, cancellationToken);
        }

        public async Task<RecipeData?> GetActiveRecipe(string recipeName,
            CancellationToken cancellationToken = default)
        {
            var active = await _recipeRepository.FindAsync(
                r => r.Name == recipeName && r.State == RecipeState.Active, cancellationToken);
            return active.FirstOrDefault();
        }

        public async Task<RecipeData> CreateRecipe(string name, string description,
            Dictionary<string, object>? parameters, string createdBy,
            CancellationToken cancellationToken = default)
        {
            var recipe = RecipeData.CreateNew(name, description, parameters, createdBy);

            await _recipeRepository.AddAsync(recipe, cancellationToken);
            await _recipeRepository.SaveChangesAsync(cancellationToken);

            _logService.Info("RecipeService", $"创建配方：{name}（版本 {recipe.VersionNumber}）");
            return recipe;
        }

        public async Task UpdateRecipe(Guid recipeId, Dictionary<string, object> parameters,
            string operatedBy, CancellationToken cancellationToken = default)
        {
            var recipe = await _recipeRepository.GetByIdAsync(recipeId, cancellationToken)
                ?? throw new InvalidOperationException($"配方 {recipeId} 不存在");

            recipe.UpdateParameters(parameters, operatedBy);
            _recipeRepository.Update(recipe);
            await _recipeRepository.SaveChangesAsync(cancellationToken);

            _logService.Info("RecipeService", $"更新配方参数：{recipe.Name}（版本 {recipe.VersionNumber}）");
        }

        public async Task<RecipeData> CreateNewVersion(Guid parentRecipeId,
            Dictionary<string, object>? newParameters, string createdBy,
            CancellationToken cancellationToken = default)
        {
            var parent = await _recipeRepository.GetByIdAsync(parentRecipeId, cancellationToken)
                ?? throw new InvalidOperationException($"父配方 {parentRecipeId} 不存在");

            var newVersion = RecipeData.CreateNewVersion(parent, newParameters, createdBy);

            await _recipeRepository.AddAsync(newVersion, cancellationToken);
            await _recipeRepository.SaveChangesAsync(cancellationToken);

            await _eventBus.PublishAsync(
                new RecipeChangedEvent(newVersion.Name,
                    parent.VersionNumber,
                    newVersion.VersionNumber),
                cancellationToken);

            _logService.Info("RecipeService",
                $"创建配方新版本：{newVersion.Name} v{parent.VersionNumber} → v{newVersion.VersionNumber}");
            return newVersion;
        }

        public async Task DeleteRecipe(Guid recipeId, string operatedBy,
            CancellationToken cancellationToken = default)
        {
            var recipe = await _recipeRepository.GetByIdAsync(recipeId, cancellationToken)
                ?? throw new InvalidOperationException($"配方 {recipeId} 不存在");

            if (recipe.State == RecipeState.Active)
                throw new InvalidOperationException($"不允许删除活跃配方：{recipe.Name}");

            _recipeRepository.Delete(recipe, operatedBy);
            await _recipeRepository.SaveChangesAsync(cancellationToken);

            _logService.Info("RecipeService", $"删除配方：{recipe.Name} v{recipe.VersionNumber}");
        }

        public async Task ActivateRecipe(Guid recipeId, string activatedBy,
            CancellationToken cancellationToken = default)
        {
            var recipe = await _recipeRepository.GetByIdAsync(recipeId, cancellationToken)
                ?? throw new InvalidOperationException($"配方 {recipeId} 不存在");

            // 归档同名的当前活跃配方
            var currentActive = await GetActiveRecipe(recipe.Name, cancellationToken);
            if (currentActive != null && currentActive.Id != recipeId)
            {
                currentActive.Archive(activatedBy);
                _recipeRepository.Update(currentActive);
            }

            recipe.Activate(activatedBy);
            _recipeRepository.Update(recipe);
            await _recipeRepository.SaveChangesAsync(cancellationToken);

            await _eventBus.PublishAsync(
                new RecipeChangedEvent(recipe.Name, null, recipe.VersionNumber),
                cancellationToken);

            _logService.Info("RecipeService", $"激活配方：{recipe.Name} v{recipe.VersionNumber}");
        }

        public async Task<string> ExportRecipe(Guid recipeId, CancellationToken cancellationToken = default)
        {
            var recipe = await _recipeRepository.GetByIdAsync(recipeId, cancellationToken)
                ?? throw new InvalidOperationException($"配方 {recipeId} 不存在");

            var exportData = new
            {
                recipe.Name,
                recipe.Description,
                recipe.VersionNumber,
                recipe.Parameters,
                recipe.State,
                ExportedAt = DateTime.Now,
                Platform = "SmartIndustry Platform v1.0"
            };

            return JsonSerializer.Serialize(exportData, new JsonSerializerOptions { WriteIndented = true });
        }

        public async Task<RecipeData> ImportRecipe(string jsonContent, string importedBy,
            CancellationToken cancellationToken = default)
        {
            var importData = JsonSerializer.Deserialize<JsonElement>(jsonContent);
            var name = importData.GetProperty("Name").GetString() ?? "Imported";
            var description = importData.GetProperty("Description").GetString() ?? "";

            Dictionary<string, object>? parameters = null;
            if (importData.TryGetProperty("Parameters", out var paramsElement))
            {
                parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(paramsElement.GetRawText());
            }

            var recipe = RecipeData.CreateNew(name + "_导入", description, parameters, importedBy);

            await _recipeRepository.AddAsync(recipe, cancellationToken);
            await _recipeRepository.SaveChangesAsync(cancellationToken);

            _logService.Info("RecipeService", $"导入配方：{recipe.Name}");
            return recipe;
        }
    }
}
