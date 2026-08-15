/**
 * @file simulatordevice.h
 * @brief 可复用模拟采集设备（V3）：TCP 服务端 + 周期数据帧 + 异常注入
 *
 * ────────────────────────────────────────────────────────────
 * 为什么把模拟设备做成类
 * ────────────────────────────────────────────────────────────
 * 若把"发数据"写成 main.cpp 里的一段过程式代码，就只能发正常帧、也无法被
 * 测试复用。抽成 SimulatorDevice（QObject + QTcpServer）后：
 *   - 独立程序（simulator/main.cpp）用它：命令行启动，供采集端 E2E 联调；
 *   - 集成测试也用它：随机端口内嵌启动，直接验证"异常设备 → 客户端行为"的
 *     真实网络链路；
 *   - 异常注入（FaultInjector）可独立开关/切换，测试可精确复现单类故障。
 *
 * ── 职责边界 ──
 *   负责：监听端口、管理客户端连接、周期组帧广播、应答通道配置查询、注入异常。
 *   不负责：报警/业务判断（那是采集端 domain 的活）、UI 渲染（本程序无 GUI）。
 *
 * ── 数据帧契约（与采集端一致）──
 *   FUNC=kFuncData(0x01)，CMD=0x00；
 *   载荷 = [通道数 N:1B] + N × [channelIndex:1B + value:4B float 大端]。
 */

#pragma once

#include <QByteArray>
#include <QHash>
#include <QObject>
#include <QVector>

#include "faultinjector.h"
#include "protocol/channelconfigcodec.h"
#include "protocol/frameparser.h"

class QTcpServer;
class QTcpSocket;
class QTimer;

namespace dscope {
namespace simulator {

/**
 * @class SimulatorDevice
 * @brief 模拟采集设备：TCP 服务端 + 周期数据帧 + 异常注入
 *
 * 用法（独立程序）：
 *   SimulatorDevice dev; dev.start(45678); return app.exec();
 * 用法（集成测试）：
 *   SimulatorDevice dev; QVERIFY(dev.start(0));  // 0 = 系统分配随机端口
 *   ... 连接 127.0.0.1:dev.port() 收发帧 ...
 */
class SimulatorDevice : public QObject
{
    Q_OBJECT

public:
    explicit SimulatorDevice(QObject *parent = nullptr);
    ~SimulatorDevice() override;

    /**
     * @brief 启动监听并开始周期广播
     * @param port 监听端口；0 = 系统分配随机可用端口（测试用）
     * @return 监听是否成功（端口被占用/无权限返回 false）
     */
    bool start(quint16 port = 0);

    /** @brief 实际监听端口（start 后有效；port=0 时为系统分配值） */
    quint16 port() const;

    /** @brief 当前已连接客户端数量 */
    int clientCount() const;

    // ---- 运行配置 ----

    /** @brief 数据帧发送间隔（毫秒）；运行中修改即时生效，至少 1ms */
    void setFrameInterval(int ms);

    /** @brief 当前帧间隔（毫秒） */
    int frameInterval() const { return m_frameIntervalMs; }

    /** @brief 设置通道数（至少 1）；变化会同步重建通道配置 */
    void setChannelCount(int count);

    /** @brief 当前通道数 */
    int channelCount() const { return m_channelCount; }

    // ---- 异常注入开关 ----

    /** @brief 开关异常注入；关闭时只发正常帧 */
    void setFaultEnabled(bool enabled);

    /** @brief 异常注入是否开启 */
    bool faultEnabled() const { return m_faultEnabled; }

    /** @brief 设定固定注入的异常类型（轮询关闭时生效） */
    void setFaultType(FaultType type);

    /** @brief 当前异常类型 */
    FaultType faultType() const { return m_faultType; }

    /** @brief 开关"轮询注入"：开启后每帧自动切换到下一异常类型（覆盖全部五类） */
    void setFaultCycling(bool cycling);

    /** @brief 轮询注入是否开启 */
    bool faultCycling() const { return m_faultCycling; }

    /**
     * @brief 当前通道配置（名称/单位/量程），供查询响应与联调观察
     * @return 协议层通道配置列表（主机查询时以此为响应数据源）
     */
    QVector<protocol::ChannelConfigInfo> channelConfig() const { return m_channelConfig; }

signals:
    /**
     * @brief 每生成一帧正常数据发出（观察/测试断言用）
     * @param seq   帧序号（0 基递增）
     * @param frame 构建好的正常帧字节（注入前）
     */
    void frameGenerated(int seq, const QByteArray &frame);

    /** @brief 客户端连接数变化 */
    void clientCountChanged(int count);

private slots:
    /** @brief 新客户端接入：收集 + 生命周期管理 */
    void onNewConnection();

    /** @brief 周期定时器：组帧 → 注入 → 广播 */
    void onTick();

private:
    /** @brief 构建一帧正常采集数据（载荷：通道数 + 每通道 [index + float 大端]） */
    QByteArray buildDataFrame();

    /** @brief 收到客户端请求字节：解析请求帧并应答（当前支持通道配置查询） */
    void handleClientRequest(QTcpSocket *client);

    /** @brief 按当前通道数重建默认通道配置（温度/压力/流量/转速，0~100） */
    void rebuildChannelConfig();

    /** @brief 把字节流段列表广播到所有已连接客户端（每段一次 write） */
    void broadcast(const QVector<QByteArray> &chunks);

    QTcpServer *m_server = nullptr;   ///< TCP 服务端
    QTimer     *m_timer  = nullptr;   ///< 数据帧定时器

    int m_frameIntervalMs = 50;       ///< 帧间隔（默认 50ms = 20Hz）
    int m_channelCount = 4;           ///< 通道数（默认 4）

    bool m_faultEnabled = false;      ///< 异常注入开关
    bool m_faultCycling = false;      ///< 轮询注入开关（每帧切换异常类型）
    FaultType m_faultType = FaultType::None;  ///< 当前注入的异常类型

    float m_phase = 0.0f;             ///< 波形相位（随时间递增）
    int   m_frameSeq = 0;             ///< 帧序号（0 基，注入轮询用）

    QVector<QTcpSocket*> m_clients;                           ///< 已连接客户端
    QHash<QTcpSocket*, protocol::FrameParser> m_clientParsers; ///< 每客户端独立解析器（多客户端不串帧）
    QVector<protocol::ChannelConfigInfo> m_channelConfig;      ///< 通道配置（供查询响应）
};

} // namespace simulator
} // namespace dscope
