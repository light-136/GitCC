// ============================================================
//  协议帧构造/解析单元测试
//
//  【测试知识】通过验证"帧首部固定字节"与"CRC 重算一致性"
//  来锁定字节序（大端 CMD / 小端 DATALEN）与组帧正确性。
// ============================================================
#include "tst_frame.h"
#include "protocol/ProtocolFrame.h"
#include "protocol/CrcCalculator.h"
#include <QtTest>

// 断言：帧的前 6 个固定字节符合 01 06 CMD(大端) 长度(小端)
static void checkHeader(const QByteArray &frame, quint16 cmd, quint16 dataLen)
{
    QVERIFY(frame.size() >= 8);
    QCOMPARE(static_cast<quint8>(frame.at(0)), static_cast<quint8>(0x01));  // SOF
    QCOMPARE(static_cast<quint8>(frame.at(1)), static_cast<quint8>(0x06));  // FUNC
    // CMD 大端
    quint16 gotCmd = (static_cast<quint8>(frame.at(2)) << 8)
                   |  static_cast<quint8>(frame.at(3));
    QCOMPARE(gotCmd, cmd);
    // DATALEN 小端
    quint16 gotLen = static_cast<quint8>(frame.at(4))
                   | (static_cast<quint16>(static_cast<quint8>(frame.at(5))) << 8);
    QCOMPARE(gotLen, dataLen);
}

// 断言：帧尾 CRC 与重算一致
static void checkCrc(const QByteArray &frame)
{
    quint16 calc = CrcCalculator::calculate(frame, 0, frame.size() - 2);
    quint16 stored = static_cast<quint8>(frame.at(frame.size() - 2))
                   | (static_cast<quint16>(static_cast<quint8>(frame.at(frame.size() - 1))) << 8);
    QCOMPARE(stored, calc);
}

void FrameTest::upgradeRequestFrame()
{
    QByteArray frame = FrameBuilder::buildUpgradeRequest(0x01);
    checkHeader(frame, 0x0010, 2);
    checkCrc(frame);
    // DATA = [0x01, 0x00]
    QCOMPARE(static_cast<quint8>(frame.at(6)), static_cast<quint8>(0x01));
    QCOMPARE(static_cast<quint8>(frame.at(7)), static_cast<quint8>(0x00));
}

void FrameTest::upgradeStartFrame()
{
    QByteArray frame = FrameBuilder::buildUpgradeStart(0x00123456u);
    checkHeader(frame, 0x0011, 4);
    checkCrc(frame);
    // 文件大小小端：0x56 0x34 0x12 0x00
    QCOMPARE(static_cast<quint8>(frame.at(6)), static_cast<quint8>(0x56));
    QCOMPARE(static_cast<quint8>(frame.at(7)), static_cast<quint8>(0x34));
    QCOMPARE(static_cast<quint8>(frame.at(8)), static_cast<quint8>(0x12));
    QCOMPARE(static_cast<quint8>(frame.at(9)), static_cast<quint8>(0x00));
}

void FrameTest::upgradeDataFrame()
{
    // 3 字节数据 + 4 字节偏移 = 7 字节 DATA
    QByteArray payload;
    payload.append(char(0xAA)); payload.append(char(0xBB)); payload.append(char(0xCC));

    QByteArray frame = FrameBuilder::buildUpgradeData(0x00000100u, payload);
    checkHeader(frame, 0x0012, 7);
    checkCrc(frame);
    // 偏移地址小端：00 01 00 00（0x00000100）
    QCOMPARE(static_cast<quint8>(frame.at(6)), static_cast<quint8>(0x00));
    QCOMPARE(static_cast<quint8>(frame.at(7)), static_cast<quint8>(0x01));
    QCOMPARE(static_cast<quint8>(frame.at(8)), static_cast<quint8>(0x00));
    QCOMPARE(static_cast<quint8>(frame.at(9)), static_cast<quint8>(0x00));
    // 随后是数据
    QCOMPARE(static_cast<quint8>(frame.at(10)), static_cast<quint8>(0xAA));
    QCOMPARE(static_cast<quint8>(frame.at(11)), static_cast<quint8>(0xBB));
    QCOMPARE(static_cast<quint8>(frame.at(12)), static_cast<quint8>(0xCC));
}

void FrameTest::jumpAppFrame()
{
    QByteArray frame = FrameBuilder::buildJumpApp();
    checkHeader(frame, 0x0013, 1);
    checkCrc(frame);
    QCOMPARE(static_cast<quint8>(frame.at(6)), static_cast<quint8>(0x01));
}

void FrameTest::versionRequestFrame()
{
    QByteArray frame = FrameBuilder::buildVersionRequest();
    checkHeader(frame, 0x0014, 1);
    checkCrc(frame);
    QCOMPARE(static_cast<quint8>(frame.at(6)), static_cast<quint8>(0x01));
}

void FrameTest::serializeDeserializeRoundTrip()
{
    // 组一帧复杂数据，拆帧后各字段应一致
    ProtocolFrame frame;
    frame.cmd = 0x0012;
    frame.data.append(char(0x11));
    frame.data.append(char(0x22));
    frame.data.append(char(0x33));

    QByteArray bytes = frame.serialize();
    bool ok = false;
    ProtocolFrame parsed = ProtocolFrame::deserialize(bytes, &ok);

    QVERIFY(ok);
    QCOMPARE(parsed.sof, frame.sof);
    QCOMPARE(parsed.func, frame.func);
    QCOMPARE(parsed.cmd, frame.cmd);
    QCOMPARE(parsed.data, frame.data);
}

void FrameTest::corruptedCrcFails()
{
    QByteArray frame = FrameBuilder::buildUpgradeRequest(0x01);
    // 翻转最后一个字节（CRC 高位）模拟传输错误
    frame[frame.size() - 1] = static_cast<char>(frame.at(frame.size() - 1) ^ 0xFF);

    bool ok = true;
    ProtocolFrame parsed = ProtocolFrame::deserialize(frame, &ok);
    QVERIFY(!ok);  // CRC 校验应失败
}

void FrameTest::wrongSofFails()
{
    QByteArray frame = FrameBuilder::buildUpgradeRequest(0x01);
    frame[0] = static_cast<char>(0x02);  // 改坏 SOF

    bool ok = true;
    ProtocolFrame::deserialize(frame, &ok);
    QVERIFY(!ok);
}

void FrameTest::shortBufferFails()
{
    // 只有 4 个字节，不足最小帧长 8 字节
    QByteArray shortData = QByteArray::fromHex("01060010");
    bool ok = true;
    ProtocolFrame::deserialize(shortData, &ok);
    QVERIFY(!ok);
}
