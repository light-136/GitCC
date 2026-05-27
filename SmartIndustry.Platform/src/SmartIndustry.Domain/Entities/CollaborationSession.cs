// ============================================================
// 文件：CollaborationSession.cs
// 层次：领域层 (Domain Layer) — 实体
// 职责：表示多人实时协同操作的会话，支持共享状态同步
// 设计思路：
//   协同会话是平台支持多工位协同操作的核心机制。
//   SharedState 以 JSON 字符串存储，灵活支持不同协同场景的状态结构。
//   Members 集合记录参与者及其角色权限。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Domain.Entities
{
    /// <summary>
    /// 协同会话实体，对应数据库表 CollaborationSessions。
    /// 管理多用户实时协同操作的共享状态和参与者信息。
    /// </summary>
    public class CollaborationSession : BaseEntity
    {
        // ----------------------------------------------------------------
        // 会话标识
        // ----------------------------------------------------------------

        /// <summary>会话名称（如："晶圆对准协同操作-工位A"）</summary>
        public string SessionName { get; set; } = string.Empty;

        /// <summary>会话描述（说明协同操作的业务目的）</summary>
        public string Description { get; set; } = string.Empty;

        // ----------------------------------------------------------------
        // 会话状态
        // ----------------------------------------------------------------

        /// <summary>会话是否活跃（false=已关闭，不接受新操作）</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>会话创建时间（UTC）</summary>
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        /// <summary>会话关闭时间（UTC，null=仍在进行）</summary>
        public DateTime? EndedAt { get; set; }

        // ----------------------------------------------------------------
        // 参与者管理（JSON 存储以简化数据模型）
        // ----------------------------------------------------------------

        /// <summary>
        /// 参与者信息（JSON 数组格式）。
        /// 格式：[{"UserId": "...", "Username": "...", "Role": "Editor", "JoinedAt": "..."}]
        /// 避免为协同成员单独建表，会话参与者是短暂的运行时数据
        /// </summary>
        public string Members { get; set; } = "[]";

        /// <summary>
        /// 会话所有者用户ID（创建者，拥有管理权限）
        /// </summary>
        public Guid OwnerId { get; set; }

        /// <summary>会话所有者用户名（冗余存储以避免联表查询）</summary>
        public string OwnerUsername { get; set; } = string.Empty;

        // ----------------------------------------------------------------
        // 共享状态
        // ----------------------------------------------------------------

        /// <summary>
        /// 共享状态数据（JSON 格式，协同成员的实时状态快照）。
        /// 格式由使用场景定义，如：{"CurrentStep": 3, "AxisPosition": {...}, "CheckResults": {...}}
        /// 每次状态变更触发 CollaborationStateUpdatedEvent，通知所有在线成员
        /// </summary>
        public string SharedState { get; set; } = "{}";

        /// <summary>共享状态最后更新时间（UTC），用于冲突检测和超时判断</summary>
        public DateTime? SharedStateUpdatedAt { get; set; }

        // ----------------------------------------------------------------
        // 领域行为
        // ----------------------------------------------------------------

        /// <summary>
        /// 关闭会话（清理资源、标记结束时间）
        /// </summary>
        public void Close(string closedBy)
        {
            if (!IsActive) return; // 幂等

            IsActive = false;
            EndedAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
            UpdatedBy = closedBy;
        }
    }
}
