/**
 * @file crc16.cpp
 * @brief MODBUS CRC16 实现（反射表驱动）
 *
 * 算法说明（照搬 V2，仅改命名空间）：
 *   - 初始值 0xFFFF；
 *   - 多项式 0xA001（0x8005 的反转形式，MODBUS 采用 LSB 先出的小端位移方向）；
 *   - 查找表：256 项，每项代表"一个字节经过 8 次多项式异或后的余数"，
 *     运行时每输入一个字节，只需 1 次查表 + 1 次异或 + 1 次移位，
 *     比逐位循环快约 8 倍。
 *
 * 教学点（对照 C#）：
 *   - 查找表用"函数局部 static + 首次调用时构造"生成，
 *     等价于 C# 的 `static readonly byte[]`（Lazy 初始化）；
 *   - 用结构体把表包起来再返回 const 引用，避免整表拷贝；
 *   - `static` 局部对象由编译器保证只初始化一次且线程安全（C++11 magic static）。
 */

#include "protocol/crc16.h"

namespace dscope {
namespace protocol {

namespace {
// ---------------------------------------------------------------------------
// MODBUS CRC16 反射查找表（私有实现细节，不对外暴露）
// ---------------------------------------------------------------------------

/**
 * @brief 查表容器：构造函数里一次性把 256 项表算好
 */
struct Crc16Table
{
    quint16 values[256];

    Crc16Table()
    {
        // 对每一个字节 i，让它按"先异或、再右移、遇到低位为 1 就异或多项式"走 8 步，
        // 得到的就是该字节作为新输入时 CRC 高字节异或项的查表结果。
        for (int i = 0; i < 256; ++i) {
            quint16 crc = static_cast<quint16>(i);
            for (int bit = 0; bit < 8; ++bit) {
                if (crc & 0x0001)
                    crc = static_cast<quint16>((crc >> 1) ^ 0xA001);
                else
                    crc = static_cast<quint16>(crc >> 1);
            }
            values[i] = crc;
        }
    }
};

/**
 * @brief 获取查找表（首次调用时构造一次，此后复用）
 */
const Crc16Table &crc16Table()
{
    static const Crc16Table table;
    return table;
}
} // namespace

quint16 Crc16::compute(const QByteArray &data)
{
    const Crc16Table &table = crc16Table();

    // MODBUS 规定初始余数为 0xFFFF
    quint16 crc = 0xFFFF;

    for (int i = 0; i < data.size(); ++i) {
        const quint8 byte = static_cast<quint8>(data.at(i));
        // 表驱动核心：查表输入 = (当前 CRC 低 8 位) ^ (新输入字节)
        // 查到的 16 位值再异或到 (CRC 右移 8 位) 上，即完成本字节的迭代
        crc = static_cast<quint16>((crc >> 8) ^ table.values[(crc ^ byte) & 0xFF]);
    }

    return crc;
}

} // namespace protocol
} // namespace dscope
