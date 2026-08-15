/**
 * @file crc16.h
 * @brief MODBUS CRC16 校验工具声明（查表法）
 *
 * 只依赖 QtCore（QByteArray / QtGlobal）。算法逻辑照搬 V2 的 crc16.cpp，
 * 仅把命名空间从 datascope::protocol 改为 dscope::protocol。
 *
 * ── WPF 对照 ──
 *   本类相当于 C# 里的一个 static class（全部静态方法、无实例状态），
 *   对应 `public static class Crc16 { public static ushort Compute(byte[] data); }`。
 */

#pragma once

#include <QByteArray>
#include <QtGlobal>

namespace dscope {
namespace protocol {

/**
 * @class Crc16
 * @brief MODBUS CRC16 校验（初值 0xFFFF，多项式 0xA001，查表法）
 *
 * 纯静态工具类，不需要实例化。计算 16 位无符号 CRC，结果不附加字节序
 * （低字节在前的排列由调用方 FrameBuilder / FrameParser 负责）。
 */
class Crc16
{
public:
    /**
     * @brief 计算字节数组的 MODBUS CRC16
     * @param data 输入字节（const 引用，不拷贝、不修改）
     * @return 16 位 CRC 余数
     */
    static quint16 compute(const QByteArray &data);
};

} // namespace protocol
} // namespace dscope
