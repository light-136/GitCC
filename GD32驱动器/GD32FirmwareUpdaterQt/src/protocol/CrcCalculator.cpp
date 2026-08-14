// ============================================================
//  CRC16 校验计算器实现 — CRC16/Modbus
//
//  【Qt知识点】QByteArray 是字节容器（类似 C# 的 byte[]），
//  at(i) 返回 char（可能有符号），因此用 static_cast<quint8>
//  保证按无符号字节参与位运算 —— 这是 C/C++ 位运算的常见坑，
//  C# 的 byte 天然无符号，C++ 的 char 则可能为有符号。
// ============================================================
#include "CrcCalculator.h"

quint16 CrcCalculator::calculate(const QByteArray &data)
{
    return calculate(data, 0, data.size());
}

quint16 CrcCalculator::calculate(const QByteArray &data, int offset, int length)
{
    // 边界保护：越界时自动裁剪到合法范围
    if (offset < 0 || offset >= data.size())
        return INITIAL_VALUE;
    if (length < 0 || offset + length > data.size())
        length = data.size() - offset;

    quint16 crc = INITIAL_VALUE;

    for (int i = offset; i < offset + length; ++i)
    {
        // 当前字节与 CRC 低字节异或（关键：无符号转换）
        crc ^= static_cast<quint8>(data.at(i));

        // 对 8 个 bit 逐位处理
        for (int j = 0; j < 8; ++j)
        {
            if (crc & 0x0001)
                crc = static_cast<quint16>((crc >> 1) ^ POLYNOMIAL);  // 最低位为1：右移异或多项式
            else
                crc = static_cast<quint16>(crc >> 1);                 // 最低位为0：仅右移
        }
    }

    return crc;
}

QByteArray CrcCalculator::toLittleEndianBytes(quint16 crc)
{
    QByteArray bytes;
    bytes.append(static_cast<char>(crc & 0xFF));         // 低字节在前
    bytes.append(static_cast<char>((crc >> 8) & 0xFF));  // 高字节在后
    return bytes;
}
