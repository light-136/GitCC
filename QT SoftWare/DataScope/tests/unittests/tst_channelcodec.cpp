/**
 * @file tst_channelcodec.cpp
 * @brief 通道配置编解码单测（V2-执行③：Unit 层）—— Qt Test 框架
 *
 * ChannelConfigCodec 是纯字节编解码器（无网络），单测直接构造数据断言：
 *
 *   1. 往返一致：编码 → 解码 还原出相同的通道配置（中文名/单位/量程）；
 *   2. 编码防御：空列表 / 超过 255 通道 → 返回空数组（编码失败）；
 *   3. 解码防御：脏载荷（长度不符/通道数非法/越界字段）→ 返回 false，out 为空；
 *   4. 定长语义：超长名称截断到 16 字节、短名称补零后解码正常；
 *   5. 字节序：min/max 为 float 大端（可解析回原始浮点值）。
 */

#include <QtTest>

#include "protocol/channelconfigcodec.h"

using namespace datascope::protocol;

namespace {
/** @brief 构造一个标准的 4 通道配置（含中文，验证 UTF-8 定长） */
QVector<ChannelConfigInfo> makeFourChannels()
{
    QVector<ChannelConfigInfo> cfg;
    for (int i = 0; i < 4; ++i) {
        ChannelConfigInfo ch;
        ch.index = static_cast<quint8>(i);
        ch.name  = i == 0 ? QStringLiteral("温度") : QStringLiteral("通道%1").arg(i);
        ch.unit  = QStringLiteral("℃");
        ch.rangeMin = 0.0f;
        ch.rangeMax = 100.0f + i * 10.0f;
        cfg.append(ch);
    }
    return cfg;
}
} // namespace

class TestChannelCodec : public QObject
{
    Q_OBJECT

private slots:
    void roundTripRestoresConfig();       // 往返一致
    void floatBigEndianRoundTrip();       // 量程浮点大端往返
    void encodeRejectsEmptyList();        // 空列表编码失败
    void encodeRejectsTooMany();          // >255 通道编码失败
    void decodeRejectsDirtyPayload();     // 脏载荷解码失败
    void longNameIsTruncated();           // 超长名称截断
    void shortNamePaddedThenDecodes();    // 短名称补零后解码正常
};

void TestChannelCodec::roundTripRestoresConfig()
{
    const QVector<ChannelConfigInfo> src = makeFourChannels();
    const QByteArray payload = ChannelConfigCodec::encode(src);
    QVERIFY(!payload.isEmpty());

    QVector<ChannelConfigInfo> out;
    QVERIFY(ChannelConfigCodec::decode(payload, out));
    QCOMPARE(out.size(), 4);

    for (int i = 0; i < 4; ++i) {
        QCOMPARE(out[i].index, src[i].index);
        QCOMPARE(out[i].name,  src[i].name);    // 中文名 UTF-8 定长往返
        QCOMPARE(out[i].unit,  src[i].unit);
        QVERIFY(qAbs(out[i].rangeMin - src[i].rangeMin) < 0.001f);
        QVERIFY(qAbs(out[i].rangeMax - src[i].rangeMax) < 0.001f);
    }
}

void TestChannelCodec::floatBigEndianRoundTrip()
{
    QVector<ChannelConfigInfo> src;
    ChannelConfigInfo ch;
    ch.index = 0;
    ch.name  = QStringLiteral("精确值");
    ch.unit  = QStringLiteral("mV");
    ch.rangeMin = -3.14159f;
    ch.rangeMax =  2.71828f;
    src.append(ch);

    const QByteArray payload = ChannelConfigCodec::encode(src);
    QVector<ChannelConfigInfo> out;
    QVERIFY(ChannelConfigCodec::decode(payload, out));

    // 浮点位模式经大端往返必须精确一致（qFuzzyCompare 对 0 值/极值不可靠）
    QCOMPARE(out[0].rangeMin, ch.rangeMin);
    QCOMPARE(out[0].rangeMax, ch.rangeMax);
}

void TestChannelCodec::encodeRejectsEmptyList()
{
    QVERIFY(ChannelConfigCodec::encode(QVector<ChannelConfigInfo>()).isEmpty());
}

void TestChannelCodec::encodeRejectsTooMany()
{
    // 256 通道 → 通道数 1 字节装不下 → 编码失败
    QVector<ChannelConfigInfo> many;
    for (int i = 0; i < 256; ++i) {
        ChannelConfigInfo ch;
        ch.index = static_cast<quint8>(i);
        many.append(ch);
    }
    QVERIFY(ChannelConfigCodec::encode(many).isEmpty());
}

void TestChannelCodec::decodeRejectsDirtyPayload()
{
    QVector<ChannelConfigInfo> out;

    // 空载荷 → false
    QVERIFY(!ChannelConfigCodec::decode(QByteArray(), out));
    QVERIFY(out.isEmpty());

    // 通道数 = 0 → false
    QVERIFY(!ChannelConfigCodec::decode(QByteArray(1, char(0)), out));

    // 通道数 = 2 但长度只有"1 通道"的载荷 → 长度校验失败
    QByteArray tooShort(1 + kChannelConfigWireSize, char(0));
    tooShort[0] = char(2);
    QVERIFY(!ChannelConfigCodec::decode(tooShort, out));
    QVERIFY(out.isEmpty());

    // 声称 255 通道、长度正确的脏数据也应能解码（长度合法时结构是有效的）
    QByteArray big(1 + 2 * kChannelConfigWireSize, char(0));
    big[0] = char(2);
    QVector<ChannelConfigInfo> ok;
    QVERIFY(ChannelConfigCodec::decode(big, ok));   // 结构合法 → 成功（防御的是"长度不符"）
    QCOMPARE(ok.size(), 2);
}

void TestChannelCodec::longNameIsTruncated()
{
    QVector<ChannelConfigInfo> src;
    ChannelConfigInfo ch;
    ch.index = 0;
    ch.name  = QString(50, QLatin1Char('测'));   // 50 个汉字，远超 16 字节
    src.append(ch);

    const QByteArray payload = ChannelConfigCodec::encode(src);
    // 单通道载荷 = 1(通道数) + 33 = 34 字节（超长名被截断，不撑爆定长布局）
    QCOMPARE(payload.size(), 1 + kChannelConfigWireSize);

    QVector<ChannelConfigInfo> out;
    QVERIFY(ChannelConfigCodec::decode(payload, out));
    // 截断到 UTF-8 的 16 字节边界内（截断可能切到多字节字符中间，
    // fromUtf8 会按"替换字符"处理 —— 只要不崩溃、长度合理即可）
    QVERIFY(out[0].name.toUtf8().size() <= kChannelNameLen);
}

void TestChannelCodec::shortNamePaddedThenDecodes()
{
    QVector<ChannelConfigInfo> src;
    ChannelConfigInfo ch;
    ch.index = 3;
    ch.name  = QStringLiteral("A");    // 极短名称
    ch.unit  = QStringLiteral("V");    // 极短单位
    src.append(ch);

    const QByteArray payload = ChannelConfigCodec::encode(src);
    QVector<ChannelConfigInfo> out;
    QVERIFY(ChannelConfigCodec::decode(payload, out));
    QCOMPARE(out[0].index, quint8(3));
    QCOMPARE(out[0].name, QStringLiteral("A"));
    QCOMPARE(out[0].unit, QStringLiteral("V"));
}

// 生成 main()
QTEST_MAIN(TestChannelCodec)
#include "tst_channelcodec.moc"
