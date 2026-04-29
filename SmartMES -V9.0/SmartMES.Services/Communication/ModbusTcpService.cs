using SmartMES.Core.Interfaces;
using System.Net.Sockets;
using System.Text;

namespace SmartMES.Services.Communication
{
    /// <summary>
    /// Modbus TCP 通信服务实现。
    /// 实现标准 Modbus TCP 协议（基于 TCP 套接字），支持：
    ///   - 功能码 01：读线圈（Read Coils）
    ///   - 功能码 03：读保持寄存器（Read Holding Registers）
    ///   - 功能码 05：写单个线圈（Write Single Coil）
    ///   - 功能码 06：写单个寄存器（Write Single Register）
    ///   - 功能码 0F：写多个线圈（Write Multiple Coils）
    ///   - 功能码 10：写多个寄存器（Write Multiple Registers）
    /// 内置心跳检测和自动重连机制，适合工业现场长连接场景。
    /// </summary>
    public class ModbusTcpService : ICommunicationService, IDisposable
    {
        // ──────── 私有字段 ────────
        private readonly string _host;                  // Modbus 设备 IP 地址
        private readonly int _port;                     // Modbus TCP 端口，标准为 502
        private readonly ILoggingService _logger;       // 日志服务
        private TcpClient? _client;                     // TCP 客户端
        private NetworkStream? _stream;                 // 网络数据流
        private readonly object _sendLock = new();     // 发送锁，防止并发写流
        private ushort _transactionId = 0;             // Modbus 事务标识符（每次请求自增）
        private readonly byte _unitId;                  // 从站地址（Slave ID）

        // 心跳与重连
        private Timer? _heartbeatTimer;                 // 心跳定时器
        private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(10);
        private bool _isReconnecting;                   // 防止重连重入

        // ──────── 公开属性 ────────
        public bool IsConnected => _client?.Connected == true && _stream != null;
        public string ProtocolName => "Modbus TCP";

        // ──────── 事件 ────────
        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler<bool>? ConnectionChanged;

        /// <summary>
        /// 创建 Modbus TCP 服务实例。
        /// </summary>
        /// <param name="host">Modbus 设备 IP 或主机名</param>
        /// <param name="port">Modbus TCP 端口（默认 502）</param>
        /// <param name="logger">日志服务</param>
        /// <param name="unitId">从站地址（默认 1）</param>
        public ModbusTcpService(string host, int port, ILoggingService logger, byte unitId = 1)
        {
            _host   = host;
            _port   = port;
            _logger = logger;
            _unitId = unitId;
        }

        // ════════ ICommunicationService 实现 ════════

        /// <summary>
        /// 建立 Modbus TCP 连接，并启动心跳检测定时器。
        /// </summary>
        public async Task ConnectAsync()
        {
            if (IsConnected) return;

            _client = new TcpClient();
            _client.ReceiveTimeout = 5000;  // 5秒接收超时
            _client.SendTimeout    = 5000;  // 5秒发送超时

            await _client.ConnectAsync(_host, _port);
            _stream = _client.GetStream();

            // 启动心跳：每 10 秒读一次寄存器确认连接存活
            _heartbeatTimer = new Timer(HeartbeatCallback, null,
                _heartbeatInterval, _heartbeatInterval);

            _logger.LogInfo($"Modbus TCP 已连接 {_host}:{_port} (从站:{_unitId})", "ModbusTcp");
            ConnectionChanged?.Invoke(this, true);
        }

        /// <summary>
        /// 断开连接并释放相关资源。
        /// </summary>
        public async Task DisconnectAsync()
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;

            _stream?.Close();
            _client?.Close();
            _stream = null;
            _client = null;

            _logger.LogInfo("Modbus TCP 已断开", "ModbusTcp");
            ConnectionChanged?.Invoke(this, false);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 发送原始字节数据（底层接口，一般使用上层功能码方法）。
        /// </summary>
        public async Task SendAsync(byte[] data)
        {
            EnsureConnected();
            await _stream!.WriteAsync(data);
        }

        /// <summary>
        /// 接收原始字节数据（底层接口）。
        /// </summary>
        public async Task<byte[]> ReceiveAsync()
        {
            EnsureConnected();
            var buffer = new byte[256];
            int count = await _stream!.ReadAsync(buffer.AsMemory(0, buffer.Length));
            var result = buffer[..count];
            DataReceived?.Invoke(this, result);
            return result;
        }

        // ════════ Modbus 功能码方法 ════════

        /// <summary>
        /// 读取线圈状态（功能码 01 - Read Coils）。
        /// </summary>
        /// <param name="startAddress">起始地址（0-based）</param>
        /// <param name="count">读取线圈数量</param>
        /// <returns>线圈状态数组（true=ON，false=OFF）</returns>
        public async Task<bool[]> ReadCoilsAsync(ushort startAddress, ushort count)
        {
            var request = BuildRequest(0x01, startAddress, count);
            var response = await SendAndReceiveAsync(request);
            ValidateResponse(response, 0x01);

            int byteCount = response[8];
            var coils = new bool[count];
            for (int i = 0; i < count; i++)
            {
                int byteIdx = 9 + i / 8;
                int bitIdx  = i % 8;
                coils[i] = (response[byteIdx] & (1 << bitIdx)) != 0;
            }
            return coils;
        }

        /// <summary>
        /// 读取保持寄存器（功能码 03 - Read Holding Registers）。
        /// </summary>
        /// <param name="startAddress">起始地址（0-based）</param>
        /// <param name="count">读取寄存器数量</param>
        /// <returns>寄存器值数组（每个寄存器 16 位 unsigned short）</returns>
        public async Task<ushort[]> ReadHoldingRegistersAsync(ushort startAddress, ushort count)
        {
            var request = BuildRequest(0x03, startAddress, count);
            var response = await SendAndReceiveAsync(request);
            ValidateResponse(response, 0x03);

            int byteCount = response[8];
            var registers = new ushort[count];
            for (int i = 0; i < count; i++)
            {
                // Modbus 大端字节序：高字节在前
                registers[i] = (ushort)((response[9 + i * 2] << 8) | response[10 + i * 2]);
            }
            return registers;
        }

        /// <summary>
        /// 写单个线圈（功能码 05 - Write Single Coil）。
        /// </summary>
        /// <param name="address">线圈地址（0-based）</param>
        /// <param name="value">true=ON（0xFF00），false=OFF（0x0000）</param>
        public async Task WriteSingleCoilAsync(ushort address, bool value)
        {
            ushort coilValue = value ? (ushort)0xFF00 : (ushort)0x0000;
            var request = BuildRequest(0x05, address, coilValue);
            var response = await SendAndReceiveAsync(request);
            ValidateResponse(response, 0x05);
            _logger.LogInfo($"[Modbus] 写线圈 {address} = {(value ? "ON" : "OFF")}", "ModbusTcp");
        }

        /// <summary>
        /// 写单个保持寄存器（功能码 06 - Write Single Register）。
        /// </summary>
        /// <param name="address">寄存器地址（0-based）</param>
        /// <param name="value">写入值（16位）</param>
        public async Task WriteSingleRegisterAsync(ushort address, ushort value)
        {
            var request = BuildRequest(0x06, address, value);
            var response = await SendAndReceiveAsync(request);
            ValidateResponse(response, 0x06);
            _logger.LogInfo($"[Modbus] 写寄存器 {address} = {value}", "ModbusTcp");
        }

        /// <summary>
        /// 写多个保持寄存器（功能码 10 - Write Multiple Registers）。
        /// </summary>
        /// <param name="startAddress">起始寄存器地址（0-based）</param>
        /// <param name="values">要写入的寄存器值数组</param>
        public async Task WriteMultipleRegistersAsync(ushort startAddress, ushort[] values)
        {
            int count = values.Length;
            // MBAP头(6字节) + 功能码(1字节) + 起始地址(2字节) + 数量(2字节) + 字节数(1字节) + 数据
            int dataLen = 7 + count * 2;
            var pdu = new byte[6 + dataLen];

            // MBAP 头
            ushort tid = NextTransactionId();
            pdu[0] = (byte)(tid >> 8);   pdu[1] = (byte)(tid & 0xFF);  // 事务标识符
            pdu[2] = 0; pdu[3] = 0;                                       // 协议标识符（Modbus=0）
            pdu[4] = (byte)(dataLen >> 8); pdu[5] = (byte)(dataLen & 0xFF); // 后续长度
            pdu[6] = _unitId;                                              // 从站地址
            pdu[7] = 0x10;                                                 // 功能码 16（0x10）
            pdu[8] = (byte)(startAddress >> 8); pdu[9] = (byte)(startAddress & 0xFF);
            pdu[10] = (byte)(count >> 8); pdu[11] = (byte)(count & 0xFF);
            pdu[12] = (byte)(count * 2);                                   // 字节数

            for (int i = 0; i < count; i++)
            {
                pdu[13 + i * 2] = (byte)(values[i] >> 8);
                pdu[14 + i * 2] = (byte)(values[i] & 0xFF);
            }

            var response = await SendAndReceiveAsync(pdu);
            ValidateResponse(response, 0x10);
            _logger.LogInfo($"[Modbus] 批量写寄存器 {startAddress}~{startAddress + count - 1}", "ModbusTcp");
        }

        // ════════ 私有辅助方法 ════════

        /// <summary>
        /// 构建标准 Modbus TCP 请求报文（适用于功能码 01/03/05/06）。
        /// 报文格式：事务ID(2) + 协议ID(2) + 长度(2) + 从站地址(1) + 功能码(1) + 起始地址(2) + 数量/值(2)
        /// </summary>
        private byte[] BuildRequest(byte functionCode, ushort address, ushort countOrValue)
        {
            ushort tid = NextTransactionId();
            return new byte[]
            {
                (byte)(tid >> 8),         (byte)(tid & 0xFF),         // 事务标识符
                0x00,                      0x00,                       // 协议标识符（Modbus=0）
                0x00,                      0x06,                       // 后续数据长度 = 6 字节
                _unitId,                                               // 从站地址
                functionCode,                                          // 功能码
                (byte)(address >> 8),     (byte)(address & 0xFF),     // 起始地址（大端）
                (byte)(countOrValue >> 8),(byte)(countOrValue & 0xFF) // 数量或写入值（大端）
            };
        }

        /// <summary>
        /// 发送请求并同步接收响应报文，带锁保证并发安全。
        /// </summary>
        private async Task<byte[]> SendAndReceiveAsync(byte[] request)
        {
            EnsureConnected();

            byte[] response;
            lock (_sendLock)
            {
                _stream!.WriteAsync(request).AsTask().Wait();
                // 先读6字节 MBAP 头，确认后续数据长度
                var header = new byte[6];
                _stream.ReadAsync(header.AsMemory(0, 6)).AsTask().Wait();
                int remaining = (header[4] << 8) | header[5];
                var body = new byte[remaining];
                _stream.ReadAsync(body.AsMemory(0, remaining)).AsTask().Wait();
                response = header.Concat(body).ToArray();
            }

            DataReceived?.Invoke(this, response);
            return await Task.FromResult(response);
        }

        /// <summary>
        /// 验证 Modbus 响应报文：检查功能码和异常码。
        /// </summary>
        private static void ValidateResponse(byte[] response, byte expectedFunctionCode)
        {
            if (response.Length < 8)
                throw new InvalidOperationException("Modbus 响应报文长度不足");

            byte returnedCode = response[7];
            if (returnedCode == (expectedFunctionCode | 0x80))
            {
                // 异常响应：功能码最高位置1，第9字节为异常码
                byte exceptionCode = response[8];
                throw new InvalidOperationException(
                    $"Modbus 设备返回异常码：0x{exceptionCode:X2} ({GetExceptionDesc(exceptionCode)})");
            }

            if (returnedCode != expectedFunctionCode)
                throw new InvalidOperationException(
                    $"Modbus 功能码不匹配：期望 0x{expectedFunctionCode:X2}，实际 0x{returnedCode:X2}");
        }

        /// <summary>获取 Modbus 异常码的中文说明</summary>
        private static string GetExceptionDesc(byte code) => code switch
        {
            0x01 => "非法功能码",
            0x02 => "非法数据地址",
            0x03 => "非法数据值",
            0x04 => "从站设备故障",
            0x05 => "确认（需等待）",
            0x06 => "从站设备忙",
            0x0A => "网关路径不可用",
            0x0B => "网关目标设备未响应",
            _    => "未知异常码"
        };

        /// <summary>获取下一个事务标识符（循环使用 0~65535）</summary>
        private ushort NextTransactionId()
        {
            unchecked { return _transactionId++; }
        }

        /// <summary>确保连接处于就绪状态，否则抛出异常</summary>
        private void EnsureConnected()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Modbus TCP 未连接，请先调用 ConnectAsync()");
        }

        /// <summary>
        /// 心跳回调：定期读取寄存器 0 验证连接是否存活，断线时触发自动重连。
        /// </summary>
        private async void HeartbeatCallback(object? state)
        {
            if (_isReconnecting) return;

            try
            {
                // 读寄存器 0 作为心跳探测
                await ReadHoldingRegistersAsync(0, 1);
            }
            catch
            {
                // 心跳失败，尝试重连
                _isReconnecting = true;
                _logger.LogWarning("Modbus TCP 心跳超时，正在重连...", "ModbusTcp");

                try
                {
                    _stream?.Close();
                    _client?.Close();
                    _stream = null;
                    _client = null;

                    await ConnectAsync();
                    _logger.LogInfo("Modbus TCP 重连成功", "ModbusTcp");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Modbus TCP 重连失败：{ex.Message}", "ModbusTcp");
                    ConnectionChanged?.Invoke(this, false);
                }
                finally
                {
                    _isReconnecting = false;
                }
            }
        }

        /// <summary>释放所有托管资源</summary>
        public void Dispose()
        {
            _heartbeatTimer?.Dispose();
            _stream?.Dispose();
            _client?.Dispose();
        }
    }
}
