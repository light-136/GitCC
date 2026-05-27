// ============================================================
// 文件：UserManager.cs
// 用途：用户管理与权限控制
// 设计思路：
//   工业设备的权限管理要求：
//   - 操作员只能启动/停止设备，不能修改参数
//   - 工程师可以修改参数、配方、手动控制
//   - 管理员拥有所有权限
//   密码使用SHA256哈希存储，不保存明文。
// ============================================================

using System.Security.Cryptography;
using System.Text;
using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Events;
using SmartSemiCon.Domain.Interfaces;
using SmartSemiCon.Domain.Models;

namespace SmartSemiCon.Application.User
{
    /// <summary>
    /// 用户管理器。
    /// </summary>
    public class UserManager : IUserService
    {
        private readonly List<UserInfo> _users = new();
        private readonly IEventBus _eventBus;
        private readonly ILogService _logService;

        /// <summary>当前登录用户</summary>
        public UserInfo? CurrentUser { get; private set; }

        /// <summary>登录状态变更事件</summary>
        public event EventHandler<UserInfo?>? UserChanged;

        public UserManager(IEventBus eventBus, ILogService logService)
        {
            _eventBus = eventBus;
            _logService = logService;
            InitializeDefaultUsers();
        }

        /// <summary>
        /// 初始化默认用户。
        /// </summary>
        private void InitializeDefaultUsers()
        {
            _users.AddRange(new[]
            {
                new UserInfo
                {
                    Id = 1, Username = "operator", DisplayName = "操作员",
                    Role = UserRole.Operator, PasswordHash = HashPassword("123456"),
                    IsEnabled = true
                },
                new UserInfo
                {
                    Id = 2, Username = "engineer", DisplayName = "工程师",
                    Role = UserRole.Engineer, PasswordHash = HashPassword("engineer"),
                    IsEnabled = true
                },
                new UserInfo
                {
                    Id = 3, Username = "admin", DisplayName = "管理员",
                    Role = UserRole.Administrator, PasswordHash = HashPassword("admin123"),
                    IsEnabled = true
                }
            });
        }

        /// <summary>用户登录。</summary>
        public Task<bool> LoginAsync(string username, string password)
        {
            var user = _users.FirstOrDefault(u =>
                u.Username == username &&
                u.PasswordHash == HashPassword(password) &&
                u.IsEnabled);

            if (user == null) return Task.FromResult(false);

            user.LastLoginAt = DateTime.Now;
            CurrentUser = user;

            _eventBus.Publish(new UserLoggedInEvent { User = user, Source = "用户管理" });
            UserChanged?.Invoke(this, user);

            _logService.Log(Domain.Enums.LogLevel.Info, "用户管理",
                $"用户登录: {user.DisplayName} ({user.Role})");

            return Task.FromResult(true);
        }

        /// <summary>用户登出。</summary>
        public void Logout()
        {
            if (CurrentUser != null)
            {
                _logService.Log(Domain.Enums.LogLevel.Info, "用户管理",
                    $"用户登出: {CurrentUser.DisplayName}");
            }
            CurrentUser = null;
            UserChanged?.Invoke(this, null);
        }

        /// <summary>检查当前用户是否具有指定权限。</summary>
        public bool HasPermission(UserRole requiredRole)
        {
            if (CurrentUser == null) return false;
            return CurrentUser.Role >= requiredRole;
        }

        /// <summary>SHA256密码哈希。</summary>
        private static string HashPassword(string password)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(bytes);
        }
    }
}
