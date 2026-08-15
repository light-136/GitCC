/**
 * @file frametypes.h
 * @brief V3 协议层基础类型与常量（header-only，仅依赖 QtCore）
 *
 * ────────────────────────────────────────────────────────────
 * 本文件是 protocol 静态库的"契约根基"，所有帧编解码类共享它。
 * 只依赖 QtCore（QByteArray / QtGlobal），禁止 include domain / Widgets / Gui / Network。
 * ────────────────────────────────────────────────────────────
 *
 * V3 帧格式（与 V2 的唯一关键差异：帧头多 1 字节 VERSION，CRC 覆盖范围从 VERSION 起）：
 *
 *   | SOF(2B: 0xAA 0x55) | VERSION(1B: 0x01) | FUNC(1B) | CMD(1B)
 *   | LEN(2B 大端) | DATA(LEN) | CRC16(2B 低字节在前) |
 *
 *   - kHeaderLen = 7（SOF2 + VERSION1 + FUNC1 + CMD1 + LEN2）
 *   - CRC 覆盖：VERSION 起（即 SOF 之后所有字节 = VERSION+FUNC+CMD+LEN+DATA）
 *   - LEN 大端；CRC 低字节在前（MODBUS 惯例）
 *
 * ── WPF 对照 ──
 *   本文件相当于 C# 里的一个"协议常量类"：
 *     public static class Protocol { public const ushort Sof = 0xAA55; ... }
 *   ParsedFrame 相当于 C# 的 record/PODO（Plain Old Data Object），只做数据容器。
 */

#pragma once

#include <QByteArray>
#include <QtGlobal>

namespace dscope {
namespace protocol {

// ---------------------------------------------------------------------------
// 帧结构常量（编译期确定，零运行时开销）
// ---------------------------------------------------------------------------

/** @brief 帧头 SOF：两字节 0xAA 0x55（kSof = 0xAA55，高字节 0xAA 在前） */
constexpr quint16 kSof = 0xAA55;

/** @brief 协议版本号：当前固定 0x01（帧内 VERSION 字段） */
constexpr quint8 kProtocolVersion = 0x01;

/** @brief 载荷最大长度（字节），超过即判为非法帧头（越界防御） */
constexpr int kMaxPayloadLen = 1024;

/** @brief 帧头长度：SOF(2) + VERSION(1) + FUNC(1) + CMD(1) + LEN(2) = 7 */
constexpr int kHeaderLen = 7;

/** @brief CRC 字段长度（2 字节，低字节在前） */
constexpr int kCrcLen = 2;

// ---------------------------------------------------------------------------
// FUNC 功能码
// ---------------------------------------------------------------------------

/** @brief 数据功能码（实时采样数据帧） */
constexpr quint8 kFuncData = 0x01;

/** @brief 配置功能码（配置下发帧） */
constexpr quint8 kFuncConfig = 0x02;

/** @brief 查询功能码（主机 → 设备的查询请求） */
constexpr quint8 kFuncQuery = 0x03;

/** @brief 响应标志掩码：响应帧 = 请求 FUNC | 0x80（最高位置 1） */
constexpr quint8 kFuncResponseMask = 0x80;

/** @brief 查询响应功能码（kFuncQuery | kFuncResponseMask = 0x03 | 0x80 = 0x83） */
constexpr quint8 kFuncQueryResponse = 0x83;

// ---------------------------------------------------------------------------
// CMD 子命令
// ---------------------------------------------------------------------------

/** @brief 子命令：查询通道配置（配合 kFuncQuery / kFuncQueryResponse 使用） */
constexpr quint8 kCmdQueryChannels = 0x01;

/**
 * @struct ParsedFrame
 * @brief 解析器产出的完整帧（纯数据容器，无逻辑）
 *
 * 由 FrameParser::next() 在解析成功时写入。字段含义：
 *   - version：帧内 VERSION 字段（恒为 kProtocolVersion 才可能通过解析）
 *   - func / cmd：功能码 / 子命令
 *   - payload：DATA 段字节（LEN 字节，已剥离帧头与 CRC）
 */
struct ParsedFrame {
    quint8 version = 0;    ///< 协议版本号
    quint8 func = 0;       ///< 功能码
    quint8 cmd = 0;        ///< 子命令
    QByteArray payload;    ///< 载荷字节
};

/**
 * @enum FrameError
 * @brief 帧解析错误分类（用于 FrameParser::next() 的 Error 出参）
 *
 * ── WPF 对照 ──
 *   相当于 C# 的 enum FrameError { CrcError, InvalidHeader }。
 */
enum class FrameError {
    CrcError,       ///< CRC16 校验失败（帧字节已损坏）
    InvalidHeader   ///< 非法帧头（LEN 超限，或 VERSION 与 kProtocolVersion 不符）
};

} // namespace protocol
} // namespace dscope
