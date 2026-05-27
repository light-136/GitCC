// ============================================================
// 文件：IUserRepository.cs
// 层次：领域层 (Domain Layer) — 用户仓储接口
// 职责：扩展泛型仓储，添加用户认证和查询专用方法
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Entities;

namespace SmartIndustry.Domain.Interfaces.Repositories
{
    /// <summary>
    /// 用户账户专用仓储接口，在泛型 CRUD 基础上添加认证相关操作。
    /// </summary>
    public interface IUserRepository : IRepository<UserAccount>
    {
        /// <summary>
        /// 按用户名查询账户（大小写不敏感）。
        /// </summary>
        /// <param name="username">用户名（查询时自动规范化为小写）</param>
        Task<UserAccount?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

        /// <summary>
        /// 验证用户身份（用户名 + 明文密码）。
        /// Infrastructure 实现负责密码哈希比较，不暴露哈希细节给调用方。
        /// </summary>
        /// <param name="username">用户名</param>
        /// <param name="plainPassword">明文密码（Infrastructure 层内部完成哈希计算）</param>
        /// <returns>认证成功返回 UserAccount 实体，认证失败返回 null</returns>
        Task<UserAccount?> AuthenticateAsync(string username, string plainPassword,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 检查用户名是否已被占用（创建新账户前调用）。
        /// </summary>
        Task<bool> IsUsernameExistsAsync(string username, CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新用户密码（Infrastructure 层内部完成新盐值生成和哈希计算）。
        /// </summary>
        /// <param name="userId">用户ID</param>
        /// <param name="newPlainPassword">新明文密码</param>
        Task UpdatePasswordAsync(Guid userId, string newPlainPassword,
            CancellationToken cancellationToken = default);
    }
}
