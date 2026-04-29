using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;

namespace SmartHMI.Modules.Traceability;

public class TraceabilityService : ITraceabilityService
{
    private readonly List<TraceRecord> _records = new();
    private readonly Lock _lock = new();

    public void Record(TraceRecord record)
    {
        lock (_lock)
        {
            _records.Add(record);
            if (_records.Count > 10000) _records.RemoveAt(0);
        }
    }

    public IReadOnlyList<TraceRecord> GetBySerial(string serialNumber)
    { lock (_lock) return _records.Where(r => r.SerialNumber == serialNumber).ToList(); }

    public IReadOnlyList<TraceRecord> GetByWorkorder(string workorderId)
    { lock (_lock) return _records.Where(r => r.WorkorderId == workorderId).ToList(); }

    public IReadOnlyList<TraceRecord> GetRecent(int count = 100)
    { lock (_lock) return _records.TakeLast(count).ToList(); }
}
