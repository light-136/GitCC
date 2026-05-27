// ============================================================
// 文件：UserAccount.cs
// 层次：领域层 (Domain Layer) — 实体
// 职责：表示系统用户账户，包含认证凭据和角色权限信息
// 设计思路：
//   密码以 Hash+Salt 形式存储（基础设施层负责哈希计算），
//   领域层只持有 PasswordHash 和 PasswordSalt，不处理明文密码。
//   账户锁定机制（LoginFailCount + LockedUntil）防止暴力破解。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Domain.Entities
{
    /// <summary>
    /// 用户账户实体，对应数据库表 UserAccounts。
    /// 管理平台操作人员的身份信息、认证凭据和访问权限。
    /// </summary>
    public class UserAccount : BaseEntity
    {
        // ----------------------------------------------------------------
        // 基本信息
        // ----------------------------------------------------------------

        /// <summary>
        /// 登录用户名（唯一，不区分大小写，不可修改）。
        /// 数据库建立唯一索引，Infrastructure 层查询时使用 ToLower 规范化
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>显示名称（可修改，用于界面展示）</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>用户邮箱（用于找回密码通知，可为空）</summary>
        public string? Email { get; set; }

        // ----------------------------------------------------------------
        // 认证凭据（密码哈希存储，不存储明文）
        // ----------------------------------------------------------------

        /// <summary>
        /// 密码哈希值（使用 PBKDF2+SHA256+随机盐 计算）。
        /// 存储 Base64 编码的哈希字节数组，认证时重新计算比较
        /// </summary>
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>
        /// 密码加盐（随机生成，每个账户独立盐值，防止彩虹表攻击）。
        /// 存储 Base64 编码的随机字节数组
        /// </summary>
        public string PasswordSalt { get; set; } = string.Empty;

        // ----------------------------------------------------------------
        // 权限控制
        // ----------------------------------------------------------------

        /// <summary>用户角色（决定功能访问权限，从 Viewer 到 Administrator 递增）</summary>
        public UserRole Role { get; set; } = UserRole.Operator;

        /// <summary>
        /// 账户是否启用（false=禁用，认证时直接拒绝，无需检查密码）。
        /// 离职员工应禁用账户而非删除，保留审计记录
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        // ----------------------------------------------------------------
        // 安全保护：暴力破解防护
        // ----------------------------------------------------------------

        /// <summary>
        /// 连续登录失败次数。
        /// 超过 MaxLoginFailCount（通常为5次）时锁定账户
        /// </summary>
        public int LoginFailCount { get; set; } = 0;

        /// <summary>
        /// 账户锁定到期时间（UTC，null=未锁定）。
        /// 超过此时间后自动解锁，或由管理员手动解锁
        /// </summary>
        public DateTime? LockedUntil { get; set; }

        /// <summary>最后一次成功登录时间（UTC），用于安全审计和"上次登录"提示</summary>
        public DateTime? LastLoginAt { get; set; }

        /// <summary>最后一次成功登录的IP地址（用于异常登录检测）</summary>
        public string? LastLoginIp { get; set; }

        // ----------------------------------------------------------------
        // 领域行为方法
        // ----------------------------------------------------------------

        /// <summary>
        /// 检查账户是否当前被锁定。
        /// 锁定到期时自动解除（不需要定时任务，每次检查时惰性解锁）
        /// </summary>
        public bool IsLocked()
        {
            if (!LockedUntil.HasValue) return false;
            // 锁定时间已过，惰性解锁
            if (DateTime.UtcNow > LockedUntil.Value)
            {
                LockedUntil = null;
                LoginFailCount = 0;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 记录一次登录失败，超过阈值时锁定账户指定时间
        /// </summary>
        /// <param name="maxFailCount">最大失败次数阈值（默认5次）</param>
        /// <param name="lockDurationMinutes">锁定时长（分钟，默认15分钟）</param>
        public void RecordLoginFailure(int maxFailCount = 5, int lockDurationMinutes = 15)
        {
            LoginFailCount++;
            UpdatedAt = DateTime.UtcNow;

            if (LoginFailCount >= maxFailCount)
            {
                LockedUntil = DateTime.UtcNow.AddMinutes(lockDurationMinutes);
            }
        }

        /// <summary>
        /// 记录一次成功登录，清空失败计数，更新最后登录信息
        /// </summary>
        /// <param name="loginIp">登录来源IP地址</param>
        public void RecordLoginSuccess(string? loginIp = null)
        {
            LoginFailCount = 0;
            LockedUntil = null;
            LastLoginAt = DateTime.UtcNow;
            LastLoginIp = loginIp;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
