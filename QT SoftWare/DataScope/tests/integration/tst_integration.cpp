/**
 * @file tst_integration.cpp
 * @brief 集成测试层（V2-执行③：测试分层 Integration）—— Qt Test 框架
 *
 * 与前几层的分工：
 *   - Unit（tst_protocol/tst_faultinjector）   ：单类纯逻辑，不起网络；
 *   - Simulator（tst_simulatordevice）          ：模拟设备自身行为（含裸 socket 回环）；
 *   - **Integration（本文件）**                  ：真实产品组件组装 —— 模拟设备 +
 *     服务层 TcpClient + 协议解析 + 配置编解码，整条"设备→主机"链路一起跑。
 *
 * 覆盖的集成链路：
 *   1. 通道配置查询：TcpClient 发查询帧 → 模拟设备应答配置帧 → 解码出通道
 *      （名称/单位/量程）—— 这是"UI 通道元数据数据驱动"的链路根基；
 *   2. 正常采集：模拟设备广播数据帧 → TcpClient 逐帧解析出合法采集帧；
 *   3. 粘包异常注入：设备两帧合发 → TcpClient 仍能重组出全部合法帧（不丢帧）；
 *   4. CRC 异常注入：设备发坏帧 → TcpClient 全部丢弃（收不到合法帧）。
 *
 * 教学点（对照 C#）：这是集成测试（Integration Test）——不 mock，让真实组件
 * 协作跑通；相比单测更接近生产形态，能抓住"各自都对、连起来就错"的接口问题。
 */

#include <QtTest>
#include <QSignalSpy>

#include "simulator/simulatordevice.h"
#include "services/tcpclient.h"
#include "protocol/framebuilder.h"
#include "protocol/channelconfigcodec.h"

// 自定义类型作为信号参数：需登记元类型（原因见 tst_tcpclient 注释）
Q_DECLARE_METATYPE(datascope::protocol::ProtocolFrame)

using namespace datascope;
using namespace datascope::simulator;
using namespace datascope::services;
using namespace datascope::protocol;

class TestIntegration : public QObject
{
    Q_OBJECT

private slots:
    void initTestCase();                      // 全局：登记元类型
    void queryChannelConfigRoundTrip();       // 链路 1：查询 → 响应 → 解码
    void normalDataStreamDeliversFrames();    // 链路 2：正常采集
    void stickyFaultStillParsesAllFrames();   // 链路 3：粘包重组不丢帧
    void crcFaultDropsAllFrames();            // 链路 4：CRC 坏帧全丢弃
};

void TestIntegration::initTestCase()
{
    qRegisterMetaType<ProtocolFrame>("ProtocolFrame");
    qRegisterMetaType<ProtocolFrame>("protocol::ProtocolFrame");
    qRegisterMetaType<ProtocolFrame>("datascope::protocol::ProtocolFrame");
}

// ================= 链路 1：通道配置查询 =================

void TestIntegration::queryChannelConfigRoundTrip()
{
    // 起一台真实模拟设备（随机端口）
    SimulatorDevice dev;
    QVERIFY(dev.start(0));

    // 服务层 TcpClient 作为主机侧网络组件
    TcpClient client;
    QSignalSpy connectedSpy(&client, &TcpClient::connected);
    QSignalSpy frameSpy(&client, &TcpClient::frameReceived);

    client.connectToHost(QStringLiteral("127.0.0.1"), dev.port());
    QVERIFY2(connectedSpy.wait(2000), "主机应能连上模拟设备");

    // 构造并发送通道配置查询帧（FUNC=0x03, CMD=0x01, 空载荷）
    ProtocolFrame query;
    query.func = kFuncQuery;
    query.cmd  = kCmdQueryChannels;
    QVERIFY2(client.sendFrame(query), "查询帧应成功写出");

    // 等待设备应答（响应帧）
    QVERIFY2(frameSpy.wait(3000), "设备应应答通道配置帧");

    // 取出响应帧并解码
    const ProtocolFrame resp = frameSpy.at(0).at(0).value<ProtocolFrame>();
    QCOMPARE(resp.func, quint8(kFuncQueryResponse));   // 0x83
    QCOMPARE(resp.cmd,  quint8(kCmdQueryChannels));    // 0x01
    QVERIFY(resp.isValid);

    QVector<ChannelConfigInfo> channels;
    QVERIFY2(ChannelConfigCodec::decode(resp.payload, channels), "响应载荷应能解码");
    QCOMPARE(channels.size(), 4);                     // 默认 4 通道

    // 校验通道 0 的元数据（名称/单位/量程 —— 数据驱动渲染的依据）
    QCOMPARE(channels[0].name, QStringLiteral("温度"));
    QCOMPARE(channels[0].unit, QStringLiteral("℃"));
    QVERIFY(qAbs(channels[0].rangeMax - 100.0f) < 0.001f);
    QCOMPARE(channels[3].name, QStringLiteral("振动"));
    QVERIFY(qAbs(channels[3].rangeMax - 5.0f) < 0.001f);
}

// ================= 链路 2：正常采集 =================

void TestIntegration::normalDataStreamDeliversFrames()
{
    SimulatorDevice dev;
    dev.setFrameInterval(10);   // 加快节奏，缩短测试时间
    QVERIFY(dev.start(0));

    TcpClient client;
    QSignalSpy connectedSpy(&client, &TcpClient::connected);
    QSignalSpy frameSpy(&client, &TcpClient::frameReceived);

    client.connectToHost(QStringLiteral("127.0.0.1"), dev.port());
    QVERIFY(connectedSpy.wait(2000));

    // 用服务端 frameGenerated 信号同步：等设备发出 ≥5 帧后再断言主机收到
    QSignalSpy genSpy(&dev, &SimulatorDevice::frameGenerated);
    QTRY_VERIFY_WITH_TIMEOUT(genSpy.count() >= 5, 3000);
    QTest::qWait(200);   // 留网络到达窗口

    // 主机应逐帧解析出合法采集帧（设备广播 = 0x01/0x01 + 16 字节载荷）
    QVERIFY2(frameSpy.count() > 0, "正常模式下主机应收到采集帧");
    const ProtocolFrame first = frameSpy.at(0).at(0).value<ProtocolFrame>();
    QCOMPARE(first.func, quint8(0x01));
    QCOMPARE(first.cmd,  quint8(0x01));
    QCOMPARE(first.payload.size(), 16);   // 4 通道 × 4 字节 float
    QVERIFY(first.isValid);
}

// ================= 链路 3：粘包重组不丢帧 =================

void TestIntegration::stickyFaultStillParsesAllFrames()
{
    SimulatorDevice dev;
    dev.setMode(SimulatorMode::Error);
    dev.setFaultConfig({ FaultType::Sticky });
    dev.setFrameInterval(10);
    QVERIFY(dev.start(0));

    TcpClient client;
    QSignalSpy connectedSpy(&client, &TcpClient::connected);
    QSignalSpy frameSpy(&client, &TcpClient::frameReceived);

    client.connectToHost(QStringLiteral("127.0.0.1"), dev.port());
    QVERIFY(connectedSpy.wait(2000));

    // 粘包注入 = 每次 write 塞两帧；主机 TcpClient 必须重组出全部合法帧（不丢）
    QSignalSpy genSpy(&dev, &SimulatorDevice::frameGenerated);
    QTRY_VERIFY_WITH_TIMEOUT(genSpy.count() >= 5, 3000);
    QTest::qWait(200);

    QVERIFY2(frameSpy.count() >= 4, "粘包下主机应重组出至少 4 帧（5 次注入×2 = 10 帧）");
    for (int i = 0; i < frameSpy.count(); ++i) {
        const ProtocolFrame f = frameSpy.at(i).at(0).value<ProtocolFrame>();
        QVERIFY(f.isValid);   // 重组出的每一帧都必须校验通过
    }
}

// ================= 链路 4：CRC 坏帧全丢弃 =================

void TestIntegration::crcFaultDropsAllFrames()
{
    SimulatorDevice dev;
    dev.setMode(SimulatorMode::Error);
    dev.setFaultConfig({ FaultType::CrcError });
    dev.setFrameInterval(10);
    QVERIFY(dev.start(0));

    TcpClient client;
    QSignalSpy connectedSpy(&client, &TcpClient::connected);
    QSignalSpy frameSpy(&client, &TcpClient::frameReceived);

    client.connectToHost(QStringLiteral("127.0.0.1"), dev.port());
    QVERIFY(connectedSpy.wait(2000));

    // 设备持续发"篡改 CRC"的坏帧；主机 TcpClient 应把每一帧都丢弃
    QSignalSpy genSpy(&dev, &SimulatorDevice::frameGenerated);
    QTRY_VERIFY_WITH_TIMEOUT(genSpy.count() >= 8, 3000);
    QTest::qWait(200);

    QCOMPARE(frameSpy.count(), 0);   // 坏帧不产生活合法帧 → 主机不应收到任何帧
}

// 生成 main()
QTEST_MAIN(TestIntegration)
#include "tst_integration.moc"
