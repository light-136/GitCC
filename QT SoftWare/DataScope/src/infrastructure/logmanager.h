/**
 * @file logmanager.h
 * @brief 日志管理器（P6：基础设施层 LogManager）
 *
 * 设计定位（对应 WPF / C# 概念）：
 *   - 相当于 NLog 的全局静态日志器，或 System.Diagnostics.TraceSource；
 *   - 全局唯一单例，任何模块通过 LogManager::instance().info(...) 即可写入统一日志；
 *   - 线程安全：内部用 QMutex 保护 QFile（QFile/QTextStream 本身非线程安全），
 *     多线程采集场景下各线程同时写日志也不会互相交错；
 *   - 观察者模式：messageLogged 信号让 UI（如主窗口日志面板）可实时订阅日志流，
 *     对应 C# 的事件/回调机制。
 *
 * 单例模式说明（Meyer's Singleton）：
 *   - `static LogManager &instance()` 返回函数内静态局部对象；
 *   - C++11 起函数内静态局部变量的初始化由编译器保证线程安全，无需额外加锁；
 *   - 对应 C# 的 `public static Lazy<LogManager>` 懒加载单例。
 */

#pragma once

#include <QObject>
#include <QString>
#include <QMutex>
#include <QFile>
#include <QMetaType>

namespace datascope {
namespace infrastructure {

/**
 * @brief 日志级别枚举（数值从 0 递增）
 *
 * 数值大小表示严重程度：Trace 最细粒度、Fatal 最严重。
 * 预留为后续"级别过滤"功能使用（例如只记录 Warn 及以上）。
 */
enum class LogLevel { Trace = 0, Debug, Info, Warn, Error, Fatal };

/**
 * @class LogManager
 * @brief 全局单例日志管理器：负责日志目录/文件的初始化与线程安全写盘
 *
 * 使用方法（三步走）：
 *   1. 程序启动时调用 LogManager::instance().init(logDir); 指定日志目录；
 *   2. 任意位置调用 LogManager::instance().info(module, msg); 写日志；
 *   3. UI 层连接 messageLogged 信号即可实时展示日志流。
 */
class LogManager : public QObject
{
    Q_OBJECT

public:
    // ---------- 单例与生命周期 ----------

    /**
     * @brief 获取全局唯一实例
     * @return 单例引用（Meyer's Singleton，线程安全）
     */
    static LogManager &instance();

    /**
     * @brief 初始化日志目录与日志文件（幂等，可重复调用以切换目录/重开会话）
     * @param logDir 日志目录绝对路径；为空时使用系统 AppData 目录
     *               （QStandardPaths::AppDataLocation）
     * @note 文件名固定为 datascope-YYYYMMDD.log，按天滚动；
     *       文件以"追加"模式打开，历史日志不会被覆盖；
     *       重复调用会先关闭旧文件再打开新文件，并把 messageCount 重置为 0。
     */
    void init(const QString &logDir = QString());

    // ---------- 写日志 ----------

    /**
     * @brief 写一条日志（加锁 → 写一行 → flush → 解锁 → 发信号）
     * @param level   日志级别
     * @param module  来源模块名（如 "ProtocolEngine"、"MainWindow"）
     * @param message 日志正文
     */
    void log(LogLevel level, const QString &module, const QString &message);

    /**
     * @brief 便捷方法：Trace 级日志
     * @param module  来源模块名
     * @param message 日志正文
     */
    void trace(const QString &module, const QString &message);

    /**
     * @brief 便捷方法：Debug 级日志
     * @param module  来源模块名
     * @param message 日志正文
     */
    void debug(const QString &module, const QString &message);

    /**
     * @brief 便捷方法：Info 级日志
     * @param module  来源模块名
     * @param message 日志正文
     */
    void info(const QString &module, const QString &message);

    /**
     * @brief 便捷方法：Warn 级日志
     * @param module  来源模块名
     * @param message 日志正文
     */
    void warn(const QString &module, const QString &message);

    /**
     * @brief 便捷方法：Error 级日志
     * @param module  来源模块名
     * @param message 日志正文
     */
    void error(const QString &module, const QString &message);

    // ---------- 状态查询 ----------

    /**
     * @brief 获取当前日志文件绝对路径
     * @return 日志文件绝对路径；未 init 时返回空串
     */
    QString logFilePath() const;

    /**
     * @brief 获取已成功写盘的日志条数
     * @return 已写盘条数（供测试断言 / 状态查询）
     */
    int messageCount() const;

signals:
    /**
     * @brief 每条日志写入后发出（无论是否成功落盘都会通知）
     * @param level   日志级别
     * @param module  来源模块
     * @param message 日志正文
     */
    void messageLogged(LogLevel level, const QString &module, const QString &message);

private:
    explicit LogManager(QObject *parent = nullptr);
    Q_DISABLE_COPY(LogManager)

    /**
     * @brief 把日志参数格式化成一行文本
     * @param level   日志级别
     * @param module  来源模块名
     * @param message 日志正文
     * @return "[yyyy-MM-dd HH:mm:ss.zzz] [级别] [模块] 消息"
     */
    QString formatLine(LogLevel level, const QString &module, const QString &message) const;

    /**
     * @brief 日志级别 → 显示用字符串
     * @param level 日志级别
     * @return 级别名（如 "Info"、"Error"）
     */
    static QString levelName(LogLevel level);

    // mutable：允许 const 成员函数（logFilePath / messageCount）内部加锁读取状态
    mutable QMutex m_mutex;      // 线程安全锁（QFile/QTextStream 非线程安全）
    QFile m_file;                // 日志文件句柄
    int m_messageCount = 0;      // 已写盘条数（供测试断言）
    QString m_logDir;            // 日志目录
};

} // namespace infrastructure
} // namespace datascope

// 把自定义枚举注册进 Qt 元类型系统：
//   - 使 messageLogged 信号可被 QSignalSpy 记录参数并正确取回（单测依赖）；
//   - 使该信号可用于跨线程 QueuedConnection 排队连接。
// 注意：Q_DECLARE_METATYPE 需放在类型可见的作用域，此处使用全限定名置于全局作用域。
Q_DECLARE_METATYPE(datascope::infrastructure::LogLevel)
