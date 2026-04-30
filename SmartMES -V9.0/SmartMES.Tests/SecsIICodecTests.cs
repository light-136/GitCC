// ============================================================
// 文件：SecsIICodecTests.cs
// 用途：SECS-II 编解码器单元测试
// 测试目标：SmartMES.Services.SecsGem.SecsIICodec
// 设计思路：
//   对 SecsIICodec 的 Encode/Decode 往返编解码进行验证，
//   确保各种数据类型（ASCII、U4、I4、F8、Binary、List）
//   编码后再解码能够还原原始值。
//   同时测试 SML 文本格式化输出的正确性。
// ============================================================

using SmartMES.Core.Models;
using SmartMES.Services.SecsGem;

namespace SmartMES.Tests
{
    /// <summary>
    /// SecsIICodec 编解码器单元测试类。
    /// 覆盖各类型的往返编解码（Encode → Decode）和 SML 文本格式化。
    /// </summary>
    public class SecsIICodecTests
    {
        // ===== 测试1：ASCII 字符串往返编解码 =====

        [Fact]
        public void AsciiRoundTrip_编码解码后值应与原始字符串一致()
        {
            // 准备：创建一个 ASCII 类型的 SecsItem
            var original = SecsItem.CreateAscii("SmartMES");

            // 执行：先编码为二进制，再从二进制解码
            byte[] encoded = SecsIICodec.Encode(original);
            SecsItem decoded = SecsIICodec.Decode(encoded);

            // 验证：解码后的类型和值应与原始一致
            Assert.Equal(SecsItemType.Ascii, decoded.Type);
            Assert.Equal("SmartMES", decoded.Value);
        }

        // ===== 测试2：U4（4字节无符号整数）往返编解码 =====

        [Fact]
        public void U4RoundTrip_编码解码后值应与原始无符号整数一致()
        {
            // 准备：创建一个 U4 类型的 SecsItem，使用较大的值测试
            uint testValue = 3000000000u;
            var original = SecsItem.CreateU4(testValue);

            // 执行：编码 → 解码
            byte[] encoded = SecsIICodec.Encode(original);
            SecsItem decoded = SecsIICodec.Decode(encoded);

            // 验证：类型为 U4，值相等
            Assert.Equal(SecsItemType.U4, decoded.Type);
            Assert.Equal(testValue, Convert.ToUInt32(decoded.Value));
        }

        // ===== 测试3：I4（4字节有符号整数）往返编解码，包含负数 =====

        [Fact]
        public void I4RoundTrip_编码解码后值应与原始有符号整数一致_包含负数()
        {
            // 准备：使用负数测试有符号整数的编解码
            int testValue = -123456;
            var original = SecsItem.CreateI4(testValue);

            // 执行：编码 → 解码
            byte[] encoded = SecsIICodec.Encode(original);
            SecsItem decoded = SecsIICodec.Decode(encoded);

            // 验证：类型为 I4，负数值正确还原
            Assert.Equal(SecsItemType.I4, decoded.Type);
            Assert.Equal(testValue, Convert.ToInt32(decoded.Value));
        }

        // ===== 测试4：F8（8字节双精度浮点）往返编解码 =====

        [Fact]
        public void F8RoundTrip_编码解码后值应与原始双精度浮点数一致()
        {
            // 准备：使用高精度浮点数测试
            double testValue = 3.141592653589793;
            var original = SecsItem.CreateF8(testValue);

            // 执行：编码 → 解码
            byte[] encoded = SecsIICodec.Encode(original);
            SecsItem decoded = SecsIICodec.Decode(encoded);

            // 验证：类型为 F8，浮点值精确匹配
            Assert.Equal(SecsItemType.F8, decoded.Type);
            Assert.Equal(testValue, Convert.ToDouble(decoded.Value));
        }

        // ===== 测试5：Binary（二进制字节数组）往返编解码 =====

        [Fact]
        public void BinaryRoundTrip_编码解码后字节数组应与原始一致()
        {
            // 准备：创建包含各种字节值的数组
            byte[] testData = { 0x00, 0x01, 0xFF, 0xAB, 0xCD };
            var original = SecsItem.CreateBinary(testData);

            // 执行：编码 → 解码
            byte[] encoded = SecsIICodec.Encode(original);
            SecsItem decoded = SecsIICodec.Decode(encoded);

            // 验证：类型为 Binary，字节数组内容完全相同
            Assert.Equal(SecsItemType.Binary, decoded.Type);
            Assert.Equal(testData, (byte[])decoded.Value!);
        }

        // ===== 测试6：嵌套列表（包含混合类型子项）往返编解码 =====

        [Fact]
        public void NestedList_编码解码后嵌套列表结构和值应完整保留()
        {
            // 准备：创建包含 ASCII、U4、I4 子项的列表
            var original = SecsItem.CreateList(
                SecsItem.CreateAscii("MDLN"),
                SecsItem.CreateU4(12345),
                SecsItem.CreateI4(-99)
            );

            // 执行：编码 → 解码
            byte[] encoded = SecsIICodec.Encode(original);
            SecsItem decoded = SecsIICodec.Decode(encoded);

            // 验证：顶层为 List 类型，子项数量为 3
            Assert.Equal(SecsItemType.List, decoded.Type);
            Assert.Equal(3, decoded.Children.Count);

            // 验证第一个子项：ASCII 字符串
            Assert.Equal(SecsItemType.Ascii, decoded.Children[0].Type);
            Assert.Equal("MDLN", decoded.Children[0].Value);

            // 验证第二个子项：U4 无符号整数
            Assert.Equal(SecsItemType.U4, decoded.Children[1].Type);
            Assert.Equal(12345u, Convert.ToUInt32(decoded.Children[1].Value));

            // 验证第三个子项：I4 有符号整数（负数）
            Assert.Equal(SecsItemType.I4, decoded.Children[2].Type);
            Assert.Equal(-99, Convert.ToInt32(decoded.Children[2].Value));
        }

        // ===== 测试7：空列表往返编解码 =====

        [Fact]
        public void EmptyList_编码解码后应为空列表()
        {
            // 准备：创建不含子项的空列表
            var original = SecsItem.CreateList();

            // 执行：编码 → 解码
            byte[] encoded = SecsIICodec.Encode(original);
            SecsItem decoded = SecsIICodec.Decode(encoded);

            // 验证：类型为 List，子项列表为空
            Assert.Equal(SecsItemType.List, decoded.Type);
            Assert.Empty(decoded.Children);
        }

        // ===== 测试8：SML 文本格式化输出验证 =====

        [Fact]
        public void ToSml_FormatsCorrectly_列表及子项应格式化为正确的SML文本()
        {
            // 准备：创建包含 ASCII 和 U4 子项的列表
            var item = SecsItem.CreateList(
                SecsItem.CreateAscii("SmartMES"),
                SecsItem.CreateU4(1)
            );

            // 执行：转换为 SML 文本
            string sml = SecsIICodec.ToSml(item);

            // 验证：SML 输出应包含列表标记和子项数量
            Assert.Contains("<L [2]", sml);

            // 验证：SML 输出应包含 ASCII 值（带引号）
            Assert.Contains("<A \"SmartMES\">", sml);

            // 验证：SML 输出应包含 U4 值
            Assert.Contains("<U4 1>", sml);

            // 验证：SML 输出应包含列表结束标记
            Assert.Contains(">", sml);
        }
    }
}
