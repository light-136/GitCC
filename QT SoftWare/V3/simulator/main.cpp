/**
 * @file main.cpp
 * @brief 模拟采集设备入口（V3）：命令行启动，供采集端 E2E 联调
 *
 * 开发思路：
 *   1. 用 QCoreApplication（无 GUI），命令行参数交给 QCommandLineParser 解析；
 *   2. 三个参数全部先"解析 + 校验"再构建设备：参数非法直接返回非零退出码，
 *      不让非法配置进入运行态；
 *   3. normal 模式 = 只发正常正弦波；fault 模式 = 开启异常注入并轮询全部五类异常。
 *
 * 用法：
 *   simulator                        # 默认：normal 模式，0.0.0.0:45678，50ms/帧
 *   simulator -p 41000               # 指定端口
 *   simulator -m fault               # 故障模式（轮询注入五类异常）
 *   simulator -i 20                  # 帧间隔 20ms
 *   simulator -p 45678 -m fault -i 50
 */

#include <QCoreApplication>
#include <QCommandLineOption>
#include <QCommandLineParser>
#include <QDebug>
#include <QString>
#include <QStringList>

#include "simulatordevice.h"
#include "version.h"

#include "domain/connectiondefaults.h"   // 默认端口单一常量源（与 UI/测试同源）

namespace {

/** @brief 默认帧间隔（毫秒，=20Hz；命令行 -i 的缺省值） */
constexpr int kDefaultIntervalMs = 50;

} // namespace

int main(int argc, char *argv[])
{
    QCoreApplication app(argc, argv);
    QCoreApplication::setApplicationName(QStringLiteral("DataScope Simulator"));
    // 版本号唯一来源：CMake configure_file 生成的 version.h（禁止手写版本字符串）
    QCoreApplication::setApplicationVersion(QStringLiteral(V3_VERSION_STRING));

    QCommandLineParser parser;
    parser.setApplicationDescription(
        QStringLiteral("DataScope Studio 模拟采集设备（V3）：TCP 周期广播协议数据帧，"
                       "支持故障注入，供采集端 E2E 联调与健壮性测试。"));
    parser.addHelpOption();
    parser.addVersionOption();

    // ---- 命令行选项定义 ----
    QCommandLineOption portOption(
        QStringList() << "p" << "port",
        QStringLiteral("监听端口（默认 %1）").arg(dscope::domain::kDefaultPort),
        "port", QString::number(dscope::domain::kDefaultPort));
    parser.addOption(portOption);

    QCommandLineOption modeOption(
        QStringList() << "m" << "mode",
        QStringLiteral("运行模式：normal（默认）/ fault（轮询注入五类异常）"),
        "mode", "normal");
    parser.addOption(modeOption);

    QCommandLineOption intervalOption(
        QStringList() << "i" << "interval",
        QStringLiteral("数据帧发送间隔（毫秒，默认 %1）").arg(kDefaultIntervalMs),
        "ms", QString::number(kDefaultIntervalMs));
    parser.addOption(intervalOption);

    parser.process(app);

    // ---- 参数校验：非法配置直接非零退出，不进入运行态 ----

    bool portOk = false;
    const quint16 port = static_cast<quint16>(parser.value(portOption).toUShort(&portOk));
    if (!portOk || port == 0) {
        qCritical().noquote() << "端口参数无效，请使用 -p <1-65535>";
        return 1;
    }

    bool intervalOk = false;
    const int interval = parser.value(intervalOption).toInt(&intervalOk);
    if (!intervalOk || interval <= 0) {
        qCritical().noquote() << "帧间隔参数无效，请使用 -i <正整数毫秒>";
        return 1;
    }

    const QString mode = parser.value(modeOption);
    if (mode != QLatin1String("normal") && mode != QLatin1String("fault")) {
        qCritical().noquote() << "模式参数无效，请使用 -m normal 或 -m fault";
        return 1;
    }

    // ---- 构建设备并应用运行配置 ----
    dscope::simulator::SimulatorDevice device;
    device.setFrameInterval(interval);

    const bool faultMode = (mode == QLatin1String("fault"));
    if (faultMode) {
        device.setFaultEnabled(true);
        device.setFaultCycling(true);   // 每帧轮询切换一种异常，覆盖全部五类
    }

    if (!device.start(port)) {
        qCritical().noquote() << QString("监听失败，端口可能被占用：%1").arg(port);
        return 1;
    }

    qInfo().noquote()
        << QString("模拟设备已启动：0.0.0.0:%1  模式=%2  帧间隔=%3ms  通道数=%4")
               .arg(device.port())
               .arg(mode)
               .arg(interval)
               .arg(device.channelCount());

    return app.exec();
}
