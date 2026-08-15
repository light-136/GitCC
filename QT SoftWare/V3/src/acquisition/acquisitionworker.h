/**
 * @file acquisitionworker.h
 * @brief V3 采集层 —— 采集工作对象（E2E 数据链路核心粘合点）声明
 *
 * ────────────────────────────────────────────────────────────
 * 本类是 E2E 数据链路的核心粘合点，位置如下：
 *
 *   Simulator ──TCP──▶ [ AcquisitionWorker ] ──▶ FrameParser ──▶ DataPoint
 *                                    │
 *                                    └──▶ ChannelConfigCodec ──▶ ChannelConfig
 *
 * ── 架构决策（权威规格硬性规定）──
 *   不建独立 TcpClient。AcquisitionWorker 是"单一 QObject"，内部直接持有
 *   QTcpSocket，并把整个对象 moveToThread 到采集线程 —— 套接字与 worker
 *   同线程，socket 的信号（connected/disconnected/readyRead/error）与 worker
 *   的槽直接连接，零跨线程跳板，也消除了"client 与 worker 生命周期不同步"
 *   的隐患。
 *
 * ── 线程模型 ──
 *   worker 被 moveToThread 后，所有成员（含 m_socket 与两个 QTimer）都活在
 *   采集线程。外部（UI 线程）通过 QueuedConnection 调 start()/stop()，worker
 *   通过 QueuedConnection 把 pointsReady / channelConfigReceived 回抛给 UI。
 *   因此 QVector<DataPoint> / QVector<ChannelConfig> 必须注册元类型（见 .cpp）。
 *
 * ── 职责边界 ──
 *   只做"传输 + 帧分发 + 载荷解包"：把协议层解析出的 ParsedFrame 按 FUNC 转成
 *   领域层的 DataPoint / ChannelConfig 后上抛。报警判定（AlarmEngine）等纯业务
 *   逻辑不在这里，保持采集层单一职责。
 *
 * ── WPF 对照 ──
 *   AcquisitionWorker 相当于 C# 里一个跑在后台线程的"数据接入服务"：
 *   start/stop 是它对外暴露的异步命令，connected/disconnected/pointsReady 是
 *   它向 UI 报告的回调事件，等价于 WPF 的 ICommand + event 组合。
 */

#pragma once

#include <QObject>
#include <QString>
#include <QTimer>
#include <QTcpSocket>
#include <QVector>

#include "domain/channelconfig.h"
#include "domain/connectionstate.h"
#include "domain/datapoint.h"
#include "domain/errorcode.h"
#include "protocol/frameparser.h"

namespace dscope {
namespace acquisition {

/**
 * @class AcquisitionWorker
 * @brief 采集工作对象：管理 TCP 连接、收发协议帧、解包成领域数据并上抛
 *
 * 生命周期由调用方持有并 moveToThread 到采集线程。对外只有 start/stop 两个
 * 命令，内部维护一张连接状态机（Disconnected/Connecting/Connected/Error），
 * 失败时按指数退避自动重连。
 */
class AcquisitionWorker : public QObject
{
    Q_OBJECT
public:
    /**
     * @brief 构造函数：创建 QTcpSocket、配置两个超时定时器、注册元类型、连接信号
     * @param parent 父对象（通常为空；生命周期由调用方管理）
     */
    explicit AcquisitionWorker(QObject *parent = nullptr);

    /**
     * @brief 析构函数：安全停止定时器并中止套接字，避免析构期间再触发回调
     */
    ~AcquisitionWorker() override;

public slots:
    /**
     * @brief 发起异步连接并开始采集
     * @param host 设备 IP 或主机名
     * @param port 设备 TCP 端口
     * @note 保存地址用于后续自动重连；立即转 Connecting 并启动 5s 连接超时。
     */
    void start(const QString &host, quint16 port);

    /**
     * @brief 停止采集：停定时器、abort 套接字、状态转 Disconnected，不再重连
     */
    void stop();

signals:
    /** @brief 底层 TCP 连接建立成功（在 worker 线程发出） */
    void connected();

    /** @brief 底层 TCP 连接断开（主动停止或意外断线都会发出） */
    void disconnected();

    /**
     * @brief 连接状态机变化通知
     * @param state dscope::domain::ConnectionState 的 int 值
     *              （Disconnected=0 / Connecting=1 / Connected=2 / Error=3）
     */
    void connectionStateChanged(int state);

    /**
     * @brief 错误告警
     * @param code dscope::domain::ErrorCode 的 int 值
     *             （连接超时/连接被拒/接收超时/CRC 错/解析错等）
     * @note 仅告警；是否断线由错误类别决定（见 .cpp 错误策略）。
     */
    void connectionError(int code);

    /**
     * @brief 实时数据点批量上抛（跨线程 QueuedConnection）
     * @param points 本帧解析出的若干 DataPoint（hostArrivalTs 已填主机到达时刻）
     */
    void pointsReady(const QVector<dscope::domain::DataPoint> &points);

    /**
     * @brief 通道配置响应上抛（跨线程 QueuedConnection）
     * @param channels 设备上报的通道配置列表（index 已在映射时丢弃）
     */
    void channelConfigReceived(const QVector<dscope::domain::ChannelConfig> &channels);

private slots:
    /** @brief socket 连接成功：发通道查询帧、转 Connected、启动接收超时 */
    void onSocketConnected();
    /** @brief socket 断开（远端关闭或本端 abort）：走统一断线终局 */
    void onSocketDisconnected();
    /** @brief socket 错误信号：按错误类别告警/触发断开 */
    void onSocketError(QAbstractSocket::SocketError socketError);
    /** @brief 有数据到达：喂解析器、逐帧派发、重置接收超时 */
    void onReadyRead();
    /** @brief 连接超时（5s 未连上）：判定 ConnectTimeout 并走重连 */
    void onConnectTimeout();
    /** @brief 接收超时（3s 无数据）：判定 ReceiveTimeout 并走重连 */
    void onReceiveTimeout();
    /** @brief 退避计时到期后真正发起重连 */
    void doReconnect();

private:
    /** @brief 推进连接状态机并发出 connectionStateChanged（状态未变则不重复发） */
    void setState(dscope::domain::ConnectionState next);
    /** @brief 统一断线终局：停定时器 → 发 disconnected → 转 Error/Disconnected → 调度重连 */
    void handleDisconnection();
    /** @brief 按指数退避调度一次重连（防重复调度） */
    void scheduleReconnect();
    /** @brief 按 FUNC 把一帧分发到对应的载荷解析器 */
    void dispatchFrame(const dscope::protocol::ParsedFrame &frame);
    /** @brief 解析实时数据帧载荷 → QVector<DataPoint> 并上抛 */
    void parseDataFrame(const QByteArray &payload);
    /** @brief 解析通道配置响应载荷 → QVector<ChannelConfig> 并上抛 */
    void parseChannelConfig(const QByteArray &payload);
    /** @brief 处理坏帧（CRC/非法帧头）：只计数告警，不触发断线 */
    void handleFrameError(dscope::protocol::FrameError error);

private:
    QTcpSocket *m_socket = nullptr;   ///< 底层 TCP 套接字（child，随 worker 一起 moveToThread）
    QTimer      m_connectTimer;       ///< 连接超时定时器（5s 单次）
    QTimer      m_receiveTimer;       ///< 接收超时定时器（3s 单次，收到数据即重置）
    dscope::protocol::FrameParser m_parser;  ///< 流式帧解析器（半包/粘包/坏帧都处理）

    QString m_host;                              ///< 目标主机（保存用于自动重连）
    quint16 m_port = 0;                          ///< 目标端口（保存用于自动重连）

    dscope::domain::ConnectionState m_state
        = dscope::domain::ConnectionState::Disconnected;  ///< 当前连接状态机状态

    int  m_retryCount         = 0;    ///< 重试次数（连接成功后归零；退避序列 1s→2s→4s→…封顶 30s）
    bool m_stopRequested      = true; ///< 是否已请求停止（true 时断线不再重连）
    bool m_reconnectScheduled = false;///< 是否已有待执行的重连调度（防重复）

    int m_crcErrorCount   = 0;        ///< CRC 坏帧累计计数（仅统计，不断线）
    int m_parseErrorCount = 0;        ///< 非法帧/解析错误累计计数（仅统计，不断线）
};

} // namespace acquisition
} // namespace dscope
