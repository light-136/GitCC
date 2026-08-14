/**
 * @file tcpclient.h
 * @brief TCP 客户端封装（P11：TcpClient）
 *
 * 用途：作为主程序 DataScope 与采集设备（真实设备或 Simulator 模拟设备）之间的
 * TCP 客户端。封装 QTcpSocket 的收发细节，对外暴露"连接 / 断开 / 发帧 / 收帧"
 * 四个业务动作，并把收到的原始字节流交给协议引擎（FrameParser）解析成协议帧。
 *
 * 分层定位（对应架构 UI→app→services→domain/protocol→infrastructure→utils）：
 *   本类属于 services 层——服务层是"业务动作"的封装者，向上对 UI/app 提供语义化接口，
 *   向下依赖 protocol 层的组帧器/解析器，自身不关心字节细节。
 *
 * 教学点（对照 WPF/C#）：
 *   - 本类 ≈ WPF 里自己封装的 TcpClient 帮助类：把底层 Socket 的
 *     Connect/Close/Send/Receive 封装成业务方法，避免 UI 直接操作裸 Socket；
 *   - QTcpSocket ≈ C# 的 TcpClient（同：内部都是 WinSock/Socket 句柄）；
 *   - 信号 connected / disconnected / frameReceived ≈ C# 的 event，
 *     UI 用 connect 订阅（对应 C# 的 `+=`），收到帧时以"事件携带数据"方式通知业务层；
 *   - 错误统一转 QString 信号：相当于 C# 里把 SocketException 包装成
 *     业务错误信息再抛出，不让 UI 感知底层 SocketError 枚举。
 */

#pragma once

#include <QObject>
#include <QString>
#include <QAbstractSocket>

#include "protocol/protocoltypes.h"
#include "protocol/frameparser.h"

// 前向声明：m_socket 是成员指针，头文件不需要完整定义，降低编译耦合
class QTcpSocket;

namespace datascope {
namespace services {

/**
 * @class TcpClient
 * @brief TCP 客户端（连接采集设备 / 模拟设备）
 */
class TcpClient : public QObject
{
    Q_OBJECT

public:
    /**
     * @brief 构造
     * @param parent 父对象（QObject 对象树托管 m_socket 生命周期）
     */
    explicit TcpClient(QObject *parent = nullptr);

    /**
     * @brief 发起 TCP 连接（异步：结果由 connected / errorOccurred 信号通知）
     * @param host 目标主机（IP 或域名）
     * @param port 目标端口
     */
    void connectToHost(const QString &host, quint16 port);

    /** @brief 主动断开连接 */
    void disconnectFromHost();

    /**
     * @brief 当前是否已连接
     * @return true = 处于 ConnectedState
     */
    bool isConnected() const;

    /**
     * @brief 发送一帧协议数据（内部用 FrameBuilder 组帧后写出）
     * @param frame 业务层构造的协议帧
     * @return true = 已交给 socket 写出；false = 未连接或组帧失败（载荷超限等）
     */
    bool sendFrame(const protocol::ProtocolFrame &frame);

signals:
    /** @brief 连接成功 */
    void connected();

    /** @brief 断开（主动断开或对端关闭 / 网络异常导致） */
    void disconnected();

    /** @brief 错误（已统一转为字符串，业务层不感知裸 socket 错误码） */
    void errorOccurred(const QString &message);

    /** @brief 收到一帧完整且校验通过的协议帧（FrameParser 解析产物） */
    void frameReceived(const protocol::ProtocolFrame &frame);

private slots:
    /** @brief socket 连接成功 → 转发 connected */
    void onConnected();

    /** @brief socket 断开 → 转发 disconnected */
    void onDisconnected();

    /** @brief socket 错误 → 统一转 QString 后转发 errorOccurred */
    void onSocketError(QAbstractSocket::SocketError error);

    /** @brief 有数据可读 → 喂解析器并逐帧 emit frameReceived */
    void onReadyRead();

private:
    QTcpSocket *m_socket = nullptr;   // 底层 TCP socket（挂 parent，由 QObject 对象树回收）
    protocol::FrameParser m_parser;   // 流式帧解析器：处理半包/粘包
};

} // namespace services
} // namespace datascope
