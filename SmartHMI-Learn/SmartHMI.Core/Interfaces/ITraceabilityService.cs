using SmartHMI.Core.Models;

namespace SmartHMI.Core.Interfaces;

public interface ITraceabilityService
{
    void Record(TraceRecord record);
    IReadOnlyList<TraceRecord> GetBySerial(string serialNumber);
    IReadOnlyList<TraceRecord> GetByWorkorder(string workorderId);
    IReadOnlyList<TraceRecord> GetRecent(int count = 100);
}
