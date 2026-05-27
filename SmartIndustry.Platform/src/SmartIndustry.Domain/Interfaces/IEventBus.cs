// ============================================================
// 文件：IEventBus.cs
// 层次：领域层 (Domain Layer) — 接口
// 职责：定义平台内部事件总线契约，实现模块间的松耦合通信
// 设计思路：
//   事件总线是平台内各功能模块解耦的核心基础设施。
//   采用发布-订阅（Pub/Sub）模式：
//     - 运动控制模块发布 AxisMotionCompletedEvent，不直接调用视觉模块
//     - 视觉模块订阅此事件，在运动完成后自动触发拍照检测
//   支持同步和异步两种处理器接口，满足不同场景：
//     - 同步处理器：UI 更新、内存状态同步（需要立即生效）
//     - 异步处理器：数据库写入、网络通知（可延迟、可重试）
//   实现约定（Infrastructure 层）：
//     - 事件在发布者的调用线程或线程池上分发（取决于实现）
//     - 处理器异常不应影响发布者（静默捕获或记录日志）
//     - 支持按事件类型路由到对应处理器集合
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Events;

namespace SmartIndustry.Domain.Interfaces
{
    // ====================================================================
    // 事件处理器接口（同步）
    // ====================================================================

    /// <summary>
    /// 同步领域事件处理器接口。
    /// 用于需要立即、同步响应的场景（如：UI 状态同步、内存缓存更新）。
    /// </summary>
    /// <typeparam name="TEvent">要处理的领域事件类型（必须继承 DomainEvent）</typeparam>
    public interface IEventHandler<in TEvent> where TEvent : DomainEvent
    {
        /// <summary>
        /// 处理领域事件。
        /// 实现此方法时应保持短暂、无副作用，不进行 I/O 操作。
        /// </summary>
        /// <param name="domainEvent">要处理的事件实例</param>
        void Handle(TEvent domainEvent);
    }

    // ====================================================================
    // 异步事件处理器接口
    // ====================================================================

    /// <summary>
    /// 异步领域事件处理器接口。
    /// 用于需要 I/O 操作的场景（如：数据库写入、HTTP 通知、邮件发送）。
    /// </summary>
    /// <typeparam name="TEvent">要处理的领域事件类型</typeparam>
    public interface IAsyncEventHandler<in TEvent> where TEvent : DomainEvent
    {
        /// <summary>
        /// 异步处理领域事件。
        /// 实现此方法可进行数据库操作、网络请求等异步 I/O。
        /// 应正确传播 CancellationToken 支持取消操作。
        /// </summary>
        /// <param name="domainEvent">要处理的事件实例</param>
        /// <param name="cancellationToken">取消令牌（宿主关闭或超时时触发）</param>
        Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
    }

    // ====================================================================
    // 事件总线核心接口
    // ====================================================================

    /// <summary>
    /// 领域事件总线接口。
    /// 提供事件的发布、订阅和取消订阅能力，是平台内模块通信的核心契约。
    /// 典型实现：内存事件总线（In-Process）、MediatR、或基于 Channel&lt;T&gt; 的高性能实现。
    /// </summary>
    public interface IEventBus
    {
        // ----------------------------------------------------------------
        // 发布方法
        // ----------------------------------------------------------------

        /// <summary>
        /// 异步发布一个领域事件（推荐用法）。
        /// 事件总线将此事件路由到所有已注册的同步和异步处理器。
        /// </summary>
        /// <typeparam name="TEvent">事件类型</typeparam>
        /// <param name="domainEvent">要发布的事件实例</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : DomainEvent;

        /// <summary>
        /// 批量发布领域事件（实体清空前统一分发，保证顺序性）。
        /// </summary>
        /// <param name="domainEvents">要发布的事件集合</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task PublishAllAsync(
            IEnumerable<DomainEvent> domainEvents,
            CancellationToken cancellationToken = default);

        // ----------------------------------------------------------------
        // 同步处理器订阅/取消订阅
        // ----------------------------------------------------------------

        /// <summary>
        /// 订阅指定类型的领域事件（同步处理器接口形式）。
        /// </summary>
        void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : DomainEvent;

        /// <summary>
        /// 订阅指定类型的领域事件（同步 Lambda 形式，返回 IDisposable 订阅令牌）。
        /// </summary>
        IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : DomainEvent;

        /// <summary>
        /// 取消同步处理器订阅。
        /// </summary>
        void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : DomainEvent;

        // ----------------------------------------------------------------
        // 异步处理器订阅/取消订阅
        // ----------------------------------------------------------------

        /// <summary>
        /// 订阅指定类型的领域事件（异步处理器接口形式）。
        /// </summary>
        void Subscribe<TEvent>(IAsyncEventHandler<TEvent> handler) where TEvent : DomainEvent;

        /// <summary>
        /// 订阅指定类型的领域事件（异步 Lambda 形式，返回 IDisposable 订阅令牌）。
        /// </summary>
        IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : DomainEvent;

        /// <summary>
        /// 取消异步处理器订阅。
        /// </summary>
        void Unsubscribe<TEvent>(IAsyncEventHandler<TEvent> handler) where TEvent : DomainEvent;
    }
}
