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

namespace datascope {
namespace ui {
class MonitorPage;   // 监控总览页（P14 填充曲线/仪表）
class DevicePage;    // 设备管理页（P8/P9 填充设备列表）
class RecordPage;    // 数据记录与回放页（P15）
class SettingsPage;  // 设置页（P7 配置 UI）
class DemoPage;      // 信号槽演示页（P4 成果 UI 化）
} // namespace ui
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

private:
    void buildMenuBar();      // 菜单栏：文件 / 视图 / 帮助
    void buildToolBar();      // 工具栏：连接 / 启动 / 停止（占位）
    void buildCentralTabs();  // 中央页签：5 个业务页面
    void buildStatusBar();    // 状态栏：消息区 + 右侧时钟
    void applyStyleSheet();   // 加载 QSS 深色主题

    // ---- 中央页签 ----
    QTabWidget *m_tabs = nullptr;
    datascope::ui::MonitorPage   *m_monitorPage = nullptr;
    datascope::ui::DevicePage    *m_devicePage = nullptr;
    datascope::ui::RecordPage    *m_recordPage = nullptr;
    datascope::ui::SettingsPage  *m_settingsPage = nullptr;
    datascope::ui::DemoPage      *m_demoPage = nullptr;

    // ---- 状态栏时钟 ----
    QLabel *m_timeLabel = nullptr;  // 右侧时钟标签
    QTimer *m_clock = nullptr;      // 每秒触发一次刷新时间
};
