/**
 * @file frameparser.cpp
 * @brief 协议流式解析器实现（三态状态机 + 读游标去 O(n)）
 *
 * 状态机设计（教学重点）：
 *
 *   状态 A —— 扫描帧头（重同步）
 *     在未消费区间 [m_readPos, size) 里找 0xAA 0x55；找到前，前面的字节一律当作垃圾丢弃。
 *     "重同步"的含义：无论链路里混入多少脏数据，只要出现合法帧头，
 *     解析器就能重新对上节奏（这正是帧头 SOF 存在的意义）。
 *     indexOf 从每个位置尝试子串匹配，天然支持重叠扫描：
 *     "AA 00 AA 55" 中第二个 AA（下标 2）也能被找到。
 *
 *   状态 B —— 读帧头字段（VERSION / FUNC / CMD / LEN 大端）
 *     未消费数据必须至少 7 字节才够读头。读头后立即做两项**越界/版本防御**：
 *       - VERSION != kProtocolVersion → 非法帧头，游标推进 7 字节回状态 A；
 *       - LEN > kMaxPayloadLen        → 非法帧头，游标推进 7 字节回状态 A；
 *     绝不等待 "7+LEN+2" 字节到齐，防止恶意 LEN=0xFFFF 逼解析器囤积约 64KB。
 *
 *   状态 C —— 收集 DATA 并校验 CRC
 *     未消费数据够 7+LEN+2 字节后，取出 DATA，用 Crc16 对 VERSION..DATA 计算，
 *     与帧尾 CRC16 比对：
 *       - 一致 → 产出 Frame（frame 写入 version/func/cmd/payload）；
 *       - 不一致 → 产出 Error(CrcError)，坏帧整帧消费（游标推进）。
 *
 * 半包/粘包：
 *   由"feed 只追加 + next 只消费"自然处理。半包时 next 返回 NeedMoreData
 *   且未消费数据保留；下次 feed 补齐后再次调用即可取出。粘包时一次 feed 多帧，
 *   连续调用 next 即可逐帧取出。
 *
 * ── 读游标（去 O(n)）──
 *   消费帧/丢弃垃圾一律 m_readPos += n，不调用 remove(0, n)；只有游标超过阈值
 *   （4096 且大于缓冲一半）才做一次物理压缩，把已消费前缀移除、游标归零。
 *
 * ── 与 V2 的差异（重要）──
 *   V2 的 nextFrame 返回 bool，CRC 坏帧在循环内 continue 静默丢弃；
 *   V3 改为三态返回，坏帧/非法头通过 FrameError 出参显式报告，绝不静默吞错。
 */

#include "protocol/frameparser.h"
#include "protocol/crc16.h"

namespace dscope {
namespace protocol {

namespace {
// ---------------------------------------------------------------------------
// 内部工具：帧头匹配
// ---------------------------------------------------------------------------

/** @brief 帧头高字节：0xAA（kSof = 0xAA55 的高 8 位），用于末尾残留判断 */
constexpr quint8 kSofHi = static_cast<quint8>((kSof >> 8) & 0xFF);

/**
 * @brief 帧头字节序列 0xAA 0x55（供 QByteArray::indexOf 子串匹配）
 *
 * 用函数局部 static 惰性构造一次，等价于 C# 的 static readonly byte[]。
 */
const QByteArray &sofPattern()
{
    static const QByteArray pattern = QByteArray::fromHex("AA55");
    return pattern;
}
} // namespace

void FrameParser::feed(const QByteArray &chunk)
{
    // 直接追加到累积缓冲。这一段可能：半帧 / 整帧 / 多帧 / 纯垃圾，
    // 全部先囤起来，交给 next() 去消费——这就是半包/粘包的解耦思路。
    m_buffer.append(chunk);
}

void FrameParser::compactIfNeeded()
{
    // 读游标超过阈值（4096）且超过缓冲一半时，把已消费前缀物理移除一次、游标归零。
    // 单次 remove 的 O(n) 开销被大量 O(1) 消费摊销，整体接近 O(1)。
    if (m_readPos > 4096 && m_readPos > m_buffer.size() / 2) {
        m_buffer.remove(0, m_readPos);
        m_readPos = 0;
    }
}

FrameParser::Status FrameParser::next(ParsedFrame &frame, FrameError &error)
{
    // 三态状态机用 while 循环 + 读游标滑动实现：
    // 循环每轮尝试消费一帧（或丢弃一段非法数据），然后处理剩余数据。
    // 未消费数据少于帧头长度（7 字节）时不可能有完整帧头，直接判定"数据不足"。
    while (m_buffer.size() - m_readPos >= kHeaderLen) {

        // ================= 状态 A：扫描帧头（重同步，从读游标起） =================
        // indexOf 从 m_readPos 起做子串匹配，逐位置尝试，天然支持重叠扫描。
        const int sofIdx = m_buffer.indexOf(sofPattern(), m_readPos);
        if (sofIdx < 0) {
            // 未消费区间不存在完整帧头 → 整段都是垃圾。
            // 唯一例外：末尾可能残留一个 0xAA，它是"下一帧 SOF 的前半个字节"，
            // 必须保留它（等下一次 feed 补 0x55），其余垃圾全部推进游标丢弃，
            // 防止垃圾无限累积撑大缓冲。
            if (static_cast<quint8>(m_buffer.at(m_buffer.size() - 1)) == kSofHi) {
                m_readPos = m_buffer.size() - 1;   // 保留末尾 1 字节 0xAA
            } else {
                m_readPos = m_buffer.size();       // 全部标记为已消费
            }
            compactIfNeeded();
            return Status::NeedMoreData;
        }

        // 把帧头前面的垃圾字节丢弃：只推进读游标到帧头位置，不 remove(0, n)
        m_readPos = sofIdx;

        // ================= 状态 B：读帧头字段（VERSION / FUNC / CMD / LEN 大端） =================
        if (m_buffer.size() - m_readPos < kHeaderLen) {
            compactIfNeeded();
            return Status::NeedMoreData;   // 帧头没凑齐（半包），保留未消费数据，等下次 feed
        }

        // 以读游标为基准（base）取字段。帧头 7 字节布局：
        //   base+0=SOF_HI, base+1=SOF_LO, base+2=VERSION,
        //   base+3=FUNC, base+4=CMD, base+5=LEN_HI, base+6=LEN_LO
        const int base = m_readPos;
        const quint8 version = static_cast<quint8>(m_buffer.at(base + 2));
        const quint8 func    = static_cast<quint8>(m_buffer.at(base + 3));
        const quint8 cmd     = static_cast<quint8>(m_buffer.at(base + 4));
        // LEN 是 2 字节大端：高字节在前
        const quint16 len = static_cast<quint16>(
            (static_cast<quint16>(static_cast<quint8>(m_buffer.at(base + 5))) << 8)
            | static_cast<quint16>(static_cast<quint8>(m_buffer.at(base + 6))));

        // ---- 版本校验：VERSION 不符 → 非法帧头，丢弃 header 后报告错误 ----
        if (version != kProtocolVersion) {
            error = FrameError::InvalidHeader;
            m_readPos += kHeaderLen;   // 丢弃 7 字节头，剩余数据保留（重同步）
            compactIfNeeded();
            return Status::Error;
        }

        // ---- 越界防御：LEN 声称超大 → 非法帧头，丢弃 header 后报告错误 ----
        // 绝不能在这里等 "7+LEN+2" 字节——恶意 LEN=0xFFFF 会逼解析器
        // 囤积约 64KB 才肯继续，缓冲会被撑爆。直接丢弃 7 字节头。
        if (len > kMaxPayloadLen) {
            error = FrameError::InvalidHeader;
            m_readPos += kHeaderLen;   // 丢弃 7 字节头，剩余数据保留（重同步）
            compactIfNeeded();
            return Status::Error;
        }

        // ================= 状态 C：收齐 DATA + CRC 后校验 =================
        const int dataStart = kHeaderLen;                 // DATA 相对帧头的偏移
        const int crcStart  = kHeaderLen + len;           // CRC_L 相对帧头的偏移
        const int totalLen  = kHeaderLen + len + kCrcLen; // 整帧长度

        if (m_buffer.size() - m_readPos < totalLen) {
            compactIfNeeded();
            return Status::NeedMoreData;   // DATA/CRC 未到齐（半包），保留未消费数据
        }

        // 取出载荷（在校验通过后使用；先取出来避免游标推进之后下标失效）
        const QByteArray data = m_buffer.mid(base + dataStart, len);

        // CRC 输入 = VERSION(base+2) 起到 DATA 末
        //             = VERSION(1) + FUNC(1) + CMD(1) + LEN(2) + DATA(len)，共 kHeaderLen+len-2 字节
        // 注意：V3 契约规定 CRC 覆盖 SOF 之后的所有字节（含 VERSION 与 LEN 字段）
        const QByteArray crcInput = m_buffer.mid(base + 2, kHeaderLen + len - 2);
        const quint16 actualCrc  = Crc16::compute(crcInput);

        // 帧尾 CRC16 低字节在前：at(base+crcStart) 是 CRC_L，at(base+crcStart+1) 是 CRC_H
        const quint16 frameCrc = static_cast<quint16>(
            (static_cast<quint16>(static_cast<quint8>(m_buffer.at(base + crcStart + 1))) << 8)
            | static_cast<quint16>(static_cast<quint8>(m_buffer.at(base + crcStart))));

        // 整帧已经消费掉（无论校验成败，坏帧也一并消费，不产出）
        m_readPos += totalLen;

        if (actualCrc != frameCrc) {
            // CRC 不通过：显式报告错误，绝不静默丢弃。剩余数据保留，
            // 调用方应继续调用 next() 处理下一帧。
            error = FrameError::CrcError;
            compactIfNeeded();
            return Status::Error;
        }

        // 校验通过：产出合法帧
        frame.version = version;
        frame.func    = func;
        frame.cmd     = cmd;
        frame.payload = data;
        compactIfNeeded();
        return Status::Frame;
    }

    return Status::NeedMoreData;   // 未消费数据不足 7 字节，无法构成完整帧头
}

int FrameParser::bufferedBytes() const
{
    return m_buffer.size() - m_readPos;
}

} // namespace protocol
} // namespace dscope
