/**
 * @file acquisitionworker.h
 * @brief 采集线程 Worker（P12：多线程与异步采集）
 *
 * 线程模型（核心教学点，对应 WPF 的后台线程/BackgroundWorker）：
 *   - 本对象被 moveToThread() 到采集线程（QThread）运行；
 *   - TcpClient 由本 Worker 在 start() 槽内创建（parent=this），
 *     因此 QTcpSocket 与其事件循环都在采集线程内——这是 Qt 线程安全铁律：
 *     **socket 必须在它所属线程内创建**，不能在主线程 new 再 moveToThread；
 *   - 采集线程只做"收发数据 + 组帧解析"，绝不触碰 UI；
 *   - 产出的数据通过 signals: pointsReady(QVector<DataPoint>)
 *     队列投递回主线程（QueuedConnection），UI 侧（DataService）消费。
 *
 * 对应 WPF：
 *   - QThread + moveToThread ≈ BackgroundWorker.RunWorkerAsync；
 *   - Worker 槽（start/stop）≈ DoWork 里的后台逻辑；
 *   - pointsReady 信号 ≈ ReportProgress/RunWorkerCompleted 携带结果。
 */

#pragma once

#include <QObject>
#include <QString>
#include <QVector>

#include "domain/models.h"   // DataPoint

namespace datascope {
namespace protocol {
struct ProtocolFrame;   // 前向声明：信号参数 const 引用，无需完整定义
}
namespace services {

class TcpClient;

/**
 * @class AcquisitionWorker
 * @brief 采集线程的工作对象：驱动 TcpClient 收发并产出 DataPoint
 *
 * 生命周期：
 *   - 由 DataService 创建 → moveToThread(采集线程) → start() 槽内 new TcpClient
 *     → 采集数据 → stop() 槽内 delete TcpClient → Worker::deleteLater()。
 */
class AcquisitionWorker : public QObject
{
    Q_OBJECT

public:
    /**
     * @brief 构造 Worker（仅做成员初始化，不碰网络）
     * @param parent 父对象（通常为 nullptr，由线程托管）
     */
    explicit AcquisitionWorker(QObject *parent = nullptr);

    /**
     * @brief 析构：确保 m_client 已被清理
     */
    ~AcquisitionWorker() override;

public slots:
    /**
     * @brief 在采集线程内启动采集：创建 TcpClient 并连接采集设备
     * @param host 设备主机
     * @param port 设备端口
     * @note 由 DataService 通过信号槽（队列）投递到本线程执行
     */
    void start(const QString &host, quint16 port);

    /**
     * @brief 在采集线程内停止采集：断开并销毁 TcpClient
     */
    void stop();

signals:
    /**
     * @brief 解析出的一批采样点（跨线程投递：帧→DataPoint 转换结果）
     * @note 参数用全限定名 datascope::domain::DataPoint（理由见 dataservice.h
     *       dataUpdated 注释）：保证 moc 规范化名与 qRegisterMetaType 注册名一致。
     */
    void pointsReady(const QVector<datascope::domain::DataPoint> &points);

    /**
     * @brief 连接状态变化（主线程据此更新 UI 状态）
     */
    void connectedChanged(bool connected);

    /**
     * @brief 采集链路错误（统一字符串，含 socket 错误与解析异常）
     */
    void errorOccurred(const QString &message);

private:
    /**
     * @brief 连接 socket 的信号：把收到的完整帧转换为 DataPoint 列表
     * @param frame 校验通过的协议帧（FUNC/CMD 已解析）
     */
    void handleFrame(const protocol::ProtocolFrame &frame);

    TcpClient *m_client = nullptr;   // 采集线程内的 TCP 客户端（start 时创建）
};

} // namespace services
} // namespace datascope
