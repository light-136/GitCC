/**
 * @file mainwindow.h
 * @brief V3 UI 层 —— 主窗口（连接工具栏 + 监控页 + 状态栏）
 *
 * ── 开发思路 ──
 * 主窗口是"收集输入 + 交互编排"的入口：工具栏收集 host/port 与连接/断开动作，把动作
 * 转成对 AcquisitionWorker 的 start/stop 调用；把 worker 的 6 个信号转发到 MonitorPage
 * 与状态栏。连接入口放工具栏（权威规格：第一阶段砍 DevicePage，不建独立设备页）。
 *
 * AcquisitionWorker 采用"构造注入、不 new"：由 Composition Root（main.cpp）创建并管理
 * 生命周期（含所属线程），MainWindow 只持有不透明指针。因为任务约束"不得 include
 * acquisition 库头文件"，worker 在本文件内是前向声明的不透明类型：
 *   - 驱动它（start/stop）走 QMetaObject::invokeMethod（字符串方法名 + 队列调用，落到
 *      worker 所在线程执行——这正是跨线程调用槽的正确姿势，也规避了对完整类型的依赖）；
 *   - 连信号走字符串式 SIGNAL/SLOT（只需 QObject*，运行时按 QMetaObject 解析）。
 *
 * ── WPF 对照 ──
 *   MainWindow  ↔  WPF 的 Shell/主窗口 + 工具栏；AcquisitionWorker 相当于注入的
 *                  连接服务，主窗口只调用其方法并订阅其事件。
 */

#pragma once

#include <QMainWindow>

#include "domain/connectionstate.h"   // dscope::domain::ConnectionState（私有方法参数用）

class QLineEdit;
class QSpinBox;
class QPushButton;
class QLabel;
class QCloseEvent;

namespace dscope {
namespace acquisition { class AcquisitionWorker; }   // 前向声明（不 include 采集层头文件）

namespace ui {

class MonitorPage;   // 前向声明（完整定义在 ui/monitorpage.h，.cpp 里 include）

/**
 * @class MainWindow
 * @brief 主窗口：工具栏（host/port + 连接/断开 + 状态标签）+ 监控页 + 状态栏
 */
class MainWindow : public QMainWindow
{
    Q_OBJECT

public:
    /**
     * @brief 构造注入采集 worker（Composition Root 创建并管理生命周期）
     * @param worker AcquisitionWorker 指针（不透明类型，仅用于驱动/连信号，不 new 不 delete）
     */
    explicit MainWindow(dscope::acquisition::AcquisitionWorker *worker, QWidget *parent = nullptr);

protected:
    void closeEvent(QCloseEvent *event) override;

private slots:
    void onConnectClicked();              ///< 连接按钮 → worker.start(host, port)
    void onDisconnectClicked();           ///< 断开按钮 → worker.stop()
    void onConnected();                   ///< worker.connected → 更新状态/按钮
    void onDisconnected();                ///< worker.disconnected → 更新状态/按钮
    void onConnectionStateChanged(int state);   ///< worker.connectionStateChanged → 状态标签
    void onConnectionError(int code);     ///< worker.connectionError → 状态栏提示

private:
    /** @brief 组装工具栏控件 */
    void buildToolbar();

    /** @brief 把不透明 worker 指针转成 QObject*（仅用于 connect / invokeMethod） */
    QObject *workerObject() const;

    /** @brief 依据连接状态刷新按钮可用性与状态标签 */
    void updateConnectionUi(dscope::domain::ConnectionState state);

    dscope::acquisition::AcquisitionWorker *m_worker = nullptr;   ///< 注入的采集 worker（不透明）
    dscope::ui::MonitorPage *m_monitorPage = nullptr;             ///< 监控页（中央部件）

    // 工具栏控件
    QLineEdit    *m_hostEdit      = nullptr;   ///< 主机地址输入
    QSpinBox     *m_portSpin      = nullptr;   ///< 端口输入
    QPushButton  *m_connectBtn    = nullptr;   ///< 连接按钮
    QPushButton  *m_disconnectBtn = nullptr;   ///< 断开按钮
    QLabel       *m_statusLabel   = nullptr;   ///< 工具栏状态标签
};

} // namespace ui
} // namespace dscope
