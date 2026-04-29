using SmartHMI.Core.Models;

namespace SmartHMI.Core.Interfaces;

public interface IUserService
{
    UserModel? CurrentUser { get; }
    bool Login(string username, string password);
    void Logout();
    IReadOnlyList<UserModel> GetAllUsers();
    void AddUser(UserModel user);
    void UpdateUser(UserModel user);
    void DeleteUser(string username);
    bool HasPermission(string permission);
    event EventHandler<UserModel?>? UserChanged;
}
