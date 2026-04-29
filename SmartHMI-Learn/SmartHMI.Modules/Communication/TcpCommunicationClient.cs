using System.Net.Sockets;
using SmartHMI.Core.Models;

namespace SmartHMI.Modules.Communication;

public class TcpCommunicationClient : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private readonly string _host;
    private readonly int _port;

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
    public event EventHandler<byte[]>? DataReceived;
    public event EventHandler<ConnectionStatus>? StatusChanged;

    public TcpCommunicationClient(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            SetStatus(ConnectionStatus.Connecting);
            _client = new TcpClient();
            await _client.ConnectAsync(_host, _port);
            _stream = _client.GetStream();
            _cts = new CancellationTokenSource();
            _ = ReceiveLoopAsync(_cts.Token);
            SetStatus(ConnectionStatus.Connected);
            return true;
        }
        catch
        {
            SetStatus(ConnectionStatus.Faulted);
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        _stream?.Close();
        _client?.Close();
        SetStatus(ConnectionStatus.Disconnected);
        await Task.CompletedTask;
    }

    public async Task<bool> SendAsync(byte[] data)
    {
        if (_stream == null || Status != ConnectionStatus.Connected) return false;
        try
        {
            await _stream.WriteAsync(data);
            return true;
        }
        catch { SetStatus(ConnectionStatus.Faulted); return false; }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (!ct.IsCancellationRequested && _stream != null)
        {
            try
            {
                var count = await _stream.ReadAsync(buffer, ct);
                if (count == 0) break;
                var data = buffer[..count];
                DataReceived?.Invoke(this, data);
            }
            catch { break; }
        }
        SetStatus(ConnectionStatus.Disconnected);
    }

    private void SetStatus(ConnectionStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _stream?.Dispose();
        _client?.Dispose();
    }
}
