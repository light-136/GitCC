/**
 * @file demopage.cpp
 * @brief 信号槽演示页实现（复用 P4 的 SignalSlotDemo 类）
 *
 * 教学脉络（与 signalslotdemo.cpp 配合阅读）：
 *   1. 用户点击按钮 → 信号 clicked → 业务对象 runAllDemos()
 *      （UI 事件驱动业务对象，正是事件驱动编程的核心链路）；
 *   2. 业务对象 messageEmitted → 日志区追加文本
 *      （业务对象把结果回传给 UI，跨 QObject → QWidget 的信号槽链路）；
 *   3. 本页**不启动时钟**：时钟由主窗口状态栏统一驱动（timeTicked →
 *      状态栏 QLabel），保证全工程只有一个时间源（单一职责）。
 *
 * 为什么在 cpp 里才 include 完整类？
 *   - 头文件只前向声明 SignalSlotDemo，编译单元（cpp）才引入完整定义，
 *     这样任何 include 本页头的文件都不会被 Qt moc 元对象代码拖累编译时间。
 */

#include "ui/pages/demopage.h"

#include "app/signalslotdemo.h"  // 前向声明的完整定义在这里引入

#include <QPushButton>
#include <QPlainTextEdit>
#include <QLabel>
#include <QVBoxLayout>

namespace datascope {
namespace ui {

DemoPage::DemoPage(QWidget *parent)
    : QWidget(parent)
{
    // 页签统一命名：供全局 QSS 以 #demoPage 选择器定位背景/边框
    setObjectName(QStringLiteral("demoPage"));

    // 先构建控件与业务对象（m_demo / m_btnRun / m_logView 才有值）
    buildUi();

    // ---------------------------------------------------------------
    // 信号槽布线（connect 的"接线"动作集中在这里，一目了然）
    // ---------------------------------------------------------------
    // ① 按钮点击（信号 clicked）→ 业务对象运行五种连接演示（槽 runAllDemos）
    //    教学点：UI 事件驱动业务对象；sender=按钮，receiver=业务对象
    connect(m_btnRun, &QPushButton::clicked,
            m_demo, &datascope::app::SignalSlotDemo::runAllDemos);

    // ② 业务对象消息（信号 messageEmitted）→ 日志区追加文本
    //    教学点：lambda 捕获 this（DemoPage），在 lambda 体内访问 UI 控件；
    //    m_logView->appendPlainText() 自动追加换行并滚动到底部
    connect(m_demo, &datascope::app::SignalSlotDemo::messageEmitted, this,
            [this](const QString &text) {
                m_logView->appendPlainText(text);
            });
}

void DemoPage::buildUi()
{
    // ---- 复用 P4 业务对象：先创建，后续 connect 布线需要它 ----
    // 所有权：传 this 作 parent，由 QObject 对象树托管（P4 教学重点：对象树）
    m_demo = new datascope::app::SignalSlotDemo(this);

    // ---- 纵向布局：标题 → 按钮 → 日志区 → 状态说明 ----
    auto *layout = new QVBoxLayout(this);

    // ---- 顶部标题：标识本页功能（QSS 可通过 #pageTitle 定位字号/颜色）----
    auto *titleLabel = new QLabel(tr("信号槽演示"), this);
    titleLabel->setObjectName(QStringLiteral("pageTitle"));
    titleLabel->setAlignment(Qt::AlignCenter);

    // ---- 运行按钮：触发五种连接方式演示 ----
    m_btnRun = new QPushButton(tr("运行信号槽五种连接演示"), this);
    m_btnRun->setObjectName(QStringLiteral("demoRunButton"));

    // ---- 只读日志区：承接 messageEmitted 的说明文本 ----
    m_logView = new QPlainTextEdit(this);
    m_logView->setObjectName(QStringLiteral("demoLogView"));
    m_logView->setReadOnly(true);             // 只读，禁止用户编辑
    m_logView->setMaximumBlockCount(500);     // 限制行数，防止长期运行内存膨胀

    // ---- 状态说明：提示本页复用了 P4 成果、与主窗口时钟的分工 ----
    m_statusLabel = new QLabel(
        tr("本页完整复用了 P4 的 SignalSlotDemo 类：点击按钮运行五种 connect 写法演示。\n"
           "与 P4 主窗口的区别：本页不启动内部时钟，时钟统一由主窗口状态栏驱动。"),
        this);
    m_statusLabel->setObjectName(QStringLiteral("pagePlaceholder"));
    m_statusLabel->setAlignment(Qt::AlignCenter);
    m_statusLabel->setWordWrap(true);

    layout->addWidget(titleLabel);
    layout->addWidget(m_btnRun);
    // 拉伸因子 1：日志区占据按钮与状态说明之间的主要空间
    layout->addWidget(m_logView, 1);
    layout->addWidget(m_statusLabel);
}

} // namespace ui
} // namespace datascope
