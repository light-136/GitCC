/**
 * @file framebuilder.cpp
 * @brief 协议帧构建器实现
 *
 * 开发思路：
 *   1. 先做载荷长度防御（payload > kMaxPayloadLen 直接失败，返回空数组）；
 *   2. 预留整帧容量（reserve），避免多次 append 触发重分配；
 *   3. 按固定顺序逐字段写入：SOF → VERSION → FUNC → CMD → LEN(大端) → DATA；
 *   4. CRC 覆盖 SOF 之后的全部字节（frame.mid(2)），结果低字节在前追加到帧尾。
 *
 * ── WPF 对照 ──
 *   `frame.append(char)` 相当于 C# `List<byte>.Add()` 或 `BinaryWriter.Write(byte)`；
 *   `(value >> 8) & 0xFF` 相当于 C# 的 `(byte)(value >> 8)`——显式掩码避免符号扩展。
 */

#include "protocol/framebuilder.h"
#include "protocol/crc16.h"
#include "protocol/frametypes.h"

namespace dscope {
namespace protocol {

QByteArray FrameBuilder::build(quint8 func, quint8 cmd, const QByteArray &payload)
{
    // ---- 越界防御：载荷超长 → 构建失败 ----
    // 返回空数组是"失败信号"（成功帧至少 9 字节：SOF2+VERSION1+FUNC1+CMD1+LEN2+CRC2），
    // 调用方据此判断构建是否成功，避免把非法帧发到链路上。
    if (payload.size() > kMaxPayloadLen)
        return QByteArray();

    QByteArray frame;
    frame.reserve(kHeaderLen + payload.size() + kCrcLen);

    // ---- SOF：0xAA 0x55（kSof 高字节在前）----
    frame.append(static_cast<char>(0xAA));
    frame.append(static_cast<char>(0x55));

    // ---- VERSION：固定协议版本号 ----
    frame.append(static_cast<char>(kProtocolVersion));

    // ---- FUNC / CMD ----
    frame.append(static_cast<char>(func));
    frame.append(static_cast<char>(cmd));

    // ---- LEN：2 字节大端（高字节在前）----
    const quint16 len = static_cast<quint16>(payload.size());
    frame.append(static_cast<char>((len >> 8) & 0xFF)); // LEN_HI
    frame.append(static_cast<char>(len & 0xFF));        // LEN_LO

    // ---- DATA ----
    frame.append(payload);

    // ---- CRC16：覆盖 SOF 之后的全部字节（frame.mid(2) = VERSION 起）----
    // 此时 frame 尚未含 CRC 字段，mid(2) 正好是 VERSION+FUNC+CMD+LEN+DATA。
    const quint16 crc = Crc16::compute(frame.mid(2));

    // ---- CRC16 低字节在前：先写 CRC_L，再写 CRC_H ----
    frame.append(static_cast<char>(crc & 0xFF));        // CRC_L
    frame.append(static_cast<char>((crc >> 8) & 0xFF)); // CRC_H

    return frame;
}

} // namespace protocol
} // namespace dscope
