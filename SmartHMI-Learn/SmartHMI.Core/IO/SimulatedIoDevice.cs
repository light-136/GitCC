namespace SmartHMI.Core.IO;

public class SimulatedIoDevice : IIoDevice, IDisposable
{
    private readonly Dictionary<int, IoChannel> _channels = new();
    private readonly Timer _timer;
    private readonly Random _rng = new();
    private readonly Lock _lock = new();

    public event EventHandler<IoChannel>? ChannelChanged;

    public SimulatedIoDevice()
    {
        // 16 DI channels (addr 0-15)
        for (int i = 0; i < 16; i++)
            _channels[i] = new IoChannel { Address = i, Name = $"DI{i:D2}", Type = IoChannelType.DigitalInput, Value = false };

        // 16 DO channels (addr 100-115)
        for (int i = 0; i < 16; i++)
            _channels[100 + i] = new IoChannel { Address = 100 + i, Name = $"DO{i:D2}", Type = IoChannelType.DigitalOutput, Value = false };

        // 4 AI channels (addr 200-203)
        for (int i = 0; i < 4; i++)
            _channels[200 + i] = new IoChannel { Address = 200 + i, Name = $"AI{i:D2}", Type = IoChannelType.AnalogInput, Value = 0.0 };

        // 4 AO channels (addr 300-303)
        for (int i = 0; i < 4; i++)
            _channels[300 + i] = new IoChannel { Address = 300 + i, Name = $"AO{i:D2}", Type = IoChannelType.AnalogOutput, Value = 0.0 };

        _timer = new Timer(Simulate, null, 1000, 1500);
    }

    private void Simulate(object? _)
    {
        lock (_lock)
        {
            // Randomly toggle some DI channels
            for (int i = 0; i < 16; i++)
            {
                if (_rng.NextDouble() < 0.1)
                {
                    var ch = _channels[i];
                    ch.Value = !(bool)ch.Value;
                    ch.LastUpdated = DateTime.Now;
                    ChannelChanged?.Invoke(this, ch);
                }
            }

            // Update AI channels with random walk
            for (int i = 0; i < 4; i++)
            {
                var ch = _channels[200 + i];
                var current = (double)ch.Value;
                var next = Math.Clamp(current + (_rng.NextDouble() - 0.5) * 5, 0, 100);
                ch.Value = Math.Round(next, 2);
                ch.LastUpdated = DateTime.Now;
                ChannelChanged?.Invoke(this, ch);
            }
        }
    }

    public bool ReadInput(int address)
    {
        lock (_lock)
            return _channels.TryGetValue(address, out var ch) ? ch.AsBool() : false;
    }

    public void WriteOutput(int address, bool value)
    {
        lock (_lock)
        {
            if (_channels.TryGetValue(address, out var ch))
            {
                ch.Value = value;
                ch.LastUpdated = DateTime.Now;
                ChannelChanged?.Invoke(this, ch);
            }
        }
    }

    public double ReadAnalog(int address)
    {
        lock (_lock)
            return _channels.TryGetValue(address, out var ch) ? ch.AsDouble() : 0.0;
    }

    public void WriteAnalog(int address, double value)
    {
        lock (_lock)
        {
            if (_channels.TryGetValue(address, out var ch))
            {
                ch.Value = value;
                ch.LastUpdated = DateTime.Now;
                ChannelChanged?.Invoke(this, ch);
            }
        }
    }

    public IReadOnlyList<IoChannel> GetChannels()
    {
        lock (_lock)
            return _channels.Values.OrderBy(c => c.Address).ToList();
    }

    public void Dispose() => _timer.Dispose();
}
