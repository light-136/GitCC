using SmartMES.Core.Recipe;

namespace SmartMES.Tests;

public class RecipeServiceTests
{
    [Fact]
    public void Constructor_ShouldLoadSamplesAndActivateDefault()
    {
        var svc = new RecipeService();

        Assert.True(svc.GetAll().Count >= 3);
        Assert.NotNull(svc.ActiveRecipe);
        Assert.Equal("产品A-标准", svc.ActiveRecipe!.Name);
    }

    [Fact]
    public void Activate_ShouldSwitchActiveRecipe()
    {
        var svc = new RecipeService();

        var ok = svc.Activate("产品B-精密");

        Assert.True(ok);
        Assert.Equal("产品B-精密", svc.ActiveRecipe!.Name);
    }

    [Fact]
    public async Task SaveAndLoad_ShouldPersistRecipes()
    {
        var svc = new RecipeService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"smartmes_recipe_{Guid.NewGuid():N}.json");

        try
        {
            svc.Add(new RecipeModel { Name = "UT-Recipe", ProductCode = "UT001" });
            await svc.SaveAsync(tempFile);

            var svc2 = new RecipeService();
            await svc2.LoadAsync(tempFile);

            Assert.NotNull(svc2.GetByName("UT-Recipe"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
