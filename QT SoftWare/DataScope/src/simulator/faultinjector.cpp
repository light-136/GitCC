/**
 * @file faultinjector.cpp
 * @brief 异常注入器实现
 *
 * 帧格式（protocoltypes.h）：SOF(2) | FUNC(1) | CMD(1) | LEN(2 大端) | DATA(LEN) | CRC(2 低字节在前)
 * 偏移约定：帧头 6 字节；DATA 从下标 6 开始；CRC 在末尾 2 字节。
 */

#include "simulator/faultinjector.h"
#include "protocol/crc16.h"

#include <cstring>

namespace datascope {
namespace simulator {

namespace {
// 帧格式常量（与 protocoltypes.h 保持一致）
constexpr int kHeaderLen = 6;       ///< 帧头长度
constexpr int kCrcLen = 2;          ///< CRC 字段长度
constexpr int kFloatBytes = 4;      ///< 单通道 float 载荷字节数

/**
 * @brief float → 大端序 4 字节（协议契约规定载荷内 float 大端序）
 */
QByteArray floatToBigEndian(float value)
{
    quint32 bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));  // 取内存位模式
    QByteArray bytes(kFloatBytes, Qt::Uninitialized);
    bytes[0] = static_cast<char>((bits >> 24) & 0xFF);
    bytes[1] = static_cast<char>((bits >> 16) & 0xFF);
    bytes[2] = static_cast<char>((bits >> 8) & 0xFF);
    bytes[3] = static_cast<char>(bits & 0xFF);
    return bytes;
}
} // namespace

QVector<QByteArray> FaultInjector::process(int frameSeq, const QByteArray &normalFrame)
{
    // 各异常类型对应的"应发送字节流"
    switch (m_cfg.type) {
    case FaultType::None:
        // 无异常：原样返回，一次写一帧
        return { normalFrame };

    case FaultType::Sticky:
        // 粘包：把两帧拼成一个字节流一次发送（主机必须能拆开）
        return { normalFrame + normalFrame };

    case FaultType::Fragment: {
        // 分包：一帧拆成两段，分两次发送（主机必须能拼接完整帧）
        const int mid = normalFrame.size() / 2;
        return { normalFrame.left(mid), normalFrame.mid(mid) };
    }

    case FaultType::CrcError:
        // CRC 错误：每帧都返回篡改版本（帧尾字节 +1）
        return { corruptCrc(normalFrame) };

    case FaultType::IllegalFrame:
        // 非法帧：把长度字段改为超大值，解析器应判定非法帧头并拒绝
        return { corruptIllegal(normalFrame) };

    case FaultType::Delay:
        // 延迟：到达触发周期的那一帧"不发"（模拟数据中断/超时）
        return shouldTrigger(frameSeq) ? QVector<QByteArray>() : QVector<QByteArray>{ normalFrame };

    case FaultType::Surge:
        // 突变：到达触发周期时把指定通道值改为超量程值（触发主机报警）
        return shouldTrigger(frameSeq)
            ? QVector<QByteArray>{ surgeChannel(normalFrame) }
            : QVector<QByteArray>{ normalFrame };

    case FaultType::Disconnect:
        // 断线不是字节变换：这里约定"本次不发送"，真正的断开动作由
        // SimulatorDevice 在设备层执行（disconnectFromHost）。
        return QVector<QByteArray>();
    }

    // 未知类型兜底：原样返回（防御性）
    return { normalFrame };
}

QByteArray FaultInjector::corruptCrc(const QByteArray &frame) const
{
    if (frame.size() < kHeaderLen + kCrcLen)
        return frame;   // 太短无法篡改：原样返回（防御）

    QByteArray bad = frame;
    // 帧尾最后一个字节 +1（模 256）：CRC 校验必然失败
    const int last = bad.size() - 1;
    bad[last] = static_cast<char>((static_cast<quint8>(bad.at(last)) + 1) & 0xFF);
    return bad;
}

QByteArray FaultInjector::corruptIllegal(const QByteArray &frame) const
{
    if (frame.size() < kHeaderLen)
        return frame;

    QByteArray bad = frame;
    // 把 LEN 字段（下标 4-5，大端）改为 0xFFFF：超过 kMaxPayloadLen(1024)，
    // 解析器会判定为非法帧头（防御性拒绝超大长度）
    bad[4] = static_cast<char>(0xFF);
    bad[5] = static_cast<char>(0xFF);
    return bad;
}

QByteArray FaultInjector::surgeChannel(const QByteArray &frame) const
{
    // 突变：把载荷中指定通道的 4 字节 float 替换为 surgeValue
    // 载荷从下标 6 开始，通道 c 的数据位于 [6 + c*4, 6 + c*4 + 4)
    const int ch = m_cfg.channelIndex;
    const int offset = kHeaderLen + ch * kFloatBytes;
    if (offset + kFloatBytes > frame.size())
        return frame;   // 通道越界：无法突变，原样返回（防御）

    QByteArray surged = frame;
    const QByteArray bigEndian = floatToBigEndian(static_cast<float>(m_cfg.surgeValue));
    std::memcpy(surged.data() + offset, bigEndian.constData(), kFloatBytes);

    // 数据突变语义：设备"活着"，只是采集值跳变 —— 帧必须仍然合法。
    // 若直接返回，CRC 会因载荷被改而校验失败，主机解析器会把整帧当坏帧丢弃，
    // 报警引擎将收不到这个超量程值，"突变 → 报警"链路就断了。
    // 因此替换字节后需重算 CRC 字段（低字节在前），与 FrameBuilder 的约定一致。
    const QByteArray crcInput = surged.mid(2, surged.size() - 2 - kCrcLen);
    const quint16 crc = protocol::Crc16::compute(crcInput);
    const int crcLo = surged.size() - 2;   // 帧尾倒数第 2 字节 = CRC_L
    surged[crcLo] = static_cast<char>(crc & 0xFF);
    surged[crcLo + 1] = static_cast<char>((crc >> 8) & 0xFF);
    return surged;
}

} // namespace simulator
} // namespace datascope
