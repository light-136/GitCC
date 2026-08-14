/**
 * @file tst_domain.cpp
 * @brief 领域模型单元测试（Qt Test 框架）
 *
 * 覆盖契约《02-领域模型接口契约.md》第三节的 6 个强制项 + 1 个补充项：
 *   1. defaultState          —— 强制项①：默认状态 Disconnected、通道数 0；
 *   2. idName_roundTrip      —— 强制项②：id/name 读写往返；
 *   3. addChannel_content    —— 强制项③：addChannel 后通道数与内容一致（顺序、字段）；
 *   4. clearChannels         —— 强制项④：清空后通道数为 0；
 *   5. channel_valueSemantics—— 强制项⑤：Channel 值语义，改副本不影响原对象；
 *   6. dataPoint_defaults    —— 强制项⑥：DataPoint 默认值与赋值后正确；
 *   7. device_copyIndependent—— 补充项：Device 拷贝后互相独立（QVector 隐式共享验证）。
 *
 * P8 教学点：
 *   - 值语义：struct 整体拷贝得到"副本"，修改副本字段不触碰原对象
 *     （对应 C# 里拷贝一个 record/POCO 后各自独立）；
 *   - 隐式共享：拷贝 Device 很便宜（共享底层 QVector），但写时自动复制，
 *     因此副本的通道/状态改动不会泄漏到原对象——本测试恰好验证了这一点；
 *   - 默认值契约：struct 成员的默认初始化（int=0、double=1.0/0.0）与
 *     QDateTime 默认"无效时间"（isValid()==false）。
 *
 * 浮点断言说明：为避免二进制浮点误差，本测试只使用可精确表示的值
 * （0.0 / 1.0 / 1.5 / 2.0 / 0.5），QCOMPARE 即可可靠通过。
 */

#include <QtTest>

#include "domain/models.h"

using datascope::domain::Channel;
using datascope::domain::DataPoint;
using datascope::domain::Device;
using datascope::domain::DeviceStatus;

// 测试类：继承 QObject，每个 private slot 就是一个独立测试用例
class TestDomain : public QObject
{
    Q_OBJECT

private slots:
    void defaultState();           // 强制项① 默认状态
    void idName_roundTrip();       // 强制项② id/name 读写往返
    void addChannel_content();     // 强制项③ 通道数与内容
    void clearChannels();          // 强制项④ 清空通道
    void channel_valueSemantics(); // 强制项⑤ Channel 值语义
    void dataPoint_defaults();     // 强制项⑥ DataPoint 默认值/赋值
    void device_copyIndependent(); // 补充项   Device 拷贝独立性
};

// ================= ① 默认状态 =================

void TestDomain::defaultState()
{
    // 契约：新建 Device 必须处于"未连接、零通道"的初始状态
    Device dev;

    // status() 默认必须是 Disconnected（m_status 的类内初始化保证）
    QCOMPARE(dev.status(), DeviceStatus::Disconnected);
    // channelCount() 默认必须是 0（m_channels 为空）
    QCOMPARE(dev.channelCount(), 0);
    // channels() 返回的只读视图同样应为空
    QVERIFY(dev.channels().isEmpty());
}

// ================= ② id/name 读写往返 =================

void TestDomain::idName_roundTrip()
{
    Device dev;

    // 写入 -> 读回：验证 setter/getter 往返一致
    dev.setId(QStringLiteral("DEV-001"));
    dev.setName(QStringLiteral("温度采集仪"));
    QCOMPARE(dev.id(), QStringLiteral("DEV-001"));
    QCOMPARE(dev.name(), QStringLiteral("温度采集仪"));
}

// ================= ③ addChannel 通道数与内容 =================

void TestDomain::addChannel_content()
{
    Device dev;

    // 构造两路通道：1 号温度、2 号压力（含 scale/offset，用可精确表示的浮点值）
    Channel ch1;
    ch1.index = 0;
    ch1.name  = QStringLiteral("温度");
    ch1.unit  = QStringLiteral("℃");

    Channel ch2;
    ch2.index  = 1;
    ch2.name   = QStringLiteral("压力");
    ch2.unit   = QStringLiteral("kPa");
    ch2.scale  = 2.0;   // 精确可表示
    ch2.offset = 1.5;   // 精确可表示

    dev.addChannel(ch1);
    dev.addChannel(ch2);

    // 通道数量：加入 2 路 -> channelCount == 2
    QCOMPARE(dev.channelCount(), 2);
    QCOMPARE(dev.channels().size(), 2);

    // 通道顺序与内容必须与加入时完全一致（append 保序）
    QCOMPARE(dev.channels().at(0).index, 0);
    QCOMPARE(dev.channels().at(0).name, QStringLiteral("温度"));
    QCOMPARE(dev.channels().at(0).unit, QStringLiteral("℃"));
    QCOMPARE(dev.channels().at(0).scale, 1.0);   // 未设置 -> 保持默认 1.0
    QCOMPARE(dev.channels().at(0).offset, 0.0);  // 未设置 -> 保持默认 0.0

    QCOMPARE(dev.channels().at(1).index, 1);
    QCOMPARE(dev.channels().at(1).name, QStringLiteral("压力"));
    QCOMPARE(dev.channels().at(1).unit, QStringLiteral("kPa"));
    QCOMPARE(dev.channels().at(1).scale, 2.0);
    QCOMPARE(dev.channels().at(1).offset, 1.5);
}

// ================= ④ clearChannels =================

void TestDomain::clearChannels()
{
    Device dev;

    // 先塞入一路，确认计数为 1
    Channel ch;
    ch.index = 0;
    ch.name  = QStringLiteral("振动");
    dev.addChannel(ch);
    QCOMPARE(dev.channelCount(), 1);

    // 清空后：数量归零、只读视图为空
    dev.clearChannels();
    QCOMPARE(dev.channelCount(), 0);
    QVERIFY(dev.channels().isEmpty());
}

// ================= ⑤ Channel 值语义 =================

void TestDomain::channel_valueSemantics()
{
    // 原通道：offset = 0.5（精确可表示）
    Channel original;
    original.index  = 3;
    original.name   = QStringLiteral("流量");
    original.offset = 0.5;

    // 整体拷贝：struct 是值类型，拷贝得到一份独立副本
    Channel copy = original;

    // 修改副本：改 offset、改 name
    copy.offset = 1.0;
    copy.name   = QStringLiteral("改动后的通道名");

    // 副本自身的修改生效
    QCOMPARE(copy.offset, 1.0);
    QCOMPARE(copy.name, QStringLiteral("改动后的通道名"));

    // 原对象不受影响（值语义核心断言）
    QCOMPARE(original.offset, 0.5);
    QCOMPARE(original.name, QStringLiteral("流量"));
}

// ================= ⑥ DataPoint 默认值与赋值 =================

void TestDomain::dataPoint_defaults()
{
    // 默认构造：channelIndex == 0、value == 0.0、timestamp 无效
    DataPoint dp;
    QCOMPARE(dp.channelIndex, 0);
    QCOMPARE(dp.value, 0.0);
    QVERIFY(!dp.timestamp.isValid()); // QDateTime 默认构造是"无效时间"

    // 赋值后：所有字段正确保存
    dp.channelIndex = 2;
    dp.value        = 3.25;
    dp.timestamp    = QDateTime(QDate(2026, 8, 14), QTime(12, 30, 45));

    QCOMPARE(dp.channelIndex, 2);
    QCOMPARE(dp.value, 3.25);
    QVERIFY(dp.timestamp.isValid());
    QCOMPARE(dp.timestamp,
             QDateTime(QDate(2026, 8, 14), QTime(12, 30, 45)));
}

// ================= ⑦ 补充：Device 拷贝独立性 =================

void TestDomain::device_copyIndependent()
{
    Device original;
    original.setId(QStringLiteral("DEV-002"));
    original.setName(QStringLiteral("原设备"));
    original.setStatus(DeviceStatus::Connected);

    Channel ch;
    ch.index = 0;
    ch.name  = QStringLiteral("电压");
    original.addChannel(ch);

    // 拷贝一份：QVector 隐式共享，拷贝很便宜，且副本独立
    Device copy = original;

    // 修改副本：改状态、改名、再加一路通道
    copy.setStatus(DeviceStatus::Error);
    copy.setName(QStringLiteral("副本设备"));
    Channel ch2;
    ch2.index = 1;
    ch2.name  = QStringLiteral("电流");
    copy.addChannel(ch2);

    // 原对象保持不受影响（补充项核心断言）
    QCOMPARE(original.status(), DeviceStatus::Connected);
    QCOMPARE(original.name(), QStringLiteral("原设备"));
    QCOMPARE(original.channelCount(), 1);  // 副本加通道不影响原对象的通道数组

    // 副本自身的修改生效
    QCOMPARE(copy.channelCount(), 2);
    QCOMPARE(copy.name(), QStringLiteral("副本设备"));
}

// 生成 main()：QTEST_MAIN 为测试类生成独立可执行文件入口
QTEST_MAIN(TestDomain)
#include "tst_domain.moc"
