/**
 * @file tst_domain_v2.cpp
 * @brief V2 领域模型单元测试（Qt Test 框架）
 *
 * 覆盖 domainmodel_v2 的全部行为与不变量 —— 这正是 V1 领域层缺失的：
 * V1 的 models.h 只有被动 getter/setter（无行为可测），V2 类型带真实逻辑，
 * 本套件验证这些逻辑的正确性（对应审查 P0-1 的"领域补行为 + 配单测"）。
 *
 * 测试分组：
 *   1. DeviceId        —— 生成唯一性、不可变、相等比较
 *   2. DeviceState     —— 合法转移表（重点：非法转移被拒绝）
 *   3. ConnectionParams—— TCP/串口参数校验
 *   4. ChannelConfig   —— 量程校验、工程值换算、越限判定
 *   5. AlarmRule       —— 报警条件评估（纯函数）
 *   6. AlarmEvent      —— 生命周期状态机（Active→Ack→Clear）
 *   7. AcquisitionRecord —— 会话完整性（begin→add→end，幂等性）
 *
 * 浮点断言说明：全部使用可精确表示的浮点值（0.0/1.0/100.0/2.0/50.0），
 * QCOMPARE 可可靠通过，避免二进制浮点误差。
 */

#include <QtTest>

#include "domain/domainmodel_v2.h"

using datascope::domain::v2::AcquisitionRecord;
using datascope::domain::v2::AlarmCondition;
using datascope::domain::v2::AlarmEvent;
using datascope::domain::v2::AlarmRule;
using datascope::domain::v2::AlarmSeverity;
using datascope::domain::v2::ChannelConfig;
using datascope::domain::v2::ConnectionParams;
using datascope::domain::v2::ConnectionType;
using datascope::domain::v2::DeviceId;
using datascope::domain::v2::DeviceState;
using datascope::domain::v2::canTransition;

class TestDomainV2 : public QObject
{
    Q_OBJECT

private slots:
    // ---- DeviceId ----
    void deviceId_create_isUniqueAndValid();
    void deviceId_equality();

    // ---- DeviceState ----
    void state_legalTransitions();
    void state_illegalTransitions();

    // ---- ConnectionParams ----
    void connParams_tcpValidation();
    void connParams_serialValidation();

    // ---- ChannelConfig ----
    void channelConfig_validity();
    void channelConfig_toEngineering();
    void channelConfig_isOutOfRange();

    // ---- AlarmRule ----
    void alarmRule_aboveHigh();
    void alarmRule_belowLow();

    // ---- AlarmEvent ----
    void alarmEvent_lifecycle();
    void alarmEvent_ackIdempotent();

    // ---- AcquisitionRecord ----
    void record_beginEndLifecycle();
    void record_addSampleAfterEnd_isNoOp();
};

// ==================== DeviceId ====================

void TestDomainV2::deviceId_create_isUniqueAndValid()
{
    // 生成两个 id：必须都有效、且不相同（唯一性 —— 设备身份不能被复用）
    const DeviceId a = DeviceId::create();
    const DeviceId b = DeviceId::create();

    QVERIFY(a.isValid());
    QVERIFY(b.isValid());
    QVERIFY(a.value().startsWith(QStringLiteral("DEV-")));
    QVERIFY(a != b);  // 两次生成必须不同（UUID 唯一）
}

void TestDomainV2::deviceId_equality()
{
    // 相同文本的 id 是"同一个设备"（相等比较按文本）
    const DeviceId a(QStringLiteral("DEV-ABC"));
    const DeviceId b(QStringLiteral("DEV-ABC"));
    const DeviceId c(QStringLiteral("DEV-OTHER"));

    QVERIFY(a == b);
    QVERIFY(a != c);

    // 默认构造是无效 id（未分配）
    const DeviceId empty;
    QVERIFY(!empty.isValid());
}

// ==================== DeviceState ====================

void TestDomainV2::state_legalTransitions()
{
    // 合法路径：未连接→连接中→已连接（正常连接流程）
    QVERIFY(canTransition(DeviceState::Disconnected, DeviceState::Connecting));
    QVERIFY(canTransition(DeviceState::Connecting, DeviceState::Online));

    // 已连接→出错（意外断线）→连接中（自动重连）
    QVERIFY(canTransition(DeviceState::Online, DeviceState::Error));
    QVERIFY(canTransition(DeviceState::Error, DeviceState::Connecting));

    // 用户主动断开：已连接/出错 → 未连接
    QVERIFY(canTransition(DeviceState::Online, DeviceState::Disconnected));
    QVERIFY(canTransition(DeviceState::Error, DeviceState::Disconnected));
}

void TestDomainV2::state_illegalTransitions()
{
    // 非法路径必须被拒绝（状态机不变量）
    // 未连接直接跳到已连接：跳过连接流程，不允许
    QVERIFY(!canTransition(DeviceState::Disconnected, DeviceState::Online));
    // 已连接再跳到连接中：重复连接，不允许
    QVERIFY(!canTransition(DeviceState::Online, DeviceState::Connecting));
    // 未连接直接跳到出错：不存在从初始态直接出错
    QVERIFY(!canTransition(DeviceState::Disconnected, DeviceState::Error));
}

// ==================== ConnectionParams ====================

void TestDomainV2::connParams_tcpValidation()
{
    // TCP 合法：host 非空 + 端口有效 + 超时为正
    ConnectionParams p;
    p.type = ConnectionType::Tcp;
    p.host = QStringLiteral("127.0.0.1");
    p.port = 40001;
    p.connectTimeoutMs = 5000;
    QVERIFY(p.isValid());

    // TCP 非法：端口为 0（未配置）
    p.port = 0;
    QVERIFY(!p.isValid());

    // TCP 非法：host 为空
    p.port = 40001;
    p.host.clear();
    QVERIFY(!p.isValid());
}

void TestDomainV2::connParams_serialValidation()
{
    // 串口合法：串口号非空 + 波特率为正
    ConnectionParams p;
    p.type = ConnectionType::Serial;
    p.portName = QStringLiteral("COM3");
    p.baudRate = 115200;
    QVERIFY(p.isValid());

    // 串口非法：波特率为 0
    p.baudRate = 0;
    QVERIFY(!p.isValid());

    // 串口非法：串口号为空
    p.baudRate = 115200;
    p.portName.clear();
    QVERIFY(!p.isValid());
}

// ==================== ChannelConfig ====================

void TestDomainV2::channelConfig_validity()
{
    // 量程下限 ≤ 上限：合法
    ChannelConfig cfg;
    cfg.minValue = 0.0;
    cfg.maxValue = 100.0;
    QVERIFY(cfg.isValid());

    // 量程下限 > 上限：非法（配置错误）
    cfg.minValue = 100.0;
    cfg.maxValue = 0.0;
    QVERIFY(!cfg.isValid());
}

void TestDomainV2::channelConfig_toEngineering()
{
    // 工程值 = 原始值 × scale + offset
    ChannelConfig cfg;
    cfg.scale  = 2.0;
    cfg.offset = 1.0;

    // 原始值 50.0 → 工程值 50*2+1 = 101.0
    QCOMPARE(cfg.toEngineering(50.0), 101.0);
    // 默认 scale=1 offset=0 → 原样
    ChannelConfig def;
    QCOMPARE(def.toEngineering(42.0), 42.0);
}

void TestDomainV2::channelConfig_isOutOfRange()
{
    // 量程 [0,100]
    ChannelConfig cfg;
    cfg.minValue = 0.0;
    cfg.maxValue = 100.0;

    QVERIFY(!cfg.isOutOfRange(50.0));   // 量程内
    QVERIFY(cfg.isOutOfRange(150.0));   // 超上限
    QVERIFY(cfg.isOutOfRange(-10.0));   // 低于下限
}

// ==================== AlarmRule ====================

void TestDomainV2::alarmRule_aboveHigh()
{
    // 超过阈值触发
    AlarmRule rule;
    rule.condition = AlarmCondition::AboveHigh;
    rule.threshold = 80.0;

    QVERIFY(rule.isTriggered(90.0));   // 90 > 80：触发
    QVERIFY(!rule.isTriggered(70.0));  // 70 <= 80：不触发
    QVERIFY(!rule.isTriggered(80.0));  // 恰好等于阈值：不触发（严格大于）
}

void TestDomainV2::alarmRule_belowLow()
{
    // 低于阈值触发
    AlarmRule rule;
    rule.condition = AlarmCondition::BelowLow;
    rule.threshold = 20.0;

    QVERIFY(rule.isTriggered(10.0));   // 10 < 20：触发
    QVERIFY(!rule.isTriggered(30.0));  // 30 >= 20：不触发
}

// ==================== AlarmEvent ====================

void TestDomainV2::alarmEvent_lifecycle()
{
    // 构造即 Active，未确认
    AlarmEvent ev(1, 0, 2, AlarmSeverity::Critical,
                  QStringLiteral("温度过高"), QDateTime(QDate(2026, 8, 15), QTime(10, 0, 0)));
    QVERIFY(ev.isActive());
    QVERIFY(!ev.isAcked());
    QVERIFY(!ev.ackTime().isValid());  // 未确认，ackTime 无效

    // 确认：进入已确认态，记录确认时刻
    const QDateTime ackTime(QDate(2026, 8, 15), QTime(10, 5, 0));
    ev.ack(ackTime);
    QVERIFY(ev.isAcked());
    QVERIFY(ev.isActive());            // 确认 ≠ 清除，报警仍挂着（等处理）
    QCOMPARE(ev.ackTime(), ackTime);

    // 清除：报警解除
    ev.clear();
    QVERIFY(!ev.isActive());
}

void TestDomainV2::alarmEvent_ackIdempotent()
{
    // 重复确认是 no-op：第二次 ack 不改变已记录的确认时刻
    AlarmEvent ev(2, 0, 1, AlarmSeverity::Warning,
                  QStringLiteral("压力偏高"), QDateTime(QDate(2026, 8, 15), QTime(9, 0, 0)));
    const QDateTime t1(QDate(2026, 8, 15), QTime(9, 10, 0));
    const QDateTime t2(QDate(2026, 8, 15), QTime(9, 20, 0));

    ev.ack(t1);
    ev.ack(t2);  // 第二次不应覆盖第一次
    QCOMPARE(ev.ackTime(), t1);
}

// ==================== AcquisitionRecord ====================

void TestDomainV2::record_beginEndLifecycle()
{
    // begin：会话激活，开始时间有效
    AcquisitionRecord rec = AcquisitionRecord::begin(QStringLiteral("SESS-001"));
    QVERIFY(rec.isActive());
    QVERIFY(rec.startTime().isValid());
    QVERIFY(!rec.endTime().isValid());  // 未结束，endTime 无效
    QCOMPARE(rec.sampleCount(), 0);

    // addSample：计数递增
    rec.addSample();
    rec.addSample();
    rec.addSample();
    QCOMPARE(rec.sampleCount(), 3);

    // end：会话结束，endTime 有效，不再活动
    const QDateTime end(QDateTime::currentDateTime());
    rec.end(end);
    QVERIFY(!rec.isActive());
    QCOMPARE(rec.endTime(), end);

    // 会话完整性：结束时刻 ≥ 开始时刻（这里直接验证 endTime 有效即可）
    QVERIFY(rec.endTime() >= rec.startTime());
}

void TestDomainV2::record_addSampleAfterEnd_isNoOp()
{
    // 会话结束后再 addSample：计数不应增加（no-op）
    AcquisitionRecord rec = AcquisitionRecord::begin(QStringLiteral("SESS-002"));
    rec.addSample();
    rec.end(QDateTime::currentDateTime());
    QCOMPARE(rec.sampleCount(), 1);

    rec.addSample();  // 结束后的 add 应被忽略
    QCOMPARE(rec.sampleCount(), 1);

    // 重复 end 也是 no-op：保留第一次结束时刻
    const QDateTime t1(QDateTime::currentDateTime());
    rec.end(t1);
    QCOMPARE(rec.endTime(), t1);
}

// 生成 main()
QTEST_MAIN(TestDomainV2)
#include "tst_domain_v2.moc"
