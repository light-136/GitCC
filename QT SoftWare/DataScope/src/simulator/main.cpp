/**
 * @file main.cpp
 * @brief 模拟采集设备入口（V2：运行模式 + 异常注入）
 *
 * V1 版本是纯过程式正弦波模拟器；V2 改为基于 SimulatorDevice 类，
 * 支持通过命令行注入真实异常，供主机联调健壮性。
 *
 * 用法：
 *   simulator                          # 默认：正常模式，0.0.0.0:40001
 *   simulator -p 41000                 # 指定端口
 *   simulator --mode alarm             # 报警模式（周期超限/恢复）
 *   simulator --mode stress            # 压力模式（10ms 高频 + 粘包）
 *   simulator --mode busy              # 设备忙（周期内静默不输出数据）
 *   simulator --mode error --fault crc # 注入 CRC 错误帧
 *   simulator --mode error --fault fragment  # 分包（一帧拆两段）
 *   simulator --mode error --fault sticky    # 粘包（两帧合并）
 *   simulator --mode error --fault illegal   # 非法帧（长度字段被破坏）
 *   simulator --mode error --fault delay     # 帧延迟（周期丢帧）
 *   simulator --mode error --fault surge     # 数据突变（通道跳超量程）
 *   simulator --mode error --fault disconnect # 断线（主动断开客户端）
 *   simulator --frame-interval 10       # 帧间隔 10ms
 *
 * 教学点：命令行参数用 QCommandLineParser（对应 C# 手写参数解析或
 * System.CommandLine），把"设备行为"做成可配置而非写死 —— 这是把
 * 模拟器变成"测试基础设施"的关键一步。
 */

#include <QCoreApplication>
#include <QCommandLineParser>
#include <QCommandLineOption>
#include <QStringList>
#include <QDebug>

#include "simulator/simulatordevice.h"

namespace {

/**
 * @brief 字符串 → 异常类型（命令行 --fault 参数映射）
 * @return 映射后的异常类型；未知参数返回 FaultType::None
 */
datascope::simulator::FaultType faultFromString(const QString &text)
{
    using datascope::simulator::FaultType;
    if (text == QStringLiteral("sticky"))    return FaultType::Sticky;
    if (text == QStringLiteral("fragment"))  return FaultType::Fragment;
    if (text == QStringLiteral("crc"))       return FaultType::CrcError;
    if (text == QStringLiteral("illegal"))   return FaultType::IllegalFrame;
    if (text == QStringLiteral("delay"))     return FaultType::Delay;
    if (text == QStringLiteral("surge"))     return FaultType::Surge;
    if (text == QStringLiteral("disconnect")) return FaultType::Disconnect;
    return FaultType::None;
}

/**
 * @brief 字符串 → 运行模式（--mode 参数映射）
 * @return 映射后的模式；未知参数返回 Normal
 */
datascope::simulator::SimulatorMode modeFromString(const QString &text)
{
    using datascope::simulator::SimulatorMode;
    if (text == QStringLiteral("alarm"))  return SimulatorMode::Alarm;
    if (text == QStringLiteral("stress")) return SimulatorMode::Stress;
    if (text == QStringLiteral("busy"))   return SimulatorMode::Busy;
    if (text == QStringLiteral("error"))  return SimulatorMode::Error;
    return SimulatorMode::Normal;
}

} // namespace

int main(int argc, char *argv[])
{
    QCoreApplication app(argc, argv);
    QCoreApplication::setApplicationName("DataScopeSimulator");
    QCoreApplication::setApplicationVersion("2.0.0");

    // ---- 命令行参数 ----
    QCommandLineParser parser;
    parser.setApplicationDescription(
        "DataScope Studio 模拟采集设备（V2）：支持运行模式与异常注入，"
        "用于主机联调与健壮性测试。");
    parser.addHelpOption();
    parser.addVersionOption();

    QCommandLineOption portOption(
        QStringList() << "p" << "port", "监听端口（默认 40001）", "port", "40001");
    parser.addOption(portOption);

    QCommandLineOption modeOption(
        QStringList() << "m" << "mode",
        "运行模式：normal（默认）/ alarm / stress / busy / error", "mode", "normal");
    parser.addOption(modeOption);

    QCommandLineOption faultOption(
        QStringList() << "f" << "fault",
        "异常类型（--mode error 时生效）：crc / sticky / fragment / illegal / delay / surge / disconnect",
        "fault", "none");
    parser.addOption(faultOption);

    QCommandLineOption intervalOption(
        QStringList() << "i" << "frame-interval",
        "数据帧发送间隔（毫秒，默认 50）", "ms", "50");
    parser.addOption(intervalOption);

    parser.process(app);

    // ---- 参数解析与校验 ----
    bool portOk = false;
    const quint16 port = static_cast<quint16>(parser.value(portOption).toUShort(&portOk));
    if (!portOk || port == 0) {
        qCritical().noquote() << "端口参数无效，请使用 --port <1-65535>";
        return 1;
    }

    bool intervalOk = false;
    const int interval = parser.value(intervalOption).toInt(&intervalOk);
    if (!intervalOk || interval <= 0) {
        qCritical().noquote() << "帧间隔参数无效，请使用 --frame-interval <正数>";
        return 1;
    }

    // ---- 构建设备并应用模式/异常 ----
    datascope::simulator::SimulatorDevice device;
    const datascope::simulator::SimulatorMode mode = modeFromString(parser.value(modeOption));
    device.setMode(mode);
    device.setFrameInterval(interval);

    if (mode == datascope::simulator::SimulatorMode::Error) {
        const datascope::simulator::FaultType fault = faultFromString(parser.value(faultOption));
        datascope::simulator::FaultConfig cfg;
        cfg.type = fault;
        device.setFaultConfig(cfg);
        qInfo().noquote() << "异常注入已配置:" << parser.value(faultOption);
    }

    if (!device.start(port)) {
        qCritical().noquote() << "监听失败，端口可能被占用:" << port;
        return 1;
    }

    qInfo().noquote()
        << QString("模拟设备已启动（V2）：0.0.0.0:%1  模式=%2  帧间隔=%3ms")
               .arg(device.port())
               .arg(parser.value(modeOption))
               .arg(interval);

    return app.exec();
}
