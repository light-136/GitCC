/**
 * @file acquisitionworker.cpp
 * @brief V3 采集层 —— 采集工作对象实现
 *
 * ── 实现总览 ──
 * 整个类围绕一张"连接状态机 + 三个断线触发源 + 一个统一断线终局"展开：
 *
 *   连接状态机：Disconnected → Connecting → Connected → Error → Connecting → …
 *   三个断线触发源（超时类错误，权威规格规定只有它们会真正断开）：
 *     1) ConnectTimeout      —— 5s 内没连上（m_connectTimer 超时）；
 *     2) ConnectionRefused   —— socket error 信号报 ConnectionRefusedError；
 *     3) ReceiveTimeout      —— 连上后 3s 无任何数据（m_receiveTimer 超时）。
 *   统一断线终局 handleDisconnection()：停定时器 → 发 disconnected → 转 Error
 *   （或主动停止则转 Disconnected）→ 按指数退避调度重连。
 *
 *   与之相对：CRC 错 / 非法帧 / 载荷解析错 —— 只计数 + 告警（connectionError），
 *   绝不触发断线。这是权威规格的硬性规定，避免偶发坏帧把整条链路打散。
 *
 * ── 半包/粘包 ──
 *   FrameParser 内部"feed 只追加、next 只消费"，天然支持半包（数据不足返回
 *   NeedMoreData，等下一段）与粘包（一次 feed 多帧，while 循环逐帧取出）。
 *
 * ── WPF 对照 ──
 *   handleDisconnection 相当于 C# 里"连接关闭后统一清理 + 启动重连定时器"的
 *   收尾方法；scheduleReconnect 的指数退避等价于 Polly 的 WaitAndRetry 策略。
 */

#include "acquisition/acquisitionworker.h"

#include <QDebug>
#include <cstring>

#include "domain/connectionstate.h"
#include "protocol/channelconfigcodec.h"
#include "protocol/framebuilder.h"
#include "protocol/frametypes.h"

// ---------------------------------------------------------------------------
// 元类型声明（跨线程 QueuedConnection 必需，集中一处落地）
// ---------------------------------------------------------------------------
// pointsReady / channelConfigReceived 都是跨线程信号（worker 线程 → UI 线程），
// Qt 队列投递时需要用 QMetaType 系统"按名字 + 拷贝/析构函数"复制参数。这里先
// 用 Q_DECLARE_METATYPE 声明两个领域值类型；其容器 QVector<T> 由 Qt 对 QVector
// 的自动元类型模板（Q_DECLARE_METATYPE_TEMPLATE_1ARG）自动获得 Defined 能力，
// 因此在构造函数里可直接 qRegisterMetaType<QVector<T>>()。
Q_DECLARE_METATYPE(dscope::domain::DataPoint)
Q_DECLARE_METATYPE(dscope::domain::ChannelConfig)

namespace dscope {
namespace acquisition {

namespace {
// ---------------------------------------------------------------------------
// 超时时间常量（毫秒，编译期确定）
// ---------------------------------------------------------------------------

/** @brief 连接超时：connectToHost 后 5s 仍未 connected 即判 ConnectTimeout */
constexpr int kConnectTimeoutMs = 5000;

/** @brief 接收超时：连上后 3s 无任何数据即判 ReceiveTimeout */
constexpr int kReceiveTimeoutMs = 3000;

// ---------------------------------------------------------------------------
// 大端 4 字节 float 读取辅助（数据帧载荷的 value 字段：float 大端）
// ---------------------------------------------------------------------------

/**
 * @brief 从字节数组按大端序读取一个 4 字节 float，并提升为 double
 * @param data   数据源
 * @param offset 起始字节下标（调用方保证 offset+4 不越界）
 * @return 解析出的工程值（double）
 *
 * 帧内 float 用"位模式"落盘（与 Simulator 侧一致）：先拼出 32 位大端整数，
 * 再 std::memcpy 保位还原成 float，最后提升为 double（领域层 DataPoint::value
 * 是 double）。不能用 reinterpret_cast 直接取地址（字节序与对齐都不安全）。
 */
double readFloat32BE(const QByteArray &data, int offset)
{
    const quint32 bits =
        (static_cast<quint32>(static_cast<quint8>(data.at(offset)))     << 24)
        | (static_cast<quint32>(static_cast<quint8>(data.at(offset + 1))) << 16)
        | (static_cast<quint32>(static_cast<quint8>(data.at(offset + 2))) << 8)
        | static_cast<quint32>(static_cast<quint8>(data.at(offset + 3)));

    float value = 0.0f;
    std::memcpy(&value, &bits, sizeof(value));
    return static_cast<double>(value);
}
} // namespace

// ===========================================================================
// 构造 / 析构
// ===========================================================================

AcquisitionWorker::AcquisitionWorker(QObject *parent)
    : QObject(parent)
    , m_socket(new QTcpSocket(this))   // 套接字作为子对象，随 worker 一起 moveToThread
    , m_connectTimer(this)
    , m_receiveTimer(this)
{
    // ---- 元类型注册：跨线程 QueuedConnection 投递 QVector<T> 参数的运行时登记 ----
    qRegisterMetaType<QVector<dscope::domain::DataPoint>>();
    qRegisterMetaType<QVector<dscope::domain::ChannelConfig>>();

    // ---- 连接超时定时器（5s 单次）----
    m_connectTimer.setSingleShot(true);
    m_connectTimer.setInterval(kConnectTimeoutMs);
    connect(&m_connectTimer, &QTimer::timeout,
            this, &AcquisitionWorker::onConnectTimeout);

    // ---- 接收超时定时器（3s 单次，收到数据时用 start() 重置/kick）----
    m_receiveTimer.setSingleShot(true);
    m_receiveTimer.setInterval(kReceiveTimeoutMs);
    connect(&m_receiveTimer, &QTimer::timeout,
            this, &AcquisitionWorker::onReceiveTimeout);

    // ---- 套接字信号（同线程直连）----
    connect(m_socket, &QTcpSocket::connected,
            this, &AcquisitionWorker::onSocketConnected);
    connect(m_socket, &QTcpSocket::disconnected,
            this, &AcquisitionWorker::onSocketDisconnected);
    // Qt 5.12 的 error 信号与 error() getter 重名，需用 QOverload 指定取"带参信号"版本
    connect(m_socket, QOverload<QAbstractSocket::SocketError>::of(&QAbstractSocket::error),
            this, &AcquisitionWorker::onSocketError);
    connect(m_socket, &QIODevice::readyRead,
            this, &AcquisitionWorker::onReadyRead);
}

AcquisitionWorker::~AcquisitionWorker()
{
    // 析构期间安全收尾：先置停止标记并停掉全部定时器，再中止套接字。
    // 这样即使 abort() 同步触发了 disconnected，也不会再调度重连。
    m_stopRequested = true;
    m_reconnectScheduled = false;
    m_connectTimer.stop();
    m_receiveTimer.stop();
    m_socket->abort();
}

// ===========================================================================
// 对外命令：start / stop
// ===========================================================================

void AcquisitionWorker::start(const QString &host, quint16 port)
{
    // ---- 保存目标地址：后续自动重连复用 ----
    m_host = host;
    m_port = port;

    // ---- 清除"停止"意图：start 表示用户要开始采集 ----
    m_stopRequested = false;

    // ---- 取消可能残留的重连调度与接收定时器，重试计数归零 ----
    // （全新一轮连接应从退避序列 1s 重新开始，而不是继承上一轮的 4s/8s）
    m_reconnectScheduled = false;
    m_receiveTimer.stop();
    m_retryCount = 0;

    // ---- 防御：若上一次连接仍在进行（重复 start），先中止，避免并存两个连接 ----
    if (m_socket->state() != QAbstractSocket::UnconnectedState) {
        // 临时置停止标记：让这次 abort 触发的 disconnected 走"主动停止"分支，
        // 而不是误调度一次重连。
        m_stopRequested = true;
        m_socket->abort();
        m_stopRequested = false;
    }

    // ---- 状态转 Connecting，并启动 5s 连接超时 ----
    setState(dscope::domain::ConnectionState::Connecting);
    m_connectTimer.start();

    // ---- 发起异步连接（立即返回，结果由 connected/error 信号驱动）----
    m_socket->connectToHost(m_host, m_port);
}

void AcquisitionWorker::stop()
{
    // ---- 记录"主动停止"意图：断开后不再重连 ----
    m_stopRequested = true;

    // ---- 停掉所有定时器，并让已排队的重连 singleShot 失效 ----
    m_connectTimer.stop();
    m_receiveTimer.stop();
    m_reconnectScheduled = false;

    // ---- 用 abort() 立即中止（权威规格明确要求，不是 disconnectFromHost）----
    // 若套接字处于 Connected：abort 同步触发 disconnected → handleDisconnection
    //   在其中看到 m_stopRequested，落到 Disconnected 且不重连。
    // 若套接字处于 Connecting/Unconnected：abort 不触发 disconnected，下方补位。
    m_socket->abort();

    // abort 未触发 disconnected（套接字本就未连接）时，显式把状态落到位。
    if (m_socket->state() == QAbstractSocket::UnconnectedState)
        setState(dscope::domain::ConnectionState::Disconnected);
}

// ===========================================================================
// 状态机
// ===========================================================================

void AcquisitionWorker::setState(dscope::domain::ConnectionState next)
{
    // 状态未变化则不发信号（幂等，避免重复通知）。
    // 合法转移与 domain::canTransition 的转移表一致，唯一例外是 stop() 会强制
    // 从任意状态（含 Connecting）落到位 Disconnected —— 这是用户主动中止语义。
    if (m_state == next)
        return;
    m_state = next;
    emit connectionStateChanged(static_cast<int>(next));
}

// ===========================================================================
// 套接字事件
// ===========================================================================

void AcquisitionWorker::onSocketConnected()
{
    // ---- 连接成功：取消连接超时、退避计数归零 ----
    m_connectTimer.stop();
    m_retryCount = 0;

    // ---- 清空上一轮可能残留的半包缓冲（新连接从干净字节流开始）----
    // 断线时若缓冲里还留着半帧，重连后旧字节会污染新帧解析；这里重置解析器。
    m_parser = dscope::protocol::FrameParser();

    // ---- 状态转 Connected ----
    setState(dscope::domain::ConnectionState::Connected);

    // ---- 立即发通道配置查询帧（kFuncQuery + kCmdQueryChannels，空载荷）----
    // 这是"设备上报驱动 UI 渲染"的第一步：先问设备有哪些通道。
    const QByteArray frame = dscope::protocol::FrameBuilder::build(
        dscope::protocol::kFuncQuery,
        dscope::protocol::kCmdQueryChannels,
        QByteArray());
    if (!frame.isEmpty()) {
        m_socket->write(frame);
    } else {
        // 空载荷永不超长，build 不会失败；保留防御告警。
        qWarning() << "AcquisitionWorker: 通道查询帧构建失败";
    }

    // ---- 启动接收超时（3s 无数据则判定 ReceiveTimeout）----
    m_receiveTimer.start();

    // ---- 通知上层"已连接" ----
    emit connected();
}

void AcquisitionWorker::onSocketDisconnected()
{
    // 套接字底层断开（远端关闭 / 本端 abort 引发）：统一走断线终局。
    handleDisconnection();
}

void AcquisitionWorker::onSocketError(QAbstractSocket::SocketError socketError)
{
    switch (socketError) {
    case QAbstractSocket::ConnectionRefusedError:
        // 连接被拒：超时类错误 → 发告警，并确定性走断线终局转 Error + 退避重连。
        // 不依赖"disconnected 是否必然随后触发"（若也触发，重连调度有守卫吸收）。
        emit connectionError(static_cast<int>(dscope::domain::ErrorCode::ConnectionRefused));
        m_socket->abort();          // 确保套接字回到 Unconnected（若已失败通常是 no-op）
        handleDisconnection();
        break;

    case QAbstractSocket::SocketTimeoutError:
        // 套接字级超时：映射为连接超时告警（本设计用自有定时器主控超时，
        // 此分支是 socket 自身抛出的兜底）。
        emit connectionError(static_cast<int>(dscope::domain::ErrorCode::ConnectTimeout));
        break;

    default:
        // 其它错误（RemoteHostClosed / Network / HostNotFound 等）：
        // 不单独告警，交给 disconnected 信号统一处理断线/重连。
        break;
    }
}

void AcquisitionWorker::onReadyRead()
{
    // 收到任意字节都视为"链路有活动"：重置接收超时定时器（单次定时器，start 即重计）。
    m_receiveTimer.start();

    // 读空 socket 缓冲并喂给流式解析器（半包/粘包/坏帧都交给它）。
    const QByteArray chunk = m_socket->readAll();
    m_parser.feed(chunk);

    // 循环取帧：一次 readyRead 可能带多帧（粘包），逐帧派发。
    dscope::protocol::ParsedFrame frame;
    dscope::protocol::FrameError  frameError;
    while (true) {
        const dscope::protocol::FrameParser::Status status = m_parser.next(frame, frameError);

        if (status == dscope::protocol::FrameParser::Status::Frame) {
            dispatchFrame(frame);                       // 合法帧：按 FUNC 分发
        } else if (status == dscope::protocol::FrameParser::Status::Error) {
            handleFrameError(frameError);               // 坏帧：只计数告警，不断线
        } else {
            break;                                      // NeedMoreData：半包，等更多数据
        }
    }
}

// ===========================================================================
// 超时处理（三个断线触发源）
// ===========================================================================

void AcquisitionWorker::onConnectTimeout()
{
    // 5s 内未连接成功：判定 ConnectTimeout，abort 后走重连。
    emit connectionError(static_cast<int>(dscope::domain::ErrorCode::ConnectTimeout));

    // Connecting 状态下 abort 不会触发 disconnected 信号，故显式走断线终局。
    m_socket->abort();
    handleDisconnection();
}

void AcquisitionWorker::onReceiveTimeout()
{
    // 3s 内无任何数据：判定 ReceiveTimeout，abort 后走重连。
    emit connectionError(static_cast<int>(dscope::domain::ErrorCode::ReceiveTimeout));

    // 显式走断线终局：不依赖"Connected 下 abort 同步触发 disconnected"。
    // 若 socket 在定时器触发与 abort 之间已异常变为 UnconnectedState（对端刚关闭
    // 的竞态窗口），abort 不再发 disconnected，显式调用才能保证必然重连。
    // handleDisconnection 内部有 m_reconnectScheduled 守卫，幂等不会双重重连。
    m_socket->abort();
    handleDisconnection();
}

// ===========================================================================
// 断线终局 + 指数退避重连
// ===========================================================================

void AcquisitionWorker::handleDisconnection()
{
    // 停掉连接/接收两个定时器（断线后它们不再有意义）。
    m_connectTimer.stop();
    m_receiveTimer.stop();

    // 通知上层：底层连接已断开（主动停止与意外断线都通知）。
    emit disconnected();

    if (m_stopRequested) {
        // 用户主动停止：终态 Disconnected，不再重连。
        setState(dscope::domain::ConnectionState::Disconnected);
        return;
    }

    // 意外断线：转 Error，并按指数退避调度重连。
    setState(dscope::domain::ConnectionState::Error);
    scheduleReconnect();
}

void AcquisitionWorker::scheduleReconnect()
{
    // 已调度则跳过（防止 abort 与 disconnected 双触发造成重复重连）。
    if (m_reconnectScheduled)
        return;
    m_reconnectScheduled = true;

    // 用当前重试次数计算退避等待（1s→2s→4s→…封顶 30s，domain 已实现），再递增。
    const int delay = dscope::domain::nextRetryDelayMs(m_retryCount);
    ++m_retryCount;

    // QTimer::singleShot 以 this 为上下文：worker 析构后回调不再触发。
    QTimer::singleShot(delay, this, [this]() {
        // 若已被 stop()/start() 取消（m_reconnectScheduled 被清），放弃本次重连。
        if (!m_reconnectScheduled)
            return;
        m_reconnectScheduled = false;

        // 等待期间用户又点了停止：不再重连。
        if (m_stopRequested)
            return;

        doReconnect();
    });
}

void AcquisitionWorker::doReconnect()
{
    // 真正发起重连：状态转 Connecting，重新启动连接超时并 connectToHost。
    setState(dscope::domain::ConnectionState::Connecting);
    m_connectTimer.start();
    m_socket->connectToHost(m_host, m_port);
}

// ===========================================================================
// 帧分发与载荷解析
// ===========================================================================

void AcquisitionWorker::dispatchFrame(const dscope::protocol::ParsedFrame &frame)
{
    switch (frame.func) {
    case dscope::protocol::kFuncData:
        // 实时数据帧 → DataPoint 列表
        parseDataFrame(frame.payload);
        break;

    case dscope::protocol::kFuncQueryResponse:
        // 查询响应帧：当前只关心"通道配置查询"这一种子命令
        if (frame.cmd == dscope::protocol::kCmdQueryChannels)
            parseChannelConfig(frame.payload);
        break;

    default:
        // 未识别的 FUNC：静默忽略，不影响链路状态。
        break;
    }
}

void AcquisitionWorker::parseDataFrame(const QByteArray &payload)
{
    // 数据帧载荷：DATA = [通道数 N:1B] + N × [channelIndex:1B + value:4B float 大端]
    if (payload.size() < 1) {
        // 连通道数字节都没有：结构非法，告警不触发断线。
        emit connectionError(static_cast<int>(dscope::domain::ErrorCode::ParseError));
        ++m_parseErrorCount;
        return;
    }

    const int channelCount = static_cast<quint8>(payload.at(0));
    // 每通道固定 5 字节（index 1 + value 4），载荷长度必须精确匹配。
    const int expectedSize = 1 + channelCount * 5;
    if (channelCount < 1 || payload.size() != expectedSize) {
        // 通道数或长度非法：告警（ParseError），不触发断线。
        emit connectionError(static_cast<int>(dscope::domain::ErrorCode::ParseError));
        ++m_parseErrorCount;
        return;
    }

    // 主机到达时刻：本帧所有数据点共享同一时刻（它们是同一批到达的）。
    const QDateTime arrival = QDateTime::currentDateTime();

    QVector<dscope::domain::DataPoint> points;
    points.reserve(channelCount);

    int offset = 1;   // 跳过第 0 字节的"通道数 N"
    for (int i = 0; i < channelCount; ++i) {
        dscope::domain::DataPoint dp;
        dp.channelIndex  = static_cast<quint8>(payload.at(offset));
        dp.value         = readFloat32BE(payload, offset + 1);
        dp.deviceTs      = QDateTime();   // 第一阶段 Simulator 帧不含设备时间戳，留无效
        dp.hostArrivalTs = arrival;       // 主机收到本帧的时刻（采集线程填写）
        points.append(dp);
        offset += 5;                      // 跳到下一个通道
    }

    emit pointsReady(points);
}

void AcquisitionWorker::parseChannelConfig(const QByteArray &payload)
{
    // 查询响应载荷 → 协议层 ChannelConfigInfo 列表（codec 已做长度/结构校验）。
    QVector<dscope::protocol::ChannelConfigInfo> infos;
    if (!dscope::protocol::ChannelConfigCodec::decode(payload, infos)) {
        // 配置载荷解析失败：告警（ParseError），不触发断线。
        emit connectionError(static_cast<int>(dscope::domain::ErrorCode::ParseError));
        ++m_parseErrorCount;
        return;
    }

    // 映射成领域层 ChannelConfig（丢弃协议层的 index 字段 —— 领域层按位置/顺序使用）。
    QVector<dscope::domain::ChannelConfig> channels;
    channels.reserve(infos.size());
    for (const dscope::protocol::ChannelConfigInfo &info : infos) {
        dscope::domain::ChannelConfig cfg;
        cfg.name     = info.name;
        cfg.unit     = info.unit;
        cfg.rangeMin = static_cast<double>(info.rangeMin);   // float → double
        cfg.rangeMax = static_cast<double>(info.rangeMax);
        channels.append(cfg);
    }

    emit channelConfigReceived(channels);
}

void AcquisitionWorker::handleFrameError(dscope::protocol::FrameError error)
{
    // 单次坏帧：只计数 + 告警，不触发断线（权威规格硬性规定）。
    // 坏帧已被 FrameParser 消费掉，链路继续收下一帧。
    if (error == dscope::protocol::FrameError::CrcError) {
        ++m_crcErrorCount;
        emit connectionError(static_cast<int>(dscope::domain::ErrorCode::CrcError));
    } else {   // FrameError::InvalidHeader（LEN 超限 / VERSION 不符）→ 非法帧
        ++m_parseErrorCount;
        emit connectionError(static_cast<int>(dscope::domain::ErrorCode::InvalidFrame));
    }
}

} // namespace acquisition
} // namespace dscope
