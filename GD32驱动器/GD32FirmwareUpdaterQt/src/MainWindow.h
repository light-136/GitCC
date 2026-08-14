// ============================================================
//  主窗口（界面层）
//
//  【Qt知识点】UI 的两种构建方式：
//  1. Qt Designer 的 .ui 文件（XAML 的类比）
//  2. 手写 C++ 代码布局（本项目采用，便于讲解学习）
//
//  布局采用 QGroupBox 分组 + 布局管理器，对应 WPF 版
//  MainWindow.xaml 的功能分区：串口设置 / 固件文件 / 操作 / 进度 / 日志。
// ============================================================
#ifndef MAINWINDOW_H
#define MAINWINDOW_H

#include <QMainWindow>
#include <QList>
#include "models/FirmwareInfo.h"
#include "services/SerialPortService.h"
#include "services/FirmwareUpgradeService.h"
#include "models/UpgradePacketModel.h"

class QComboBox;
class QLineEdit;
class QLabel;
class QPushButton;
class QProgressBar;
class QPlainTextEdit;
class QTableView;
class QGroupBox;

class MainWindow : public QMainWindow
{
    Q_OBJECT

public:
    explicit MainWindow(QWidget *parent = nullptr);
    ~MainWindow() override;

private slots:
    // ---- 界面操作响应 ----
    void onRefreshPorts();        // 刷新串口列表
    void onToggleConnect();       // 打开/关闭串口
    void onBrowseFirmware();      // 选择固件文件
    void onQueryVersion();        // 查询 Boot 版本号
    void onStartUpgrade();        // 开始升级
    void onCancelUpgrade();       // 取消升级
    void onClearLog();            // 清空日志

    // ---- 服务层信号响应 ----
    void onSerialLog(const QString &msg);          // 串口日志
    void onConnectionChanged(bool connected);      // 连接状态变化
    void onUpgradeProgress(int percent, const QString &msg);  // 升级进度
    void onUpgradeFinished(bool success);          // 升级结束
    void onVersionReceived(const QString &version);// 版本查询结果
    void onPacketSent(quint32 offset, int length); // 数据包发送
    void onPacketStatusChanged(int row, const QString &status); // 数据包状态

private:
    // ---- 界面构建 ----
    void setupUi();
    void setupSerialGroup();   // 串口设置组
    void setupFirmwareGroup(); // 固件文件组
    void setupActionGroup();   // 操作组
    void setupLogGroup();      // 日志 + 数据包表格组

    // ---- 辅助 ----
    void appendLog(const QString &msg, const QString &color = QString());
    void setControlsEnabled(bool enabled);   // 升级期间禁用操作按钮

    // ---- 服务对象 ----
    SerialPortService      *m_serialPortService = nullptr;
    FirmwareUpgradeService *m_upgradeService    = nullptr;
    UpgradePacketModel     *m_packetModel       = nullptr;

    // 当前加载的固件
    FirmwareInfo m_firmwareInfo;
    bool m_hasFirmware = false;

    // ---- 分组框容器 ----
    QGroupBox *m_serialGroupBox   = nullptr;   // 串口设置组
    QGroupBox *m_firmwareGroupBox = nullptr;   // 固件文件组
    QGroupBox *m_actionGroupBox   = nullptr;   // 操作组
    QGroupBox *m_logGroupBox      = nullptr;   // 数据包 + 日志组

    // ---- 控件指针 ----
    // 串口设置区
    QComboBox *m_comboPort       = nullptr;
    QComboBox *m_comboBaud       = nullptr;
    QComboBox *m_comboDataBits   = nullptr;
    QComboBox *m_comboStopBits   = nullptr;
    QComboBox *m_comboParity     = nullptr;
    QPushButton *m_btnConnect    = nullptr;
    QPushButton *m_btnRefresh    = nullptr;
    QLabel *m_labelVersion       = nullptr;

    // 固件文件区
    QLineEdit *m_editFirmwarePath = nullptr;
    QLabel *m_labelFileSize       = nullptr;
    QPushButton *m_btnBrowse      = nullptr;

    // 操作区
    QPushButton *m_btnQueryVersion = nullptr;
    QPushButton *m_btnStartUpgrade = nullptr;
    QPushButton *m_btnCancelUpgrade = nullptr;
    QPushButton *m_btnClearLog      = nullptr;

    // 进度区
    QProgressBar *m_progressBar = nullptr;
    QLabel *m_labelStatus       = nullptr;

    // 日志 + 表格区
    QPlainTextEdit *m_textLog  = nullptr;
    QTableView *m_tablePackets = nullptr;
};

#endif // MAINWINDOW_H
