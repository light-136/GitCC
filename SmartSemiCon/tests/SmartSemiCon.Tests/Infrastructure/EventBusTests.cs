// ============================================================
// 文件：EventBusTests.cs
// 用途：事件总线单元测试
// 设计思路：
//   事件总线是系统模块间通信的核心组件。
//   测试其发布/订阅机制的正确性，确保：
//   1. 发布事件后，所有订阅者能收到通知
//   2. 取消订阅后，不再收到事件
//   3. 多个订阅者能独立接收同一事件
//   4. 不同事件类型互不干扰
//
//   使用真实的 EventBusService 实例测试，不使用 Mock，
//   因为要验证的是事件总线本身的真实逻辑。
// ============================================================

using SmartSemiCon.Domain.Events;
using SmartSemiCon.Infrastructure.EventBus;

namespace SmartSemiCon.Tests.Infrastructure
{
    /// <summary>
    /// 事件总线测试类 — 验证 EventBusService 的发布/订阅行为。
    /// </summary>
    public class EventBusTests
    {
        // =============================================
        // 发布/订阅功能测试
        // =============================================

        /// <summary>
        /// 验证：订阅某事件后，发布该事件时订阅者能收到通知。
        /// 这是事件总线最基本的功能。
        /// </summary>
        [Fact]
        public void Publish_WithSubscriber_ShouldInvokeHandler()
        {
            // Arrange — 创建事件总线并订阅 AlarmTriggeredEvent
            var eventBus = new EventBusService();
            AlarmTriggeredEvent? receivedEvent = null;

            eventBus.Subscribe<AlarmTriggeredEvent>(e =>
            {
                receivedEvent = e;
            });

            // Act — 发布报警触发事件
            var alarmEvent = new AlarmTriggeredEvent
            {
                Source = "测试模块"
            };
            eventBus.Publish(alarmEvent);

            // Assert — 订阅者应收到事件，且内容一致
            Assert.NotNull(receivedEvent);
            Assert.Equal("测试模块", receivedEvent.Source);
            Assert.Equal(alarmEvent.EventId, receivedEvent.EventId);
        }

        /// <summary>
        /// 验证：没有订阅者时发布事件，不应抛出异常。
        /// 事件总线应安全处理无订阅者的情况。
        /// </summary>
        [Fact]
        public void Publish_WithoutSubscriber_ShouldNotThrow()
        {
            var eventBus = new EventBusService();

            // Act & Assert — 无订阅者时发布事件不应出错
            var exception = Record.Exception(() =>
            {
                eventBus.Publish(new DeviceStateChangedEvent
                {
                    Source = "测试"
                });
            });

            Assert.Null(exception);
        }

        /// <summary>
        /// 验证：发布 null 事件时不应抛出异常。
        /// </summary>
        [Fact]
        public void Publish_NullEvent_ShouldNotThrow()
        {
            var eventBus = new EventBusService();
            eventBus.Subscribe<AlarmTriggeredEvent>(_ => { });

            var exception = Record.Exception(() =>
            {
                eventBus.Publish<AlarmTriggeredEvent>(null!);
            });

            Assert.Null(exception);
        }

        /// <summary>
        /// 验证：订阅者收到的事件对象与发布的是同一个引用。
        /// 事件总线不应复制事件对象。
        /// </summary>
        [Fact]
        public void Publish_ShouldPassSameEventReference()
        {
            var eventBus = new EventBusService();
            AlarmClearedEvent? receivedEvent = null;

            eventBus.Subscribe<AlarmClearedEvent>(e =>
            {
                receivedEvent = e;
            });

            var published = new AlarmClearedEvent { AlarmCode = 1001 };
            eventBus.Publish(published);

            // Assert — 是同一个对象引用
            Assert.Same(published, receivedEvent);
        }

        // =============================================
        // 取消订阅功能测试
        // =============================================

        /// <summary>
        /// 验证：调用 Dispose 取消订阅后，不再收到事件。
        /// Subscribe 返回的 IDisposable 是取消订阅的令牌。
        /// </summary>
        [Fact]
        public void Unsubscribe_AfterDispose_ShouldNotReceiveEvents()
        {
            var eventBus = new EventBusService();
            int callCount = 0;

            // 订阅事件
            var subscription = eventBus.Subscribe<AlarmTriggeredEvent>(_ =>
            {
                callCount++;
            });

            // 第一次发布 — 应该收到
            eventBus.Publish(new AlarmTriggeredEvent());
            Assert.Equal(1, callCount);

            // 取消订阅
            subscription.Dispose();

            // 第二次发布 — 不应该收到
            eventBus.Publish(new AlarmTriggeredEvent());
            Assert.Equal(1, callCount); // 计数不变
        }

        /// <summary>
        /// 验证：多次调用 Dispose 不应抛出异常（幂等性）。
        /// </summary>
        [Fact]
        public void Unsubscribe_DoubleDispose_ShouldNotThrow()
        {
            var eventBus = new EventBusService();
            var subscription = eventBus.Subscribe<AlarmTriggeredEvent>(_ => { });

            var exception = Record.Exception(() =>
            {
                subscription.Dispose();
                subscription.Dispose(); // 第二次调用
            });

            Assert.Null(exception);
        }

        /// <summary>
        /// 验证：取消一个订阅者后，其他订阅者不受影响。
        /// </summary>
        [Fact]
        public void Unsubscribe_OneOfMany_OthersShouldStillReceive()
        {
            var eventBus = new EventBusService();
            int handler1Count = 0;
            int handler2Count = 0;

            var sub1 = eventBus.Subscribe<AlarmTriggeredEvent>(_ => handler1Count++);
            var sub2 = eventBus.Subscribe<AlarmTriggeredEvent>(_ => handler2Count++);

            // 取消第一个订阅者
            sub1.Dispose();

            // 发布事件
            eventBus.Publish(new AlarmTriggeredEvent());

            // handler1 不再收到，handler2 仍然收到
            Assert.Equal(0, handler1Count);
            Assert.Equal(1, handler2Count);
        }

        // =============================================
        // 多订阅者测试
        // =============================================

        /// <summary>
        /// 验证：同一事件类型有多个订阅者时，所有订阅者都应收到事件。
        /// </summary>
        [Fact]
        public void Publish_MultipleSubscribers_AllShouldReceive()
        {
            var eventBus = new EventBusService();
            int handler1Count = 0;
            int handler2Count = 0;
            int handler3Count = 0;

            eventBus.Subscribe<AlarmTriggeredEvent>(_ => handler1Count++);
            eventBus.Subscribe<AlarmTriggeredEvent>(_ => handler2Count++);
            eventBus.Subscribe<AlarmTriggeredEvent>(_ => handler3Count++);

            // 发布一次事件
            eventBus.Publish(new AlarmTriggeredEvent());

            // 三个订阅者都应收到
            Assert.Equal(1, handler1Count);
            Assert.Equal(1, handler2Count);
            Assert.Equal(1, handler3Count);
        }

        /// <summary>
        /// 验证：不同事件类型的订阅者互不干扰。
        /// 发布 AlarmTriggeredEvent 不应触发 DeviceStateChangedEvent 的订阅者。
        /// </summary>
        [Fact]
        public void Publish_DifferentEventTypes_ShouldNotInterfere()
        {
            var eventBus = new EventBusService();
            int alarmCount = 0;
            int stateCount = 0;

            eventBus.Subscribe<AlarmTriggeredEvent>(_ => alarmCount++);
            eventBus.Subscribe<DeviceStateChangedEvent>(_ => stateCount++);

            // 只发布报警事件
            eventBus.Publish(new AlarmTriggeredEvent());

            // 只有报警订阅者收到
            Assert.Equal(1, alarmCount);
            Assert.Equal(0, stateCount);
        }

        /// <summary>
        /// 验证：某个订阅者抛出异常时，不影响其他订阅者的执行。
        /// 事件总线应捕获单个处理器的异常。
        /// </summary>
        [Fact]
        public void Publish_HandlerThrows_OtherHandlersShouldStillExecute()
        {
            var eventBus = new EventBusService();
            int successCount = 0;

            // 第一个订阅者正常执行
            eventBus.Subscribe<AlarmTriggeredEvent>(_ => successCount++);

            // 第二个订阅者抛出异常
            eventBus.Subscribe<AlarmTriggeredEvent>(_ =>
            {
                throw new InvalidOperationException("模拟异常");
            });

            // 第三个订阅者正常执行
            eventBus.Subscribe<AlarmTriggeredEvent>(_ => successCount++);

            // 发布事件
            eventBus.Publish(new AlarmTriggeredEvent());

            // 第一个和第三个应成功执行（第二个异常被捕获）
            Assert.Equal(2, successCount);
        }

        /// <summary>
        /// 使用 Theory 测试：发布多次事件时，订阅者应收到对应次数的通知。
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(10)]
        public void Publish_MultipleTimes_HandlerShouldBeCalledCorrectTimes(int publishCount)
        {
            var eventBus = new EventBusService();
            int callCount = 0;

            eventBus.Subscribe<AlarmTriggeredEvent>(_ => callCount++);

            // 发布 N 次
            for (int i = 0; i < publishCount; i++)
            {
                eventBus.Publish(new AlarmTriggeredEvent());
            }

            Assert.Equal(publishCount, callCount);
        }
    }
}
