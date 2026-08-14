/**
 * @file tst_devicecontroller.cpp
 * @brief 设备状态机控制器单测（P13）—— Qt Test 框架
 *
 * 严格对照契约《07-设备状态机契约.md》第五节 6 个强制项：
 *   1. 初始状态 Disconnected；
 *   2. connectDevice → 回环伪设备 → 状态经 Connecting→Connected；
 *   3. 连不存在端口 → Error + errorOccurred；
 *   4. disconnectDevice → Disconnected；
 *   5. Error 态自动重连：伪设备先拒后收 → 最终 Connected（退避重试）；
 *   6. 采集数据流转发：connectDevice + startAcquisition → dataUpdated 触发。
 *
 * 教学点（对照 C#）：
 *   - QSignalSpy 监听 statusChanged：断言"状态机确实经过中间态"（对应
 *     INotifyPropertyChanged 的断点观察）；
 *   - QTRY_* 宏处理异步网络/重连定时器：绝不裸 sleep（重连退避 1s，等待给足）。
 */

#include <QtTest>

#include "services/devicecontroller.h"
#include "protocol/framebuilder.h"
#include "domain/models.h"

#include <QTcpServer>
#include <QTcpSocket>
#include <QHostAddress>
#include <QPointer>

using datascope::domain::DeviceStatus;
using datascope::services::DeviceController;

// ---- 元类型声明：QVariant / QSignalSpy 携带 DataPoint 需要（与 dataservice 一致）----
// DeviceStatus 是命名空间内 enum class：QSignalSpy 记录 statusChanged 参数时必须
// 注册元类型，否则警告"Unable to handle parameter"且参数取值为空。
Q_DECLARE_METATYPE(datascope::domain::DeviceStatus)
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
    // 辅助函数不用 QVERIFY（失败展开为无值 return，与返回 quint16 冲突）
    if (!server.listen(QHostAddress::LocalHost, 0)) {
        return 0;
    }
    QObject::connect(&server, &QTcpServer::newConnection, [&peer, &server]() {
        peer = server.nextPendingConnection();   // 记录对端 socket
    });
    return server.serverPort();
}
} // namespace

// 测试类：每个 private slot 一个独立用例
class TestDeviceController : public QObject
{
    Q_OBJECT

private slots:
    void initTestCase();               // 全局初始化：注册元类型
    void initial_state();              // 强制项 1：初始 Disconnected
    void connectTo_emitsStatusTransitions();   // 强制项 2：Connecting→Connected
    void connectFailure_goesError();   // 强制项 3：连不上 → Error
    void disconnect_goesDisconnected();// 强制项 4：主动断开 → Disconnected
    void autoReconnectAfterFailure();  // 强制项 5：先拒后收 → 自动重连成功
    void acquisitionForwardsData();    // 强制项 6：采集数据流转发
};

void TestDeviceController::initTestCase()
{
    // dataUpdated(QVector<DataPoint>) 参数跨 QSignalSpy 拷贝需元类型。
    // DeviceStatus 同理（enum class）：moc 对命名空间类型的规范化名在不同场景
    // 可能是 裸名/相对名/全限定名，把三种都登记为别名，任何写法都能命中（无害）。
    qRegisterMetaType<datascope::domain::DeviceStatus>("DeviceStatus");
    qRegisterMetaType<datascope::domain::DeviceStatus>("domain::DeviceStatus");
    qRegisterMetaType<datascope::domain::DeviceStatus>("datascope::domain::DeviceStatus");
    qRegisterMetaType<datascope::domain::DataPoint>("datascope::domain::DataPoint");
    qRegisterMetaType<QVector<datascope::domain::DataPoint>>("QVector<datascope::domain::DataPoint>");
}

// ================= 强制项 1：初始状态 =================

void TestDeviceController::initial_state()
{
    DeviceController ctl;
    QCOMPARE(ctl.status(), DeviceStatus::Disconnected);   // 创建即未连接
}

// ================= 强制项 2：连接回环伪设备 → 状态经 Connecting→Connected =================

void TestDeviceController::connectTo_emitsStatusTransitions()
{
    QTcpServer server;
    QPointer<QTcpSocket> peer;
    const quint16 port = startFakeServer(server, peer);
    QVERIFY(port != 0);

    DeviceController ctl;
    QSignalSpy statusSpy(&ctl, &DeviceController::statusChanged);

    ctl.connectDevice(QStringLiteral("127.0.0.1"), port);

    // 等最终到达 Connected（连接是异步的，走 QTRY 等待）
    QTRY_COMPARE_WITH_TIMEOUT(ctl.status(), DeviceStatus::Connected, 3000);

    // 断言状态转移序列里同时出现过 Connecting 与 Connected（状态机"走位"正确）
    bool sawConnecting = false;
    bool sawConnected  = false;
    for (const QVariantList &args : statusSpy) {
        const auto s = static_cast<DeviceStatus>(args.at(0).toInt());
        if (s == DeviceStatus::Connecting) sawConnecting = true;
        if (s == DeviceStatus::Connected)  sawConnected  = true;
    }
    QVERIFY(sawConnecting);   // 必须经过 Connecting 中间态
    QVERIFY(sawConnected);
}

// ================= 强制项 3：连不存在端口 → Error + errorOccurred =================

void TestDeviceController::connectFailure_goesError()
{
    // 找"此刻确定无监听"的端口（理由见 tst_tcpclient::connectFailureEmitsError 注释：
    // 相邻端口法会撞上活跃监听，绑定-释放法 Windows 有约 2s 拒绝延迟，等待给足）
    quint16 rejectPort = 0;
    for (int attempt = 0; attempt < 8 && rejectPort == 0; ++attempt) {
        QTcpServer verify;
        if (verify.listen(QHostAddress::LocalHost, 0)) {
            rejectPort = verify.serverPort();
        }
    }
    QVERIFY(rejectPort != 0);

    DeviceController ctl;
    QSignalSpy errSpy(&ctl, &DeviceController::errorOccurred);

    ctl.connectDevice(QStringLiteral("127.0.0.1"), rejectPort);
    // 等待放 6s：Windows 对"刚释放的 LISTEN 端口"约 2s 才回 RST（见 tst_tcpclient
    // 注释），叠加线程投递开销，实测最快 ~3.3s 到达 Error；3s 曾超时。
    QTRY_VERIFY_WITH_TIMEOUT(ctl.status() == DeviceStatus::Error, 6000);
    QVERIFY(errSpy.count() >= 1);   // 错误信息已透传
}

// ================= 强制项 4：主动断开 → Disconnected =================

void TestDeviceController::disconnect_goesDisconnected()
{
    QTcpServer server;
    QPointer<QTcpSocket> peer;
    const quint16 port = startFakeServer(server, peer);
    QVERIFY(port != 0);

    DeviceController ctl;
    ctl.connectDevice(QStringLiteral("127.0.0.1"), port);
    QTRY_COMPARE_WITH_TIMEOUT(ctl.status(), DeviceStatus::Connected, 3000);

    ctl.disconnectDevice();   // 用户主动断开
    QCOMPARE(ctl.status(), DeviceStatus::Disconnected);   // 立即回 Disconnected
}

// ================= 强制项 5：先拒后收 → 自动重连成功（退避重试） =================

void TestDeviceController::autoReconnectAfterFailure()
{
    // 伪设备行为：第一次连接立即 abort（RST，模拟"设备未就绪"），之后正常接受
    QTcpServer server;
    QVERIFY(server.listen(QHostAddress::LocalHost, 0));
    QPointer<QTcpSocket> peer;
    int connCount = 0;
    QObject::connect(&server, &QTcpServer::newConnection, [&]() {
        ++connCount;
        QTcpSocket *s = server.nextPendingConnection();
        if (connCount == 1) {
            s->abort();        // 第一次：强制 RST → 客户端连接失败
            s->deleteLater();
        } else {
            peer = s;          // 之后：正常接受 → 重连成功
        }
    });

    DeviceController ctl;
    QSignalSpy errSpy(&ctl, &DeviceController::errorOccurred);

    ctl.connectDevice(QStringLiteral("127.0.0.1"), server.serverPort());

    // 第一次连接被拒 → Error + errorOccurred
    QTRY_VERIFY_WITH_TIMEOUT(errSpy.count() >= 1, 3000);
    // 退避重试（初始 1s）→ 第二次成功 → 最终 Connected
    QTRY_VERIFY_WITH_TIMEOUT(ctl.status() == DeviceStatus::Connected, 6000);
    QVERIFY(connCount >= 2);   // 伪设备确实被连接了至少两次
}

// ================= 强制项 6：采集数据流转发 =================

void TestDeviceController::acquisitionForwardsData()
{
    QTcpServer server;
    QPointer<QTcpSocket> peer;
    const quint16 port = startFakeServer(server, peer);
    QVERIFY(port != 0);

    DeviceController ctl;
    QSignalSpy dataSpy(&ctl, &DeviceController::dataUpdated);

    ctl.connectDevice(QStringLiteral("127.0.0.1"), port);
    QTRY_COMPARE_WITH_TIMEOUT(ctl.status(), DeviceStatus::Connected, 3000);
    ctl.startAcquisition();

    QVERIFY(peer != nullptr);   // 伪设备已接受连接
    peer->write(makeAcquisitionFrame());   // 伪设备发一帧采集数据
    peer->flush();

    QTRY_VERIFY_WITH_TIMEOUT(dataSpy.count() >= 1, 3000);

    const QVariantList args = dataSpy.takeFirst();
    const QVector<datascope::domain::DataPoint> points =
        args.at(0).value<QVector<datascope::domain::DataPoint>>();
    QCOMPARE(points.size(), 4);   // 4 通道全部转发
}

// 生成 main()：QTEST_MAIN 为测试类生成独立可执行文件入口
QTEST_MAIN(TestDeviceController)
#include "tst_devicecontroller.moc"
