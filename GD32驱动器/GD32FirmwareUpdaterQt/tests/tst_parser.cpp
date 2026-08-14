// ============================================================
//  固件解析器单元测试
//
//  【测试知识】测试用临时文件：
//  QTemporaryDir 自动创建/清理临时目录，避免污染仓库。
// ============================================================
#include "tst_parser.h"
#include "services/FirmwareParser.h"
#include <QtTest>
#include <QTemporaryDir>
#include <QFile>
#include <QDebug>

// 在临时目录中写一个文件，返回完整路径
static QString writeTempFile(QTemporaryDir &dir, const QString &name, const QByteArray &content)
{
    QString path = dir.filePath(name);
    QFile f(path);
    if (f.open(QIODevice::WriteOnly))
    {
        f.write(content);
        f.close();
    }
    return path;
}

void ParserTest::missingFileFails()
{
    QString error;
    FirmwareInfo info;
    bool ok = FirmwareParser::loadFirmware(QStringLiteral("C:/不存在/的/文件.bin"), &info, &error);
    QVERIFY(!ok);
    QVERIFY(!error.isEmpty());
}

void ParserTest::wrongExtensionFails()
{
    QTemporaryDir dir;
    QVERIFY(dir.isValid());
    QString path = writeTempFile(dir, QStringLiteral("fw.txt"), QByteArray("hello"));
    QString error;
    FirmwareInfo info;
    bool ok = FirmwareParser::loadFirmware(path, &info, &error);
    QVERIFY(!ok);
    QVERIFY(error.contains(QStringLiteral("bin")));
}

void ParserTest::emptyFileFails()
{
    QTemporaryDir dir;
    QVERIFY(dir.isValid());
    QString path = writeTempFile(dir, QStringLiteral("empty.bin"), QByteArray());
    QString error;
    FirmwareInfo info;
    bool ok = FirmwareParser::loadFirmware(path, &info, &error);
    QVERIFY(!ok);
}

void ParserTest::validBinSucceeds()
{
    QTemporaryDir dir;
    QVERIFY(dir.isValid());

    // 模拟一段 1KB 固件（0xAA 0x55 循环填充）
    QByteArray content;
    for (int i = 0; i < 1024; ++i)
        content.append(char(i % 2 == 0 ? 0xAA : 0x55));

    QString path = writeTempFile(dir, QStringLiteral("firmware.bin"), content);
    QString error;
    FirmwareInfo info;
    bool ok = FirmwareParser::loadFirmware(path, &info, &error);

    QVERIFY(ok);
    QVERIFY(error.isEmpty());
    QCOMPARE(info.fileSize, qint64(1024));
    QCOMPARE(info.data.size(), 1024);
    QVERIFY(info.fileName.contains(QStringLiteral("firmware.bin")));
    QVERIFY(!info.fileSizeText().isEmpty());
}

void ParserTest::contentMatches()
{
    QTemporaryDir dir;
    QVERIFY(dir.isValid());

    // 写入带标记的二进制内容，读取后逐字节核对
    QByteArray content;
    for (int i = 0; i < 256; ++i)
        content.append(char(i));

    QString path = writeTempFile(dir, QStringLiteral("pattern.bin"), content);
    QString error;
    FirmwareInfo info;
    FirmwareParser::loadFirmware(path, &info, &error);

    QCOMPARE(info.data, content);
}
