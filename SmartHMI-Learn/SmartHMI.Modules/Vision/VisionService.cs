using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.Modules.Vision;

public class VisionService : IVisionService
{
    private readonly List<VisionResult> _results = new();
    private readonly Lock _lock = new();
    private bool _running;
    private readonly Random _rng = new();

    public bool IsRunning => _running;

    public event EventHandler<VisionResult>? ResultAvailable;

    public void Start() => _running = true;
    public void Stop() => _running = false;

    public async Task<VisionResult> TriggerAsync(string jobName, string cameraId = "CAM01")
    {
        // 仿真：模拟相机采集延迟 80-200ms
        await Task.Delay(_rng.Next(80, 200));

        var result = SimulateDetection(jobName, cameraId);

        lock (_lock)
        {
            _results.Add(result);
            if (_results.Count > 500) _results.RemoveAt(0);
        }

        ResultAvailable?.Invoke(this, result);
        return result;
    }

    public IReadOnlyList<VisionResult> GetRecentResults(int count = 50)
    {
        lock (_lock) return _results.TakeLast(count).ToList();
    }

    private VisionResult SimulateDetection(string jobName, string cameraId)
    {
        // 仿真：95% OK，4% NG，1% Uncertain
        var roll = _rng.NextDouble();
        var resultType = roll < 0.95 ? VisionResultType.OK
                       : roll < 0.99 ? VisionResultType.NG
                       : VisionResultType.Uncertain;

        return new VisionResult
        {
            CameraId = cameraId,
            JobName = jobName,
            ResultType = resultType,
            Confidence = resultType == VisionResultType.OK ? 0.92 + _rng.NextDouble() * 0.08
                       : resultType == VisionResultType.NG ? 0.80 + _rng.NextDouble() * 0.15
                       : 0.50 + _rng.NextDouble() * 0.20,
            DefectType = resultType == VisionResultType.NG
                ? new[] { "划痕", "缺料", "污点", "尺寸超差" }[_rng.Next(4)]
                : "",
            OffsetX = (_rng.NextDouble() - 0.5) * 0.2,
            OffsetY = (_rng.NextDouble() - 0.5) * 0.2,
            OffsetAngle = (_rng.NextDouble() - 0.5) * 0.5,
            Measurements = new Dictionary<string, double>
            {
                ["Width"] = 50.0 + (_rng.NextDouble() - 0.5) * 0.1,
                ["Height"] = 30.0 + (_rng.NextDouble() - 0.5) * 0.1,
                ["Roundness"] = 0.98 + _rng.NextDouble() * 0.02
            }
        };
    }
}
