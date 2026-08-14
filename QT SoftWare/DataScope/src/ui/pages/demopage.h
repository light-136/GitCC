/**
 * @file demopage.h
 * @brief 信号槽演示页（把 P4 的 SignalSlotDemo 成果完整搬入 UI）
 *
 * P5 设计意图：
 *   - P4 的 SignalSlotDemo 是"纯 QObject 的教学对象"，演示信号槽五种连接写法；
 *   - 本页是它的 UI 化收尾：把"按钮 + 日志区"从 P4 的临时主窗口中央面板
 *     迁移到独立页签，让主窗口腾出空间给真正的业务页面；
 *   - 与 P4 主窗口的关键区别：**本页不启动内部时钟**。时钟统一由主窗口
 *     状态栏驱动，避免多个时钟源同时发 timeTicked（单一职责 / 单一数据源）。
 *
 * 对应 WPF 的锚点：
 *   - SignalSlotDemo 相当于一个"事件发布器"（类似 C# 的 EventAggregator 一角），
 *     本页用 connect 把它与 UI 控件接线（相当于 XAML 里挂事件处理器）。
 */

#pragma once

#include <QWidget>

namespace datascope {
namespace app {
class SignalSlotDemo;  // 前向声明：头文件不引入完整定义，降低编译耦合（P4 惯例）
} // namespace app
} // namespace datascope

// 前向声明 UI 控件类型，避免头文件引入完整定义（降低编译依赖）
class QPushButton;
class QPlainTextEdit;
class QLabel;

namespace datascope {
namespace ui {

/**
 * @class DemoPage
 * @brief 信号槽五种连接演示页（P4 成果的 UI 化收尾）
 */
class DemoPage : public QWidget
{
    Q_OBJECT

public:
    /**
     * @brief 构造信号槽演示页
     * @param parent 父控件（主窗口的 QTabWidget 会以本页为页签容器）
     */
    explicit DemoPage(QWidget *parent = nullptr);

    // 禁止拷贝：QObject 派生类不可复制
    Q_DISABLE_COPY(DemoPage)

private:
    /**
     * @brief 构建本页 UI（标题 + 运行按钮 + 只读日志区 + 状态说明）
     * @note 布局、父子层级、objectName 都在这里集中构建
     */
    void buildUi();

    // ---- 复用 P4 业务对象（所有权：传 this 作 parent，由对象树回收）----
    datascope::app::SignalSlotDemo *m_demo = nullptr;

    // ---- UI 控件 ----
    QPushButton    *m_btnRun      = nullptr;  // 触发五种连接演示的按钮
    QPlainTextEdit *m_logView     = nullptr;  // 演示输出日志（只读）
    QLabel         *m_statusLabel = nullptr;  // 页面状态/教学说明
};

} // namespace ui
} // namespace datascope
