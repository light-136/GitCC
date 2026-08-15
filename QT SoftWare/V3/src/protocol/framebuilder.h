/**
 * @file framebuilder.h
 * @brief 协议帧构建器声明（把 func/cmd/载荷 组装成完整帧字节）
 *
 * 只依赖 QtCore（QByteArray / QtGlobal）。build() 自动填入 VERSION=0x01，
 * 并按 V3 契约补上 SOF、LEN（大端）与 CRC16（低字节在前）。
 *
 * ── WPF 对照 ──
 *   本类相当于 C# 里的 BinaryWriter 封装：把结构化字段按线格式逐个写出。
 *   `QByteArray` 相当于 C# 的 `byte[]`（可动态追加的字节序列）。
 */

#pragma once

#include <QByteArray>
#include <QtGlobal>

namespace dscope {
namespace protocol {

/**
 * @class FrameBuilder
 * @brief 帧构建器（发送侧：字段 → 线格式字节）
 *
 * 纯静态工具类，不需要实例化。
 * 用法：
 *   const QByteArray frame = FrameBuilder::build(kFuncQuery, kCmdQueryChannels, QByteArray());
 */
class FrameBuilder
{
public:
    /**
     * @brief 构建一帧完整协议帧
     * @param func    功能码（1 字节）
     * @param cmd     子命令（1 字节）
     * @param payload 载荷（DATA 段，长度必须 ≤ kMaxPayloadLen）
     * @return 完整帧字节；payload 超长时返回空 QByteArray 表示构建失败
     *
     * 帧布局：SOF(2) + VERSION(1) + FUNC(1) + CMD(1) + LEN(2 大端) + DATA + CRC(2 低字节在前)
     */
    static QByteArray build(quint8 func, quint8 cmd, const QByteArray &payload);
};

} // namespace protocol
} // namespace dscope
