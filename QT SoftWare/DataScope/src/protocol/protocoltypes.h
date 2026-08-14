/**
 * @file protocoltypes.h
 * @brief 协议引擎公共类型与帧常量（P10：协议引擎 ProtocolEngine）
 *
 * 用途：DataScope Studio 与采集设备之间的字节级通信帧定义。
 * 本文件是整个协议模块的"地基"——组帧器（FrameBuilder）、解析器（FrameParser）、
 * 校验（Crc16）以及后续串口/模拟设备（P11）都依赖这里定义的帧结构与常量。
 *
 * 帧格式（严格对应《03-通信协议帧格式契约.md》）：
 *   | SOF(2B) | FUNC(1B) | CMD(1B) | LEN(2B 大端) | DATA(LEN) | CRC16(2B 低字节在前) |
 *
 * 教学点（对照 C#）：
 *   1. `constexpr` —— 编译期常量，等价于 C# 的 `const`；
 *      相比 C# `const` 只支持基本类型，C++ `constexpr` 还能用于函数与更复杂表达式；
 *   2. 结构体用"默认成员初始化器"（`= 0` / `= false`），
 *      对应 C# 里给属性设默认值，避免"未初始化"的脏数据；
 *   3. 载荷用 QByteArray（RAII 容器），对应 C# 的 byte[]，但自动管理内存、带长度。
 */

#pragma once

#include <QByteArray>
#include <QtGlobal>

namespace datascope {
namespace protocol {

// ===================== 帧常量 =====================

/** @brief 帧头（SOF），固定 0xAA 0x55，用于接收端重同步 */
constexpr quint16 kSof = 0xAA55;

/**
 * @brief 载荷（DATA）长度上限
 *
 * 防御性设计：解析时 LEN 字段声称的长度若超过此值，直接判定为非法帧头。
 * 目的是防止恶意/损坏数据里 LEN=0xFFFF 之类超大值导致解析器无限等待、
 * 缓冲区被撑爆（内存攻击）。此处 1024 远小于协议能表达的 65535。
 */
constexpr int kMaxPayloadLen = 1024;

/** @brief 帧头长度：SOF(2) + FUNC(1) + CMD(1) + LEN(2) = 6 字节 */
constexpr int kHeaderLen = 6;

/** @brief CRC16 字段长度：2 字节（低字节在前） */
constexpr int kCrcLen = 2;

// ===================== 帧结构 =====================

/**
 * @struct ProtocolFrame
 * @brief 一帧协议数据的解析结果
 *
 * 对应 C# 里解析网络包时常用的"结果对象"：
 * 把字节流中拆出来的字段装进一个强类型结构，业务层只跟结构体打交道，
 * 不用再手动抠字节（对应 BinaryReader 手动解析的产物）。
 */
struct ProtocolFrame {
    /** @brief 功能码原始值（0x01 采集 / 0x02 配置 / 0x03 查询；0x80 表示响应帧，如 0x81） */
    quint8 func = 0;

    /** @brief 子命令（各功能码内部自行定义含义） */
    quint8 cmd = 0;

    /** @brief 载荷数据（可为空，对应 LEN=0 的帧） */
    QByteArray payload;

    /**
     * @brief 是否校验通过
     *
     * 约定：FrameParser 只在 CRC16 校验通过时才产出帧（isValid=true）；
     * 校验失败的帧会被直接丢弃、不产出。保留该字段是为了语义完整，
     * 也便于其它模块（如模拟设备直接构造帧）使用。
     */
    bool isValid = false;
};

} // namespace protocol
} // namespace datascope
