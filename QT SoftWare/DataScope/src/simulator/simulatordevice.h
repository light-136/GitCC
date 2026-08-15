/**
 * @file simulatordevice.h
 * @brief 可复用模拟采集设备（V2：支持运行模式 + 异常注入）
 *
 * ────────────────────────────────────────────────────────────
 * 为什么把模拟设备做成类（V2-执行③ 核心）
 * ────────────────────────────────────────────────────────────
 * V1 的 Simulator 是 main.cpp 里的一段过程式代码：
 *   - 只能发正常正弦波，无法注入任何异常；
 *   - 无法被测试复用（测试想验证主机健壮性，没有"会出错的设备"可用）。
 *
 * V2 把模拟设备抽成 SimulatorDevice 类（QObject + QTcpServer）：
 *   - 独立程序（src/simulator/main.cpp）用它：命令行启动，供主机联调；
 *   - 集成测试（tests/integration/）也用它：随机端口内嵌启动，直接验证
 *     "异常设备 → 客户端行为"的真实网络链路；
 *   - 支持运行模式（正常/报警/压力/错误）与细粒度异常注入（FaultInjector）。
 *
 * ── 运行模式（SimulatorMode）──
 *   Normal  正常：4 通道正弦波，帧间隔默认 50ms
 *   Alarm   报警：周期性把某通道推到超量程，交替"报警/恢复"（测主机报警链路）
 *   Stress  压力：高频（10ms）+ 每帧粘包（两帧合并），测主机吞吐与重组
 *   Error   错误：按 FaultConfig 注入指定异常（CRC/非法帧/分包/延迟/突变）
 *
 * ── 生命周期 ──
 *   start() 监听指定端口（0 = 系统分配随机端口）→ onTick() 周期组帧发送
 *   → 客户端连接/断开由 QTcpServer 信号管理。
 */

#pragma once

#include <QHash>
#include <QObject>
#include <QString>
#include <QVector>

#include "simulator/faultinjector.h"
#include "protocol/channelconfigcodec.h"
#include "protocol/frameparser.h"

class QTcpServer;
class QTcpSocket;
class QTimer;

namespace datascope {
namespace simulator {

/**
 * @brief 模拟设备运行模式
 */
enum class SimulatorMode {
    Normal,   ///< 正常正弦波
    Alarm,    ///< 周期报警/恢复（通道值跳超量程）
    Stress,   ///< 高频 + 粘包（压力测试）
    Busy,     ///< 设备忙：周期内连续静默（不输出数据帧）
    Error     ///< 注入配置的异常（FaultConfig）
};

/**
 * @class SimulatorDevice
 * @brief 模拟采集设备：TCP 服务端 + 周期数据帧 + 异常注入
 *
 * 用法（独立程序）：
 *   SimulatorDevice dev; dev.start(40001); return app.exec();
 * 用法（测试）：
 *   SimulatorDevice dev; QVERIFY(dev.start(0));  // 随机端口
 *   TcpClient client; client.connectTo("127.0.0.1", dev.port());
 */
class SimulatorDevice : public QObject
{
    Q_OBJECT

public:
    explicit SimulatorDevice(QObject *parent = nullptr);
    ~SimulatorDevice() override;

    /**
     * @brief 启动监听
     * @param port 监听端口；0 = 系统分配随机可用端口（测试用）
     * @return 是否启动成功
     */
    bool start(quint16 port = 0);

    /** @brief 实际监听端口（start 后有效；port=0 时为系统分配值） */
    quint16 port() const;

    /** @brief 当前已连接客户端数量 */
    int clientCount() const;

    // ---- 运行配置 ----

    /** @brief 设置运行模式（Normal/Alarm/Stress/Error） */
    void setMode(SimulatorMode mode);

    /** @brief 当前模式 */
    SimulatorMode mode() const { return m_mode; }

    /** @brief 注入异常配置（Error 模式生效） */
    void setFaultConfig(const FaultConfig &cfg) { m_injector.setConfig(cfg); }

    /** @brief 当前异常配置 */
    FaultConfig faultConfig() const { return m_injector.config(); }

    /** @brief 数据帧发送间隔（毫秒）；Stress 模式建议调小（如 10） */
    void setFrameInterval(int ms);

    /** @brief 通道数（默认 4，与主机监控页一致） */
    void setChannelCount(int count);

    /**
     * @brief 当前通道配置（名称/单位/量程，供查询响应与联调观察）
     * @return 协议层的通道配置列表（主机查询时以此为响应数据源）
     */
    QVector<protocol::ChannelConfigInfo> channelConfig() const { return m_channelConfig; }

signals:
    /**
     * @brief 每生成一帧数据发出（观察/测试断言用）
     * @param seq   帧序号（0 基递增）
     * @param frame 构建好的正常帧字节（注入前）
     */
    void frameGenerated(int seq, const QByteArray &frame);

    /** @brief 客户端连接数变化 */
    void clientCountChanged(int count);

private slots:
    void onNewConnection();  // 新客户端接入（收集 + 生命周期管理）
    void onTick();           // 周期定时器：组帧 → 注入 → 广播

private:
    /** @brief 按当前模式构建一帧正常采集数据（4 通道正弦/报警/突变） */
    QByteArray buildDataFrame();

    /** @brief 把注入器输出的段列表写到指定客户端（每段一次 write） */
    void writeChunks(QTcpSocket *client, const QVector<QByteArray> &chunks);

    /** @brief 断线注入是否触发（帧序号按 FaultConfig.period 取模） */
    bool disconnectTriggered() const;

    /** @brief Busy 模式：当前帧序号是否处于"忙窗口"（该窗口内不输出数据） */
    bool inBusyWindow() const;

    /** @brief 收到客户端请求字节：解析请求帧并应答（当前支持通道配置查询） */
    void handleClientRequest(QTcpSocket *client);

    /** @brief 按当前通道数重建默认通道配置（名称/单位/量程） */
    void rebuildChannelConfig();

    QTcpServer *m_server = nullptr;          ///< TCP 服务端
    QTimer     *m_timer  = nullptr;          ///< 数据帧定时器
    FaultInjector m_injector;                ///< 异常注入器

    SimulatorMode m_mode = SimulatorMode::Normal; ///< 运行模式
    int m_frameIntervalMs = 50;              ///< 帧间隔（默认 50ms = 20Hz）
    int m_channelCount = 4;                  ///< 通道数

    double m_phase = 0.0;                    ///< 波形相位（随时间递增）
    int    m_frameSeq = 0;                   ///< 帧序号（注入器触发周期用）

    QVector<QTcpSocket*> m_clients;          ///< 已连接客户端
    QHash<QTcpSocket*, protocol::FrameParser> m_clientParsers; ///< 每客户端请求解析器（多客户端不串帧）

    QVector<protocol::ChannelConfigInfo> m_channelConfig; ///< 通道配置（名称/单位/量程，供查询响应）

    // ---- Alarm 模式状态 ----
    bool m_alarmActive = false;              ///< 当前是否处于"报警"相位
    int  m_alarmCounter = 0;                 ///< 相位计数器（每 20 帧翻转）
};

} // namespace simulator
} // namespace datascope
