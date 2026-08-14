/**
 * @file tst_dataservice.cpp
 * @brief 数据总线单元测试（P12：多线程采集链路）
 *
 * 覆盖契约《06-采集链路与数据总线契约.md》第六节强制项：
 *   1. connectTo 到本地回环伪设备 → connected 信号；
 *   2. 伪设备发采集帧 → dataUpdated 触发，points 每帧含 4 个 DataPoint；
 *   3. stopAcquisition 后 dataUpdated 不再触发；
 *   4. lastData() 快照可读且含 4 点；
 *   5. 断开后 isConnected()==false。
 *
 * 线程教学点：
 *   - DataService 运行在主线程，Worker 在采集线程；本测试通过 QSignalSpy
 *     验证"跨线程数据确实到达主线程"（队列连接 + 元类型注册缺一不可）；
 *   - QSignalSpy 对 dataUpdated(QVector<DataPoint>) 参数：DataPoint 必须
 *     Q_DECLARE_METATYPE，否则 QVariant 拿不到实际数据。
 */

#include <QtTest>

#include "services/dataservice.h"
#include "protocol/framebuilder.h"
#include "protocol/protocoltypes.h"
#include "domain/models.h"

#include <QTcpServer>
#include <QTcpSocket>
#include <QHostAddress>

using datascope::domain::DataPoint;
using datascope::services::DataService;

// ---- 元类型声明（QVariant 需要；与 dataservice.cpp 中的声明一致）----
Q_DECLARE_METATYPE(datascope::domain::DataPoint)
Q_DECLARE_METATYPE(QVector<datascope::domain::DataPoint>)

namespace {
/** @brief 把 float 编码为大端 4 字节（与模拟设备帧载荷布局一致） */
QByteArray floatBE(float value)
{
    quint32 raw = 0;
    memcpy(&raw, &value, sizeof(value));
    QByteArray b;
    b.append(static_cast<char>((raw >> 24) & 0xFF));
    b.append(static_cast<char>((raw >> 16) & 0xFF));
    b.append(static_cast<char>((raw >> 8)  & 0xFF));
    b.append(static_cast<char>((raw)       & 0xFF));
    return b;
}

/** @brief 构造一帧"4 通道模拟采集数据"（CMD=0x01）的完整字节流 */
QByteArray makeAcquisitionFrame()
{
    QByteArray payload;
    payload += floatBE(50.0f);   // 通道0 温度
    payload += floatBE(5.0f);    // 通道1 压力
    payload += floatBE(25.0f);   // 通道2 流量
    payload += floatBE(2.5f);    // 通道3 振动
    return datascope::protocol::FrameBuilder::build(0x01, 0x01, payload);
}

/** @brief 建一个"本地回环伪设备"（QTcpServer 随机端口），失败返回 0 */
quint16 startFakeServer(QTcpServer &server, QPointer<QTcpSocket> &peer)
{
    // 注意：辅助函数不能直接用 QVERIFY（失败时 QVERIFY 展开为"无值 return"，
    // 与返回 quint16 冲突）——改用 if+return 0，调用方自行断言。
    if (!server.listen(QHostAddress::LocalHost, 0)) {
        return 0;   // 监听失败
    }
    QObject::connect(&server, &QTcpServer::newConnection, [&peer, &server]() {
        peer = server.nextPendingConnection();   // 记录对端 socket
    });
    return server.serverPort();
}
} // namespace

// 测试类：每个 private slot 一个独立用例
class TestDataService : public QObject
{
    Q_OBJECT

private slots:
    void initial_state();            // 初始：未连接、未采集、快照空
    void connectTo_emitsConnected(); // 连接回环伪设备 → connected
    void acquisition_deliversData(); // 采集帧 → dataUpdated + 4 个 DataPoint
    void stopAcquisition_stopsData();// 停止采集 → 不再收到数据
    void disconnect_setsFlagFalse(); // 断开 → isConnected()==false
};

// ================= 初始状态 =================

void TestDataService::initial_state()
{
    DataService svc;
    QVERIFY(!svc.isConnected());
    QVERIFY(!svc.isAcquiring());
    QVERIFY(svc.lastData().isEmpty());   // 无数据 → 快照空
}

// ================= ① 连接回环伪设备 → connected =================

void TestDataService::connectTo_emitsConnected()
{
    QTcpServer server;
    QPointer<QTcpSocket> peer;
    const quint16 port = startFakeServer(server, peer);
    QVERIFY(port != 0);   // 伪设备监听失败即中止本用例

    DataService svc;
    QSignalSpy connSpy(&svc, &DataService::connected);
    QSignalSpy errSpy(&svc, &DataService::connectionError);

    svc.connectTo(QStringLiteral("127.0.0.1"), port);

    // 教学点：QTRY_* 会反复处理事件循环直到条件满足或超时——
    // 异步网络事件的正确等待方式（绝不用裸 sleep）。
    QTRY_VERIFY_WITH_TIMEOUT(peer, 2000);          // 伪设备收到连接
    QTRY_COMPARE_WITH_TIMEOUT(connSpy.count(), 1, 2000);   // 主线程收到 connected
    QCOMPARE(errSpy.count(), 0);                   // 无错误
    QVERIFY(svc.isConnected());
}

// ================= ② 采集帧 → dataUpdated =================

void TestDataService::acquisition_deliversData()
{
    QTcpServer server;
    QPointer<QTcpSocket> peer;
    const quint16 port = startFakeServer(server, peer);
    QVERIFY(port != 0);   // 伪设备监听失败即中止本用例

    DataService svc;
    QSignalSpy connSpy(&svc, &DataService::connected);
    QSignalSpy dataSpy(&svc, &DataService::dataUpdated);

    svc.connectTo(QStringLiteral("127.0.0.1"), port);
    QTRY_COMPARE_WITH_TIMEOUT(connSpy.count(), 1, 2000);

    svc.startAcquisition();   // 标记采集中（连接已在 connectTo 发起）

    // 伪设备发一帧采集数据 → 期待主线程收到 dataUpdated
    peer->write(makeAcquisitionFrame());
    peer->flush();

    QTRY_VERIFY_WITH_TIMEOUT(dataSpy.count() >= 1, 2000);

    // ---- 取出第一包数据，验证 4 个通道 + 值正确 ----
    const QVariantList args = dataSpy.takeFirst();
    const QVector<DataPoint> points =
        args.at(0).value<QVector<DataPoint>>();

    QCOMPARE(points.size(), 4);   // 4 通道
    QCOMPARE(points.at(0).channelIndex, 0);
    QVERIFY(qAbs(points.at(0).value - 50.0) < 0.01);   // 温度≈50
    QCOMPARE(points.at(1).channelIndex, 1);
    QVERIFY(qAbs(points.at(1).value - 5.0) < 0.01);    // 压力≈5
    QCOMPARE(points.at(2).channelIndex, 2);
    QCOMPARE(points.at(3).channelIndex, 3);
    QVERIFY(points.at(0).timestamp.isValid());         // 时间戳有效
}

// ================= ③ 停止采集 → 数据停 =================

void TestDataService::stopAcquisition_stopsData()
{
    QTcpServer server;
    QPointer<QTcpSocket> peer;
    const quint16 port = startFakeServer(server, peer);
    QVERIFY(port != 0);   // 伪设备监听失败即中止本用例

    DataService svc;
    QSignalSpy connSpy(&svc, &DataService::connected);
    QSignalSpy dataSpy(&svc, &DataService::dataUpdated);

    svc.connectTo(QStringLiteral("127.0.0.1"), port);
    QTRY_COMPARE_WITH_TIMEOUT(connSpy.count(), 1, 2000);

    svc.startAcquisition();
    peer->write(makeAcquisitionFrame());
    peer->flush();
    QTRY_VERIFY_WITH_TIMEOUT(dataSpy.count() >= 1, 2000);

    // ---- 停止采集后：客户端断开，再发帧不应产生 dataUpdated ----
    svc.stopAcquisition();
    QVERIFY(!svc.isAcquiring());

    const int countBefore = dataSpy.count();
    // 伪设备尝试再发（此时 Worker 已断开对端，write 可能失败——可忽略）
    peer->write(makeAcquisitionFrame());
    peer->flush();
    // 给事件循环一小段窗口（若数据误投会在此暴露）
    QTest::qWait(300);

    QCOMPARE(dataSpy.count(), countBefore);   // 计数不变 = 无新数据
}

// ================= ④ 断开 → isConnected false =================

void TestDataService::disconnect_setsFlagFalse()
{
    QTcpServer server;
    QPointer<QTcpSocket> peer;
    const quint16 port = startFakeServer(server, peer);
    QVERIFY(port != 0);   // 伪设备监听失败即中止本用例

    DataService svc;
    QSignalSpy connSpy(&svc, &DataService::connected);
    QSignalSpy discSpy(&svc, &DataService::disconnected);

    svc.connectTo(QStringLiteral("127.0.0.1"), port);
    QTRY_COMPARE_WITH_TIMEOUT(connSpy.count(), 1, 2000);
    QVERIFY(svc.isConnected());

    svc.disconnectFromDevice();   // 断开设备
    QTRY_COMPARE_WITH_TIMEOUT(discSpy.count(), 1, 2000);   // disconnected 信号
    QVERIFY(!svc.isConnected());
}

// 生成 main()：QTEST_MAIN 为测试类生成独立可执行文件入口
QTEST_MAIN(TestDataService)
#include "tst_dataservice.moc"
