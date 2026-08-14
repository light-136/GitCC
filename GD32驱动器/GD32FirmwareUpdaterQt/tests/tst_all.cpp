// ============================================================
//  单元测试统一入口
//
//  【Qt知识点】QTest::qExec 手动运行测试类：
//  相比每个文件一个 QTEST_MAIN，这里用一个 main 依次执行
//  全部测试类，方便统一统计、统一退出码。
//  （对比 C# 测试运行器依次运行各 [Fact]/[TestMethod]）
// ============================================================
#include <QtTest>
#include <QCoreApplication>
#include "tst_crc.h"
#include "tst_frame.h"
#include "tst_parser.h"

int main(int argc, char *argv[])
{
    QCoreApplication app(argc, argv);

    // 每组的测试结果分别输出到独立文件，避免互相覆盖
    // （QTest::qExec 的 QStringList 重载可注入命令行参数）
    QStringList crcArgs    = QStringList() << QStringLiteral("CrcTest")
                                           << QStringLiteral("-o") << QStringLiteral("tst_crc_results.txt,txt");
    QStringList frameArgs  = QStringList() << QStringLiteral("FrameTest")
                                           << QStringLiteral("-o") << QStringLiteral("tst_frame_results.txt,txt");
    QStringList parserArgs = QStringList() << QStringLiteral("ParserTest")
                                           << QStringLiteral("-o") << QStringLiteral("tst_parser_results.txt,txt");

    int status = 0;

    // 依次执行三组测试；任一失败都会累加退出码（非零即失败）
    {
        CrcTest tc;
        status |= QTest::qExec(&tc, crcArgs);
    }
    {
        FrameTest tc;
        status |= QTest::qExec(&tc, frameArgs);
    }
    {
        ParserTest tc;
        status |= QTest::qExec(&tc, parserArgs);
    }

    return status;
}
