using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using SmartHMI.Modules.Recipe;
using SmartHMI.Modules.Traceability;

namespace SmartHMI.Tests;

public class RecipeServiceTests
{
    private static RecipeService CreateService() => new();

    [Fact]
    public void GetAll_ShouldReturnSeededRecipes()
    {
        var svc = CreateService();
        Assert.Equal(2, svc.GetAll().Count);
    }

    [Fact]
    public void Add_ShouldIncreaseCount()
    {
        var svc = CreateService();
        svc.Add(new RecipeModel { Name = "测试配方", ProductType = "TestProduct" });
        Assert.Equal(3, svc.GetAll().Count);
    }

    [Fact]
    public void GetById_ShouldReturnCorrectRecipe()
    {
        var svc = CreateService();
        var recipe = new RecipeModel { Name = "查找测试", ProductType = "P1" };
        svc.Add(recipe);
        var found = svc.GetById(recipe.Id);
        Assert.NotNull(found);
        Assert.Equal("查找测试", found.Name);
    }

    [Fact]
    public void Delete_ShouldRemoveRecipe()
    {
        var svc = CreateService();
        var recipe = new RecipeModel { Name = "删除测试", ProductType = "P1" };
        svc.Add(recipe);
        svc.Delete(recipe.Id);
        Assert.Null(svc.GetById(recipe.Id));
    }

    [Fact]
    public void SetActive_ShouldFireRecipeAppliedEvent()
    {
        var svc = CreateService();
        RecipeModel? applied = null;
        svc.RecipeApplied += (_, r) => applied = r;
        var id = svc.GetAll()[0].Id;
        svc.SetActive(id);
        Assert.NotNull(applied);
        Assert.Equal(id, applied.Id);
    }

    [Fact]
    public void SetActive_ShouldDeactivateOthersOfSameProductType()
    {
        var svc = CreateService();
        var r1 = new RecipeModel { Name = "R1", ProductType = "SameType" };
        var r2 = new RecipeModel { Name = "R2", ProductType = "SameType" };
        svc.Add(r1);
        svc.Add(r2);
        svc.SetActive(r1.Id);
        svc.SetActive(r2.Id);
        Assert.False(svc.GetById(r1.Id)!.IsActive);
        Assert.True(svc.GetById(r2.Id)!.IsActive);
    }
}

public class TraceabilityServiceTests
{
    [Fact]
    public void Record_ShouldBeRetrievableBySerial()
    {
        var svc = new TraceabilityService();
        svc.Record(new TraceRecord { SerialNumber = "SN001", WorkorderId = "WO001", EventType = TraceEventType.Complete });
        var records = svc.GetBySerial("SN001");
        Assert.Single(records);
        Assert.Equal("SN001", records[0].SerialNumber);
    }

    [Fact]
    public void GetByWorkorder_ShouldReturnMatchingRecords()
    {
        var svc = new TraceabilityService();
        svc.Record(new TraceRecord { SerialNumber = "SN001", WorkorderId = "WO-A", EventType = TraceEventType.Start });
        svc.Record(new TraceRecord { SerialNumber = "SN002", WorkorderId = "WO-A", EventType = TraceEventType.Complete });
        svc.Record(new TraceRecord { SerialNumber = "SN003", WorkorderId = "WO-B", EventType = TraceEventType.Start });
        var records = svc.GetByWorkorder("WO-A");
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void GetRecent_ShouldReturnLastN()
    {
        var svc = new TraceabilityService();
        for (int i = 0; i < 10; i++)
            svc.Record(new TraceRecord { SerialNumber = $"SN{i:D3}", WorkorderId = "WO-X", EventType = TraceEventType.StepComplete });
        var recent = svc.GetRecent(5);
        Assert.Equal(5, recent.Count);
    }
}
