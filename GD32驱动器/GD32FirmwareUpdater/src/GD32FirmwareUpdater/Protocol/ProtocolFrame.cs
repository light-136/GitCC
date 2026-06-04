// GD32低压驱动器Boot升级协议 - 协议帧构造与解析
// 负责协议帧的组装和拆解
//
// 帧格式：
// | SOF(1B) | FUNC(1B) | CMD(2B,大端) | DATALEN(2B,小端) | DATA(NB) | CRC(2B,小端) |
// CRC校验范围：从SOF到DATA的所有字节

namespace GD32FirmwareUpdater.Protocol
{
    /// <summary>
    /// 协议帧数据结构
    /// </summary>
    public class ProtocolFrame
    {
        // 帧头（固定0x01）
        public byte Sof { get; set; } = FrameConstants.SOF;

        // 功能码（固定0x06）
        public byte Func { get; set; } = FrameConstants.FUNC;

        // 指令码（大端模式存储）
        public ushort Cmd { get; set; }

        // 有效数据
        public byte[] Data { get; set; } = Array.Empty<byte>();

        // CRC校验值
        public ushort Crc { get; set; }

        /// <summary>
        /// 将协议帧序列化为字节数组（用于发送）
        /// 组帧流程：
        /// 1. 写入SOF + FUNC
        /// 2. 写入CMD（大端：高字节在前）
        /// 3. 写入DATALEN（小端：低字节在前）
        /// 4. 写入DATA
        /// 5. 计算CRC并写入（小端）
        /// </summary>
        public byte[] Serialize()
        {
            int dataLen = Data?.Length ?? 0;
            // 总帧长 = 帧头(6) + 数据(N) + CRC(2)
            int frameLen = FrameConstants.HEADER_SIZE + dataLen + FrameConstants.CRC_SIZE;
            byte[] buffer = new byte[frameLen];

            int offset = 0;

            // SOF
            buffer[offset++] = Sof;

            // FUNC
            buffer[offset++] = Func;

            // CMD - 大端模式（高字节在前）
            buffer[offset++] = (byte)((Cmd >> 8) & 0xFF);
            buffer[offset++] = (byte)(Cmd & 0xFF);

            // DATALEN - 小端模式（低字节在前）
            ushort dataLength = (ushort)dataLen;
            buffer[offset++] = (byte)(dataLength & 0xFF);
            buffer[offset++] = (byte)((dataLength >> 8) & 0xFF);

            // DATA
            if (dataLen > 0)
            {
                Array.Copy(Data!, 0, buffer, offset, dataLen);
                offset += dataLen;
            }

            // CRC - 对SOF到DATA区域计算CRC16，小端写入
            ushort crc = CrcCalculator.Calculate(buffer, 0, offset);
            Crc = crc;
            byte[] crcBytes = CrcCalculator.ToLittleEndianBytes(crc);
            buffer[offset++] = crcBytes[0];
            buffer[offset++] = crcBytes[1];

            return buffer;
        }

        /// <summary>
        /// 从字节数组中反序列化协议帧（用于接收解析）
        /// 解帧流程：
        /// 1. 校验最小长度
        /// 2. 校验SOF和FUNC
        /// 3. 解析CMD（大端）和DATALEN（小端）
        /// 4. 提取DATA
        /// 5. 校验CRC
        /// </summary>
        /// <returns>解析成功返回ProtocolFrame对象，失败返回null</returns>
        public static ProtocolFrame? Deserialize(byte[] buffer, int offset, int length)
        {
            // 最小帧长 = 帧头(6) + CRC(2) = 8字节
            if (length < FrameConstants.HEADER_SIZE + FrameConstants.CRC_SIZE)
                return null;

            int pos = offset;

            // 校验SOF
            byte sof = buffer[pos++];
            if (sof != FrameConstants.SOF)
                return null;

            // 校验FUNC
            byte func = buffer[pos++];
            if (func != FrameConstants.FUNC)
                return null;

            // 解析CMD（大端）
            ushort cmd = (ushort)((buffer[pos] << 8) | buffer[pos + 1]);
            pos += 2;

            // 解析DATALEN（小端）
            ushort dataLen = (ushort)(buffer[pos] | (buffer[pos + 1] << 8));
            pos += 2;

            // 校验总长度是否匹配
            int expectedLen = FrameConstants.HEADER_SIZE + dataLen + FrameConstants.CRC_SIZE;
            if (length < expectedLen)
                return null;

            // 提取DATA
            byte[] data = new byte[dataLen];
            if (dataLen > 0)
            {
                Array.Copy(buffer, pos, data, 0, dataLen);
                pos += dataLen;
            }

            // 提取CRC（小端）
            ushort receivedCrc = (ushort)(buffer[pos] | (buffer[pos + 1] << 8));

            // 计算并校验CRC（从SOF到DATA结束）
            int crcDataLen = FrameConstants.HEADER_SIZE + dataLen;
            ushort calculatedCrc = CrcCalculator.Calculate(buffer, offset, crcDataLen);

            if (receivedCrc != calculatedCrc)
                return null;

            return new ProtocolFrame
            {
                Sof = sof,
                Func = func,
                Cmd = cmd,
                Data = data,
                Crc = receivedCrc
            };
        }

        /// <summary>
        /// 从完整字节数组反序列化
        /// </summary>
        public static ProtocolFrame? Deserialize(byte[] buffer)
        {
            return Deserialize(buffer, 0, buffer.Length);
        }
    }

    /// <summary>
    /// 协议帧构造工厂 - 快速创建各种指令帧
    /// </summary>
    public static class FrameBuilder
    {
        /// <summary>
        /// 构建升级请求帧（CMD=0x0010）
        /// </summary>
        /// <param name="requestType">请求类型：0x01=升级请求, 0x02=查询状态</param>
        public static byte[] BuildUpgradeRequest(byte requestType)
        {
            var frame = new ProtocolFrame
            {
                Cmd = CommandCode.UPGRADE_REQ,
                Data = new byte[] { requestType, 0x00 }  // DATA[1]保留为0
            };
            return frame.Serialize();
        }

        /// <summary>
        /// 构建启动升级帧（CMD=0x0011）
        /// </summary>
        /// <param name="fileSize">固件文件总大小（字节）</param>
        public static byte[] BuildUpgradeStart(uint fileSize)
        {
            var frame = new ProtocolFrame
            {
                Cmd = CommandCode.UPGRADE_START,
                Data = new byte[]
                {
                    (byte)(fileSize & 0xFF),
                    (byte)((fileSize >> 8) & 0xFF),
                    (byte)((fileSize >> 16) & 0xFF),
                    (byte)((fileSize >> 24) & 0xFF)
                }
            };
            return frame.Serialize();
        }

        /// <summary>
        /// 构建发送升级数据帧（CMD=0x0012）
        /// </summary>
        /// <param name="offsetAddr">当前数据在固件文件中的偏移地址</param>
        /// <param name="data">升级数据（不超过128字节）</param>
        public static byte[] BuildUpgradeData(uint offsetAddr, byte[] data)
        {
            // 构建DATA区域：4字节偏移地址 + N字节升级数据
            byte[] frameData = new byte[4 + data.Length];

            // 偏移地址（4字节）
            frameData[0] = (byte)(offsetAddr & 0xFF);
            frameData[1] = (byte)((offsetAddr >> 8) & 0xFF);
            frameData[2] = (byte)((offsetAddr >> 16) & 0xFF);
            frameData[3] = (byte)((offsetAddr >> 24) & 0xFF);

            // 升级数据
            Array.Copy(data, 0, frameData, 4, data.Length);

            var frame = new ProtocolFrame
            {
                Cmd = CommandCode.UPGRADE_DATA,
                Data = frameData
            };
            return frame.Serialize();
        }

        /// <summary>
        /// 构建完成升级帧（CMD=0x0013）
        /// </summary>
        public static byte[] BuildJumpApp()
        {
            var frame = new ProtocolFrame
            {
                Cmd = CommandCode.JUMP_APP,
                Data = new byte[] { 0x01 }  // 0x01=升级完成请求
            };
            return frame.Serialize();
        }

        /// <summary>
        /// 构建版本号查询帧（CMD=0x0014）
        /// </summary>
        public static byte[] BuildVersionRequest()
        {
            var frame = new ProtocolFrame
            {
                Cmd = CommandCode.VER_REQ,
                Data = new byte[] { 0x01 }  // 0x01=请求版本号
            };
            return frame.Serialize();
        }
    }
}
