// ============================================================
// 文件：PacketProtocol.cs
// 层次：基础设施层 (Infrastructure Layer) — TCP 封包协议
// 职责：
//   实现 Length-Prefix 封包/拆包协议，解决 TCP 粘包和拆包问题。
//   TCP 是流式协议，不保证每次 Receive 恰好对应一个完整消息，
//   因此应用层必须自定义消息边界。Length-Prefix 是最常用的工业协议帧头设计：
//     [ 4 字节 Little-Endian uint32 长度头 | N 字节消息体 ]
//   最大消息体限制（默认10MB）防止内存耗尽攻击。
// 设计思路：
//   PacketBuilder 维护一个内部字节缓冲区，每次 Append 传入新收到的数据，
//   TryGetNextPacket 尝试从缓冲区中取出一个完整的消息包。
//   完整消息包 = 缓冲区长度 >= 4（能读长度头）且 缓冲区长度 >= 4 + 声明的消息长度。
//   粘包：多个包合并为一次 Receive，TryGetNextPacket 循环调用直到返回 null。
//   拆包：一个包被分为多次 Receive，等待下次 Append 后再尝试取包。
// 作者：SmartIndustry Platform Team
// 创建日期：2026-05-25
// ============================================================

using System.Buffers.Binary;

namespace SmartIndustry.Infrastructure.Network.Tcp
{
    /// <summary>
    /// Length-Prefix 封包协议：静态工具类，提供消息封装和解封装方法。
    /// 帧格式：[4字节 uint32 小端序长度头][N字节消息体]
    /// </summary>
    public static class PacketProtocol
    {
        // ----------------------------------------------------------------
        // 协议常量
        // ----------------------------------------------------------------

        /// <summary>长度头大小（字节）：固定4字节，存储消息体的字节长度</summary>
        public const int HeaderSize = 4;

        /// <summary>
        /// 单个消息体最大允许长度（默认 10MB）。
        /// 超过此限制的消息视为非法帧，防止恶意或错误的超大包导致内存耗尽。
        /// </summary>
        public const int MaxPayloadSize = 10 * 1024 * 1024; // 10MB

        // ================================================================
        // 封包：将消息体包装为带长度头的完整帧
        // ================================================================

        /// <summary>
        /// 封包：将消息体字节数组封装为 Length-Prefix 格式的完整帧。
        /// 输出格式：[4字节小端序长度][消息体字节]
        /// </summary>
        /// <param name="payload">消息体（不包含长度头）</param>
        /// <returns>包含长度头的完整帧字节数组</returns>
        /// <exception cref="ArgumentException">消息体超过最大限制时抛出</exception>
        public static byte[] WrapMessage(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length > MaxPayloadSize)
                throw new ArgumentException(
                    $"消息体长度 {payload.Length} 超过最大限制 {MaxPayloadSize} 字节");

            // 分配帧缓冲区：长度头(4字节) + 消息体
            var frame = new byte[HeaderSize + payload.Length];

            // 使用 System.Buffers.Binary.BinaryPrimitives 写入小端序 uint32 长度头
            // 小端序（Little-Endian）：低字节在前，Intel/AMD 处理器原生支持
            BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)payload.Length);

            // 将消息体复制到帧的 [4..] 位置
            Buffer.BlockCopy(payload, 0, frame, HeaderSize, payload.Length);

            return frame;
        }

        /// <summary>
        /// 封包（Span 版本）：零拷贝封装，性能更优（适合高频发送场景）。
        /// </summary>
        public static byte[] WrapMessage(ReadOnlySpan<byte> payload)
        {
            if (payload.Length > MaxPayloadSize)
                throw new ArgumentException(
                    $"消息体长度 {payload.Length} 超过最大限制 {MaxPayloadSize} 字节");

            var frame = new byte[HeaderSize + payload.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)payload.Length);
            payload.CopyTo(frame.AsSpan(HeaderSize));

            return frame;
        }

        // ================================================================
        // 拆包：从完整帧中提取消息体
        // ================================================================

        /// <summary>
        /// 拆包：从 Length-Prefix 格式的完整帧中提取消息体（不含长度头）。
        /// 用于直接解析一个已知完整的帧，不用于流式拆包（流式拆包用 PacketBuilder）。
        /// </summary>
        /// <param name="frame">包含长度头的完整帧</param>
        /// <returns>消息体字节数组（不含长度头）</returns>
        public static byte[] UnwrapMessage(byte[] frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (frame.Length < HeaderSize)
                throw new ArgumentException("帧长度不足以包含4字节长度头");

            var payloadLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(frame);

            if (payloadLength > MaxPayloadSize)
                throw new InvalidDataException(
                    $"帧声明的消息体长度 {payloadLength} 超过最大限制 {MaxPayloadSize}");

            if (frame.Length < HeaderSize + payloadLength)
                throw new ArgumentException(
                    $"帧长度不足：期望 {HeaderSize + payloadLength} 字节，实际 {frame.Length} 字节");

            var payload = new byte[payloadLength];
            Buffer.BlockCopy(frame, HeaderSize, payload, 0, payloadLength);

            return payload;
        }
    }

    /// <summary>
    /// 流式拆包器。
    /// 维护一个内部字节缓冲区（MemoryStream），每次收到新数据后追加，
    /// 循环调用 TryGetNextPacket 直到缓冲区中没有完整消息包为止。
    /// 线程安全说明：PacketBuilder 实例绑定到单个 TCP 连接，不跨线程使用。
    /// </summary>
    public class PacketBuilder
    {
        // ----------------------------------------------------------------
        // 内部缓冲区：使用 List<byte> 存储未处理的字节流
        // 考虑：大量粘包场景下，List<byte> 的 RemoveRange 有内存移动开销。
        //       生产环境可改用 System.IO.Pipelines.Pipe 以实现零拷贝流处理。
        // ----------------------------------------------------------------
        private readonly List<byte> _buffer = new();

        /// <summary>当前缓冲区内未处理的字节数（便于外部监控缓冲区积压情况）</summary>
        public int BufferedBytes => _buffer.Count;

        /// <summary>
        /// 将新收到的字节数据追加到内部缓冲区。
        /// </summary>
        /// <param name="data">本次 Receive 收到的字节数组</param>
        /// <param name="offset">有效数据在 data 中的起始偏移</param>
        /// <param name="count">有效字节数（不一定是 data.Length）</param>
        public void Append(byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || offset + count > data.Length)
                throw new ArgumentOutOfRangeException("offset 或 count 超出 data 范围");

            // 追加有效字节到缓冲区
            _buffer.AddRange(new ArraySegment<byte>(data, offset, count));
        }

        /// <summary>
        /// 便捷重载：追加完整字节数组
        /// </summary>
        public void Append(byte[] data) => Append(data, 0, data.Length);

        /// <summary>
        /// 尝试从缓冲区中取出一个完整的消息包（消息体部分，不含长度头）。
        /// 调用方应在循环中反复调用，直到返回 null（缓冲区内无更多完整包）。
        /// </summary>
        /// <returns>
        /// 完整消息体字节数组（如果有），或 null（缓冲区中数据不足以构成一个完整包）
        /// </returns>
        /// <exception cref="InvalidDataException">帧声明的消息体超过最大限制时抛出（可能是恶意数据）</exception>
        public byte[]? TryGetNextPacket()
        {
            // 检查 1：缓冲区是否有足够字节读取长度头（至少需要4字节）
            if (_buffer.Count < PacketProtocol.HeaderSize)
                return null;

            // 读取长度头（前4字节，小端序 uint32）
            // 注意：只是"窥视"，不从缓冲区移除（因为可能消息体还没到齐）
            var lengthBytes = _buffer.GetRange(0, PacketProtocol.HeaderSize).ToArray();
            var payloadLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);

            // 安全检查：防止恶意/错误帧声明超大消息体
            if (payloadLength > PacketProtocol.MaxPayloadSize)
                throw new InvalidDataException(
                    $"收到非法帧：声明消息体长度 {payloadLength} 超过最大限制 {PacketProtocol.MaxPayloadSize}");

            // 检查 2：缓冲区是否已经积累了足够字节（长度头 + 消息体）
            var requiredLength = PacketProtocol.HeaderSize + payloadLength;
            if (_buffer.Count < requiredLength)
                return null; // 消息体尚未完全到达，等待下一次 Append

            // 提取完整消息体（跳过长度头的4字节）
            var payload = _buffer.GetRange(PacketProtocol.HeaderSize, payloadLength).ToArray();

            // 从缓冲区移除已处理的字节（长度头 + 消息体）
            _buffer.RemoveRange(0, requiredLength);

            return payload;
        }

        /// <summary>
        /// 清空内部缓冲区（连接断开重连时调用，丢弃残余的未完整帧数据）
        /// </summary>
        public void Clear() => _buffer.Clear();
    }
}
