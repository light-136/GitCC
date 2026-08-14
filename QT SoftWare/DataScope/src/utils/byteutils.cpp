/**
 * @file byteutils.cpp
 * @brief 字节工具库实现
 *
 * P2 重点：这里刻意使用了多种 C++ 现代写法，阅读时对照注释理解：
 *   - 范围 for（range-based for）遍历字符串；
 *   - 栈对象（QByteArray/局部变量）RAII 自动释放；
 *   - 引用传参避免无谓拷贝；
 *   - QByteArray 的 reserve() 预分配（类似 C# List 的 Capacity）。
 */

#include "utils/byteutils.h"

namespace datascope {
namespace utils {

namespace {
// 文件内私有常量（匿名命名空间 = 该 .cpp 文件内的"内部静态"，类似 C# 的 private const）
const char kHexDigits[] = "0123456789ABCDEF";
} // namespace

QString ByteUtils::toHexString(const QByteArray &data, const QString &separator)
{
    if (data.isEmpty())
        return QString();

    // 预分配容量：每字节 2 个 hex 字符 + 1 个分隔符，避免多次扩容
    // 这对应 C# 里 StringBuilder 的 Capacity 预分配思路
    QString result;
    result.reserve(data.size() * 3);

    for (int i = 0; i < data.size(); ++i) {
        if (i > 0)
            result += separator;
        // 把字节拆成高 4 位 / 低 4 位，查表得到 hex 字符
        const uchar byte = static_cast<uchar>(data.at(i));
        result += QLatin1Char(kHexDigits[byte >> 4]);
        result += QLatin1Char(kHexDigits[byte & 0x0F]);
    }
    return result;
}

QByteArray ByteUtils::fromHexString(const QString &hex)
{
    if (hex.isEmpty())
        return QByteArray();

    QByteArray result;
    // 最坏情况：每 2 个 hex 字符出 1 字节，预留空间减少扩容
    result.reserve(hex.size() / 2);

    // 用一个手工状态机扫描：把空格/逗号/0x 前缀全部容忍掉，
    // 遇到"两两成对"的 hex 数字才组成一个字节
    bool haveHigh = false;      // 是否已捕获一个字节的高 4 位
    quint8 currentByte = 0;     // 正在拼接的字节

    for (int i = 0; i < hex.size(); ++i) {
        const QChar c = hex.at(i);
        int digit = -1;
        if (c >= QLatin1Char('0') && c <= QLatin1Char('9'))
            digit = c.unicode() - QLatin1Char('0').unicode();
        else if (c >= QLatin1Char('A') && c <= QLatin1Char('F'))
            digit = c.unicode() - QLatin1Char('A').unicode() + 10;
        else if (c >= QLatin1Char('a') && c <= QLatin1Char('f'))
            digit = c.unicode() - QLatin1Char('a').unicode() + 10;

        if (digit >= 0) {
            // 是有效的 hex 数字：先到的作高 4 位，后到的作低 4 位
            if (!haveHigh) {
                currentByte = static_cast<quint8>(digit << 4);
                haveHigh = true;
            } else {
                currentByte |= static_cast<quint8>(digit & 0x0F);
                result.append(static_cast<char>(currentByte));
                haveHigh = false;
            }
        } else if ((c == QLatin1Char('x') || c == QLatin1Char('X'))
                   && haveHigh && currentByte == 0) {
            // 宽容解析 "0x" 前缀：撤销被误当作高半字节的 '0'。
            // 若不撤销，"0xAA" 会被拼成 0x0A（'0'=高4位 + 'A'=低4位），显然错误。
            haveHigh = false;
        }
        // 其余非法字符（空格、逗号等）直接跳过：宽容解析
    }

    // 若末尾只凑到半个字节，说明输入不合法，返回空数组表示失败
    if (haveHigh)
        return QByteArray();
    return result;
}

bool ByteUtils::readUInt16BE(const QByteArray &data, int offset, quint16 &out)
{
    // 越界检查：需要连续 2 字节
    if (offset < 0 || offset + 1 >= data.size())
        return false;

    const uchar hi = static_cast<uchar>(data.at(offset));
    const uchar lo = static_cast<uchar>(data.at(offset + 1));
    out = static_cast<quint16>((quint16(hi) << 8) | quint16(lo));
    return true;
}

bool ByteUtils::readUInt32BE(const QByteArray &data, int offset, quint32 &out)
{
    if (offset < 0 || offset + 3 >= data.size())
        return false;

    quint32 value = 0;
    for (int i = 0; i < 4; ++i) {
        value = (value << 8) | static_cast<uchar>(data.at(offset + i));
    }
    out = value;
    return true;
}

void ByteUtils::appendUInt16BE(QByteArray &out, quint16 value)
{
    // 大端序：高位字节在前
    out.append(static_cast<char>((value >> 8) & 0xFF));
    out.append(static_cast<char>(value & 0xFF));
}

void ByteUtils::appendUInt32BE(QByteArray &out, quint32 value)
{
    for (int i = 3; i >= 0; --i) {
        out.append(static_cast<char>((value >> (8 * i)) & 0xFF));
    }
}

} // namespace utils
} // namespace datascope
