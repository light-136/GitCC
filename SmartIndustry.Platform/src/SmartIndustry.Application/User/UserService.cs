// ============================================================
// 文件：UserService.cs
// 层次：应用层 (Application Layer) — 用户服务
// 职责：实现 IUserService 接口
// ============================================================

using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Enums;
using SmartIndustry.Domain.Events;
using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Interfaces.Repositories;
using System.Collections.Concurrent;

namespace SmartIndustry.Application.User
{
    /// <summary>
    /// 用户服务实现
    /// </summary>
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IEventBus _eventBus;
        private readonly ILogService _logService;

        // Token 存储：Token → (UserId, Expiry)
        private readonly ConcurrentDictionary<string, (Guid UserId, DateTime Expiry)> _tokens = new();

        // 角色权限映射
        private static readonly Dictionary<UserRole, HashSet<string>> _rolePermissions = new()
        {
            [UserRole.Viewer] = new()
            {
                "dashboard.view", "alarm.view", "log.view", "recipe.view"
            },
            [UserRole.Operator] = new()
            {
                "dashboard.view", "alarm.view", "alarm.acknowledge", "log.view",
                "recipe.view", "recipe.load", "motion.jog", "motion.home",
                "vision.capture", "communication.view"
            },
            [UserRole.Engineer] = new()
            {
                "dashboard.view", "alarm.view", "alarm.acknowledge", "alarm.clear", "log.view", "log.export",
                "recipe.view", "recipe.load", "recipe.edit", "recipe.create", "recipe.export", "recipe.import",
                "motion.jog", "motion.home", "motion.absolute", "motion.config",
                "vision.capture", "vision.config", "vision.calibrate",
                "communication.view", "communication.config",
                "settings.view", "settings.system"
            },
            [UserRole.Administrator] = new()
            {
                "dashboard.view", "alarm.view", "alarm.acknowledge", "alarm.clear", "log.view", "log.export",
                "recipe.view", "recipe.load", "recipe.edit", "recipe.create", "recipe.delete", "recipe.export", "recipe.import",
                "motion.jog", "motion.home", "motion.absolute", "motion.config",
                "vision.capture", "vision.config", "vision.calibrate",
                "communication.view", "communication.config",
                "settings.view", "settings.system", "settings.user",
                "user.create", "user.edit", "user.delete", "user.password"
            }
        };

        public UserService(
            IUserRepository userRepository,
            IEventBus eventBus,
            ILogService logService)
        {
            _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        public async Task<LoginResult> LoginAsync(string username, string password,
            CancellationToken cancellationToken = default)
        {
            var user = await _userRepository.AuthenticateAsync(username, password, cancellationToken);
            if (user == null)
            {
                _logService.Warning("UserService", $"登录失败：用户 {username}");
                return LoginResult.Failure("用户名或密码错误，或账户已被锁定");
            }

            // 生成 Token
            var token = Guid.NewGuid().ToString("N");
            var expiry = DateTime.UtcNow.AddHours(8);
            _tokens[token] = (user.Id, expiry);

            // 发布登录事件
            await _eventBus.PublishAsync(
                new UserLoginEvent(username, user.Role, DateTime.Now), cancellationToken);

            _logService.Info("UserService", $"用户登录成功：{username}（{user.Role}）");
            return LoginResult.Success(user, token, expiry);
        }

        public async Task LogoutAsync(Guid userId, string token,
            CancellationToken cancellationToken = default)
        {
            _tokens.TryRemove(token, out _);

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user != null)
            {
                await _eventBus.PublishAsync(
                    new UserLogoutEvent(user.Username, DateTime.Now), cancellationToken);
                _logService.Info("UserService", $"用户注销：{user.Username}");
            }
        }

        public async Task<UserAccount> CreateUser(string username, string password,
            string displayName, UserRole role, string? email, string createdBy,
            CancellationToken cancellationToken = default)
        {
            if (await _userRepository.IsUsernameExistsAsync(username, cancellationToken))
                throw new InvalidOperationException($"用户名 {username} 已存在");

            var user = new UserAccount
            {
                Username = username,
                DisplayName = displayName,
                Role = role,
                Email = email ?? "",
                IsEnabled = true,
                CreatedBy = createdBy
            };

            await _userRepository.AddAsync(user, cancellationToken);
            await _userRepository.SaveChangesAsync(cancellationToken);

            await _userRepository.UpdatePasswordAsync(user.Id, password, cancellationToken);

            _logService.Info("UserService", $"创建用户：{username}（{role}），操作人：{createdBy}");
            return user;
        }

        public async Task UpdateUser(Guid userId, string? displayName, string? email,
            UserRole? role, string operatedBy, CancellationToken cancellationToken = default)
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
                ?? throw new InvalidOperationException($"用户 {userId} 不存在");

            if (displayName != null) user.DisplayName = displayName;
            if (email != null) user.Email = email;
            if (role.HasValue) user.Role = role.Value;
            user.UpdatedBy = operatedBy;

            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync(cancellationToken);

            _logService.Info("UserService", $"更新用户信息：{user.Username}，操作人：{operatedBy}");
        }

        public async Task DeleteUser(Guid userId, string operatedBy,
            CancellationToken cancellationToken = default)
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
                ?? throw new InvalidOperationException($"用户 {userId} 不存在");

            _userRepository.Delete(user, operatedBy);
            await _userRepository.SaveChangesAsync(cancellationToken);

            _logService.Info("UserService", $"删除用户：{user.Username}，操作人：{operatedBy}");
        }

        public async Task ChangePassword(Guid userId, string? oldPassword, string newPassword,
            string operatedBy, CancellationToken cancellationToken = default)
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
                ?? throw new InvalidOperationException($"用户 {userId} 不存在");

            // 非管理员重置模式需验证旧密码
            if (oldPassword != null)
            {
                var authResult = await _userRepository.AuthenticateAsync(
                    user.Username, oldPassword, cancellationToken);
                if (authResult == null)
                    throw new InvalidOperationException("旧密码验证失败");
            }

            await _userRepository.UpdatePasswordAsync(userId, newPassword, cancellationToken);
            _logService.Info("UserService", $"用户 {user.Username} 修改密码，操作人：{operatedBy}");
        }

        public async Task<UserAccount?> GetCurrentUser(string token,
            CancellationToken cancellationToken = default)
        {
            if (!_tokens.TryGetValue(token, out var session)) return null;
            if (session.Expiry < DateTime.UtcNow)
            {
                _tokens.TryRemove(token, out _);
                return null;
            }
            return await _userRepository.GetByIdAsync(session.UserId, cancellationToken);
        }

        public async Task<IReadOnlyList<UserAccount>> GetAllUsers(
            CancellationToken cancellationToken = default)
        {
            return await _userRepository.GetAllAsync(cancellationToken);
        }

        public async Task<bool> HasPermission(Guid userId, string permission,
            CancellationToken cancellationToken = default)
        {
            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null) return false;

            return _rolePermissions.TryGetValue(user.Role, out var permissions)
                && permissions.Contains(permission);
        }

        /// <summary>清理过期 Token（由调度器定期调用）</summary>
        public void CleanExpiredTokens()
        {
            var now = DateTime.UtcNow;
            var expired = _tokens.Where(t => t.Value.Expiry < now).Select(t => t.Key).ToList();
            foreach (var token in expired)
                _tokens.TryRemove(token, out _);
        }
    }
}
