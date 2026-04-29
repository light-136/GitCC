using SmartHMI.Core.Models;

namespace SmartHMI.Core.Interfaces;

public interface IMesConnector
{
    bool IsConnected { get; }
    Task<bool> ConnectAsync();
    void Disconnect();
    Task<WorkorderModel?> GetCurrentWorkorderAsync();
    Task<bool> ReportProductionAsync(string workorderId, int qty, int ngQty);
    Task<bool> ReportAlarmAsync(string alarmCode, string message);
    event EventHandler<WorkorderModel>? WorkorderReceived;
}
