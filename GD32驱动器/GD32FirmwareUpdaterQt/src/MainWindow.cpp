// ============================================================
//  主窗口实现
//
//  【Qt知识点】布局管理器（Layout）体系：
//  - QVBoxLayout：垂直排列（类比 WPF 的 StackPanel Orientation=Vertical）
//  - QHBoxLayout：水平排列（类比 StackPanel Horizontal）
//  - QGridLayout：网格排列（类比 Grid）
//  布局管理器自动处理窗口缩放时的重排，等价 WPF 的布局系统。
//
//  【Qt知识点】信号-槽连接 UI：
//  connect(按钮, &QPushButton::clicked, this, &MainWindow::onXxx)
//  对比 WPF 的 Click += (s,e)=>{} 事件订阅。
// ============================================================
#include "MainWindow.h"
#include "protocol/CommandDefinitions.h"
#include "services/FirmwareParser.h"

#include <QGroupBox>
#include <QHBoxLayout>
#include <QVBoxLayout>
#include <QGridLayout>
#include <QFormLayout>
#include <QComboBox>
#include <QLineEdit>
#include <QLabel>
#include <QPushButton>
#include <QProgressBar>
#include <QPlainTextEdit>
#include <QTableView>
#include <QFileDialog>
#include <QMessageBox>
#include <QSerialPort>
#include <QFont>
#include <QHeaderView>
#include <QScrollBar>

MainWindow::MainWindow(QWidget *parent)
    : QMainWindow(parent)
{
    // ---- 创建服务对象（QObject 父子关系：this 负责销毁） ----
    m_serialPortService = new SerialPortService(this);
    m_upgradeService    = new FirmwareUpgradeService(m_serialPortService, this);
    m_packetModel       = new UpgradePacketModel(this);

    setupUi();

    setWindowTitle(QStringLiteral("GD32 低压驱动器固件升级工具 (Qt版)"));
    resize(880, 660);

    // ---- 连接服务层信号到界面槽 ----
    connect(m_serialPortService, &SerialPortService::logMessage,
            this, &MainWindow::onSerialLog);
    connect(m_serialPortService, &SerialPortService::connectionChanged,
            this, &MainWindow::onConnectionChanged);
    connect(m_upgradeService, &FirmwareUpgradeService::progressChanged,
            this, &MainWindow::onUpgradeProgress);
    connect(m_upgradeService, &FirmwareUpgradeService::logMessage,
            this, &MainWindow::onSerialLog);
    connect(m_upgradeService, &FirmwareUpgradeService::finished,
            this, &MainWindow::onUpgradeFinished);
    connect(m_upgradeService, &FirmwareUpgradeService::versionReceived,
            this, &MainWindow::onVersionReceived);
    connect(m_upgradeService, &FirmwareUpgradeService::packetSent,
            this, &MainWindow::onPacketSent);
    connect(m_upgradeService, &FirmwareUpgradeService::packetStatusChanged,
            this, &MainWindow::onPacketStatusChanged);

    // ---- 初始化界面状态 ----
    onRefreshPorts();
    m_btnCancelUpgrade->setEnabled(false);
    m_btnStartUpgrade->setEnabled(false);
    m_btnQueryVersion->setEnabled(false);

    // ---- 全局 QSS 样式（对比 WPF 的 Styles/Resources） ----
    setStyleSheet(QStringLiteral(R"(
        QMainWindow { background-color: #f0f2f5; }
        QGroupBox {
            font-weight: bold; border: 1px solid #d0d7de;
            border-radius: 6px; margin-top: 10px; padding-top: 12px;
            background-color: #ffffff;
        }
        QGroupBox::title { subcontrol-origin: margin; left: 12px; padding: 0 5px; color: #34495e; }
        QPushButton {
            background-color: #3b82f6; color: white; border: none;
            border-radius: 4px; padding: 6px 16px;
        }
        QPushButton:hover { background-color: #2563eb; }
        QPushButton:pressed { background-color: #1d4ed8; }
        QPushButton:disabled { background-color: #b6c2d1; color: #e5e7eb; }
        QPushButton#dangerBtn { background-color: #ef4444; }
        QPushButton#dangerBtn:hover { background-color: #dc2626; }
        QComboBox, QLineEdit {
            border: 1px solid #cbd5e1; border-radius: 4px;
            padding: 4px 8px; background-color: white;
        }
        QComboBox:disabled, QLineEdit:disabled { background-color: #f1f5f9; }
        QProgressBar {
            border: 1px solid #cbd5e1; border-radius: 5px;
            text-align: center; background-color: #e2e8f0; height: 20px;
        }
        QProgressBar::chunk { background-color: #3b82f6; border-radius: 4px; }
        QPlainTextEdit {
            border: 1px solid #cbd5e1; border-radius: 4px;
            background-color: #0f172a; color: #e2e8f0;
            font-family: Consolas, "Courier New", monospace; font-size: 13px;
        }
        QTableView {
            border: 1px solid #cbd5e1; border-radius: 4px;
            background-color: white; gridline-color: #e2e8f0;
        }
        QHeaderView::section {
            background-color: #f1f5f9; color: #475569; padding: 5px;
            border: none; border-right: 1px solid #cbd5e1;
        }
        QLabel { color: #1e293b; }
    )"));
}

MainWindow::~MainWindow() = default;

// ==================== 界面构建 ====================

void MainWindow::setupUi()
{
    QWidget *central = new QWidget(this);
    QVBoxLayout *mainLayout = new QVBoxLayout(central);
    mainLayout->setSpacing(8);

    setupSerialGroup();
    setupFirmwareGroup();
    setupActionGroup();

    // ---- 进度条 + 状态（放在操作组下方，独立成区） ----
    m_progressBar = new QProgressBar(central);
    m_progressBar->setRange(0, 100);
    m_progressBar->setValue(0);
    m_progressBar->setFormat(QStringLiteral("就绪"));
    m_labelStatus = new QLabel(QStringLiteral("就绪"), central);
    m_labelStatus->setStyleSheet(QStringLiteral("font-weight: bold; color: #334155;"));

    mainLayout->addWidget(m_serialGroupBox);
    mainLayout->addWidget(m_firmwareGroupBox);
    mainLayout->addWidget(m_actionGroupBox);
    mainLayout->addWidget(m_progressBar);
    mainLayout->addWidget(m_labelStatus);
    setupLogGroup();
    mainLayout->addWidget(m_logGroupBox, 1);

    setCentralWidget(central);
}

void MainWindow::setupSerialGroup()
{
    m_serialGroupBox = new QGroupBox(QStringLiteral("串口设置"), this);
    QGridLayout *grid = new QGridLayout(m_serialGroupBox);
    grid->setHorizontalSpacing(6);

    // ---- 行 0：端口 / 波特率 / 数据位 / 停止位 / 校验位 ----
    grid->addWidget(new QLabel(QStringLiteral("端口:"), m_serialGroupBox), 0, 0);
    m_comboPort = new QComboBox(m_serialGroupBox);
    m_comboPort->setMinimumWidth(90);
    grid->addWidget(m_comboPort, 0, 1);
    m_btnRefresh = new QPushButton(QStringLiteral("刷新"), m_serialGroupBox);
    grid->addWidget(m_btnRefresh, 0, 2);

    grid->addWidget(new QLabel(QStringLiteral("波特率:"), m_serialGroupBox), 0, 3);
    m_comboBaud = new QComboBox(m_serialGroupBox);
    m_comboBaud->addItems(QStringList{ QStringLiteral("9600"), QStringLiteral("19200"),
        QStringLiteral("38400"), QStringLiteral("57600"), QStringLiteral("115200"),
        QStringLiteral("230400"), QStringLiteral("460800"), QStringLiteral("921600") });
    m_comboBaud->setCurrentText(QStringLiteral("115200"));  // 默认值与官方工具一致
    grid->addWidget(m_comboBaud, 0, 4);

    grid->addWidget(new QLabel(QStringLiteral("数据位:"), m_serialGroupBox), 0, 5);
    m_comboDataBits = new QComboBox(m_serialGroupBox);
    m_comboDataBits->addItems(QStringList{ QStringLiteral("7"), QStringLiteral("8") });
    m_comboDataBits->setCurrentText(QStringLiteral("8"));
    grid->addWidget(m_comboDataBits, 0, 6);

    grid->addWidget(new QLabel(QStringLiteral("停止位:"), m_serialGroupBox), 0, 7);
    m_comboStopBits = new QComboBox(m_serialGroupBox);
    m_comboStopBits->addItems(QStringList{ QStringLiteral("1"), QStringLiteral("1.5"), QStringLiteral("2") });
    m_comboStopBits->setCurrentText(QStringLiteral("1"));
    grid->addWidget(m_comboStopBits, 0, 8);

    grid->addWidget(new QLabel(QStringLiteral("校验位:"), m_serialGroupBox), 0, 9);
    m_comboParity = new QComboBox(m_serialGroupBox);
    m_comboParity->addItems(QStringList{ QStringLiteral("NONE"), QStringLiteral("ODD"), QStringLiteral("EVEN") });
    m_comboParity->setCurrentText(QStringLiteral("EVEN"));  // 协议默认偶校验
    grid->addWidget(m_comboParity, 0, 10);

    // ---- 行 1：连接按钮 + Boot 版本号 ----
    m_btnConnect = new QPushButton(QStringLiteral("打开串口"), m_serialGroupBox);
    grid->addWidget(m_btnConnect, 1, 0, 1, 3);

    grid->addWidget(new QLabel(QStringLiteral("Boot版本号:"), m_serialGroupBox), 1, 4);
    m_labelVersion = new QLabel(QStringLiteral("未知"), m_serialGroupBox);
    m_labelVersion->setStyleSheet(QStringLiteral("font-weight: bold; color: #3b82f6;"));
    grid->addWidget(m_labelVersion, 1, 5);

    grid->setColumnStretch(11, 1);

    // 按钮事件
    connect(m_btnRefresh, &QPushButton::clicked, this, &MainWindow::onRefreshPorts);
    connect(m_btnConnect, &QPushButton::clicked, this, &MainWindow::onToggleConnect);
}

void MainWindow::setupFirmwareGroup()
{
    m_firmwareGroupBox = new QGroupBox(QStringLiteral("固件文件"), this);
    QHBoxLayout *h = new QHBoxLayout(m_firmwareGroupBox);

    m_editFirmwarePath = new QLineEdit(m_firmwareGroupBox);
    m_editFirmwarePath->setReadOnly(true);
    m_editFirmwarePath->setPlaceholderText(QStringLiteral("请选择 .bin 固件文件"));
    h->addWidget(m_editFirmwarePath, 1);

    m_btnBrowse = new QPushButton(QStringLiteral("选择固件"), m_firmwareGroupBox);
    h->addWidget(m_btnBrowse);

    h->addWidget(new QLabel(QStringLiteral("大小:"), m_firmwareGroupBox));
    m_labelFileSize = new QLabel(QStringLiteral("-"), m_firmwareGroupBox);
    m_labelFileSize->setStyleSheet(QStringLiteral("font-weight: bold; color: #16a34a;"));
    h->addWidget(m_labelFileSize);

    connect(m_btnBrowse, &QPushButton::clicked, this, &MainWindow::onBrowseFirmware);
}

void MainWindow::setupActionGroup()
{
    m_actionGroupBox = new QGroupBox(QStringLiteral("操作"), this);
    QHBoxLayout *h = new QHBoxLayout(m_actionGroupBox);

    m_btnQueryVersion = new QPushButton(QStringLiteral("查询版本"), m_actionGroupBox);
    m_btnStartUpgrade = new QPushButton(QStringLiteral("开始升级"), m_actionGroupBox);
    m_btnCancelUpgrade = new QPushButton(QStringLiteral("取消升级"), m_actionGroupBox);
    m_btnCancelUpgrade->setObjectName(QStringLiteral("dangerBtn"));
    m_btnClearLog = new QPushButton(QStringLiteral("清空日志"), m_actionGroupBox);

    h->addWidget(m_btnQueryVersion);
    h->addWidget(m_btnStartUpgrade);
    h->addWidget(m_btnCancelUpgrade);
    h->addStretch();
    h->addWidget(m_btnClearLog);

    connect(m_btnQueryVersion, &QPushButton::clicked, this, &MainWindow::onQueryVersion);
    connect(m_btnStartUpgrade, &QPushButton::clicked, this, &MainWindow::onStartUpgrade);
    connect(m_btnCancelUpgrade, &QPushButton::clicked, this, &MainWindow::onCancelUpgrade);
    connect(m_btnClearLog, &QPushButton::clicked, this, &MainWindow::onClearLog);
}

void MainWindow::setupLogGroup()
{
    m_logGroupBox = new QGroupBox(QStringLiteral("升级数据包 & 通讯日志"), this);
    QVBoxLayout *v = new QVBoxLayout(m_logGroupBox);

    // ---- 数据包表格（Model/View 教学演示） ----
    m_tablePackets = new QTableView(m_logGroupBox);
    m_tablePackets->setModel(m_packetModel);
    m_tablePackets->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_tablePackets->setEditTriggers(QAbstractItemView::NoEditTriggers);
    m_tablePackets->verticalHeader()->setVisible(false);
    m_tablePackets->horizontalHeader()->setStretchLastSection(true);
    m_tablePackets->setMaximumHeight(150);
    v->addWidget(m_tablePackets);

    // ---- 通讯日志区（深色底，终端风格） ----
    m_textLog = new QPlainTextEdit(m_logGroupBox);
    m_textLog->setReadOnly(true);
    m_textLog->setMaximumBlockCount(5000);  // 限制日志条数，防止内存膨胀
    v->addWidget(m_textLog, 1);
}

// ==================== 界面操作响应 ====================

void MainWindow::onRefreshPorts()
{
    QStringList ports = SerialPortService::availablePorts();
    QString current = m_comboPort->currentText();

    m_comboPort->clear();
    m_comboPort->addItems(ports);

    // 尽量保持用户原来的选择
    int idx = m_comboPort->findText(current);
    if (idx >= 0)
        m_comboPort->setCurrentIndex(idx);

    if (ports.isEmpty())
        appendLog(QStringLiteral("未检测到可用串口"), QStringLiteral("#f59e0b"));
    else
        appendLog(QStringLiteral("检测到 %1 个可用串口: %2")
                      .arg(ports.size()).arg(ports.join(QStringLiteral(", "))),
                  QStringLiteral("#94a3b8"));
}

void MainWindow::onToggleConnect()
{
    if (m_serialPortService->isConnected())
    {
        m_serialPortService->close();
        return;
    }

    if (m_comboPort->currentText().isEmpty())
    {
        appendLog(QStringLiteral("请先选择 COM 端口"), QStringLiteral("#ef4444"));
        return;
    }

    SerialPortConfig config;
    config.portName = m_comboPort->currentText();
    config.baudRate = m_comboBaud->currentText().toInt();

    // 数据位 / 停止位 / 校验位 映射为 QSerialPort 枚举
    config.dataBits = (m_comboDataBits->currentText() == QStringLiteral("8"))
                      ? QSerialPort::Data8 : QSerialPort::Data7;
    config.stopBits = (m_comboStopBits->currentText() == QStringLiteral("2"))
                      ? QSerialPort::TwoStop
                      : (m_comboStopBits->currentText() == QStringLiteral("1.5"))
                        ? QSerialPort::OneAndHalfStop : QSerialPort::OneStop;
    config.parity = (m_comboParity->currentText() == QStringLiteral("EVEN"))
                    ? QSerialPort::EvenParity
                    : (m_comboParity->currentText() == QStringLiteral("ODD"))
                      ? QSerialPort::OddParity : QSerialPort::NoParity;

    m_serialPortService->open(config);
}

void MainWindow::onBrowseFirmware()
{
    // 打开文件选择对话框（类比 WPF 的 OpenFileDialog）
    QString filePath = QFileDialog::getOpenFileName(
        this, QStringLiteral("选择固件文件"), QString(),
        QStringLiteral("BIN固件文件 (*.bin);;所有文件 (*.*)"));
    if (filePath.isEmpty())
        return;

    QString error;
    FirmwareInfo info;
    if (!FirmwareParser::loadFirmware(filePath, &info, &error))
    {
        appendLog(QStringLiteral("加载固件失败: %1").arg(error), QStringLiteral("#ef4444"));
        return;
    }

    m_firmwareInfo = info;
    m_hasFirmware = true;
    m_editFirmwarePath->setText(info.filePath);
    m_labelFileSize->setText(info.fileSizeText());
    appendLog(QStringLiteral("已加载固件: %1 (%2)").arg(info.fileName, info.fileSizeText()),
              QStringLiteral("#16a34a"));

    // 若串口已开则允许开始升级
    m_btnStartUpgrade->setEnabled(m_serialPortService->isConnected());
}

void MainWindow::onQueryVersion()
{
    if (!m_serialPortService->isConnected())
    {
        appendLog(QStringLiteral("请先打开串口"), QStringLiteral("#ef4444"));
        return;
    }
    m_labelVersion->setText(QStringLiteral("查询中..."));
    m_upgradeService->queryVersion();
}

void MainWindow::onVersionReceived(const QString &version)
{
    m_labelVersion->setText(version.isEmpty()
                            ? QStringLiteral("查询失败")
                            : version);
}

void MainWindow::onStartUpgrade()
{
    if (!m_serialPortService->isConnected())
    {
        appendLog(QStringLiteral("请先打开串口"), QStringLiteral("#ef4444"));
        return;
    }
    if (!m_hasFirmware || m_firmwareInfo.data.isEmpty())
    {
        appendLog(QStringLiteral("请先选择固件文件"), QStringLiteral("#ef4444"));
        return;
    }

    // 重置本轮升级的界面状态
    m_packetModel->clear();
    m_progressBar->setValue(0);
    m_labelStatus->setText(QStringLiteral("开始升级..."));
    setControlsEnabled(false);
    m_btnCancelUpgrade->setEnabled(true);

    appendLog(QStringLiteral("========== 开始固件升级 =========="), QStringLiteral("#3b82f6"));
    m_upgradeService->startUpgrade(m_firmwareInfo.data);
}

void MainWindow::onCancelUpgrade()
{
    appendLog(QStringLiteral("正在取消升级..."), QStringLiteral("#f59e0b"));
    m_upgradeService->cancel();
}

void MainWindow::onClearLog()
{
    m_textLog->clear();
}

// ==================== 服务层信号响应 ====================

void MainWindow::onSerialLog(const QString &msg)
{
    // 根据消息内容智能着色
    QString color;
    if (msg.contains(QStringLiteral("[TX]")))
        color = QStringLiteral("#60a5fa");
    else if (msg.contains(QStringLiteral("[RX]")))
        color = QStringLiteral("#4ade80");
    else if (msg.contains(QStringLiteral("失败")) || msg.contains(QStringLiteral("超时"))
             || msg.contains(QStringLiteral("取消")))
        color = QStringLiteral("#f87171");
    else if (msg.contains(QStringLiteral("成功")) || msg.contains(QStringLiteral("完成")))
        color = QStringLiteral("#4ade80");
    appendLog(msg, color);
}

void MainWindow::onConnectionChanged(bool connected)
{
    m_btnConnect->setText(connected ? QStringLiteral("关闭串口") : QStringLiteral("打开串口"));

    // 连接状态下才允许查询版本 / 开始升级
    m_btnQueryVersion->setEnabled(connected);
    m_btnStartUpgrade->setEnabled(connected && m_hasFirmware);
}

void MainWindow::onUpgradeProgress(int percent, const QString &msg)
{
    m_progressBar->setValue(percent);
    m_progressBar->setFormat(QStringLiteral("%1%").arg(percent));
    m_labelStatus->setText(msg);
}

void MainWindow::onUpgradeFinished(bool success)
{
    setControlsEnabled(true);
    m_btnCancelUpgrade->setEnabled(false);
    m_labelStatus->setText(success ? QStringLiteral("固件升级成功！")
                                   : QStringLiteral("固件升级失败"));
    appendLog(QStringLiteral("========== 固件升级%1 ==========").arg(success ? "成功" : "失败"),
              success ? QStringLiteral("#4ade80") : QStringLiteral("#f87171"));
}

void MainWindow::onPacketSent(quint32 offset, int length)
{
    m_packetModel->addPacket(offset, length);
}

void MainWindow::onPacketStatusChanged(int row, const QString &status)
{
    m_packetModel->updateStatus(row, status);
}

// ==================== 辅助函数 ====================

void MainWindow::appendLog(const QString &msg, const QString &color)
{
    // QPlainTextEdit::appendHtml 支持富文本；消息内容需转义防止 HTML 注入
    QString html = msg.toHtmlEscaped();
    if (!color.isEmpty())
        html = QStringLiteral("<span style=\"color:%1;\">%2</span>").arg(color, html);
    m_textLog->appendHtml(html);
}

void MainWindow::setControlsEnabled(bool enabled)
{
    m_btnConnect->setEnabled(enabled);
    m_btnRefresh->setEnabled(enabled);
    m_btnBrowse->setEnabled(enabled);
    m_btnQueryVersion->setEnabled(enabled);
    m_btnStartUpgrade->setEnabled(enabled && m_hasFirmware && m_serialPortService->isConnected());
    m_btnClearLog->setEnabled(enabled);
    m_comboPort->setEnabled(enabled);
    m_comboBaud->setEnabled(enabled);
    m_comboDataBits->setEnabled(enabled);
    m_comboStopBits->setEnabled(enabled);
    m_comboParity->setEnabled(enabled);
}
