// ============================================================
//  串口通讯服务实现
//
//  【Qt知识点】串口数据接收是"事件驱动"的：
//  QSerialPort 底层有线程在读取系统串口，数据到达时发出
//  readyRead() 信号，我们在主线程的槽里读取 —— 这避免了阻塞。
//  （对比：C# 的 DataReceived 事件同样是在后台线程触发）
//
//  【Qt知识点】粘包/分包处理：
//  串口是一段字节流，没有"帧"的天然边界。一帧数据可能分多次
//  到达（分包），多帧可能一起到达（粘包）。这里用：
//  1. 缓冲所有到达字节
//  2. 寻找 SOF 定位帧头
//  3. 按 DATALEN 计算出完整帧长，凑够才消费
// ============================================================
#include "SerialPortService.h"
#include <QSerialPortInfo>
#include <QDateTime>
#include <QDebug>

// 十六进制字节转可读字符串（如 "01 06 00 10 02 00"）
static QString toHexString(const QByteArray &data)
{
    return data.toHex(' ').toUpper();
}

SerialPortService::SerialPortService(QObject *parent)
    : QObject(parent)
{
    // 串口对象延迟到 open() 时创建
    m_serialPort = nullptr;

    // 超时定时器：单次触发，超时即放弃等待回复
    m_timeoutTimer.setSingleShot(true);
    connect(&m_timeoutTimer, &QTimer::timeout, this, &SerialPortService::onTimeout);
}

SerialPortService::~SerialPortService()
{
    close();
}

QStringList SerialPortService::availablePorts()
{
    QStringList ports;
#if QT_VERSION >= QT_VERSION_CHECK(5, 3, 0)
    // QSerialPortInfo 枚举系统枚举的串口
    const auto infos = QSerialPortInfo::availablePorts();
    for (const QSerialPortInfo &info : infos)
        ports << info.portName();
#endif
    return ports;
}

bool SerialPortService::isConnected() const
{
    return m_serialPort && m_serialPort->isOpen();
}

bool SerialPortService::open(const SerialPortConfig &config)
{
    // 先关闭旧的串口，再重新创建
    close();

    m_serialPort = new QSerialPort(this);
    m_serialPort->setPortName(config.portName);
    m_serialPort->setBaudRate(config.baudRate);
    m_serialPort->setDataBits(config.dataBits);
    m_serialPort->setStopBits(config.stopBits);
    m_serialPort->setParity(config.parity);
    m_serialPort->setReadBufferSize(4096);

    // 注册数据就绪槽
    connect(m_serialPort, &QSerialPort::readyRead, this, &SerialPortService::onReadyRead);

    if (!m_serialPort->open(QIODevice::ReadWrite))
    {
        log(QStringLiteral("打开串口失败: %1").arg(m_serialPort->errorString()));
        emit connectionChanged(false);
        return false;
    }

    log(QStringLiteral("串口 %1 已打开 (波特率:%2, 数据位:%3, 停止位:%4, 校验:%5)")
            .arg(config.portName)
            .arg(config.baudRate)
            .arg(static_cast<int>(config.dataBits))
            .arg(static_cast<int>(config.stopBits))
            .arg(static_cast<int>(config.parity)));
    emit connectionChanged(true);
    return true;
}

void SerialPortService::close()
{
    if (m_serialPort)
    {
        if (m_serialPort->isOpen())
        {
            m_serialPort->close();
            log(QStringLiteral("串口已关闭"));
        }
        m_serialPort->deleteLater();
        m_serialPort = nullptr;
        emit connectionChanged(false);
    }
    m_receiveBuffer.clear();
    m_requestPending = false;
    m_timeoutTimer.stop();
}

void SerialPortService::sendRequest(const QByteArray &frame, quint16 expectedCmd, int timeoutMs)
{
    if (!m_serialPort || !m_serialPort->isOpen())
    {
        emit requestFailed(ERR_NOT_OPEN);
        return;
    }

    // 清空旧的接收缓冲，避免历史残帧干扰本次等待
    m_receiveBuffer.clear();

    // 登记期望回复，启动超时定时器
    m_expectedCmd = expectedCmd;
    m_requestPending = true;
    m_timeoutTimer.start(timeoutMs);

    log(QStringLiteral("[TX] %1").arg(toHexString(frame)));
    m_serialPort->write(frame);
}

void SerialPortService::onReadyRead()
{
    if (!m_serialPort) return;

    // 一次性读取当前所有可用字节
    QByteArray chunk = m_serialPort->readAll();
    if (chunk.isEmpty()) return;

    m_receiveBuffer.append(chunk);
    log(QStringLiteral("[RX] %1").arg(toHexString(chunk)));

    tryParseFrame();
}

void SerialPortService::tryParseFrame()
{
    // ---- 1. 在缓冲中寻找帧头 SOF ----
    int sofIndex = m_receiveBuffer.indexOf(static_cast<char>(FrameConstants::SOF));
    if (sofIndex < 0)
    {
        // 没有任何有效帧头，丢弃全部
        m_receiveBuffer.clear();
        return;
    }
    // 丢弃 SOF 之前的无效数据
    if (sofIndex > 0)
        m_receiveBuffer.remove(0, sofIndex);

    // ---- 2. 最小帧长检查 ----
    if (m_receiveBuffer.size() < FrameConstants::HEADER_SIZE + FrameConstants::CRC_SIZE)
        return;  // 数据不够，等下一次 readyRead

    // ---- 3. 读取 DATALEN（偏移 4-5，小端），计算期望总帧长 ----
    quint16 dataLen = static_cast<quint8>(m_receiveBuffer.at(4))
                    | (static_cast<quint16>(static_cast<quint8>(m_receiveBuffer.at(5))) << 8);
    int frameLen = FrameConstants::HEADER_SIZE + dataLen + FrameConstants::CRC_SIZE;

    // ---- 4. 若完整帧未到齐，继续等待 ----
    if (m_receiveBuffer.size() < frameLen)
        return;

    // ---- 5. 取出完整帧并消费 ----
    QByteArray frameBytes = m_receiveBuffer.left(frameLen);
    m_receiveBuffer.remove(0, frameLen);

    bool ok = false;
    ProtocolFrame frame = ProtocolFrame::deserialize(frameBytes, &ok);
    if (!ok)
        return;  // CRC 或帧头错误，丢弃（完整帧已消费，不会死循环）

    // ---- 6. 通知等待者 ----
    if (m_requestPending)
    {
        m_timeoutTimer.stop();
        m_requestPending = false;
        if (frame.cmd == m_expectedCmd)
            emit requestCompleted(frame);
        else
            emit requestFailed(ERR_MISMATCH);
    }
}

void SerialPortService::onTimeout()
{
    if (m_requestPending)
    {
        m_requestPending = false;
        log(QStringLiteral("等待回复超时"));
        emit requestFailed(ERR_TIMEOUT);
    }
}

void SerialPortService::log(const QString &msg)
{
    emit logMessage(QStringLiteral("[%1] %2")
                        .arg(QDateTime::currentDateTime().toString("HH:mm:ss.zzz"))
                        .arg(msg));
}
