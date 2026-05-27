// ============================================================
// 文件：ICollaborationService.cs
// 层次：领域层 (Domain Layer) — 接口
// 职责：定义多人实时协同服务接口
// 设计思路：
//   ICollaborationService 实现了类似工业控制场景下的"Google Docs"功能：
//   多个工程师可以同时查看和修改同一个设备操作视图，
//   变更实时同步到所有参与者。
//   实现技术选择（Infrastructure 层）：
//     - 进程内：使用内存字典 + ConcurrentDictionary
//     - 局域网：使用 SignalR Hub（双向实时推送）
//     - 跨网络：使用 MQTT 发布订阅
//   OnStateUpdated 事件是客户端收到远程状态更新的回调，
//   与 BroadcastStateAsync（发送本地变更）配对使用。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Entities;
using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Domain.Interfaces
{
    /// <summary>
    /// 多人实时协同服务接口。
    /// 管理协同会话的创建/加入/离开，以及共享状态的实时广播和同步。
    /// </summary>
    public interface ICollaborationService
    {
        // ----------------------------------------------------------------
        // 会话生命周期
        // ----------------------------------------------------------------

        /// <summary>
        /// 创建新的协同会话（当前用户成为 Owner）。
        /// </summary>
        /// <param name="sessionName">会话显示名称（说明协同目的）</param>
        /// <param name="ownerUserId">所有者用户 ID</param>
        /// <param name="ownerUsername">所有者用户名</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>创建的协同会话实体</returns>
        Task<CollaborationSession> CreateSessionAsync(
            string sessionName,
            Guid ownerUserId,
            string ownerUsername,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 加入已存在的协同会话。
        /// </summary>
        /// <param name="sessionId">协同会话业务 ID（如 "SESSION-20260525-A1B2C3"）</param>
        /// <param name="userId">加入的用户 ID</param>
        /// <param name="username">加入的用户名</param>
        /// <param name="role">
        /// 请求的协同角色（最终角色由所有者决定，默认为 Viewer）
        /// </param>
        Task<CollaborationSession> JoinSessionAsync(
            string sessionId,
            Guid userId,
            string username,
            CollaborationRole role = CollaborationRole.Viewer,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 离开协同会话（清理用户连接，必要时转移所有权或关闭会话）。
        /// </summary>
        Task LeaveSessionAsync(
            string sessionId,
            Guid userId,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 共享状态操作
        // ----------------------------------------------------------------

        /// <summary>
        /// 广播状态变更到会话中的所有其他成员。
        /// 调用方需有 Editor 或 Owner 角色。
        /// </summary>
        /// <param name="sessionId">会话业务 ID</param>
        /// <param name="userId">操作用户 ID（用于权限验证）</param>
        /// <param name="key">状态键名</param>
        /// <param name="value">状态新值（序列化为字符串）</param>
        Task BroadcastStateAsync(
            string sessionId,
            Guid userId,
            string key,
            string value,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 请求全量同步当前会话的共享状态（新加入用户或断线重连后调用）。
        /// </summary>
        /// <param name="sessionId">会话业务 ID</param>
        /// <returns>完整的共享状态字典快照</returns>
        Task<IReadOnlyDictionary<string, string>> SyncStateAsync(
            string sessionId,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 状态接收事件（被动接收远程广播）
        // ----------------------------------------------------------------

        /// <summary>
        /// 远程状态更新事件。
        /// 当其他成员调用 BroadcastStateAsync 时，本地订阅者收到此事件。
        /// 参数：(sessionId: 会话ID, key: 键名, value: 新值, fromUserId: 发送方用户ID)
        /// </summary>
        event Action<string, string, string, Guid> OnStateUpdated;

        // ----------------------------------------------------------------
        // 会话查询
        // ----------------------------------------------------------------

        /// <summary>
        /// 获取所有活动中的协同会话列表。
        /// </summary>
        Task<IReadOnlyList<CollaborationSession>> GetActiveSessions(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 按业务 ID 获取单个协同会话。
        /// </summary>
        Task<CollaborationSession?> GetSession(
            string sessionId,
            CancellationToken cancellationToken = default);
    }
}
