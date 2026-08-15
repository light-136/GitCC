/**
 * @file tst_faultinjector.cpp
 * @brief 异常注入器单测（V2-执行③：模拟设备升级）—— Qt Test 框架
 *
 * FaultInjector 是"正常帧 → 异常字节流"的纯逻辑变换器，因此可以脱离网络
 * 直接单测：用 FrameBuilder 构造一帧真实协议帧，喂入各 FaultType，
 * 断言输出字节流符合异常语义：
 *
 *   None         → 原样返回一帧（一次 write）
 *   Sticky       → 两帧合并为一段（一次 write 里塞两帧，考验主机粘包重组）
 *   Fragment     → 一帧拆两段（两次 write，考验主机分包拼接）
 *   CrcError     → 篡改帧尾校验字节（喂给 FrameParser 必须被丢弃）
 *   IllegalFrame → 把 LEN 字段改为 0xFFFF（解析器应判非法帧头并防御性拒绝）
 *   Delay        → 触发周期内不发送（空 vector = 本次不发）
 *   Surge        → 指定通道 float 被替换为突变值（可解析回 float 断言）
 *
 * 教学点（对照 C#）：FaultInjector 等价于测试替身里的"故障注入器"，
 * 让被测试对象（主机解析链路）在可控故障下得到验证 —— 这是"测试基础设施"
 * 的核心思想：把故障做成可配置的输入，而不是祈祷不出错。
 */

#include <QtTest>

#include "simulator/faultinjector.h"
#include "protocol/framebuilder.h"
#include "protocol/frameparser.h"
#include "protocol/protocoltypes.h"

#include <cstring>

using namespace datascope::simulator;
using namespace datascope::protocol;

namespace {

/**
 * @brief 从协议帧载荷中读取指定通道的大端 float 值
 * @param frame 完整协议帧字节（载荷从下标 6 开始，每通道 4 字节）
 * @param channel 通道序号
 * @return 解析出的 float 值（越界返回 0）
 */
float readChannelValue(const QByteArray &frame, int channel)
{
    const int offset = kHeaderLen + channel * 4;
    if (offset + 4 > frame.size())
        return 0.0f;

    quint32 bits = 0;
    bits |= static_cast<quint8>(frame.at(offset))     << 24;
    bits |= static_cast<quint8>(frame.at(offset + 1)) << 16;
    bits |= static_cast<quint8>(frame.at(offset + 2)) << 8;
    bits |= static_cast<quint8>(frame.at(offset + 3));
    float value = 0.0f;
    std::memcpy(&value, &bits, sizeof(value));
    return value;
}

} // namespace

class TestFaultInjector : public QObject
{
    Q_OBJECT

private slots:
    // ---- 无异常：原样返回 ----
    void noneTypeReturnsFrameAsIs();

    // ---- 粘包：两帧合并一次发送 ----
    void stickyMergesTwoFrames();

    // ---- 分包：一帧拆两段发送 ----
    void fragmentSplitsIntoTwoChunks();

    // ---- CRC 错误：篡改校验字节，解析器必须拒绝 ----
    void crcErrorFrameRejectedByParser();

    // ---- 非法帧：LEN 字段被破坏 ----
    void illegalFrameCorruptsLengthField();

    // ---- 延迟：触发周期内不发送 ----
    void delaySuppressesFrameOnTrigger();

    // ---- 数据突变：通道值被替换为超量程值 ----
    void surgeReplacesChannelValue();

    // ---- 防御：短帧不崩溃 ----
    void shortFrameDoesNotCrash();
};

// ================= 无异常 =================

void TestFaultInjector::noneTypeReturnsFrameAsIs()
{
    const QByteArray frame = FrameBuilder::build(0x01, 0x01, QByteArray(16, char(0x33)));
    FaultInjector injector;   // 默认 cfg.type = None

    const QVector<QByteArray> chunks = injector.process(0, frame);

    // 原样返回：1 段，内容与输入完全一致
    QCOMPARE(chunks.size(), 1);
    QCOMPARE(chunks[0], frame);
}

// ================= 粘包 =================

void TestFaultInjector::stickyMergesTwoFrames()
{
    const QByteArray frame = FrameBuilder::build(0x01, 0x01, QByteArray(16, char(0x11)));
    FaultInjector injector;
    injector.setConfig({ FaultType::Sticky });

    const QVector<QByteArray> chunks = injector.process(0, frame);

    // 粘包语义：1 段字节流 = 两帧拼接（一次 write 塞两帧）
    QCOMPARE(chunks.size(), 1);
    QCOMPARE(chunks[0], frame + frame);

    // 关键验证：主机若把这整段喂给 FrameParser，应能拆出 2 帧合法帧
    FrameParser parser;
    parser.feed(chunks[0]);
    ProtocolFrame a, b;
    QVERIFY(parser.nextFrame(a));
    QVERIFY(parser.nextFrame(b));
    QVERIFY(a.isValid);
    QVERIFY(b.isValid);
    QCOMPARE(parser.bufferedBytes(), 0);
}

// ================= 分包 =================

void TestFaultInjector::fragmentSplitsIntoTwoChunks()
{
    const QByteArray frame = FrameBuilder::build(0x01, 0x01, QByteArray(16, char(0x22)));
    FaultInjector injector;
    injector.setConfig({ FaultType::Fragment });

    const QVector<QByteArray> chunks = injector.process(0, frame);

    // 分包语义：2 段，拼接后等于原帧
    QCOMPARE(chunks.size(), 2);
    QCOMPARE(chunks[0] + chunks[1], frame);

    // 每段都必须是"中间状态"：第一段不足一帧（单独喂入取不出帧）
    FrameParser parser;
    parser.feed(chunks[0]);
    ProtocolFrame out;
    QVERIFY(!parser.nextFrame(out));   // 半包：取不出完整帧

    // 补上第二段后应能取出完整合法帧
    parser.feed(chunks[1]);
    QVERIFY(parser.nextFrame(out));
    QVERIFY(out.isValid);
    QCOMPARE(out.func, quint8(0x01));
    QCOMPARE(parser.bufferedBytes(), 0);
}

// ================= CRC 错误 =================

void TestFaultInjector::crcErrorFrameRejectedByParser()
{
    const QByteArray frame = FrameBuilder::build(0x01, 0x01, QByteArray(16, char(0x44)));
    FaultInjector injector;
    injector.setConfig({ FaultType::CrcError });

    const QVector<QByteArray> chunks = injector.process(0, frame);

    // 篡改语义：1 段，长度不变（只改字节不增减），内容已变
    QCOMPARE(chunks.size(), 1);
    QCOMPARE(chunks[0].size(), frame.size());
    QVERIFY(chunks[0] != frame);

    // 关键验证：篡改后的帧喂给解析器必须被丢弃（CRC 校验失败不产出帧）
    FrameParser parser;
    parser.feed(chunks[0]);
    ProtocolFrame out;
    QVERIFY(!parser.nextFrame(out));
    QCOMPARE(parser.bufferedBytes(), 0);   // 坏帧被消费丢弃
}

// ================= 非法帧 =================

void TestFaultInjector::illegalFrameCorruptsLengthField()
{
    const QByteArray frame = FrameBuilder::build(0x01, 0x01, QByteArray(16, char(0x55)));
    FaultInjector injector;
    injector.setConfig({ FaultType::IllegalFrame });

    const QVector<QByteArray> chunks = injector.process(0, frame);

    QCOMPARE(chunks.size(), 1);
    // LEN 字段（下标 4-5，大端）被改为 0xFFFF
    QCOMPARE(static_cast<quint8>(chunks[0].at(4)), quint8(0xFF));
    QCOMPARE(static_cast<quint8>(chunks[0].at(5)), quint8(0xFF));

    // 关键验证：解析器遇到超大 LEN 必须防御性拒绝，且不膨胀缓冲
    FrameParser parser;
    parser.feed(chunks[0]);
    ProtocolFrame out;
    QVERIFY(!parser.nextFrame(out));
    QCOMPARE(parser.bufferedBytes(), 0);
}

// ================= 延迟 =================

void TestFaultInjector::delaySuppressesFrameOnTrigger()
{
    const QByteArray frame = FrameBuilder::build(0x01, 0x01, QByteArray(16, char(0x66)));
    FaultInjector injector;
    injector.setConfig({ FaultType::Delay, /*period=*/2 });

    // 触发帧（frameSeq=0）：不发送（空 vector）
    const QVector<QByteArray> suppressed = injector.process(0, frame);
    QVERIFY(suppressed.isEmpty());

    // 非触发帧（frameSeq=1）：正常发送
    const QVector<QByteArray> normal = injector.process(1, frame);
    QCOMPARE(normal.size(), 1);
    QCOMPARE(normal[0], frame);

    // 再次触发（frameSeq=2）：又不发送 —— 周期行为稳定
    QVERIFY(injector.process(2, frame).isEmpty());
}

// ================= 数据突变 =================

void TestFaultInjector::surgeReplacesChannelValue()
{
    // 构造 4 通道帧，载荷全为字节 0x01（对应的 float 值很小）
    const QByteArray frame = FrameBuilder::build(0x01, 0x01, QByteArray(16, char(0x01)));
    FaultInjector injector;
    injector.setConfig({ FaultType::Surge, /*period=*/1, /*channelIndex=*/0, /*surgeValue=*/200.0 });

    // 触发帧：通道 0 应被替换为 200.0
    const QVector<QByteArray> chunks = injector.process(0, frame);
    QCOMPARE(chunks.size(), 1);

    const float surged = readChannelValue(chunks[0], 0);
    QVERIFY(qAbs(surged - 200.0f) < 0.001f);

    // 其它通道不受影响：通道 1 仍是原始值
    const float ch1 = readChannelValue(chunks[0], 1);
    QCOMPARE(ch1, readChannelValue(frame, 1));

    // 关键验证：突变帧 CRC 仍有效（不是把帧弄坏，而是把"值"改到超量程）
    FrameParser parser;
    parser.feed(chunks[0]);
    ProtocolFrame out;
    QVERIFY(parser.nextFrame(out));
    QVERIFY(out.isValid);   // 帧本身合法，但值已超量程 —— 触发主机报警
}

// ================= 防御：短帧不崩溃 =================

void TestFaultInjector::shortFrameDoesNotCrash()
{
    // 各种异常类型喂入过短帧（不足帧头）都不能崩溃，应原样或安全返回
    FaultInjector injector;
    const QByteArray tooShort("AA", 2);

    injector.setConfig({ FaultType::CrcError });
    QCOMPARE(injector.process(0, tooShort), QVector<QByteArray>{ tooShort });

    injector.setConfig({ FaultType::IllegalFrame });
    QCOMPARE(injector.process(0, tooShort), QVector<QByteArray>{ tooShort });

    injector.setConfig({ FaultType::Surge, 1, 5, 200.0 });   // 通道越界
    QCOMPARE(injector.process(0, tooShort), QVector<QByteArray>{ tooShort });
}

// 生成 main()
QTEST_MAIN(TestFaultInjector)
#include "tst_faultinjector.moc"
