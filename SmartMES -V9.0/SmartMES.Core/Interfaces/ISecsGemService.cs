// ============================================================
// 文件：ISecsGemService.cs
// 用途：SECS/GEM 服务接口定义
// 设计思路：
//   定义设备端 SECS/GEM 通信的完整接口，涵盖：
//   - HSMS 连接管理（连接/断开/选择）
//   - GEM 状态控制（上线/离线/模式切换）
//   - 消息收发（通用发送 + 特定消息处理器注册）
//   - 数据管理（SV/EC/CE/报告/告警）
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Core.Interfaces
{
    /// <summary>
    /// SECS/GEM 服务接口 — 设备端半导体通信的顶层抽象。
    /// 实现类需要组合 HSMS 传输层、SECS-II 编解码、GEM 状态机等子模块。
    /// </summary>
    public interface ISecsGemService
    {
        // ---- 状态属性 ----

        /// <summary>HSMS 连接状态。</summary>
        HsmsState ConnectionState { get; }

        /// <summary>GEM 控制状态。</summary>
        GemControlState ControlState { get; }

        /// <summary>GEM 通信状态。</summary>
        GemCommunicationState CommunicationState { get; }

        /// <summary>HSMS 定时器配置。</summary>
        HsmsTimerConfig TimerConfig { get; set; }

        // ---- 连接管理 ----

        /// <summary>
        /// 连接到 HSMS 主机。
        /// </summary>
        /// <param name="host">主机 IP 地址。</param>
        /// <param name="port">主机端口号（默认 5000）。</param>
        /// <returns>连接是否成功。</returns>
        Task<bool> ConnectAsync(string host, int port);

        /// <summary>
        /// 断开 HSMS 连接。
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// 执行 HSMS Select 握手。
        /// </summary>
        /// <returns>Select 是否成功。</returns>
        Task<bool> SelectAsync();

        // ---- GEM 状态控制 ----

        /// <summary>
        /// 启用通信功能（进入 WaitCommunicating 状态）。
        /// </summary>
        Task EnableAsync();

        /// <summary>
        /// 禁用通信功能。
        /// </summary>
        Task DisableAsync();

        /// <summary>
        /// 请求上线。
        /// </summary>
        /// <param name="remoteMode">true=远程模式，false=本地模式。</param>
        /// <returns>上线是否成功。</returns>
        Task<bool> GoOnlineAsync(bool remoteMode = true);

        /// <summary>
        /// 请求离线。
        /// </summary>
        Task GoOfflineAsync();

        // ---- 消息收发 ----

        /// <summary>
        /// 发送 SECS 消息并等待回复。
        /// </summary>
        /// <param name="message">要发送的消息。</param>
        /// <returns>对方的回复消息。</returns>
        Task<SecsMessage> SendAsync(SecsMessage message);

        /// <summary>
        /// 发送 SECS 消息（不等待回复）。
        /// </summary>
        Task SendOnlyAsync(SecsMessage message);

        /// <summary>
        /// 注册特定 Stream/Function 的消息处理器。
        /// 当收到匹配的主消息时，自动调用处理器生成回复。
        /// </summary>
        /// <param name="stream">消息流号。</param>
        /// <param name="function">消息功能号。</param>
        /// <param name="handler">处理器函数（输入主消息，返回回复消息）。</param>
        void RegisterMessageHandler(int stream, int function,
            Func<SecsMessage, Task<SecsMessage>> handler);

        // ---- 数据管理 ----

        /// <summary>
        /// 注册状态变量。
        /// </summary>
        void RegisterStatusVariable(StatusVariable sv);

        /// <summary>
        /// 更新状态变量的值。
        /// </summary>
        void UpdateStatusVariable(uint svId, object value);

        /// <summary>
        /// 注册设备常量。
        /// </summary>
        void RegisterEquipmentConstant(EquipmentConstant ec);

        /// <summary>
        /// 注册采集事件。
        /// </summary>
        void RegisterCollectionEvent(CollectionEvent ce);

        /// <summary>
        /// 触发采集事件（自动发送 S6F11）。
        /// </summary>
        /// <param name="ceId">事件 ID。</param>
        /// <param name="additionalData">附加数据变量。</param>
        Task TriggerCollectionEventAsync(uint ceId,
            Dictionary<uint, object>? additionalData = null);

        /// <summary>
        /// 注册告警定义。
        /// </summary>
        void RegisterAlarm(SecsAlarm alarm);

        /// <summary>
        /// 设置告警（激活，发送 S5F1）。
        /// </summary>
        Task SetAlarmAsync(uint alarmId, string text);

        /// <summary>
        /// 清除告警（发送 S5F1 清除）。
        /// </summary>
        Task ClearAlarmAsync(uint alarmId);

        // ---- 远程命令 ----

        /// <summary>
        /// 注册远程命令处理器。
        /// </summary>
        /// <param name="commandName">命令名称（如 START/STOP/PAUSE）。</param>
        /// <param name="handler">
        /// 处理器函数：输入参数字典，返回 (HCACK, 原因描述)。
        /// HCACK: 0=OK, 1=无效命令, 2=当前无法执行, 3=参数错误。
        /// </param>
        void RegisterRemoteCommand(string commandName,
            Func<Dictionary<string, string>, (byte HCACK, string Reason)> handler);

        // ---- 工艺程序 ----

        /// <summary>
        /// 获取已存储的工艺程序列表。
        /// </summary>
        IReadOnlyList<string> GetProcessProgramList();

        /// <summary>
        /// 获取指定工艺程序的内容。
        /// </summary>
        byte[]? GetProcessProgram(string ppId);

        // ---- 消息日志 ----

        /// <summary>
        /// 获取最近的消息日志。
        /// </summary>
        IReadOnlyList<SecsMessage> GetMessageLog(int count = 100);

        // ---- 事件 ----

        /// <summary>收到消息时触发。</summary>
        event EventHandler<SecsMessage>? MessageReceived;

        /// <summary>发送消息时触发。</summary>
        event EventHandler<SecsMessage>? MessageSent;

        /// <summary>控制状态变化时触发。</summary>
        event EventHandler<GemControlState>? ControlStateChanged;

        /// <summary>通信状态变化时触发。</summary>
        event EventHandler<GemCommunicationState>? CommunicationStateChanged;

        /// <summary>HSMS 连接状态变化时触发。</summary>
        event EventHandler<HsmsState>? ConnectionStateChanged;
    }
}
