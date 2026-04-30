// ============================================================
// 文件：HsmsMessage.cs
// 用途：HSMS 消息帧结构定义（SEMI E37 线格式）
// 标准：SEMI E37 — High-Speed SECS Message Services
// 设计思路：
//   HSMS 协议基于 TCP/IP，每条消息由以下部分组成：
//     [4字节长度前缀(大端)] + [10字节消息头] + [可变长度消息体]
//   消息头包含 SessionId、Stream/Function、SType 等字段。
//   本文件定义 HsmsHeader（消息头）和 HsmsFrame（完整帧），
//   并提供序列化/反序列化方法以及常用控制消息的工厂方法。
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Services.SecsGem
{
    /// <summary>
    /// HSMS 消息头 — 固定 10 字节，承载消息路由和控制信息。
    /// </summary>
    public class HsmsHeader
    {
        /// <summary>会话 ID — 标识 HSMS 会话（设备 ID）。</summary>
        public ushort SessionId { get; set; }

        /// <summary>W-Bit（等待位）— true 表示期望收到回复。</summary>
        public bool WBit { get; set; }

        /// <summary>消息流号（Stream）。</summary>
        public byte Stream { get; set; }

        /// <summary>消息功能号（Function）。</summary>
        public byte Function { get; set; }

        /// <summary>表示类型 — 固定 0x00（SECS-II）。</summary>
        public byte PType { get; set; } = 0x00;

        /// <summary>会话类型 — 数据消息或控制消息。</summary>
        public HsmsMessageType SType { get; set; }

        /// <summary>系统字节 — 事务标识符，匹配请求与响应。</summary>
        public uint SystemBytes { get; set; }

        /// <summary>序列化为 10 字节数组（大端序）。</summary>
        public byte[] ToBytes()
        {
            var b = new byte[10];
            b[0] = (byte)(SessionId >> 8);
            b[1] = (byte)(SessionId & 0xFF);
            b[2] = (byte)((WBit ? 0x80 : 0x00) | (Stream & 0x7F));
            b[3] = Function;
            b[4] = PType;
            b[5] = (byte)SType;
            b[6] = (byte)(SystemBytes >> 24);
            b[7] = (byte)(SystemBytes >> 16);
            b[8] = (byte)(SystemBytes >> 8);
            b[9] = (byte)(SystemBytes & 0xFF);
            return b;
        }

        /// <summary>从 10 字节数组反序列化。</summary>
        public static HsmsHeader FromBytes(byte[] data)
        {
            if (data.Length < 10)
                throw new ArgumentException("HSMS 消息头需要至少 10 字节。");
            return new HsmsHeader
            {
                SessionId = (ushort)((data[0] << 8) | data[1]),
                WBit = (data[2] & 0x80) != 0,
                Stream = (byte)(data[2] & 0x7F),
                Function = data[3],
                PType = data[4],
                SType = (HsmsMessageType)data[5],
                SystemBytes = (uint)((data[6] << 24) | (data[7] << 16) | (data[8] << 8) | data[9])
            };
        }
    }

    /// <summary>
    /// HSMS 完整帧 — 消息头 + 消息体，可序列化为线格式。
    /// 线格式：[4字节长度(大端)] + [10字节头] + [消息体]
    /// </summary>
    public class HsmsFrame
    {
        /// <summary>消息头。</summary>
        public HsmsHeader Header { get; set; } = new();

        /// <summary>消息体（SECS-II 编码数据，控制消息为空）。</summary>
        public byte[] Body { get; set; } = Array.Empty<byte>();

        /// <summary>全局系统字节计数器。</summary>
        private static uint _sysBytesCounter;

        /// <summary>生成唯一系统字节（线程安全）。</summary>
        private static uint NextSysBytes() => Interlocked.Increment(ref _sysBytesCounter);
        /// <summary>序列化为线格式字节数组。</summary>
        public byte[] ToWireFormat()
        {
            var hdr = Header.ToBytes();
            int len = hdr.Length + Body.Length;
            var result = new byte[4 + len];
            // 4 字节长度前缀（大端序）
            result[0] = (byte)(len >> 24);
            result[1] = (byte)(len >> 16);
            result[2] = (byte)(len >> 8);
            result[3] = (byte)(len & 0xFF);
            Array.Copy(hdr, 0, result, 4, hdr.Length);
            if (Body.Length > 0)
                Array.Copy(Body, 0, result, 14, Body.Length);
            return result;
        }

        /// <summary>从线格式字节数组反序列化。</summary>
        public static HsmsFrame FromWireFormat(byte[] data, int offset = 0)
        {
            int msgLen = (data[offset] << 24) | (data[offset + 1] << 16)
                       | (data[offset + 2] << 8) | data[offset + 3];
            var hdrBytes = new byte[10];
            Array.Copy(data, offset + 4, hdrBytes, 0, 10);
            int bodyLen = msgLen - 10;
            var body = new byte[bodyLen > 0 ? bodyLen : 0];
            if (bodyLen > 0)
                Array.Copy(data, offset + 14, body, 0, bodyLen);
            return new HsmsFrame
            {
                Header = HsmsHeader.FromBytes(hdrBytes),
                Body = body
            };
        }

        /// <summary>创建 Select.req — 请求建立 HSMS 会话。</summary>
        public static HsmsFrame CreateSelectReq(ushort sessionId) => new()
        {
            Header = new HsmsHeader { SessionId = sessionId, SType = HsmsMessageType.SelectReq, SystemBytes = NextSysBytes() }
        };

        /// <summary>创建 Select.rsp — 响应 Select 请求。</summary>
        public static HsmsFrame CreateSelectRsp(ushort sessionId, uint sysBytes) => new()
        {
            Header = new HsmsHeader { SessionId = sessionId, SType = HsmsMessageType.SelectRsp, SystemBytes = sysBytes }
        };

        /// <summary>创建 Linktest.req — 心跳检测。</summary>
        public static HsmsFrame CreateLinktestReq() => new()
        {
            Header = new HsmsHeader { SessionId = 0xFFFF, SType = HsmsMessageType.LinktestReq, SystemBytes = NextSysBytes() }
        };

        /// <summary>创建 Linktest.rsp — 心跳响应。</summary>
        public static HsmsFrame CreateLinktestRsp(uint sysBytes) => new()
        {
            Header = new HsmsHeader { SessionId = 0xFFFF, SType = HsmsMessageType.LinktestRsp, SystemBytes = sysBytes }
        };

        /// <summary>创建 Separate.req — 断开会话（单向，无需响应）。</summary>
        public static HsmsFrame CreateSeparateReq(ushort sessionId) => new()
        {
            Header = new HsmsHeader { SessionId = sessionId, SType = HsmsMessageType.SeparateReq, SystemBytes = NextSysBytes() }
        };

        /// <summary>创建数据消息帧 — 封装 SECS-II 消息。</summary>
        public static HsmsFrame CreateDataMessage(
            ushort sessionId, byte stream, byte function,
            bool wBit, byte[]? body = null, uint systemBytes = 0) => new()
        {
            Header = new HsmsHeader
            {
                SessionId = sessionId,
                Stream = stream,
                Function = function,
                WBit = wBit,
                SType = HsmsMessageType.DataMessage,
                SystemBytes = systemBytes == 0 ? NextSysBytes() : systemBytes
            },
            Body = body ?? Array.Empty<byte>()
        };
    }
}