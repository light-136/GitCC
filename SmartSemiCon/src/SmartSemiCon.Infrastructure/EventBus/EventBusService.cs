// ============================================================
// 文件：EventBusService.cs
// 用途：事件总线实现
// 设计思路：
//   事件总线是模块间解耦通信的核心组件。
//   使用 ConcurrentDictionary 存储事件类型到处理器列表的映射。
//   支持同步和异步处理器。
//   线程安全设计 — 发布/订阅可在任意线程调用。
//
//   工作流程：
//   1. 模块A调用 Subscribe<TEvent>(handler) 注册处理器
//   2. 模块B调用 Publish<TEvent>(event) 发布事件
//   3. 事件总线遍历所有 TEvent 的处理器，依次调用
//   4. 处理器可通过返回的 IDisposable 取消订阅
// ============================================================

using System.Collections.Concurrent;
using SmartSemiCon.Domain.Events;
using SmartSemiCon.Domain.Interfaces;

namespace SmartSemiCon.Infrastructure.EventBus
{
    /// <summary>
    /// 事件总线实现 — 进程内发布/订阅消息中介。
    /// 单例注册到DI容器，全局共享。
    /// </summary>
    public class EventBusService : IEventBus
    {
        // 事件类型 → 处理器列表（每个处理器是一个Delegate）
        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

        // 锁对象，保护处理器列表的修改操作
        private readonly object _lock = new();

        /// <summary>
        /// 发布事件 — 遍历所有订阅者并调用。
        /// 如果某个处理器抛出异常，不影响其他处理器的执行。
        /// </summary>
        public void Publish<TEvent>(TEvent domainEvent) where TEvent : DomainEvent
        {
            if (domainEvent == null) return;

            var eventType = typeof(TEvent);

            if (!_handlers.TryGetValue(eventType, out var handlers)) return;

            // 复制一份处理器列表，避免在遍历时被修改
            List<Delegate> snapshot;
            lock (_lock)
            {
                snapshot = new List<Delegate>(handlers);
            }

            foreach (var handler in snapshot)
            {
                try
                {
                    switch (handler)
                    {
                        // 同步处理器
                        case Action<TEvent> syncHandler:
                            syncHandler(domainEvent);
                            break;
                        // 异步处理器 — 使用Fire-and-forget模式
                        case Func<TEvent, Task> asyncHandler:
                            _ = Task.Run(async () =>
                            {
                                try { await asyncHandler(domainEvent); }
                                catch { /* 异步处理器异常被吞掉，生产环境应记录日志 */ }
                            });
                            break;
                    }
                }
                catch
                {
                    // 单个处理器失败不影响其他处理器
                }
            }
        }

        /// <summary>
        /// 订阅事件（同步处理器）。
        /// 返回一个 IDisposable，调用 Dispose() 可取消订阅。
        /// </summary>
        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : DomainEvent
        {
            return SubscribeInternal<TEvent>(handler);
        }

        /// <summary>
        /// 订阅事件（异步处理器）。
        /// </summary>
        public IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : DomainEvent
        {
            return SubscribeInternal<TEvent>(handler);
        }

        /// <summary>
        /// 内部订阅实现 — 将处理器添加到对应事件类型的列表中。
        /// </summary>
        private IDisposable SubscribeInternal<TEvent>(Delegate handler) where TEvent : DomainEvent
        {
            var eventType = typeof(TEvent);

            lock (_lock)
            {
                if (!_handlers.TryGetValue(eventType, out var handlers))
                {
                    handlers = new List<Delegate>();
                    _handlers[eventType] = handlers;
                }
                handlers.Add(handler);
            }

            // 返回取消订阅的令牌
            return new SubscriptionToken(() =>
            {
                lock (_lock)
                {
                    if (_handlers.TryGetValue(eventType, out var handlers))
                    {
                        handlers.Remove(handler);
                    }
                }
            });
        }

        /// <summary>
        /// 订阅令牌 — 调用 Dispose 取消订阅。
        /// </summary>
        private class SubscriptionToken : IDisposable
        {
            private readonly Action _unsubscribe;
            private bool _disposed;

            public SubscriptionToken(Action unsubscribe)
            {
                _unsubscribe = unsubscribe;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _unsubscribe();
            }
        }
    }
}
