using SmartHMI.Core.Events;
using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using SmartHMI.Core.StateMachine;

namespace SmartHMI.Modules.Motion;

public class AxisController : IDisposable
{
    private readonly AxisModel _axis;
    private readonly DeviceStateMachine _sm = new();
    private readonly IEventBus _eventBus;
    private readonly ILoggingService _logger;
    private readonly Timer _simulationTimer;
    private readonly Random _rng = new();

    public AxisModel Axis => _axis;
    public event EventHandler<AxisModel>? AxisUpdated;

    public AxisController(string id, string name, IEventBus eventBus, ILoggingService logger)
    {
        _axis = new AxisModel { Id = id, Name = name };
        _eventBus = eventBus;
        _logger = logger;
        _simulationTimer = new Timer(SimulateMotion, null, 500, 200);
    }

    private void SimulateMotion(object? _)
    {
        if (_axis.State == AxisState.Moving)
        {
            var diff = _axis.TargetPosition - _axis.Position;
            if (Math.Abs(diff) < 0.1)
            {
                _axis.Position = _axis.TargetPosition;
                _axis.Velocity = 0;
                _axis.State = AxisState.Idle;
                _logger.Info($"{_axis.Name} 到达目标位置 {_axis.TargetPosition:F2}", "Motion");
            }
            else
            {
                var step = Math.Sign(diff) * Math.Min(Math.Abs(diff), _axis.MaxVelocity * 0.2 / 5);
                _axis.Position += step;
                _axis.Velocity = step * 5;
            }
            _axis.LastUpdated = DateTime.Now;
            AxisUpdated?.Invoke(this, _axis);
        }
        else if (_axis.State == AxisState.Jogging)
        {
            _axis.Position += _axis.Velocity * 0.2;
            _axis.LastUpdated = DateTime.Now;
            AxisUpdated?.Invoke(this, _axis);
        }
    }

    public async Task<bool> EnableAsync()
    {
        await Task.Delay(300);
        _axis.IsEnabled = true;
        _axis.State = AxisState.Idle;
        _axis.LastUpdated = DateTime.Now;
        AxisUpdated?.Invoke(this, _axis);
        _logger.Info($"{_axis.Name} 已使能", "Motion");
        return true;
    }

    public async Task<bool> HomeAsync()
    {
        if (!_axis.IsEnabled) return false;
        _axis.State = AxisState.Homing;
        AxisUpdated?.Invoke(this, _axis);
        _logger.Info($"{_axis.Name} 开始回零", "Motion");
        await Task.Delay(2000);
        _axis.Position = 0;
        _axis.IsHomed = true;
        _axis.State = AxisState.Idle;
        _axis.LastUpdated = DateTime.Now;
        AxisUpdated?.Invoke(this, _axis);
        _logger.Info($"{_axis.Name} 回零完成", "Motion");
        return true;
    }

    public bool MoveToPosition(double position)
    {
        if (!_axis.IsEnabled || !_axis.IsHomed) return false;
        _axis.TargetPosition = position;
        _axis.State = AxisState.Moving;
        _axis.LastUpdated = DateTime.Now;
        AxisUpdated?.Invoke(this, _axis);
        _logger.Info($"{_axis.Name} 移动至 {position:F2}", "Motion");
        return true;
    }

    public void StartJog(double velocity)
    {
        if (!_axis.IsEnabled) return;
        _axis.State = AxisState.Jogging;
        _axis.Velocity = velocity;
        AxisUpdated?.Invoke(this, _axis);
    }

    public void StopJog()
    {
        _axis.State = AxisState.Idle;
        _axis.Velocity = 0;
        AxisUpdated?.Invoke(this, _axis);
    }

    public void EStop()
    {
        _axis.State = AxisState.Faulted;
        _axis.Velocity = 0;
        _axis.FaultMessage = "急停触发";
        _axis.LastUpdated = DateTime.Now;
        AxisUpdated?.Invoke(this, _axis);
        _logger.Warning($"{_axis.Name} 急停触发", "Motion");
    }

    public void Reset()
    {
        _axis.State = AxisState.Idle;
        _axis.FaultMessage = null;
        _axis.LastUpdated = DateTime.Now;
        AxisUpdated?.Invoke(this, _axis);
    }

    public void Dispose() => _simulationTimer.Dispose();
}
