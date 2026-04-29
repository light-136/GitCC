using SmartMES.Core.Infrastructure;
using SmartMES.Core.Interfaces;
using SmartMES.Core.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartMES.UI.ViewModels
{
    /// <summary>
    /// 用户管理 ViewModel：支持登录/登出/新增/编辑/删除用户 + 权限分配。
    /// </summary>
    public class UserViewModel : ViewModelBase
    {
        private readonly IEventBus _eventBus;
        private readonly ILoggingService _logger;

        public ObservableCollection<UserModel> Users { get; } = new()
        {
            new()
            {
                Username = "admin",
                Password = "admin123",
                DisplayName = "管理员",
                Role = UserRole.Admin,
                IsActive = true,
                Permissions = PagePermissions.ForRole(UserRole.Admin)
            },
            new()
            {
                Username = "operator",
                Password = "op123",
                DisplayName = "操作员甲",
                Role = UserRole.Operator,
                IsActive = true,
                Permissions = PagePermissions.ForRole(UserRole.Operator)
            },
            new()
            {
                Username = "viewer",
                Password = "view123",
                DisplayName = "观察员",
                Role = UserRole.Viewer,
                IsActive = true,
                Permissions = PagePermissions.ForRole(UserRole.Viewer)
            },
        };

        private string _username = string.Empty;
        public string Username { get => _username; set => SetProperty(ref _username, value); }

        private string _password = string.Empty;
        public string Password { get => _password; set => SetProperty(ref _password, value); }

        private UserModel? _currentUser;
        public UserModel? CurrentUser
        {
            get => _currentUser;
            set
            {
                if (!SetProperty(ref _currentUser, value)) return;
                OnPropertyChanged(nameof(IsLoggedIn));
                OnPropertyChanged(nameof(IsAdmin));
                OnPropertyChanged(nameof(RoleText));
                OnPropertyChanged(nameof(Permissions));
                RefreshCommandState();
            }
        }

        private string _loginMessage = "请输入用户名和密码";
        public string LoginMessage { get => _loginMessage; set => SetProperty(ref _loginMessage, value); }

        public bool IsLoggedIn => _currentUser != null;
        public bool IsAdmin => _currentUser?.Role == UserRole.Admin;
        public PagePermissions? Permissions => _currentUser?.Permissions;

        public string RoleText => _currentUser?.Role switch
        {
            UserRole.Admin => "管理员",
            UserRole.Operator => "操作员",
            UserRole.Viewer => "观察员",
            _ => "未登录"
        };

        private UserModel? _selectedUser;
        public UserModel? SelectedUser
        {
            get => _selectedUser;
            set
            {
                if (!SetProperty(ref _selectedUser, value)) return;
                EditUser = value == null ? null : CloneUser(value);
                RefreshCommandState();
            }
        }

        private UserModel? _editUser;
        public UserModel? EditUser
        {
            get => _editUser;
            set
            {
                if (!SetProperty(ref _editUser, value)) return;
                RefreshCommandState();
            }
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (!SetProperty(ref _isEditing, value)) return;
                RefreshCommandState();
            }
        }

        public RelayCommand LoginCommand { get; }
        public RelayCommand LogoutCommand { get; }
        public RelayCommand NewUserCommand { get; }
        public RelayCommand SaveUserCommand { get; }
        public RelayCommand DeleteUserCommand { get; }
        public RelayCommand CancelEditCommand { get; }

        public UserViewModel(IEventBus eventBus, ILoggingService logger)
        {
            _eventBus = eventBus;
            _logger = logger;

            LoginCommand = new RelayCommand(_ => Login(), _ => !IsLoggedIn);
            LogoutCommand = new RelayCommand(_ => Logout(), _ => IsLoggedIn);
            NewUserCommand = new RelayCommand(_ => NewUser(), _ => IsAdmin);
            SaveUserCommand = new RelayCommand(_ => SaveUser(), _ => IsAdmin && EditUser != null);
            DeleteUserCommand = new RelayCommand(_ => DeleteUser(), _ => IsAdmin && SelectedUser != null);
            CancelEditCommand = new RelayCommand(_ =>
            {
                IsEditing = false;
                EditUser = null;
            });
        }

        private void Login()
        {
            var user = Users.FirstOrDefault(u =>
                u.Username == _username &&
                u.Password == _password &&
                u.IsActive);

            if (user == null)
            {
                LoginMessage = "✘ 用户名或密码错误";
                _logger.LogWarning($"登录失败: [{_username}]", "UserSystem");
                return;
            }

            user.LastLoginAt = DateTime.Now;
            CurrentUser = user;
            LoginMessage = $"✔ 欢迎，{user.DisplayName} [{RoleText}]";
            Password = string.Empty;

            _logger.LogInfo($"用户 [{user.Username}] 登录，角色: {user.Role}", "UserSystem");
            _eventBus.Publish(new UserLoginEvent
            {
                Username = user.DisplayName,
                IsLogin = true,
                Role = RoleText,
                Permissions = user.Permissions
            });
        }

        private void Logout()
        {
            var name = _currentUser?.Username ?? string.Empty;
            CurrentUser = null;
            Username = string.Empty;
            LoginMessage = "已登出";

            _logger.LogInfo($"用户 [{name}] 已登出", "UserSystem");
            _eventBus.Publish(new UserLoginEvent
            {
                Username = name,
                IsLogin = false,
                Role = string.Empty,
                Permissions = null
            });
        }

        private void NewUser()
        {
            EditUser = new UserModel
            {
                Role = UserRole.Operator,
                Permissions = PagePermissions.ForRole(UserRole.Operator),
                IsActive = true
            };
            IsEditing = true;
        }

        private void SaveUser()
        {
            if (EditUser == null) return;

            if (string.IsNullOrWhiteSpace(EditUser.Username))
            {
                MessageBox.Show("用户名不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Users.Any(u => u.Username == EditUser.Username && u.Id != EditUser.Id))
            {
                MessageBox.Show("用户名已存在", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var existing = Users.FirstOrDefault(u => u.Id == EditUser.Id);
            if (existing != null)
            {
                var idx = Users.IndexOf(existing);
                Users[idx] = EditUser;
            }
            else
            {
                Users.Add(EditUser);
            }

            _logger.LogInfo($"用户 [{EditUser.Username}] 已保存", "UserSystem");
            IsEditing = false;
            EditUser = null;
        }

        private void DeleteUser()
        {
            if (SelectedUser == null) return;

            if (SelectedUser.Username == "admin")
            {
                MessageBox.Show("不能删除内置 admin 账号", "提示");
                return;
            }

            Users.Remove(SelectedUser);
            _logger.LogInfo($"用户 [{SelectedUser.Username}] 已删除", "UserSystem");
        }

        private void RefreshCommandState()
        {
            LoginCommand.RaiseCanExecuteChanged();
            LogoutCommand.RaiseCanExecuteChanged();
            NewUserCommand.RaiseCanExecuteChanged();
            SaveUserCommand.RaiseCanExecuteChanged();
            DeleteUserCommand.RaiseCanExecuteChanged();
            CancelEditCommand.RaiseCanExecuteChanged();
        }

        private static UserModel CloneUser(UserModel src) => new()
        {
            Id = src.Id,
            Username = src.Username,
            DisplayName = src.DisplayName,
            Password = src.Password,
            Role = src.Role,
            IsActive = src.IsActive,
            LastLoginAt = src.LastLoginAt,
            Permissions = new PagePermissions
            {
                Dashboard = src.Permissions.Dashboard,
                Device = src.Permissions.Device,
                Communication = src.Permissions.Communication,
                Alarm = src.Permissions.Alarm,
                Log = src.Permissions.Log,
                Automation = src.Permissions.Automation,
                MesComm = src.Permissions.MesComm,
                FileProcess = src.Permissions.FileProcess,
                Database = src.Permissions.Database,
                Motion = src.Permissions.Motion,
                Native = src.Permissions.Native,
                Settings = src.Permissions.Settings,
                UserManage = src.Permissions.UserManage,
                Motion10Axis = src.Permissions.Motion10Axis,
                Vision = src.Permissions.Vision,
                VisionMotion = src.Permissions.VisionMotion,
                Industrial = src.Permissions.Industrial
            }
        };
    }
}
