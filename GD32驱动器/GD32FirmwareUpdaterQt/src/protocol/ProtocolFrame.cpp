// ============================================================
//  协议帧序列化 / 反序列化实现
//
//  【Qt知识点】大小端：
//  - 大端（Big-Endian）：高位字节在前，用于 CMD
//  - 小端（Little-Endian）：低位字节在前，用于 DATALEN 和 CRC
//  注意与 WPF 版完全一致，保证串口线上字节流逐字节兼容。
// ============================================================
#include "ProtocolFrame.h"
#include "CrcCalculator.h"

// ==================== 序列化（发送方向） ====================
QByteArray ProtocolFrame::serialize() const
{
    QByteArray buffer;

    // 1. 帧头 + 功能码
    buffer.append(static_cast<char>(sof));
    buffer.append(static_cast<char>(func));

    // 2. CMD — 大端（高字节在前）
    buffer.append(static_cast<char>((cmd >> 8) & 0xFF));
    buffer.append(static_cast<char>(cmd & 0xFF));

    // 3. DATALEN — 小端（低字节在前）
    quint16 dataLen = static_cast<quint16>(data.size());
    buffer.append(static_cast<char>(dataLen & 0xFF));
    buffer.append(static_cast<char>((dataLen >> 8) & 0xFF));

    // 4. DATA
    buffer.append(data);

    // 5. CRC — 对 SOF 到 DATA 区域计算，小端写入
    quint16 crcValue = CrcCalculator::calculate(buffer);
    buffer.append(CrcCalculator::toLittleEndianBytes(crcValue));

    return buffer;
}

// ==================== 反序列化（接收方向） ====================
ProtocolFrame ProtocolFrame::deserialize(const QByteArray &buffer, bool *ok)
{
    ProtocolFrame frame;

    // 最小帧长 = 帧头(6) + CRC(2) = 8 字节
    if (buffer.size() < FrameConstants::HEADER_SIZE + FrameConstants::CRC_SIZE)
    {
        if (ok) *ok = false;
        return frame;
    }

    // 帧头字段
    frame.sof  = static_cast<quint8>(buffer.at(0));
    frame.func = static_cast<quint8>(buffer.at(1));

    // 解析 CMD（大端）
    frame.cmd = (static_cast<quint16>(static_cast<quint8>(buffer.at(2))) << 8)
              |  static_cast<quint8>(buffer.at(3));

    // 解析 DATALEN（小端）
    quint16 dataLen = static_cast<quint8>(buffer.at(4))
                    | (static_cast<quint16>(static_cast<quint8>(buffer.at(5))) << 8);

    // 总帧长校验
    int frameLen = FrameConstants::HEADER_SIZE + dataLen + FrameConstants::CRC_SIZE;
    if (buffer.size() < frameLen)
    {
        if (ok) *ok = false;
        return frame;
    }

    // 提取 DATA
    frame.data = buffer.mid(FrameConstants::HEADER_SIZE, dataLen);

    // 提取 CRC（小端）
    int crcPos = FrameConstants::HEADER_SIZE + dataLen;
    frame.crc = static_cast<quint8>(buffer.at(crcPos))
              | (static_cast<quint16>(static_cast<quint8>(buffer.at(crcPos + 1))) << 8);

    // 校验帧头固定值
    if (frame.sof != FrameConstants::SOF || frame.func != FrameConstants::FUNC)
    {
        if (ok) *ok = false;
        return frame;
    }

    // 计算并比对 CRC（范围：SOF 到 DATA 结束）
    quint16 calcCrc = CrcCalculator::calculate(buffer, 0, FrameConstants::HEADER_SIZE + dataLen);
    if (frame.crc != calcCrc)
    {
        if (ok) *ok = false;
        return frame;
    }

    if (ok) *ok = true;
    return frame;
}

// ==================== 帧构造工厂实现 ====================

QByteArray FrameBuilder::buildUpgradeRequest(quint8 requestType)
{
    // DATA = [请求类型, 保留0x00]
    QByteArray data;
    data.append(static_cast<char>(requestType));
    data.append(static_cast<char>(0x00));

    ProtocolFrame frame;
    frame.cmd = CommandCode::UPGRADE_REQ;
    frame.data = data;
    return frame.serialize();
}

QByteArray FrameBuilder::buildUpgradeStart(quint32 fileSize)
{
    // DATA = 文件总大小（4 字节，小端）
    QByteArray data;
    data.append(static_cast<char>(fileSize & 0xFF));
    data.append(static_cast<char>((fileSize >> 8) & 0xFF));
    data.append(static_cast<char>((fileSize >> 16) & 0xFF));
    data.append(static_cast<char>((fileSize >> 24) & 0xFF));

    ProtocolFrame frame;
    frame.cmd = CommandCode::UPGRADE_START;
    frame.data = data;
    return frame.serialize();
}

QByteArray FrameBuilder::buildUpgradeData(quint32 offsetAddr, const QByteArray &data)
{
    // DATA = [4字节偏移地址(小端)] + [升级数据]
    QByteArray frameData;
    frameData.append(static_cast<char>(offsetAddr & 0xFF));
    frameData.append(static_cast<char>((offsetAddr >> 8) & 0xFF));
    frameData.append(static_cast<char>((offsetAddr >> 16) & 0xFF));
    frameData.append(static_cast<char>((offsetAddr >> 24) & 0xFF));
    frameData.append(data);

    ProtocolFrame frame;
    frame.cmd = CommandCode::UPGRADE_DATA;
    frame.data = frameData;
    return frame.serialize();
}

QByteArray FrameBuilder::buildJumpApp()
{
    // DATA = [0x01 = 升级完成请求]
    QByteArray data;
    data.append(static_cast<char>(0x01));

    ProtocolFrame frame;
    frame.cmd = CommandCode::JUMP_APP;
    frame.data = data;
    return frame.serialize();
}

QByteArray FrameBuilder::buildVersionRequest()
{
    // DATA = [0x01 = 请求版本号]
    QByteArray data;
    data.append(static_cast<char>(0x01));

    ProtocolFrame frame;
    frame.cmd = CommandCode::VER_REQ;
    frame.data = data;
    return frame.serialize();
}
