using SmartMES.Core.Interfaces;

namespace SmartMES.Services.Device
{
    /// <summary>
    /// 传感器设备模拟实现（第二种设备类型）
    /// 模拟压力、温度、液位等传感器
    /// 与PlcDevice不同：传感器直接返回物理量测量值
    /// </summary>
    public class SensorDevice : IDevice
    {
        private bool _isConnected = false;
        private readonly Random _random = new Random();
        private double _lastValue = 0;
        private string _status = "未连接";

        /// <summary>传感器类型（Temperature/Pressure/Speed）</summary>
        private readonly string _sensorType;
        /// <summary>数据范围下限</summary>
        private readonly double _minValue;
        /// <summary>数据范围上限</summary>
        private readonly double _maxValue;

        public string Id { get; } = Guid.NewGuid().ToString();
        public string Name { get; }
        public string DeviceType => $"传感器({_sensorType})";
        public bool IsConnected => _isConnected;
        public string Status => _status;
        public double LastValue => _lastValue;

        public event EventHandler<DeviceStatusChangedEventArgs>? StatusChanged;

        /// <summary>
        /// 构造传感器设备
        /// </summary>
        /// <param name="name">设备名称</param>
        /// <param name="sensorType">传感器类型描述</param>
        /// <param name="minValue">最小测量值</param>
        /// <param name="maxValue">最大测量值</param>
        public SensorDevice(string name, string sensorType, double minValue, double maxValue)
        {
            Name = name;
            _sensorType = sensorType;
            _minValue = minValue;
            _maxValue = maxValue;
            // 初始值设为量程中间值
            _lastValue = (minValue + maxValue) / 2.0;
        }

        /// <summary>建立传感器连接并更新状态</summary>
        public async Task ConnectAsync()
        {
            _status = "初始化中...";
            RaiseStatusChanged();
            await Task.Delay(400);
            _isConnected = true;
            _status = "正常";
            RaiseStatusChanged();
        }

        /// <summary>断开传感器连接并更新状态</summary>
        public async Task DisconnectAsync()
        {
            await Task.Delay(100);
            _isConnected = false;
            _status = "离线";
            RaiseStatusChanged();
        }

        /// <summary>
        /// 读取传感器当前值
        /// 模拟真实传感器的随机漂移特性
        /// </summary>
        public async Task<double> ReadDataAsync()
        {
            if (!_isConnected)
                throw new InvalidOperationException($"{Name} 未连接");

            await Task.Delay(20);

            // 在当前值基础上随机漂移（漂移量为量程的2%）
            double drift = (_maxValue - _minValue) * 0.02 * (_random.NextDouble() - 0.5);
            _lastValue = Math.Max(_minValue, Math.Min(_maxValue, _lastValue + drift));

            return Math.Round(_lastValue, 2);
        }

        /// <summary>触发设备状态变化通知（供UI/监控订阅）</summary>
        private void RaiseStatusChanged()
        {
            StatusChanged?.Invoke(this,
                new DeviceStatusChangedEventArgs(_status, _isConnected));
        }
    }
}
