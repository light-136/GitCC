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

#include "services/dataservice.h"
#include "services/recordmanager.h"
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

    // 数据总线必须先于页面创建：DevicePage（V2）构造时需要注入 DataService
    // 作为连接/断开的驱动源（依赖注入，而非页面内部 new）。
    m_service = new datascope::services::DataService(this);

    buildMenuBar();
    buildToolBar();
    buildCentralTabs();
    buildStatusBar();

    // ---- P12 集成：数据总线接入监控页与工具栏 ----
    // 数据源可替换设计：监控页只认 onDataUpdated(QVector<DataPoint>) 一个入口，
    // P14 演示源 → P12 真实链路，MonitorPage 零改动。
    connect(m_service, &datascope::services::DataService::dataUpdated,
            m_monitorPage, &datascope::ui::MonitorPage::onDataUpdated);
    connect(m_service, &datascope::services::DataService::connected,
            this, &MainWindow::onServiceConnected);
    connect(m_service, &datascope::services::DataService::disconnected,
            this, &MainWindow::onServiceDisconnected);
    connect(m_service, &datascope::services::DataService::connectionError,
            this, &MainWindow::onServiceError);

    connect(m_actConnect, &QAction::triggered, this, &MainWindow::connectDevice);
    connect(m_actStart, &QAction::triggered,
            m_service, &datascope::services::DataService::startAcquisition);
    connect(m_actStop, &QAction::triggered, this, &MainWindow::stopAcquisition);

    // ---- P15 集成：数据记录与回放 ----
    // 数据流：采集 → 数据总线(dataUpdated) → RecordManager(记录中才落盘 CSV)
    //         回放：RecordManager(replayData) → MonitorPage(同一 onDataUpdated 入口)
    m_recordManager = new datascope::services::RecordManager(this);
    m_recordPage->setRecordManager(m_recordManager);
    connect(m_service, &datascope::services::DataService::dataUpdated,
            m_recordManager, &datascope::services::RecordManager::appendData);
    connect(m_recordManager, &datascope::services::RecordManager::replayData,
            m_monitorPage, &datascope::ui::MonitorPage::onDataUpdated);

    // ---- V2 设置页接线：主题切换即时生效 ----
    // 设置页保存主题后发 themeChanged，主窗口据此加载/清除 QSS
    connect(m_settingsPage, &datascope::ui::SettingsPage::themeChanged,
            this, &MainWindow::onThemeChanged);

    // 启动后自动连接本机模拟设备（127.0.0.1:40001）：
    // 验收时先启动 simulator，界面即自动出现真实采集曲线；
    // 未启动则状态栏报错，可随时点击[连接设备]重试。
    connectDevice();
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

    // P12：三个动作先创建为成员，信号接线统一放在构造函数"P12 集成段"——
    // 因为连接目标是 m_service（数据总线），须等其创建后再 connect。
    m_actConnect = toolBar->addAction(tr("连接设备"));
    m_actConnect->setToolTip(tr("连接采集设备（默认 127.0.0.1:40001）"));

    m_actStart = toolBar->addAction(tr("启动采集"));
    m_actStart->setToolTip(tr("开始实时采集"));
    m_actStart->setEnabled(false);   // 未连接时不可用

    m_actStop = toolBar->addAction(tr("停止采集"));
    m_actStop->setToolTip(tr("停止实时采集"));
    m_actStop->setEnabled(false);   // 未连接时不可用
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
    // V2：DevicePage 注入数据总线（设备管理页的连接/断开由它驱动）
    m_devicePage   = new datascope::ui::DevicePage(m_service, m_tabs);
    m_recordPage   = new datascope::ui::RecordPage(m_tabs);
    m_settingsPage = new datascope::ui::SettingsPage(m_tabs);
    m_demoPage     = new datascope::ui::DemoPage(m_tabs);

    m_tabs->addTab(m_monitorPage,  tr("监控总览"));   // P14 实时曲线/仪表
    m_tabs->addTab(m_devicePage,   tr("设备管理"));   // P8/P9 设备列表与连接
    m_tabs->addTab(m_recordPage,   tr("数据记录"));   // P15 记录与回放
    m_tabs->addTab(m_settingsPage, tr("设置"));       // P7 配置 UI
    m_tabs->addTab(m_demoPage,     tr("信号槽演示")); // P4 成果展示

    setCentralWidget(m_tabs);

    // P14→P12：演示数据源已替换为真实采集链路（DataService）。
    // 数据源可替换设计：监控页只认 onDataUpdated(QVector<DataPoint>) 一个入口，
    // P14 演示源(setDemoRunning) 与 P12 真实链路(dataUpdated 信号)在此切换，
    // MonitorPage 内部零改动。P12 起启用真实链路，演示源显式停用。
    m_monitorPage->setDemoRunning(false);
}

// ---------------------------------------------------------------------------
// 状态栏：左侧消息区 + 右侧常驻时钟（QTimer 每秒刷新，复用 P4 的定时器思路）
// ---------------------------------------------------------------------------
void MainWindow::buildStatusBar()
{
    statusBar()->showMessage(tr("就绪 —— 请在左侧选择功能页签"));

    // 右侧：连接状态指示 + 时钟（QTimer 每秒刷新，复用 P4 的定时器思路）
    m_connLabel = new QLabel(tr("○ 未连接"), this);
    statusBar()->addPermanentWidget(m_connLabel);

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
// P12：数据链路控制（连接 / 停止 / 状态反馈）
// ---------------------------------------------------------------------------
void MainWindow::connectDevice()
{
    // 连接本机模拟设备（simulator 默认监听 0.0.0.0:40001），随即启动采集。
    // connectTo 是异步的（队列投递到采集线程），结果由 onServiceConnected /
    // onServiceError 通知。
    statusBar()->showMessage(tr("正在连接 127.0.0.1:40001 ..."));
    m_service->connectTo(QStringLiteral("127.0.0.1"), 40001);
    m_service->startAcquisition();
}

void MainWindow::stopAcquisition()
{
    m_service->stopAcquisition();
    statusBar()->showMessage(tr("已停止采集"), 3000);
    m_actStart->setEnabled(true);   // 停止后可再次启动
    m_actStop->setEnabled(false);
}

void MainWindow::onServiceConnected()
{
    m_actConnect->setEnabled(false);   // 已连接：禁用"连接"避免重复
    m_actStart->setEnabled(true);
    m_actStop->setEnabled(true);
    m_connLabel->setText(tr("● 已连接"));
    m_connLabel->setStyleSheet(QStringLiteral("color:#2ecc71; font-weight:bold;"));  // 绿
    statusBar()->showMessage(tr("设备已连接"), 3000);
}

void MainWindow::onServiceDisconnected()
{
    m_actConnect->setEnabled(true);
    m_actStart->setEnabled(false);
    m_actStop->setEnabled(false);
    m_connLabel->setText(tr("○ 未连接"));
    m_connLabel->setStyleSheet(QStringLiteral("color:#95a5a6;"));
    statusBar()->showMessage(tr("设备已断开"), 3000);
}

void MainWindow::onServiceError(const QString &message)
{
    // 链路错误（连接被拒/超时/解析异常）→ 状态栏红字提示 + 按钮复位
    statusBar()->showMessage(tr("错误：%1").arg(message), 5000);
    m_actStart->setEnabled(false);
    m_actStop->setEnabled(false);
    m_connLabel->setText(tr("○ 未连接"));
    m_connLabel->setStyleSheet(QStringLiteral("color:#e74c3c;"));   // 红
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

// ---------------------------------------------------------------------------
// V2：设置页主题切换（深色 → 加载 QSS；亮色 → 系统默认样式）
// ---------------------------------------------------------------------------
void MainWindow::onThemeChanged(const QString &theme)
{
    if (theme == QStringLiteral("light")) {
        // 亮色主题：清除样式表，恢复系统默认外观
        setStyleSheet(QString());
        statusBar()->showMessage(tr("已切换亮色主题"), 3000);
    } else {
        // 深色主题：重新加载 QSS 资源
        applyStyleSheet();
        statusBar()->showMessage(tr("已切换深色主题"), 3000);
    }
}
