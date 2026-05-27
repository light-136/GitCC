// ============================================================
// 文件：MqttService.cs
// 层次：基础设施层 (Infrastructure Layer) — MQTT 客户端服务
// 职责：
//   使用 MQTTnet 4.x 库实现 MQTT 客户端，提供：
//   - ConnectAsync / DisconnectAsync 连接管理
//   - PublishAsync 发布消息
//   - SubscribeAsync / UnsubscribeAsync 订阅/取消订阅
//   - 自动重连（MQTTnet 内置）
//   - QoS 级别支持（0/1/2）
//   - 消息接收事件
// 设计思路：
//   MQTT 在工业 IoT 中广泛用于设备数据采集：
//     - PLC 发布传感器数据到 Broker
//     - 平台订阅相关 Topic 接收实时数据
//   MQTTnet 4.x API 变化较大（相对 3.x），此实现使用最新 4.x API。
//   消息接收使用 ApplicationMessageReceivedAsync 事件，
//   内部通过 C# event（MessageReceived）暴露给上层，并发布到事件总线。
// 注意：
//   MQTTnet 4.3.x API：MqttClientOptions 的构造使用 MqttClientOptionsBuilder
//   connect 使用 ConnectAsync 返回 MqttClientConnectResult
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using SmartIndustry.Domain.Events;
using SmartIndustry.Domain.Interfaces;
using SmartIndustry.Domain.Enums;

namespace SmartIndustry.Infrastructure.Network.Mqtt
{
    // ----------------------------------------------------------------
    // MQTT 服务配置
    // ----------------------------------------------------------------

    /// <summary>
    /// MQTT 客户端配置参数
    /// </summary>
    public class MqttServiceOptions
    {
        /// <summary>MQTT Broker 主机名或 IP</summary>
        public string BrokerHost { get; set; } = "127.0.0.1";

        /// <summary>MQTT Broker 端口（默认 1883，TLS 使用 8883）</summary>
        public int BrokerPort { get; set; } = 1883;

        /// <summary>客户端唯一标识符（建议使用设备序列号或随机 GUID）</summary>
        public string ClientId { get; set; } = $"SmartIndustry_{Guid.NewGuid():N}";

        /// <summary>通道名称（日志和事件路由标识）</summary>
        public string ChannelName { get; set; } = "MqttClient";

        /// <summary>认证用户名（Broker 未启用认证时留空）</summary>
        public string? Username { get; set; }

        /// <summary>认证密码</summary>
        public string? Password { get; set; }

        /// <summary>连接超时（秒，默认10秒）</summary>
        public int ConnectionTimeoutSeconds { get; set; } = 10;

        /// <summary>保活间隔（秒，MQTT PINGREQ 间隔，默认60秒）</summary>
        public int KeepAliveSeconds { get; set; } = 60;

        /// <summary>是否使用 TLS/SSL（连接加密）</summary>
        public bool UseTls { get; set; } = false;

        /// <summary>是否启用自动重连（默认启用）</summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>自动重连间隔（秒，默认5秒）</summary>
        public int ReconnectIntervalSeconds { get; set; } = 5;
    }

    /// <summary>
    /// 收到的 MQTT 消息数据
    /// </summary>
    public record MqttMessage(
        string Topic,
        byte[] Payload,
        MqttQualityOfServiceLevel QoS,
        bool Retain
    );

    /// <summary>
    /// MQTT 客户端服务实现（MQTTnet 4.x）。
    /// 提供工业 IoT 场景下的 MQTT 发布/订阅能力。
    /// </summary>
    public class MqttService : IDisposable
    {
        // ----------------------------------------------------------------
        // 依赖字段
        // ----------------------------------------------------------------

        private readonly MqttServiceOptions _options;
        private readonly IEventBus _eventBus;

        /// <summary>MQTTnet 客户端实例（线程安全）</summary>
        private readonly IMqttClient _mqttClient;

        /// <summary>客户端配置选项（Connect 时使用）</summary>
        private readonly MqttClientOptions _mqttOptions;

        // ----------------------------------------------------------------
        // 状态和同步
        // ----------------------------------------------------------------

        private volatile ConnectionState _state = ConnectionState.Disconnected;
        private readonly CancellationTokenSource _lifetimeCts = new();

        // ----------------------------------------------------------------
        // 外部事件
        // ----------------------------------------------------------------

        /// <summary>收到订阅 Topic 消息事件（Topic, 消息负载字节）</summary>
        public event Func<MqttMessage, Task>? MessageReceived;

        /// <summary>连接状态变更事件</summary>
        public event Action<ConnectionState>? StateChanged;

        // ----------------------------------------------------------------
        // 属性
        // ----------------------------------------------------------------

        /// <summary>当前连接状态</summary>
        public ConnectionState State => _state;

        /// <summary>是否已连接</summary>
        public bool IsConnected => _mqttClient.IsConnected;

        /// <summary>
        /// 构造函数：初始化 MQTTnet 客户端并注册内部事件处理器
        /// </summary>
        public MqttService(MqttServiceOptions options, IEventBus eventBus)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));

            // 使用 MqttFactory 创建客户端实例
            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            // 构建连接选项
            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithClientId(_options.ClientId)
                .WithTcpServer(_options.BrokerHost, _options.BrokerPort)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(_options.KeepAliveSeconds))
                .WithTimeout(TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds));

            // 可选：认证
            if (!string.IsNullOrEmpty(_options.Username))
                optionsBuilder.WithCredentials(_options.Username, _options.Password);

            // 可选：TLS
            if (_options.UseTls)
                optionsBuilder.WithTlsOptions(tls => tls.UseTls());

            _mqttOptions = optionsBuilder.Build();

            // 注册 MQTTnet 内部事件：消息接收
            _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

            // 注册连接/断开事件
            _mqttClient.ConnectedAsync += OnConnectedAsync;
            _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
        }

        // ================================================================
        // 连接管理
        // ================================================================

        /// <summary>
        /// 连接到 MQTT Broker
        /// </summary>
        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_mqttClient.IsConnected) return;

            UpdateState(ConnectionState.Connecting);

            try
            {
                await _mqttClient.ConnectAsync(_mqttOptions, cancellationToken);
                // 连接成功状态在 OnConnectedAsync 中更新
            }
            catch (Exception)
            {
                UpdateState(ConnectionState.Error);
                // 如果启用自动重连，后台启动重连（MQTTnet 4.x 需要手动实现自动重连）
                if (_options.AutoReconnect)
                    _ = Task.Run(() => ReconnectAsync(_lifetimeCts.Token));
                throw;
            }
        }

        /// <summary>
        /// 主动断开 MQTT 连接
        /// </summary>
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (!_mqttClient.IsConnected) return;

            var disconnectOptions = new MqttClientDisconnectOptions
            {
                Reason = MqttClientDisconnectOptionsReason.NormalDisconnection
            };

            await _mqttClient.DisconnectAsync(disconnectOptions, cancellationToken);
        }

        // ================================================================
        // 发布/订阅
        // ================================================================

        /// <summary>
        /// 发布消息到指定 Topic
        /// </summary>
        /// <param name="topic">目标 Topic（如 "factory/line1/axis1/position"）</param>
        /// <param name="payload">消息负载字节</param>
        /// <param name="qos">服务质量等级（0=最多一次，1=至少一次，2=恰好一次）</param>
        /// <param name="retain">是否保留消息（Broker 保留最后一条，新订阅者立即收到）</param>
        public async Task PublishAsync(
            string topic,
            byte[] payload,
            MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce,
            bool retain = false,
            CancellationToken cancellationToken = default)
        {
            if (!_mqttClient.IsConnected)
                throw new InvalidOperationException("MQTT 客户端未连接，无法发布消息");

            // 构建 MQTT 消息（MQTTnet 4.x API）
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(qos)
                .WithRetainFlag(retain)
                .Build();

            await _mqttClient.PublishAsync(message, cancellationToken);
        }

        /// <summary>
        /// 发布文本消息（UTF-8 编码）到指定 Topic
        /// </summary>
        public async Task PublishAsync(
            string topic,
            string payload,
            MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce,
            bool retain = false,
            CancellationToken cancellationToken = default)
        {
            await PublishAsync(topic, System.Text.Encoding.UTF8.GetBytes(payload), qos, retain, cancellationToken);
        }

        /// <summary>
        /// 订阅指定 Topic（支持 MQTT 通配符：+ 单级，# 多级）
        /// </summary>
        /// <param name="topic">订阅 Topic（如 "factory/#" 订阅所有 factory/ 子 Topic）</param>
        /// <param name="qos">期望的 QoS 等级（Broker 可降级）</param>
        public async Task SubscribeAsync(
            string topic,
            MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce,
            CancellationToken cancellationToken = default)
        {
            if (!_mqttClient.IsConnected)
                throw new InvalidOperationException("MQTT 客户端未连接，无法订阅");

            // MQTTnet 4.x 订阅 API
            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(topic, qos)
                .Build();

            await _mqttClient.SubscribeAsync(subscribeOptions, cancellationToken);
        }

        /// <summary>
        /// 取消订阅指定 Topic
        /// </summary>
        public async Task UnsubscribeAsync(string topic, CancellationToken cancellationToken = default)
        {
            if (!_mqttClient.IsConnected) return;

            var unsubscribeOptions = new MqttClientUnsubscribeOptionsBuilder()
                .WithTopicFilter(topic)
                .Build();

            await _mqttClient.UnsubscribeAsync(unsubscribeOptions, cancellationToken);
        }

        // ================================================================
        // 内部事件处理器
        // ================================================================

        /// <summary>
        /// MQTTnet 消息接收处理器（在 MQTTnet 内部线程调用）。
        /// 将消息转发给外部订阅者的 MessageReceived 事件，并发布到事件总线。
        /// </summary>
        private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            var message = new MqttMessage(
                Topic: e.ApplicationMessage.Topic,
                Payload: e.ApplicationMessage.PayloadSegment.ToArray(),
                QoS: e.ApplicationMessage.QualityOfServiceLevel,
                Retain: e.ApplicationMessage.Retain
            );

            // 触发外部事件（允许 null，处理器可选）
            if (MessageReceived != null)
            {
                try
                {
                    await MessageReceived.Invoke(message);
                }
                catch
                {
                    // 事件处理器异常不影响 MQTT 连接
                }
            }

            // 发布到平台事件总线
            await _eventBus.PublishAsync(new DataReceivedEvent(
                _options.ChannelName,
                message.Payload));
        }

        /// <summary>MQTTnet 连接成功事件处理</summary>
        private Task OnConnectedAsync(MqttClientConnectedEventArgs e)
        {
            UpdateState(ConnectionState.Connected);
            return Task.CompletedTask;
        }

        /// <summary>MQTTnet 断开事件处理（触发自动重连）</summary>
        private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
        {
            UpdateState(ConnectionState.Disconnected);

            // 如果不是主动断开且启用自动重连，启动重连后台任务
            if (_options.AutoReconnect &&
                !_lifetimeCts.IsCancellationRequested &&
                e.Reason != MqttClientDisconnectReason.NormalDisconnection)
            {
                _ = Task.Run(() => ReconnectAsync(_lifetimeCts.Token));
            }

            return Task.CompletedTask;
        }

        /// <summary>自动重连后台逻辑（连接断开后等待间隔后重试）</summary>
        private async Task ReconnectAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && !_mqttClient.IsConnected)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectIntervalSeconds), cancellationToken);

                    if (!cancellationToken.IsCancellationRequested)
                        await _mqttClient.ConnectAsync(_mqttOptions, cancellationToken);

                    if (_mqttClient.IsConnected) break; // 重连成功
                }
                catch (OperationCanceledException) { break; }
                catch { /* 继续重试 */ }
            }
        }

        /// <summary>更新连接状态并发布事件</summary>
        private void UpdateState(ConnectionState newState)
        {
            var old = _state;
            _state = newState;

            if (old != newState)
            {
                StateChanged?.Invoke(newState);
                _ = _eventBus.PublishAsync(new CommunicationStateChangedEvent(
                    _options.ChannelName, old, newState));
            }
        }

        // ================================================================
        // IDisposable
        // ================================================================

        public void Dispose()
        {
            _lifetimeCts.Cancel();
            _mqttClient.DisconnectedAsync -= OnDisconnectedAsync;
            _mqttClient.ConnectedAsync -= OnConnectedAsync;
            _mqttClient.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;

            if (_mqttClient.IsConnected)
                _mqttClient.DisconnectAsync().GetAwaiter().GetResult();

            _mqttClient.Dispose();
            _lifetimeCts.Dispose();
        }
    }
}
