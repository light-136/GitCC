// ============================================================
// 文件：SecsIICodec.cs
// 用途：SECS-II 二进制编解码器（SEMI E5 标准）
// 标准：SEMI E5 — SECS-II Message Content
// 设计思路：
//   每个数据项 = [格式字节(1B)] + [长度字节(1~3B)] + [数据]
//   格式字节：高6位=格式码，低2位=长度字节数
//   List 的长度值=子项数量，其他类型的长度值=数据字节数
//   所有数值使用大端字节序（Big-Endian）
//
//   编码过程：SecsItem → 递归序列化为二进制
//   解码过程：二进制 → 递归构建 SecsItem 树
//   SML 转换：SecsItem ↔ SML 文本表示（调试和日志用）
// ============================================================

using System.Text;
using SmartMES.Core.Models;

namespace SmartMES.Services.SecsGem
{
    /// <summary>
    /// SECS-II 编解码器 — SecsItem 与二进制字节流的转换，以及 SML 文本格式支持。
    /// </summary>
    public static class SecsIICodec
    {
        // SECS-II 格式码定义（高6位）
        // 这些常量对应 SEMI E5 标准中的格式码
        private const byte FMT_LIST   = 0x00; // List
        private const byte FMT_BINARY = 0x20; // Binary (byte[])
        private const byte FMT_BOOL   = 0x24; // Boolean
        private const byte FMT_ASCII  = 0x40; // ASCII string
        private const byte FMT_I8     = 0x60; // 8-byte signed integer
        private const byte FMT_I1     = 0x64; // 1-byte signed integer
        private const byte FMT_I2     = 0x68; // 2-byte signed integer
        private const byte FMT_I4     = 0x70; // 4-byte signed integer
        private const byte FMT_F8     = 0x80; // 8-byte float (double)
        private const byte FMT_F4     = 0x90; // 4-byte float (float)
        private const byte FMT_U8     = 0xA0; // 8-byte unsigned integer
        private const byte FMT_U1     = 0xA4; // 1-byte unsigned integer
        private const byte FMT_U2     = 0xA8; // 2-byte unsigned integer
        private const byte FMT_U4     = 0xB0; // 4-byte unsigned integer

        // ======================== 编码（SecsItem → 二进制）========================

        /// <summary>将 SecsItem 递归编码为 SECS-II 二进制。</summary>
        public static byte[] Encode(SecsItem item)
        {
            using var ms = new MemoryStream();
            EncodeItem(ms, item);
            return ms.ToArray();
        }

        /// <summary>递归编码单个数据项。</summary>
        private static void EncodeItem(MemoryStream ms, SecsItem item)
        {
            if (item.Type == SecsItemType.List)
            {
                // List：长度值 = 子项数量（不是字节数）
                WriteItemHeader(ms, item.Type, item.Children.Count);
                foreach (var child in item.Children)
                    EncodeItem(ms, child);
            }
            else
            {
                // 叶节点：先序列化值为字节数组，再写 头+数据
                byte[] data = SerializeValue(item);
                WriteItemHeader(ms, item.Type, data.Length);
                ms.Write(data, 0, data.Length);
            }
        }

        /// <summary>
        /// 写入项头：格式字节 + 长度字节。
        /// 格式字节 = (格式码 & 0xFC) | (长度字节数 & 0x03)
        /// 长度字节数取决于长度值大小：1字节(≤255), 2字节(≤65535), 3字节(更大)
        /// </summary>
        private static void WriteItemHeader(MemoryStream ms, SecsItemType type, int length)
        {
            byte formatCode = GetFormatCode(type);

            // 确定需要的长度字节数
            int numLenBytes;
            if (length <= 0xFF) numLenBytes = 1;
            else if (length <= 0xFFFF) numLenBytes = 2;
            else numLenBytes = 3;

            // 格式字节 = 格式码 | 长度字节数
            byte formatByte = (byte)(formatCode | numLenBytes);
            ms.WriteByte(formatByte);

            // 写入长度字节（大端序）
            if (numLenBytes >= 3) ms.WriteByte((byte)(length >> 16));
            if (numLenBytes >= 2) ms.WriteByte((byte)(length >> 8));
            ms.WriteByte((byte)(length & 0xFF));
        }

        /// <summary>将 SecsItemType 映射到 SECS-II 格式码。</summary>
        private static byte GetFormatCode(SecsItemType type) => type switch
        {
            SecsItemType.List => FMT_LIST,
            SecsItemType.Binary => FMT_BINARY,
            SecsItemType.Boolean => FMT_BOOL,
            SecsItemType.Ascii => FMT_ASCII,
            SecsItemType.I8 => FMT_I8,
            SecsItemType.I1 => FMT_I1,
            SecsItemType.I2 => FMT_I2,
            SecsItemType.I4 => FMT_I4,
            SecsItemType.F8 => FMT_F8,
            SecsItemType.F4 => FMT_F4,
            SecsItemType.U8 => FMT_U8,
            SecsItemType.U1 => FMT_U1,
            SecsItemType.U2 => FMT_U2,
            SecsItemType.U4 => FMT_U4,
            _ => throw new ArgumentException($"不支持的 SecsItem 类型：{type}")
        };

        /// <summary>
        /// 将 SecsItem 的值序列化为字节数组（大端序）。
        /// 不同类型有不同的序列化方式。
        /// </summary>
        private static byte[] SerializeValue(SecsItem item)
        {
            switch (item.Type)
            {
                case SecsItemType.Ascii:
                    return Encoding.ASCII.GetBytes(item.Value?.ToString() ?? "");

                case SecsItemType.Binary:
                    return item.Value as byte[] ?? Array.Empty<byte>();

                case SecsItemType.Boolean:
                    return new[] { (byte)(Convert.ToBoolean(item.Value) ? 1 : 0) };

                case SecsItemType.I1:
                    return new[] { unchecked((byte)Convert.ToSByte(item.Value)) };

                case SecsItemType.I2:
                {
                    short v = Convert.ToInt16(item.Value);
                    return new[] { (byte)(v >> 8), (byte)(v & 0xFF) };
                }
                case SecsItemType.I4:
                {
                    int v = Convert.ToInt32(item.Value);
                    return new[]
                    {
                        (byte)(v >> 24), (byte)(v >> 16),
                        (byte)(v >> 8), (byte)(v & 0xFF)
                    };
                }
                case SecsItemType.I8:
                {
                    long v = Convert.ToInt64(item.Value);
                    var b = new byte[8];
                    for (int i = 7; i >= 0; i--) { b[7 - i] = (byte)(v >> (i * 8)); }
                    return b;
                }
                case SecsItemType.U1:
                    return new[] { Convert.ToByte(item.Value) };

                case SecsItemType.U2:
                {
                    ushort v = Convert.ToUInt16(item.Value);
                    return new[] { (byte)(v >> 8), (byte)(v & 0xFF) };
                }
                case SecsItemType.U4:
                {
                    uint v = Convert.ToUInt32(item.Value);
                    return new[]
                    {
                        (byte)(v >> 24), (byte)(v >> 16),
                        (byte)(v >> 8), (byte)(v & 0xFF)
                    };
                }
                case SecsItemType.U8:
                {
                    ulong v = Convert.ToUInt64(item.Value);
                    var b = new byte[8];
                    for (int i = 7; i >= 0; i--) { b[7 - i] = (byte)(v >> (i * 8)); }
                    return b;
                }
                case SecsItemType.F4:
                {
                    float v = Convert.ToSingle(item.Value);
                    var b = BitConverter.GetBytes(v);
                    if (BitConverter.IsLittleEndian) Array.Reverse(b);
                    return b;
                }
                case SecsItemType.F8:
                {
                    double v = Convert.ToDouble(item.Value);
                    var b = BitConverter.GetBytes(v);
                    if (BitConverter.IsLittleEndian) Array.Reverse(b);
                    return b;
                }
                default:
                    return Array.Empty<byte>();
            }
        }

        // ======================== 解码（二进制 → SecsItem）========================

        /// <summary>将 SECS-II 二进制解码为 SecsItem 树。</summary>
        public static SecsItem Decode(byte[] data)
        {
            int offset = 0;
            return DecodeItem(data, ref offset);
        }

        /// <summary>递归解码单个数据项。</summary>
        private static SecsItem DecodeItem(byte[] data, ref int offset)
        {
            if (offset >= data.Length)
                throw new InvalidDataException("SECS-II 数据意外结束");

            // 读取格式字节
            byte formatByte = data[offset++];
            byte formatCode = (byte)(formatByte & 0xFC); // 高6位 = 格式码
            int numLenBytes = formatByte & 0x03;          // 低2位 = 长度字节数

            if (numLenBytes == 0) numLenBytes = 1;

            // 读取长度字节（大端序）
            int length = 0;
            for (int i = 0; i < numLenBytes; i++)
            {
                length = (length << 8) | data[offset++];
            }

            // 根据格式码解析
            SecsItemType itemType = FormatCodeToType(formatCode);

            if (itemType == SecsItemType.List)
            {
                // List：length = 子项数量
                var list = SecsItem.CreateList();
                for (int i = 0; i < length; i++)
                {
                    list.Children.Add(DecodeItem(data, ref offset));
                }
                return list;
            }
            else
            {
                // 叶节点：读取 length 字节数据
                byte[] valueBytes = new byte[length];
                Array.Copy(data, offset, valueBytes, 0, length);
                offset += length;
                return DeserializeValue(itemType, valueBytes);
            }
        }

        /// <summary>将格式码映射回 SecsItemType。</summary>
        private static SecsItemType FormatCodeToType(byte code) => code switch
        {
            FMT_LIST => SecsItemType.List,
            FMT_BINARY => SecsItemType.Binary,
            FMT_BOOL => SecsItemType.Boolean,
            FMT_ASCII => SecsItemType.Ascii,
            FMT_I8 => SecsItemType.I8,
            FMT_I1 => SecsItemType.I1,
            FMT_I2 => SecsItemType.I2,
            FMT_I4 => SecsItemType.I4,
            FMT_F8 => SecsItemType.F8,
            FMT_F4 => SecsItemType.F4,
            FMT_U8 => SecsItemType.U8,
            FMT_U1 => SecsItemType.U1,
            FMT_U2 => SecsItemType.U2,
            FMT_U4 => SecsItemType.U4,
            _ => throw new InvalidDataException($"未知的 SECS-II 格式码：0x{code:X2}")
        };

        /// <summary>
        /// 将字节数组反序列化为 SecsItem（大端序 → 值）。
        /// </summary>
        private static SecsItem DeserializeValue(SecsItemType type, byte[] data)
        {
            switch (type)
            {
                case SecsItemType.Ascii:
                    return SecsItem.CreateAscii(Encoding.ASCII.GetString(data));

                case SecsItemType.Binary:
                    return SecsItem.CreateBinary(data);

                case SecsItemType.Boolean:
                    return SecsItem.CreateBoolean(data.Length > 0 && data[0] != 0);

                case SecsItemType.I1:
                    return SecsItem.CreateI1((sbyte)(data.Length > 0 ? data[0] : 0));

                case SecsItemType.I2:
                    return SecsItem.CreateI2((short)((data[0] << 8) | data[1]));

                case SecsItemType.I4:
                    return SecsItem.CreateI4((data[0] << 24) | (data[1] << 16) |
                                              (data[2] << 8) | data[3]);

                case SecsItemType.I8:
                {
                    long v = 0;
                    for (int i = 0; i < 8; i++) v = (v << 8) | data[i];
                    return SecsItem.CreateI8(v);
                }

                case SecsItemType.U1:
                    return SecsItem.CreateU1(data.Length > 0 ? data[0] : (byte)0);

                case SecsItemType.U2:
                    return SecsItem.CreateU2((ushort)((data[0] << 8) | data[1]));

                case SecsItemType.U4:
                    return SecsItem.CreateU4((uint)((data[0] << 24) | (data[1] << 16) |
                                                    (data[2] << 8) | data[3]));

                case SecsItemType.U8:
                {
                    ulong v = 0;
                    for (int i = 0; i < 8; i++) v = (v << 8) | data[i];
                    return SecsItem.CreateU8(v);
                }

                case SecsItemType.F4:
                {
                    if (BitConverter.IsLittleEndian) Array.Reverse(data, 0, 4);
                    return SecsItem.CreateF4(BitConverter.ToSingle(data, 0));
                }

                case SecsItemType.F8:
                {
                    if (BitConverter.IsLittleEndian) Array.Reverse(data, 0, 8);
                    return SecsItem.CreateF8(BitConverter.ToDouble(data, 0));
                }

                default:
                    return SecsItem.CreateBinary(data);
            }
        }

        // ======================== SML 文本格式 ========================

        /// <summary>
        /// 将 SecsItem 转换为 SML 文本表示（调试和日志用）。
        /// 示例输出：
        ///   &lt;L [2]
        ///     &lt;A "SmartMES"&gt;
        ///     &lt;U4 1&gt;
        ///   &gt;
        /// </summary>
        public static string ToSml(SecsItem item, int indent = 0)
        {
            var sb = new StringBuilder();
            string prefix = new(' ', indent * 2);

            if (item.Type == SecsItemType.List)
            {
                sb.AppendLine($"{prefix}<L [{item.Children.Count}]");
                foreach (var child in item.Children)
                {
                    sb.Append(ToSml(child, indent + 1));
                }
                sb.AppendLine($"{prefix}>");
            }
            else
            {
                string typeName = GetSmlTypeName(item.Type);
                string value = FormatSmlValue(item);
                sb.AppendLine($"{prefix}<{typeName} {value}>");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 将 SecsMessage 转换为 SML 文本（含 Stream/Function 头）。
        /// 示例：
        ///   S1F13 W
        ///   &lt;L [3]
        ///     ...
        ///   &gt;
        /// </summary>
        public static string MessageToSml(SecsMessage msg)
        {
            var sb = new StringBuilder();
            sb.Append($"S{msg.Stream}F{msg.Function}");
            if (msg.WBit) sb.Append(" W");
            sb.AppendLine();
            if (msg.Body != null)
            {
                sb.Append(ToSml(msg.Body));
            }
            return sb.ToString();
        }

        /// <summary>获取类型的SML缩写名。</summary>
        private static string GetSmlTypeName(SecsItemType type) => type switch
        {
            SecsItemType.Ascii => "A",
            SecsItemType.Binary => "B",
            SecsItemType.Boolean => "BOOLEAN",
            SecsItemType.I1 => "I1",
            SecsItemType.I2 => "I2",
            SecsItemType.I4 => "I4",
            SecsItemType.I8 => "I8",
            SecsItemType.U1 => "U1",
            SecsItemType.U2 => "U2",
            SecsItemType.U4 => "U4",
            SecsItemType.U8 => "U8",
            SecsItemType.F4 => "F4",
            SecsItemType.F8 => "F8",
            _ => type.ToString()
        };

        /// <summary>格式化SML值文本。</summary>
        private static string FormatSmlValue(SecsItem item)
        {
            if (item.Value == null) return "";

            return item.Type switch
            {
                SecsItemType.Ascii => $"\"{item.Value}\"",
                SecsItemType.Binary => BitConverter.ToString(item.Value as byte[] ?? Array.Empty<byte>()),
                SecsItemType.Boolean => Convert.ToBoolean(item.Value) ? "TRUE" : "FALSE",
                _ => item.Value.ToString() ?? ""
            };
        }
    }
}
