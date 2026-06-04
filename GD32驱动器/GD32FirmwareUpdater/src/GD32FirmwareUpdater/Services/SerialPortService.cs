// 串口通讯服务
// 封装 System.IO.Ports.SerialPort，提供异步收发、超时处理能力
// 设计思路：
// 1. 串口数据接收使用事件驱动（DataReceived）
// 2. 发送指令后通过 SemaphoreSlim 等待回复
// 3. 接收缓冲区处理粘包/分包情况

using System.IO.Ports;
using GD32FirmwareUpdater.Protocol;

namespace GD32FirmwareUpdater.Services
{
    /// <summary>
    /// 串口通讯服务
    /// 负责串口的打开/关闭、数据收发、帧缓冲和超时管理
    /// </summary>
    public class SerialPortService : IDisposable
    {
        private SerialPort? _serialPort;

        // 接收缓冲区（用于处理不完整帧）
        private readonly List<byte> _receiveBuffer = new();
        private readonly object _bufferLock = new();

        // 用于异步等待回复帧
        private TaskCompletionSource<ProtocolFrame?>? _responseWaiter;
        private readonly object _waiterLock = new();

        // 日志回调（通知UI显示日志）
        public event Action<string>? OnLog;

        // 串口连接状态变化事件
        public event Action<bool>? OnConnectionChanged;

        // 当前是否已连接
        public bool IsConnected => _serialPort?.IsOpen ?? false;

        /// <summary>
        /// 获取系统中所有可用的COM端口名称
        /// </summary>
        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        /// <summary>
        /// 打开串口
        /// </summary>
        public bool Open(Models.SerialPortConfig config)
        {
            try
            {
                Close();

                _serialPort = new SerialPort
                {
                    PortName = config.PortName,
                    BaudRate = config.BaudRate,
                    DataBits = config.DataBits,
                    StopBits = config.StopBits,
                    Parity = config.Parity,
                    ReadTimeout = config.ReadTimeout,
                    WriteTimeout = config.WriteTimeout,
                    ReadBufferSize = 4096,
                    WriteBufferSize = 4096
                };

                // 注册数据接收事件
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();

                Log($"串口 {config.PortName} 已打开 (波特率:{config.BaudRate}, 数据位:{config.DataBits}, 停止位:{config.StopBits}, 校验:{config.Parity})");
                OnConnectionChanged?.Invoke(true);
                return true;
            }
            catch (Exception ex)
            {
                Log($"打开串口失败: {ex.Message}");
                OnConnectionChanged?.Invoke(false);
                return false;
            }
        }

        /// <summary>
        /// 关闭串口
        /// </summary>
        public void Close()
        {
            if (_serialPort != null)
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.DataReceived -= SerialPort_DataReceived;
                    _serialPort.Close();
                    Log("串口已关闭");
                }
                _serialPort.Dispose();
                _serialPort = null;
                OnConnectionChanged?.Invoke(false);
            }

            lock (_bufferLock)
            {
                _receiveBuffer.Clear();
            }
        }

        /// <summary>
        /// 发送协议帧并等待回复
        /// 流程：
        /// 1. 清空接收缓冲区
        /// 2. 创建等待信号
        /// 3. 发送数据
        /// 4. 等待回复或超时
        /// </summary>
        /// <param name="frameData">待发送的帧字节数组</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <returns>收到的回复帧，超时返回null</returns>
        public async Task<ProtocolFrame?> SendAndWaitResponseAsync(byte[] frameData, int timeoutMs = 3000)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                Log("发送失败: 串口未打开");
                return null;
            }

            // 清空旧的接收缓冲区
            lock (_bufferLock)
            {
                _receiveBuffer.Clear();
            }

            // 创建异步等待信号
            var tcs = new TaskCompletionSource<ProtocolFrame?>();
            lock (_waiterLock)
            {
                _responseWaiter = tcs;
            }

            try
            {
                // 发送数据
                Log($"[TX] {BitConverter.ToString(frameData).Replace("-", " ")}");
                _serialPort.Write(frameData, 0, frameData.Length);

                // 使用超时等待回复
                using var cts = new CancellationTokenSource(timeoutMs);
                cts.Token.Register(() => tcs.TrySetResult(null));

                var response = await tcs.Task;
                return response;
            }
            catch (Exception ex)
            {
                Log($"通讯异常: {ex.Message}");
                return null;
            }
            finally
            {
                lock (_waiterLock)
                {
                    _responseWaiter = null;
                }
            }
        }

        /// <summary>
        /// 串口数据接收事件处理
        /// 将接收到的字节追加到缓冲区，尝试解析完整帧
        /// </summary>
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            try
            {
                int bytesToRead = _serialPort.BytesToRead;
                if (bytesToRead <= 0) return;

                byte[] buffer = new byte[bytesToRead];
                int actualRead = _serialPort.Read(buffer, 0, bytesToRead);

                lock (_bufferLock)
                {
                    _receiveBuffer.AddRange(buffer.AsSpan(0, actualRead).ToArray());
                }

                Log($"[RX] {BitConverter.ToString(buffer, 0, actualRead).Replace("-", " ")}");

                // 尝试从缓冲区解析帧
                TryParseFrame();
            }
            catch (Exception ex)
            {
                Log($"接收数据异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 尝试从接收缓冲区中解析出完整的协议帧
        /// 处理策略：
        /// 1. 在缓冲区中寻找SOF标记
        /// 2. 如果数据足够，尝试解析完整帧
        /// 3. 成功解析后通知等待者
        /// </summary>
        private void TryParseFrame()
        {
            lock (_bufferLock)
            {
                // 寻找帧头SOF（0x01）的位置
                int sofIndex = _receiveBuffer.IndexOf(FrameConstants.SOF);
                if (sofIndex < 0)
                {
                    _receiveBuffer.Clear();
                    return;
                }

                // 丢弃SOF之前的无效数据
                if (sofIndex > 0)
                {
                    _receiveBuffer.RemoveRange(0, sofIndex);
                }

                // 最小帧长检查
                int minFrameLen = FrameConstants.HEADER_SIZE + FrameConstants.CRC_SIZE;
                if (_receiveBuffer.Count < minFrameLen)
                    return;

                // 读取DATALEN（偏移4-5，小端）
                ushort dataLen = (ushort)(_receiveBuffer[4] | (_receiveBuffer[5] << 8));
                int expectedFrameLen = FrameConstants.HEADER_SIZE + dataLen + FrameConstants.CRC_SIZE;

                // 数据不足，等待更多数据
                if (_receiveBuffer.Count < expectedFrameLen)
                    return;

                // 尝试解析帧
                byte[] frameBytes = _receiveBuffer.GetRange(0, expectedFrameLen).ToArray();
                var frame = ProtocolFrame.Deserialize(frameBytes);

                // 消费已解析的数据
                _receiveBuffer.RemoveRange(0, expectedFrameLen);

                if (frame != null)
                {
                    // 通知等待者
                    lock (_waiterLock)
                    {
                        _responseWaiter?.TrySetResult(frame);
                    }
                }
            }
        }

        /// <summary>
        /// 输出日志
        /// </summary>
        private void Log(string message)
        {
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }

        public void Dispose()
        {
            Close();
        }
    }
}
