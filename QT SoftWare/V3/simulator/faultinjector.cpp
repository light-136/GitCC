/**
 * @file faultinjector.cpp
 * @brief 异常注入器实现（V3）
 *
 * 开发思路：
 *   1. inject() 是唯一入口：按 FaultType 分发到对应的字节变换，每种变换都
 *      是"输入一段正常帧 → 输出若干段字节"，无任何成员状态；
 *   2. 所有"改哪个字节"都固定（DATA 首字节、LEN 字段），不随机、不取时间，
 *      从而满足"同一输入同一输出"的可复现约束；
 *   3. 每个变换都做"帧过短"防御：无法注入时退化为原样返回，绝不越界访问。
 *
 * ── 帧格式偏移（与 frametypes.h 的 kHeaderLen=7 契约一致）──
 *   SOF(2) VERSION(1) FUNC(1) CMD(1) LEN(2 大端) DATA CRC16(2)
 *   LEN 在偏移 [5,6]；DATA 从偏移 7 起。
 */

#include "faultinjector.h"

#include "protocol/frametypes.h"

namespace dscope {
namespace simulator {

QVector<QByteArray> FaultInjector::inject(FaultType type, const QByteArray &normalFrame)
{
    switch (type) {
    case FaultType::None:
        // 无异常：原样返回，一次写一帧
        return { normalFrame };

    case FaultType::Sticky: {
        // 粘包：把一帧拆成两半，分两次发送——接收端必须缓存前半、等后半到齐再拼出完整帧
        const int mid = normalFrame.size() / 2;
        if (mid <= 0)
            return { normalFrame };   // 帧太短（<2 字节）无法拆：退化为原样（防御）
        return { normalFrame.left(mid), normalFrame.mid(mid) };
    }

    case FaultType::Fragment:
        // 分包：把"本帧 + 下一帧"拼成一段一次发送——接收端必须从一段里拆出两帧。
        // 无状态约束下，"下一帧"取本帧的副本（保证同一输入同一输出，纯函数可复现）
        return { normalFrame + normalFrame };

    case FaultType::CrcError:
        // 坏CRC：篡改 DATA 首字节——CRC 字段不变，接收端校验必然失败并拒绝整帧
        return { corruptDataByte(normalFrame) };

    case FaultType::InvalidFrame:
        // 非法帧：把 LEN 改为 0xFFFF（> kMaxPayloadLen）——接收端判非法帧头并丢弃
        return { corruptLenField(normalFrame) };

    case FaultType::GarbagePrefix:
        // 突变：在合法帧前塞入垃圾字节——接收端扫描帧头时必须跳过垃圾、重同步到 SOF
        return { garbageBytes() + normalFrame };
    }

    // 未知枚举值兜底：原样返回（防御，不抛错）
    return { normalFrame };
}

FaultType FaultInjector::nextType(FaultType current)
{
    switch (current) {
    case FaultType::Sticky:        return FaultType::Fragment;
    case FaultType::Fragment:      return FaultType::CrcError;
    case FaultType::CrcError:      return FaultType::InvalidFrame;
    case FaultType::InvalidFrame:  return FaultType::GarbagePrefix;
    case FaultType::GarbagePrefix: return FaultType::Sticky;
    default:                       return FaultType::Sticky;   // None/未知 → 从第一个开始
    }
}

const char *FaultInjector::typeName(FaultType type)
{
    switch (type) {
    case FaultType::None:          return "无异常";
    case FaultType::Sticky:        return "粘包（拆两半）";
    case FaultType::Fragment:      return "分包（两帧拼接）";
    case FaultType::CrcError:      return "坏CRC（篡改DATA字节）";
    case FaultType::InvalidFrame:  return "非法帧（篡改LEN字段）";
    case FaultType::GarbagePrefix: return "突变（垃圾前缀）";
    }
    return "未知异常";
}

QByteArray FaultInjector::corruptDataByte(const QByteArray &frame)
{
    // 需要至少 kHeaderLen+1 字节（帧头之后才有 DATA 可篡改）
    if (frame.size() < protocol::kHeaderLen + 1)
        return frame;   // 无 DATA 可篡改：原样返回（防御）

    QByteArray bad = frame;
    // DATA 首字节（帧头后第一个字节）按位取反：必然改变字节值 → CRC 必然失败
    const int dataOffset = protocol::kHeaderLen;
    bad[dataOffset] = static_cast<char>(static_cast<quint8>(bad.at(dataOffset)) ^ 0xFF);
    return bad;
}

QByteArray FaultInjector::corruptLenField(const QByteArray &frame)
{
    if (frame.size() < protocol::kHeaderLen)
        return frame;   // 连帧头都不完整：无法篡改（防御）

    QByteArray bad = frame;
    // LEN 字段在帧头偏移 5、6（大端），改为 0xFFFF：
    // 超过 kMaxPayloadLen(1024)，解析器直接判非法帧头（顺带防御"恶意超大长度"囤积）
    bad[5] = static_cast<char>(0xFF);   // LEN_HI
    bad[6] = static_cast<char>(0xFF);   // LEN_LO
    return bad;
}

QByteArray FaultInjector::garbageBytes()
{
    // 固定 3 字节垃圾前缀：中间一个"游离 0xAA"（后跟 0x00 而非 0x55）。
    // 选它是有意的：解析器扫帧头时会在中间遇到一个 0xAA，却找不到 0xAA55，
    // 必须把它当垃圾跳过、继续向后重同步到真正的 SOF——这正是重同步逻辑的核心考验。
    static const QByteArray bytes = QByteArray::fromHex("00AA00");
    return bytes;
}

} // namespace simulator
} // namespace dscope
