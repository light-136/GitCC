using SmartHMI.Core.Interfaces;
using SmartHMI.Core.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartHMI.Modules.Mes;

public class MesConnector : IMesConnector
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private bool _connected;
    private WorkorderModel? _currentWorkorder;

    public bool IsConnected => _connected;
    public event EventHandler<WorkorderModel>? WorkorderReceived;

    public string BaseUrl { get; set; } = "http://localhost:8080/api/mes";

    public async Task<bool> ConnectAsync()
    {
        try
        {
            // 仿真：直接返回成功，并生成一个模拟工单
            await Task.Delay(300);
            _connected = true;
            _currentWorkorder = GenerateSimulatedWorkorder();
            WorkorderReceived?.Invoke(this, _currentWorkorder);
            return true;
        }
        catch
        {
            _connected = false;
            return false;
        }
    }

    public void Disconnect()
    {
        _connected = false;
        _currentWorkorder = null;
    }

    public async Task<WorkorderModel?> GetCurrentWorkorderAsync()
    {
        if (!_connected) return null;
        await Task.Delay(50); // 仿真网络延迟
        return _currentWorkorder;
    }

    public async Task<bool> ReportProductionAsync(string workorderId, int qty, int ngQty)
    {
        if (!_connected) return false;
        await Task.Delay(100);
        if (_currentWorkorder?.Id == workorderId)
        {
            _currentWorkorder.CompletedQty += qty;
            _currentWorkorder.NgQty += ngQty;
        }
        return true;
    }

    public async Task<bool> ReportAlarmAsync(string alarmCode, string message)
    {
        if (!_connected) return false;
        await Task.Delay(50);
        return true;
    }

    private static WorkorderModel GenerateSimulatedWorkorder() => new()
    {
        Id = $"WO-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
        ProductType = "ProductA",
        RecipeId = "recipe-001",
        PlannedQty = 1000,
        CompletedQty = Random.Shared.Next(0, 200),
        Status = WorkorderStatus.Running,
        StartTime = DateTime.Now.AddHours(-2),
        OperatorId = "operator"
    };
}
