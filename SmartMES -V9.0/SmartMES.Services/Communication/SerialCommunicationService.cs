using SmartMES.Core.Interfaces;

namespace SmartMES.Services.Communication
{
    /// <summary>模拟串口通信服务。</summary>
    public class SerialCommunicationService : ICommunicationService
    {
        private bool _isConnected = false;
        private readonly string _portName;
        private readonly int _baudRate;
        private readonly Random _random = new Random();
        private readonly ILoggingService _logger;

        public bool IsConnected => _isConnected;
        public string ProtocolName => $"SerialPort({_portName}@{_baudRate}bps)";

        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler<bool>? ConnectionChanged;

        /// <summary>构造串口通信服务。</summary>
        public SerialCommunicationService(string portName, int baudRate, ILoggingService logger)
        {
            _portName = portName;
            _baudRate = baudRate;
            _logger = logger;
        }

        /// <summary>连接串口并触发连接事件。</summary>
        public async Task ConnectAsync()
        {
            _logger.LogCommunication($"[串口] 正在打开 {_portName} @ {_baudRate}bps...");
            await Task.Delay(300);
            _isConnected = true;
            ConnectionChanged?.Invoke(this, true);
            _logger.LogCommunication($"[串口] {_portName} 已打开");
        }

        /// <summary>断开串口并触发连接事件。</summary>
        public async Task DisconnectAsync()
        {
            await Task.Delay(100);
            _isConnected = false;
            ConnectionChanged?.Invoke(this, false);
            _logger.LogCommunication($"[串口] {_portName} 已关闭");
        }

        /// <summary>发送字节流到串口。</summary>
        public async Task SendAsync(byte[] data)
        {
            if (!_isConnected)
                throw new InvalidOperationException("串口未打开，无法发送");

            await Task.Delay(20);
            var hex = BitConverter.ToString(data);
            _logger.LogCommunication($"[串口] TX {data.Length}字节: {hex}");
        }

        /// <summary>接收字节流并触发接收事件。</summary>
        public async Task<byte[]> ReceiveAsync()
        {
            await Task.Delay(30);
            var buf = new byte[8];
            _random.NextBytes(buf);
            var hex = BitConverter.ToString(buf);
            _logger.LogCommunication($"[串口] RX {buf.Length}字节: {hex}");
            DataReceived?.Invoke(this, buf);
            return buf;
        }
    }
}
