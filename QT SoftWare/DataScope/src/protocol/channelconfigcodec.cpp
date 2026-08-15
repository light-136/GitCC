/**
 * @file channelconfigcodec.cpp
 * @brief 通道配置编解码实现
 *
 * 布局（与头文件契约一致）：
 *   载荷 = [count:1B] + N × [index:1B][name:16B UTF-8][unit:8B UTF-8]
 *                        [min:4B float 大端][max:4B float 大端]
 *   单通道 kChannelConfigWireSize = 33 字节 → 载荷总长可精确校验。
 */

#include "protocol/channelconfigcodec.h"
#include "utils/byteutils.h"

#include <cstring>

namespace datascope {
namespace protocol {

QByteArray ChannelConfigCodec::encode(const QVector<ChannelConfigInfo> &channels)
{
    // 通道数用 1 字节表示：0 个或 >255 个都视为编码失败（防御）
    if (channels.isEmpty() || channels.size() > 255)
        return QByteArray();

    QByteArray payload;
    payload.reserve(1 + channels.size() * kChannelConfigWireSize);
    payload.append(static_cast<char>(channels.size()));   // 通道数

    for (const ChannelConfigInfo &ch : channels)
        payload += encodeChannel(ch);

    return payload;
}

QByteArray ChannelConfigCodec::encodeChannel(const ChannelConfigInfo &ch)
{
    QByteArray out;
    out.reserve(kChannelConfigWireSize);

    // index：1 字节
    out.append(static_cast<char>(ch.index));

    // name / unit：UTF-8 定长，超长截断、不足补 0
    const QByteArray nameUtf8 = ch.name.toUtf8();
    out.append(nameUtf8.left(kChannelNameLen));
    out.append(QByteArray(kChannelNameLen - qMin(nameUtf8.size(), kChannelNameLen), char(0)));

    const QByteArray unitUtf8 = ch.unit.toUtf8();
    out.append(unitUtf8.left(kChannelUnitLen));
    out.append(QByteArray(kChannelUnitLen - qMin(unitUtf8.size(), kChannelUnitLen), char(0)));

    // min / max：float 大端 4 字节（位模式经 uint32 落盘，两端字节序约定一致）
    quint32 minBits = 0, maxBits = 0;
    std::memcpy(&minBits, &ch.rangeMin, sizeof(minBits));
    std::memcpy(&maxBits, &ch.rangeMax, sizeof(maxBits));
    utils::ByteUtils::appendUInt32BE(out, minBits);
    utils::ByteUtils::appendUInt32BE(out, maxBits);

    return out;
}

bool ChannelConfigCodec::decode(const QByteArray &payload, QVector<ChannelConfigInfo> &out)
{
    out.clear();

    // 长度精确校验：1 + 33×N，防止脏数据导致越界跳读
    if (payload.size() < 1)
        return false;
    const int count = static_cast<quint8>(payload.at(0));
    if (count < 1 || payload.size() != 1 + count * kChannelConfigWireSize)
        return false;

    for (int i = 0; i < count; ++i) {
        ChannelConfigInfo ch;
        if (!decodeChannel(payload, 1 + i * kChannelConfigWireSize, ch))
            return false;   // 中途失败：整体失败，out 已被 clear
        out.append(ch);
    }
    return true;
}

bool ChannelConfigCodec::decodeChannel(const QByteArray &payload, int offset, ChannelConfigInfo &out)
{
    // 越界防御（防御性：即使入口已校验整长，也逐字段检查）
    const int need = offset + kChannelConfigWireSize;
    if (need > payload.size())
        return false;

    out.index = static_cast<quint8>(payload.at(offset));

    // name：16 字节 UTF-8，去掉尾部补零后解码
    const QByteArray nameRaw = payload.mid(offset + 1, kChannelNameLen);
    const int nameEnd = nameRaw.indexOf(char(0));
    out.name = QString::fromUtf8(nameRaw.left(nameEnd < 0 ? kChannelNameLen : nameEnd));

    // unit：8 字节 UTF-8，同上
    const int unitStart = offset + 1 + kChannelNameLen;
    const QByteArray unitRaw = payload.mid(unitStart, kChannelUnitLen);
    const int unitEnd = unitRaw.indexOf(char(0));
    out.unit = QString::fromUtf8(unitRaw.left(unitEnd < 0 ? kChannelUnitLen : unitEnd));

    // min / max：大端 uint32 位模式还原 float
    quint32 minBits = 0, maxBits = 0;
    const int minStart = unitStart + kChannelUnitLen;
    if (!utils::ByteUtils::readUInt32BE(payload, minStart, minBits)
        || !utils::ByteUtils::readUInt32BE(payload, minStart + 4, maxBits))
        return false;
    std::memcpy(&out.rangeMin, &minBits, sizeof(minBits));
    std::memcpy(&out.rangeMax, &maxBits, sizeof(maxBits));

    return true;
}

} // namespace protocol
} // namespace datascope
