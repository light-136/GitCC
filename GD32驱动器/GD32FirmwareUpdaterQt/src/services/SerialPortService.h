// ============================================================
//  串口通讯服务
//
//  【Qt知识点】QObject + 信号槽 —— Qt 通讯类的标准形态：
//  - 继承 QObject，配合 Q_OBJECT 宏启用元对象系统
//  - 对外暴露"信号"，对内用"槽"响应 Qt 事件（串口就绪、定时器超时）
//  - 对比 WPF：本类 ≈ SerialPortService.cs + DataReceived 事件
//
//  设计思路（单请求-单响应模式）：
//  1. 调用方 sendRequest() 发送帧，登记"期望的回复 CMD"
//  2. 串口数据就绪 → 缓冲 → 拆帧 → 匹配 CMD → 发 requestCompleted
//  3. 超时定时器到点 → 发 requestFailed
//  （替代 C# 版 SendAndWaitResponseAsync 的 await 阻塞，改事件驱动）
// ============================================================
#ifndef SERIALPORTSERVICE_H
#define SERIALPORTSERVICE_H

#include <QObject>
#include <QSerialPort>
#include <QTimer>
#include <QByteArray>
#include "protocol/ProtocolFrame.h"
#include "models/SerialPortConfig.h"

class SerialPortService : public QObject
{
    Q_OBJECT

public:
    explicit SerialPortService(QObject *parent = nullptr);
    ~SerialPortService() override;

    // 请求失败原因码
    enum FailReason {
        ERR_NOT_OPEN = 0,    // 串口未打开
        ERR_TIMEOUT  = 1,    // 等待回复超时
        ERR_MISMATCH = 2     // 回复帧 CMD 与期望不一致
    };

    /// <summary>
    /// 获取系统中所有可用 COM 端口（类似 C# SerialPort.GetPortNames）
    /// </summary>
    static QStringList availablePorts();

    /// <summary>
    /// 当前串口是否已连接
    /// </summary>
    bool isConnected() const;

    /// <summary>
    /// 打开串口
    /// </summary>
    bool open(const SerialPortConfig &config);

    /// <summary>
    /// 关闭串口
    /// </summary>
    void close();

    /// <summary>
    /// 发送协议帧并登记期望回复（非阻塞，结果通过信号返回）
    /// </summary>
    /// <param name="frame">待发送的帧字节数据</param>
    /// <param name="expectedCmd">期望回复的指令码</param>
    /// <param name="timeoutMs">超时时间（毫秒）</param>
    void sendRequest(const QByteArray &frame, quint16 expectedCmd, int timeoutMs);

signals:
    /// <summary>
    /// 收到并成功解析出期望的回复帧
    /// </summary>
    void requestCompleted(const ProtocolFrame &frame);

    /// <summary>
    /// 请求失败（超时 / 未打开 / 帧不匹配）
    /// </summary>
    /// <param name="reason">失败原因码，见 FailReason</param>
    void requestFailed(int reason);

    /// <summary>
    /// 日志输出（交给界面显示）
    /// </summary>
    void logMessage(const QString &msg);

    /// <summary>
    /// 串口连接状态变化
    /// </summary>
    void connectionChanged(bool connected);

private slots:
    // 串口有数据到达时的槽
    void onReadyRead();

    // 等待回复超时的槽
    void onTimeout();

private:
    // 尝试从接收缓冲区解析完整帧（处理粘包/分包）
    void tryParseFrame();

    // 输出日志（统一加时间戳）
    void log(const QString &msg);

    // 底层串口对象
    QSerialPort *m_serialPort = nullptr;

    // 接收缓冲区（处理不完整帧 / 多帧粘连）
    QByteArray m_receiveBuffer;

    // 当前正在等待回复的期望 CMD（0 表示无等待请求）
    quint16 m_expectedCmd = 0;

    // 是否有请求在等待回复
    bool m_requestPending = false;

    // 等待回复的超时定时器
    QTimer m_timeoutTimer;
};

#endif // SERIALPORTSERVICE_H
