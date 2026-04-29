namespace SmartHMI.Core.Models;

public enum UserRole { Viewer, Operator, Engineer, Admin }

public class UserModel
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Viewer;
    public List<string> Permissions { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    public static UserModel CreateDefault(string username, string password, UserRole role)
    {
        return new UserModel
        {
            Username = username,
            PasswordHash = HashPassword(password),
            DisplayName = username,
            Role = role,
            Permissions = GetDefaultPermissions(role)
        };
    }

    public static string HashPassword(string password) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(password)));

    private static List<string> GetDefaultPermissions(UserRole role) => role switch
    {
        UserRole.Admin => ["*"],
        UserRole.Engineer => ["view", "control", "config", "recipe"],
        UserRole.Operator => ["view", "control"],
        _ => ["view"]
    };
}
