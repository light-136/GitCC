/**
 * @file devicecontroller.cpp
 * @brief 设备状态机控制器实现（P13）
 *
 * 状态机驱动逻辑（对照契约 07 第二节转移表）：
 *   - connectDevice()          → Connecting → (DataService::connected) → Connected
 *   - connectDevice() 失败      → Error → 自动重连（退避）→ Connecting → Connected
 *   - 意外断线(disconnected)    → Error → 自动重连
 *   - disconnectDevice() 主动   → Disconnected（不重连）
 *
 * 关键设计：
 *   - 用 m_userDisconnected 区分"用户主动断开"与"意外断线"——DataService
 *     的 disconnected 信号在这两种情况下都会触发，必须由控制器判别语义；
 *   - 自动重连依赖 worker 在出错时销毁 m_client（P13 修复，见 acquisitionworker.cpp），
 *     否则 start() 会因残留客户端而吞掉重连请求；
 *   - 退避间隔用 m_retryAttempts 计数：1s → 2s → 4s…封顶 30s，防止风暴。
 */

#include "services/devicecontroller.h"

#include "services/dataservice.h"

#include <QTimer>
#include <QDebug>

namespace datascope {
namespace services {

namespace {
// 重连退避参数（契约 07 第四节）：初始 1 秒，每次失败翻倍，封顶 30 秒
const int kInitialRetryMs = 1000;
const int kMaxRetryMs     = 30000;
} // namespace

DeviceController::DeviceController(QObject *parent)
    : QObject(parent)
{
    // ---- 创建数据总线：DeviceController 是它的门面/外观 ----
    m_service = new DataService(this);

    // 重连定时器：单次触发，每次超时由 onReconnectTimeout 决定是否再起
    m_reconnectTimer = new QTimer(this);
    m_reconnectTimer->setSingleShot(true);

    // ---- 把 DataService 的无状态信号接入状态机 ----
    connect(m_service, &DataService::connected,        this, &DeviceController::onServiceConnected);
    connect(m_service, &DataService::disconnected,     this, &DeviceController::onServiceDisconnected);
    connect(m_service, &DataService::connectionError,  this, &DeviceController::onServiceError);
    connect(m_service, &DataService::dataUpdated,      this, &DeviceController::onDataUpdated);
    connect(m_reconnectTimer, &QTimer::timeout,        this, &DeviceController::onReconnectTimeout);
}

DeviceController::~DeviceController() = default;   // DataService / QTimer 随对象树回收

datascope::domain::DeviceStatus DeviceController::status() const
{
    return m_status;
}

// ---------------------------------------------------------------------------
// 状态驱动操作
// ---------------------------------------------------------------------------
void DeviceController::connectDevice(const QString &host, quint16 port)
{
    if (m_status == datascope::domain::DeviceStatus::Connected) {
        return;   // 已连接：忽略重复请求（UI 上"连接"按钮已禁用，这里是双保险）
    }
    m_host = host;               // 保存目标：失败后自动重连用
    m_port = port;
    m_userDisconnected = false;  // 本次是主动发起
    m_retryAttempts = 0;         // 重置退避计数
    stopReconnectTimer();        // 清掉可能遗留的重连定时器

    setStatus(datascope::domain::DeviceStatus::Connecting);
    m_service->connectTo(host, port);   // 异步发起，结果由信号通知
}

void DeviceController::disconnectDevice()
{
    m_userDisconnected = true;    // 标记主动断开：后续 disconnected 不会触发重连
    stopReconnectTimer();         // 取消未完成的重连倒计时
    m_service->disconnectFromDevice();
    setStatus(datascope::domain::DeviceStatus::Disconnected);
}

void DeviceController::startAcquisition()
{
    m_service->startAcquisition();
}

void DeviceController::stopAcquisition()
{
    m_service->stopAcquisition();
}

// ---------------------------------------------------------------------------
// DataService 信号 → 状态机转移
// ---------------------------------------------------------------------------
void DeviceController::onServiceConnected()
{
    m_userDisconnected = false;
    m_retryAttempts = 0;          // 连接成功：重置退避，下次断线从 1s 重新计
    stopReconnectTimer();
    setStatus(datascope::domain::DeviceStatus::Connected);
}

void DeviceController::onServiceDisconnected()
{
    if (m_userDisconnected) {
        // 用户主动断开：安静回到 Disconnected，不做任何重连
        m_userDisconnected = false;
        setStatus(datascope::domain::DeviceStatus::Disconnected);
    } else {
        // 意外断线（设备掉电/网线断开/对端关闭）→ Error + 自动重连
        setStatus(datascope::domain::DeviceStatus::Error);
        startReconnectTimer();
    }
}

void DeviceController::onServiceError(const QString &message)
{
    emit errorOccurred(message);  // 错误信息透传（UI 状态栏/日志）
    setStatus(datascope::domain::DeviceStatus::Error);
    if (!m_userDisconnected) {
        startReconnectTimer();    // 连接失败/通信异常 → 退避重连
    }
}

void DeviceController::onDataUpdated(const QVector<datascope::domain::DataPoint> &points)
{
    emit dataUpdated(points);     // 数据总线 → 控制器 → UI，逐级转发
}

void DeviceController::onReconnectTimeout()
{
    // 定时器超时：重连一次。连接动作是异步的，结果继续由信号驱动。
    setStatus(datascope::domain::DeviceStatus::Connecting);
    m_service->connectTo(m_host, m_port);
}

// ---------------------------------------------------------------------------
// 状态机内部工具
// ---------------------------------------------------------------------------
void DeviceController::setStatus(datascope::domain::DeviceStatus s)
{
    if (m_status == s) {
        return;   // 状态未变化：不重复广播，避免 UI 无意义刷新
    }
    m_status = s;
    emit statusChanged(s);
}

void DeviceController::startReconnectTimer()
{
    if (m_userDisconnected || m_status != datascope::domain::DeviceStatus::Error) {
        return;   // 仅 Error 态、且非用户主动断开时才自动重连
    }
    if (m_reconnectTimer->isActive()) {
        return;   // 已在倒计时：保持当前间隔，防止"断线+错误"双重触发让退避跳档
    }
    // 退避计算：第 n 次重试间隔 = min(1s << n, 30s)
    const int intervalMs = qMin(kInitialRetryMs * (1 << m_retryAttempts), kMaxRetryMs);
    ++m_retryAttempts;

    emit errorOccurred(
        QStringLiteral("设备连接失败，%1 秒后自动重连...").arg(intervalMs / 1000));
    m_reconnectTimer->start(intervalMs);
}

void DeviceController::stopReconnectTimer()
{
    m_reconnectTimer->stop();
}

} // namespace services
} // namespace datascope
