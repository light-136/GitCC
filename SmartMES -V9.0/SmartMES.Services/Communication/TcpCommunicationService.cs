using SmartMES.Core.Interfaces;

namespace SmartMES.Services.Communication
{
    /// <summary>模拟 TCP 通信服务。</summary>
    public class TcpCommunicationService : ICommunicationService
    {
        private bool _isConnected = false;
        private readonly string _serverIp;
        private readonly int _port;
        private readonly Random _random = new Random();
        private readonly ILoggingService _logger;

        public bool IsConnected => _isConnected;
        public string ProtocolName => "TCP";

        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler<bool>? ConnectionChanged;

        /// <summary>构造 TCP 通信服务。</summary>
        public TcpCommunicationService(string serverIp, int port, ILoggingService logger)
        {
            _serverIp = serverIp;
            _port = port;
            _logger = logger;
        }

        /// <summary>模拟连接 TCP 服务器。</summary>
        public async Task ConnectAsync()
        {
            _logger.LogCommunication($"[TCP] 正在连接 {_serverIp}:{_port}...");
            await Task.Delay(500);
            _isConnected = true;
            ConnectionChanged?.Invoke(this, true);
            _logger.LogCommunication($"[TCP] 已连接到 {_serverIp}:{_port}");
        }

        /// <summary>模拟断开 TCP 连接。</summary>
        public async Task DisconnectAsync()
        {
            await Task.Delay(100);
            _isConnected = false;
            ConnectionChanged?.Invoke(this, false);
            _logger.LogCommunication("[TCP] 连接已断开");
        }

        /// <summary>模拟发送数据。</summary>
        public async Task SendAsync(byte[] data)
        {
            if (!_isConnected)
                throw new InvalidOperationException("TCP未连接，无法发送数据");

            await Task.Delay(10);
            var hex = BitConverter.ToString(data);
            _logger.LogCommunication($"[TCP] 发送 {data.Length} 字节: {hex}");
        }

        /// <summary>模拟接收数据。</summary>
        public async Task<byte[]> ReceiveAsync()
        {
            await Task.Delay(50);
            var buf = new byte[4];
            _random.NextBytes(buf);
            var hex = BitConverter.ToString(buf);
            _logger.LogCommunication($"[TCP] 接收 {buf.Length} 字节: {hex}");
            DataReceived?.Invoke(this, buf);
            return buf;
        }
    }
}
