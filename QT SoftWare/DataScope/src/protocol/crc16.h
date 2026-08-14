/**
 * @file crc16.h
 * @brief MODBUS CRC16 校验工具（P10：协议引擎 ProtocolEngine）
 *
 * 用途：为通信帧计算/校验 CRC16。
 * 协议约定（《03-通信协议帧格式契约.md》）：
 *   - 算法：MODBUS CRC16，初始值 0xFFFF，多项式 0xA001（反转表驱动）；
 *   - 输入：FUNC 起到 DATA 末尾的所有字节；
 *   - 追加顺序：低字节在前（先 CRC_L 再 CRC_H）。
 *
 * 教学点（对照 C#）：
 *   - 这是"静态工具类"，对应 C# 里的 `public static class`，
 *     不需要实例化，直接 `Crc16::compute(...)` 调用；
 *   - C# 一般用查表法（byte 表）提速；这里同样用 256 项反射表驱动。
 */

#pragma once

#include <QByteArray>
#include <QtGlobal>

namespace datascope {
namespace protocol {

/**
 * @class Crc16
 * @brief MODBUS CRC16 计算工具（全部为静态方法）
 */
class Crc16
{
public:
    /**
     * @brief 计算 MODBUS CRC16
     * @param data 参与校验的字节序列：FUNC 起到 DATA 末尾的所有字节
     *             （即 FUNC(1) + CMD(1) + LEN(2) + DATA(len)，LEN 也参与校验）
     * @return 16 位 CRC 值
     * @note 返回的是整数，调用方负责按"低字节在前"拆成两个字节追加到帧尾
     */
    static quint16 compute(const QByteArray &data);
};

} // namespace protocol
} // namespace datascope
