/**
 * @file frameparser.h
 * @brief 协议流式解析器声明（三态状态机 + 读游标）
 *
 * 只依赖 QtCore（QByteArray / QtGlobal）。负责把任意字节流（半包/粘包/含垃圾）
 * 切分成一帧帧完整的 ParsedFrame，并对每帧做 VERSION 校验 + CRC16 校验。
 *
 * ── 读游标（去 O(n)）──
 *   V2 用 m_buffer.remove(0, n) 消费帧头/垃圾，每次 remove 都要把剩余字节前移，
 *   长缓冲下是 O(n)。V3 改为读游标 m_readPos：消费只移动游标（O(1)），
 *   游标超过阈值时一次性压缩（摊销 O(1)），见 compactIfNeeded()。
 *
 * ── WPF 对照 ──
 *   feed() 相当于 C# 里向 List<byte> 缓冲追加 socket 收到的字节；
 *   m_readPos 相当于 C# 中"已消费偏移"整数游标（避免反复 RemoveAt(0) 造成拷贝）；
 *   next() 三态返回值相当于 C# 的状态机枚举（TryParse 风格）：
 *     Status.Frame        → 成功取出一帧（对应 bool 返回 true）
 *     Status.Error        → 取出一帧但校验失败（对应 TryParse 返回 false + error 出参）
 *     Status.NeedMoreData → 数据不足，需继续 feed（对应"等更多字节"）
 */

#pragma once

#include <QByteArray>
#include <QtGlobal>

#include "protocol/frametypes.h"

namespace dscope {
namespace protocol {

/**
 * @class FrameParser
 * @brief 帧解析器（接收侧：字节流 → ParsedFrame）
 *
 * 用法：
 *   FrameParser parser;
 *   parser.feed(chunk);                        // 每收到一段字节就喂进去
 *   ParsedFrame frame; FrameError err;
 *   switch (parser.next(frame, err)) { ... }   // 反复调用取出帧
 *
 * 设计要点（半包/粘包）：
 *   - feed() 只追加、next() 只消费，二者解耦，天然支持半包（数据不足返回
 *     NeedMoreData，未消费数据保留）与粘包（一次 feed 多帧，反复 next 逐帧取出）。
 */
class FrameParser
{
public:
    /**
     * @enum Status
     * @brief next() 的三态返回值
     */
    enum class Status {
        Frame,        ///< 成功取出一帧合法帧（frame 已写入）
        Error,        ///< 取出帧但校验失败（error 已写入，坏帧字节已消费）
        NeedMoreData  ///< 数据不足，无法构成完整帧（未消费数据保留，继续 feed）
    };

    /**
     * @brief 喂入一段新收到的字节
     * @param chunk 本轮收到的字节（半帧/整帧/多帧/纯垃圾均可）
     */
    void feed(const QByteArray &chunk);

    /**
     * @brief 尝试从缓冲中取出下一帧
     * @param frame 出参：Status==Frame 时写入完整帧
     * @param error 出参：Status==Error 时写入错误分类
     * @return 三态结果，见 Status
     *
     * @note 单次调用只报告一个结果；遇 Error 会消费坏帧字节后返回，未消费数据保留，
     *       调用方应继续调用 next() 处理剩余数据（这保证 CRC 错不会被静默吞掉）。
     */
    [[nodiscard]] Status next(ParsedFrame &frame, FrameError &error);

    /**
     * @brief 当前缓冲里还剩多少未消费字节（= m_buffer.size() - m_readPos）
     */
    [[nodiscard]] int bufferedBytes() const;

private:
    QByteArray m_buffer;   ///< 累积缓冲：feed 追加、next 消费
    int m_readPos = 0;     ///< 读游标：已消费字节在 m_buffer 中的偏移（去 O(n)）

    /**
     * @brief 读游标压缩：m_readPos 超过阈值时物理移除已消费前缀（摊销 O(1)）
     *
     * 触发条件：m_readPos > 4096 且 m_readPos > 缓冲一半。
     * 满足时执行 m_buffer.remove(0, m_readPos) 并把 m_readPos 归零。
     */
    void compactIfNeeded();
};

} // namespace protocol
} // namespace dscope
