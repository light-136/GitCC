namespace SmartHMI.Core.Models;

public class RecipeModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0";
    public string ProductType { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;
    public string CreatedBy { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public Dictionary<string, string> Parameters { get; set; } = new();
}
