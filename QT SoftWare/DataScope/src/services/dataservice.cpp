/**
 * @file dataservice.cpp
 * @brief 数据总线实现（P12）
 *
 * 线程生命周期管理（核心）：
 *   - DataService 构造函数：创建采集线程 + Worker，moveToThread；
 *   - connectTo / startAcquisition：通过信号槽把请求投递到采集线程；
 *   - 析构：先 stop（Worker::stop 队列投递）→ thread.quit() → wait()，
 *     保证"先停 Worker 再退线程"，杜绝线程在清理中途销毁。
 *
 * 元类型注册（跨线程信号参数）：
 *   - QVector<datascope::domain::DataPoint> 作为 QueuedConnection 参数，
 *     必须先在 main() 里 qRegisterMetaType（见 src/app/main.cpp），
 *     否则运行时警告"Don't know how to handle... "且参数丢失。
 */

#include "services/dataservice.h"

#include "services/acquisitionworker.h"
#include "domain/models.h"

#include <QMutex>
#include <QMutexLocker>
#include <QThread>
#include <QDebug>

// ---- 元类型声明：QVector<DataPoint> 作为跨线程（QueuedConnection）信号参数 ----
// Q_DECLARE_METATYPE 让类型进入 Qt 元类型系统（可被 QVariant 包裹、可跨线程拷贝）。
// 注意：DataPoint 是命名空间内的 struct，必须在完整定义之后声明（上面已 include）。
Q_DECLARE_METATYPE(datascope::domain::DataPoint)
Q_DECLARE_METATYPE(QVector<datascope::domain::DataPoint>)

namespace datascope {
namespace services {

DataService::DataService(QObject *parent)
    : QObject(parent)
    , m_mutex(new QMutex)   // 快照锁：UI 读 / Worker 写 串行化
{
    // 运行时注册：QueuedConnection 拷贝 QVector<DataPoint> 参数时需要元类型信息。
    // 用"裸名"注册（QVector<datascope::domain::DataPoint>），保证 moc 生成的
    // 规范名与注册名一致，否则运行时警告"Don't know how to handle... "。
    qRegisterMetaType<QVector<datascope::domain::DataPoint>>(
        "QVector<datascope::domain::DataPoint>");
    // ---- 创建采集线程与 Worker，并把 Worker 迁到采集线程 ----
    // 教学点：moveToThread 后，Worker 的槽由采集线程事件循环调度；
    // 跨线程调用 connectTo/start 时，参数会被"拷贝"进队列投递。
    m_thread = new QThread(this);
    m_thread->setObjectName(QStringLiteral("AcquisitionThread"));

    m_worker = new AcquisitionWorker;            // parent=nullptr：由线程托管
    m_worker->moveToThread(m_thread);

    // ---- 连接 Worker 信号 → 本类（自动 QueuedConnection，因跨线程）----
    connect(m_worker, &AcquisitionWorker::pointsReady,
            this,     &DataService::onPointsReady);
    connect(m_worker, &AcquisitionWorker::connectedChanged,
            this,     &DataService::onConnectedChanged);
    connect(m_worker, &AcquisitionWorker::errorOccurred,
            this,     &DataService::connectionError);

    m_thread->start();   // 启动采集线程事件循环（Worker 就绪待命）
}

DataService::~DataService()
{
    // ---- 安全关停：先停采集，再退线程 ----
    stopAcquisition();       // 队列投递 Worker::stop（若线程活着会执行）
    m_thread->quit();        // 请求线程退出事件循环
    m_thread->wait(3000);    // 等线程结束（最多 3s，防止死锁卡死 UI）

    delete m_mutex;          // 释放快照锁
    m_mutex = nullptr;
}

void DataService::connectTo(const QString &host, quint16 port)
{
    // ---- 请求连接：队列投递到采集线程（Worker::start 里 new TcpClient）----
    // 教学点：跨线程调用用 QMetaObject::invokeMethod + Qt::QueuedConnection，
    // 等价于"发一条消息给另一个线程的事件循环"。
    if (!m_thread || !m_worker) {
        emit connectionError(QStringLiteral("数据总线未初始化"));
        return;
    }
    if (m_connected) {
        emit connectionError(QStringLiteral("已在连接中"));
        return;
    }
    QMetaObject::invokeMethod(m_worker, "start", Qt::QueuedConnection,
                              Q_ARG(QString, host), Q_ARG(quint16, port));
}

void DataService::disconnectFromDevice()
{
    // 简化：采集与连接共用一个 TcpClient，断开即停止采集
    stopAcquisition();
}

bool DataService::isConnected() const
{
    QMutexLocker locker(m_mutex);
    return m_connected;
}

void DataService::startAcquisition()
{
    if (!m_thread || !m_worker) {
        return;
    }
    if (m_acquiring) {
        return; // 防重复启动
    }
    {
        QMutexLocker locker(m_mutex);
        m_acquiring = true;
    }
    // 采集启动后 Worker 才开始驱动数据；连接动作在 connectTo 时已发起。
    // 这里仅记录状态并通知 UI。真正的"每帧产出"由 Worker 收到帧驱动。
    emit acquisitionStarted();
}

void DataService::stopAcquisition()
{
    if (!m_thread || !m_worker) {
        return;
    }
    {
        QMutexLocker locker(m_mutex);
        m_acquiring = false;
        m_connected = false;   // 停止采集视为断开（连接由 Worker 清理）
    }
    QMetaObject::invokeMethod(m_worker, "stop", Qt::QueuedConnection);
    emit acquisitionStopped();
    emit disconnected();
}

bool DataService::isAcquiring() const
{
    QMutexLocker locker(m_mutex);
    return m_acquiring;
}

QVector<domain::DataPoint> DataService::lastData() const
{
    QMutexLocker locker(m_mutex);
    return m_lastData;   // 返回拷贝（快照），锁内读取，安全
}

void DataService::onPointsReady(const QVector<domain::DataPoint> &points)
{
    // ---- 主线程收到采集数据：更新快照 + 转发 dataUpdated ----
    {
        QMutexLocker locker(m_mutex);
        m_lastData = points;   // 覆盖式快照（最新一批，回放/CSV 用）
    }
    emit dataUpdated(points);  // UI 订阅（MonitorPage::onDataUpdated）
}

void DataService::onConnectedChanged(bool nowConnected)
{
    // 参数命名注意：不能叫 connected——那会遮蔽同名信号 connected()，
    // 导致 emit connected() 被解析成"把参数当函数调用"而编译失败。
    {
        QMutexLocker locker(m_mutex);
        m_connected = nowConnected;
    }
    if (nowConnected) {
        emit connected();
    } else {
        emit disconnected();
    }
}

} // namespace services
} // namespace datascope
