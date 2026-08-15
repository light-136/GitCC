/**
 * @file logger.cpp
 * @brief V3 基础设施层 —— 极薄日志实现
 *
 * ── 实现思路 ──
 * 核心只有一个全局消息处理器 messageHandler()，它被 qInstallMessageHandler 注册后，
 * Qt 的所有日志（qDebug/qInfo/qWarning/qCritical/qFatal）都会经它吐出。处理器把每条
 * 日志格式化成"时间戳 [级别] 消息"，追加写入日志文件，并同时写 stderr（保留调试时
 * 从控制台直接观察的能力）。
 *
 * ── 线程安全 ──
 * 采集线程与主线程都可能打日志（采集层 qWarning 在采集线程、UI 在主线程），文件句柄
 * 是共享的，故用 QMutex 串行化写操作，避免多线程同时 write 导致日志行交错。
 *
 * ── 为什么日志文件句柄用全局裸指针而非 QObject ──
 * 消息处理器是 C 风格回调，无事件循环依赖，用全局 QFile* + QMutex 最直白；进程退出
 * 时由操作系统回收，不必引入额外生命周期管理，符合"极薄"原则。
 */

#include "infra/logger.h"

#include <QDateTime>
#include <QDir>
#include <QFile>
#include <QMutex>
#include <QMutexLocker>
#include <QtGlobal>

#include <cstdio>

namespace dscope {
namespace infra {

namespace {

QFile  *g_logFile = nullptr;   ///< 日志文件句柄（进程内全局，install 时打开）
QMutex  g_logMutex;            ///< 写日志互斥锁（采集线程/主线程共享文件句柄）

/**
 * @brief 把 Qt 日志级别枚举翻译成可读级别串
 * @param type QtMsgType
 * @return 指向静态字符串的指针（勿释放）
 */
const char *levelName(QtMsgType type)
{
    switch (type) {
    case QtDebugMsg:    return "DEBUG";
    case QtInfoMsg:     return "INFO ";
    case QtWarningMsg:  return "WARN ";
    case QtCriticalMsg: return "CRIT ";
    case QtFatalMsg:    return "FATAL";
    }
    return "?????";
}

/**
 * @brief 全局消息处理器：格式化并落盘每条 Qt 日志
 * @param type 日志级别
 * @param ctx  日志上下文（文件/行/函数；极薄实现里未使用，但签名必须保留）
 * @param msg  已格式化好的日志正文
 *
 * 每行格式：`yyyy-MM-dd HH:mm:ss.zzz [级别] 正文`
 */
void messageHandler(QtMsgType type, const QMessageLogContext &ctx, const QString &msg)
{
    Q_UNUSED(ctx);   // 极薄实现不展开文件/行号，保留参数位以符合处理器签名

    const QString line = QStringLiteral("%1 [%2] %3\n")
        .arg(QDateTime::currentDateTime().toString(QStringLiteral("yyyy-MM-dd HH:mm:ss.zzz")))
        .arg(QString::fromLatin1(levelName(type)))
        .arg(msg);

    // 加锁写文件：采集线程与主线程并发打日志时，保证整行原子落盘、不交错
    QMutexLocker locker(&g_logMutex);
    if (g_logFile && g_logFile->isOpen()) {
        g_logFile->write(line.toUtf8());
        g_logFile->flush();   // 立即刷盘：诊断日志宁可慢一点，也要保证崩溃前不丢
    }

    // 同时写 stderr：从控制台启动时仍可实时观察（GUI 下无控制台则无副作用）
    std::fputs(line.toUtf8().constData(), stderr);
}

} // namespace

bool Logger::install(const QString &logDir)
{
    // 确保日志目录存在（不存在则递归创建）
    QDir().mkpath(logDir);

    // 以追加模式打开日志文件（进程重启不覆盖历史日志）
    g_logFile = new QFile(logDir + QStringLiteral("/datascope.log"));
    if (!g_logFile->open(QIODevice::WriteOnly | QIODevice::Append)) {
        // 日志文件打不开（无权限/磁盘满）：不致命，程序继续运行但日志静默
        delete g_logFile;
        g_logFile = nullptr;
        return false;
    }

    // 安装全局处理器：此后所有 Qt 日志都经 messageHandler 落盘
    qInstallMessageHandler(messageHandler);
    return true;
}

} // namespace infra
} // namespace dscope
