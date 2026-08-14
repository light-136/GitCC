/**
 * @file tcpclient.cpp
 * @brief TCP 客户端封装实现（P11）
 *
 * 实现要点（严格对照《05-TCP链路与模拟设备契约.md》第四节）：
 *   1. 内部持有 QTcpSocket* 与 FrameParser；
 *   2. readyRead → 读全部字节 → m_parser.feed(chunk) → 循环 nextFrame() → emit frameReceived；
 *   3. sendFrame 用 FrameBuilder::build 组帧后 write；
 *   4. 错误统一转 QString 信号。
 *
 * 教学点（对照 C#）：
 *   - 事件驱动的数据到达：C# 里是 TcpClient 的异步 Receive 回调 / await，
 *     Qt 里是 readyRead 信号 → 槽。二者的共同点是"数据到了才处理，不轮询"；
 *   - readAll() 一次性读走本次就绪的字节，帧边界（半包/粘包）的处理完全交给
 *     FrameParser——这正是把协议解析下沉到 protocol 层的价值：
 *     services 层只负责"拿到字节就喂给解析器"。
 */

#include "services/tcpclient.h"

#include <QTcpSocket>

#include "protocol/framebuilder.h"

namespace datascope {
namespace services {

TcpClient::TcpClient(QObject *parent)
    : QObject(parent)
{
    // 创建底层 socket 并挂到本对象下（QObject 对象树自动回收，无需手动 delete）
    m_socket = new QTcpSocket(this);

    // ---- 连接 Qt 原生信号 → 本类的内部槽，再转成对外业务信号 ----
    // 1) 连接成功
    connect(m_socket, &QTcpSocket::connected,
            this, &TcpClient::onConnected);
    // 2) 断开（主动 disconnectFromHost / 对端关闭 / 网络异常）
    connect(m_socket, &QTcpSocket::disconnected,
            this, &TcpClient::onDisconnected);
    // 3) 错误：Qt 5.15 才新增 errorOccurred 信号；5.12 只有 error(...)。
    //    error 与同名的"错误码查询方法 error()"重载，必须用 QOverload 精确指定
    //    "带 QAbstractSocket::SocketError 参数"的那一个信号。
    connect(m_socket, QOverload<QAbstractSocket::SocketError>::of(&QAbstractSocket::error),
            this, &TcpClient::onSocketError);
    // 4) 有数据可读 → 解析
    connect(m_socket, &QTcpSocket::readyRead,
            this, &TcpClient::onReadyRead);
}

void TcpClient::connectToHost(const QString &host, quint16 port)
{
    // 异步发起连接：结果通过 connected / errorOccurred 信号通知（对应 C# 的 ConnectAsync）
    m_socket->connectToHost(host, port);
}

void TcpClient::disconnectFromHost()
{
    m_socket->disconnectFromHost();
}

bool TcpClient::isConnected() const
{
    // 只有已建立连接才算 connected；Connecting / HostLookup 等中间态均不算
    return m_socket->state() == QAbstractSocket::ConnectedState;
}

bool TcpClient::sendFrame(const protocol::ProtocolFrame &frame)
{
    // 未连接时直接拒绝发送，避免 QIODevice::write 在未打开设备上返回错误
    if (!isConnected())
        return false;

    // 组帧：载荷超过 kMaxPayloadLen 等失败场景返回空数组
    const QByteArray bytes = protocol::FrameBuilder::build(frame.func, frame.cmd, frame.payload);
    if (bytes.isEmpty())
        return false;

    // 写进 socket 发送缓冲（write 不阻塞，真正发出由 Qt 事件循环负责）
    const qint64 written = m_socket->write(bytes);
    return written == static_cast<qint64>(bytes.size());
}

void TcpClient::onConnected()
{
    emit connected();
}

void TcpClient::onDisconnected()
{
    emit disconnected();
}

void TcpClient::onSocketError(QAbstractSocket::SocketError /*error*/)
{
    // 把底层 socket 错误统一转成可读的 QString，业务层不感知错误枚举
    // （教学：类似 C# 里把 SocketException 包装成带中文说明的异常信息再抛出）
    emit errorOccurred(m_socket->errorString());
}

void TcpClient::onReadyRead()
{
    // 读走本次就绪的全部字节（可能含：半帧 / 整帧 / 多帧 / 垃圾数据）
    const QByteArray chunk = m_socket->readAll();
    m_parser.feed(chunk);

    // 循环取出所有完整帧：粘包场景一次可能产出多帧
    protocol::ProtocolFrame frame;
    while (m_parser.nextFrame(frame)) {
        emit frameReceived(frame);
    }
}

} // namespace services
} // namespace datascope
