// ============================================================
// 文件：DomainEvents.cs
// 用途：领域事件定义
// 设计思路：
//   事件驱动架构的核心 — 所有模块间通信通过事件总线传递。
//   每个事件继承自 DomainEvent 基类，携带事件数据。
//   发布者不需要知道谁订阅了事件，实现模块间完全解耦。
//
//   使用场景示例：
//   - 运动控制完成 → 发布 AxisMotionCompletedEvent → 视觉系统收到后开始拍照
//   - 报警触发 → 发布 AlarmTriggeredEvent → UI更新报警面板 + 日志记录
// ============================================================

using SmartSemiCon.Domain.Enums;
using SmartSemiCon.Domain.Models;

namespace SmartSemiCon.Domain.Events
{
    /// <summary>
    /// 领域事件基类 — 所有事件的公共基类。
    /// 携带事件ID和时间戳，支持事件追踪和调试。
    /// </summary>
    public abstract class DomainEvent
    {
        /// <summary>事件唯一ID</summary>
        public Guid EventId { get; } = Guid.NewGuid();

        /// <summary>事件发生时间</summary>
        public DateTime Timestamp { get; } = DateTime.Now;

        /// <summary>事件来源模块</summary>
        public string Source { get; init; } = string.Empty;
    }

    // ---- 设备状态事件 ----

    /// <summary>
    /// 设备状态变更事件 — 设备状态机切换状态时触发。
    /// </summary>
    public class DeviceStateChangedEvent : DomainEvent
    {
        /// <summary>前一个状态</summary>
        public DeviceState PreviousState { get; init; }

        /// <summary>当前状态</summary>
        public DeviceState CurrentState { get; init; }

        /// <summary>触发原因</summary>
        public string Trigger { get; init; } = string.Empty;
    }

    // ---- 运动控制事件 ----

    /// <summary>
    /// 轴运动完成事件 — 某个轴完成运动到达目标位置时触发。
    /// </summary>
    public class AxisMotionCompletedEvent : DomainEvent
    {
        /// <summary>轴ID</summary>
        public int AxisId { get; init; }

        /// <summary>最终位置（mm）</summary>
        public double FinalPosition { get; init; }

        /// <summary>运动是否成功</summary>
        public bool IsSuccess { get; init; }
    }

    /// <summary>
    /// 轴状态变更事件 — 轴状态发生变化时触发。
    /// </summary>
    public class AxisStateChangedEvent : DomainEvent
    {
        /// <summary>轴ID</summary>
        public int AxisId { get; init; }

        /// <summary>前一个状态</summary>
        public AxisState PreviousState { get; init; }

        /// <summary>当前状态</summary>
        public AxisState CurrentState { get; init; }
    }

    /// <summary>
    /// 轴报警事件 — 运动轴发生报警时触发。
    /// </summary>
    public class AxisAlarmEvent : DomainEvent
    {
        /// <summary>轴ID</summary>
        public int AxisId { get; init; }

        /// <summary>报警代码</summary>
        public int AlarmCode { get; init; }

        /// <summary>报警描述</summary>
        public string Message { get; init; } = string.Empty;
    }

    // ---- 通讯事件 ----

    /// <summary>
    /// 通讯连接状态变更事件。
    /// </summary>
    public class ConnectionStateChangedEvent : DomainEvent
    {
        /// <summary>通讯通道名称</summary>
        public string ChannelName { get; init; } = string.Empty;

        /// <summary>前一个状态</summary>
        public ConnectionState PreviousState { get; init; }

        /// <summary>当前状态</summary>
        public ConnectionState CurrentState { get; init; }
    }

    /// <summary>
    /// 数据接收事件 — 通讯通道接收到数据时触发。
    /// </summary>
    public class DataReceivedEvent : DomainEvent
    {
        /// <summary>通讯通道名称</summary>
        public string ChannelName { get; init; } = string.Empty;

        /// <summary>接收到的数据</summary>
        public byte[] Data { get; init; } = Array.Empty<byte>();
    }

    // ---- SECS/GEM事件 ----

    /// <summary>
    /// SECS消息接收事件。
    /// </summary>
    public class SecsMessageReceivedEvent : DomainEvent
    {
        /// <summary>Stream号</summary>
        public int Stream { get; init; }

        /// <summary>Function号</summary>
        public int Function { get; init; }

        /// <summary>消息原始数据</summary>
        public byte[] RawData { get; init; } = Array.Empty<byte>();
    }

    /// <summary>
    /// GEM控制状态变更事件。
    /// </summary>
    public class GemControlStateChangedEvent : DomainEvent
    {
        /// <summary>前一个状态</summary>
        public GemControlState PreviousState { get; init; }

        /// <summary>当前状态</summary>
        public GemControlState CurrentState { get; init; }
    }

    // ---- 报警事件 ----

    /// <summary>
    /// 报警触发事件。
    /// </summary>
    public class AlarmTriggeredEvent : DomainEvent
    {
        /// <summary>报警记录</summary>
        public AlarmRecord Alarm { get; init; } = new();
    }

    /// <summary>
    /// 报警清除事件。
    /// </summary>
    public class AlarmClearedEvent : DomainEvent
    {
        /// <summary>报警代码</summary>
        public int AlarmCode { get; init; }

        /// <summary>清除操作者</summary>
        public string ClearedBy { get; init; } = string.Empty;
    }

    // ---- 视觉事件 ----

    /// <summary>
    /// 图像采集完成事件。
    /// </summary>
    public class ImageCapturedEvent : DomainEvent
    {
        /// <summary>相机ID</summary>
        public int CameraId { get; init; }

        /// <summary>图像数据（原始字节）</summary>
        public byte[] ImageData { get; init; } = Array.Empty<byte>();

        /// <summary>图像宽度</summary>
        public int Width { get; init; }

        /// <summary>图像高度</summary>
        public int Height { get; init; }
    }

    /// <summary>
    /// 视觉检测完成事件。
    /// </summary>
    public class VisionInspectionCompletedEvent : DomainEvent
    {
        /// <summary>相机ID</summary>
        public int CameraId { get; init; }

        /// <summary>检测结果</summary>
        public VisionResult Result { get; init; } = new();
    }

    // ---- 用户事件 ----

    /// <summary>
    /// 用户登录事件。
    /// </summary>
    public class UserLoggedInEvent : DomainEvent
    {
        /// <summary>用户信息</summary>
        public UserInfo User { get; init; } = new();
    }

    /// <summary>
    /// 配方切换事件。
    /// </summary>
    public class RecipeChangedEvent : DomainEvent
    {
        /// <summary>前一个配方名称</summary>
        public string? PreviousRecipeName { get; init; }

        /// <summary>当前配方名称</summary>
        public string CurrentRecipeName { get; init; } = string.Empty;
    }
}
