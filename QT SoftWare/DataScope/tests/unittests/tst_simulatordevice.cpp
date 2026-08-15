/**
 * @file tst_simulatordevice.cpp
 * @brief 模拟设备网络层测试（V2-执行③）—— Qt Test 框架
 *
 * 这一层不再只测"字节变换"（那已由 tst_faultinjector 覆盖），而是走真实
 * TCP 回环，验证 SimulatorDevice 作为"会出错的测试设备"整体行为：
 *
 *   1. start(0) → 随机端口监听成功，port() 返回实际端口；
 *   2. frameGenerated 信号周期性发射（定时器驱动）；
 *   3. 真实客户端连上 → clientCount 上报；
 *   4. Normal 模式：客户端收到的字节流能解析出合法数据帧（端到端）；
 *   5. Error+CrcError 模式：客户端收到的帧被解析器拒绝（异常注入生效）。
 *
 * 教学点（对照 C#）：这是"集成测试"的雏形 —— 不 mock 网络，直接起一个
 * 真实 TCP 服务端 + 真实客户端，验证两端协议契约；SimulatorDevice 起的是
 * "测试替身（Test Double）"里 fake + fault-injection 的角色。
 */

#include <QtTest>
#include <QTcpSocket>
#include <QSignalSpy>

#include "simulator/simulatordevice.h"
#include "protocol/frameparser.h"
#include "protocol/protocoltypes.h"

using namespace datascope::simulator;
using namespace datascope::protocol;

class TestSimulatorDevice : public QObject
{
    Q_OBJECT

private slots:
    // ---- 生命周期：随机端口 ----
    void startWithRandomPort();

    // ---- 定时器驱动：frameGenerated 周期性发射 ----
    void emitsFramesPeriodically();

    // ---- 客户端接入：clientCount 上报 ----
    void reportsClientCount();

    // ---- 正常模式：端到端能解析出合法帧 ----
    void normalModeDeliversValidFrames();

    // ---- 异常模式：CRC 错误注入端到端生效 ----
    void errorModeCrcFramesRejected();

    // ---- 设备忙模式：忙窗口内不输出数据，但连接保持 ----
    void busyModeSuppressesFrames();

    // ---- 断线注入：触发周期内主动断开客户端 ----
    void disconnectFaultDropsClient();
};

// ================= 生命周期：随机端口 =================

void TestSimulatorDevice::startWithRandomPort()
{
    SimulatorDevice dev;
    QVERIFY(dev.start(0));          // 0 = 系统分配随机端口
    QVERIFY(dev.port() > 0);        // 分配成功必须给出实际端口
    QVERIFY(dev.clientCount() == 0); // 初始无客户端
}

// ================= 定时器驱动 =================

void TestSimulatorDevice::emitsFramesPeriodically()
{
    SimulatorDevice dev;
    QVERIFY(dev.start(0));

    QSignalSpy spy(&dev, &SimulatorDevice::frameGenerated);
    QVERIFY(spy.isValid());

    // 帧间隔默认 50ms：等 5 帧（最多 3 秒）—— 定时器确实在周期工作
    QTRY_VERIFY_WITH_TIMEOUT(spy.count() >= 5, 3000);

    // 每帧数据都是"非空完整帧"（≥ 帧头+CRC 最小 8 字节）
    for (int i = 0; i < spy.count(); ++i)
        QVERIFY(spy.at(i).at(1).toByteArray().size() >= kHeaderLen + kCrcLen);
}

// ================= 客户端接入 =================

void TestSimulatorDevice::reportsClientCount()
{
    SimulatorDevice dev;
    QVERIFY(dev.start(0));

    QTcpSocket client;
    client.connectToHost(QHostAddress::LocalHost, dev.port());
    QTRY_COMPARE_WITH_TIMEOUT(client.state(), QAbstractSocket::ConnectedState, 3000);

    // 客户端接入后，服务端计数必须同步为 1
    QTRY_COMPARE_WITH_TIMEOUT(dev.clientCount(), 1, 3000);

    client.disconnectFromHost();
    QTRY_COMPARE_WITH_TIMEOUT(dev.clientCount(), 0, 3000);
}

// ================= 正常模式：端到端合法帧 =================

void TestSimulatorDevice::normalModeDeliversValidFrames()
{
    SimulatorDevice dev;
    QVERIFY(dev.start(0));   // 默认 Normal 模式

    // 关键顺序：先挂 readyRead，再 connect —— 否则"数据先到、信号已发、
    // 后挂的 lambda 收不到首批数据"的竞态会让解析器永远拿不到内容。
    QTcpSocket client;
    FrameParser parser;
    int receivedBytes = 0;
    QObject::connect(&client, &QTcpSocket::readyRead, [&]() {
        const QByteArray data = client.readAll();
        receivedBytes += data.size();
        parser.feed(data);
    });

    client.connectToHost(QHostAddress::LocalHost, dev.port());
    QTRY_COMPARE_WITH_TIMEOUT(client.state(), QAbstractSocket::ConnectedState, 3000);
    QTRY_COMPARE_WITH_TIMEOUT(dev.clientCount(), 1, 3000);

    // 用服务端 frameGenerated 信号同步：等服务端已发出 ≥3 帧，再留网络到达窗口。
    // 这样才真正等到数据流经 TCP 到客户端（QTRY_VERIFY 反复调 nextFrame 无法推进
    // readyRead 与数据到达之间的时序，且失败即跳出、诊断不可见）。
    QSignalSpy spy(&dev, &SimulatorDevice::frameGenerated);
    QTRY_VERIFY_WITH_TIMEOUT(spy.count() >= 3, 3000);
    QTest::qWait(200);   // 让已发帧从服务端 socket 传到客户端 socket

    qInfo().noquote() << "[normalMode] receivedBytes=" << receivedBytes
                      << " buffered=" << parser.bufferedBytes();

    ProtocolFrame frame;
    QVERIFY2(parser.nextFrame(frame),
             qPrintable(QString("取帧失败: receivedBytes=%1 buffered=%2")
                            .arg(receivedBytes).arg(parser.bufferedBytes())));
    QVERIFY(frame.isValid);          // 正常模式：帧校验必须通过
    QCOMPARE(frame.func, quint8(0x01)); // 采集功能码
    QCOMPARE(frame.payload.size(), 16); // 4 通道 × 4 字节
}

// ================= 异常模式：CRC 错误注入生效 =================

void TestSimulatorDevice::errorModeCrcFramesRejected()
{
    SimulatorDevice dev;
    dev.setMode(SimulatorMode::Error);
    dev.setFaultConfig({ FaultType::CrcError });
    QVERIFY(dev.start(0));

    QTcpSocket client;
    client.connectToHost(QHostAddress::LocalHost, dev.port());
    QTRY_COMPARE_WITH_TIMEOUT(client.state(), QAbstractSocket::ConnectedState, 3000);

    // 收集客户端字节并喂给真实解析器
    FrameParser parser;
    QObject::connect(&client, &QTcpSocket::readyRead, [&]() {
        parser.feed(client.readAll());
    });

    // 用 frameGenerated 信号同步：等服务端至少发出了 3 帧（都被注入篡改），
    // 再断言客户端侧解析器从未取出过合法帧 —— 注入确实端到端生效。
    QSignalSpy spy(&dev, &SimulatorDevice::frameGenerated);
    QTRY_VERIFY_WITH_TIMEOUT(spy.count() >= 3, 3000);
    QTest::qWait(150);   // 留出最后一帧从网络到达客户端的窗口

    ProtocolFrame out;
    QVERIFY(!parser.nextFrame(out));   // 始终拿不到合法帧 → CRC 注入生效
}

// ================= 设备忙模式：忙窗口静默 =================

void TestSimulatorDevice::busyModeSuppressesFrames()
{
    SimulatorDevice dev;
    dev.setMode(SimulatorMode::Busy);
    dev.setFrameInterval(10);   // 10ms/帧：1.2s ≈ 120 帧，覆盖 2 个完整忙周期
    QVERIFY(dev.start(0));

    // 忙 ≠ 断线：连接必须能建立并保持
    QTcpSocket client;
    FrameParser parser;
    QObject::connect(&client, &QTcpSocket::readyRead, [&]() {
        parser.feed(client.readAll());
    });
    client.connectToHost(QHostAddress::LocalHost, dev.port());
    QTRY_COMPARE_WITH_TIMEOUT(client.state(), QAbstractSocket::ConnectedState, 3000);

    // 运行 1.2 秒，期间经历 2 个忙窗口（每 50 帧周期末尾 20 帧静默）
    QTest::qWait(1200);

    // 统计收到的合法帧数
    ProtocolFrame f;
    int receivedCount = 0;
    while (parser.nextFrame(f)) {
        if (f.isValid) ++receivedCount;
    }

    // 全速 120 帧本应收 ~120 帧；忙窗口使发送量降到 2 周期 × 30 帧 = 60 + 前 20 帧 = 80。
    // 断言：收到帧明显少于全发量（证明忙窗口在抑制输出），且链路确实通（>0）。
    QVERIFY2(receivedCount > 0, "busy 模式链路应正常，至少收到若干帧");
    QVERIFY2(receivedCount < 90,
             qPrintable(QString("忙窗口应显著减少帧数, 实际=%1").arg(receivedCount)));

    // 忙 ≠ 断线：连接必须仍然保持
    QCOMPARE(client.state(), QAbstractSocket::ConnectedState);
}

// ================= 断线注入 =================

void TestSimulatorDevice::disconnectFaultDropsClient()
{
    // 先正常启动：避免"注入断线"与"连接建立"的竞态（frameSeq=0 即断开，
    // 客户端可能连上瞬间就被断，无法先验证连接成功）。
    SimulatorDevice dev;
    QVERIFY(dev.start(0));

    QTcpSocket client;
    client.connectToHost(QHostAddress::LocalHost, dev.port());
    QTRY_COMPARE_WITH_TIMEOUT(client.state(), QAbstractSocket::ConnectedState, 3000);
    QTRY_COMPARE_WITH_TIMEOUT(dev.clientCount(), 1, 3000);

    // 连接稳定后，动态注入断线（每 2 帧断一次）——FaultConfig 是运行时读取，
    // 下一帧 onTick 即生效。这同时验证了"运行中改配置"能力。
    dev.setFaultConfig({ FaultType::Disconnect, /*period=*/2 });

    // 注入生效：客户端感知断线（Unconnected），服务端移除连接计数归零
    QTRY_COMPARE_WITH_TIMEOUT(client.state(), QAbstractSocket::UnconnectedState, 3000);
    QTRY_COMPARE_WITH_TIMEOUT(dev.clientCount(), 0, 3000);
}

// 生成 main()
QTEST_MAIN(TestSimulatorDevice)
#include "tst_simulatordevice.moc"
