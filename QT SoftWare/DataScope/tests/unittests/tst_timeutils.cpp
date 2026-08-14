/**
 * @file tst_timeutils.cpp
 * @brief TimeUtils 单元测试（Qt Test 框架）
 *
 * P3 教学点：
 *   - 数据驱动测试（_data 函数 + QFETCH/QCOMPARE）复用于 parseTime / formatDuration，
 *     对应 C# NUnit 的 TestCase；
 *   - 用 QRegularExpression 校验自由文本格式（nowString 是当前时刻，无法断言固定串）；
 *   - TryParse 风格的"失败不修改出参"验证；
 *   - 边界测试：空串、非法输入、负数、0 间隔。
 */

#include <QtTest>
#include <QRegularExpression>
#include "utils/timeutils.h"

using datascope::utils::TimeUtils;

// 测试类：继承 QObject，槽函数即测试用例
class TestTimeUtils : public QObject
{
    Q_OBJECT

private slots:
    // ---- 格式化 ----
    void formatDateTime_format();
    void nowString_format();
    void fileTimestamp_noSeparator();

    // ---- 解析 ----
    void parseTime_data();
    void parseTime();
    void parseTime_invalidKeepsOut();

    // ---- 时长 ----
    void formatDuration_data();
    void formatDuration();

    // ---- 间隔 ----
    void elapsedMicros_zero();
    void elapsedMicros_100ms();
};

// ================= 格式化 =================

void TestTimeUtils::formatDateTime_format()
{
    // 用固定时刻构造，验证精确串（毫秒 123 → ".123"）
    const QDateTime dt(QDate(2026, 8, 14), QTime(9, 30, 12, 123));
    QCOMPARE(TimeUtils::formatDateTime(dt), QStringLiteral("2026-08-14 09:30:12.123"));
}

void TestTimeUtils::nowString_format()
{
    // nowString 是"当前时刻"，不能断言具体值，用正则校验"格式形状"
    // 形状：yyyy-MM-dd HH:mm:ss.zzz
    const QString s = TimeUtils::nowString();
    const QRegularExpression re(QStringLiteral(
        "^\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}\\.\\d{3}$"));
    QVERIFY2(re.match(s).hasMatch(), qPrintable(QStringLiteral("nowString 格式异常: ") + s));
}

void TestTimeUtils::fileTimestamp_noSeparator()
{
    // 固定时刻 → 无分隔符：8 位日期 + _ + 6 位时分秒 + _ + 3 位毫秒
    const QDateTime dt(QDate(2026, 8, 14), QTime(9, 30, 12, 123));
    const QString s = TimeUtils::fileTimestamp(dt);
    QCOMPARE(s, QStringLiteral("20260814_093012_123"));
    QCOMPARE(s.size(), 19); // 8位日期 + _ + 6位时分秒 + _ + 3位毫秒 = 19
    // 不允许出现空格、冒号、连字符、点号等非法文件名字符
    QVERIFY(!s.contains(QLatin1Char(' ')));
    QVERIFY(!s.contains(QLatin1Char(':')));
    QVERIFY(!s.contains(QLatin1Char('-')));
    QVERIFY(!s.contains(QLatin1Char('.')));
}

// ================= 解析 =================

void TestTimeUtils::parseTime_data()
{
    QTest::addColumn<QString>("text");
    QTest::addColumn<bool>("expectedValid");
    QTest::addColumn<QTime>("expectedTime");

    QTest::newRow("正常") << QStringLiteral("09:30:12") << true << QTime(9, 30, 12);
    QTest::newRow("带毫秒") << QStringLiteral("09:30:12.123") << true << QTime(9, 30, 12, 123);
    QTest::newRow("非法-小时越界") << QStringLiteral("25:00:00") << false << QTime();
    QTest::newRow("非法-空串") << QString() << false << QTime();
}

void TestTimeUtils::parseTime()
{
    QFETCH(QString, text);
    QFETCH(bool, expectedValid);
    QFETCH(QTime, expectedTime);

    QTime out;
    const bool ok = TimeUtils::parseTime(text, out);
    QCOMPARE(ok, expectedValid);
    if (expectedValid)
        QCOMPARE(out, expectedTime);
}

void TestTimeUtils::parseTime_invalidKeepsOut()
{
    // TryParse 风格：解析失败必须返回 false 且不修改 out
    QTime out(1, 2, 3); // 预设一个初值
    QVERIFY(!TimeUtils::parseTime(QStringLiteral("not a time"), out));
    QCOMPARE(out, QTime(1, 2, 3)); // out 保持初值，未被污染
}

// ================= 时长 =================

void TestTimeUtils::formatDuration_data()
{
    QTest::addColumn<qint64>("seconds");
    QTest::addColumn<QString>("expected");

    QTest::newRow("59秒") << qint64(59) << QStringLiteral("00:59");
    QTest::newRow("3599秒") << qint64(3599) << QStringLiteral("59:59");
    QTest::newRow("3600秒=1小时") << qint64(3600) << QStringLiteral("01:00:00");
    QTest::newRow("1小时1分1秒") << qint64(3661) << QStringLiteral("01:01:01");
    QTest::newRow("负数按0") << qint64(-5) << QStringLiteral("00:00");
}

void TestTimeUtils::formatDuration()
{
    QFETCH(qint64, seconds);
    QFETCH(QString, expected);

    QCOMPARE(TimeUtils::formatDuration(seconds), expected);
}

// ================= 间隔 =================

void TestTimeUtils::elapsedMicros_zero()
{
    // 两个相同时刻 → 0 微秒
    const QDateTime dt(QDate(2026, 8, 14), QTime(9, 30, 12, 0));
    QCOMPARE(TimeUtils::elapsedMicros(dt, dt), qint64(0));
}

void TestTimeUtils::elapsedMicros_100ms()
{
    // 间隔 100ms → 100 * 1000 = 100000 微秒
    const QDateTime start(QDate(2026, 8, 14), QTime(9, 30, 12, 0));
    const QDateTime end(QDate(2026, 8, 14), QTime(9, 30, 12, 100));
    QCOMPARE(TimeUtils::elapsedMicros(start, end), qint64(100000));
    // 反向 → 负值（msecsTo 的方向语义）
    QCOMPARE(TimeUtils::elapsedMicros(end, start), qint64(-100000));
}

// 生成 main()：QTEST_MAIN 会为测试类生成独立可执行文件入口
QTEST_MAIN(TestTimeUtils)
#include "tst_timeutils.moc"
