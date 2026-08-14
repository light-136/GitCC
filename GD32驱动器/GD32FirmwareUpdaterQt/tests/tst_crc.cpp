// ============================================================
//  CRC16 校验单元测试实现
//
//  【测试知识】标准校验向量（Check Vector）：
//  CRC-16/MODBUS 算法对 ASCII 字符串 "123456789"
//  的官方校验值为 0x4B37，这是验证算法正确性的国际标准。
// ============================================================
#include "tst_crc.h"
#include "protocol/CrcCalculator.h"
#include <QtTest>

void CrcTest::standardVector()
{
    // "123456789" 的 CRC16/MODBUS 标准校验值 = 0x4B37
    QByteArray data("123456789");
    quint16 crc = CrcCalculator::calculate(data);
    QCOMPARE(crc, static_cast<quint16>(0x4B37));
}

void CrcTest::emptyData()
{
    // 空数据：算法直接返回初值 0xFFFF
    QByteArray data;
    quint16 crc = CrcCalculator::calculate(data);
    QCOMPARE(crc, static_cast<quint16>(0xFFFF));
}

void CrcTest::littleEndianBytes()
{
    // 0x4B37 小端 = 低字节 0x37 在前，高字节 0x4B 在后
    QByteArray bytes = CrcCalculator::toLittleEndianBytes(0x4B37);
    QCOMPARE(bytes.size(), 2);
    QCOMPARE(static_cast<quint8>(bytes.at(0)), static_cast<quint8>(0x37));
    QCOMPARE(static_cast<quint8>(bytes.at(1)), static_cast<quint8>(0x4B));
}

void CrcTest::rangeCalculate()
{
    // 对 "123456789" 的第 4 位起取 5 个字节（"56789"）计算
    QByteArray data("123456789");
    quint16 range = CrcCalculator::calculate(data, 4, 5);

    // 单独构造 "56789" 计算，应一致
    QByteArray tail("56789");
    quint16 expected = CrcCalculator::calculate(tail);
    QCOMPARE(range, expected);
}

void CrcTest::frameRoundTrip()
{
    // 构造一帧模拟数据：SOF + FUNC + CMD(大端) + DATALEN(小端) + DATA
    QByteArray frame;
    frame.append(char(0x01));                 // SOF
    frame.append(char(0x06));                 // FUNC
    frame.append(char(0x00)); frame.append(char(0x10));  // CMD = 0x0010 大端
    frame.append(char(0x02)); frame.append(char(0x00));  // DATALEN = 2 小端
    frame.append(char(0x01)); frame.append(char(0x00));  // DATA

    // 计算并附加小端 CRC
    quint16 crc = CrcCalculator::calculate(frame);
    frame.append(static_cast<char>(crc & 0xFF));
    frame.append(static_cast<char>((crc >> 8) & 0xFF));

    // 重算（不含 CRC 区）应一致
    quint16 recalc = CrcCalculator::calculate(frame, 0, frame.size() - 2);
    QCOMPARE(recalc, crc);
}
