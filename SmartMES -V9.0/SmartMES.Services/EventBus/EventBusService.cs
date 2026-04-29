using SmartMES.Core.Interfaces;
using System.Collections.Concurrent;

namespace SmartMES.Services.EventBus
{
    /// <summary>
    /// 事件总线服务实现（EventAggregator模式）
    /// 
    /// 设计思路：
    /// - 使用字典存储「事件类型 → 处理委托列表」的映射
    /// - 发布时找到对应类型的所有订阅者并逐一调用
    /// - 模块间通信无需直接引用对方，彻底解耦
    /// 
    /// 使用示例：
    ///   eventBus.Subscribe<NewAlarmEvent>(e => Console.WriteLine(e.Message));
    ///   eventBus.Publish(new NewAlarmEvent { Message = "温度超限" });
    /// </summary>
    public class EventBusService : IEventBus
    {
        /// <summary>
        /// 订阅者字典：键为事件类型，值为订阅者委托列表
        /// 使用ConcurrentDictionary保证多线程安全
        /// </summary>
        private readonly ConcurrentDictionary<Type, List<object>> _handlers
            = new ConcurrentDictionary<Type, List<object>>();

        /// <summary>线程锁，保护handlers列表的读写操作</summary>
        private readonly object _lock = new object();

        /// <summary>
        /// 订阅事件
        /// 将处理委托注册到对应事件类型的订阅者列表中
        /// </summary>
        public void Subscribe<TEvent>(Action<TEvent> handler)
        {
            var eventType = typeof(TEvent);
            lock (_lock)
            {
                // GetOrAdd：如果键不存在则创建新列表，线程安全
                var handlers = _handlers.GetOrAdd(eventType, _ => new List<object>());
                handlers.Add(handler);
            }
        }

        /// <summary>
        /// 取消订阅
        /// 从订阅者列表中移除指定委托
        /// </summary>
        public void Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            var eventType = typeof(TEvent);
            lock (_lock)
            {
                if (_handlers.TryGetValue(eventType, out var handlers))
                {
                    handlers.Remove(handler);
                }
            }
        }

        /// <summary>
        /// 发布事件
        /// 找到所有订阅该事件类型的处理者并调用
        /// </summary>
        public void Publish<TEvent>(TEvent eventData)
        {
            var eventType = typeof(TEvent);
            List<object>? handlersCopy;

            // 复制一份列表，避免在回调中修改列表导致的并发问题
            lock (_lock)
            {
                if (!_handlers.TryGetValue(eventType, out var handlers))
                    return;
                handlersCopy = new List<object>(handlers);
            }

            // 遍历所有订阅者并调用
            foreach (var handler in handlersCopy)
            {
                try
                {
                    ((Action<TEvent>)handler)(eventData);
                }
                catch (Exception ex)
                {
                    // 单个处理者异常不影响其他处理者执行
                    System.Diagnostics.Debug.WriteLine($"[EventBus] 事件处理异常: {ex.Message}");
                }
            }
        }
    }
}
