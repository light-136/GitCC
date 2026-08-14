/**
 * @file devicecontroller.h
 * @brief 设备状态机控制器（P13：状态机与错误处理）
 *
 * 严格对照契约《07-设备状态机契约.md》：
 *   状态机：Disconnected → Connecting → Connected / Error
 *            （Error 带退避自动重连，见第三节接口契约、第四节错误处理策略）。
 *
 * 职责：
 *   - 持有 DataService（P12 数据总线）并把它"无状态"的信号翻译成
 *     "有状态"的设备状态机，供 UI 展示与用户控制；
 *   - 对外只暴露业务动作（connect/disconnect/start/stop）与状态信号
 *     （statusChanged / errorOccurred / dataUpdated），UI 不感知 DataService 细节；
 *   - 意外断线/连接失败自动重连（退避：1s→2s→4s…封顶 30s），用户主动断开则退出状态机。
 *
 * 教学点（对照 WPF）：
 *   - 状态机 ≈ MVVM 里 ViewModel 的连接状态属性 + ICommand 状态守卫；
 *   - statusChanged 信号 ≈ INotifyPropertyChanged 驱动 UI 状态样式；
 *   - 退避重连 ≈ 工业上位机"设备离线自动恢复"的标准做法（封顶避免风暴）。
 */

#pragma once

#include <QObject>
#include <QString>
#include <QVector>

#include "domain/models.h"   // DeviceStatus / DataPoint

class QTimer;

namespace datascope {
namespace services {

class DataService;

/**
 * @class DeviceController
 * @brief 设备连接状态机控制器（管理连接生命周期与自动重连）
 */
class DeviceController : public QObject
{
    Q_OBJECT

public:
    /**
     * @brief 构造：内部创建 DataService 数据总线 + 重连定时器
     * @param parent 父对象
     */
    explicit DeviceController(QObject *parent = nullptr);

    /**
     * @brief 析构：DataService 随对象树销毁（其内部线程会安全退出）
     */
    ~DeviceController() override;

    /**
     * @brief 查询当前设备状态
     * @return DeviceStatus 枚举（Disconnected/Connecting/Connected/Error）
     */
    datascope::domain::DeviceStatus status() const;

    /**
     * @brief 发起连接（异步）：Disconnected/Error → Connecting → Connected
     * @param host 设备主机（如 127.0.0.1）
     * @param port 设备端口（模拟设备默认 40001）
     * @note 已处于 Connected 时忽略（防重复连接）
     */
    void connectDevice(const QString &host, quint16 port);

    /**
     * @brief 主动断开连接：任何状态 → Disconnected（并取消自动重连）
     */
    void disconnectDevice();

    /**
     * @brief 启动采集（数据流经 dataUpdated 信号转发）
     */
    void startAcquisition();

    /**
     * @brief 停止采集
     */
    void stopAcquisition();

signals:
    /** @brief 状态转移通知（UI 据此更新 LED/按钮/状态栏） */
    void statusChanged(datascope::domain::DeviceStatus status);

    /** @brief 错误/降级通知（连接失败、断线、重连提示等） */
    void errorOccurred(const QString &message);

    /** @brief 实时数据转发（主线程，MonitorPage::onDataUpdated 消费） */
    void dataUpdated(const QVector<datascope::domain::DataPoint> &points);

private slots:
    /** @brief DataService 连接成功 → Connected（重置退避） */
    void onServiceConnected();
    /** @brief DataService 断开 → 区分"主动断开"与"意外断线" */
    void onServiceDisconnected();
    /** @brief DataService 报错 → Error + 触发自动重连 */
    void onServiceError(const QString &message);
    /** @brief DataService 数据转发 */
    void onDataUpdated(const QVector<datascope::domain::DataPoint> &points);
    /** @brief 重连定时器超时 → 重新发起连接 */
    void onReconnectTimeout();

private:
    /** @brief 状态转移（状态不变时不重复发信号） */
    void setStatus(datascope::domain::DeviceStatus s);
    /** @brief 启动退避重连定时器（仅 Error 态且非用户主动断开时生效） */
    void startReconnectTimer();
    /** @brief 停止重连定时器 */
    void stopReconnectTimer();

    DataService *m_service = nullptr;                 // 数据总线（实际连接/采集动作）
    QTimer     *m_reconnectTimer = nullptr;           // 重连定时器（单次触发）
    datascope::domain::DeviceStatus m_status =
        datascope::domain::DeviceStatus::Disconnected; // 当前状态（初始：未连接）
    bool  m_userDisconnected = false;                 // 用户主动断开标志（区分意外断线）
    int   m_retryAttempts = 0;                        // 重连尝试计数（驱动退避间隔）
    QString m_host;                                   // 重连目标主机（保存以便重试）
    quint16 m_port = 0;                               // 重连目标端口
};

} // namespace services
} // namespace datascope
