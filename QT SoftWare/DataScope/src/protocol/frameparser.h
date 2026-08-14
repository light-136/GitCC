/**
 * @file frameparser.h
 * @brief 协议流式解析器（P10：协议引擎 ProtocolEngine）
 *
 * 用途：把来自串口/网络等"源源不断的原始字节流"解析成一帧帧的协议帧。
 * 这是整个协议引擎的**核心难点**，必须正确处理三种真实链路现象：
 *   - 半包：一帧被拆成多段到达（如串口每 8 字节收一次）；
 *   - 粘包：一次到达多帧拼在一起；
 *   - 错位/垃圾：帧头前面混入了脏数据（链路上电噪声、上一条帧的残尾）。
 *
 * 设计：feed() 把新字节追加进内部缓冲，nextFrame() 每次尝试从缓冲头部
 * 取出一帧——"累积缓冲 + 按需提取"天然解决了半包与粘包，无需自己排队。
 *
 * 教学点（对照 C#）：
 *   - C# 里处理粘包半包通常维护一个 `List<byte> buffer` 并在 Receive 回调里拼接，
 *     这里用 QByteArray 实现完全相同的思路，但更简洁（自带 append/mid/remove）；
 *   - 状态机三态（找帧头 → 读头字段 → 收数据校验）在实现里以 while 循环体现，
 *     详见 frameparser.cpp 的状态注释。
 */

#pragma once

#include <QByteArray>
#include <QtGlobal>

#include "protocol/protocoltypes.h"

namespace datascope {
namespace protocol {

/**
 * @class FrameParser
 * @brief 流式帧解析器（半包/粘包/错位重同步）
 */
class FrameParser
{
public:
    /**
     * @brief 喂入一段原始字节流（追加到内部缓冲）
     * @param chunk 新收到的字节；允许任意长度（0 字节、1 字节、多帧均可）
     * @note feed 只做追加，不做解析；真正的解析发生在 nextFrame() 被调用时
     */
    void feed(const QByteArray &chunk);

    /**
     * @brief 尝试从缓冲中取出下一帧
     * @param out 出参：解析成功后写入帧内容
     * @return true=取到一帧（out.isValid 为 true）；
     *         false=缓冲内没有完整且校验通过的帧（此时 out 不被修改）
     *
     * @note 关键不变量：
     *   - 返回 false 时**不破坏 m_buffer**（半包时缓冲原样保留，等下次 feed 后再取）；
     *   - 返回 true 时该帧已从缓冲头部消费掉，可连续调用取多帧（粘包场景）。
     */
    bool nextFrame(ProtocolFrame &out);

    /**
     * @brief 当前缓冲区内未消费的字节数
     * @return m_buffer 大小（字节）
     * @note 可用于调试/水位监控：正常取完所有帧后应为 0；
     *       若持续增长说明链路可能有大量无法重同步的垃圾数据。
     */
    int bufferedBytes() const;

private:
    QByteArray m_buffer;   // 累积缓冲：尚未消费的原始字节
};

} // namespace protocol
} // namespace datascope
