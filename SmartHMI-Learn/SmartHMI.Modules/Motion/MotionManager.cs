using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.Modules.Motion;

public class MotionManager : IDisposable
{
    private readonly Dictionary<string, AxisController> _axes = new();
    private readonly IEventBus _eventBus;
    private readonly ILoggingService _logger;

    public IReadOnlyDictionary<string, AxisController> Axes => _axes;

    public MotionManager(IEventBus eventBus, ILoggingService logger)
    {
        _eventBus = eventBus;
        _logger = logger;

        _axes["X"] = new AxisController("axis-x", "X 轴", eventBus, logger);
        _axes["Y"] = new AxisController("axis-y", "Y 轴", eventBus, logger);
        _axes["Z"] = new AxisController("axis-z", "Z 轴", eventBus, logger);
    }

    public async Task EnableAllAsync()
    {
        foreach (var axis in _axes.Values)
            await axis.EnableAsync();
        _logger.Info("所有轴已使能", "Motion");
    }

    public async Task HomeAllAsync()
    {
        foreach (var axis in _axes.Values)
            await axis.HomeAsync();
        _logger.Info("所有轴回零完成", "Motion");
    }

    public void EStopAll()
    {
        foreach (var axis in _axes.Values)
            axis.EStop();
        _logger.Warning("全轴急停触发", "Motion");
    }

    public void ResetAll()
    {
        foreach (var axis in _axes.Values)
            axis.Reset();
    }

    public void Dispose()
    {
        foreach (var axis in _axes.Values)
            axis.Dispose();
    }
}
