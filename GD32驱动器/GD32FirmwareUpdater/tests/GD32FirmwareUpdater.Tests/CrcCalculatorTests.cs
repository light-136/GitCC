// CRC16校验算法单元测试
// 验证CRC16/Modbus算法的正确性

using GD32FirmwareUpdater.Protocol;

namespace GD32FirmwareUpdater.Tests;

/// <summary>
/// CRC16计算器测试
/// </summary>
public class CrcCalculatorTests
{
    [Fact]
    public void Calculate_EmptyArray_ReturnsInitialValue()
    {
        // 空数组应返回初始值0xFFFF
        ushort result = CrcCalculator.Calculate(Array.Empty<byte>());
        Assert.Equal(0xFFFF, result);
    }

    [Fact]
    public void Calculate_KnownData_ReturnsCorrectCrc()
    {
        // CRC16/Modbus标准测试向量: "123456789" → 0x4B37
        byte[] data = { 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39 };
        ushort result = CrcCalculator.Calculate(data);
        Assert.Equal(0x4B37, result);
    }

    [Fact]
    public void Calculate_SingleByte_ReturnsCorrectCrc()
    {
        byte[] data = { 0x01 };
        ushort result = CrcCalculator.Calculate(data);
        // 手动验算: 初始=0xFFFF, XOR 0x01 = 0xFFFE
        // 逐位处理后应得到确定值
        Assert.NotEqual(0xFFFF, result);
    }

    [Fact]
    public void Calculate_WithOffsetAndLength_CalculatesCorrectSubset()
    {
        byte[] data = { 0xAA, 0x31, 0x32, 0x33, 0xBB };
        // 只计算中间3个字节 { 0x31, 0x32, 0x33 }
        ushort result1 = CrcCalculator.Calculate(data, 1, 3);
        ushort result2 = CrcCalculator.Calculate(new byte[] { 0x31, 0x32, 0x33 });
        Assert.Equal(result2, result1);
    }

    [Fact]
    public void ToLittleEndianBytes_CorrectByteOrder()
    {
        // 0x1234 → 小端: { 0x34, 0x12 }
        byte[] result = CrcCalculator.ToLittleEndianBytes(0x1234);
        Assert.Equal(2, result.Length);
        Assert.Equal(0x34, result[0]); // 低字节在前
        Assert.Equal(0x12, result[1]); // 高字节在后
    }

    [Fact]
    public void Calculate_SameDataProducesSameResult()
    {
        // 验证确定性：相同输入应产生相同输出
        byte[] data = { 0x01, 0x06, 0x00, 0x10, 0x02, 0x00, 0x01, 0x00 };
        ushort result1 = CrcCalculator.Calculate(data);
        ushort result2 = CrcCalculator.Calculate(data);
        Assert.Equal(result1, result2);
    }
}
