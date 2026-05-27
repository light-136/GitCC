// ============================================================
// 文件：UserRepository.cs
// 层次：基础设施层 (Infrastructure Layer) — 用户仓储实现
// 职责：
//   继承 GenericRepository<UserAccount>，实现 IUserRepository 定义的
//   用户认证和账户管理专用方法。
// 设计思路：
//   密码认证是安全关键路径，所有密码操作在仓储内部完成，不暴露哈希细节：
//     1. 存储时：生成随机盐 + PBKDF2(SHA256) 哈希（迭代次数100000，密钥256bit）
//     2. 验证时：用存储的盐重新计算哈希，与数据库存储值做字节级比较
//   使用 System.Security.Cryptography.Rfc2898DeriveBytes 实现 PBKDF2，
//   符合 NIST SP 800-132 密码存储规范。
//   账户锁定检查在 AuthenticateAsync 中实现，锁定期间直接拒绝认证，
//   不会执行耗时的密码哈希计算（防止锁定绕过）。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using Microsoft.EntityFrameworkCore;
using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Interfaces.Repositories;
using SmartIndustry.Infrastructure.Database.Context;
using System.Security.Cryptography;
using System.Text;

namespace SmartIndustry.Infrastructure.Database.Repositories
{
    /// <summary>
    /// 用户账户专用仓储，实现 IUserRepository 定义的认证和账户管理方法。
    /// </summary>
    public class UserRepository : GenericRepository<UserAccount>, IUserRepository
    {
        // ----------------------------------------------------------------
        // 密码哈希参数常量（PBKDF2+SHA256）
        // ----------------------------------------------------------------

        /// <summary>PBKDF2 迭代次数（越高越安全，但越慢；100000 次符合当前安全建议）</summary>
        private const int PasswordHashIterations = 100_000;

        /// <summary>盐值长度（字节）：128bit = 16字节，足够防彩虹表攻击</summary>
        private const int SaltSizeBytes = 16;

        /// <summary>哈希输出长度（字节）：256bit = 32字节（SHA256 输出长度）</summary>
        private const int HashSizeBytes = 32;

        /// <summary>
        /// 构造函数
        /// </summary>
        public UserRepository(AppDbContext context) : base(context)
        {
        }

        // ================================================================
        // 用户专用查询实现
        // ================================================================

        /// <summary>
        /// 按用户名查询账户（大小写不敏感）。
        /// SQLite 默认大小写不敏感，但为了跨数据库兼容，显式调用 ToLower 规范化。
        /// </summary>
        public async Task<UserAccount?> GetByUsernameAsync(
            string username,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("用户名不能为空", nameof(username));

            // 规范化为小写，确保跨数据库一致性
            var normalizedUsername = username.Trim().ToLowerInvariant();

            return await _dbSet
                .FirstOrDefaultAsync(
                    u => u.Username.ToLower() == normalizedUsername,
                    cancellationToken);
        }

        /// <summary>
        /// 验证用户身份（用户名 + 明文密码认证）。
        /// 认证流程：
        ///   1. 按用户名查询账户（不存在则返回 null）
        ///   2. 检查账户是否启用（IsEnabled=false 则拒绝）
        ///   3. 检查账户是否被锁定（LockedUntil > UtcNow 则拒绝）
        ///   4. 验证密码哈希（使用存储的盐重新计算，字节级比较）
        ///   5. 认证失败：累计失败次数，可能触发锁定
        ///   6. 认证成功：清零失败次数，更新最后登录时间
        /// </summary>
        public async Task<UserAccount?> AuthenticateAsync(
            string username,
            string plainPassword,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("用户名不能为空", nameof(username));
            if (string.IsNullOrWhiteSpace(plainPassword))
                throw new ArgumentException("密码不能为空", nameof(plainPassword));

            // 第一步：查询账户
            var user = await GetByUsernameAsync(username, cancellationToken);
            if (user == null) return null;  // 账户不存在，静默返回（不暴露"用户不存在"信息防止用户枚举攻击）

            // 第二步：检查账户启用状态
            if (!user.IsEnabled) return null;

            // 第三步：检查账户锁定状态（惰性解锁逻辑在 IsLocked() 内部）
            if (user.IsLocked()) return null;

            // 第四步：验证密码哈希
            var isPasswordValid = VerifyPassword(plainPassword, user.PasswordHash, user.PasswordSalt);

            if (!isPasswordValid)
            {
                // 认证失败：记录失败次数，可能触发账户锁定
                user.RecordLoginFailure(maxFailCount: 5, lockDurationMinutes: 15);
                _context.Entry(user).State = EntityState.Modified;
                await _context.SaveChangesAsync(cancellationToken);
                return null;
            }

            // 第五步：认证成功，更新登录信息
            user.RecordLoginSuccess();
            _context.Entry(user).State = EntityState.Modified;
            await _context.SaveChangesAsync(cancellationToken);

            return user;
        }

        /// <summary>
        /// 检查用户名是否已被占用（创建新账户前验证）
        /// </summary>
        public async Task<bool> IsUsernameExistsAsync(
            string username,
            CancellationToken cancellationToken = default)
        {
            var normalizedUsername = username.Trim().ToLowerInvariant();
            return await _dbSet.AnyAsync(
                u => u.Username.ToLower() == normalizedUsername,
                cancellationToken);
        }

        /// <summary>
        /// 更新用户密码：生成新盐值，重新计算哈希，更新数据库。
        /// </summary>
        public async Task UpdatePasswordAsync(
            Guid userId,
            string newPlainPassword,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(newPlainPassword))
                throw new ArgumentException("新密码不能为空", nameof(newPlainPassword));

            var user = await GetByIdAsync(userId, cancellationToken)
                ?? throw new InvalidOperationException($"用户 ID={userId} 不存在");

            // 生成新盐值并计算哈希（每次修改密码都重新生成盐，防止旧哈希被复用）
            var (hash, salt) = HashPassword(newPlainPassword);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
            user.UpdatedAt = DateTime.UtcNow;

            _context.Entry(user).State = EntityState.Modified;
            await _context.SaveChangesAsync(cancellationToken);
        }

        // ================================================================
        // 密码哈希工具方法（私有，仅供本类使用）
        // ================================================================

        /// <summary>
        /// 生成密码的哈希值和盐值。
        /// 算法：PBKDF2（Password-Based Key Derivation Function 2）+ SHA256 + 随机盐
        /// 返回 Base64 编码的哈希和盐（便于在数据库中存储为字符串）
        /// </summary>
        /// <param name="plainPassword">明文密码</param>
        /// <returns>(哈希Base64字符串, 盐Base64字符串) 元组</returns>
        private static (string Hash, string Salt) HashPassword(string plainPassword)
        {
            // 生成密码学安全的随机盐（使用 System.Security.Cryptography.RandomNumberGenerator）
            var saltBytes = RandomNumberGenerator.GetBytes(SaltSizeBytes);

            // 使用 PBKDF2+SHA256 计算哈希
            // Rfc2898DeriveBytes 是 .NET 内置的 PBKDF2 实现
            using var pbkdf2 = new Rfc2898DeriveBytes(
                Encoding.UTF8.GetBytes(plainPassword), // 密码字节
                saltBytes,                              // 盐字节
                PasswordHashIterations,                 // 迭代次数（越高越安全）
                HashAlgorithmName.SHA256);              // 底层哈希算法

            var hashBytes = pbkdf2.GetBytes(HashSizeBytes);

            return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
        }

        /// <summary>
        /// 验证明文密码与存储的哈希是否匹配。
        /// 使用 CryptographicOperations.FixedTimeEquals 做常数时间比较，
        /// 防止时序攻击（Timing Attack）通过响应时间差异推断哈希前缀是否正确。
        /// </summary>
        /// <param name="plainPassword">用户输入的明文密码</param>
        /// <param name="storedHash">数据库中存储的哈希 Base64 字符串</param>
        /// <param name="storedSalt">数据库中存储的盐 Base64 字符串</param>
        /// <returns>密码正确返回 true，否则 false</returns>
        private static bool VerifyPassword(string plainPassword, string storedHash, string storedSalt)
        {
            try
            {
                var saltBytes = Convert.FromBase64String(storedSalt);
                var expectedHashBytes = Convert.FromBase64String(storedHash);

                // 用相同的盐重新计算哈希
                using var pbkdf2 = new Rfc2898DeriveBytes(
                    Encoding.UTF8.GetBytes(plainPassword),
                    saltBytes,
                    PasswordHashIterations,
                    HashAlgorithmName.SHA256);

                var actualHashBytes = pbkdf2.GetBytes(HashSizeBytes);

                // 常数时间比较（防止时序攻击）
                return CryptographicOperations.FixedTimeEquals(actualHashBytes, expectedHashBytes);
            }
            catch
            {
                // Base64 解码失败或其他异常：视为验证失败
                return false;
            }
        }
    }
}
