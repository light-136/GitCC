/**
 * @file logmanager.cpp
 * @brief 日志管理器实现
 *
 * 实现要点（教学注释）：
 *   - 线程安全：QFile / QTextStream 都不是线程安全的，因此对文件与
 *     m_messageCount 的所有读写都放进 QMutex 临界区内；
 *   - 锁的生命周期：log() 里用一对花括号把"加锁 + 写盘"包成独立作用域，
 *     使锁在 emit 信号之前自动释放——信号槽可能很慢，甚至可能反向调用本对象，
 *     若持锁 emit 会造成性能问题乃至死锁；
 *   - 编码：Windows 下 QTextStream 默认可能使用本地 ANSI 编码（GBK），
 *     通过 QTextStream::setCodec("UTF-8") 显式指定 UTF-8，保证中文日志不乱码。
 */

#include "infrastructure/logmanager.h"

#include <QDateTime>
#include <QDir>
#include <QMetaType>
#include <QStandardPaths>
#include <QTextStream>

namespace datascope {
namespace infrastructure {

LogManager &LogManager::instance()
{
    // Meyer's Singleton：函数内静态局部对象。
    // C++11 起静态局部对象的初始化由编译器保证线程安全，无需额外加锁。
    static LogManager s_instance;
    return s_instance;
}

LogManager::LogManager(QObject *parent)
    : QObject(parent)
{
    // 把自定义枚举注册进 Qt 元类型系统：
    //   - 让 messageLogged 信号可以跨线程排队连接（QueuedConnection）；
    //   - 让 QSignalSpy 能把枚举参数存入 QVariant 并正确取回（配合 Q_DECLARE_METATYPE）。
    // 关键坑：Q_DECLARE_METATYPE(datascope::infrastructure::LogLevel) 内部用
    // 预处理字符串化注册的名字是"带命名空间"的 "datascope::infrastructure::LogLevel"，
    // 而 moc 解析信号声明 `void messageLogged(LogLevel, ...)` 时不会做 C++ 语义解析，
    // 生成的参数类型名是"不带命名空间"的 "LogLevel"。
    // 因此这里必须显式注册别名 "LogLevel"，否则 QSignalSpy 用 "LogLevel" 查询元类型
    // 会失败并警告 "Unable to handle parameter ... use qRegisterMetaType to register it"。
    qRegisterMetaType<LogLevel>("LogLevel");
}

void LogManager::init(const QString &logDir)
{
    QMutexLocker locker(&m_mutex);

    // 1) 解析日志目录：未指定则使用系统 AppData 目录
    //    对应 C# 的 Environment.SpecialFolder.ApplicationData 下的应用子目录
    QString dir = logDir;
    if (dir.isEmpty()) {
        dir = QStandardPaths::writableLocation(QStandardPaths::AppDataLocation);
    }

    // 2) 确保目录存在（mkpath 会递归创建所有缺失的父目录）
    if (!QDir().mkpath(dir)) {
        // 目录创建失败不中断进程；m_file 会保持未打开，log() 将跳过写盘
        qWarning("LogManager: 创建日志目录失败: %s", qPrintable(dir));
    }

    // 3) 生成按天滚动的文件名：datascope-YYYYMMDD.log
    const QString dateStamp = QDateTime::currentDateTime().toString(QStringLiteral("yyyyMMdd"));
    const QString filePath = QDir(dir).filePath(
        QStringLiteral("datascope-%1.log").arg(dateStamp));

    // 4) 若此前已 init 过，先关闭旧文件，避免句柄泄漏与文件被占用
    if (m_file.isOpen())
        m_file.close();

    // 5) 以"追加 + 文本"模式打开：
    //    Append → 追加写入、不覆盖历史；Text → Windows 下自动处理换行（\n → \r\n）
    m_file.setFileName(filePath);
    if (!m_file.open(QIODevice::Append | QIODevice::Text)) {
        qWarning("LogManager: 打开日志文件失败: %s", qPrintable(filePath));
    }

    // 6) 文件编码 UTF-8 设置在每次写入的 QTextStream 实例上（见 log()），
    //    保证 Windows 下中文日志不乱码、跨平台一致。
    //    注：QTextStream::setCodec 是成员函数，不能写成静态调用。

    // 7) 记录目录并重置计数：init 视为开启一段全新的日志会话
    m_logDir = dir;
    m_messageCount = 0;
}

void LogManager::log(LogLevel level, const QString &module, const QString &message)
{
    // 用独立作用域控制锁的生命周期：写盘完成后立即解锁，再进行信号广播
    {
        QMutexLocker locker(&m_mutex);

        // 文件未初始化或打开失败时，跳过写盘（但下方仍会广播信号）
        if (m_file.isOpen()) {
            // 每次在栈上创建 QTextStream 包裹同一个文件句柄。
            // 析构时会自动 flush，这里再显式 flush 一次，保证立即落盘。
            QTextStream stream(&m_file);
            stream.setCodec("UTF-8");   // 指定 UTF-8 编码：Windows 中文不乱码
            stream << formatLine(level, module, message) << '\n';
            stream.flush();

            ++m_messageCount;   // 记录"已成功写盘"的条数
        }
    } // 作用域结束：QMutexLocker 析构 → 自动解锁

    // 无论是否写盘成功都广播日志，便于 UI 日志面板实时显示
    emit messageLogged(level, module, message);
}

void LogManager::trace(const QString &module, const QString &message)
{
    log(LogLevel::Trace, module, message);
}

void LogManager::debug(const QString &module, const QString &message)
{
    log(LogLevel::Debug, module, message);
}

void LogManager::info(const QString &module, const QString &message)
{
    log(LogLevel::Info, module, message);
}

void LogManager::warn(const QString &module, const QString &message)
{
    log(LogLevel::Warn, module, message);
}

void LogManager::error(const QString &module, const QString &message)
{
    log(LogLevel::Error, module, message);
}

QString LogManager::logFilePath() const
{
    QMutexLocker locker(&m_mutex);   // mutable 互斥锁允许在 const 方法中加锁
    return m_file.fileName();        // 未 init 时 fileName() 为空串，符合契约
}

int LogManager::messageCount() const
{
    QMutexLocker locker(&m_mutex);
    return m_messageCount;
}

QString LogManager::formatLine(LogLevel level, const QString &module, const QString &message) const
{
    // 行格式：[yyyy-MM-dd HH:mm:ss.zzz] [级别] [模块] 消息
    const QString timestamp = QDateTime::currentDateTime()
                                  .toString(QStringLiteral("yyyy-MM-dd HH:mm:ss.zzz"));
    return QStringLiteral("[%1] [%2] [%3] %4")
        .arg(timestamp)
        .arg(levelName(level))
        .arg(module)
        .arg(message);
}

QString LogManager::levelName(LogLevel level)
{
    switch (level) {
    case LogLevel::Trace: return QStringLiteral("Trace");
    case LogLevel::Debug: return QStringLiteral("Debug");
    case LogLevel::Info:  return QStringLiteral("Info");
    case LogLevel::Warn:  return QStringLiteral("Warn");
    case LogLevel::Error: return QStringLiteral("Error");
    case LogLevel::Fatal: return QStringLiteral("Fatal");
    }
    // 防御性兜底：未来新增级别却忘记更新此函数时，返回明确标识而非空串
    return QStringLiteral("Unknown");
}

} // namespace infrastructure
} // namespace datascope
