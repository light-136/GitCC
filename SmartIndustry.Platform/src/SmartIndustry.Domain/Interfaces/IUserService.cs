// ============================================================
// 文件：IUserService.cs
// 层次：领域层 (Domain Layer) — 接口
// 职责：定义用户认证和权限管理服务接口
// 设计思路：
//   IUserService 封装了用户的完整生命周期：
//     - 认证：LoginAsync/LogoutAsync（返回 JWT Token 或 Session）
//     - 用户管理：CreateUser/UpdateUser/DeleteUser
//     - 密码管理：ChangePassword（旧密码验证 + 新密码哈希 + 存储）
//     - 权限判断：HasPermission（角色 + 细粒度权限联合判断）
//     - 会话查询：GetCurrentUser（从 Session/Token 中解析当前登录用户）
//   安全设计：
//     - LoginAsync 返回结果而非直接操作实体，避免暴露密码哈希
//     - 失败登录次数由 UserAccount 实体内部追踪（领域逻辑）
//     - 会话管理（Token 生成/验证）在 Infrastructure 层实现
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Domain.Interfaces
{
    /// <summary>
    /// 登录结果数据类（避免直接暴露 UserAccount 实体给 UI 层）。
    /// </summary>
    public sealed class LoginResult
    {
        /// <summary>是否登录成功</summary>
        public bool IsSuccess { get; init; }

        /// <summary>失败原因（成功时为 null）</summary>
        public string? FailureReason { get; init; }

        /// <summary>登录成功的用户账户（失败时为 null）</summary>
        public UserAccount? User { get; init; }

        /// <summary>会话令牌（JWT 或 GUID Session Token，失败时为 null）</summary>
        public string? Token { get; init; }

        /// <summary>令牌过期时间（UTC，失败时为 null）</summary>
        public DateTime? TokenExpiry { get; init; }

        /// <summary>创建登录成功结果</summary>
        public static LoginResult Success(UserAccount user, string token, DateTime expiry)
            => new() { IsSuccess = true, User = user, Token = token, TokenExpiry = expiry };

        /// <summary>创建登录失败结果</summary>
        public static LoginResult Failure(string reason)
            => new() { IsSuccess = false, FailureReason = reason };
    }

    /// <summary>
    /// 用户认证与权限管理服务接口。
    /// </summary>
    public interface IUserService
    {
        // ----------------------------------------------------------------
        // 认证操作
        // ----------------------------------------------------------------

        /// <summary>
        /// 用户登录认证（验证用户名和密码，成功后返回会话令牌）。
        /// </summary>
        /// <param name="username">用户名（不区分大小写）</param>
        /// <param name="password">明文密码（服务内部计算哈希后验证）</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task<LoginResult> LoginAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 用户登出（撤销会话令牌，发布 UserLogoutEvent）。
        /// </summary>
        /// <param name="userId">要登出的用户 ID</param>
        /// <param name="token">要撤销的会话令牌</param>
        Task LogoutAsync(Guid userId, string token, CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 用户管理（Administrator 角色操作）
        // ----------------------------------------------------------------

        /// <summary>
        /// 创建新用户账户（仅管理员可调用）。
        /// </summary>
        Task<UserAccount> CreateUser(
            string username,
            string password,
            string displayName,
            UserRole role,
            string? email,
            string createdBy,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新用户基本信息（显示名称、邮箱、角色）。
        /// </summary>
        Task UpdateUser(
            Guid userId,
            string? displayName,
            string? email,
            UserRole? role,
            string operatedBy,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 删除用户（软删除，保留审计记录）。
        /// </summary>
        Task DeleteUser(Guid userId, string operatedBy, CancellationToken cancellationToken = default);

        /// <summary>
        /// 修改用户密码（需要提供旧密码或管理员重置模式）。
        /// </summary>
        /// <param name="userId">目标用户 ID</param>
        /// <param name="oldPassword">旧密码（普通用户自改时提供，管理员重置时传 null）</param>
        /// <param name="newPassword">新密码</param>
        /// <param name="operatedBy">操作人（管理员重置时与 userId 不同）</param>
        Task ChangePassword(
            Guid userId,
            string? oldPassword,
            string newPassword,
            string operatedBy,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 用户查询
        // ----------------------------------------------------------------

        /// <summary>
        /// 从会话令牌解析当前登录用户（用于 API 请求的身份验证中间件）。
        /// </summary>
        /// <returns>当前登录用户，令牌无效或过期时返回 null</returns>
        Task<UserAccount?> GetCurrentUser(string token, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取所有用户列表（不含软删除用户）。
        /// </summary>
        Task<IReadOnlyList<UserAccount>> GetAllUsers(CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 权限判断
        // ----------------------------------------------------------------

        /// <summary>
        /// 判断指定用户是否拥有某项权限（角色权限 + 细粒度权限联合判断）。
        /// </summary>
        /// <param name="userId">用户 ID</param>
        /// <param name="permission">权限字符串（如 "motion.jog"、"recipe.activate"）</param>
        Task<bool> HasPermission(Guid userId, string permission, CancellationToken cancellationToken = default);
    }
}
