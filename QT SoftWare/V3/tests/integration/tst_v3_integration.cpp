/**
 * @file tst_v3_integration.cpp
 * @brief V3 集成测试 —— 第一条真实 E2E 数据链路（QtTest + 真实 TCP 回路）
 *
 * ── 测试目标（对应权威规格第 12 节 Integration 层）──
 *   验证"Simulator → TCP → AcquisitionWorker → FrameParser → DataPoint → 信号"整条
 *   链路真实打通：内嵌 SimulatorDevice（随机端口）作为真实 TCP 对端，内嵌
 *   AcquisitionWorker（moveToThread 到采集线程）作为采集端，二者走真实 socket
 *   回路收发协议帧，断言收到合法数据点与通道配置。
 *
 * ── 为什么必须内嵌而非起外部进程 ──
 *   外部进程的端口/生命周期/时序都不稳定，测试会 flaky。内嵌 SimulatorDevice
 *   用 start(0) 拿到系统分配的随机端口，进程内即可闭环，测试确定、可重复。
 *
 * ── 线程模型还原 ──
 *   AcquisitionWorker 被 moveToThread 到独立 QThread，与生产 main.cpp 的线程
 *   模型一致：worker 的 socket I/O 在采集线程，断言在主线程，中间靠
 *   QueuedConnection（信号/槽跨线程队列投递）协作。
 */

#include <QtTest>

#include <QSet>
#include <QThread>

#include "acquisition/acquisitionworker.h"
#include "domain/connectiondefaults.h"
#include "domain/errorcode.h"
#include "simulatordevice.h"

/**
 * @class V3IntegrationTest
 * @brief 集成测试容器（QTEST_MAIN 驱动，需事件循环跑 QThread + socket）
 */
class V3IntegrationTest : public QObject
{
    Q_OBJECT

private slots:
    void fullE2EChain();             // 完整 E2E 数据链路（normal 模式）
    void faultInjectionRobustness();  // 故障注入下采集端不断线（权威规格硬性规定）
};

void V3IntegrationTest::fullE2EChain()
{
    // ── 1. 内嵌模拟设备（随机端口，作为真实 TCP 对端）──
    dscope::simulator::SimulatorDevice device;
    QVERIFY(device.start(0));          // 0 = 系统分配随机可用端口
    const quint16 port = device.port();
    QVERIFY(port > 0);

    // ── 2. 采集 worker 迁到独立采集线程（还原生产线程模型）──
    dscope::acquisition::AcquisitionWorker worker;
    QThread workerThread;
    worker.moveToThread(&workerThread);
    workerThread.start();

    // ── 3. 结果收集（lambda 经 QueuedConnection 回主线程执行）──
    int connectedCount = 0;                                  // 连接建立次数
    int pointsCount    = 0;                                  // 数据点批次次数
    int configCount    = 0;                                  // 通道配置次数
    QVector<dscope::domain::DataPoint>     lastPoints;       // 最近一批数据点
    QVector<dscope::domain::ChannelConfig> lastChannels;     // 最近一次通道配置

    connect(&worker, &dscope::acquisition::AcquisitionWorker::connected,
            this, [&]() { ++connectedCount; });
    connect(&worker, &dscope::acquisition::AcquisitionWorker::pointsReady,
            this, [&](const QVector<dscope::domain::DataPoint> &pts) {
                ++pointsCount;
                lastPoints = pts;
            });
    connect(&worker, &dscope::acquisition::AcquisitionWorker::channelConfigReceived,
            this, [&](const QVector<dscope::domain::ChannelConfig> &chs) {
                ++configCount;
                lastChannels = chs;
            });

    // ── 4. 发起连接（跨线程队列调用 start 槽，落到采集线程执行）──
    QMetaObject::invokeMethod(&worker, "start", Qt::QueuedConnection,
                              Q_ARG(QString, QString::fromLatin1(dscope::domain::kDefaultHost)),
                              Q_ARG(quint16, port));

    // ── 5. 等待三条链路事件（连接 + 数据 + 配置）──
    QTRY_VERIFY_WITH_TIMEOUT(connectedCount >= 1, 5000);
    QTRY_VERIFY_WITH_TIMEOUT(pointsCount    >= 1, 5000);
    QTRY_VERIFY_WITH_TIMEOUT(configCount    >= 1, 5000);

    // ── 6. 断言数据点内容：通道号连续（0..N-1）、值已填 ──
    QVERIFY(!lastPoints.isEmpty());
    for (int i = 0; i < lastPoints.size(); ++i)
        QCOMPARE(lastPoints.at(i).channelIndex, i);

    // ── 7. 断言通道配置：默认 4 通道、量程 0~100 ──
    QCOMPARE(lastChannels.size(), 4);
    for (const dscope::domain::ChannelConfig &cfg : lastChannels) {
        QVERIFY(cfg.rangeMin >= 0.0);
        QVERIFY(cfg.rangeMax <= 100.0);
        QVERIFY(!cfg.name.isEmpty());
    }

    // ── 8. 收尾：停采集 → 退线程（stop 已清理定时器/套接字，栈对象安全析构）──
    QMetaObject::invokeMethod(&worker, "stop", Qt::BlockingQueuedConnection);
    workerThread.quit();
    workerThread.wait();
}

void V3IntegrationTest::faultInjectionRobustness()
{
    // ── 1. 故障设备：轮询注入五类异常（粘包/分包/坏CRC/非法帧/垃圾前缀）──
    dscope::simulator::SimulatorDevice device;
    device.setFaultEnabled(true);
    device.setFaultCycling(true);
    QVERIFY(device.start(0));
    const quint16 port = device.port();

    // ── 2. 采集 worker 迁到采集线程 ──
    dscope::acquisition::AcquisitionWorker worker;
    QThread workerThread;
    worker.moveToThread(&workerThread);
    workerThread.start();

    // ── 3. 结果收集 ──
    int pointsCount    = 0;   // 正常数据点批次（Sticky/Fragment/GarbagePrefix 产生）
    int errorCount     = 0;   // 坏帧告警次数（CrcError/InvalidFrame 产生）
    int disconnectCount = 0;  // 断线次数（权威规格：坏帧不得触发断线，应恒为 0）
    QSet<int> errorCodes;     // 收集出现的错误码，断言坏CRC/非法帧各自映射到正确错误码

    connect(&worker, &dscope::acquisition::AcquisitionWorker::pointsReady,
            this, [&](const QVector<dscope::domain::DataPoint> &) { ++pointsCount; });
    connect(&worker, &dscope::acquisition::AcquisitionWorker::connectionError,
            this, [&](int code) { ++errorCount; errorCodes.insert(code); });
    connect(&worker, &dscope::acquisition::AcquisitionWorker::disconnected,
            this, [&]() { ++disconnectCount; });

    // ── 4. 发起连接 ──
    QMetaObject::invokeMethod(&worker, "start", Qt::QueuedConnection,
                              Q_ARG(QString, QString::fromLatin1(dscope::domain::kDefaultHost)),
                              Q_ARG(quint16, port));

    // ── 5. 等待：合法帧（粘包/分包/重同步）产生数据点 ──
    QTRY_VERIFY_WITH_TIMEOUT(pointsCount >= 1, 8000);

    // fault 轮询顺序 Sticky→Fragment→CrcError→InvalidFrame→GarbagePrefix。
    // 逐类错误码断言前必须分别等待对应故障帧真正到达：InvalidFrame 晚于 CrcError
    // 一帧（第 3/4 次 onTick），若只等 errorCount>=1 会在 InvalidFrame 注入前就
    // 断言，造成时序竞态（上一版断言偶发失败即因此）。
    QTRY_VERIFY_WITH_TIMEOUT(
        errorCodes.contains(static_cast<int>(dscope::domain::ErrorCode::CrcError)), 8000);
    QTRY_VERIFY_WITH_TIMEOUT(
        errorCodes.contains(static_cast<int>(dscope::domain::ErrorCode::InvalidFrame)), 8000);

    // ── 6. 关键断言：坏帧只计数告警，绝不触发断线 ──
    QCOMPARE(disconnectCount, 0);

    // ── 7. 收尾 ──
    QMetaObject::invokeMethod(&worker, "stop", Qt::BlockingQueuedConnection);
    workerThread.quit();
    workerThread.wait();
}

QTEST_MAIN(V3IntegrationTest)
#include "tst_v3_integration.moc"
