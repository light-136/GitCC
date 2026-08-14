/**
 * @file main.cpp
 * @brief 模拟采集设备（Simulator）—— 独立可执行程序（P11）
 *
 * 用途：在没有真实硬件时，模拟一个"采集设备"供主程序 DataScope 联调。
 * 行为（严格对照《05-TCP链路与模拟设备契约.md》第三节）：
 *   1. 启动 TCP 服务端，默认监听 0.0.0.0:40001（可用 --port / -p 覆盖）；
 *   2. 新连接 → 记录日志；断开 → 移除并记录日志；
 *   3. 收到客户端任意帧 → FrameParser 解析 → 组"响应帧（func|0x80）"回发，验证双向链路；
 *   4. 每 50ms 向所有已连接客户端发一帧"模拟采集数据"：
 *      FUNC=0x01（采集），CMD=0x01，载荷 = 4 通道 × 4 字节 float（大端序），
 *      各通道为幅度/相位/频率不同的正弦波（模拟实时采集）。
 *
 * 教学点（对照 C#）：
 *   - QTcpServer ≈ C# 的 TcpListener；nextPendingConnection ≈ AcceptTcpClient；
 *   - 每个客户端一个独立 FrameParser：类似 C# 里每个客户端连接维护独立的接收缓冲，
 *     避免多客户端的字节流互相串包（半包/粘包状态必须按连接隔离）；
 *   - QTimer 定时组帧发送 ≈ C# 的 System.Timers.Timer 里周期构造数据并发送。
 */

#include <QCoreApplication>
#include <QCommandLineParser>
#include <QCommandLineOption>
#include <QTcpServer>
#include <QTcpSocket>
#include <QHostAddress>
#include <QTimer>
#include <QHash>
#include <QList>
#include <QDebug>

#include <cmath>
#include <cstring>

#include "protocol/framebuilder.h"
#include "protocol/frameparser.h"
#include "protocol/protocoltypes.h"

namespace {

// ---- 4 通道正弦波参数：幅度 / 角频率 / 初相位 ----
// 各通道数值不同，便于在 UI 实时曲线上直观区分（对应 C# 里配置 4 个通道的模拟波形）
const float kAmplitudes[4]  = { 10.0f, 8.0f, 5.0f, 3.0f };
const float kFrequencies[4] = { 1.0f, 2.0f, 3.0f, 0.5f };
const float kPhases[4]      = { 0.0f, 1.5707963f, 3.1415927f, 0.7853982f }; // 0、π/2、π、π/4

/**
 * @brief 把 float 按大端序编码成 4 字节
 * @param value 待编码的浮点值
 * @return 大端序 4 字节
 *
 * 教学点：协议契约规定载荷内 float 用大端序，而 PC（x86）内存是小端。
 * 这里把内存里的 4 字节按"高字节在前"重排，等价于 C# 里
 * `BitConverter.GetBytes(v)` 后再手动反转字节序。
 */
QByteArray floatToBigEndian(float value)
{
    quint32 bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));   // 取内存位模式（不关心本机端序）
    QByteArray bytes(4, Qt::Uninitialized);
    bytes[0] = static_cast<char>((bits >> 24) & 0xFF);
    bytes[1] = static_cast<char>((bits >> 16) & 0xFF);
    bytes[2] = static_cast<char>((bits >> 8) & 0xFF);
    bytes[3] = static_cast<char>(bits & 0xFF);
    return bytes;
}

} // namespace

int main(int argc, char *argv[])
{
    QCoreApplication app(argc, argv);
    QCoreApplication::setApplicationName("DataScopeSimulator");
    QCoreApplication::setApplicationVersion("1.0.0");

    // ---- 命令行参数：--port / -p 覆盖默认端口 40001 ----
    QCommandLineParser parser;
    parser.setApplicationDescription(
        "DataScope Studio 模拟采集设备：监听 TCP 端口，周期性发送 4 通道正弦波采集帧，"
        "并回发响应帧以验证双向链路。");
    parser.addHelpOption();
    parser.addVersionOption();
    QCommandLineOption portOption(
        QStringList() << "p" << "port",
        "监听端口（默认 40001）",
        "port", "40001");
    parser.addOption(portOption);
    parser.process(app);

    bool portOk = false;
    const quint16 port = static_cast<quint16>(parser.value(portOption).toUShort(&portOk));
    if (!portOk || port == 0) {
        qCritical().noquote() << "端口参数无效，请使用 --port <1-65535>";
        return 1;
    }

    // ---- 客户端集合：已连接 socket 列表 + 每客户端独立的流式解析器 ----
    // 每客户端独立 parser：多客户端同时发数据时，各自的半包/粘包状态不会互相污染
    QList<QTcpSocket*> clients;
    QHash<QTcpSocket*, datascope::protocol::FrameParser> parsers;

    // ---- 启动 TCP 服务端，监听 0.0.0.0（所有网卡） ----
    QTcpServer server;
    if (!server.listen(QHostAddress::Any, port)) {
        qCritical().noquote() << "监听失败:" << server.errorString();
        return 1;
    }
    qInfo().noquote() << QString("模拟设备已启动，监听 0.0.0.0:%1").arg(server.serverPort());

    // ---- 新连接处理 ----
    QObject::connect(&server, &QTcpServer::newConnection, [&]() {
        // 循环取出所有待处理的新连接（事件循环可能一次积累多个）
        while (server.hasPendingConnections()) {
            QTcpSocket *client = server.nextPendingConnection();
            clients.append(client);
            parsers.insert(client, datascope::protocol::FrameParser());

            // 收到数据：读全部字节 → 喂解析器 → 逐帧解析并回发响应帧
            QObject::connect(client, &QTcpSocket::readyRead, [&, client]() {
                const QByteArray chunk = client->readAll();
                parsers[client].feed(chunk);

                datascope::protocol::ProtocolFrame in;
                while (parsers[client].nextFrame(in)) {
                    // 校验通过的合法帧：记录并构造响应帧（func | 0x80）回发
                    // 契约约定：0x80 为响应标志，收到 FUNC 就回 FUNC|0x80，验证双向链路
                    qInfo().nospace()
                        << "收到帧 func=0x" << hex << quint16(in.func)
                        << " cmd=0x" << quint16(in.cmd) << dec
                        << " 载荷长度=" << in.payload.size()
                        << "，回发响应帧(func|0x80)";
                    const QByteArray resp = datascope::protocol::FrameBuilder::build(
                        static_cast<quint8>(in.func | 0x80), in.cmd, in.payload);
                    client->write(resp);
                }
            });

            // 断开：移除客户端并记录
            QObject::connect(client, &QTcpSocket::disconnected, [&, client]() {
                qInfo().noquote() << "客户端断开:"
                                  << client->peerAddress().toString()
                                  << ":" << client->peerPort();
                clients.removeAll(client);
                parsers.remove(client);
                client->deleteLater();   // 延迟销毁，避免事件处理中析构崩溃
            });

            qInfo().noquote() << "新客户端连接:"
                              << client->peerAddress().toString()
                              << ":" << client->peerPort();
        }
    });

    // ---- 每 50ms 向所有已连接客户端发送一帧"模拟采集数据" ----
    double phase = 0.0;   // 相位累计：随每次定时器触发递增，产生随时间变化的正弦波
    QTimer dataTimer;
    dataTimer.setInterval(50);
    QObject::connect(&dataTimer, &QTimer::timeout, [&]() {
        phase += 0.05;   // 相位步进：值越大，波形变化越快

        // 组帧：FUNC=0x01 采集，CMD=0x01，载荷 = 4 通道 × 4 字节 float 大端
        QByteArray payload;
        payload.reserve(16);
        for (int ch = 0; ch < 4; ++ch) {
            const float v = kAmplitudes[ch]
                * static_cast<float>(std::sin(phase * kFrequencies[ch] + kPhases[ch]));
            payload += floatToBigEndian(v);
        }
        const QByteArray frame = datascope::protocol::FrameBuilder::build(0x01, 0x01, payload);

        // 只向仍处于连接态的客户端发送（防止向已断开 socket 写数据触发误报）
        for (QTcpSocket *client : clients) {
            if (client->state() == QAbstractSocket::ConnectedState)
                client->write(frame);
        }
    });
    dataTimer.start();

    qInfo().noquote() << "模拟设备运行中，按 Ctrl+C 退出";
    return app.exec();
}
