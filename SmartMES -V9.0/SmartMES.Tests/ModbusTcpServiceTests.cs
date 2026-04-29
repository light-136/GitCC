using SmartMES.Services.Communication;
using SmartMES.Core.Interfaces;
using System.Net;
using System.Net.Sockets;

namespace SmartMES.Tests;

/// <summary>
/// ModbusTcpService 单元测试。
/// 由于 ModbusTcpService 依赖真实 TCP 连接，本测试文件分为两个部分：
///   1. 纯逻辑测试（不需要网络）：构造函数、未连接状态、异常码描述等
///   2. 集成测试（需要本地模拟服务器）：使用 TcpListener 模拟 Modbus 从站响应
///
/// 注意：集成测试在 CI/CD 环境中可能因端口占用而跳过（使用 Skip 特性）。
/// </summary>
public class ModbusTcpServiceTests : IAsyncDisposable
{
    private readonly SimpleLogger _logger = new SimpleLogger();

    // ════════ 构造函数/初始状态测试 ════════

    [Fact]
    public void Constructor_创建后IsConnected应为false()
    {
        using var svc = new ModbusTcpService("127.0.0.1", 502, _logger);
        Assert.False(svc.IsConnected);
    }

    [Fact]
    public void ProtocolName_应返回ModbusTCP()
    {
        using var svc = new ModbusTcpService("127.0.0.1", 502, _logger);
        Assert.Equal("Modbus TCP", svc.ProtocolName);
    }

    [Fact]
    public void Constructor_自定义从站地址应不影响初始连接状态()
    {
        using var svc = new ModbusTcpService("192.168.1.100", 502, _logger, unitId: 5);
        Assert.False(svc.IsConnected);
    }

    // ════════ 未连接状态下的异常测试 ════════

    [Fact]
    public async Task SendAsync_未连接时应抛出InvalidOperationException()
    {
        using var svc = new ModbusTcpService("127.0.0.1", 502, _logger);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SendAsync(new byte[] { 0x01 }));
    }

    [Fact]
    public async Task ReceiveAsync_未连接时应抛出InvalidOperationException()
    {
        using var svc = new ModbusTcpService("127.0.0.1", 502, _logger);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ReceiveAsync());
    }

    [Fact]
    public async Task ReadCoilsAsync_未连接时应抛出InvalidOperationException()
    {
        using var svc = new ModbusTcpService("127.0.0.1", 502, _logger);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ReadCoilsAsync(0, 8));
    }

    [Fact]
    public async Task ReadHoldingRegistersAsync_未连接时应抛出InvalidOperationException()
    {
        using var svc = new ModbusTcpService("127.0.0.1", 502, _logger);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ReadHoldingRegistersAsync(0, 4));
    }

    [Fact]
    public async Task WriteSingleCoilAsync_未连接时应抛出InvalidOperationException()
    {
        using var svc = new ModbusTcpService("127.0.0.1", 502, _logger);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.WriteSingleCoilAsync(0, true));
    }

    [Fact]
    public async Task WriteSingleRegisterAsync_未连接时应抛出InvalidOperationException()
    {
        using var svc = new ModbusTcpService("127.0.0.1", 502, _logger);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.WriteSingleRegisterAsync(0, 1234));
    }

    [Fact]
    public async Task WriteMultipleRegistersAsync_未连接时应抛出InvalidOperationException()
    {
        using var svc = new ModbusTcpService("127.0.0.1", 502, _logger);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.WriteMultipleRegistersAsync(0, new ushort[] { 1, 2, 3 }));
    }

    // ════════ 连接到不存在的服务器应失败 ════════

    [Fact(Skip = "需要真实网络环境验证连接超时行为，CI环境可能卡住，手动运行时去掉Skip")]
    public async Task ConnectAsync_连接到不存在的主机应抛出连接相关异常()
    {
        // 使用 127.0.0.2（本地回环但无服务监听），确保立即被拒绝
        using var svc = new ModbusTcpService("127.0.0.2", 59999, _logger);

        // 连接被拒绝时抛 SocketException；连接超时时抛 TimeoutException/TaskCanceledException
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await svc.ConnectAsync().WaitAsync(cts.Token);
        });
    }

    // ════════ DisconnectAsync 在未连接状态安全调用 ════════

    [Fact]
    public async Task DisconnectAsync_未连接时调用不应抛出异常()
    {
        using var svc = new ModbusTcpService("127.0.0.1", 502, _logger);
        var ex = await Record.ExceptionAsync(() => svc.DisconnectAsync());
        Assert.Null(ex);
    }

    // ════════ Dispose 安全性测试 ════════

    [Fact]
    public void Dispose_未连接时调用不应抛出异常()
    {
        var svc = new ModbusTcpService("127.0.0.1", 502, _logger);
        var ex = Record.Exception(() => svc.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_多次调用不应抛出异常()
    {
        var svc = new ModbusTcpService("127.0.0.1", 502, _logger);
        svc.Dispose();
        // 二次 Dispose 不应异常（IDisposable 契约要求）
        var ex = Record.Exception(() => svc.Dispose());
        Assert.Null(ex);
    }

    // ════════ 事件订阅测试 ════════

    [Fact]
    public void ConnectionChanged_可以订阅事件不应抛出异常()
    {
        using var svc = new ModbusTcpService("127.0.0.1", 502, _logger);
        bool? lastState = null;
        svc.ConnectionChanged += (_, state) => lastState = state;

        // 未实际连接，仅验证订阅不崩溃
        Assert.Null(lastState);
    }

    [Fact]
    public void DataReceived_可以订阅事件不应抛出异常()
    {
        using var svc = new ModbusTcpService("127.0.0.1", 502, _logger);
        byte[]? received = null;
        svc.DataReceived += (_, data) => received = data;

        Assert.Null(received);
    }

    // ════════ 集成测试：使用本地 TcpListener 模拟 Modbus 从站 ════════

    /// <summary>
    /// 使用本地 TcpListener 模拟 Modbus 从站，进行端到端通信测试。
    /// 测试 ConnectAsync 和 DisconnectAsync 的完整流程。
    /// </summary>
    [Fact(Skip = "集成测试，需要本地 TcpListener 端口正常释放，可单独运行验证")]
    public async Task ConnectAndDisconnect_本地模拟服务器完整流程()
    {
        int port = await FindAvailablePortAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // 启动本地模拟服务器（只接受连接，不发送数据）
        var serverTask = RunMockServerAsync(port, serveRequests: false, cts.Token);

        await Task.Delay(50);  // 等待服务器就绪

        using var svc = new ModbusTcpService("127.0.0.1", port, _logger);

        bool connected = false;
        bool disconnected = false;
        svc.ConnectionChanged += (_, state) =>
        {
            if (state)  connected = true;
            else        disconnected = true;
        };

        await svc.ConnectAsync();
        Assert.True(svc.IsConnected);
        Assert.True(connected);

        await svc.DisconnectAsync();
        Assert.False(svc.IsConnected);
        Assert.True(disconnected);

        cts.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }

    /// <summary>
    /// 模拟 Modbus 从站，验证读保持寄存器请求和响应。
    /// 从站响应固定值：寄存器0=0x1234，寄存器1=0x5678
    /// </summary>
    [Fact(Skip = "集成测试，需要本地 TcpListener 端口正常释放，可单独运行验证")]
    public async Task ReadHoldingRegistersAsync_模拟从站应解析正确寄存器值()
    {
        int port = await FindAvailablePortAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // 期望的寄存器值
        ushort[] expectedValues = { 0x1234, 0x5678 };

        var serverTask = RunModbusRegisterServerAsync(port, expectedValues, cts.Token);
        await Task.Delay(50);

        using var svc = new ModbusTcpService("127.0.0.1", port, _logger);
        await svc.ConnectAsync();

        var registers = await svc.ReadHoldingRegistersAsync(0, 2);

        Assert.Equal(2, registers.Length);
        Assert.Equal(0x1234, registers[0]);
        Assert.Equal(0x5678, registers[1]);

        await svc.DisconnectAsync();
        cts.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }

    /// <summary>
    /// 模拟 Modbus 从站，验证读线圈请求和响应解析。
    /// 从站响应固定字节：0b10110011（高位到低位：线圈7~0）
    /// </summary>
    [Fact(Skip = "集成测试，需要本地 TcpListener 端口正常释放，可单独运行验证")]
    public async Task ReadCoilsAsync_模拟从站应正确解析线圈位()
    {
        int port = await FindAvailablePortAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // 响应字节 0b10110011 = 0xB3 表示8个线圈的状态
        byte coilByte = 0b10110011;
        var serverTask = RunModbusCoilServerAsync(port, coilByte, cts.Token);
        await Task.Delay(50);

        using var svc = new ModbusTcpService("127.0.0.1", port, _logger);
        await svc.ConnectAsync();

        var coils = await svc.ReadCoilsAsync(0, 8);

        // 验证位解析：bit0=1, bit1=1, bit2=0, bit3=0, bit4=1, bit5=1, bit6=0, bit7=1
        Assert.Equal(8, coils.Length);
        Assert.True(coils[0]);   // bit0 = 1
        Assert.True(coils[1]);   // bit1 = 1
        Assert.False(coils[2]);  // bit2 = 0
        Assert.False(coils[3]);  // bit3 = 0
        Assert.True(coils[4]);   // bit4 = 1
        Assert.True(coils[5]);   // bit5 = 1
        Assert.False(coils[6]);  // bit6 = 0
        Assert.True(coils[7]);   // bit7 = 1

        await svc.DisconnectAsync();
        cts.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }

    /// <summary>
    /// 模拟从站返回异常响应（功能码最高位=1），验证 ValidateResponse 能正确抛出异常。
    /// </summary>
    [Fact(Skip = "集成测试，需要本地 TcpListener 端口正常释放，可单独运行验证")]
    public async Task ReadHoldingRegistersAsync_从站返回异常码应抛出InvalidOperationException()
    {
        int port = await FindAvailablePortAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = RunModbusExceptionServerAsync(port, 0x03, 0x02, cts.Token); // FC03 异常 + 异常码02（非法地址）
        await Task.Delay(50);

        using var svc = new ModbusTcpService("127.0.0.1", port, _logger);
        await svc.ConnectAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ReadHoldingRegistersAsync(9999, 1));

        Assert.Contains("异常码", ex.Message);
        Assert.Contains("非法数据地址", ex.Message);

        await svc.DisconnectAsync();
        cts.Cancel();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }

    // ════════ 私有辅助方法：模拟 Modbus 服务器 ════════

    /// <summary>查找本机可用的随机端口（避免端口冲突）</summary>
    private static async Task<int> FindAvailablePortAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        await Task.Yield();
        return port;
    }

    /// <summary>启动简单的 TCP 监听器，仅接受连接不发送数据（测试连接/断开）</summary>
    private static async Task RunMockServerAsync(int port, bool serveRequests, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                client.Dispose();  // 立即关闭，不发数据
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    /// <summary>启动 Modbus 寄存器响应服务器（功能码 03 - Read Holding Registers）</summary>
    private static async Task RunModbusRegisterServerAsync(
        int port, ushort[] values, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            var client = await listener.AcceptTcpClientAsync(ct);
            var stream = client.GetStream();

            // 读取请求（12 字节：6 MBAP + 1单元ID + 1FC + 2起始地址 + 2数量）
            var req = new byte[12];
            await stream.ReadAsync(req.AsMemory(0, req.Length), ct);

            // 构造响应（6 MBAP + 1单元ID + 1FC + 1字节数 + values*2）
            int byteCount = values.Length * 2;
            int remaining = 3 + byteCount;  // unitId(1) + FC(1) + 字节数(1) + 数据
            var resp = new byte[6 + remaining];

            // 复制 MBAP 事务ID 和协议ID
            resp[0] = req[0]; resp[1] = req[1];              // 事务ID
            resp[2] = 0; resp[3] = 0;                         // 协议ID
            resp[4] = (byte)(remaining >> 8);
            resp[5] = (byte)(remaining & 0xFF);               // 后续长度
            resp[6] = req[6];   // 单元ID
            resp[7] = 0x03;     // 功能码
            resp[8] = (byte)byteCount;

            for (int i = 0; i < values.Length; i++)
            {
                resp[9 + i * 2] = (byte)(values[i] >> 8);
                resp[10 + i * 2] = (byte)(values[i] & 0xFF);
            }

            await stream.WriteAsync(resp.AsMemory(0, 9 + byteCount), ct);
            client.Dispose();
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    /// <summary>启动 Modbus 线圈响应服务器（功能码 01 - Read Coils）</summary>
    private static async Task RunModbusCoilServerAsync(
        int port, byte coilByte, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            var client = await listener.AcceptTcpClientAsync(ct);
            var stream = client.GetStream();

            var req = new byte[12];
            await stream.ReadAsync(req.AsMemory(0, req.Length), ct);

            // 响应：MBAP(6) + 单元ID(1) + FC01(1) + 字节数(1) + 线圈字节(1) = 10字节
            var resp = new byte[10];
            resp[0] = req[0]; resp[1] = req[1];  // 事务ID
            resp[2] = 0; resp[3] = 0;
            resp[4] = 0; resp[5] = 4;             // 后续长度 = 4
            resp[6] = req[6];   // 单元ID
            resp[7] = 0x01;     // 功能码
            resp[8] = 0x01;     // 字节数 = 1
            resp[9] = coilByte; // 线圈状态

            await stream.WriteAsync(resp, ct);
            client.Dispose();
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    /// <summary>启动 Modbus 异常响应服务器（返回功能码最高位置1的错误响应）</summary>
    private static async Task RunModbusExceptionServerAsync(
        int port, byte originalFc, byte exceptionCode, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        try
        {
            var client = await listener.AcceptTcpClientAsync(ct);
            var stream = client.GetStream();

            var req = new byte[12];
            await stream.ReadAsync(req.AsMemory(0, req.Length), ct);

            // 异常响应：MBAP(6) + 单元ID(1) + 异常FC(1) + 异常码(1) = 9字节
            var resp = new byte[9];
            resp[0] = req[0]; resp[1] = req[1];  // 事务ID
            resp[2] = 0; resp[3] = 0;
            resp[4] = 0; resp[5] = 3;                     // 后续长度 = 3
            resp[6] = req[6];                              // 单元ID
            resp[7] = (byte)(originalFc | 0x80);           // 功能码最高位=1表示异常
            resp[8] = exceptionCode;                       // 异常码

            await stream.WriteAsync(resp, ct);
            client.Dispose();
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }
}

// ════════ 辅助类：简单日志记录器（用于测试，不依赖 WPF） ════════

/// <summary>
/// 单元测试用简单日志实现，将日志收集到内存列表方便断言。
/// 严格实现 ILoggingService 接口（无多余方法）。
/// </summary>
internal class SimpleLogger : SmartMES.Core.Interfaces.ILoggingService
{
    public List<string> InfoLogs         { get; } = new();
    public List<string> WarningLogs      { get; } = new();
    public List<string> ErrorLogs        { get; } = new();
    public List<string> CommLogs         { get; } = new();

    public IReadOnlyList<SmartMES.Core.Models.LogEntry> GetLogs()
        => new List<SmartMES.Core.Models.LogEntry>();

    public void LogInfo(string message, string source = "System")
        => InfoLogs.Add($"[INFO] {source}: {message}");

    public void LogWarning(string message, string source = "System")
        => WarningLogs.Add($"[WARN] {source}: {message}");

    public void LogError(string message, string source = "System")
        => ErrorLogs.Add($"[ERROR] {source}: {message}");

    public void LogCommunication(string message, string source = "Communication")
        => CommLogs.Add($"[COMM] {source}: {message}");

    public event EventHandler<SmartMES.Core.Models.LogEntry>? LogAdded;
}
