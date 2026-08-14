/**
 * @file frameparser.cpp
 * @brief 协议流式解析器实现（三态状态机）
 *
 * 状态机设计（教学重点）：
 *
 *   状态 A —— 扫描帧头（重同步）
 *     在缓冲里找 0xAA 0x55；找到前，前面的字节一律当作垃圾丢弃。
 *     "重同步"的含义：无论链路里混入多少脏数据，只要出现合法帧头，
 *     解析器就能重新对上节奏（这正是帧头 SOF 存在的意义）。
 *
 *   状态 B —— 读帧头字段（FUNC / CMD / LEN 大端）
 *     缓冲必须至少 6 字节才够读头。读出 LEN 后立即做**越界防御**：
 *     LEN > kMaxPayloadLen 判定为非法帧头，把 6 字节头当垃圾丢弃，
 *     回到状态 A。绝不等待"6+LEN+2"字节到齐，防止缓冲区被恶意撑爆。
 *
 *   状态 C —— 收集 DATA 并校验 CRC
 *     缓冲够 6+LEN+2 字节后，取出 DATA，用 Crc16 对 FUNC..DATA 计算，
 *     与帧尾的 CRC16 比对：
 *       - 一致 → 产出 isValid=true 的帧，整帧从缓冲头部消费掉；
 *       - 不一致 → 该帧是坏帧，整帧丢弃（不产出），回到状态 A 继续找下一帧。
 *
 * 半包/粘包：
 *   由"feed 只追加 + nextFrame 只消费"自然处理。半包时 nextFrame 返回 false
 *   且缓冲原样保留；下次 feed 补齐后再次调用即可取出。粘包时一次 feed 多帧，
 *   连续调用 nextFrame 即可逐帧取出。
 */

#include "protocol/frameparser.h"
#include "protocol/crc16.h"

namespace datascope {
namespace protocol {

namespace {
// ---------------------------------------------------------------------------
// 内部工具：帧头扫描
// ---------------------------------------------------------------------------

/** @brief 帧头高字节：0xAA（kSof = 0xAA55 的高 8 位） */
constexpr quint8 kSofHi = static_cast<quint8>((kSof >> 8) & 0xFF);
/** @brief 帧头低字节：0x55 */
constexpr quint8 kSofLo = static_cast<quint8>(kSof & 0xFF);

/**
 * @brief 在缓冲中查找完整帧头 0xAA 0x55 的起始下标
 * @param buf 待扫描缓冲
 * @return 帧头起始下标；找不到返回 -1
 *
 * @note 采用"每次只前进 1 字节"的重叠扫描：
 *       "0xAA 0x00 0xAA 0x55" 中，第一个 0xAA 不是帧头，但第二个是——
 *       若前进 2 字节就会漏掉它，所以必须逐字节推进。
 */
int findSof(const QByteArray &buf)
{
    for (int i = 0; i + 1 < buf.size(); ++i) {
        if (static_cast<quint8>(buf.at(i)) == kSofHi
            && static_cast<quint8>(buf.at(i + 1)) == kSofLo) {
            return i;
        }
    }
    return -1;
}
} // namespace

void FrameParser::feed(const QByteArray &chunk)
{
    // 直接追加到累积缓冲。这一段可能：半帧 / 整帧 / 多帧 / 纯垃圾，
    // 全部先囤起来，交给 nextFrame 去消费——这就是半包/粘包的解耦思路。
    m_buffer.append(chunk);
}

bool FrameParser::nextFrame(ProtocolFrame &out)
{
    // 三态状态机用 while 循环 + 缓冲滑动实现：
    // 循环每轮尝试消费一帧（或丢弃一段非法数据），然后继续处理剩余缓冲。
    // 少于帧头长度（6 字节）时不可能有完整帧头，直接判定"无完整帧"。
    while (m_buffer.size() >= kHeaderLen) {

        // ================= 状态 A：扫描帧头（重同步） =================
        const int sofIdx = findSof(m_buffer);
        if (sofIdx < 0) {
            // 缓冲里不存在完整帧头 → 整段都是垃圾。
            // 唯一例外：末尾可能残留一个 0xAA，它是"下一帧 SOF 的前半个字节"，
            // 必须保留它（等下一次 feed 补 0x55），其余垃圾全部清掉，
            // 防止垃圾无限累积撑大缓冲。
            if (static_cast<quint8>(m_buffer.at(m_buffer.size() - 1)) == kSofHi) {
                const QByteArray keep(1, static_cast<char>(kSofHi));
                m_buffer = keep;
            } else {
                m_buffer.clear();
            }
            return false;
        }

        // 把帧头前面的垃圾字节丢弃，让缓冲对齐到帧头起始位置
        if (sofIdx > 0)
            m_buffer.remove(0, sofIdx);

        // ================= 状态 B：读帧头字段（FUNC / CMD / LEN 大端） =================
        if (m_buffer.size() < kHeaderLen)
            return false;   // 帧头没凑齐（半包），保留缓冲，等下次 feed

        const quint8 func = static_cast<quint8>(m_buffer.at(2));
        const quint8 cmd  = static_cast<quint8>(m_buffer.at(3));
        // LEN 是 2 字节大端：高字节在前
        const quint16 len = static_cast<quint16>(
            (static_cast<quint16>(static_cast<quint8>(m_buffer.at(4))) << 8)
            | static_cast<quint16>(static_cast<quint8>(m_buffer.at(5))));

        // ---- 越界防御：LEN 声称超大 → 非法帧头，丢弃后重新扫描 ----
        if (len > kMaxPayloadLen) {
            // 绝不能在这里等 "6+LEN+2" 字节——恶意 LEN=0xFFFF 会逼解析器
            // 囤积约 64KB 才肯继续，缓冲会被撑爆。直接丢掉 6 字节头，回状态 A。
            m_buffer.remove(0, kHeaderLen);
            continue;
        }

        // ================= 状态 C：收齐 DATA + CRC 后校验 =================
        const int dataStart = kHeaderLen;                 // DATA 起始下标
        const int crcStart  = kHeaderLen + len;           // CRC_L 的下标
        const int totalLen  = kHeaderLen + len + kCrcLen; // 整帧长度

        if (m_buffer.size() < totalLen)
            return false;   // DATA/CRC 未到齐（半包），保留缓冲

        // 取出载荷（在校验通过后使用；先取出来避免 remove 之后下标失效）
        const QByteArray data = m_buffer.mid(dataStart, len);

        // CRC 输入 = FUNC(下标2) 起到 DATA 末
        //             = FUNC(1) + CMD(1) + LEN(2) + DATA(len)，共 kHeaderLen+len-2 字节
        // 注意：LEN 字段也参与校验（契约规定"覆盖 FUNC 起到 DATA 末尾的所有字节"）
        const QByteArray crcInput = m_buffer.mid(2, kHeaderLen + len - 2);
        const quint16 actualCrc  = Crc16::compute(crcInput);

        // 帧尾 CRC16 低字节在前：at(crcStart) 是 CRC_L，at(crcStart+1) 是 CRC_H
        const quint16 frameCrc = static_cast<quint16>(
            (static_cast<quint16>(static_cast<quint8>(m_buffer.at(crcStart + 1))) << 8)
            | static_cast<quint16>(static_cast<quint8>(m_buffer.at(crcStart))));

        // 整帧已经消费掉（无论校验成败，坏帧也一并丢弃，不产出）
        m_buffer.remove(0, totalLen);

        if (actualCrc == frameCrc) {
            // 校验通过：产出合法帧
            out.func    = func;
            out.cmd     = cmd;
            out.payload = data;
            out.isValid = true;
            return true;
        }

        // CRC 不通过：丢弃该帧，继续在剩余缓冲里找下一帧（重同步）
        continue;
    }

    return false;   // 缓冲不足 6 字节，无法构成完整帧头
}

int FrameParser::bufferedBytes() const
{
    return m_buffer.size();
}

} // namespace protocol
} // namespace datascope
