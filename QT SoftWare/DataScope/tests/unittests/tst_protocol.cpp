/**
 * @file tst_protocol.cpp
 * @brief 协议引擎单测（P10：ProtocolEngine）—— Qt Test 框架
 *
 * 严格对照《03-通信协议帧格式契约.md》第五节"测试强制项"，逐项覆盖：
 *   1. 组帧 → 解析 往返一致（多种 func/cmd/载荷，含空载荷、最大载荷）；
 *   2. 半包：一帧拆两段 feed，仍能完整解析；
 *   3. 粘包：一个 feed 塞两帧，能逐帧取出；
 *   4. CRC 篡改 → 不产出错误帧（被丢弃/跳过）；
 *   5. SOF 错位：帧头前混入垃圾字节能重同步；
 *   6. LEN 越界保护：LEN 声称超大不崩溃、不产出帧、缓冲不膨胀，还能重同步到后续合法帧；
 *   7. 空载荷（LEN=0）正常往返；
 *   8. 边界：单字节 feed、空 feed；
 *   9. 附加：bufferedBytes 语义。
 *
 * 教学点（对照 C# NUnit）：
 *   - 数据驱动测试用 _data() + QFETCH/QCOMPARE，等价于 NUnit 的 TestCaseSource；
 *   - 每个测试方法只验证一件事，方法名即用例名。
 */

#include <QtTest>
#include "protocol/protocoltypes.h"
#include "protocol/crc16.h"
#include "protocol/framebuilder.h"
#include "protocol/frameparser.h"

using namespace datascope::protocol;

// 测试类：继承 QObject，槽函数即测试用例
class TestProtocol : public QObject
{
    Q_OBJECT

private slots:
    // ---- 强制项 1：往返一致（数据驱动）----
    void roundTrip_data();
    void roundTrip();

    // ---- 强制项 2：半包 ----
    void halfPacket();

    // ---- 强制项 3：粘包 ----
    void stickyPackets();

    // ---- 强制项 4：CRC 篡改 ----
    void crcCorrupted();

    // ---- 强制项 5：SOF 错位（垃圾前缀重同步）----
    void sofMisalign();

    // ---- 强制项 6：LEN 越界保护 ----
    void lenOverflow();

    // ---- 强制项 7：空载荷往返 ----
    void emptyPayloadRoundTrip();

    // ---- 强制项 8：单字节 feed / 空 feed ----
    void singleByteFeed();
    void emptyFeed();

    // ---- 附加：bufferedBytes 语义 ----
    void bufferedBytesSemantics();

    // ---- 附加：组帧防御 / CRC 标准向量 ----
    void buildOversizedRejected();
    void crc16KnownVector();
};

// ================= 强制项 1：组帧 → 解析 往返一致 =================

void TestProtocol::roundTrip_data()
{
    QTest::addColumn<quint8>("func");
    QTest::addColumn<quint8>("cmd");
    QTest::addColumn<QByteArray>("payload");

    QTest::newRow("空载荷")   << quint8(0x01) << quint8(0x00)
                             << QByteArray();
    QTest::newRow("采集命令") << quint8(0x01) << quint8(0x01)
                             << QByteArray("\x01\x02\x03\x04", 4);
    QTest::newRow("配置命令") << quint8(0x02) << quint8(0x10)
                             << QByteArray("config", 6);
    QTest::newRow("查询命令") << quint8(0x03) << quint8(0x00)
                             << QByteArray("ABCDEFGHIJ", 10);
    QTest::newRow("响应帧")   << quint8(0x81) << quint8(0x01)
                             << QByteArray("\xFF\x00\xAA\x55", 4);
    QTest::newRow("最大载荷") << quint8(0x01) << quint8(0x00)
                             << QByteArray(kMaxPayloadLen, char(0x5A));
}

void TestProtocol::roundTrip()
{
    QFETCH(quint8, func);
    QFETCH(quint8, cmd);
    QFETCH(QByteArray, payload);

    // 1) 组帧：成功必须返回非空（空载荷也能得到 8 字节，空数组只表示失败）
    const QByteArray frame = FrameBuilder::build(func, cmd, payload);
    QVERIFY(!frame.isEmpty());
    // 整帧长度 = 6 + 载荷 + 2
    QCOMPARE(frame.size(), kHeaderLen + payload.size() + kCrcLen);

    // 2) 解析：直接整帧喂入并取出
    FrameParser parser;
    parser.feed(frame);
    ProtocolFrame out;
    QVERIFY(parser.nextFrame(out));
    QCOMPARE(out.func, func);
    QCOMPARE(out.cmd, cmd);
    QCOMPARE(out.payload, payload);
    QVERIFY(out.isValid);
    // 取完帧后缓冲应清零
    QCOMPARE(parser.bufferedBytes(), 0);
}

// ================= 强制项 2：半包 =================

void TestProtocol::halfPacket()
{
    const QByteArray payload("\x11\x22\x33\x44", 4);
    const QByteArray frame = FrameBuilder::build(0x01, 0x01, payload);   // 整帧 12 字节

    FrameParser parser;
    ProtocolFrame out;

    // 阶段1：只喂 3 字节（不足帧头 6 字节，状态 A/B 交界）→ 取帧失败、缓冲保留
    parser.feed(frame.left(3));
    QVERIFY(!parser.nextFrame(out));
    QCOMPARE(parser.bufferedBytes(), 3);

    // 阶段2：补到 8 字节（帧头完整但 DATA 只到一半，状态 C 交界）→ 仍取不出，缓冲不被破坏
    parser.feed(frame.mid(3, 5));
    QCOMPARE(parser.bufferedBytes(), 8);
    QVERIFY(!parser.nextFrame(out));
    QCOMPARE(parser.bufferedBytes(), 8);

    // 阶段3：补齐剩余字节 → 应能取出完整帧
    parser.feed(frame.mid(8));
    QVERIFY(parser.nextFrame(out));
    QCOMPARE(out.func, quint8(0x01));
    QCOMPARE(out.cmd, quint8(0x01));
    QCOMPARE(out.payload, payload);
    QVERIFY(out.isValid);
    QCOMPARE(parser.bufferedBytes(), 0);
}

// ================= 强制项 3：粘包 =================

void TestProtocol::stickyPackets()
{
    const QByteArray f1 = FrameBuilder::build(0x01, 0x01, QByteArray("A", 1));
    const QByteArray f2 = FrameBuilder::build(0x02, 0x02, QByteArray("BC", 2));

    FrameParser parser;
    parser.feed(f1 + f2);   // 一个 feed 里塞两帧

    ProtocolFrame a;
    ProtocolFrame b;
    QVERIFY(parser.nextFrame(a));
    QCOMPARE(a.func, quint8(0x01));
    QCOMPARE(a.cmd, quint8(0x01));
    QCOMPARE(a.payload, QByteArray("A", 1));
    QVERIFY(a.isValid);

    QVERIFY(parser.nextFrame(b));
    QCOMPARE(b.func, quint8(0x02));
    QCOMPARE(b.cmd, quint8(0x02));
    QCOMPARE(b.payload, QByteArray("BC", 2));
    QVERIFY(b.isValid);

    QCOMPARE(parser.bufferedBytes(), 0);
}

// ================= 强制项 4：CRC 篡改 =================

void TestProtocol::crcCorrupted()
{
    // 场景一：篡改 DATA 首字节
    QByteArray frame = FrameBuilder::build(0x01, 0x00, QByteArray("payload", 7));
    // 帧布局：下标 0-1 SOF，2 FUNC，3 CMD，4-5 LEN，6 起 DATA
    frame[6] = char(quint8(frame.at(6)) ^ 0xFF);

    FrameParser parser;
    parser.feed(frame);
    ProtocolFrame out;
    // CRC 不通过 → 不产出错误帧，且坏帧被消费丢弃
    QVERIFY(!parser.nextFrame(out));
    QCOMPARE(parser.bufferedBytes(), 0);

    // 场景二：篡改 CRC 末字节（CRC_H）
    QByteArray frame2 = FrameBuilder::build(0x01, 0x00, QByteArray("payload", 7));
    const int last = frame2.size() - 1;
    frame2[last] = char(quint8(frame2.at(last)) ^ 0xFF);

    FrameParser parser2;
    parser2.feed(frame2);
    QVERIFY(!parser2.nextFrame(out));
    QCOMPARE(parser2.bufferedBytes(), 0);
}

// ================= 强制项 5：SOF 错位（垃圾前缀重同步） =================

void TestProtocol::sofMisalign()
{
    const QByteArray payload("data", 4);
    const QByteArray frame = FrameBuilder::build(0x03, 0x00, payload);

    FrameParser parser;
    parser.feed(QByteArray("XYZ") + frame);   // 帧头前混入 3 字节垃圾

    ProtocolFrame out;
    QVERIFY(parser.nextFrame(out));
    QCOMPARE(out.func, quint8(0x03));
    QCOMPARE(out.cmd, quint8(0x00));
    QCOMPARE(out.payload, payload);
    QVERIFY(out.isValid);
    QCOMPARE(parser.bufferedBytes(), 0);
}

// ================= 强制项 6：LEN 越界保护 =================

void TestProtocol::lenOverflow()
{
    // 构造恶意帧头：SOF + FUNC + CMD + LEN=0xFFFF（远超 kMaxPayloadLen），
    // 后跟大量垃圾字节，模拟攻击者持续灌入数据。
    QByteArray evil;
    evil.append(char(0xAA)).append(char(0x55));  // SOF
    evil.append(char(0x01));                     // FUNC
    evil.append(char(0x00));                     // CMD
    evil.append(char(0xFF)).append(char(0xFF));  // LEN = 0xFFFF
    evil.append(QByteArray(2000, char(0x00)));   // 灌入 2000 字节垃圾

    FrameParser parser;
    parser.feed(evil);
    ProtocolFrame out;
    // 不崩溃、不产出帧
    QVERIFY(!parser.nextFrame(out));
    // 缓冲不膨胀（垃圾被清除，而不是等 65535 字节）
    QCOMPARE(parser.bufferedBytes(), 0);

    // 关键恢复性：恶意帧头后面紧跟一帧合法帧，应丢弃坏帧头、重同步到合法帧
    const QByteArray good = FrameBuilder::build(0x03, 0x00, QByteArray("ok", 2));
    FrameParser parser2;
    parser2.feed(evil + good);
    QVERIFY(parser2.nextFrame(out));
    QCOMPARE(out.func, quint8(0x03));
    QCOMPARE(out.payload, QByteArray("ok", 2));
    QVERIFY(out.isValid);
    QCOMPARE(parser2.bufferedBytes(), 0);
}

// ================= 强制项 7：空载荷往返 =================

void TestProtocol::emptyPayloadRoundTrip()
{
    // 空载荷 → LEN=0，整帧 = 6 + 0 + 2 = 8 字节
    const QByteArray frame = FrameBuilder::build(0x02, 0x00, QByteArray());
    QCOMPARE(frame.size(), 8);
    // 帧头后第 1、2 字节应是 LEN=0x0000
    QCOMPARE(static_cast<quint8>(frame.at(4)), quint8(0x00));
    QCOMPARE(static_cast<quint8>(frame.at(5)), quint8(0x00));

    FrameParser parser;
    parser.feed(frame);
    ProtocolFrame out;
    QVERIFY(parser.nextFrame(out));
    QCOMPARE(out.func, quint8(0x02));
    QCOMPARE(out.cmd, quint8(0x00));
    QVERIFY(out.payload.isEmpty());   // 载荷为空
    QVERIFY(out.isValid);
    QCOMPARE(parser.bufferedBytes(), 0);
}

// ================= 强制项 8：单字节 feed / 空 feed =================

void TestProtocol::singleByteFeed()
{
    // 4 字节载荷 → 整帧 6+4+2 = 12 字节，逐字节喂 12 次
    const QByteArray payload("\x10\x20\x30\x40", 4);
    const QByteArray frame = FrameBuilder::build(0x01, 0x02, payload);
    QCOMPARE(frame.size(), 12);

    FrameParser parser;
    ProtocolFrame out;
    for (int i = 0; i < frame.size(); ++i) {
        // 未喂完整帧前，任何一次取帧都必须失败（缓冲不足）
        QVERIFY(!parser.nextFrame(out));
        parser.feed(frame.mid(i, 1));
    }
    // 全部喂完后应能取出完整帧
    QVERIFY(parser.nextFrame(out));
    QCOMPARE(out.func, quint8(0x01));
    QCOMPARE(out.cmd, quint8(0x02));
    QCOMPARE(out.payload, payload);
    QVERIFY(out.isValid);
    QCOMPARE(parser.bufferedBytes(), 0);
}

void TestProtocol::emptyFeed()
{
    // 空 feed：不崩溃、不产出帧、缓冲不变
    FrameParser parser;
    parser.feed(QByteArray());
    ProtocolFrame out;
    QVERIFY(!parser.nextFrame(out));
    QCOMPARE(parser.bufferedBytes(), 0);
}

// ================= 附加：bufferedBytes 语义 =================

void TestProtocol::bufferedBytesSemantics()
{
    const QByteArray frame = FrameBuilder::build(0x01, 0x00, QByteArray("abcd", 4));
    FrameParser parser;

    QCOMPARE(parser.bufferedBytes(), 0);   // 初始为 0

    // 喂入半帧（前 4 字节：SOF+FUNC+CMD）
    parser.feed(frame.left(4));
    QCOMPARE(parser.bufferedBytes(), 4);

    // 半包取帧失败，缓冲必须原样保留
    ProtocolFrame out;
    QVERIFY(!parser.nextFrame(out));
    QCOMPARE(parser.bufferedBytes(), 4);

    // 补齐整帧后取出，缓冲清零
    parser.feed(frame.mid(4));
    QVERIFY(parser.nextFrame(out));
    QCOMPARE(out.payload, QByteArray("abcd", 4));
    QVERIFY(out.isValid);
    QCOMPARE(parser.bufferedBytes(), 0);
}

// ================= 附加：组帧防御 / CRC 标准向量 =================

void TestProtocol::buildOversizedRejected()
{
    // 载荷超过上限 → 组帧失败返回空数组
    const QByteArray tooBig(kMaxPayloadLen + 1, char(0x01));
    QVERIFY(FrameBuilder::build(0x01, 0x00, tooBig).isEmpty());

    // 边界：恰好等于上限 → 允许，且能正常往返
    const QByteArray ok(kMaxPayloadLen, char(0x01));
    const QByteArray frame = FrameBuilder::build(0x01, 0x00, ok);
    QCOMPARE(frame.size(), kHeaderLen + kMaxPayloadLen + kCrcLen);

    FrameParser parser;
    parser.feed(frame);
    ProtocolFrame out;
    QVERIFY(parser.nextFrame(out));
    QCOMPARE(out.payload.size(), kMaxPayloadLen);
    QVERIFY(out.isValid);
    QCOMPARE(parser.bufferedBytes(), 0);
}

void TestProtocol::crc16KnownVector()
{
    // MODBUS CRC16 官方测试向量："123456789"(ASCII) → 0x4B37
    const QByteArray data("123456789", 9);
    QCOMPARE(Crc16::compute(data), quint16(0x4B37));
}

// 生成 main()：QTEST_MAIN 会为测试类生成独立可执行文件入口
QTEST_MAIN(TestProtocol)
#include "tst_protocol.moc"
