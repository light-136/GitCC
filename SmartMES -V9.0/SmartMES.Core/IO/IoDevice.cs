namespace SmartMES.Core.IO
{
    /// <summary>IO閫氶亾绫诲瀷</summary>
    public enum IoChannelType { DigitalInput, DigitalOutput, AnalogInput, AnalogOutput }

    /// <summary>IO閫氶亾瀹氫箟</summary>
    public class IoChannel
    {
        public int    Address  { get; init; }
        public string Name     { get; init; } = string.Empty;
        public IoChannelType Type { get; init; }
        public bool   Value    { get; set; }      // 鏁板瓧閲?
        public double AnalogValue { get; set; }   // 妯℃嫙閲?
        public DateTime LastUpdate { get; set; } = DateTime.Now;
    }

    /// <summary>IO璁惧鎺ュ彛</summary>
    public interface IIoDevice
    {
        string DeviceName { get; }
        bool   ReadInput(int address);
        void   WriteOutput(int address, bool value);
        double ReadAnalog(int address);
        void   WriteAnalog(int address, double value);
        IReadOnlyList<IoChannel> GetChannels();
        event EventHandler<IoChannel>? ChannelChanged;
    }

    /// <summary>
    /// 妯℃嫙IO璁惧锛堢敤浜庝豢鐪?娴嬭瘯锛屾棤闇€鐪熷疄纭欢锛?
    /// 鐢熶骇鐜鏇挎崲涓?ModbusIoDevice / OpcUaIoDevice
    /// </summary>
    public class SimulatedIoDevice : IIoDevice
    {
        public string DeviceName { get; }
        private readonly Dictionary<int, IoChannel> _channels = new();
        private readonly object _lock = new();
        private readonly System.Threading.Timer _simulationTimer;

        public event EventHandler<IoChannel>? ChannelChanged;

        /// <summary>
        /// 自动补齐：SimulatedIoDevice 方法说明。
        /// </summary>
        public SimulatedIoDevice(string name = "SimIO")
        {
            DeviceName = name;
            // 鍒濆鍖?6涓狣I + 16涓狣O + 4涓狝I + 4涓狝O
            for (int i = 0; i < 16; i++)
            {
                _channels[i]      = new IoChannel { Address=i,      Name=$"DI{i:D2}", Type=IoChannelType.DigitalInput };
                _channels[100+i]  = new IoChannel { Address=100+i,  Name=$"DO{i:D2}", Type=IoChannelType.DigitalOutput };
            }
            for (int i = 0; i < 4; i++)
            {
                _channels[200+i]  = new IoChannel { Address=200+i,  Name=$"AI{i}",    Type=IoChannelType.AnalogInput };
                _channels[300+i]  = new IoChannel { Address=300+i,  Name=$"AO{i}",    Type=IoChannelType.AnalogOutput };
            }
            // 浠跨湡瀹氭椂鍣細闅忔満鍙樺寲DI鍜孉I鍊?
            _simulationTimer = new System.Threading.Timer(_ => SimulateInputs(), null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        }

        /// <summary>
        /// 自动补齐：ReadInput 方法说明。
        /// </summary>
        public bool ReadInput(int address)
        { lock (_lock) return _channels.TryGetValue(address, out var ch) && ch.Value; }

        /// <summary>
        /// 自动补齐：WriteOutput 方法说明。
        /// </summary>
        public void WriteOutput(int address, bool value)
        {
            lock (_lock)
            {
                if (!_channels.TryGetValue(address, out var ch)) return;
                ch.Value = value;
                ch.LastUpdate = DateTime.Now;
                ChannelChanged?.Invoke(this, ch);
            }
        }

        /// <summary>
        /// 自动补齐：ReadAnalog 方法说明。
        /// </summary>
        public double ReadAnalog(int address)
        { lock (_lock) return _channels.TryGetValue(address, out var ch) ? ch.AnalogValue : 0; }

        /// <summary>
        /// 自动补齐：WriteAnalog 方法说明。
        /// </summary>
        public void WriteAnalog(int address, double value)
        {
            lock (_lock)
            {
                if (!_channels.TryGetValue(address, out var ch)) return;
                ch.AnalogValue = value;
                ch.LastUpdate = DateTime.Now;
                ChannelChanged?.Invoke(this, ch);
            }
        }

        /// <summary>
        /// 自动补齐：GetChannels 方法说明。
        /// </summary>
        public IReadOnlyList<IoChannel> GetChannels()
        { lock (_lock) return _channels.Values.ToList(); }

        /// <summary>
        /// 自动补齐：SimulateInputs 方法说明。
        /// </summary>
        private void SimulateInputs()
        {
            var rnd = new Random();
            lock (_lock)
            {
                // 闅忔満缈昏浆閮ㄥ垎DI
                for (int i = 0; i < 4; i++)
                {
                    if (_channels.TryGetValue(i, out var ch) && rnd.NextDouble() < 0.1)
                    {
                        ch.Value = !ch.Value;
                        ch.LastUpdate = DateTime.Now;
                        ChannelChanged?.Invoke(this, ch);
                    }
                }
                // 妯℃嫙AI鍙樺寲
                for (int i = 200; i < 204; i++)
                {
                    if (_channels.TryGetValue(i, out var ch))
                    {
                        ch.AnalogValue = Math.Round(ch.AnalogValue + (rnd.NextDouble()-0.5)*5, 2);
                        ch.AnalogValue = Math.Max(0, Math.Min(100, ch.AnalogValue));
                        ch.LastUpdate = DateTime.Now;
                    }
                }
            }
        }
    }
}
