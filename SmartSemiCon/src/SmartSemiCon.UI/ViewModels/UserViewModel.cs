// ============================================================
// 文件：UserViewModel.cs
// 用途：用户管理页面ViewModel
// 设计思路：
//   工业设备的用户管理强调权限分级：
//   - Operator（操作员）：仅能启停设备、确认报警
//   - Engineer（工程师）：可修改参数、配方、手动控制
//   - Administrator（管理员）：所有权限+用户管理
//   此页面提供登录/登出、用户信息显示功能。
// ============================================================

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;

namespace SmartSemiCon.UI.ViewModels
{
    /// <summary>
    /// 用户管理页面ViewModel。
    /// </summary>
    public partial class UserViewModel : ObservableObject
    {
        private readonly IUserService _userService;
        private readonly ILogService _logService;

        [ObservableProperty]
        private string _username = "";

        [ObservableProperty]
        private string _password = "";

        [ObservableProperty]
        private string _loginStatus = "未登录";

        [ObservableProperty]
        private string _currentRole = "-";

        [ObservableProperty]
        private string _lastLoginTime = "-";

        [ObservableProperty]
        private bool _isLoggedIn;

        [ObservableProperty]
        private string _statusMessage = "";

        public UserViewModel(IUserService userService, ILogService logService)
        {
            _userService = userService;
            _logService = logService;

            _userService.UserChanged += (_, user) =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() => UpdateUserDisplay(user));
            };

            UpdateUserDisplay(_userService.CurrentUser);
        }

        /// <summary>登录</summary>
        [RelayCommand]
        private async Task Login()
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                StatusMessage = "请输入用户名";
                return;
            }

            var success = await _userService.LoginAsync(Username, Password);
            if (success)
            {
                StatusMessage = $"登录成功！欢迎 {_userService.CurrentUser?.DisplayName}";
                Password = "";
            }
            else
            {
                StatusMessage = "登录失败：用户名或密码错误";
            }
        }

        /// <summary>登出</summary>
        [RelayCommand]
        private void Logout()
        {
            _userService.Logout();
            StatusMessage = "已登出";
            Username = "";
            Password = "";
        }

        private void UpdateUserDisplay(UserInfo? user)
        {
            if (user != null)
            {
                IsLoggedIn = true;
                LoginStatus = $"已登录：{user.DisplayName}";
                CurrentRole = user.Role switch
                {
                    UserRole.Operator => "操作员",
                    UserRole.Engineer => "工程师",
                    UserRole.Administrator => "管理员",
                    _ => "未知"
                };
                LastLoginTime = user.LastLoginAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
            }
            else
            {
                IsLoggedIn = false;
                LoginStatus = "未登录";
                CurrentRole = "-";
                LastLoginTime = "-";
            }
        }
    }
}
