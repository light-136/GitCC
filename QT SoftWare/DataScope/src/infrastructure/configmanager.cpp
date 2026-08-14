/**
 * @file configmanager.cpp
 * @brief 配置管理器实现（QSettings + INI 文件 + 线程安全）
 *
 * 教学点：
 *   1. QSettings 的"自动落盘"（auto-sync）：setValue 先写入内存缓存，
 *      到 sync() / 析构时才真正写文件 —— 类似 C# Settings.Save() 的延迟写盘思路；
 *   2. 懒加载：若调用方没先 init() 就 setValue，这里会按默认目录自动创建 QSettings；
 *   3. QMutexLocker RAII：构造时加锁、析构时解锁，配合函数内多条 return 也不丢锁。
 */

#include "infrastructure/configmanager.h"

#include <QDir>
#include <QStandardPaths>
#include <QMutexLocker>

namespace datascope {
namespace infrastructure {

namespace {
// 配置文件固定文件名（匿名命名空间 = 本文件私有常量，类似 C# 的 private const）
const char kConfigFileName[] = "datascope-config.ini";

// 把"配置目录 + 文件名"拼成完整路径；目录为空返回空串
QString buildConfigFilePath(const QString &configDir)
{
    return configDir.isEmpty() ? QString()
                               : QDir(configDir).filePath(QLatin1String(kConfigFileName));
}

// 解析最终配置目录：未指定则用系统 AppData；仍拿不到则回退到 当前目录/config
QString resolveConfigDir(const QString &explicitDir)
{
    QString dir = explicitDir;
    if (dir.isEmpty())
        dir = QStandardPaths::writableLocation(QStandardPaths::AppDataLocation);
    if (dir.isEmpty())
        dir = QDir::currentPath() + QStringLiteral("/config");
    QDir().mkpath(dir); // 自动创建多级目录，目录已存在时是空操作
    return dir;
}
} // namespace

ConfigManager &ConfigManager::instance()
{
    // Meyer's Singleton：C++11 起"魔法静态变量"在首次调用时才构造、且线程安全，
    // 对应 C# 中 Lazy<T> 或静态属性式单例。
    static ConfigManager s_instance;
    return s_instance;
}

ConfigManager::~ConfigManager()
{
    // 进程退出时释放内部 QSettings：其析构会自动把未落盘的写入 flush 到磁盘，
    // 既避免内存泄漏，也保证"最后的写入"尽量持久化。
    QMutexLocker locker(&m_mutex); // 退出时也可能有其他线程仍在读取，加锁更稳妥
    delete m_settings;
    m_settings = nullptr;
}

void ConfigManager::init(const QString &configDir)
{
    QMutexLocker locker(&m_mutex); // 防止并发 init 时重建 QSettings 的竞态

    // 教学点：Windows 上 QSettings 默认格式是 NativeFormat（写注册表），
    // 这里显式设为 IniFormat，并配合"完整文件路径"构造，保证落到指定 INI 文件。
    QSettings::setDefaultFormat(QSettings::IniFormat);

    m_configDir = resolveConfigDir(configDir);

    // 重建 QSettings：先释放旧实例（析构时自动把未落盘的写入 flush 到磁盘），
    // 再指向同一文件的新实例 —— 这正是"程序重启后重新加载配置"的模拟。
    delete m_settings;
    m_settings = new QSettings(buildConfigFilePath(m_configDir), QSettings::IniFormat);
}

void ConfigManager::setValue(const QString &key, const QVariant &value)
{
    QMutexLocker locker(&m_mutex);

    // 懒加载：调用方未显式 init() 时，按默认目录自动创建 QSettings，避免空指针
    if (!m_settings) {
        QSettings::setDefaultFormat(QSettings::IniFormat);
        m_configDir = resolveConfigDir(QString());
        m_settings = new QSettings(buildConfigFilePath(m_configDir), QSettings::IniFormat);
    }

    m_settings->setValue(key, value);
    // QSettings 自动落盘：写入先进内存缓存，析构 / sync() 时才写文件。
    // 教学：这是"延迟写盘"设计 —— 高频写入不逐个 IO；代价是进程被强杀时可能丢最近写入。
}

QVariant ConfigManager::value(const QString &key, const QVariant &defaultValue) const
{
    QMutexLocker locker(&m_mutex); // m_mutex 是 mutable，const 方法也能加锁

    if (!m_settings)
        return defaultValue; // 从未 init()：视为无任何键，安全返回默认值
    if (!m_settings->contains(key))
        return defaultValue; // 键不存在 → 返回默认值
    return m_settings->value(key, defaultValue);
}

bool ConfigManager::contains(const QString &key) const
{
    QMutexLocker locker(&m_mutex);
    return m_settings != nullptr && m_settings->contains(key);
}

void ConfigManager::remove(const QString &key)
{
    QMutexLocker locker(&m_mutex);
    if (m_settings)
        m_settings->remove(key);
}

void ConfigManager::sync()
{
    QMutexLocker locker(&m_mutex);
    if (m_settings)
        m_settings->sync(); // 把内存缓存强制写入磁盘（持久化保证）
}

int ConfigManager::intValue(const QString &key, int def) const
{
    return value(key, QVariant(def)).toInt();
}

bool ConfigManager::boolValue(const QString &key, bool def) const
{
    return value(key, QVariant(def)).toBool();
}

QString ConfigManager::stringValue(const QString &key, const QString &def) const
{
    return value(key, QVariant(def)).toString();
}

double ConfigManager::doubleValue(const QString &key, double def) const
{
    return value(key, QVariant(def)).toDouble();
}

QString ConfigManager::configFilePath() const
{
    QMutexLocker locker(&m_mutex);
    return buildConfigFilePath(m_configDir);
}

} // namespace infrastructure
} // namespace datascope
