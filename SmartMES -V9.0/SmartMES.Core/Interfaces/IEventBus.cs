namespace SmartMES.Core.Interfaces
{
    /// <summary>
    /// 事件总线接口（EventAggregator模式）
    /// 用于模块间解耦通信：发布者不需要知道订阅者，订阅者不需要知道发布者
    /// 这是模块化架构中的核心基础设施
    /// </summary>
    public interface IEventBus
    {
        /// <summary>
        /// 订阅事件
        /// </summary>
        /// <typeparam name="TEvent">事件类型</typeparam>
        /// <param name="handler">事件处理委托</param>
        void Subscribe<TEvent>(Action<TEvent> handler);

        /// <summary>
        /// 取消订阅
        /// </summary>
        /// <typeparam name="TEvent">事件类型</typeparam>
        /// <param name="handler">要取消的处理委托</param>
        void Unsubscribe<TEvent>(Action<TEvent> handler);

        /// <summary>
        /// 发布事件（同步）
        /// </summary>
        /// <typeparam name="TEvent">事件类型</typeparam>
        /// <param name="eventData">事件数据</param>
        void Publish<TEvent>(TEvent eventData);
    }

    // ==================== 系统内置事件定义 ====================

    /// <summary>设备状态变化事件</summary>
    public class DeviceStatusEvent
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>新报警事件</summary>
    public class NewAlarmEvent
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;
    }

    /// <summary>流程状态变化事件</summary>
    public class AutomationStatusEvent
    {
        public string StepName { get; set; } = string.Empty;
        public bool IsRunning { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>用户登录/登出事件</summary>
    public class UserLoginEvent
    {
        public string Username { get; set; } = string.Empty;
        public bool IsLogin { get; set; }
        public string Role { get; set; } = string.Empty;
        public SmartMES.Core.Models.PagePermissions? Permissions { get; set; }
    }
}
