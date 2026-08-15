/**
 * @file simulatordevice.cpp
 * @brief 模拟采集设备实现（V3）
 *
 * 开发思路：
 *   1. 数据生成与异常注入分层：buildDataFrame() 只构造"正常帧"，
 *      FaultInjector::inject() 再把正常帧变换为异常字节流，二者解耦；
 *   2. 周期广播用 QTimer（20Hz 默认），每次组帧后对每个已连接客户端逐段 write，
 *      模拟真实链路的多次发送/到达；
 *   3. 客户端请求用"每客户端一个 FrameParser"解析（V3 三态 next），互不串帧；
 *      命中通道配置查询就回 0x83 配置帧，其余请求忽略（向前兼容）。
 *
 * ── 数据帧载荷契约（与采集端一致）──
 *   [通道数 N:1B] + N × [channelIndex:1B + value:4B float 大端]
 *   每通道 5 字节；float 用大端（高字节在前）编码。
 */

#include "simulatordevice.h"

#include "protocol/framebuilder.h"
#include "protocol/frametypes.h"

#include <QAbstractSocket>
#include <QHostAddress>
#include <QString>
#include <QTcpServer>
#include <QTcpSocket>
#include <QTimer>

#include <cmath>
#include <cstring>

namespace dscope {
namespace simulator {

namespace {

/** @brief 数据帧子命令 CMD：0x00（协议契约规定；frametypes.h 未定义具名常量） */
constexpr quint8 kCmdData = 0x00;

/** @brief 每帧相位步进：50ms 一帧下波形平滑且肉眼可分辨（约 0.32Hz 起） */
constexpr float kPhaseStep = 0.1f;

/**
 * @brief 单通道正弦波参数（中心值 / 幅度 / 角频率系数 / 初相位）
 *
 * 各通道参数不同，使四路波形相位、幅度、中心错开，仪表与曲线肉眼可区分。
 * 通道 0~2 中心 50、幅度 < 50，值域落在 (0, 100) 内（正常信号，不报警）；
 * 通道 3（转速）中心故意抬到 85、幅度 30 → 55~115，周期性越过量程上限 100，
 * 用于在 E2E 里演示"越限报警 → LED 亮 / 报警中心记录触发与恢复"的完整链路
 * （配合采集端默认阈值 = 量程上限 100、回差 = 量程 5%）。
 */
struct WaveParam {
    float center;      ///< 波形中心值
    float amplitude;   ///< 幅度（峰值偏离中心的大小）
    float frequency;   ///< 角频率系数（越大摆动越快）
    float phase;       ///< 初相位（弧度）
};

const WaveParam kWaves[4] = {
    { 50.0f, 40.0f, 1.0f, 0.0f },      // 通道0 温度：10~90（正常，不越限）
    { 50.0f, 30.0f, 1.3f, 1.5708f },   // 通道1 压力：20~80（正常）
    { 50.0f, 25.0f, 2.0f, 3.1416f },   // 通道2 流量：25~75（正常）
    { 85.0f, 30.0f, 0.7f, 0.7854f },   // 通道3 转速：55~115（周期性越 100，演示报警）
};

/**
 * @brief float → 大端序 4 字节追加到载荷（协议契约规定载荷内 float 大端）
 * @param out   目标载荷（非 const 引用：函数会修改它）
 * @param value 要写入的浮点值
 *
 * 用 std::memcpy 取 float 的内存位模式（不改变数值），再按高字节在前逐字节写出。
 */
void appendFloatBE(QByteArray &out, float value)
{
    quint32 bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));
    out.append(static_cast<char>((bits >> 24) & 0xFF));   // 高字节在前
    out.append(static_cast<char>((bits >> 16) & 0xFF));
    out.append(static_cast<char>((bits >> 8) & 0xFF));
    out.append(static_cast<char>(bits & 0xFF));
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

    // 默认 4 通道配置（温度/压力/流量/转速，0~100，供通道配置查询响应）
    rebuildChannelConfig();
}

SimulatorDevice::~SimulatorDevice() = default;

bool SimulatorDevice::start(quint16 port)
{
    // 监听所有网卡；port=0 由系统分配随机可用端口（测试场景）
    if (!m_server->listen(QHostAddress::Any, port))
        return false;   // 端口被占用 / 无权限 → 监听失败

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

void SimulatorDevice::setFrameInterval(int ms)
{
    m_frameIntervalMs = ms < 1 ? 1 : ms;   // 防御：间隔至少 1ms
    if (m_timer->isActive())
        m_timer->setInterval(m_frameIntervalMs);   // 运行中修改即时生效
}

void SimulatorDevice::setChannelCount(int count)
{
    m_channelCount = count < 1 ? 1 : count;   // 防御：至少 1 通道
    rebuildChannelConfig();                    // 通道数变化 → 配置随之重建
}

void SimulatorDevice::setFaultEnabled(bool enabled)
{
    m_faultEnabled = enabled;
}

void SimulatorDevice::setFaultType(FaultType type)
{
    m_faultType = type;
}

void SimulatorDevice::setFaultCycling(bool cycling)
{
    m_faultCycling = cycling;
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
        m_clientParsers.insert(client, protocol::FrameParser());   // 每客户端独立解析器，互不串帧
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
    // 载荷布局：通道数 N(1B) + N × [channelIndex(1B) + value(4B float 大端)]
    QByteArray payload;
    payload.reserve(1 + m_channelCount * 5);
    payload.append(static_cast<char>(m_channelCount));

    for (int ch = 0; ch < m_channelCount; ++ch) {
        payload.append(static_cast<char>(ch));   // 通道序号

        const WaveParam &w = kWaves[ch % 4];     // 超 4 通道循环复用波形参数（防御，不越界）
        const float v = w.center + w.amplitude
            * static_cast<float>(std::sin(m_phase * w.frequency + w.phase));
        appendFloatBE(payload, v);
    }

    return protocol::FrameBuilder::build(protocol::kFuncData, kCmdData, payload);
}

void SimulatorDevice::onTick()
{
    m_phase += kPhaseStep;   // 相位推进：波形随时间变化

    const QByteArray frame = buildDataFrame();
    emit frameGenerated(m_frameSeq, frame);   // 发出注入前的正常帧（测试观察用）

    // 故障注入：轮询模式下每帧切换到下一异常类型，覆盖全部五类
    if (m_faultEnabled && m_faultCycling)
        m_faultType = FaultInjector::nextType(m_faultType);

    const QVector<QByteArray> chunks = m_faultEnabled
        ? FaultInjector::inject(m_faultType, frame)
        : QVector<QByteArray>{ frame };

    ++m_frameSeq;
    broadcast(chunks);
}

void SimulatorDevice::broadcast(const QVector<QByteArray> &chunks)
{
    // 对每个仍处于连接态的客户端逐段 write（每段一次 write，模拟真实多次发送/到达）
    for (QTcpSocket *client : m_clients) {
        if (client->state() == QAbstractSocket::ConnectedState) {
            for (const QByteArray &chunk : chunks)
                client->write(chunk);
        }
    }
}

// ---------------------------------------------------------------------------
// 客户端请求处理（设备主动上报通道配置）
// ---------------------------------------------------------------------------

void SimulatorDevice::handleClientRequest(QTcpSocket *client)
{
    protocol::FrameParser &parser = m_clientParsers[client];
    parser.feed(client->readAll());

    protocol::ParsedFrame req;
    protocol::FrameError err;
    for (;;) {
        const protocol::FrameParser::Status st = parser.next(req, err);
        if (st == protocol::FrameParser::Status::NeedMoreData)
            break;   // 数据不足（半包）：保留未消费字节，等下次 readyRead

        if (st == protocol::FrameParser::Status::Frame) {
            // 当前支持：通道配置查询（FUNC=0x03, CMD=0x01）→ 响应 0x83
            if (req.func == protocol::kFuncQuery
                && req.cmd == protocol::kCmdQueryChannels) {
                const QByteArray payload = protocol::ChannelConfigCodec::encode(m_channelConfig);
                if (!payload.isEmpty()) {
                    client->write(protocol::FrameBuilder::build(
                        protocol::kFuncQueryResponse, protocol::kCmdQueryChannels, payload));
                }
            }
            // 其它请求暂不支持：忽略（设备不崩溃、不回错误，保持协议向前兼容）
        }
        // Status::Error：坏帧已被解析器消费，忽略并继续处理剩余字节
    }
}

void SimulatorDevice::rebuildChannelConfig()
{
    // 默认 4 通道工业信号（名称/单位/量程），与采集端监控页契约一致。
    // 设备是通道配置的唯一事实来源：主机查询后按真实配置渲染，消除 UI 侧硬编码。
    static const struct {
        const char *name;
        const char *unit;
        float min;
        float max;
    } kDefaultChannels[4] = {
        { "温度", "℃",    0.0f, 100.0f },
        { "压力", "kPa",  0.0f, 100.0f },
        { "流量", "L/min", 0.0f, 100.0f },
        { "转速", "rpm",  0.0f, 100.0f },
    };

    m_channelConfig.clear();
    for (int i = 0; i < m_channelCount; ++i) {
        protocol::ChannelConfigInfo cfg;
        cfg.index = static_cast<quint8>(i);
        const int src = i % 4;   // 超 4 通道循环复用模板（防御，不越界）
        cfg.name = QString::fromUtf8(kDefaultChannels[src].name);
        cfg.unit = QString::fromUtf8(kDefaultChannels[src].unit);
        cfg.rangeMin = kDefaultChannels[src].min;
        cfg.rangeMax = kDefaultChannels[src].max;
        m_channelConfig.append(cfg);
    }
}

} // namespace simulator
} // namespace dscope
