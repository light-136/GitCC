using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.Modules.Recipe;

public class RecipeService : IRecipeService
{
    private readonly List<RecipeModel> _recipes = new();
    private readonly Lock _lock = new();

    public event EventHandler<RecipeModel>? RecipeApplied;

    public RecipeService() => SeedDefaultRecipes();

    private void SeedDefaultRecipes()
    {
        _recipes.Add(new RecipeModel
        {
            Name = "标准产品A配方",
            Version = "1.0",
            ProductType = "ProductA",
            Description = "标准产品A生产参数",
            CreatedBy = "admin",
            Parameters = new Dictionary<string, string>
            {
                ["Speed"] = "1200",
                ["Temperature"] = "75.0",
                ["Pressure"] = "6.5",
                ["CycleTime"] = "8.5",
                ["VisionJob"] = "InspectA"
            }
        });
        _recipes.Add(new RecipeModel
        {
            Name = "高速产品B配方",
            Version = "2.1",
            ProductType = "ProductB",
            Description = "高速产品B生产参数",
            CreatedBy = "engineer",
            Parameters = new Dictionary<string, string>
            {
                ["Speed"] = "1800",
                ["Temperature"] = "82.0",
                ["Pressure"] = "7.2",
                ["CycleTime"] = "6.0",
                ["VisionJob"] = "InspectB"
            }
        });
    }

    public IReadOnlyList<RecipeModel> GetAll() { lock (_lock) return _recipes.ToList(); }

    public RecipeModel? GetById(Guid id) { lock (_lock) return _recipes.FirstOrDefault(r => r.Id == id); }

    public RecipeModel? GetActive(string productType)
    { lock (_lock) return _recipes.FirstOrDefault(r => r.ProductType == productType && r.IsActive); }

    public void Add(RecipeModel recipe) { lock (_lock) _recipes.Add(recipe); }

    public void Update(RecipeModel recipe)
    {
        lock (_lock)
        {
            var idx = _recipes.FindIndex(r => r.Id == recipe.Id);
            if (idx >= 0) { recipe.ModifiedAt = DateTime.Now; _recipes[idx] = recipe; }
        }
    }

    public void Delete(Guid id) { lock (_lock) _recipes.RemoveAll(r => r.Id == id); }

    public void SetActive(Guid id)
    {
        lock (_lock)
        {
            var recipe = _recipes.FirstOrDefault(r => r.Id == id);
            if (recipe == null) return;
            foreach (var r in _recipes.Where(r => r.ProductType == recipe.ProductType))
                r.IsActive = r.Id == id;
            RecipeApplied?.Invoke(this, recipe);
        }
    }
}
