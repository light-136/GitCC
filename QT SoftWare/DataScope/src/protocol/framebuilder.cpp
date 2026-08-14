/**
 * @file framebuilder.cpp
 * @brief 协议组帧器实现
 *
 * 组帧过程（对应契约字节序）：
 *   1. 写帧头 SOF：0xAA 0x55（kSof 高字节在前）；
 *   2. 写 FUNC / CMD 各 1 字节；
 *   3. 写 LEN：载荷长度，**大端序**（高字节在前）；
 *   4. 追加 DATA；
 *   5. 计算 CRC16（覆盖 FUNC 起到 DATA 末尾，即跳过 SOF 的所有字节），
 *      按 MODBUS 惯例**低字节在前**追加。
 *
 * 教学点（对照 C#）：
 *   - `reserve()` 预分配容量，对应 C# `List<byte>` / `MemoryStream` 的 Capacity 预分配，
 *     避免反复扩容拷贝；
 *   - 大端写 16 位：`(v >> 8) & 0xFF` 取高字节、`v & 0xFF` 取低字节，与 C# 手动组帧写法一致。
 */

#include "protocol/framebuilder.h"
#include "protocol/protocoltypes.h"
#include "protocol/crc16.h"

namespace datascope {
namespace protocol {

QByteArray FrameBuilder::build(quint8 func, quint8 cmd, const QByteArray &payload)
{
    // 载荷超限防御：超过 kMaxPayloadLen 视为非法输入，返回空数组表示失败
    // （不截断、不吞掉，让调用方明确知道这帧没组成功）
    if (payload.size() > kMaxPayloadLen)
        return QByteArray();

    QByteArray frame;
    // 预分配整帧容量：帧头 6 + 载荷 + CRC 2，减少扩容
    frame.reserve(kHeaderLen + payload.size() + kCrcLen);

    // ---- 1) 帧头 SOF：0xAA 0x55（kSof 的高字节 0xAA 在前）----
    frame.append(static_cast<char>((kSof >> 8) & 0xFF));
    frame.append(static_cast<char>(kSof & 0xFF));

    // ---- 2) FUNC / CMD ----
    frame.append(static_cast<char>(func));
    frame.append(static_cast<char>(cmd));

    // ---- 3) LEN：大端序（高字节在前）----
    frame.append(static_cast<char>((payload.size() >> 8) & 0xFF));
    frame.append(static_cast<char>(payload.size() & 0xFF));

    // ---- 4) DATA ----
    frame.append(payload);

    // ---- 5) CRC16：覆盖 FUNC 起到 DATA 末尾（即 frame 跳过 SOF 的全部字节）----
    //     参与字节 = FUNC(1) + CMD(1) + LEN(2) + DATA(len)；此时 CRC 尚未追加，
    //     所以 mid(2) 恰好取到 FUNC..DATA 末。CRC 低字节在前追加。
    const QByteArray crcInput = frame.mid(2);               // FUNC + CMD + LEN + DATA
    const quint16 crc = Crc16::compute(crcInput);
    frame.append(static_cast<char>(crc & 0xFF));            // CRC_L（低字节在前）
    frame.append(static_cast<char>((crc >> 8) & 0xFF));     // CRC_H

    return frame;
}

} // namespace protocol
} // namespace datascope
