/**
 * @file tst_signalslotdemo.cpp
 * @brief SignalSlotDemo 信号槽机制单测（P4）
 *
 * 教学点：
 *   - QSignalSpy 是 QtTest 提供的"信号探针"：像 WPF 里订阅事件来断言事件
 *     是否触发（对应 NUnit 的事件计数验证）；
 *   - 信号是 public 成员函数，测试里可以像普通函数一样调用（触发所有已连接槽），
 *     这正是"信号 = 广播方法"的本质；
 *   - clock 用例故意等待 1.2 秒，实证"QTimer 必须依赖事件循环才会触发"。
 */

#include <QtTest>
#include <QSignalSpy>

#include "app/signalslotdemo.h"

using namespace datascope::app;

class TestSignalSlotDemo : public QObject
{
    Q_OBJECT

private slots:
    void customSignal_emitsToConnectedSlot();   // 信号 → 已连接槽
    void disconnect_stopsDelivery();            // 断开后不再送达
    void runAllDemos_emitsMessages();           // 五种演示都发说明消息
    void clock_emitsAfterEventLoop();           // 定时器依赖事件循环
};

void TestSignalSlotDemo::customSignal_emitsToConnectedSlot()
{
    SignalSlotDemo demo;

    // 探针1：监听 counterChanged 是否发出
    QSignalSpy counterSpy(&demo, &SignalSlotDemo::counterChanged);
    // 探针2：监听 onCounterChanged 转发出的说明消息
    QSignalSpy msgSpy(&demo, &SignalSlotDemo::messageEmitted);

    // 接线：counterChanged 信号 → onCounterChanged 槽
    QObject::connect(&demo, &SignalSlotDemo::counterChanged,
                     &demo, &SignalSlotDemo::onCounterChanged);

    // 手动触发信号（信号是 public 函数，可像普通函数调用）
    emit demo.counterChanged(42);

    QCOMPARE(counterSpy.count(), 1);   // 信号确实发出一次
    QVERIFY(msgSpy.count() >= 1);      // 槽收到并转发了一条说明消息
}

void TestSignalSlotDemo::disconnect_stopsDelivery()
{
    SignalSlotDemo demo;
    QSignalSpy msgSpy(&demo, &SignalSlotDemo::messageEmitted);

    // 建立连接并触发一次，确认链路通畅
    const QMetaObject::Connection conn =
        QObject::connect(&demo, &SignalSlotDemo::counterChanged,
                         &demo, &SignalSlotDemo::onCounterChanged);
    emit demo.counterChanged(1);
    QVERIFY(msgSpy.count() >= 1);      // 连接期间：消息送达

    // 按连接句柄断开 → 再触发，消息不再到达
    const int countBefore = msgSpy.count();
    QObject::disconnect(conn);
    emit demo.counterChanged(2);
    QCOMPARE(msgSpy.count(), countBefore);   // 数量不变 = 断开生效
}

void TestSignalSlotDemo::runAllDemos_emitsMessages()
{
    SignalSlotDemo demo;
    QSignalSpy spy(&demo, &SignalSlotDemo::messageEmitted);

    // runAllDemos 依次演示五种连接，每种都会 emit 至少一条说明文本
    demo.runAllDemos();

    QVERIFY2(spy.count() >= 5,
             "五种连接方式每种都应输出至少一条演示消息");
}

void TestSignalSlotDemo::clock_emitsAfterEventLoop()
{
    SignalSlotDemo demo;
    QSignalSpy spy(&demo, &SignalSlotDemo::timeTicked);

    demo.startClock();

    // 关键教学点：只 start() 还不够，必须让 Qt 事件循环跑起来。
    // QSignalSpy::wait(ms) 会在等待期间运行事件循环，并等"timeTicked 信号
    // 出现"这一确定性事件返回 true——比盲等固定时长(qWait)更稳：
    // 修复高负载下 QTimer 到期边界与等待窗口错位导致的偶发失败（flaky）。
    // QTimer(1000ms) 每秒触发一次，2 秒等待窗口充裕。
    QVERIFY2(spy.wait(2000),
             "启动时钟并进入事件循环后，应收到至少一次 timeTicked");
}

QTEST_MAIN(TestSignalSlotDemo)
#include "tst_signalslotdemo.moc"
