// 固件解析器单元测试类声明（无 main，由 tst_all.cpp 统一执行）
#ifndef TST_PARSER_H
#define TST_PARSER_H

#include <QObject>

class ParserTest : public QObject
{
    Q_OBJECT
private slots:
    void missingFileFails();     // 文件不存在
    void wrongExtensionFails();  // 非 .bin 扩展名
    void emptyFileFails();       // 空文件
    void validBinSucceeds();     // 正常 .bin 文件
    void contentMatches();       // 内容一致性
};

#endif // TST_PARSER_H
