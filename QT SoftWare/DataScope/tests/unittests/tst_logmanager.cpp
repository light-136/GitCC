/**
 * @file tst_logmanager.cpp
 * @brief LogManager 单元测试（Qt Test 框架）
 *
 * P6 教学点：
 *   - 单例测试隔离：LogManager 是进程级单例，用例之间共享状态，
 *     因此每个用例都重新 init() 到一个全新的临时目录，杜绝相互污染；
 *   - 临时目录隔离：用 QTemporaryDir 把日志写入系统临时目录，
 *     绝不触碰用户的真实 AppData 目录；
 *   - QSignalSpy：Qt 官方的信号监听器，验证 messageLogged 信号是否发出、参数是否正确。
 *
 * 注意（Windows 平台限制）：
 *   LogManager 会一直持有日志文件句柄，因此用例结束时 QTemporaryDir 自动删除
 *   可能在 Windows 下因文件被占用而静默失败（临时目录残留在 %TEMP%）。
 *   这不影响任何断言结果，残留目录可由系统或手动清理；Linux/macOS 下则能正常删除。
 */

#include <QtTest>

#include <QDir>
#include <QFile>
#include <QRegularExpression>
#include <QSignalSpy>
#include <QTemporaryDir>
#include <QTextStream>

#include "infrastructure/logmanager.h"

using datascope::infrastructure::LogManager;
using datascope::infrastructure::LogLevel;

// 测试类：继承 QObject，每个 private slot 就是一个测试用例
class TestLogManager : public QObject
{
    Q_OBJECT

private slots:
    void init_createsFile();            // a) init 后文件被创建
    void log_writesToFile();            // b) 写盘内容与计数
    void levels_coverAll();             // c) 各级别标签齐全
    void messageLogged_signalEmitted(); // d) 信号发出与参数
    void formatLine_containsTimestamp();// e) 时间戳格式
};

// ================= 用例 a：init 创建文件 =================

void TestLogManager::init_createsFile()
{
    // 每个用例都新建独立临时目录，并让单例重新 init，避免状态互相污染
    QTemporaryDir tempDir(QDir::tempPath() + QStringLiteral("/dscore_logtest_XXXXXX"));
    QVERIFY(tempDir.isValid());

    LogManager::instance().init(tempDir.path());

    // 契约：init 后 logFilePath() 非空，且对应文件真实存在
    const QString path = LogManager::instance().logFilePath();
    QVERIFY(!path.isEmpty());
    QVERIFY(QFile::exists(path));
}

// ================= 用例 b：写盘内容与计数 =================

void TestLogManager::log_writesToFile()
{
    QTemporaryDir tempDir(QDir::tempPath() + QStringLiteral("/dscore_logtest_XXXXXX"));
    QVERIFY(tempDir.isValid());

    LogManager::instance().init(tempDir.path());

    // 写一条 Info 日志
    LogManager::instance().info(QStringLiteral("ModuleA"), QStringLiteral("hello log message"));

    // 已写盘条数应为 1（init 重置过计数）
    QCOMPARE(LogManager::instance().messageCount(), 1);

    // 读取文件内容（Text 模式读取会把 CRLF 归一为 LF，便于断言）
    QFile file(LogManager::instance().logFilePath());
    QVERIFY(file.open(QIODevice::ReadOnly | QIODevice::Text));
    const QString content = QString::fromUtf8(file.readAll());
    file.close();

    // 行内应包含级别标签、模块名、消息正文
    QVERIFY(content.contains(QStringLiteral("[Info]")));
    QVERIFY(content.contains(QStringLiteral("[ModuleA]")));
    QVERIFY(content.contains(QStringLiteral("hello log message")));
}

// ================= 用例 c：覆盖全部级别 =================

void TestLogManager::levels_coverAll()
{
    QTemporaryDir tempDir(QDir::tempPath() + QStringLiteral("/dscore_logtest_XXXXXX"));
    QVERIFY(tempDir.isValid());

    LogManager::instance().init(tempDir.path());

    // 用 5 个便捷方法 + log(Fatal) 覆盖全部 6 个级别
    LogManager::instance().trace(QStringLiteral("M"), QStringLiteral("trace msg"));
    LogManager::instance().debug(QStringLiteral("M"), QStringLiteral("debug msg"));
    LogManager::instance().info(QStringLiteral("M"), QStringLiteral("info msg"));
    LogManager::instance().warn(QStringLiteral("M"), QStringLiteral("warn msg"));
    LogManager::instance().error(QStringLiteral("M"), QStringLiteral("error msg"));
    LogManager::instance().log(LogLevel::Fatal, QStringLiteral("M"), QStringLiteral("fatal msg"));

    QFile file(LogManager::instance().logFilePath());
    QVERIFY(file.open(QIODevice::ReadOnly | QIODevice::Text));
    const QString content = QString::fromUtf8(file.readAll());
    file.close();

    // 每个级别标签都应出现在文件中
    QVERIFY(content.contains(QStringLiteral("[Trace]")));
    QVERIFY(content.contains(QStringLiteral("[Debug]")));
    QVERIFY(content.contains(QStringLiteral("[Info]")));
    QVERIFY(content.contains(QStringLiteral("[Warn]")));
    QVERIFY(content.contains(QStringLiteral("[Error]")));
    QVERIFY(content.contains(QStringLiteral("[Fatal]")));
}

// ================= 用例 d：messageLogged 信号 =================

void TestLogManager::messageLogged_signalEmitted()
{
    QTemporaryDir tempDir(QDir::tempPath() + QStringLiteral("/dscore_logtest_XXXXXX"));
    QVERIFY(tempDir.isValid());

    LogManager::instance().init(tempDir.path());

    // 用 QSignalSpy 监听单例的 messageLogged 信号
    QSignalSpy spy(&LogManager::instance(), &LogManager::messageLogged);

    LogManager::instance().info(QStringLiteral("Mod"), QStringLiteral("payload"));

    // 只发出一次
    QCOMPARE(spy.count(), 1);

    // 参数个数应为 3：level / module / message
    QCOMPARE(spy.at(0).size(), 3);

    // 第 1 个参数：LogLevel（自定义枚举，借助 Q_DECLARE_METATYPE 取回）
    const QVariant levelVar = spy.at(0).at(0);
    QVERIFY(levelVar.isValid());
    QCOMPARE(static_cast<int>(levelVar.value<LogLevel>()), static_cast<int>(LogLevel::Info));

    // 第 2、3 个参数：模块名与消息正文
    QCOMPARE(spy.at(0).at(1).toString(), QStringLiteral("Mod"));
    QCOMPARE(spy.at(0).at(2).toString(), QStringLiteral("payload"));
}

// ================= 用例 e：行内含时间戳 =================

void TestLogManager::formatLine_containsTimestamp()
{
    QTemporaryDir tempDir(QDir::tempPath() + QStringLiteral("/dscore_logtest_XXXXXX"));
    QVERIFY(tempDir.isValid());

    LogManager::instance().init(tempDir.path());

    LogManager::instance().error(QStringLiteral("M"), QStringLiteral("x"));

    QFile file(LogManager::instance().logFilePath());
    QVERIFY(file.open(QIODevice::ReadOnly | QIODevice::Text));
    const QString content = QString::fromUtf8(file.readAll());
    file.close();

    // 时间戳应匹配 "yyyy-MM-dd HH:mm:ss"（含到秒）
    const QRegularExpression re(
        QStringLiteral("\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}"));
    QVERIFY(re.match(content).hasMatch());
}

// 生成 main()：QTEST_MAIN 会为测试类生成独立可执行文件入口
QTEST_MAIN(TestLogManager)
#include "tst_logmanager.moc"
