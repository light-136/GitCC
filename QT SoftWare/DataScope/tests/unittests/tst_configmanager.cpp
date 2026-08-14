/**
 * @file tst_configmanager.cpp
 * @brief ConfigManager 单元测试（Qt Test 框架）
 *
 * P7 教学点：
 *   - 用 QTemporaryDir 做"临时目录隔离"：每个用例独立 init 到临时目录，
 *     测试结束自动删除，绝不污染用户真实配置（对应 C# 里测试用临时文件的做法）；
 *   - 持久化用例：写 → sync → 重新 init 同一目录，验证数据能从磁盘读回；
 *   - 类型强转用例：QSettings 的 INI 读写对 QVariant 有类型推断行为。
 */

#include <QtTest>
#include <QTemporaryDir>
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include "infrastructure/configmanager.h"

using datascope::infrastructure::ConfigManager;

// 测试类：继承 QObject，槽函数即测试用例
class TestConfigManager : public QObject
{
    Q_OBJECT

private slots:
    void init_createsFile();
    void setAndGet_roundTrip();
    void typedGetters();
    void defaultValue_WhenMissing();
    void remove_key();
    void persistence_acrossReload();
    void typeCoercion();
};

// a) init 后能给出路径，setValue+sync 后 INI 文件真实落盘
void TestConfigManager::init_createsFile()
{
    QTemporaryDir dir; // 独立临时目录（RAII，析构自动删除）
    QVERIFY(dir.isValid());

    ConfigManager &cm = ConfigManager::instance();
    cm.init(dir.path()); // 指向临时目录

    const QString path = cm.configFilePath();
    QVERIFY(!path.isEmpty());                                          // init 后必须能给出路径
    QCOMPARE(QFileInfo(path).absolutePath(), QDir(dir.path()).absolutePath()); // 文件就在临时目录里

    cm.setValue("app/firstRun", true);
    cm.sync();                              // 强制落盘
    QVERIFY(QFile::exists(path));           // 落盘后 INI 文件真实存在于磁盘
}

// b) 写读回环：写入的值能原样读回
void TestConfigManager::setAndGet_roundTrip()
{
    QTemporaryDir dir;
    QVERIFY(dir.isValid());

    ConfigManager &cm = ConfigManager::instance();
    cm.init(dir.path());

    cm.setValue("com/port", QStringLiteral("COM3"));
    cm.setValue("com/baud", 9600);

    QCOMPARE(cm.value("com/port").toString(), QStringLiteral("COM3"));
    QCOMPARE(cm.value("com/baud").toInt(), 9600);
}

// c) 四个类型化读取各自正确
void TestConfigManager::typedGetters()
{
    QTemporaryDir dir;
    QVERIFY(dir.isValid());

    ConfigManager &cm = ConfigManager::instance();
    cm.init(dir.path());

    cm.setValue("ui/fontSize", 14);
    cm.setValue("ui/darkMode", true);
    cm.setValue("ui/language", QStringLiteral("zh-CN"));
    cm.setValue("ui/zoom", 1.5);

    QCOMPARE(cm.intValue("ui/fontSize"), 14);
    QCOMPARE(cm.boolValue("ui/darkMode"), true);
    QCOMPARE(cm.stringValue("ui/language"), QStringLiteral("zh-CN"));
    QCOMPARE(cm.doubleValue("ui/zoom"), 1.5);
}

// d) 键不存在时：无默认 → 无效 QVariant；有默认 → 返回默认值
void TestConfigManager::defaultValue_WhenMissing()
{
    QTemporaryDir dir;
    QVERIFY(dir.isValid());

    ConfigManager &cm = ConfigManager::instance();
    cm.init(dir.path());

    QVERIFY(!cm.value("no/such").isValid());                            // 无默认 → 无效 QVariant
    QCOMPARE(cm.value("no/such", QVariant(42)).toInt(), 42);            // 带默认 → 返回默认
    QCOMPARE(cm.intValue("no/such", 99), 99);
    QCOMPARE(cm.boolValue("no/such", true), true);
    QCOMPARE(cm.stringValue("no/such", QStringLiteral("def")), QStringLiteral("def"));
    QCOMPARE(cm.doubleValue("no/such", 2.5), 2.5);
    QVERIFY(!cm.contains("no/such"));
}

// e) remove 后键消失，value 回到默认值
void TestConfigManager::remove_key()
{
    QTemporaryDir dir;
    QVERIFY(dir.isValid());

    ConfigManager &cm = ConfigManager::instance();
    cm.init(dir.path());

    cm.setValue("temp/key", QStringLiteral("value"));
    QVERIFY(cm.contains("temp/key"));

    cm.remove("temp/key");
    QVERIFY(!cm.contains("temp/key"));
    QCOMPARE(cm.value("temp/key", QStringLiteral("def")).toString(), QStringLiteral("def"));
}

// f) 关键持久化用例：写 → sync → 重新 init 同一目录（模拟程序重启）→ 键能读回
void TestConfigManager::persistence_acrossReload()
{
    QTemporaryDir dir;
    QVERIFY(dir.isValid());

    ConfigManager &cm = ConfigManager::instance();
    cm.init(dir.path());

    // 第一轮：写入若干配置并 sync 落盘
    cm.setValue("device/name", QStringLiteral("DAQ-2000"));
    cm.setValue("device/channels", 8);
    cm.setValue("comm/baud", 115200);
    cm.sync();

    // 模拟"程序重启"：重新 init 到同一目录，QSettings 重建、重新读盘
    cm.init(dir.path());

    // 第二轮：键必须能从磁盘读回（持久化验证）
    QCOMPARE(cm.stringValue("device/name"), QStringLiteral("DAQ-2000"));
    QCOMPARE(cm.intValue("device/channels"), 8);
    QCOMPARE(cm.intValue("comm/baud"), 115200);
    QVERIFY(cm.contains("device/name"));
    QVERIFY(!cm.contains("no/such"));
}

// g) 类型强转：以字符串数字写入，类型化读取能自动转换（QSettings 的 QVariant 类型推断）
void TestConfigManager::typeCoercion()
{
    QTemporaryDir dir;
    QVERIFY(dir.isValid());

    ConfigManager &cm = ConfigManager::instance();
    cm.init(dir.path());

    cm.setValue("param/count", QVariant(QStringLiteral("123")));
    cm.setValue("param/ratio", QVariant(QStringLiteral("3.14")));
    cm.setValue("param/enabled", QVariant(QStringLiteral("true")));

    QCOMPARE(cm.intValue("param/count"), 123);
    QCOMPARE(cm.doubleValue("param/ratio"), 3.14);
    QCOMPARE(cm.boolValue("param/enabled"), true);
}

QTEST_MAIN(TestConfigManager)
#include "tst_configmanager.moc"
