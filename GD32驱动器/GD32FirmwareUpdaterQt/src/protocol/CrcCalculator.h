// ============================================================
//  CRC16 校验计算器 — CRC16/Modbus 标准
//
//  【Qt知识点】static 方法工具类：
//  C# 用 static class，C++ 用 class + 全静态方法（构造函数私有）。
//  本类不依赖 QObject —— 无信号槽需求，保持最轻量。
//
//  算法说明（与 WPF 版 CrcCalculator.cs 完全一致）：
//  1. CRC 初始值 = 0xFFFF
//  2. 逐字节与 CRC 异或
//  3. 对每一位进行移位 + 多项式判断
//  4. 输出 2 字节校验值（小端存储）
// ============================================================
#ifndef CRCCALCULATOR_H
#define CRCCALCULATOR_H

#include <QByteArray>
#include <QtGlobal>

class CrcCalculator
{
public:
    // 禁止实例化（纯工具类）
    CrcCalculator() = delete;

    /// <summary>
    /// 计算整段数据的 CRC16 校验值
    /// </summary>
    /// <param name="data">待校验数据</param>
    /// <returns>CRC16 校验值（无符号 16 位）</returns>
    static quint16 calculate(const QByteArray &data);

    /// <summary>
    /// 计算指定范围数据的 CRC16 校验值
    /// </summary>
    /// <param name="data">数据</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="length">计算长度（-1 表示到末尾）</param>
    static quint16 calculate(const QByteArray &data, int offset, int length);

    /// <summary>
    /// 将 CRC 值转为小端 2 字节（低字节在前）
    /// </summary>
    static QByteArray toLittleEndianBytes(quint16 crc);

private:
    // CRC16/Modbus 多项式（反向形式）
    static const quint16 POLYNOMIAL = 0xA001;

    // CRC 初始值
    static const quint16 INITIAL_VALUE = 0xFFFF;
};

#endif // CRCCALCULATOR_H
