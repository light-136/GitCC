/**
 * @file mainwindow.cpp
 * @brief 主窗口实现（P5：页签布局 + 菜单/工具栏 + QSS 主题）
 *
 * P5 教学脉络（对照 WPF）：
 *   - QTabWidget ≈ WPF TabControl；每个 QWidget 页 ≈ TabItem；
 *   - QMenuBar/QToolBar/QStatusBar ≈ WPF 的 Menu/DockPanel ToolBar/StatusBar；
 *   - QSS 加载 ≈ App.xaml 全局资源字典；
 *   - 布局用 setCentralWidget + QTabWidget，替代 WPF 的 Grid 根元素。
 */

#include "ui/mainwindow.h"

#include "ui/pages/monitorpage.h"
#include "ui/pages/devicepage.h"
#include "ui/pages/recordpage.h"
#include "ui/pages/settingspage.h"
#include "ui/pages/demopage.h"

#include <QTabWidget>
#include <QMenuBar>
#include <QToolBar>
#include <QStatusBar>
#include <QLabel>
#include <QTimer>
#include <QAction>
#include <QKeySequence>
#include <QMessageBox>
#include <QFile>
#include <QDebug>
#include <QDateTime>

namespace {
// 文件内私有常量（匿名命名空间，类似 C# private const）
const QString kWindowTitle = QStringLiteral("DataScope Studio —— 工业多通道数据采集与实时监控系统");
const QString kStyleSheetPath = QStringLiteral(":/qss/main.qss");   // qrc 资源路径
const int kWindowWidth  = 1280;
const int kWindowHeight = 800;
} // namespace

MainWindow::MainWindow(QWidget *parent)
    : QMainWindow(parent)
{
    setWindowTitle(kWindowTitle);
    resize(kWindowWidth, kWindowHeight);

    applyStyleSheet();     // 先应用主题（页面创建后背景统一）
    buildMenuBar();
    buildToolBar();
    buildCentralTabs();
    buildStatusBar();
}

MainWindow::~MainWindow() = default;  // 对象树统一回收所有 child

// ---------------------------------------------------------------------------
// 菜单栏：文件 / 帮助
// ---------------------------------------------------------------------------
void MainWindow::buildMenuBar()
{
    // 文件菜单
    auto *fileMenu = menuBar()->addMenu(tr("文件(&F)"));
    auto *quitAction = fileMenu->addAction(tr("退出(&X)"));
    quitAction->setShortcut(QKeySequence::Quit);   // Ctrl+Q（WPF 的 InputBinding 对应物）
    connect(quitAction, &QAction::triggered, this, &QWidget::close);

    // 帮助菜单
    auto *helpMenu = menuBar()->addMenu(tr("帮助(&H)"));
    auto *aboutAction = helpMenu->addAction(tr("关于(&A)"));
    connect(aboutAction, &QAction::triggered, this, [this]() {
        QMessageBox::about(this, tr("关于 DataScope Studio"),
            tr("DataScope Studio —— 工业多通道数据采集与实时监控系统\n"
               "版本 0.1.0 ｜ Qt 5.12.2 ｜ 用于 Qt 系统学习与真实工程实践。"));
    });
}

// ---------------------------------------------------------------------------
// 工具栏：连接 / 启动采集 / 停止采集（P9 前为占位动作）
// ---------------------------------------------------------------------------
void MainWindow::buildToolBar()
{
    auto *toolBar = addToolBar(tr("主工具栏"));
    toolBar->setMovable(false);   // 固定工具栏，避免布局漂移

    // 占位动作：P9 接入串口连接，P10 接入协议引擎
    auto *actConnect = toolBar->addAction(tr("连接设备"));
    actConnect->setToolTip(tr("连接采集设备（P9 接入串口/TCP）"));

    auto *actStart = toolBar->addAction(tr("启动采集"));
    actStart->setToolTip(tr("开始实时采集（P10 接入协议引擎）"));

    auto *actStop = toolBar->addAction(tr("停止采集"));
    actStop->setToolTip(tr("停止实时采集（P10 接入协议引擎）"));
    actStop->setEnabled(false);   // 初始不可用

    // 教学点：占位动作目前不做事，但框架先行——后续阶段只需替换槽函数体
    Q_UNUSED(actConnect); Q_UNUSED(actStart); Q_UNUSED(actStop);
}

// ---------------------------------------------------------------------------
// 中央页签：5 个业务页面（各页面由 U2 Agent 实现，主窗口负责组装）
// ---------------------------------------------------------------------------
void MainWindow::buildCentralTabs()
{
    m_tabs = new QTabWidget(this);
    m_tabs->setDocumentMode(true);   // 页签条更扁平（类似 Chrome 风格）

    // 页面对象均挂到 m_tabs 下，由对象树托管生命周期
    m_monitorPage  = new datascope::ui::MonitorPage(m_tabs);
    m_devicePage   = new datascope::ui::DevicePage(m_tabs);
    m_recordPage   = new datascope::ui::RecordPage(m_tabs);
    m_settingsPage = new datascope::ui::SettingsPage(m_tabs);
    m_demoPage     = new datascope::ui::DemoPage(m_tabs);

    m_tabs->addTab(m_monitorPage,  tr("监控总览"));   // P14 实时曲线/仪表
    m_tabs->addTab(m_devicePage,   tr("设备管理"));   // P8/P9 设备列表与连接
    m_tabs->addTab(m_recordPage,   tr("数据记录"));   // P15 记录与回放
    m_tabs->addTab(m_settingsPage, tr("设置"));       // P7 配置 UI
    m_tabs->addTab(m_demoPage,     tr("信号槽演示")); // P4 成果展示

    setCentralWidget(m_tabs);
}

// ---------------------------------------------------------------------------
// 状态栏：左侧消息区 + 右侧常驻时钟（QTimer 每秒刷新，复用 P4 的定时器思路）
// ---------------------------------------------------------------------------
void MainWindow::buildStatusBar()
{
    statusBar()->showMessage(tr("就绪 —— 请在左侧选择功能页签"));

    // 右侧时钟：QTimer(1000ms) + lambda 更新 QLabel
    m_timeLabel = new QLabel(tr("--:--:--"), this);
    statusBar()->addPermanentWidget(m_timeLabel);

    m_clock = new QTimer(this);
    m_clock->setInterval(1000);
    connect(m_clock, &QTimer::timeout, this, [this]() {
        m_timeLabel->setText(QDateTime::currentDateTime().toString(QStringLiteral("HH:mm:ss")));
    });
    m_clock->start();
}

// ---------------------------------------------------------------------------
// 应用 QSS 深色主题（对应 WPF 全局资源加载）
// ---------------------------------------------------------------------------
void MainWindow::applyStyleSheet()
{
    QFile qss(kStyleSheetPath);
    if (qss.open(QIODevice::ReadOnly | QIODevice::Text)) {
        setStyleSheet(QString::fromUtf8(qss.readAll()));
        qss.close();
    } else {
        // 资源加载失败不中断程序（退化为系统默认样式）
        qWarning("MainWindow: 无法加载样式表 %s", qPrintable(kStyleSheetPath));
    }
}
