/**
 * @file simulatordevice.cpp
 * @brief 模拟采集设备实现（V2：模式 + 异常注入）
 *
 * 帧格式（协议契约）：
 *   SOF(AA55) | FUNC(0x01 采集) | CMD(0x01) | LEN(2 大端) | DATA(4×float 大端) | CRC16
 *
 * 数据生成与异常注入分层：
 *   buildDataFrame()  → 只构造"正常帧"（按模式调整通道值）；
 *   m_injector.process() → 把正常帧变换为异常字节流（CRC/粘包/分包/延迟/突变）。
 */

#include "simulator/simulatordevice.h"

#include "protocol/framebuilder.h"
#include "protocol/protocoltypes.h"

#include <QTcpServer>
#include <QTcpSocket>
#include <QHostAddress>
#include <QTimer>

#include <cmath>
#include <cstring>

namespace datascope {
namespace simulator {

namespace {
// 4 通道正弦波参数（幅度 / 角频率 / 初相位）—— 各通道数值不同便于 UI 区分
const float kAmplitudes[4]  = { 10.0f, 8.0f, 5.0f, 3.0f };
const float kFrequencies[4] = { 1.0f, 2.0f, 3.0f, 0.5f };
const float kPhases[4]      = { 0.0f, 1.5707963f, 3.1415927f, 0.7853982f };

/** @brief 报警模式：每 20 帧翻转一次"报警/恢复"相位 */
constexpr int kAlarmPhaseFrames = 20;
/** @brief 报警模式的超限值（超出 0..100 量程，触发主机报警） */
constexpr float kAlarmSurgeValue = 200.0f;

/** @brief 设备忙模式周期：每 50 帧一个忙周期 */
constexpr int kBusyPeriod = 50;
/** @brief 忙窗口长度：周期末尾连续 20 帧不输出数据（模拟设备忙/超时） */
constexpr int kBusyWindowFrames = 20;

/** @brief float → 大端序 4 字节（协议契约） */
QByteArray floatToBigEndian(float value)
{
    quint32 bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));
    QByteArray bytes(4, Qt::Uninitialized);
    bytes[0] = static_cast<char>((bits >> 24) & 0xFF);
    bytes[1] = static_cast<char>((bits >> 16) & 0xFF);
    bytes[2] = static_cast<char>((bits >> 8) & 0xFF);
    bytes[3] = static_cast<char>(bits & 0xFF);
    return bytes;
}
} // namespace

SimulatorDevice::SimulatorDevice(QObject *parent)
    : QObject(parent)
{
    m_server = new QTcpServer(this);
    m_timer  = new QTimer(this);

    // 新客户端接入 → 收集并记录连接数
    connect(m_server, &QTcpServer::newConnection,
            this, &SimulatorDevice::onNewConnection);

    // 数据帧定时器：周期组帧并广播
    connect(m_timer, &QTimer::timeout, this, &SimulatorDevice::onTick);

    // 默认 4 通道配置（名称/单位/量程 —— 供主机"通道配置查询"响应）
    rebuildChannelConfig();
}

SimulatorDevice::~SimulatorDevice() = default;

bool SimulatorDevice::start(quint16 port)
{
    // 监听所有网卡；port=0 由系统分配随机可用端口（测试场景）
    if (!m_server->listen(QHostAddress::Any, port))
        return false;

    m_timer->setInterval(m_frameIntervalMs);
    m_timer->start();
    return true;
}

quint16 SimulatorDevice::port() const
{
    return static_cast<quint16>(m_server->serverPort());
}

int SimulatorDevice::clientCount() const
{
    return m_clients.size();
}

void SimulatorDevice::setMode(SimulatorMode mode)
{
    m_mode = mode;
    // Stress 模式：高频（10ms）+ 每帧粘包（两帧合并），考验主机吞吐/重组
    if (mode == SimulatorMode::Stress) {
        setFrameInterval(10);
        setFaultConfig({ FaultType::Sticky, /*period=*/1 });
    }
}

void SimulatorDevice::setFrameInterval(int ms)
{
    m_frameIntervalMs = ms;
    if (m_timer->isActive())
        m_timer->setInterval(ms);   // 运行中修改立即生效
}

void SimulatorDevice::setChannelCount(int count)
{
    m_channelCount = count < 1 ? 1 : count;
    rebuildChannelConfig();   // 通道数变化 → 配置随之重建
}

// ---------------------------------------------------------------------------
// 客户端管理
// ---------------------------------------------------------------------------

void SimulatorDevice::onNewConnection()
{
    // 循环取出所有待处理的新连接（事件循环可能一次积累多个）
    while (m_server->hasPendingConnections()) {
        QTcpSocket *client = m_server->nextPendingConnection();
        m_clients.append(client);
        m_clientParsers.insert(client, protocol::FrameParser());
        emit clientCountChanged(m_clients.size());

        // 客户端发来请求（如通道配置查询）→ 解析并应答
        connect(client, &QTcpSocket::readyRead, this, [this, client]() {
            handleClientRequest(client);
        });

        // 断开：移除并延迟销毁（避免事件处理中析构）
        connect(client, &QTcpSocket::disconnected, this, [this, client]() {
            m_clients.removeAll(client);
            m_clientParsers.remove(client);
            emit clientCountChanged(m_clients.size());
            client->deleteLater();
        });
    }
}

// ---------------------------------------------------------------------------
// 数据帧生成与广播
// ---------------------------------------------------------------------------

QByteArray SimulatorDevice::buildDataFrame()
{
    // 载荷：4 通道 × 4 字节 float 大端（Alarm 模式可把通道 0 推到超限）
    QByteArray payload;
    payload.reserve(m_channelCount * 4);

    for (int ch = 0; ch < m_channelCount; ++ch) {
        float v = kAmplitudes[ch % 4]
            * static_cast<float>(std::sin(m_phase * kFrequencies[ch % 4] + kPhases[ch % 4]));

        // Alarm 模式：交替"报警/恢复"相位 —— 报警相位把通道 0 推到超量程
        if (m_mode == SimulatorMode::Alarm && ch == 0 && m_alarmActive)
            v = kAlarmSurgeValue;

        payload += floatToBigEndian(v);
    }
    return datascope::protocol::FrameBuilder::build(0x01, 0x01, payload);
}

void SimulatorDevice::onTick()
{
    m_phase += 0.05;   // 相位步进：波形随时间变化

    // Alarm 模式相位翻转：每 kAlarmPhaseFrames 帧切换 报警/恢复
    if (m_mode == SimulatorMode::Alarm) {
        m_alarmCounter = (m_alarmCounter + 1) % kAlarmPhaseFrames;
        if (m_alarmCounter == 0)
            m_alarmActive = !m_alarmActive;   // 报警 → 恢复 → 报警……
    }

    // Busy 模式忙窗口：设备"忙"——静默不输出任何数据帧（连接仍保持）
    if (m_mode == SimulatorMode::Busy && inBusyWindow()) {
        ++m_frameSeq;   // 帧序号照常推进，保证忙窗口边界确定
        return;
    }

    // 构建正常帧 → 注入异常（Error 模式按 FaultConfig 变换字节流）
    const QByteArray frame = buildDataFrame();
    emit frameGenerated(m_frameSeq, frame);

    // 断线注入：触发周期内主动断开所有客户端（主机应感知断线并自动重连）。
    // 断开后本帧不再发送——设备都断了，自然发不出数据。
    if (disconnectTriggered()) {
        for (QTcpSocket *client : m_clients) {
            if (client->state() == QAbstractSocket::ConnectedState)
                client->disconnectFromHost();
        }
        ++m_frameSeq;
        return;
    }

    const QVector<QByteArray> chunks = m_injector.process(m_frameSeq, frame);
    ++m_frameSeq;

    // 广播到所有仍处于连接态的客户端
    for (QTcpSocket *client : m_clients) {
        if (client->state() == QAbstractSocket::ConnectedState)
            writeChunks(client, chunks);
    }
}

void SimulatorDevice::writeChunks(QTcpSocket *client, const QVector<QByteArray> &chunks)
{
    // 每段字节一次 write（模拟真实网络的多次发送/到达）
    for (const QByteArray &chunk : chunks)
        client->write(chunk);
}

bool SimulatorDevice::disconnectTriggered() const
{
    // 与 FaultInjector 的 shouldTrigger 同规则：period>0 时按帧序号取模触发
    const FaultConfig cfg = m_injector.config();
    return cfg.type == FaultType::Disconnect && cfg.period > 0
        && m_frameSeq % cfg.period == 0;
}

bool SimulatorDevice::inBusyWindow() const
{
    const int pos = m_frameSeq % kBusyPeriod;
    return pos >= kBusyPeriod - kBusyWindowFrames;   // 周期末尾 20 帧静默
}

// ---------------------------------------------------------------------------
// 客户端请求处理（V2-执行③：设备主动上报通道配置）
// ---------------------------------------------------------------------------

void SimulatorDevice::handleClientRequest(QTcpSocket *client)
{
    // 每客户端独立解析器：不同客户端的字节流不会互相污染
    protocol::FrameParser &parser = m_clientParsers[client];
    parser.feed(client->readAll());

    protocol::ProtocolFrame req;
    while (parser.nextFrame(req)) {
        // 当前支持：通道配置查询（FUNC=0x03, CMD=0x01）→ 响应 0x83
        if (req.func == protocol::kFuncQuery
            && req.cmd == protocol::kCmdQueryChannels) {
            const QByteArray payload = protocol::ChannelConfigCodec::encode(m_channelConfig);
            if (!payload.isEmpty()) {
                const QByteArray resp = protocol::FrameBuilder::build(
                    protocol::kFuncQueryResponse, protocol::kCmdQueryChannels, payload);
                client->write(resp);
            }
        }
        // 其它请求暂不支持：忽略（设备不崩溃、不回复错误，保持协议向前兼容）
    }
}

void SimulatorDevice::rebuildChannelConfig()
{
    // 默认 4 通道工业信号：名称/单位/量程与"监控页默认通道"对齐，
    // 让"设备上报配置 → 主机渲染"链路端到端一致（消除 UI 侧硬编码的依据）。
    static const struct {
        const char *name;
        const char *unit;
        float min;
        float max;
    } kDefaultChannels[4] = {
        { "温度", "℃",    0.0f,  100.0f },
        { "压力", "MPa",  0.0f,   10.0f },
        { "流量", "m3/h", 0.0f,   50.0f },
        { "振动", "mm/s", 0.0f,    5.0f },
    };

    m_channelConfig.clear();
    for (int i = 0; i < m_channelCount; ++i) {
        protocol::ChannelConfigInfo cfg;
        cfg.index = static_cast<quint8>(i);
        const int src = i % 4;   // 超过 4 通道时循环使用模板（防御：不越界）
        cfg.name = QString::fromUtf8(kDefaultChannels[src].name);
        cfg.unit = QString::fromUtf8(kDefaultChannels[src].unit);
        cfg.rangeMin = kDefaultChannels[src].min;
        cfg.rangeMax = kDefaultChannels[src].max;
        m_channelConfig.append(cfg);
    }
}

} // namespace simulator
} // namespace datascope
