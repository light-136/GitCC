using SmartMES.Core.Interfaces;

namespace SmartMES.Services.Device
{
    /// <summary>
    /// PLC 设备模拟实现。
    /// </summary>
    public class PlcDevice : IDevice
    {
        private bool _isConnected = false;
        private readonly Random _random = new Random();
        private double _lastValue = 0;
        private string _status = "未连接";

        public string Id { get; } = Guid.NewGuid().ToString();
        public string Name { get; }
        public string DeviceType => "PLC";
        public bool IsConnected => _isConnected;
        public string Status => _status;
        public double LastValue => _lastValue;

        /// <summary>设备状态变化事件。</summary>
        public event EventHandler<DeviceStatusChangedEventArgs>? StatusChanged;

        /// <summary>构造 PLC 设备实例。</summary>
        public PlcDevice(string name = "西门子S7-1200")
        {
            Name = name;
        }

        /// <summary>模拟连接 PLC。</summary>
        public async Task ConnectAsync()
        {
            _status = "连接中...";
            RaiseStatusChanged();

            await Task.Delay(800);

            _isConnected = true;
            _status = "已连接";
            RaiseStatusChanged();
        }

        /// <summary>模拟断开 PLC。</summary>
        public async Task DisconnectAsync()
        {
            await Task.Delay(200);
            _isConnected = false;
            _status = "已断开";
            RaiseStatusChanged();
        }

        /// <summary>模拟读取 PLC 数据。</summary>
        public async Task<double> ReadDataAsync()
        {
            if (!_isConnected)
                throw new InvalidOperationException($"{Name} 未连接，无法读取数据");

            await Task.Delay(50);

            _lastValue = Math.Max(20, Math.Min(100, _lastValue + (_random.NextDouble() - 0.5) * 5));
            if (_lastValue == 0)
                _lastValue = 50 + _random.NextDouble() * 20;

            return _lastValue;
        }

        /// <summary>触发状态变更事件。</summary>
        private void RaiseStatusChanged()
        {
            StatusChanged?.Invoke(this, new DeviceStatusChangedEventArgs(_status, _isConnected));
        }
    }
}
