/**
 * @file tst_byteutils.cpp
 * @brief ByteUtils 单元测试（Qt Test 框架）
 *
 * P2 教学点：
 *   - Qt Test 的"数据驱动测试"（_data 函数 + QFETCH/QCOMPARE）：
 *     一个测试逻辑，用多组输入/期望值跑多遍，对应 C# NUnit 的 TestCase；
 *   - 边界测试：空数组、单字节、越界读取、非法输入；
 *   - 独立测试 target：与主程序分离，仅链接被测库（对应 WPF 里的测试项目）。
 */

#include <QtTest>
#include "utils/byteutils.h"

using datascope::utils::ByteUtils;

// 测试类：继承 QObject，槽函数即测试用例
class TestByteUtils : public QObject
{
    Q_OBJECT

private slots:
    // ---- 十六进制互转 ----
    void toHexString_data();   // 提供数据（数据驱动测试）
    void toHexString();        // 消费数据执行断言
    void fromHexString_normal();
    void fromHexString_tolerant();
    void fromHexString_invalid();

    // ---- 大小端读取 ----
    void readUInt16BE();
    void readUInt32BE();
    void readOutOfRange();

    // ---- 大小端写入 ----
    void appendUInt16BE();
    void appendUInt32BE();
};

// ================= 十六进制互转 =================

void TestByteUtils::toHexString_data()
{
    // 数据驱动：声明列，再一行一个用例
    QTest::addColumn<QByteArray>("data");
    QTest::addColumn<QString>("separator");
    QTest::addColumn<QString>("expected");

    QTest::newRow("空数组") << QByteArray() << QStringLiteral(" ") << QStringLiteral("");
    QTest::newRow("单字节") << QByteArray("\xAA", 1) << QStringLiteral(" ") << QStringLiteral("AA");
    QTest::newRow("多字节带分隔符") << QByteArray("\x01\x02\xFF", 3)
                                    << QStringLiteral(" ")
                                    << QStringLiteral("01 02 FF");
    QTest::newRow("无分隔符") << QByteArray("\x0A\x0B", 2)
                              << QStringLiteral("")
                              << QStringLiteral("0A0B");
}

void TestByteUtils::toHexString()
{
    QFETCH(QByteArray, data);
    QFETCH(QString, separator);
    QFETCH(QString, expected);

    QCOMPARE(ByteUtils::toHexString(data, separator), expected);
}

void TestByteUtils::fromHexString_normal()
{
    // 标准大写输入
    const QByteArray result = ByteUtils::fromHexString(QStringLiteral("01 02 FF"));
    QCOMPARE(result.size(), 3);
    QCOMPARE(static_cast<quint8>(result.at(0)), quint8(0x01));
    QCOMPARE(static_cast<quint8>(result.at(1)), quint8(0x02));
    QCOMPARE(static_cast<quint8>(result.at(2)), quint8(0xFF));
}

void TestByteUtils::fromHexString_tolerant()
{
    // 宽容解析：小写、0x 前缀、逗号分隔混合
    const QByteArray result = ByteUtils::fromHexString(QStringLiteral("0xAA,bb,0c"));
    QCOMPARE(result.size(), 3);
    QCOMPARE(static_cast<quint8>(result.at(0)), quint8(0xAA));
    QCOMPARE(static_cast<quint8>(result.at(1)), quint8(0xBB));
    QCOMPARE(static_cast<quint8>(result.at(2)), quint8(0x0C));
}

void TestByteUtils::fromHexString_invalid()
{
    // 非法字符 → 空数组
    QVERIFY(ByteUtils::fromHexString(QStringLiteral("GG HH")).isEmpty());
    // 奇数个 hex 数字（凑不齐字节）→ 空数组
    QVERIFY(ByteUtils::fromHexString(QStringLiteral("A")).isEmpty());
    // 空输入 → 空数组
    QVERIFY(ByteUtils::fromHexString(QString()).isEmpty());
}

// ================= 大小端读取 =================

void TestByteUtils::readUInt16BE()
{
    // 构造 0x0102 的字节序：01 02
    const QByteArray data = QByteArray("\x01\x02", 2);
    quint16 value = 0;
    QVERIFY(ByteUtils::readUInt16BE(data, 0, value));
    QCOMPARE(value, quint16(0x0102));
}

void TestByteUtils::readUInt32BE()
{
    // 构造 0x01020304：01 02 03 04
    const QByteArray data = QByteArray("\x01\x02\x03\x04", 4);
    quint32 value = 0;
    QVERIFY(ByteUtils::readUInt32BE(data, 0, value));
    QCOMPARE(value, quint32(0x01020304));
}

void TestByteUtils::readOutOfRange()
{
    // 越界读取必须返回 false 且不修改 out（用初值验证 out 未被触碰）
    const QByteArray tiny = QByteArray("\x01", 1);
    quint16 v16 = 0xABCD;
    quint32 v32 = 0xABCDEF12;

    // 单字节数据读 16 位 → 越界
    QVERIFY(!ByteUtils::readUInt16BE(tiny, 0, v16));
    QCOMPARE(v16, quint16(0xABCD)); // out 未被修改

    // 负偏移 → 越界
    QVERIFY(!ByteUtils::readUInt32BE(tiny, -1, v32));
    QCOMPARE(v32, quint32(0xABCDEF12));

    // 偏移指向数据末尾 → 越界
    const QByteArray d4 = QByteArray("\x01\x02\x03\x04", 4);
    QVERIFY(!ByteUtils::readUInt32BE(d4, 1, v32));
}

// ================= 大小端写入 =================

void TestByteUtils::appendUInt16BE()
{
    QByteArray out;
    ByteUtils::appendUInt16BE(out, quint16(0x0102));
    QCOMPARE(out.size(), 2);
    QCOMPARE(static_cast<quint8>(out.at(0)), quint8(0x01)); // 高位在前
    QCOMPARE(static_cast<quint8>(out.at(1)), quint8(0x02));
}

void TestByteUtils::appendUInt32BE()
{
    QByteArray out;
    ByteUtils::appendUInt32BE(out, quint32(0x01020304));
    QCOMPARE(out.size(), 4);
    QCOMPARE(static_cast<quint8>(out.at(0)), quint8(0x01));
    QCOMPARE(static_cast<quint8>(out.at(1)), quint8(0x02));
    QCOMPARE(static_cast<quint8>(out.at(2)), quint8(0x03));
    QCOMPARE(static_cast<quint8>(out.at(3)), quint8(0x04));
}

// 生成 main()：QTEST_MAIN 会为测试类生成独立可执行文件入口
QTEST_MAIN(TestByteUtils)
#include "tst_byteutils.moc"
