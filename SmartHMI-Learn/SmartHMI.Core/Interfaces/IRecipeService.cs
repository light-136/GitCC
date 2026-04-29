using SmartHMI.Core.Models;

namespace SmartHMI.Core.Interfaces;

public interface IRecipeService
{
    IReadOnlyList<RecipeModel> GetAll();
    RecipeModel? GetById(Guid id);
    RecipeModel? GetActive(string productType);
    void Add(RecipeModel recipe);
    void Update(RecipeModel recipe);
    void Delete(Guid id);
    void SetActive(Guid id);
    event EventHandler<RecipeModel>? RecipeApplied;
}
