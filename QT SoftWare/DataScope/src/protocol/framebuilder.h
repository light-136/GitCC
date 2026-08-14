/**
 * @file framebuilder.h
 * @brief 协议组帧器（P10：协议引擎 ProtocolEngine）
 *
 * 用途：把"功能码 + 子命令 + 载荷"按契约拼成一条完整的发送帧字节流。
 * 与解析器（FrameParser）严格互逆：build 的产物能直接被 nextFrame 解析回来。
 *
 * 帧布局（严格按《03-通信协议帧格式契约.md》）：
 *   | SOF(0xAA 0x55) | FUNC | CMD | LEN(2B 大端) | DATA | CRC16(2B 低字节在前) |
 *
 * 教学点（对照 C#）：
 *   - 对应 C# 里 `MemoryStream + BinaryWriter` 的手动组帧；
 *     C# 默认小端，这里要手动把 LEN 写成大端（高位在前），
 *     CRC 则按 MODBUS 惯例"低字节在前"追加；
 *   - 返回 QByteArray 而非 void：让调用方能拿到"组装好的完整字节流"直接交给串口发送。
 */

#pragma once

#include <QByteArray>
#include <QtGlobal>

namespace datascope {
namespace protocol {

/**
 * @class FrameBuilder
 * @brief 组帧器：把协议字段构造成完整帧字节流
 */
class FrameBuilder
{
public:
    /**
     * @brief 构造一帧
     * @param func    功能码（0x01/0x02/0x03/0x81...）
     * @param cmd     子命令
     * @param payload 载荷数据（可为空）
     * @return 完整帧字节流；载荷超过 kMaxPayloadLen 时返回空数组表示组帧失败
     *
     * @note 返回空数组（isEmpty()==true）是唯一的失败信号——
     *       即使空载荷，成功组帧也能得到 6+0+2=8 字节的非空结果。
     */
    static QByteArray build(quint8 func, quint8 cmd, const QByteArray &payload);
};

} // namespace protocol
} // namespace datascope
