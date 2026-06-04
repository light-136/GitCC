// 协议帧构造与解析单元测试
// 验证帧的序列化/反序列化、字节序和CRC校验

using GD32FirmwareUpdater.Protocol;

namespace GD32FirmwareUpdater.Tests;

/// <summary>
/// 协议帧测试
/// </summary>
public class ProtocolFrameTests
{
    [Fact]
    public void Serialize_UpgradeRequestFrame_HasCorrectStructure()
    {
        // 构建升级请求帧
        var frame = new ProtocolFrame
        {
            Cmd = CommandCode.UPGRADE_REQ,
            Data = new byte[] { 0x01, 0x00 }
        };

        byte[] result = frame.Serialize();

        // 验证帧头
        Assert.Equal(FrameConstants.SOF, result[0]);   // SOF = 0x01
        Assert.Equal(FrameConstants.FUNC, result[1]);   // FUNC = 0x06

        // 验证CMD（大端模式）：0x0010 → { 0x00, 0x10 }
        Assert.Equal(0x00, result[2]);
        Assert.Equal(0x10, result[3]);

        // 验证DATALEN（小端模式）：2 → { 0x02, 0x00 }
        Assert.Equal(0x02, result[4]);
        Assert.Equal(0x00, result[5]);

        // 验证DATA
        Assert.Equal(0x01, result[6]);
        Assert.Equal(0x00, result[7]);

        // 总长度 = 6(帧头) + 2(数据) + 2(CRC) = 10
        Assert.Equal(10, result.Length);
    }

    [Fact]
    public void Serialize_Deserialize_Roundtrip_Succeeds()
    {
        // 构造一个帧，序列化再反序列化，验证数据一致性
        var original = new ProtocolFrame
        {
            Cmd = CommandCode.UPGRADE_DATA,
            Data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0xAA, 0xBB, 0xCC }
        };

        byte[] serialized = original.Serialize();
        var deserialized = ProtocolFrame.Deserialize(serialized);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Cmd, deserialized!.Cmd);
        Assert.Equal(original.Data, deserialized.Data);
    }

    [Fact]
    public void Deserialize_InvalidSof_ReturnsNull()
    {
        // SOF不正确，解析应返回null
        byte[] badFrame = { 0xFF, 0x06, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00 };
        var result = ProtocolFrame.Deserialize(badFrame);
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_InvalidCrc_ReturnsNull()
    {
        // 构建一个正常帧然后篡改CRC
        var frame = new ProtocolFrame
        {
            Cmd = CommandCode.VER_REQ,
            Data = new byte[] { 0x01 }
        };
        byte[] serialized = frame.Serialize();

        // 篡改最后一个CRC字节
        serialized[^1] ^= 0xFF;

        var result = ProtocolFrame.Deserialize(serialized);
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_TooShort_ReturnsNull()
    {
        // 数据不足最小帧长（8字节）
        byte[] tooShort = { 0x01, 0x06, 0x00 };
        var result = ProtocolFrame.Deserialize(tooShort);
        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_DataLenMismatch_ReturnsNull()
    {
        // DATALEN声明的数据长度比实际缓冲区大
        byte[] frame = { 0x01, 0x06, 0x00, 0x10, 0xFF, 0x00, 0x01, 0x00 };
        var result = ProtocolFrame.Deserialize(frame);
        Assert.Null(result);
    }

    [Fact]
    public void FrameBuilder_BuildUpgradeRequest_CorrectFormat()
    {
        byte[] frame = FrameBuilder.BuildUpgradeRequest(UpgradeRequestType.REQUEST_UPGRADE);

        // 解析回来验证
        var parsed = ProtocolFrame.Deserialize(frame);
        Assert.NotNull(parsed);
        Assert.Equal(CommandCode.UPGRADE_REQ, parsed!.Cmd);
        Assert.Equal(2, parsed.Data.Length);
        Assert.Equal(0x01, parsed.Data[0]);
        Assert.Equal(0x00, parsed.Data[1]);
    }

    [Fact]
    public void FrameBuilder_BuildUpgradeStart_EncodesFileSizeCorrectly()
    {
        uint fileSize = 0x00012345; // 74565字节
        byte[] frame = FrameBuilder.BuildUpgradeStart(fileSize);

        var parsed = ProtocolFrame.Deserialize(frame);
        Assert.NotNull(parsed);
        Assert.Equal(CommandCode.UPGRADE_START, parsed!.Cmd);
        Assert.Equal(4, parsed.Data.Length);

        // 文件大小是小端编码
        uint decodedSize = (uint)(parsed.Data[0] | (parsed.Data[1] << 8) | (parsed.Data[2] << 16) | (parsed.Data[3] << 24));
        Assert.Equal(fileSize, decodedSize);
    }

    [Fact]
    public void FrameBuilder_BuildUpgradeData_HasOffsetAndPayload()
    {
        uint offset = 256;
        byte[] payload = { 0xDE, 0xAD, 0xBE, 0xEF };

        byte[] frame = FrameBuilder.BuildUpgradeData(offset, payload);
        var parsed = ProtocolFrame.Deserialize(frame);

        Assert.NotNull(parsed);
        Assert.Equal(CommandCode.UPGRADE_DATA, parsed!.Cmd);
        // DATA = 4字节偏移 + 4字节负载 = 8字节
        Assert.Equal(8, parsed.Data.Length);

        // 验证偏移地址
        uint decodedOffset = (uint)(parsed.Data[0] | (parsed.Data[1] << 8) | (parsed.Data[2] << 16) | (parsed.Data[3] << 24));
        Assert.Equal(offset, decodedOffset);

        // 验证负载数据
        Assert.Equal(payload, parsed.Data[4..]);
    }

    [Fact]
    public void FrameBuilder_BuildJumpApp_CorrectFormat()
    {
        byte[] frame = FrameBuilder.BuildJumpApp();
        var parsed = ProtocolFrame.Deserialize(frame);

        Assert.NotNull(parsed);
        Assert.Equal(CommandCode.JUMP_APP, parsed!.Cmd);
        Assert.Single(parsed.Data);
        Assert.Equal(0x01, parsed.Data[0]);
    }

    [Fact]
    public void FrameBuilder_BuildVersionRequest_CorrectFormat()
    {
        byte[] frame = FrameBuilder.BuildVersionRequest();
        var parsed = ProtocolFrame.Deserialize(frame);

        Assert.NotNull(parsed);
        Assert.Equal(CommandCode.VER_REQ, parsed!.Cmd);
        Assert.Single(parsed.Data);
        Assert.Equal(0x01, parsed.Data[0]);
    }

    [Fact]
    public void Cmd_BigEndian_EncodingVerification()
    {
        // 验证CMD=0x0012的大端编码
        var frame = new ProtocolFrame
        {
            Cmd = 0x0012,
            Data = Array.Empty<byte>()
        };

        byte[] serialized = frame.Serialize();
        // CMD在offset 2-3，大端：高字节在前
        Assert.Equal(0x00, serialized[2]);
        Assert.Equal(0x12, serialized[3]);
    }
}
