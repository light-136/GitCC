/**
 * @file byteutils.h
 * @brief 字节工具库（P2：C++ 现代特性筑基的载体）
 *
 * 为什么用这个类作为 P2 的代码载体：
 *   - 全部是纯函数（不碰 UI/IO），边界清晰，最适合练习
 *     const 引用入参、非 const 引用出参、RAII、命名空间；
 *   - 是后续 P10 协议引擎（帧解析/CRC）的公共依赖，先造地基；
 *   - 对应 C# 里的一个 static 工具类（如 BitConverter + Convert 的组合）。
 *
 * 教学点（阅读时注意）：
 *   1. `const QByteArray &data` —— const 引用入参：只读不复制，调用方数据不被修改；
 *   2. `quint16 &out` —— 非 const 引用出参：函数通过引用把结果"写回"给调用者；
 *   3. QByteArray / QString 是 RAII 类型：离开作用域自动释放，无需手动 delete。
 */

#pragma once

#include <QByteArray>
#include <QString>
#include <QtGlobal>

namespace datascope {
namespace utils {

/**
 * @class ByteUtils
 * @brief 字节与十六进制字符串互转、大小端读写工具
 *
 * 全部为静态方法（工具类不需要实例化，对应 C# 的 static class）。
 */
class ByteUtils
{
public:
    // ---------- 十六进制互转 ----------

    /**
     * @brief 字节数组 → 十六进制大写字符串
     * @param data      输入字节数组（const 引用，不拷贝）
     * @param separator 每字节之间插入的分隔符，默认空格，如 "AA BB 0F"
     * @return 十六进制字符串；空输入返回空串
     * @note 对应 C# 的 BitConverter.ToString(bytes).Replace("-", sep)
     */
    static QString toHexString(const QByteArray &data,
                               const QString &separator = QStringLiteral(" "));

    /**
     * @brief 十六进制字符串 → 字节数组（解析）
     * @param hex 形如 "AA BB 0F"、"aabb0f"、"0xAA,0xBB"（容忍空白与逗号，大小写不敏感）
     * @return 解析后的字节数组；遇到非法字符返回空数组（宁可返回空也不产出脏数据）
     * @note 对应 C# 的手写 hex 解析（.NET 没有现成反向转换）
     */
    static QByteArray fromHexString(const QString &hex);

    // ---------- 大小端读取（网络/工业协议常用大端） ----------

    /**
     * @brief 从字节数组大端序读取 16 位无符号整数
     * @param data   数据源（const 引用）
     * @param offset 起始字节下标（0 起）
     * @param out    出参引用：成功后写入读取值
     * @return 越界返回 false 且不修改 out；成功返回 true
     * @note 用"返回值报告成败 + 出参带结果"，对应 C# 的 int.TryParse 风格
     */
    static bool readUInt16BE(const QByteArray &data, int offset, quint16 &out);

    /**
     * @brief 从字节数组大端序读取 32 位无符号整数（语义同上）
     */
    static bool readUInt32BE(const QByteArray &data, int offset, quint32 &out);

    // ---------- 大小端写入（构造发送帧用） ----------

    /**
     * @brief 把 16 位无符号整数以大端序追加到字节数组末尾
     * @param out   目标数组（非 const 引用：函数会修改它）
     * @param value 要写入的值
     * @note 对应 C# 的 BinaryWriter.Write(ushort) + 大端字节序手动调整
     */
    static void appendUInt16BE(QByteArray &out, quint16 value);

    /**
     * @brief 把 32 位无符号整数以大端序追加到字节数组末尾（语义同上）
     */
    static void appendUInt32BE(QByteArray &out, quint32 value);
};

} // namespace utils
} // namespace datascope
