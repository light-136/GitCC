/**
 * @file mainwindow.h
 * @brief 主窗口（P5：页签布局 + 菜单/工具栏 + QSS 深色主题）
 *
 * P5 设计意图：
 *   - 主窗口升级为工业软件的标准骨架：菜单栏 → 工具栏 → 中央页签区 → 状态栏；
 *   - 中央用 QTabWidget 承载 5 个业务页面（监控/设备/记录/设置/信号槽演示），
 *     对应 WPF 的 TabControl；后续阶段逐个页签填充真实功能；
 *   - 深色工业主题通过 resources/qss/main.qss 全局加载（对应 WPF 全局 Style）。
 *
 * 页面职责划分（重要）：
 *   - 每个页面是独立的 QWidget 组件（src/ui/pages/），由 U2 Agent 并行开发；
 *   - 主窗口只负责"组装"页面与全局框架，不写业务逻辑。
 */

#pragma once

#include <QMainWindow>

class QTabWidget;
class QLabel;
class QTimer;
class QAction;   // 工具栏动作（P12 数据链路控制）
class QCloseEvent;  // 退出确认（closeEvent 重写）

namespace datascope {
namespace ui {
class MonitorPage;   // 监控总览页（P14 填充曲线/仪表）
class DevicePage;    // 设备管理页（V2：真实设备管理）
class RecordPage;    // 数据记录与回放页（P15）
class SettingsPage;  // 设置页（V2：真实配置中心）
} // namespace ui

namespace services {
class DataService;     // 数据总线（P12：连接/采集/数据分发）
class RecordManager;   // 记录/回放管理器（P15）
} // namespace services

} // namespace datascope

/**
 * @class MainWindow
 * @brief DataScope Studio 主窗口
 */
class MainWindow : public QMainWindow
{
    Q_OBJECT

public:
    explicit MainWindow(QWidget *parent = nullptr);
    ~MainWindow() override;

    Q_DISABLE_COPY(MainWindow)

protected:
    /**
     * @brief 关闭事件：确认退出（采集运行中强制退出会中断数据，需用户确认）
     */
    void closeEvent(QCloseEvent *event) override;

private:
    void buildMenuBar();      // 菜单栏：文件 / 视图 / 帮助
    void buildToolBar();      // 工具栏：连接 / 启动 / 停止（占位）
    void buildCentralTabs();  // 中央页签：5 个业务页面
    void buildStatusBar();    // 状态栏：消息区 + 右侧时钟
    void applyStyleSheet();   // 加载 QSS 深色主题

    // ---- P12 数据链路槽 ----
    void connectDevice();                 // 连接设备（默认参数从配置读取）并启动采集
    void disconnectDevice();              // 断开设备连接（V2：明确断开入口）
    void stopAcquisition();               // 停止采集
    void onServiceConnected();            // 连接成功：更新按钮与状态栏
    void onServiceDisconnected();         // 断开：复位按钮与状态栏
    void onServiceError(const QString &message);   // 链路错误：状态栏提示
    void onThemeChanged(const QString &theme);     // 设置页主题切换（深/亮）

    // ---- 中央页签 ----
    QTabWidget *m_tabs = nullptr;
    datascope::ui::MonitorPage   *m_monitorPage = nullptr;
    datascope::ui::DevicePage    *m_devicePage = nullptr;
    datascope::ui::RecordPage    *m_recordPage = nullptr;
    datascope::ui::SettingsPage  *m_settingsPage = nullptr;

    // ---- P12 数据链路（DataService 数据总线）----
    datascope::services::DataService *m_service = nullptr;  // 数据总线
    datascope::services::RecordManager *m_recordManager = nullptr;  // 记录/回放（P15）
    QAction *m_actConnect = nullptr;    // 工具栏：连接设备
    QAction *m_actDisconnect = nullptr; // 工具栏：断开设备（V2 新增明确断开入口）
    QAction *m_actStart   = nullptr;  // 工具栏：启动采集
    QAction *m_actStop    = nullptr;  // 工具栏：停止采集

    // ---- 状态栏 ----
    QLabel *m_connLabel = nullptr;    // 连接状态指示（左）
    QLabel *m_timeLabel = nullptr;    // 右侧时钟标签
    QTimer *m_clock = nullptr;        // 每秒触发一次刷新时间
};
