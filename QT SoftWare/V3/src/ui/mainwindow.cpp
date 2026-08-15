/**
 * @file mainwindow.cpp
 * @brief V3 UI 层 —— 主窗口实现
 *
 * ── 开发思路 ──
 * 主窗口的两项编排职责：
 *   1. 输入 → 动作：连接按钮把 host/port 转成 worker.start()，断开按钮转成 worker.stop()。
 *      因 worker 是不透明类型（不 include acquisition 头），用 QMetaObject::invokeMethod
 *      以方法名字符串调用；默认 AutoConnection 会依据 worker 的线程亲和性决定直连或
 *      队列调用——worker 被 moveToThread 后即为队列调用，start/stop 落到采集线程执行。
 *   2. 信号 → 页面/状态栏：worker 的 6 个信号用字符串式 SIGNAL/SLOT 转发。字符串式
 *      连接只需 QObject*（运行时按 QMetaObject 解析），是对不透明类型解耦的标准做法。
 *
 * 依赖提示（供主 Agent 集成时确认）：
 *   - QVector<dscope::domain::DataPoint> / ChannelConfig 跨线程队列投递需要
 *     qRegisterMetaType（权威规格：集中 metatype_registry.cpp 一处注册）；
 *   - closeEvent 只发 stop 请求，采集线程的 quit()+wait() 收尾由 Composition Root 负责。
 */

#include "ui/mainwindow.h"

#include "ui/monitorpage.h"
#include "ui/theme.h"

#include "domain/channelconfig.h"   // 字符串式 SIGNAL 签名中用到的类型名
#include "domain/connectiondefaults.h"  // 默认 host/port 单一常量源
#include "domain/datapoint.h"
#include "domain/errorcode.h"       // errorCodeName

#include <QCloseEvent>
#include <QLabel>
#include <QLineEdit>
#include <QMetaObject>
#include <QPalette>
#include <QPushButton>
#include <QSpinBox>
#include <QStatusBar>
#include <QToolBar>

namespace dscope {
namespace ui {

MainWindow::MainWindow(dscope::acquisition::AcquisitionWorker *worker, QWidget *parent)
    : QMainWindow(parent)
    , m_worker(worker)
{
    setWindowTitle(QStringLiteral("DataScope Studio V3 —— 工业多通道数据采集与实时监控系统"));
    resize(1280, 800);

    // 中央部件：监控页
    m_monitorPage = new MonitorPage(this);
    setCentralWidget(m_monitorPage);

    buildToolbar();

    // ── 信号转发：worker（采集线程）→ MonitorPage ──────────────────────────────
    // 说明：字符串式 SIGNAL/SLOT 的签名必须与 AcquisitionWorker 的 moc 归一化签名
    // 完全一致（QVector<dscope::domain::DataPoint> 等）。
    connect(workerObject(), SIGNAL(pointsReady(QVector<dscope::domain::DataPoint>)),
            m_monitorPage, SLOT(onPointsReady(QVector<dscope::domain::DataPoint>)));
    connect(workerObject(), SIGNAL(channelConfigReceived(QVector<dscope::domain::ChannelConfig>)),
            m_monitorPage, SLOT(onChannelConfigReceived(QVector<dscope::domain::ChannelConfig>)));
    connect(workerObject(), SIGNAL(connected()),
            m_monitorPage, SLOT(onConnected()));
    connect(workerObject(), SIGNAL(disconnected()),
            m_monitorPage, SLOT(onDisconnected()));
    connect(workerObject(), SIGNAL(connectionStateChanged(int)),
            m_monitorPage, SLOT(onConnectionStateChanged(int)));
    connect(workerObject(), SIGNAL(connectionError(int)),
            m_monitorPage, SLOT(onConnectionError(int)));

    // ── 信号转发：worker → 主窗口（工具栏状态标签 + 按钮） ─────────────────────
    connect(workerObject(), SIGNAL(connected()),
            this, SLOT(onConnected()));
    connect(workerObject(), SIGNAL(disconnected()),
            this, SLOT(onDisconnected()));
    connect(workerObject(), SIGNAL(connectionStateChanged(int)),
            this, SLOT(onConnectionStateChanged(int)));
    connect(workerObject(), SIGNAL(connectionError(int)),
            this, SLOT(onConnectionError(int)));

    // 初始状态：未连接
    updateConnectionUi(dscope::domain::ConnectionState::Disconnected);
}

void MainWindow::buildToolbar()
{
    QToolBar *toolbar = addToolBar(QStringLiteral("连接"));
    toolbar->setMovable(false);
    toolbar->setFloatable(false);

    toolbar->addWidget(new QLabel(QStringLiteral(" 主机 "), toolbar));

    m_hostEdit = new QLineEdit(QString::fromLatin1(dscope::domain::kDefaultHost), toolbar);
    m_hostEdit->setMaximumWidth(140);
    m_hostEdit->setToolTip(QStringLiteral("设备主机地址（IP 或域名）"));
    toolbar->addWidget(m_hostEdit);

    toolbar->addWidget(new QLabel(QStringLiteral(" 端口 "), toolbar));

    m_portSpin = new QSpinBox(toolbar);
    m_portSpin->setRange(1, 65535);
    m_portSpin->setValue(static_cast<int>(dscope::domain::kDefaultPort));   // 默认端口（单一常量源 connectiondefaults.h）
    m_portSpin->setMaximumWidth(90);
    toolbar->addWidget(m_portSpin);

    m_connectBtn = new QPushButton(QStringLiteral("连接"), toolbar);
    m_disconnectBtn = new QPushButton(QStringLiteral("断开"), toolbar);
    toolbar->addWidget(m_connectBtn);
    toolbar->addWidget(m_disconnectBtn);

    toolbar->addSeparator();

    m_statusLabel = new QLabel(QStringLiteral("未连接"), toolbar);
    toolbar->addWidget(m_statusLabel);

    connect(m_connectBtn, &QPushButton::clicked,
            this, &MainWindow::onConnectClicked);
    connect(m_disconnectBtn, &QPushButton::clicked,
            this, &MainWindow::onDisconnectClicked);
}

QObject *MainWindow::workerObject() const
{
    // AcquisitionWorker 是不透明类型（头文件由采集 Agent 维护），契约保证其继承 QObject。
    // reinterpret_cast 仅用于把指针当作 QObject* 交给 connect/invokeMethod；
    // 单继承下 QObject 子对象位于对象首地址，指针值一致，运行时行为安全。
    return reinterpret_cast<QObject *>(m_worker);
}

void MainWindow::onConnectClicked()
{
    const QString host = m_hostEdit->text().trimmed();
    const quint16 port = static_cast<quint16>(m_portSpin->value());

    if (host.isEmpty()) {
        statusBar()->showMessage(QStringLiteral("主机地址不能为空"), 3000);
        return;
    }

    // 以方法名调用 worker 的 start 槽（AutoConnection：worker 在采集线程时队列执行）
    const bool ok = QMetaObject::invokeMethod(workerObject(), "start", Qt::AutoConnection,
                                              Q_ARG(QString, host), Q_ARG(quint16, port));
    if (!ok)
        statusBar()->showMessage(QStringLiteral("调用 start 失败（签名不匹配？）"), 5000);
}

void MainWindow::onDisconnectClicked()
{
    QMetaObject::invokeMethod(workerObject(), "stop", Qt::AutoConnection);
}

void MainWindow::onConnected()
{
    updateConnectionUi(dscope::domain::ConnectionState::Connected);
    statusBar()->showMessage(QStringLiteral("已连接"), 3000);
}

void MainWindow::onDisconnected()
{
    updateConnectionUi(dscope::domain::ConnectionState::Disconnected);
    statusBar()->showMessage(QStringLiteral("已断开"), 3000);
}

void MainWindow::onConnectionStateChanged(int state)
{
    updateConnectionUi(static_cast<dscope::domain::ConnectionState>(state));
}

void MainWindow::onConnectionError(int code)
{
    const auto error = static_cast<dscope::domain::ErrorCode>(code);
    m_statusLabel->setText(QStringLiteral("错误"));
    QPalette pal = m_statusLabel->palette();
    pal.setColor(QPalette::WindowText, Theme::statusError());
    m_statusLabel->setPalette(pal);
    statusBar()->showMessage(
        QStringLiteral("连接错误：%1").arg(dscope::domain::errorCodeName(error)), 5000);
}

void MainWindow::updateConnectionUi(dscope::domain::ConnectionState state)
{
    // 按钮可用性：空闲（未连接/错误）可连接；连接中/已连接可断开
    const bool idle = (state == dscope::domain::ConnectionState::Disconnected
                       || state == dscope::domain::ConnectionState::Error);
    m_connectBtn->setEnabled(idle);
    m_disconnectBtn->setEnabled(!idle);

    // 状态标签文字 + 颜色（颜色走 Theme，用调色板设置，不写裸 hex）
    m_statusLabel->setText(dscope::domain::connectionStateName(state));

    QColor color = Theme::statusDisconnected();
    if (state == dscope::domain::ConnectionState::Connected)
        color = Theme::statusConnected();
    else if (state == dscope::domain::ConnectionState::Error)
        color = Theme::statusError();

    QPalette pal = m_statusLabel->palette();
    pal.setColor(QPalette::WindowText, color);
    m_statusLabel->setPalette(pal);
}

void MainWindow::closeEvent(QCloseEvent *event)
{
    // 关窗前请求停止采集；线程 quit()+wait() 收尾由 Composition Root 负责
    QMetaObject::invokeMethod(workerObject(), "stop", Qt::AutoConnection);
    event->accept();
}

} // namespace ui
} // namespace dscope
