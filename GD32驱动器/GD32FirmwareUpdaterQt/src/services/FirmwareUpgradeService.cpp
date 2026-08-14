// ============================================================
//  固件升级引擎实现
//
//  【Qt知识点】本文件展示 Qt 事件驱动状态机的完整写法：
//  没有阻塞等待，只有"发请求 → 收到信号 → 推进状态"的回环。
//  核心在 onRequestCompleted()：根据当前状态分支处理回复。
// ============================================================
#include "FirmwareUpgradeService.h"
#include "protocol/CommandDefinitions.h"
#include <QDateTime>
#include <QTimer>
#include <QDebug>

FirmwareUpgradeService::FirmwareUpgradeService(SerialPortService *serialPort, QObject *parent)
    : QObject(parent)
    , m_serialPort(serialPort)
{
    // 在构造函数中一次性连接串口服务的回复信号
    // 【Qt知识点】信号-槽连接跨线程安全性：
    // 这里默认 AutoConnection —— 同线程走直接调用，安全高效。
    connect(m_serialPort, &SerialPortService::requestCompleted,
            this, &FirmwareUpgradeService::onRequestCompleted);
    connect(m_serialPort, &SerialPortService::requestFailed,
            this, &FirmwareUpgradeService::onRequestFailed);
}

// ==================== 升级主流程 ====================
void FirmwareUpgradeService::startUpgrade(const QByteArray &firmwareData)
{
    m_firmwareData = firmwareData;
    m_offset = 0;
    m_packetIndex = 0;
    m_retryCount = 0;
    m_cancelled = false;

    // 计算总包数（向上取整）
    m_totalPackets = (m_firmwareData.size() + FrameConstants::MAX_DATA_PAYLOAD - 1)
                     / FrameConstants::MAX_DATA_PAYLOAD;

    // ---- Step1: 升级请求 ----
    m_state = UpgradeState::Requesting;
    emit stateChanged(m_state);
    reportProgress(0, QStringLiteral("正在发送升级请求..."));
    log(QStringLiteral("Step 1/4: 发送升级请求 (CMD=0x0010)"));
    sendUpgradeRequest();
}

// ==================== 四步发送动作 ====================

void FirmwareUpgradeService::sendUpgradeRequest()
{
    QByteArray frame = FrameBuilder::buildUpgradeRequest(UpgradeRequestType::REQUEST_UPGRADE);
    m_serialPort->sendRequest(frame, CommandCode::UPGRADE_REQ, RESPONSE_TIMEOUT);
}

void FirmwareUpgradeService::sendUpgradeStart()
{
    QByteArray frame = FrameBuilder::buildUpgradeStart(static_cast<quint32>(m_firmwareData.size()));
    // 擦除 Flash 耗时较长，使用更长超时
    m_serialPort->sendRequest(frame, CommandCode::UPGRADE_START, ERASE_TIMEOUT);
}

void FirmwareUpgradeService::sendUpgradeData()
{
    // 计算本帧数据大小（最后一帧可能不足 128 字节）
    int remaining = m_firmwareData.size() - m_offset;
    m_currentChunkSize = qMin(FrameConstants::MAX_DATA_PAYLOAD, remaining);
    QByteArray chunk = m_firmwareData.mid(m_offset, m_currentChunkSize);

    // 上报数据包信息（供界面表格显示）
    emit packetSent(static_cast<quint32>(m_offset), m_currentChunkSize);

    QByteArray frame = FrameBuilder::buildUpgradeData(static_cast<quint32>(m_offset), chunk);
    m_serialPort->sendRequest(frame, CommandCode::UPGRADE_DATA, RESPONSE_TIMEOUT);
}

void FirmwareUpgradeService::sendJumpApp()
{
    QByteArray frame = FrameBuilder::buildJumpApp();
    m_serialPort->sendRequest(frame, CommandCode::JUMP_APP, RESPONSE_TIMEOUT);
}

// ==================== 回复处理（状态机核心） ====================

void FirmwareUpgradeService::onRequestCompleted(const ProtocolFrame &frame)
{
    switch (m_state)
    {
    // ---------- Step1: 处理升级请求回复 ----------
    case UpgradeState::Requesting:
    {
        if (frame.data.size() < 1)
        {
            retryRequest();
            return;
        }
        quint8 status = static_cast<quint8>(frame.data.at(0));
        switch (status)
        {
        case UpgradeRequestResponse::IN_BOOT_READY:
        case UpgradeRequestResponse::IN_BOOT_STATUS_ONLY:
            log(QStringLiteral("设备已在 Boot 模式，具备升级条件"));
            // ---- 进入 Step2 ----
            m_state = UpgradeState::Erasing;
            emit stateChanged(m_state);
            reportProgress(5, QStringLiteral("正在擦除 Flash，请等待..."));
            log(QStringLiteral("Step 2/4: 启动升级 (CMD=0x0011)，固件大小: %1 字节")
                    .arg(m_firmwareData.size()));
            sendUpgradeStart();
            break;

        case UpgradeRequestResponse::IN_APP_JUMPING:
            log(QStringLiteral("设备正在从 App 跳转到 Boot，等待 2 秒后重试..."));
            // 跳转需要时间，延迟 2 秒后重发升级请求
            QTimer::singleShot(2000, this, [this]() {
                if (!m_cancelled && m_state == UpgradeState::Requesting)
                    sendUpgradeRequest();
            });
            break;

        case UpgradeRequestResponse::IN_APP_STATUS_ONLY:
            log(QStringLiteral("设备在 App 模式，仅回复状态。重新发送升级请求..."));
            retryRequest();
            break;

        default:
            log(QStringLiteral("未知的回复状态: 0x%1").arg(status, 2, 16, QLatin1Char('0')));
            retryRequest();
            break;
        }
        break;
    }

    // ---------- Step2: 处理启动升级回复 ----------
    case UpgradeState::Erasing:
    {
        if (frame.data.size() < 1)
        {
            complete(false, QStringLiteral("Flash 擦除失败：回复格式异常"));
            return;
        }
        if (static_cast<quint8>(frame.data.at(0)) == ResponseStatus::SUCCESS)
        {
            log(QStringLiteral("Flash 擦除完成，准备接收固件数据"));
            // ---- 进入 Step3 ----
            m_state = UpgradeState::Transferring;
            emit stateChanged(m_state);
            m_retryCount = 0;
            reportProgress(10, QStringLiteral("正在传输固件数据..."));
            log(QStringLiteral("Step 3/4: 发送升级数据 (CMD=0x0012)，共 %1 字节，%2 包")
                    .arg(m_firmwareData.size()).arg(m_totalPackets));
            sendUpgradeData();
        }
        else
        {
            complete(false, QStringLiteral("Flash 擦除失败，错误码: 0x%1")
                              .arg(static_cast<quint8>(frame.data.at(0)), 2, 16, QLatin1Char('0')));
        }
        break;
    }

    // ---------- Step3: 处理数据包回复 ----------
    case UpgradeState::Transferring:
    {
        if (frame.data.size() < 1)
        {
            retryDataPacket();
            return;
        }
        if (static_cast<quint8>(frame.data.at(0)) == ResponseStatus::SUCCESS)
        {
            // 本包成功
            m_retryCount = 0;
            emit packetStatusChanged(m_packetIndex, QStringLiteral("成功"));

            // 推进偏移
            m_offset += m_currentChunkSize;
            m_packetIndex++;

            if (m_offset >= m_firmwareData.size())
            {
                // 全部发送完毕，进入 Step4
                m_state = UpgradeState::Finishing;
                emit stateChanged(m_state);
                reportProgress(95, QStringLiteral("正在完成升级，跳转 App..."));
                log(QStringLiteral("Step 4/4: 发送完成升级指令 (CMD=0x0013)"));
                sendJumpApp();
            }
            else
            {
                // 继续发送下一包
                int percent = 10 + static_cast<int>(85.0 * m_offset / m_firmwareData.size());
                reportProgress(percent, QStringLiteral("正在传输: %1/%2 字节 (%3/%4 包)")
                                           .arg(m_offset).arg(m_firmwareData.size())
                                           .arg(m_packetIndex).arg(m_totalPackets));
                sendUpgradeData();
            }
        }
        else
        {
            log(QStringLiteral("数据包 %1/%2 写入失败，重试中...")
                    .arg(m_packetIndex + 1).arg(m_totalPackets));
            emit packetStatusChanged(m_packetIndex, QStringLiteral("失败重试"));
            retryDataPacket();
        }
        break;
    }

    // ---------- Step4: 处理完成升级回复 ----------
    case UpgradeState::Finishing:
    {
        if (frame.data.size() < 1)
        {
            complete(false, QStringLiteral("跳转 App 失败：回复格式异常"));
            return;
        }
        if (static_cast<quint8>(frame.data.at(0)) == ResponseStatus::SUCCESS)
        {
            complete(true, QStringLiteral("固件升级成功！"));
        }
        else
        {
            complete(false, QStringLiteral("跳转 App 失败，错误码: 0x%1")
                               .arg(static_cast<quint8>(frame.data.at(0)), 2, 16, QLatin1Char('0')));
        }
        break;
    }

    default:
        // Idle / Completed / Failed 状态下的回复忽略
        break;
    }
}

void FirmwareUpgradeService::onRequestFailed(int reason)
{
    Q_UNUSED(reason)

    switch (m_state)
    {
    case UpgradeState::Requesting:
        retryRequest();
        break;

    case UpgradeState::Erasing:
        complete(false, QStringLiteral("启动升级超时（Flash 擦除可能需要较长时间）"));
        break;

    case UpgradeState::Transferring:
        log(QStringLiteral("数据包 %1/%2 超时（重试中）")
                .arg(m_packetIndex + 1).arg(m_totalPackets));
        emit packetStatusChanged(m_packetIndex, QStringLiteral("超时重试"));
        retryDataPacket();
        break;

    case UpgradeState::Finishing:
        complete(false, QStringLiteral("完成升级指令超时"));
        break;

    default:
        break;
    }
}

// ==================== 重试与结束 ====================

void FirmwareUpgradeService::retryRequest()
{
    m_retryCount++;
    if (m_retryCount >= MAX_RETRY)
    {
        complete(false, QStringLiteral("升级请求失败：设备未响应或不具备升级条件"));
        return;
    }
    log(QStringLiteral("升级请求超时（第 %1 次）").arg(m_retryCount));
    sendUpgradeRequest();
}

void FirmwareUpgradeService::retryDataPacket()
{
    m_retryCount++;
    if (m_retryCount >= MAX_RETRY)
    {
        complete(false, QStringLiteral("数据包发送失败（已重试 %1 次）").arg(MAX_RETRY));
        return;
    }
    log(QStringLiteral("数据包 %1/%2 重试中...").arg(m_packetIndex + 1).arg(m_totalPackets));
    sendUpgradeData();
}

void FirmwareUpgradeService::complete(bool success, const QString &message)
{
    m_state = success ? UpgradeState::Completed : UpgradeState::Failed;
    emit stateChanged(m_state);
    reportProgress(success ? 100 : 0, message);
    log(success ? QStringLiteral("固件升级完成！") : message);
    emit finished(success);
}

// ==================== 取消 ====================
void FirmwareUpgradeService::cancel()
{
    if (m_state == UpgradeState::Idle || m_state == UpgradeState::Completed
        || m_state == UpgradeState::Failed)
        return;

    m_cancelled = true;
    complete(false, QStringLiteral("升级已取消"));
}

// ==================== 版本查询（独立一次性请求） ====================
void FirmwareUpgradeService::queryVersion()
{
    log(QStringLiteral("查询 Bootloader 版本号 (CMD=0x0014)"));

    // 【Qt知识点】一次性连接（connectOnce）：
    // Qt 5.12 没有 Qt 6 的 QObject::connectOnce，因此手动管理：
    // 用 shared_ptr 保存连接句柄，在 lambda 内使用完即 disconnect。
    auto conn1 = QSharedPointer<QMetaObject::Connection>::create();
    auto conn2 = QSharedPointer<QMetaObject::Connection>::create();

    // 回复处理
    *conn1 = connect(m_serialPort, &SerialPortService::requestCompleted,
                     this, [this, conn1, conn2](const ProtocolFrame &frame) {
        disconnect(*conn1);
        disconnect(*conn2);
        if (frame.cmd == CommandCode::VER_REQ && frame.data.size() >= 5)
        {
            // 版本号由 5 个字节组成
            QStringList parts;
            for (int i = 0; i < 5; ++i)
                parts << QString::number(static_cast<quint8>(frame.data.at(i)));
            QString version = parts.join(QStringLiteral("."));
            log(QStringLiteral("Bootloader 版本号: %1").arg(version));
            emit versionReceived(version);
        }
        else
        {
            log(QStringLiteral("版本号回复格式异常"));
            emit versionReceived(QString());
        }
    });

    // 失败处理
    *conn2 = connect(m_serialPort, &SerialPortService::requestFailed,
                     this, [this, conn1, conn2](int) {
        disconnect(*conn1);
        disconnect(*conn2);
        log(QStringLiteral("版本号查询超时"));
        emit versionReceived(QString());
    });

    QByteArray frame = FrameBuilder::buildVersionRequest();
    m_serialPort->sendRequest(frame, CommandCode::VER_REQ, RESPONSE_TIMEOUT);
}

// ==================== 辅助 ====================

void FirmwareUpgradeService::reportProgress(int percent, const QString &message)
{
    emit progressChanged(percent, message);
}

void FirmwareUpgradeService::log(const QString &msg)
{
    emit logMessage(QStringLiteral("[%1] %2")
                        .arg(QDateTime::currentDateTime().toString("HH:mm:ss.zzz"))
                        .arg(msg));
}
