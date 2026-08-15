/**
 * @file tst_v3_unit.cpp
 * @brief V3 单元测试 —— domain + protocol 纯逻辑（QtTest，无 GUI 无事件循环）
 *
 * ── 测试范围（对应权威规格第 11 节 Unit 层）──
 *   protocol：Crc16 标准向量、FrameBuilder/FrameParser 往返、半包、粘包、
 *             坏 CRC、前导垃圾重同步、LEN 越界拒绝；
 *   domain  ：AlarmEngine 触发/恢复/去重、ConnectionState 转移白名单、
 *             重连指数退避（封顶）。
 *
 * ── 为什么用 QtTest 而非 gtest ──
 *   本机零远程依赖（build.ps1 注释明确），Qt 5.12.2 自带 Qt5::Test；
 *   domain/protocol 只依赖 QtCore，QtTest 无需事件循环即可测纯逻辑，
 *   与"只链接 domain+protocol、禁 QtNetwork/QtWidgets"的白名单约束一致。
 */

#include <QtTest>

#include "protocol/channelconfigcodec.h"
#include "protocol/crc16.h"
#include "protocol/framebuilder.h"
#include "protocol/frameparser.h"
#include "protocol/frametypes.h"
#include "domain/alarmengine.h"
#include "domain/alarmrule.h"
#include "domain/datapoint.h"
#include "domain/connectionstate.h"

using namespace dscope::protocol;
using namespace dscope::domain;

/**
 * @class V3UnitTest
 * @brief domain/protocol 纯逻辑测试容器（无 GUI，QTEST_APPLESS_MAIN 驱动）
 */
class V3UnitTest : public QObject
{
    Q_OBJECT

private slots:
    void crc16_standardVector();        // CRC16 标准测试向量
    void frameRoundTrip();              // 帧构建→解析往返
    void parserHalfPacket();            // 半包
    void parserCoalescedPackets();      // 粘包
    void parserBadCrc();                // 坏 CRC
    void parserResyncGarbage();         // 前导垃圾重同步
    void parserOversizeLenRejected();   // LEN 越界拒绝
    void alarmTriggerRecoverDedup();    // 报警触发/恢复/去重
    void connectionStateTransitions();  // 连接状态转移白名单
    void retryBackoff();                // 重连指数退避封顶
    void channelConfigCodecRoundTrip(); // 通道配置编解码往返
};

void V3UnitTest::crc16_standardVector()
{
    // MODBUS CRC16 标准测试向量："123456789" → 0x4B37（初值 0xFFFF，poly 0xA001）
    QCOMPARE(Crc16::compute(QByteArray("123456789")), quint16(0x4B37));
}

void V3UnitTest::frameRoundTrip()
{
    const QByteArray payload = QByteArray::fromHex("0102030405");
    const QByteArray frame = FrameBuilder::build(kFuncData, 0x01, payload);
    QVERIFY(!frame.isEmpty());
    QCOMPARE(frame.size(), kHeaderLen + payload.size() + kCrcLen);

    FrameParser parser;
    parser.feed(frame);
    ParsedFrame parsed;
    FrameError err;
    QVERIFY(parser.next(parsed, err) == FrameParser::Status::Frame);
    QCOMPARE(parsed.version, kProtocolVersion);
    QCOMPARE(parsed.func, kFuncData);
    QCOMPARE(parsed.cmd, quint8(0x01));
    QCOMPARE(parsed.payload, payload);
}

void V3UnitTest::parserHalfPacket()
{
    const QByteArray payload = QByteArray::fromHex("AABBCC");
    const QByteArray frame = FrameBuilder::build(kFuncData, 0x01, payload);

    FrameParser parser;
    ParsedFrame parsed;
    FrameError err;

    parser.feed(frame.left(3));    // 只喂 3 字节（不足帧头 7 字节）
    QVERIFY(parser.next(parsed, err) == FrameParser::Status::NeedMoreData);

    parser.feed(frame.mid(3));     // 补齐剩余
    QVERIFY(parser.next(parsed, err) == FrameParser::Status::Frame);
    QCOMPARE(parsed.payload, payload);
}

void V3UnitTest::parserCoalescedPackets()
{
    const QByteArray f1 = FrameBuilder::build(kFuncData, 0x01, QByteArray::fromHex("11"));
    const QByteArray f2 = FrameBuilder::build(kFuncData, 0x02, QByteArray::fromHex("22"));

    FrameParser parser;
    parser.feed(f1 + f2);          // 粘包：两帧一次喂入

    ParsedFrame parsed;
    FrameError err;
    QVERIFY(parser.next(parsed, err) == FrameParser::Status::Frame);
    QCOMPARE(parsed.cmd, quint8(0x01));
    QCOMPARE(parsed.payload, QByteArray::fromHex("11"));

    QVERIFY(parser.next(parsed, err) == FrameParser::Status::Frame);
    QCOMPARE(parsed.cmd, quint8(0x02));
    QCOMPARE(parsed.payload, QByteArray::fromHex("22"));

    QVERIFY(parser.next(parsed, err) == FrameParser::Status::NeedMoreData);
}

void V3UnitTest::parserBadCrc()
{
    QByteArray frame = FrameBuilder::build(kFuncData, 0x01, QByteArray::fromHex("112233"));
    frame[kHeaderLen] = static_cast<char>(frame[kHeaderLen] ^ 0xFF);  // 破坏 DATA 首字节

    FrameParser parser;
    parser.feed(frame);
    ParsedFrame parsed;
    FrameError err;
    QVERIFY(parser.next(parsed, err) == FrameParser::Status::Error);
    QVERIFY(err == FrameError::CrcError);
}

void V3UnitTest::parserResyncGarbage()
{
    const QByteArray frame = FrameBuilder::build(kFuncData, 0x01, QByteArray::fromHex("ABCDEF"));

    FrameParser parser;
    parser.feed(QByteArray::fromHex("000102") + frame);   // 前导垃圾 + 合法帧

    ParsedFrame parsed;
    FrameError err;
    QVERIFY(parser.next(parsed, err) == FrameParser::Status::Frame);
    QCOMPARE(parsed.payload, QByteArray::fromHex("ABCDEF"));
}

void V3UnitTest::parserOversizeLenRejected()
{
    // 手工构造一帧：LEN 声明 1025（> kMaxPayloadLen=1024）→ 非法帧头
    QByteArray bad;
    bad.append(static_cast<char>(0xAA));
    bad.append(static_cast<char>(0x55));
    bad.append(static_cast<char>(kProtocolVersion));
    bad.append(static_cast<char>(kFuncData));
    bad.append(static_cast<char>(0x01));
    bad.append(static_cast<char>(0x04));   // LEN_HI = 0x04
    bad.append(static_cast<char>(0x01));   // LEN_LO = 0x01 → LEN = 0x0401 = 1025
    bad.append(QByteArray(10, '\0'));      // 填充部分数据

    FrameParser parser;
    parser.feed(bad);
    ParsedFrame parsed;
    FrameError err;
    QVERIFY(parser.next(parsed, err) == FrameParser::Status::Error);
    QVERIFY(err == FrameError::InvalidHeader);
}

void V3UnitTest::alarmTriggerRecoverDedup()
{
    AlarmEngine engine;
    AlarmRule rule;
    rule.channelIndex = 0;
    rule.threshold    = 100.0;
    rule.hysteresis   = 5.0;
    engine.addRule(rule);

    DataPoint dp;
    dp.channelIndex = 0;

    // 未触发：90 < 100
    dp.value = 90.0;
    QVERIFY(engine.evaluate(dp).isEmpty());

    // 触发：110 > 100 → Trigger
    dp.value = 110.0;
    QVector<AlarmEvent> ev1 = engine.evaluate(dp);
    QCOMPARE(ev1.size(), 1);
    QVERIFY(ev1[0].type == AlarmEvent::Trigger);

    // 去重：仍在报警态，继续 110 → 无事件
    dp.value = 110.0;
    QVERIFY(engine.evaluate(dp).isEmpty());

    // 回差死区：97 在 (95,100] 之间 → 不恢复
    dp.value = 97.0;
    QVERIFY(engine.evaluate(dp).isEmpty());

    // 恢复：90 < 95（threshold - hysteresis）→ Recover
    dp.value = 90.0;
    QVector<AlarmEvent> ev2 = engine.evaluate(dp);
    QCOMPARE(ev2.size(), 1);
    QVERIFY(ev2[0].type == AlarmEvent::Recover);
}

void V3UnitTest::connectionStateTransitions()
{
    // 合法转移（状态机白名单）
    QVERIFY(canTransition(ConnectionState::Disconnected, ConnectionState::Connecting));
    QVERIFY(canTransition(ConnectionState::Connecting, ConnectionState::Connected));
    QVERIFY(canTransition(ConnectionState::Connecting, ConnectionState::Error));
    QVERIFY(canTransition(ConnectionState::Connected, ConnectionState::Error));
    QVERIFY(canTransition(ConnectionState::Connected, ConnectionState::Disconnected));
    QVERIFY(canTransition(ConnectionState::Error, ConnectionState::Connecting));
    QVERIFY(canTransition(ConnectionState::Error, ConnectionState::Disconnected));

    // 非法转移（默认拒绝）
    QVERIFY(!canTransition(ConnectionState::Disconnected, ConnectionState::Connected));
    QVERIFY(!canTransition(ConnectionState::Disconnected, ConnectionState::Error));
    QVERIFY(!canTransition(ConnectionState::Connected, ConnectionState::Connecting));
    QVERIFY(!canTransition(ConnectionState::Connecting, ConnectionState::Disconnected));
}

void V3UnitTest::retryBackoff()
{
    QCOMPARE(nextRetryDelayMs(0), 1000);
    QCOMPARE(nextRetryDelayMs(1), 2000);
    QCOMPARE(nextRetryDelayMs(2), 4000);
    QCOMPARE(nextRetryDelayMs(3), 8000);
    QCOMPARE(nextRetryDelayMs(4), 16000);
    QCOMPARE(nextRetryDelayMs(5), 30000);   // 32000 封顶到 30000
    QCOMPARE(nextRetryDelayMs(30), 30000);  // 1000<<30 未溢出，正常左移路径再封顶
    QCOMPARE(nextRetryDelayMs(31), 30000);  // 钳制分支起点：attempt>=31 直接封顶，规避左移 UB
    QCOMPARE(nextRetryDelayMs(54), 30000);  // 远超出钳制阈值，仍安全返回封顶值
    QCOMPARE(nextRetryDelayMs(100), 30000);
    QCOMPARE(nextRetryDelayMs(-1), 1000);   // 负数防御：视为第一次重试
}

void V3UnitTest::channelConfigCodecRoundTrip()
{
    // 构造 4 通道配置（含中文名/单位，验证 UTF-8 定长编解码对称）
    QVector<ChannelConfigInfo> cfg;
    for (int i = 0; i < 4; ++i) {
        ChannelConfigInfo info;
        info.index    = static_cast<quint8>(i);
        info.name     = QStringLiteral("通道%1").arg(i + 1);
        info.unit     = QStringLiteral("单位%1").arg(i + 1);
        info.rangeMin = static_cast<float>(i);
        info.rangeMax = static_cast<float>(100 + i);
        cfg.append(info);
    }

    // 编码：载荷长度 = 1(通道数) + N×33(每通道定长)
    const QByteArray payload = ChannelConfigCodec::encode(cfg);
    QCOMPARE(payload.size(), 1 + 4 * kChannelConfigWireSize);
    QCOMPARE(static_cast<quint8>(payload.at(0)), quint8(4));   // 首字节 = 通道数 4

    // 解码：往返一致
    QVector<ChannelConfigInfo> decoded;
    QVERIFY(ChannelConfigCodec::decode(payload, decoded));
    QCOMPARE(decoded.size(), 4);
    for (int i = 0; i < 4; ++i) {
        QCOMPARE(decoded.at(i).index,    cfg.at(i).index);
        QCOMPARE(decoded.at(i).name,     cfg.at(i).name);
        QCOMPARE(decoded.at(i).unit,     cfg.at(i).unit);
        QCOMPARE(decoded.at(i).rangeMin, cfg.at(i).rangeMin);
        QCOMPARE(decoded.at(i).rangeMax, cfg.at(i).rangeMax);
    }

    // 解码非法载荷：长度与"1+33N"不符 → 返回 false 且 out 为空
    QVector<ChannelConfigInfo> bad;
    QVERIFY(!ChannelConfigCodec::decode(QByteArray::fromHex("04010203"), bad));
    QVERIFY(bad.isEmpty());
}

QTEST_APPLESS_MAIN(V3UnitTest)
#include "tst_v3_unit.moc"
