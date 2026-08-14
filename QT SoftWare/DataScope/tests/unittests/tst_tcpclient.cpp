/**
 * @file tst_tcpclient.cpp
 * @brief TcpClient 单测（P11）—— Qt Test 框架
 *
 * 严格对照《05-TCP链路与模拟设备契约.md》第五节的 4 个强制项：
 *   1. 本地回环：测试内建 QTcpServer 监听 127.0.0.1:0 随机端口作"伪设备"，
 *      TcpClient 连接它，伪设备用 FrameBuilder 发帧 → frameReceived 收到该帧；
 *   2. 连不存在的端口 → errorOccurred 信号触发；
 *   3. 半包：伪设备分两段发一帧 → 仍能完整解析；
 *   4. 粘包：一帧 + 下一帧前半一起发 → 先收到第 1 帧；补齐剩余 → 两帧都收到。
 *
 * 教学点（对照 C#）：
 *   - QSignalSpy ≈ NUnit 里"订阅事件并断言事件是否触发 / 触发几次"；
 *   - QTest::qWait 临时跑事件循环，模拟"异步结果稍后才到"的真实网络场景；
 *   - 自定义类型参数（ProtocolFrame）必须先 Q_DECLARE_METATYPE + qRegisterMetaType，
 *     否则 QSignalSpy 无法把参数值拷贝出来（对应 C# 里事件参数类型安全由 CLR 保证，
 *     C++/Qt 需要显式登记元类型）。
 */

#include <QtTest>
#include <QSignalSpy>
#include <QTcpServer>
#include <QTcpSocket>
#include <QHostAddress>

#include "services/tcpclient.h"
#include "protocol/protocoltypes.h"
#include "protocol/framebuilder.h"

// 让 QVariant / QSignalSpy 能携带自定义类型 ProtocolFrame（必须放全局作用域）
Q_DECLARE_METATYPE(datascope::protocol::ProtocolFrame)

using namespace datascope::services;
using namespace datascope::protocol;

// 测试类：继承 QObject，槽函数即测试用例
class TestTcpClient : public QObject
{
    Q_OBJECT

private slots:
    void initTestCase();                    // 首个测试前的全局初始化
    void loopbackFrameReceived();           // 强制项 1：本地回环收发帧
    void connectFailureEmitsError();        // 强制项 2：连不存在的端口报错
    void halfPacketParsed();                // 强制项 3：半包解析
    void stickyPacketsWithNextHalf();       // 强制项 4：粘包 + 半包
};

void TestTcpClient::initTestCase()
{
    // ProtocolFrame 是自定义类型。QSignalSpy 构造时按信号参数的类型名去查元类型，
    // 若未登记会警告"Cannot handle parameter"且取不出参数值。
    // moc 对命名空间类型记录参数名的方式在不同场景下有差异，可能记成：
    //   裸名 "ProtocolFrame" / 全限定名 "datascope::protocol::ProtocolFrame" /
    //   相对名 "protocol::ProtocolFrame"。
    // 这里把三种写法都登记为同一个元类型 id 的别名，确保 QSignalSpy 无论按哪种
    // 名字查找都能命中（同一类型登记多个名字是无害的）。
    qRegisterMetaType<datascope::protocol::ProtocolFrame>("ProtocolFrame");
    qRegisterMetaType<datascope::protocol::ProtocolFrame>("protocol::ProtocolFrame");
    qRegisterMetaType<datascope::protocol::ProtocolFrame>("datascope::protocol::ProtocolFrame");
}

// ================= 强制项 1：本地回环 =================

void TestTcpClient::loopbackFrameReceived()
{
    // ---- 伪设备：QTcpServer 监听 127.0.0.1 随机端口 ----
    QTcpServer pseudoServer;
    QVERIFY(pseudoServer.listen(QHostAddress::LocalHost, 0));
    const quint16 port = pseudoServer.serverPort();
    QVERIFY(port != 0);

    TcpClient client;
    QSignalSpy connectedSpy(&client, &TcpClient::connected);
    QSignalSpy frameSpy(&client, &TcpClient::frameReceived);

    // 伪设备"接收连接"：连 newConnection 信号，把对端 socket 记下来。
    // 教学点：不能用同步的 waitForNewConnection —— 它等 newConnection 信号，
    // 而该信号可能已被 connectedSpy.wait() 的事件循环消费（Qt 信号一次性，
    // 发出时无接收者即丢弃），造成随机竞态失败。事件驱动（连信号 + QTRY 等）
    // 是唯一可靠姿势，与 tst_dataservice 里 startFakeServer 全绿模式一致。
    QTcpSocket *pseudo = nullptr;
    QObject::connect(&pseudoServer, &QTcpServer::newConnection, [&]() {
        pseudo = pseudoServer.nextPendingConnection();
    });

    // 客户端发起连接（异步）→ 等待 connected，再等伪设备完成接受
    client.connectToHost("127.0.0.1", port);
    QVERIFY2(connectedSpy.wait(1000), "TcpClient 应能连上本地伪设备");
    QTRY_VERIFY_WITH_TIMEOUT(pseudo != nullptr, 2000);

    // 伪设备组一帧并发给客户端
    const QByteArray payload("ABCD", 4);
    const QByteArray frame = FrameBuilder::build(0x01, 0x01, payload);
    pseudo->write(frame);
    pseudo->flush();

    // 等待客户端解析出帧
    QVERIFY2(frameSpy.wait(1000), "收到完整帧后 frameReceived 应被触发");
    QCOMPARE(frameSpy.count(), 1);

    // 校验帧内容与契约字段
    const ProtocolFrame received = frameSpy.at(0).at(0).value<ProtocolFrame>();
    QCOMPARE(received.func, quint8(0x01));
    QCOMPARE(received.cmd, quint8(0x01));
    QCOMPARE(received.payload, payload);
    QVERIFY(received.isValid);
}

// ================= 强制项 2：连不存在的端口 =================

void TestTcpClient::connectFailureEmitsError()
{
    // 找一个"此刻确定无监听"的端口：先 listen 验证空闲，再释放后立即连接。
    // 实测结论（本机 Windows 回环）：
    //   - "绑定-释放法"：Windows 对刚释放的 LISTEN 端口约 2 秒后才回 RST
    //     (ConnectionRefused)——TCP 栈 SYN 重传机制导致，故等待给足 5 秒；
    //   - "相邻端口法"（listen 存活端口 + 1）：不可靠！系统动态端口可能恰好
    //     被占用（实测 +1 端口被别的服务监听），连接会"成功"，error 永不触发，
    //     造成随机 flaky 失败。故本测试回到"验证空闲"法。
    quint16 rejectPort = 0;
    for (int attempt = 0; attempt < 8 && rejectPort == 0; ++attempt) {
        QTcpServer verify;                    // 仅用于"探测空闲"的临时服务器
        if (verify.listen(QHostAddress::LocalHost, 0)) {
            rejectPort = verify.serverPort(); // 系统分配的随机端口=此刻空闲
            // 作用域结束 verify 析构 → 端口释放，随后立即发起连接
        }
    }
    QVERIFY2(rejectPort != 0, "未能找到验证空闲的端口，测试环境异常");

    TcpClient client;
    QSignalSpy errorSpy(&client, &TcpClient::errorOccurred);

    // 连接一个没有服务监听的端口 → 应触发 errorOccurred
    client.connectToHost(QStringLiteral("127.0.0.1"), rejectPort);
    QVERIFY2(errorSpy.wait(5000), "连接不存在的端口应触发 errorOccurred");
    QVERIFY(errorSpy.count() >= 1);

    // 错误信息已统一转 QString，应非空
    const QString message = errorSpy.at(0).at(0).toString();
    QVERIFY(!message.isEmpty());
}

// ================= 强制项 3：半包 =================

void TestTcpClient::halfPacketParsed()
{
    QTcpServer pseudoServer;
    QVERIFY(pseudoServer.listen(QHostAddress::LocalHost, 0));
    const quint16 port = pseudoServer.serverPort();

    TcpClient client;
    QSignalSpy connectedSpy(&client, &TcpClient::connected);
    QSignalSpy frameSpy(&client, &TcpClient::frameReceived);

    // 事件驱动接收连接（理由见 loopbackFrameReceived 注释：waitForNewConnection
    // 等一次性信号，竞态失败；连 newConnection 信号 + QTRY 等待才可靠）
    QTcpSocket *pseudo = nullptr;
    QObject::connect(&pseudoServer, &QTcpServer::newConnection, [&]() {
        pseudo = pseudoServer.nextPendingConnection();
    });
    client.connectToHost("127.0.0.1", port);
    QVERIFY(connectedSpy.wait(1000));
    QTRY_VERIFY_WITH_TIMEOUT(pseudo != nullptr, 2000);

    // 一帧（5 字节载荷 → 整帧 13 字节）拆成两段发送：前 5 字节 + 剩余 8 字节
    const QByteArray payload("hello", 5);
    const QByteArray frame = FrameBuilder::build(0x02, 0x00, payload);
    QCOMPARE(frame.size(), 13);

    // 第一段：仅 5 字节（连 6 字节帧头都不够）→ 不应产出帧
    pseudo->write(frame.left(5));
    pseudo->flush();
    QTest::qWait(100);                  // 给事件循环时间送达并处理第一段
    QCOMPARE(frameSpy.count(), 0);      // 半包不产出帧

    // 第二段：补齐剩余字节 → 应解析出完整帧
    pseudo->write(frame.mid(5));
    pseudo->flush();
    QVERIFY(frameSpy.wait(1000));
    QCOMPARE(frameSpy.count(), 1);

    const ProtocolFrame received = frameSpy.at(0).at(0).value<ProtocolFrame>();
    QCOMPARE(received.func, quint8(0x02));
    QCOMPARE(received.cmd, quint8(0x00));
    QCOMPARE(received.payload, payload);
    QVERIFY(received.isValid);
}

// ================= 强制项 4：粘包 + 半包 =================

void TestTcpClient::stickyPacketsWithNextHalf()
{
    QTcpServer pseudoServer;
    QVERIFY(pseudoServer.listen(QHostAddress::LocalHost, 0));
    const quint16 port = pseudoServer.serverPort();

    TcpClient client;
    QSignalSpy connectedSpy(&client, &TcpClient::connected);
    QSignalSpy frameSpy(&client, &TcpClient::frameReceived);

    // 事件驱动接收连接（理由见 loopbackFrameReceived 注释：waitForNewConnection
    // 等一次性信号，竞态失败；连 newConnection 信号 + QTRY 等待才可靠）
    QTcpSocket *pseudo = nullptr;
    QObject::connect(&pseudoServer, &QTcpServer::newConnection, [&]() {
        pseudo = pseudoServer.nextPendingConnection();
    });
    client.connectToHost("127.0.0.1", port);
    QVERIFY(connectedSpy.wait(1000));
    QTRY_VERIFY_WITH_TIMEOUT(pseudo != nullptr, 2000);

    // 帧1（2 字节载荷 → 10 字节）整帧 + 帧2 的前 6 字节（恰好是帧头）一起发
    const QByteArray f1 = FrameBuilder::build(0x01, 0x01, QByteArray("AA", 2));
    const QByteArray f2 = FrameBuilder::build(0x02, 0x02, QByteArray("BB", 2));
    QCOMPARE(f1.size(), 10);
    QCOMPARE(f2.size(), 10);

    const int f2HeaderLen = 6;   // SOF+FUNC+CMD+LEN，不含 DATA/CRC
    pseudo->write(f1 + f2.left(f2HeaderLen));
    pseudo->flush();
    QTest::qWait(100);

    // 帧1 应已完整解析；帧2 只到了帧头，属半包，不应产出
    QCOMPARE(frameSpy.count(), 1);

    // 补齐帧2 的剩余 4 字节（DATA+CRC）→ 两帧都应收到
    pseudo->write(f2.mid(f2HeaderLen));
    pseudo->flush();
    QVERIFY(frameSpy.wait(1000));
    QCOMPARE(frameSpy.count(), 2);

    // 逐帧校验内容与到达顺序
    const ProtocolFrame r1 = frameSpy.at(0).at(0).value<ProtocolFrame>();
    const ProtocolFrame r2 = frameSpy.at(1).at(0).value<ProtocolFrame>();
    QCOMPARE(r1.func, quint8(0x01));
    QCOMPARE(r1.cmd, quint8(0x01));
    QCOMPARE(r1.payload, QByteArray("AA", 2));
    QVERIFY(r1.isValid);

    QCOMPARE(r2.func, quint8(0x02));
    QCOMPARE(r2.cmd, quint8(0x02));
    QCOMPARE(r2.payload, QByteArray("BB", 2));
    QVERIFY(r2.isValid);
}

// 生成 main()：QTEST_MAIN 为测试类生成独立可执行文件入口
QTEST_MAIN(TestTcpClient)
#include "tst_tcpclient.moc"
