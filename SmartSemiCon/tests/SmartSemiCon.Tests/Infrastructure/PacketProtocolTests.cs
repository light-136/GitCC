// ============================================================
// 文件：PacketProtocolTests.cs
// 用途：TCP消息封包/拆包协议单元测试
// 设计思路：
//   TCP是流式协议，消息边界需要应用层自行处理。
//   本项目使用 Length-Prefixed 协议（4字节大端序长度头 + 消息体）。
//
//   测试覆盖：
//   1. PacketBuilder — 构建带长度前缀的数据包
//   2. PacketParser — 解析单个完整包
//   3. PacketParser — 处理粘包（一次收到多个包）
//   4. PacketParser — 处理拆包（一个包分多次收到）
//
//   这些测试验证真实的字节操作逻辑，确保工业通讯的可靠性。
// ============================================================

using SmartSemiCon.Infrastructure.Communication.Protocol;

namespace SmartSemiCon.Tests.Infrastructure
{
    /// <summary>
    /// TCP封包/拆包协议测试类 — 验证 PacketBuilder 和 PacketParser 的正确性。
    /// </summary>
    public class PacketProtocolTests
    {
        // =============================================
        // PacketBuilder 测试
        // =============================================

        /// <summary>
        /// 验证 PacketBuilder.Build 输出的包格式：
        /// 前4字节为大端序长度，后面跟消息体。
        /// </summary>
        [Fact]
        public void PacketBuilder_Build_ShouldPrependBigEndianLength()
        {
            // Arrange — 准备5字节的消息体
            var body = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

            // Act — 构建数据包
            var packet = PacketBuilder.Build(body);

            // Assert — 总长度 = 4(长度头) + 5(消息体) = 9
            Assert.Equal(9, packet.Length);

            // 验证大端序长度头（5 = 0x00000005）
            Assert.Equal(0x00, packet[0]); // 最高字节
            Assert.Equal(0x00, packet[1]);
            Assert.Equal(0x00, packet[2]);
            Assert.Equal(0x05, packet[3]); // 最低字节

            // 验证消息体内容不变
            Assert.Equal(0x01, packet[4]);
            Assert.Equal(0x02, packet[5]);
            Assert.Equal(0x03, packet[6]);
            Assert.Equal(0x04, packet[7]);
            Assert.Equal(0x05, packet[8]);
        }

        /// <summary>
        /// 验证 PacketBuilder.Build 对空消息体的处理。
        /// 空消息体应生成4字节长度头（值为0）。
        /// </summary>
        [Fact]
        public void PacketBuilder_Build_EmptyBody_ShouldHaveZeroLength()
        {
            var body = Array.Empty<byte>();

            var packet = PacketBuilder.Build(body);

            // 总长度 = 4(长度头) + 0(空消息体) = 4
            Assert.Equal(4, packet.Length);
            // 长度值为0
            Assert.Equal(0x00, packet[0]);
            Assert.Equal(0x00, packet[1]);
            Assert.Equal(0x00, packet[2]);
            Assert.Equal(0x00, packet[3]);
        }

        /// <summary>
        /// 验证 PacketBuilder.Build 对较大消息体的长度编码。
        /// 测试长度值超过256（需要使用多个字节编码）的情况。
        /// </summary>
        [Fact]
        public void PacketBuilder_Build_LargeBody_ShouldEncodeLengthCorrectly()
        {
            // 300字节的消息体（300 = 0x0000012C）
            var body = new byte[300];

            var packet = PacketBuilder.Build(body);

            Assert.Equal(304, packet.Length); // 4 + 300

            // 验证大端序：300 = 0x00 0x00 0x01 0x2C
            Assert.Equal(0x00, packet[0]);
            Assert.Equal(0x00, packet[1]);
            Assert.Equal(0x01, packet[2]);
            Assert.Equal(0x2C, packet[3]);
        }

        /// <summary>
        /// 使用 Theory 参数化测试：验证不同大小消息体的封包。
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        public void PacketBuilder_Build_VariousSizes_ShouldProduceCorrectLength(int bodySize)
        {
            var body = new byte[bodySize];

            var packet = PacketBuilder.Build(body);

            // 总包长 = 4字节头 + 消息体长度
            Assert.Equal(4 + bodySize, packet.Length);

            // 解析长度头验证
            int parsedLength = (packet[0] << 24) | (packet[1] << 16) | (packet[2] << 8) | packet[3];
            Assert.Equal(bodySize, parsedLength);
        }

        // =============================================
        // PacketParser 解析单个完整包
        // =============================================

        /// <summary>
        /// 验证 PacketParser 能正确解析一个完整的数据包。
        /// 模拟场景：TCP一次恰好收到一个完整包。
        /// </summary>
        [Fact]
        public void PacketParser_Append_SingleCompletePacket_ShouldParseCorrectly()
        {
            var parser = new PacketParser();
            var body = new byte[] { 0xAA, 0xBB, 0xCC };

            // 用 PacketBuilder 构建完整包
            var packet = PacketBuilder.Build(body);

            // Act — 一次性送入完整包
            var messages = parser.Append(packet);

            // Assert — 应解析出1条消息
            Assert.Single(messages);
            Assert.Equal(body, messages[0]);
        }

        /// <summary>
        /// 验证 PacketParser 能正确还原消息体内容。
        /// </summary>
        [Fact]
        public void PacketParser_Append_ShouldReturnExactBodyContent()
        {
            var parser = new PacketParser();
            var body = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello" 的 ASCII

            var packet = PacketBuilder.Build(body);
            var messages = parser.Append(packet);

            Assert.Single(messages);
            // 逐字节验证
            for (int i = 0; i < body.Length; i++)
            {
                Assert.Equal(body[i], messages[0][i]);
            }
        }

        // =============================================
        // PacketParser 处理粘包
        // =============================================

        /// <summary>
        /// 验证 PacketParser 处理粘包：一次收到两个完整包。
        /// TCP经常出现多个消息粘在一起发送的情况。
        /// </summary>
        [Fact]
        public void PacketParser_Append_TwoPacketsStuckTogether_ShouldParseBoth()
        {
            var parser = new PacketParser();
            var body1 = new byte[] { 0x01, 0x02 };
            var body2 = new byte[] { 0x03, 0x04, 0x05 };

            // 构建两个包并拼接在一起（模拟粘包）
            var packet1 = PacketBuilder.Build(body1);
            var packet2 = PacketBuilder.Build(body2);

            var combined = new byte[packet1.Length + packet2.Length];
            Array.Copy(packet1, 0, combined, 0, packet1.Length);
            Array.Copy(packet2, 0, combined, packet1.Length, packet2.Length);

            // Act — 一次送入粘在一起的数据
            var messages = parser.Append(combined);

            // Assert — 应解析出2条消息
            Assert.Equal(2, messages.Count);
            Assert.Equal(body1, messages[0]);
            Assert.Equal(body2, messages[1]);
        }

        /// <summary>
        /// 验证 PacketParser 处理三个包粘在一起的情况。
        /// </summary>
        [Fact]
        public void PacketParser_Append_ThreePacketsStuckTogether_ShouldParseAll()
        {
            var parser = new PacketParser();
            var bodies = new[]
            {
                new byte[] { 0x01 },
                new byte[] { 0x02, 0x03 },
                new byte[] { 0x04, 0x05, 0x06 }
            };

            // 构建并拼接三个包
            var packets = bodies.Select(b => PacketBuilder.Build(b)).ToArray();
            var totalLength = packets.Sum(p => p.Length);
            var combined = new byte[totalLength];
            int offset = 0;
            foreach (var p in packets)
            {
                Array.Copy(p, 0, combined, offset, p.Length);
                offset += p.Length;
            }

            var messages = parser.Append(combined);

            Assert.Equal(3, messages.Count);
            for (int i = 0; i < bodies.Length; i++)
            {
                Assert.Equal(bodies[i], messages[i]);
            }
        }

        // =============================================
        // PacketParser 处理拆包
        // =============================================

        /// <summary>
        /// 验证 PacketParser 处理拆包：一个完整包分两次收到。
        /// 第一次只收到长度头的一部分，第二次收到剩余数据。
        /// </summary>
        [Fact]
        public void PacketParser_Append_SplitPacket_ShouldParseAfterComplete()
        {
            var parser = new PacketParser();
            var body = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };

            var packet = PacketBuilder.Build(body);

            // 第一次只发送前3字节（长度头不完整）
            var part1 = packet[..3];
            var messages1 = parser.Append(part1);
            Assert.Empty(messages1); // 数据不够，解析不出消息

            // 第二次发送剩余数据
            var part2 = packet[3..];
            var messages2 = parser.Append(part2);
            Assert.Single(messages2); // 凑够后解析出完整消息
            Assert.Equal(body, messages2[0]);
        }

        /// <summary>
        /// 验证 PacketParser 处理拆包：长度头完整但消息体不完整。
        /// </summary>
        [Fact]
        public void PacketParser_Append_HeaderCompleteBodyIncomplete_ShouldWait()
        {
            var parser = new PacketParser();
            var body = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

            var packet = PacketBuilder.Build(body);

            // 第一次发送长度头 + 部分消息体（6字节 = 4头 + 2体）
            var part1 = packet[..6];
            var messages1 = parser.Append(part1);
            Assert.Empty(messages1); // 消息体不完整

            // 第二次发送剩余消息体
            var part2 = packet[6..];
            var messages2 = parser.Append(part2);
            Assert.Single(messages2);
            Assert.Equal(body, messages2[0]);
        }

        /// <summary>
        /// 验证 PacketParser 处理拆包 + 粘包的复合场景：
        /// 第一次收到一个完整包 + 下一个包的部分数据，
        /// 第二次收到下一个包的剩余数据。
        /// </summary>
        [Fact]
        public void PacketParser_Append_SplitAndStick_ShouldHandleCorrectly()
        {
            var parser = new PacketParser();
            var body1 = new byte[] { 0x01, 0x02 };
            var body2 = new byte[] { 0x03, 0x04, 0x05 };

            var packet1 = PacketBuilder.Build(body1);
            var packet2 = PacketBuilder.Build(body2);

            // 第一次：完整 packet1 + packet2 的前2字节
            var firstBatch = new byte[packet1.Length + 2];
            Array.Copy(packet1, 0, firstBatch, 0, packet1.Length);
            Array.Copy(packet2, 0, firstBatch, packet1.Length, 2);

            var messages1 = parser.Append(firstBatch);
            Assert.Single(messages1);            // 解析出 packet1
            Assert.Equal(body1, messages1[0]);

            // 第二次：packet2 的剩余部分
            var secondBatch = packet2[2..];
            var messages2 = parser.Append(secondBatch);
            Assert.Single(messages2);            // 解析出 packet2
            Assert.Equal(body2, messages2[0]);
        }

        /// <summary>
        /// 验证 PacketParser.Reset 清空缓冲区后，之前的残余数据不影响后续解析。
        /// </summary>
        [Fact]
        public void PacketParser_Reset_ShouldClearBuffer()
        {
            var parser = new PacketParser();
            var body = new byte[] { 0x01, 0x02, 0x03 };
            var packet = PacketBuilder.Build(body);

            // 送入不完整数据
            parser.Append(packet[..3]);

            // 重置缓冲区
            parser.Reset();

            // 送入新的完整包（不应受之前残余数据影响）
            var newBody = new byte[] { 0xAA };
            var newPacket = PacketBuilder.Build(newBody);
            var messages = parser.Append(newPacket);

            Assert.Single(messages);
            Assert.Equal(newBody, messages[0]);
        }

        /// <summary>
        /// 使用 Theory 测试：通过 PacketBuilder 构建再用 PacketParser 解析，
        /// 验证端到端的数据一致性。
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(500)]
        public void BuildThenParse_RoundTrip_ShouldPreserveData(int bodySize)
        {
            var parser = new PacketParser();

            // 生成测试数据（使用索引值填充，便于验证）
            var body = new byte[bodySize];
            for (int i = 0; i < bodySize; i++)
            {
                body[i] = (byte)(i % 256);
            }

            // 构建 → 解析
            var packet = PacketBuilder.Build(body);
            var messages = parser.Append(packet);

            // 验证端到端数据一致
            Assert.Single(messages);
            Assert.Equal(body.Length, messages[0].Length);
            Assert.Equal(body, messages[0]);
        }
    }
}
