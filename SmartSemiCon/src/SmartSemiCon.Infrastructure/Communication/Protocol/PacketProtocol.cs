// ============================================================
// 文件：PacketProtocol.cs
// 用途：TCP消息封包/拆包协议
// 设计思路：
//   TCP是流式协议，存在粘包和拆包问题。
//   解决方案：在每个消息前加4字节长度头（Length-Prefixed协议）。
//
//   消息格式：[4字节长度][消息体]
//   长度字段使用大端序（Big-Endian），这是工业通讯的惯例。
//
//   PacketBuilder — 将消息体封装为带长度头的完整包
//   PacketParser  — 从TCP字节流中解析出完整的消息
// ============================================================

namespace SmartSemiCon.Infrastructure.Communication.Protocol
{
    /// <summary>
    /// 消息封包器 — 将消息体添加长度头。
    /// </summary>
    public static class PacketBuilder
    {
        /// <summary>
        /// 封包 — 在消息体前添加4字节大端序长度头。
        /// </summary>
        /// <param name="body">消息体</param>
        /// <returns>完整的数据包 [4字节长度 + 消息体]</returns>
        public static byte[] Build(byte[] body)
        {
            var length = body.Length;
            var packet = new byte[4 + length];

            // 大端序写入长度（高字节在前）
            packet[0] = (byte)(length >> 24);
            packet[1] = (byte)(length >> 16);
            packet[2] = (byte)(length >> 8);
            packet[3] = (byte)(length);

            Array.Copy(body, 0, packet, 4, length);
            return packet;
        }
    }

    /// <summary>
    /// 消息拆包器 — 从TCP流中解析完整消息。
    /// 处理粘包（一次收到多个包）和拆包（一个包分多次收到）。
    /// 使用内部缓冲区累积收到的数据，当凑够一个完整消息时输出。
    /// </summary>
    public class PacketParser
    {
        // 内部缓冲区
        private readonly List<byte> _buffer = new();
        private readonly object _lock = new();

        /// <summary>最大消息长度保护（防止恶意大包耗尽内存）</summary>
        public int MaxPacketSize { get; set; } = 1024 * 1024; // 1MB

        /// <summary>
        /// 向解析器追加收到的原始数据。
        /// </summary>
        /// <param name="data">从TCP流读取的原始字节</param>
        /// <returns>解析出的完整消息列表（可能0个、1个或多个）</returns>
        public List<byte[]> Append(byte[] data)
        {
            var messages = new List<byte[]>();

            lock (_lock)
            {
                _buffer.AddRange(data);

                // 循环尝试解析（处理粘包 — 一次收到多个完整消息）
                while (_buffer.Count >= 4) // 至少要有长度头
                {
                    // 读取消息长度（大端序）
                    var length = (_buffer[0] << 24)
                               | (_buffer[1] << 16)
                               | (_buffer[2] << 8)
                               | _buffer[3];

                    // 安全检查
                    if (length <= 0 || length > MaxPacketSize)
                    {
                        _buffer.Clear(); // 协议错误，清空缓冲区
                        break;
                    }

                    // 检查是否已收到完整消息（处理拆包 — 数据还没收完）
                    if (_buffer.Count < 4 + length)
                    {
                        break; // 数据不够，等待更多数据
                    }

                    // 提取完整消息体
                    var message = new byte[length];
                    _buffer.CopyTo(4, message, 0, length);

                    // 从缓冲区移除已解析的部分
                    _buffer.RemoveRange(0, 4 + length);

                    messages.Add(message);
                }
            }

            return messages;
        }

        /// <summary>
        /// 清空缓冲区。
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _buffer.Clear();
            }
        }
    }
}
