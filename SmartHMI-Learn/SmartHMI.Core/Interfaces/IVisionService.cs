using SmartHMI.Core.Models;

namespace SmartHMI.Core.Interfaces;

public interface IVisionService
{
    bool IsRunning { get; }
    void Start();
    void Stop();
    Task<VisionResult> TriggerAsync(string jobName, string cameraId = "CAM01");
    IReadOnlyList<VisionResult> GetRecentResults(int count = 50);
    event EventHandler<VisionResult>? ResultAvailable;
}
