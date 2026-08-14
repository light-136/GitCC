// CRC16 校验单元测试类声明（无 main，由 tst_all.cpp 统一执行）
#ifndef TST_CRC_H
#define TST_CRC_H

#include <QObject>

class CrcTest : public QObject
{
    Q_OBJECT
private slots:
    void standardVector();    // 标准校验向量（"123456789" → 0x4B37）
    void emptyData();         // 空数据
    void littleEndianBytes(); // 小端字节输出
    void rangeCalculate();    // 指定范围计算
    void frameRoundTrip();    // 组帧/拆帧 CRC 自洽
};

#endif // TST_CRC_H
