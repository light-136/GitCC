using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using System.Collections.ObjectModel;

namespace SmartHMI.UI.ViewModels;

public class RecipeViewModel : BaseViewModel
{
    private readonly IRecipeService _recipeService;

    public ObservableCollection<RecipeModel> Recipes { get; } = new();

    private RecipeModel? _selectedRecipe;
    public RecipeModel? SelectedRecipe { get => _selectedRecipe; set { SetField(ref _selectedRecipe, value); OnPropertyChanged(nameof(HasSelection)); } }

    public bool HasSelection => _selectedRecipe != null;

    private string _newName = "";
    public string NewName { get => _newName; set => SetField(ref _newName, value); }

    private string _newProductType = "";
    public string NewProductType { get => _newProductType; set => SetField(ref _newProductType, value); }

    private string _statusMessage = "";
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public RelayCommand SetActiveCommand { get; }
    public RelayCommand AddCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public RecipeViewModel(IRecipeService recipeService)
    {
        _recipeService = recipeService;
        SetActiveCommand = new RelayCommand(_ => SetActive(), _ => HasSelection);
        AddCommand = new RelayCommand(_ => AddRecipe(), _ => !string.IsNullOrWhiteSpace(NewName));
        DeleteCommand = new RelayCommand(_ => DeleteRecipe(), _ => HasSelection);
        RefreshCommand = new RelayCommand(_ => LoadRecipes());
        LoadRecipes();
    }

    private void LoadRecipes()
    {
        Recipes.Clear();
        foreach (var r in _recipeService.GetAll()) Recipes.Add(r);
    }

    private void SetActive()
    {
        if (_selectedRecipe == null) return;
        _recipeService.SetActive(_selectedRecipe.Id);
        StatusMessage = $"已激活配方：{_selectedRecipe.Name}";
        LoadRecipes();
    }

    private void AddRecipe()
    {
        var recipe = new RecipeModel { Name = NewName, ProductType = NewProductType, CreatedBy = "operator" };
        _recipeService.Add(recipe);
        NewName = "";
        NewProductType = "";
        StatusMessage = $"已添加配方：{recipe.Name}";
        LoadRecipes();
    }

    private void DeleteRecipe()
    {
        if (_selectedRecipe == null) return;
        _recipeService.Delete(_selectedRecipe.Id);
        StatusMessage = $"已删除配方：{_selectedRecipe.Name}";
        SelectedRecipe = null;
        LoadRecipes();
    }
}
