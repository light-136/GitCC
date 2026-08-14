// ============================================================
//  串口配置数据模型
//
//  【Qt知识点】结构体（struct）默认公有：
//  C++ 中 struct 默认成员公有，适合纯数据聚合；
//  对应 WPF 版 SerialPortConfig.cs（class 属性）。
//  默认值参照官方工具 ConfigInfo.ini：115200 / 8 / EVEN / 1
// ============================================================
#ifndef SERIALPORTCONFIG_H
#define SERIALPORTCONFIG_H

#include <QString>
#include <QSerialPort>

struct SerialPortConfig
{
    // COM 端口名称（如 COM1、COM3）
    QString portName;

    // 波特率（默认 115200）
    qint32 baudRate = 115200;

    // 数据位（默认 8 位）
    QSerialPort::DataBits dataBits = QSerialPort::Data8;

    // 停止位（默认 1 位）
    QSerialPort::StopBits stopBits = QSerialPort::OneStop;

    // 校验位（默认偶校验，与协议官方配置一致）
    QSerialPort::Parity parity = QSerialPort::EvenParity;

    // 读取超时（毫秒）
    int readTimeoutMs = 3000;

    // 写入超时（毫秒）
    int writeTimeoutMs = 3000;
};

#endif // SERIALPORTCONFIG_H
