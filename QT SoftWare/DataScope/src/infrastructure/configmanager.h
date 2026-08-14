/**
 * @file configmanager.h
 * @brief 配置管理器（P7：基于 QSettings 的 INI 全局配置存储）
 *
 * 对应 WPF/C# 里的什么？
 *   - 对应 Properties.Settings（用户级配置，自动持久化到磁盘）；
 *   - 也类似 app.config / .config 文件（读取写入键值配置）。
 *
 * 为什么用 INI 文件而不是注册表（Windows 教学重点）？
 *   - QSettings 默认格式 NativeFormat 在 Windows 上 = 写注册表（HKEY_CURRENT_USER）；
 *   - 注册表不透明、难备份、卸载残留、无法随程序分发、不能跨平台；
 *   - INI 是纯文本：用户可读可改、可复制分发、可纳入版本管理。
 *
 * 为什么加锁？
 *   - 本类为单例，被 UI 线程 / 采集线程等共享；而 QSettings 本身不是线程安全的；
 *   - 用 QMutex 把每次读/写/删除操作串行化，避免数据竞争（类似 C# 的 lock 语句）。
 */

#pragma once

#include <QString>
#include <QVariant>
#include <QSettings>
#include <QMutex>
#include <QtGlobal>

namespace datascope {
namespace infrastructure {

/**
 * @class ConfigManager
 * @brief 线程安全、类型安全的 INI 键值配置管理器（单例）
 *
 * 用法（教学）：
 *   1. 程序启动时调用 ConfigManager::instance().init();   // 指定目录或默认 AppData；
 *   2. 任意模块读写：instance().setValue("com/port", "COM3");
 *   3. 读取：instance().stringValue("com/port");
 */
class ConfigManager
{
public:
    /// @brief 单例访问入口（Meyer's Singleton：C++11 魔法静态变量，编译器保证线程安全）
    static ConfigManager &instance();

    /// @brief 初始化配置存储，可重复调用；重复调用 = 模拟"程序重启后重新加载配置"
    /// @param configDir 配置目录；为空则使用 QStandardPaths::AppDataLocation
    void init(const QString &configDir = QString());

    /// @brief 写入一个键值（QSettings 自动落盘：析构 / sync() 时才真正写文件）
    void setValue(const QString &key, const QVariant &value);

    /// @brief 读取键值；键不存在时返回 defaultValue
    QVariant value(const QString &key, const QVariant &defaultValue = QVariant()) const;

    /// @brief 键是否存在
    bool contains(const QString &key) const;

    /// @brief 删除一个键
    void remove(const QString &key);

    /// @brief 强制把内存中的改动写入磁盘（持久化保证）
    void sync();

    /// @brief 类型化读取：整数
    int intValue(const QString &key, int def = 0) const;
    /// @brief 类型化读取：布尔
    bool boolValue(const QString &key, bool def = false) const;
    /// @brief 类型化读取：字符串
    QString stringValue(const QString &key, const QString &def = QString()) const;
    /// @brief 类型化读取：双精度浮点
    double doubleValue(const QString &key, double def = 0.0) const;

    /// @brief 当前配置文件绝对路径（尚未 init 时可能为空字符串）
    QString configFilePath() const;

private:
    ConfigManager() = default;     // 私有构造：禁止外部 new（单例）
    ~ConfigManager();              // 析构释放内部 QSettings（退出时自动落盘，避免泄漏）
    Q_DISABLE_COPY(ConfigManager)  // 禁止拷贝 / 赋值（保持单例唯一性）

    mutable QMutex m_mutex;          // 读写互斥锁（mutable 使 const 方法也能加锁）
    QSettings *m_settings = nullptr; // 内部存储对象，负责 INI 文件读写
    QString m_configDir;             // 当前配置目录
};

} // namespace infrastructure
} // namespace datascope
