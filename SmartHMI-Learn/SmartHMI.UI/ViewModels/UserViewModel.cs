using System.Collections.ObjectModel;
using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.UI.ViewModels;

public class UserViewModel : BaseViewModel
{
    private readonly IUserService _userService;
    private UserModel? _selectedUser;
    private string _newUsername = "", _newPassword = "", _newDisplayName = "";
    private UserRole _newRole = UserRole.Operator;
    private string _statusMessage = "";

    public ObservableCollection<UserModel> Users { get; } = new();
    public UserModel? SelectedUser { get => _selectedUser; set => SetField(ref _selectedUser, value); }
    public string NewUsername { get => _newUsername; set => SetField(ref _newUsername, value); }
    public string NewPassword { get => _newPassword; set => SetField(ref _newPassword, value); }
    public string NewDisplayName { get => _newDisplayName; set => SetField(ref _newDisplayName, value); }
    public UserRole NewRole { get => _newRole; set => SetField(ref _newRole, value); }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public Array RoleValues => Enum.GetValues(typeof(UserRole));

    public RelayCommand AddUserCommand { get; }
    public RelayCommand DeleteUserCommand { get; }
    public RelayCommand RefreshCommand { get; }

    public UserViewModel(IUserService userService)
    {
        _userService = userService;

        AddUserCommand = new RelayCommand(AddUser,
            () => !string.IsNullOrWhiteSpace(NewUsername) && !string.IsNullOrWhiteSpace(NewPassword));
        DeleteUserCommand = new RelayCommand(DeleteUser,
            () => SelectedUser != null && SelectedUser.Username != "admin");
        RefreshCommand = new RelayCommand(Refresh);

        Refresh();
    }

    private void Refresh()
    {
        Users.Clear();
        foreach (var u in _userService.GetAllUsers())
            Users.Add(u);
    }

    private void AddUser()
    {
        if (Users.Any(u => u.Username == NewUsername))
        {
            StatusMessage = "用户名已存在";
            return;
        }
        var user = UserModel.CreateDefault(NewUsername, NewPassword, NewRole);
        user.DisplayName = string.IsNullOrWhiteSpace(NewDisplayName) ? NewUsername : NewDisplayName;
        _userService.AddUser(user);
        StatusMessage = $"用户 {NewUsername} 已创建";
        NewUsername = NewPassword = NewDisplayName = "";
        Refresh();
    }

    private void DeleteUser()
    {
        if (SelectedUser == null) return;
        _userService.DeleteUser(SelectedUser.Username);
        StatusMessage = $"用户 {SelectedUser.Username} 已删除";
        Refresh();
    }
}
