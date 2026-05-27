// ============================================================
// 文件：EventBusService.cs
// 层次：基础设施层 (Infrastructure Layer) — 事件总线实现
// 职责：
//   实现领域层定义的 IEventBus 接口，提供进程内（In-Process）的发布/订阅机制：
//   - 泛型发布/订阅（按事件类型路由）
//   - 同步和异步处理器均支持
//   - Lambda 订阅（Action/Func，返回 IDisposable 订阅令牌）
//   - 接口订阅（IEventHandler<T> / IAsyncEventHandler<T>）
//   - 线程安全：ConcurrentDictionary 管理处理器集合，写操作加锁
//   - 弱引用（WeakReference）防止内存泄漏：订阅者销毁后自动清理处理器
//   - 异常隔离：单个处理器抛出异常不影响其他处理器
//   - 支持事件过滤（通过 Lambda 条件订阅）
// 设计思路：
//   事件总线是平台内模块解耦的核心。运动控制完成后发布 AxisMotionCompletedEvent，
//   视觉模块订阅后触发拍照，应用层订阅后写日志，UI 订阅后刷新位置显示。
//   各模块完全解耦，无直接依赖关系。
//   弱引用策略：
//     - 接口处理器（IEventHandler<T>）：使用弱引用，允许订阅者 GC 后自动清理
//     - Lambda 处理器（Action<T>）：使用强引用（委托本身持有闭包引用），
//       调用方通过 IDisposable 手动取消订阅（推荐模式）
// 注意：
//   事件在发布者的调用线程或线程池上分发（同步处理器在调用线程，异步处理器在线程池）。
//   不适合跨进程通信（跨进程请使用 MQTT 或 RabbitMQ）。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using SmartIndustry.Domain.Events;
using SmartIndustry.Domain.Interfaces;
using System.Collections.Concurrent;

namespace SmartIndustry.Infrastructure.EventBus
{
    /// <summary>
    /// 进程内事件总线实现。
    /// 基于 ConcurrentDictionary + ReaderWriterLockSlim，支持并发发布和动态订阅。
    /// 在 DI 容器中注册为 Singleton，整个应用生命周期内共享一个实例。
    /// </summary>
    public class EventBusService : IEventBus
    {
        // ----------------------------------------------------------------
        // 内部数据结构：处理器包装基类
        // ----------------------------------------------------------------

        /// <summary>
        /// 处理器包装抽象基类。
        /// 封装具体的处理器（同步接口、异步接口、同步Lambda、异步Lambda），
        /// 统一处理弱引用管理和调用逻辑。
        /// </summary>
        private abstract class HandlerWrapper
        {
            /// <summary>
            /// 处理器唯一标识符（用于按引用取消订阅）
            /// </summary>
            public Guid HandlerId { get; } = Guid.NewGuid();

            /// <summary>
            /// 异步调用处理器。
            /// </summary>
            /// <param name="domainEvent">要处理的领域事件</param>
            /// <param name="cancellationToken">取消令牌</param>
            /// <returns>true=处理器仍然存活（弱引用未失效），false=处理器已被 GC（需要清理）</returns>
            public abstract Task<bool> InvokeAsync(object domainEvent, CancellationToken cancellationToken);
        }

        /// <summary>
        /// 同步接口处理器包装（使用弱引用防止内存泄漏）
        /// </summary>
        private sealed class SyncInterfaceHandlerWrapper<TEvent> : HandlerWrapper where TEvent : DomainEvent
        {
            private readonly WeakReference<IEventHandler<TEvent>> _weakRef;

            public SyncInterfaceHandlerWrapper(IEventHandler<TEvent> handler)
                => _weakRef = new WeakReference<IEventHandler<TEvent>>(handler);

            public override Task<bool> InvokeAsync(object domainEvent, CancellationToken cancellationToken)
            {
                if (_weakRef.TryGetTarget(out var handler))
                {
                    handler.Handle((TEvent)domainEvent);
                    return Task.FromResult(true);
                }
                return Task.FromResult(false); // 处理器已被 GC
            }

            public bool IsSameHandler(IEventHandler<TEvent> handler)
                => _weakRef.TryGetTarget(out var h) && ReferenceEquals(h, handler);
        }

        /// <summary>
        /// 异步接口处理器包装（使用弱引用防止内存泄漏）
        /// </summary>
        private sealed class AsyncInterfaceHandlerWrapper<TEvent> : HandlerWrapper where TEvent : DomainEvent
        {
            private readonly WeakReference<IAsyncEventHandler<TEvent>> _weakRef;

            public AsyncInterfaceHandlerWrapper(IAsyncEventHandler<TEvent> handler)
                => _weakRef = new WeakReference<IAsyncEventHandler<TEvent>>(handler);

            public override async Task<bool> InvokeAsync(object domainEvent, CancellationToken cancellationToken)
            {
                if (_weakRef.TryGetTarget(out var handler))
                {
                    await handler.HandleAsync((TEvent)domainEvent, cancellationToken);
                    return true;
                }
                return false;
            }

            public bool IsSameHandler(IAsyncEventHandler<TEvent> handler)
                => _weakRef.TryGetTarget(out var h) && ReferenceEquals(h, handler);
        }

        /// <summary>
        /// 同步 Lambda 处理器包装（强引用，通过 IDisposable 手动取消订阅）
        /// </summary>
        private sealed class ActionHandlerWrapper<TEvent> : HandlerWrapper where TEvent : DomainEvent
        {
            private readonly Action<TEvent> _action;
            private volatile bool _disposed = false;

            public ActionHandlerWrapper(Action<TEvent> action) => _action = action;

            public override Task<bool> InvokeAsync(object domainEvent, CancellationToken cancellationToken)
            {
                if (_disposed) return Task.FromResult(false);
                _action((TEvent)domainEvent);
                return Task.FromResult(true);
            }

            public void Dispose() => _disposed = true;
        }

        /// <summary>
        /// 异步 Lambda 处理器包装（强引用，通过 IDisposable 手动取消订阅）
        /// </summary>
        private sealed class FuncHandlerWrapper<TEvent> : HandlerWrapper where TEvent : DomainEvent
        {
            private readonly Func<TEvent, CancellationToken, Task> _func;
            private volatile bool _disposed = false;

            public FuncHandlerWrapper(Func<TEvent, CancellationToken, Task> func) => _func = func;

            public override async Task<bool> InvokeAsync(object domainEvent, CancellationToken cancellationToken)
            {
                if (_disposed) return false;
                await _func((TEvent)domainEvent, cancellationToken);
                return true;
            }

            public void Dispose() => _disposed = true;
        }

        // ----------------------------------------------------------------
        // 处理器存储：EventType -> List<HandlerWrapper>
        // ----------------------------------------------------------------

        /// <summary>
        /// 核心存储：事件类型名称 -> 处理器包装列表。
        /// Key = 事件类型的完全限定类型名（typeof(TEvent).FullName）
        /// Value = 该事件类型的所有处理器包装列表
        /// ConcurrentDictionary 保证字典级别的线程安全（键的新增/删除）。
        /// 列表内容的修改通过 _handlersLock 保护。
        /// </summary>
        private readonly ConcurrentDictionary<string, List<HandlerWrapper>> _handlers = new();

        /// <summary>保护处理器列表修改的读写锁（多个发布者并发读，订阅/取消订阅时写）</summary>
        private readonly ReaderWriterLockSlim _handlersLock = new();

        // ================================================================
        // IEventBus 实现：发布
        // ================================================================

        /// <summary>
        /// 异步发布领域事件（推荐方式）。
        /// 找到该事件类型的所有处理器，并发调用（异常隔离：一个失败不影响其他）。
        /// </summary>
        public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
            where TEvent : DomainEvent
        {
            if (domainEvent == null) throw new ArgumentNullException(nameof(domainEvent));

            var key = GetKey<TEvent>();

            // 获取此事件类型的处理器列表快照（避免在迭代时被修改）
            List<HandlerWrapper>? snapshot = null;
            _handlersLock.EnterReadLock();
            try
            {
                if (_handlers.TryGetValue(key, out var list))
                    snapshot = new List<HandlerWrapper>(list); // 浅拷贝，保证快照一致性
            }
            finally
            {
                _handlersLock.ExitReadLock();
            }

            if (snapshot == null || snapshot.Count == 0) return;

            // 收集已失效的处理器（弱引用目标被 GC）
            var toRemove = new List<HandlerWrapper>();

            // 并发调用所有处理器（Task.WhenAll 使所有处理器并发运行）
            var invokeTasks = snapshot.Select(async handler =>
            {
                try
                {
                    var alive = await handler.InvokeAsync(domainEvent, cancellationToken);
                    if (!alive) toRemove.Add(handler);
                }
                catch (Exception ex)
                {
                    // 异常隔离：记录但不重新抛出，其他处理器继续执行
                    System.Diagnostics.Debug.WriteLine(
                        $"[EventBus] 处理器 {handler.HandlerId} 处理 {typeof(TEvent).Name} 时发生异常：{ex.Message}");
                }
            });

            await Task.WhenAll(invokeTasks);

            // 清理已失效的弱引用处理器
            if (toRemove.Count > 0)
                RemoveHandlers(key, toRemove);
        }

        /// <summary>
        /// 批量发布事件（按顺序依次发布，保证顺序性）
        /// </summary>
        public async Task PublishAllAsync(IEnumerable<DomainEvent> domainEvents,
            CancellationToken cancellationToken = default)
        {
            foreach (var evt in domainEvents)
            {
                // 使用反射调用泛型 PublishAsync，保证类型安全的事件路由
                var eventType = evt.GetType();
                var method = typeof(EventBusService)
                    .GetMethod(nameof(PublishAsync))!
                    .MakeGenericMethod(eventType);

                await (Task)method.Invoke(this, new object[] { evt, cancellationToken })!;
            }
        }

        // ================================================================
        // IEventBus 实现：订阅（同步接口）
        // ================================================================

        /// <summary>订阅（同步接口处理器）</summary>
        public void Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : DomainEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            AddHandler(GetKey<TEvent>(), new SyncInterfaceHandlerWrapper<TEvent>(handler));
        }

        /// <summary>订阅（同步 Lambda，返回 IDisposable 订阅令牌）</summary>
        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : DomainEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var wrapper = new ActionHandlerWrapper<TEvent>(handler);
            AddHandler(GetKey<TEvent>(), wrapper);
            // 返回订阅令牌（Dispose 时从列表中移除并标记 wrapper 为已释放）
            return new SubscriptionToken(() =>
            {
                wrapper.Dispose();
                RemoveHandlers(GetKey<TEvent>(), new[] { (HandlerWrapper)wrapper });
            });
        }

        /// <summary>取消同步接口处理器的订阅</summary>
        public void Unsubscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : DomainEvent
        {
            var key = GetKey<TEvent>();
            _handlersLock.EnterWriteLock();
            try
            {
                if (_handlers.TryGetValue(key, out var list))
                    list.RemoveAll(h => h is SyncInterfaceHandlerWrapper<TEvent> w && w.IsSameHandler(handler));
            }
            finally
            {
                _handlersLock.ExitWriteLock();
            }
        }

        // ================================================================
        // IEventBus 实现：订阅（异步接口）
        // ================================================================

        /// <summary>订阅（异步接口处理器）</summary>
        public void Subscribe<TEvent>(IAsyncEventHandler<TEvent> handler) where TEvent : DomainEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            AddHandler(GetKey<TEvent>(), new AsyncInterfaceHandlerWrapper<TEvent>(handler));
        }

        /// <summary>订阅（异步 Lambda，返回 IDisposable 订阅令牌）</summary>
        public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
            where TEvent : DomainEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var wrapper = new FuncHandlerWrapper<TEvent>(handler);
            AddHandler(GetKey<TEvent>(), wrapper);
            return new SubscriptionToken(() =>
            {
                wrapper.Dispose();
                RemoveHandlers(GetKey<TEvent>(), new[] { (HandlerWrapper)wrapper });
            });
        }

        /// <summary>取消异步接口处理器的订阅</summary>
        public void Unsubscribe<TEvent>(IAsyncEventHandler<TEvent> handler) where TEvent : DomainEvent
        {
            var key = GetKey<TEvent>();
            _handlersLock.EnterWriteLock();
            try
            {
                if (_handlers.TryGetValue(key, out var list))
                    list.RemoveAll(h => h is AsyncInterfaceHandlerWrapper<TEvent> w && w.IsSameHandler(handler));
            }
            finally
            {
                _handlersLock.ExitWriteLock();
            }
        }

        // ================================================================
        // 私有工具方法
        // ================================================================

        /// <summary>获取事件类型的路由键（使用类型全名，保证唯一性）</summary>
        private static string GetKey<TEvent>() => typeof(TEvent).FullName ?? typeof(TEvent).Name;

        /// <summary>线程安全地添加处理器到列表</summary>
        private void AddHandler(string key, HandlerWrapper wrapper)
        {
            _handlersLock.EnterWriteLock();
            try
            {
                var list = _handlers.GetOrAdd(key, _ => new List<HandlerWrapper>());
                list.Add(wrapper);
            }
            finally
            {
                _handlersLock.ExitWriteLock();
            }
        }

        /// <summary>线程安全地从列表中移除处理器</summary>
        private void RemoveHandlers(string key, IEnumerable<HandlerWrapper> toRemove)
        {
            _handlersLock.EnterWriteLock();
            try
            {
                if (_handlers.TryGetValue(key, out var list))
                {
                    foreach (var wrapper in toRemove)
                        list.Remove(wrapper);
                }
            }
            finally
            {
                _handlersLock.ExitWriteLock();
            }
        }

        // ================================================================
        // 订阅令牌（实现 IDisposable，取消 Lambda 订阅使用）
        // ================================================================

        /// <summary>
        /// 订阅令牌：持有取消订阅操作的委托。
        /// 调用 Dispose() 时自动执行取消订阅逻辑（Lambda 从处理器列表移除）。
        /// </summary>
        private sealed class SubscriptionToken : IDisposable
        {
            private readonly Action _unsubscribeAction;
            private bool _disposed = false;

            public SubscriptionToken(Action unsubscribeAction)
                => _unsubscribeAction = unsubscribeAction;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _unsubscribeAction();
            }
        }

        // ================================================================
        // IDisposable
        // ================================================================

        public void Dispose()
        {
            _handlersLock.Dispose();
            _handlers.Clear();
        }
    }
}
