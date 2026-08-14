/**
 * @file dataservice.h
 * @brief 数据总线（P12：DataService）
 *
 * 定位：主线程与采集线程之间的"唯一数据通道"（对应 WPF 里
 * ViewModel + 后台线程数据总线）。UI 只认 DataService：
 *   - 发起连接/采集（connectTo/startAcquisition）；
 *   - 订阅 dataUpdated(QVector<DataPoint>) 拿实时数据；
 *   - 查询 lastData() 拿最新快照（回放/CSV 导出用）。
 *
 * 线程模型：
 *   - DataService 本身运行在主线程（UI 线程）；
 *   - 内部持有 QThread（采集线程）+ AcquisitionWorker（moveToThread）；
 *   - 跨线程通信一律信号槽（QueuedConnection），共享数据用 QMutex 保护。
 *
 * 教学点（对照 WPF）：
 *   - QThread + Worker ≈ BackgroundWorker；DataService ≈ 业务层门面；
 *   - dataUpdated 信号 ≈ 事件聚合（类似 Prism EventAggregator）；
 *   - lastData() 快照 ≈ 缓存最新 ViewModel 数据供其它视图读取。
 */

#pragma once

#include <QObject>
#include <QString>
#include <QVector>

#include "domain/models.h"   // DataPoint / Channel

class QThread;
class QMutex;

namespace datascope {
namespace services {

class AcquisitionWorker;

/**
 * @class DataService
 * @brief 数据总线：连接控制 + 采集控制 + 数据分发 + 最新快照
 */
class DataService : public QObject
{
    Q_OBJECT

public:
    /**
     * @brief 构造数据总线
     * @param parent 父对象
     */
    explicit DataService(QObject *parent = nullptr);

    /**
     * @brief 析构：确保采集线程安全退出（quit + wait）
     */
    ~DataService() override;

    /**
     * @brief 连接采集设备（异步，结果由 connected / connectionError 通知）
     * @param host 设备主机（如 127.0.0.1）
     * @param port 设备端口（模拟设备默认 40001）
     */
    void connectTo(const QString &host, quint16 port);

    /**
     * @brief 断开设备连接
     */
    void disconnectFromDevice();

    /**
     * @brief 当前是否已连接
     */
    bool isConnected() const;

    /**
     * @brief 启动采集（Worker 在采集线程创建 TcpClient 并发起连接）
     */
    void startAcquisition();

    /**
     * @brief 停止采集（Worker 在采集线程断开并清理）
     */
    void stopAcquisition();

    /**
     * @brief 是否正在采集
     */
    bool isAcquiring() const;

    /**
     * @brief 最新数据快照（回放/CSV 导出读取；线程安全）
     * @return 最近一次 pointsReady 的拷贝
     */
    QVector<domain::DataPoint> lastData() const;

signals:
    /** @brief 连接成功（主线程收到） */
    void connected();

    /** @brief 断开（主动或异常） */
    void disconnected();

    /** @brief 连接/采集错误（统一字符串） */
    void connectionError(const QString &message);

    /**
     * @brief 实时数据分发（主线程：MonitorPage::onDataUpdated 消费）
     * @note 参数类型用"全限定名 datascope::domain::DataPoint"——
     *       moc 对模板参数里的相对名（domain::DataPoint）会规范化为不带
     *       命名空间前缀的 "QVector<domain::DataPoint>"，与 qRegisterMetaType
     *       注册的全限定名不一致，导致跨线程队列参数丢失（运行时警告）。
     *       全限定名保证 moc 规范化名 == 注册名 == Q_DECLARE_METATYPE 名。
     */
    void dataUpdated(const QVector<datascope::domain::DataPoint> &points);

    /** @brief 采集开始 */
    void acquisitionStarted();

    /** @brief 采集停止 */
    void acquisitionStopped();

private slots:
    /**
     * @brief 收到 Worker 投递的采样点：更新快照 + 转发 dataUpdated
     * @param points 一批采样点
     * @note 本槽运行在主线程（Worker 跨线程队列投递）
     */
    void onPointsReady(const QVector<domain::DataPoint> &points);

    /**
     * @brief Worker 报告连接状态变化
     * @param connected 是否已连接
     */
    void onConnectedChanged(bool connected);

private:
    QThread              *m_thread   = nullptr;   // 采集线程（生命周期托管）
    AcquisitionWorker    *m_worker   = nullptr;   // 采集线程工作对象
    QMutex               *m_mutex    = nullptr;   // 保护 m_lastData / 状态标志
    QVector<domain::DataPoint> m_lastData;        // 最新数据快照（锁保护）
    bool                  m_connected = false;    // 连接状态（锁保护）
    bool                  m_acquiring = false;    // 采集状态（锁保护）
};

} // namespace services
} // namespace datascope
