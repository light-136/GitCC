using SmartHMI.Core.Events;
using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.Services;

public class UserService : IUserService
{
    private readonly List<UserModel> _users = new();
    private readonly IEventBus _eventBus;
    private UserModel? _currentUser;

    public UserModel? CurrentUser => _currentUser;
    public event EventHandler<UserModel?>? UserChanged;

    public UserService(IEventBus eventBus)
    {
        _eventBus = eventBus;
        SeedDefaultUsers();
    }

    private void SeedDefaultUsers()
    {
        _users.Add(UserModel.CreateDefault("admin", "admin123", UserRole.Admin));
        _users.Add(UserModel.CreateDefault("engineer", "eng123", UserRole.Engineer));
        _users.Add(UserModel.CreateDefault("operator", "op123", UserRole.Operator));
        _users.Add(UserModel.CreateDefault("viewer", "view123", UserRole.Viewer));
    }

    public bool Login(string username, string password)
    {
        var hash = UserModel.HashPassword(password);
        var user = _users.FirstOrDefault(u =>
            u.Username == username && u.PasswordHash == hash && u.IsActive);

        if (user == null) return false;

        user.LastLoginAt = DateTime.Now;
        _currentUser = user;
        UserChanged?.Invoke(this, user);
        _eventBus.Publish(new UserLoginEvent { User = user, IsLogin = true });
        return true;
    }

    public void Logout()
    {
        _currentUser = null;
        UserChanged?.Invoke(this, null);
        _eventBus.Publish(new UserLoginEvent { User = null, IsLogin = false });
    }

    public IReadOnlyList<UserModel> GetAllUsers() => _users.ToList();

    public void AddUser(UserModel user) => _users.Add(user);

    public void UpdateUser(UserModel user)
    {
        var idx = _users.FindIndex(u => u.Username == user.Username);
        if (idx >= 0) _users[idx] = user;
    }

    public void DeleteUser(string username) =>
        _users.RemoveAll(u => u.Username == username);

    public bool HasPermission(string permission)
    {
        if (_currentUser == null) return false;
        return _currentUser.Permissions.Contains("*") ||
               _currentUser.Permissions.Contains(permission);
    }
}
