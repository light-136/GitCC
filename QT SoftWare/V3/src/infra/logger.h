/**
 * @file logger.h
 * @brief V3 基础设施层 —— 极薄日志（消息处理器重定向）
 *
 * ── 为什么需要 Logger ──
 * DataScope 是 GUI 程序（CMake 设了 WIN32_EXECUTABLE，不弹控制台），Qt 默认把
 * qDebug/qWarning/qCritical 写向 stderr，在无控制台的 GUI 里这些日志会**静默丢失**。
 * 采集层遇到连接错误、CRC 坏帧时靠 qWarning 诊断，看不到日志就无从排查。因此需要
 * 一个极薄的日志重定向：把 Qt 日志消息处理器接到文件，让运行期诊断有迹可循。
 *
 * ── 为什么"极薄"──
 * 权威规格明确"infra 只保留极薄 Config/Logger"，所以这里只做一件事：qInstallMessageHandler
 * 把全部 Qt 日志（含级别 + 时间戳）追加写到日志文件，不做日志分级过滤、滚动切分、
 * 异步队列等重框架——那些对学习项目是负担而非价值。simulator 是控制台程序，qInfo
 * 已直接可见，故 Logger 仅 DataScope 主程序使用。
 *
 * ── WPF 对照 ──
 *   Logger::install()  ↔  C# 里在 App 启动时订阅 AppDomain.UnhandledException /
 *                         Trace.Listeners.Add(FileTraceListener)，统一把日志落盘。
 */

#pragma once

#include <QString>

namespace dscope {
namespace infra {

/**
 * @class Logger
 * @brief 极薄日志：安装 Qt 全局消息处理器，把日志重定向到文件（全静态，不可实例化）
 */
class Logger
{
public:
    /**
     * @brief 安装消息处理器：此后所有 qDebug/qInfo/qWarning/qCritical 都会追加写入日志文件
     * @param logDir 日志目录（不存在则自动创建）；日志文件固定为 logDir/datascope.log
     * @return 是否成功安装（日志文件打不开时返回 false，但不影响程序继续运行）
     *
     * @note 幂等但非线程安全：应在 main() 早期（任何线程启动前）调用一次。
     *       日志文件句柄进程内全局持有，随进程退出自动释放，无需显式 uninstall。
     */
    static bool install(const QString &logDir);
};

} // namespace infra
} // namespace dscope
