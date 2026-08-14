/**
 * @file acquisitionworker.cpp
 * @brief 采集线程 Worker 实现（P12）
 *
 * 帧 → DataPoint 转换规则（见契约《06-采集链路与数据总线契约.md》）：
 *   模拟设备采集帧（CMD=0x01）载荷布局 = 4 通道 × 4 字节 float（大端）：
 *     [通道0 温度 float][通道1 压力 float][通道2 流量 float][通道3 振动 float]
 *   每帧产出 4 个 DataPoint（channelIndex = 0..3），timestamp = 当前时间。
 *
 * 线程安全铁律（本文件遵守）：
 *   1. TcpClient 在 start() 槽内创建——确保 socket 与线程同生；
 *   2. 本对象所有槽只在采集线程执行（moveToThread + 队列连接）；
 *   3. 不触碰任何 UI 对象。
 */

#include "services/acquisitionworker.h"

#include "services/tcpclient.h"
#include "protocol/protocoltypes.h"

#include <QByteArray>
#include <QDateTime>
#include <QDebug>

namespace datascope {
namespace services {

AcquisitionWorker::AcquisitionWorker(QObject *parent)
    : QObject(parent)
{
}

AcquisitionWorker::~AcquisitionWorker()
{
    // 安全兜底：若 stop() 未显式调用（异常退出），这里确保客户端被清理
    if (m_client) {
        m_client->deleteLater();
        m_client = nullptr;
    }
}

void AcquisitionWorker::start(const QString &host, quint16 port)
{
    // ---- 采集线程内创建 TCP 客户端 ----
    // 教学点：socket 必须在所属线程内创建。若在主线程 new 后 moveToThread，
    // QTcpSocket 会因"notifiers 跨线程启停"发出警告且行为未定义。
    if (m_client) {
        return; // 已在运行，防重复启动
    }

    m_client = new TcpClient(this);   // parent = this，随 Worker 生命周期管理

    // ---- 连接业务信号：把 socket 事件翻译成 Worker 自己的信号 ----
    connect(m_client, &TcpClient::connected, this, [this]() {
        emit connectedChanged(true);   // 连接成功 → 通知主线程
    });
    connect(m_client, &TcpClient::disconnected, this, [this]() {
        emit connectedChanged(false);  // 断开 → 通知主线程
    });
    connect(m_client, &TcpClient::errorOccurred, this,
            [this](const QString &message) {
        emit errorOccurred(message);   // 错误转发给 DataService
        // 连接失败或通信异常：立即销毁客户端，使后续 start() 能重建——
        // 这是 P13 自动重连（DeviceController）能工作的关键：每次重连都是
        // "新建一个全新连接"，而不是在残留的坏 socket 上重试。
        if (m_client) {
            m_client->deleteLater();
            m_client = nullptr;
        }
    });
    connect(m_client, &TcpClient::frameReceived, this,
            &AcquisitionWorker::handleFrame);     // 帧 → DataPoint 转换

    // ---- 发起连接（异步，结果由上述信号通知）----
    m_client->connectToHost(host, port);
}

void AcquisitionWorker::stop()
{
    // 采集线程内停止：断开并销毁客户端
    if (m_client) {
        m_client->disconnectFromHost();
        m_client->deleteLater();   // 延迟删除：等本线程事件循环处理完当前事件
        m_client = nullptr;
    }
}

void AcquisitionWorker::handleFrame(const protocol::ProtocolFrame &frame)
{
    // ---- 帧 → DataPoint 批量转换 ----
    // 教学点：只处理"采集数据"帧（CMD=0x01 及其响应帧）；
    // 其它功能码（配置/查询）在此阶段不做业务处理，避免 UI 被无关帧打扰。
    constexpr int kFloatBytes = 4;                 // float 字节数
    const int channelCount = frame.payload.size() / kFloatBytes;

    if (channelCount <= 0) {
        return; // 空载荷帧：没有采样数据
    }

    QVector<domain::DataPoint> points;
    points.reserve(channelCount);

    for (int i = 0; i < channelCount; ++i) {
        const int offset = i * kFloatBytes;

        // ---- 大端 4 字节 → float ----
        // 教学点：浮点大端手工拼装（对应 C# BitConverter.ToSingle）；
        // 用位运算把 4 字节按大端序合成 32 位无符号整数，再按 float 位模式解释。
        quint32 raw = (static_cast<quint32>(
                           static_cast<quint8>(frame.payload.at(offset)))     << 24)
                    | (static_cast<quint32>(
                           static_cast<quint8>(frame.payload.at(offset + 1)))  << 16)
                    | (static_cast<quint32>(
                           static_cast<quint8>(frame.payload.at(offset + 2)))  << 8)
                    | static_cast<quint32>(
                           static_cast<quint8>(frame.payload.at(offset + 3)));
        float value = 0.0f;
        memcpy(&value, &raw, sizeof(value));   // 位模式 reinterpret（避免别名 UB）

        domain::DataPoint dp;
        dp.channelIndex = i;
        dp.value        = static_cast<double>(value);
        dp.timestamp    = QDateTime::currentDateTime();
        points.append(dp);
    }

    // ---- 跨线程投递：QVector<DataPoint> 队列连接回主线程 ----
    // 教学点：QueuedConnection 会自动把参数拷贝到接收线程；
    // QVector 隐式共享，拷贝便宜。DataService 侧连到本信号消费。
    emit pointsReady(points);
}

} // namespace services
} // namespace datascope
