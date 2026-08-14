// 协议帧单元测试类声明（无 main，由 tst_all.cpp 统一执行）
#ifndef TST_FRAME_H
#define TST_FRAME_H

#include <QObject>

class FrameTest : public QObject
{
    Q_OBJECT
private slots:
    void upgradeRequestFrame();  // 升级请求帧结构
    void upgradeStartFrame();    // 启动升级帧结构
    void upgradeDataFrame();     // 数据帧结构
    void jumpAppFrame();         // 完成升级帧结构
    void versionRequestFrame();  // 版本查询帧结构
    void serializeDeserializeRoundTrip();  // 组帧→拆帧往返
    void corruptedCrcFails();    // CRC 损坏应失败
    void wrongSofFails();        // 帧头错误应失败
    void shortBufferFails();     // 数据不足应失败
};

#endif // TST_FRAME_H
