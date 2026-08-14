// ============================================================
//  固件升级引擎（四步流程状态机）
//
//  【Qt知识点】事件驱动的状态机设计（替代 C# 的 async/await）：
//  WPF 版用 await 顺序执行四步，Qt 里"发送指令→等回复"是异步的，
//  因此用一个状态枚举 + 信号推进：每收到一个回复，就跳到下一步。
//
//  升级流程：
//  Step1: 发升级请求(0x0010) → 收到"在Boot已就绪"
//  Step2: 发启动升级(0x0011, 文件大小) → 驱动器擦除Flash → 收到"擦除完成"
//  Step3: 循环发升级数据(0x0012) → 每帧≤128字节 → 每帧等确认
//  Step4: 发完成升级(0x0013) → 驱动器跳转App
// ============================================================
#ifndef FIRMWAREUPGRADESERVICE_H
#define FIRMWAREUPGRADESERVICE_H

#include <QObject>
#include <QByteArray>
#include <QSharedPointer>
#include "protocol/ProtocolFrame.h"
#include "services/SerialPortService.h"

// 升级流程状态枚举（对应 WPF 版 UpgradeState）
enum class UpgradeState {
    Idle,         // 空闲
    Requesting,   // 正在发送升级请求
    Erasing,      // 正在擦除 Flash
    Transferring, // 正在传输数据
    Finishing,    // 正在完成升级（跳转 App）
    Completed,    // 升级完成
    Failed        // 升级失败
};

class FirmwareUpgradeService : public QObject
{
    Q_OBJECT

public:
    explicit FirmwareUpgradeService(SerialPortService *serialPort, QObject *parent = nullptr);

    /// <summary>
    /// 开始固件升级流程
    /// </summary>
    /// <param name="firmwareData">固件二进制数据</param>
    void startUpgrade(const QByteArray &firmwareData);

    /// <summary>
    /// 取消升级
    /// </summary>
    void cancel();

    /// <summary>
    /// 查询 Bootloader 版本号（独立于升级流程）
    /// </summary>
    void queryVersion();

    /// <summary>
    /// 当前状态
    /// </summary>
    UpgradeState state() const { return m_state; }

signals:
    // 进度变化（0-100）
    void progressChanged(int percent, const QString &message);

    // 状态变化
    void stateChanged(UpgradeState state);

    // 日志
    void logMessage(const QString &msg);

    // 升级结束（true=成功）
    void finished(bool success);

    // 版本号查询结果（失败返回空串）
    void versionReceived(const QString &version);

    // 数据包发送信息（供界面 Model/View 表格展示）
    void packetSent(quint32 offset, int length);

    // 数据包状态更新（row=包序号, status=成功/重试）
    void packetStatusChanged(int row, const QString &status);

private slots:
    // 收到期望回复帧
    void onRequestCompleted(const ProtocolFrame &frame);

    // 请求失败（超时等）
    void onRequestFailed(int reason);

private:
    // ---- 四步流程的发送动作 ----
    void sendUpgradeRequest();
    void sendUpgradeStart();
    void sendUpgradeData();
    void sendJumpApp();

    // ---- 流程控制辅助 ----
    void retryRequest();       // 升级请求重试
    void retryDataPacket();    // 数据包重试
    void complete(bool success, const QString &message);  // 结束升级
    void reportProgress(int percent, const QString &message);
    void log(const QString &msg);

    // 串口服务引用（不拥有所有权）
    SerialPortService *m_serialPort;

    // 当前状态
    UpgradeState m_state = UpgradeState::Idle;

    // 固件数据
    QByteArray m_firmwareData;

    // 已发送字节偏移
    int m_offset = 0;

    // 当前数据包的大小（推进偏移需要）
    int m_currentChunkSize = 0;

    // 已发送包计数
    int m_packetIndex = 0;

    // 总包数
    int m_totalPackets = 0;

    // 当前请求的重试计数
    int m_retryCount = 0;

    // 用户是否取消
    bool m_cancelled = false;

    // ---- 协议超时/重试参数（与 WPF 版一致） ----
    static const int RESPONSE_TIMEOUT = 5000;   // 普通通讯超时（毫秒）
    static const int ERASE_TIMEOUT     = 30000; // Flash 擦除超时（擦除较慢）
    static const int MAX_RETRY         = 3;     // 最大重试次数
};

#endif // FIRMWAREUPGRADESERVICE_H
