/**
 * @file tst_alarmengine.cpp
 * @brief 报警引擎单测（Qt Test + QSignalSpy）
 *
 * 验证报警引擎的核心职责（审查 P0-3"报警逻辑应在服务层"的落地）：
 *   1. 边界触发：值超过阈值只产生一次报警（去重，不刷屏）；
 *   2. 恢复检测：值回正常范围发 ruleRecovered；
 *   3. 通道过滤：只评估匹配通道的规则；
 *   4. 坏数据跳过：质量非 Good 的数据不触发报警（防假警）；
 *   5. 恢复后再次触发：能产生第二条报警（状态机正确复位）。
 *
 * 用 QSignalSpy 断言信号行为，这正是"把逻辑从 View 移出后可测试"的价值。
 */

#include <QtTest>
#include <QSignalSpy>

#include "services/alarmengine.h"

using datascope::domain::v2::AlarmCondition;
using datascope::domain::v2::AlarmRule;
using datascope::domain::v2::DataPoint;
using datascope::domain::v2::DataQuality;
using datascope::services::AlarmEngine;

class TestAlarmEngine : public QObject
{
    Q_OBJECT

private slots:
    void initTestCase();  // 注册 AlarmEvent 元类型（QSignalSpy 捕获信号参数的前提）
    void trigger_oncePerRise();
    void recover_afterFalling();
    void filter_byChannel();
    void badQuality_skipped();
    void retrigger_afterRecovery();
};

void TestAlarmEngine::initTestCase()
{
    // AlarmEvent 作为信号参数被 QSignalSpy 捕获时，必须先在运行时注册元类型；
    // 编译期 Q_DECLARE_METATYPE 只声明类型，qRegisterMetaType 才让 QVariant 认识它
    qRegisterMetaType<datascope::domain::v2::AlarmEvent>();
}

// 构造一条"超过阈值"规则的便捷函数
AlarmRule makeHighRule(int channel, double threshold)
{
    AlarmRule rule;
    rule.channelIndex = channel;
    rule.condition    = AlarmCondition::AboveHigh;
    rule.threshold    = threshold;
    rule.description  = QStringLiteral("通道%1 超限").arg(channel + 1);
    return rule;
}

// 构造一个数据点的便捷函数
DataPoint makePoint(int channel, double eng, DataQuality q = DataQuality::Good)
{
    return DataPoint(channel, eng, eng, q, QDateTime::currentDateTime());
}

// ==================== 边界触发去重 ====================

void TestAlarmEngine::trigger_oncePerRise()
{
    AlarmEngine engine;
    const int ruleIdx = engine.addRule(makeHighRule(0, 80.0));  // 通道0 阈值80

    QSignalSpy triggeredSpy(&engine, &AlarmEngine::ruleTriggered);
    QSignalSpy recoveredSpy(&engine, &AlarmEngine::ruleRecovered);

    // 低于阈值：不触发
    engine.evaluate(makePoint(0, 70.0));
    QCOMPARE(triggeredSpy.count(), 0);

    // 超过阈值：触发 1 次
    engine.evaluate(makePoint(0, 90.0));
    QCOMPARE(triggeredSpy.count(), 1);

    // 继续超阈值（连续多帧）：去重，不再触发（不刷屏）
    engine.evaluate(makePoint(0, 95.0));
    engine.evaluate(makePoint(0, 100.0));
    QCOMPARE(triggeredSpy.count(), 1);

    // 报警态确认
    QVERIFY(engine.isRuleActive(ruleIdx));

    // 触发的报警事件内容正确（QSignalSpy 已捕获，initTestCase 注册过元类型）
    const datascope::domain::v2::AlarmEvent ev =
        triggeredSpy.at(0).at(0).value<datascope::domain::v2::AlarmEvent>();
    QCOMPARE(ev.channelIndex(), 0);
    QCOMPARE(ev.isActive(), true);
}

// ==================== 恢复检测 ====================

void TestAlarmEngine::recover_afterFalling()
{
    AlarmEngine engine;
    const int ruleIdx = engine.addRule(makeHighRule(1, 50.0));

    // 触发
    engine.evaluate(makePoint(1, 60.0));

    QSignalSpy recoveredSpy(&engine, &AlarmEngine::ruleRecovered);

    // 值回落：发恢复信号，报警态清除
    engine.evaluate(makePoint(1, 40.0));
    QCOMPARE(recoveredSpy.count(), 1);
    QVERIFY(!engine.isRuleActive(ruleIdx));

    // 恢复后继续正常：不再重复发恢复
    engine.evaluate(makePoint(1, 30.0));
    QCOMPARE(recoveredSpy.count(), 1);
}

// ==================== 通道过滤 ====================

void TestAlarmEngine::filter_byChannel()
{
    AlarmEngine engine;
    engine.addRule(makeHighRule(0, 80.0));  // 只监控通道0
    engine.addRule(makeHighRule(2, 50.0));  // 只监控通道2

    QSignalSpy triggeredSpy(&engine, &AlarmEngine::ruleTriggered);

    // 通道1的数据：两条规则都不匹配 → 不触发
    engine.evaluate(makePoint(1, 999.0));
    QCOMPARE(triggeredSpy.count(), 0);

    // 通道2的数据：只触发规则2（通道2那条）
    engine.evaluate(makePoint(2, 60.0));
    QCOMPARE(triggeredSpy.count(), 1);
}

// ==================== 坏数据跳过 ====================

void TestAlarmEngine::badQuality_skipped()
{
    AlarmEngine engine;
    engine.addRule(makeHighRule(0, 80.0));

    QSignalSpy triggeredSpy(&engine, &AlarmEngine::ruleTriggered);

    // 质量 OverRange/Invalid 的数据：即使值超限也不触发（防假警）
    engine.evaluate(makePoint(0, 200.0, DataQuality::OverRange));
    engine.evaluate(makePoint(0, 200.0, DataQuality::Invalid));
    QCOMPARE(triggeredSpy.count(), 0);
}

// ==================== 恢复后再次触发 ====================

void TestAlarmEngine::retrigger_afterRecovery()
{
    AlarmEngine engine;
    const int ruleIdx = engine.addRule(makeHighRule(0, 80.0));

    QSignalSpy triggeredSpy(&engine, &AlarmEngine::ruleTriggered);

    // 第一次触发 → 恢复 → 第二次触发：共 2 条报警（状态机正确复位）
    engine.evaluate(makePoint(0, 90.0));   // 触发1
    QCOMPARE(triggeredSpy.count(), 1);

    engine.evaluate(makePoint(0, 70.0));   // 恢复
    QVERIFY(!engine.isRuleActive(ruleIdx));

    engine.evaluate(makePoint(0, 95.0));   // 触发2
    QCOMPARE(triggeredSpy.count(), 2);

    // 两条报警事件 id 不同（唯一性）
    const datascope::domain::v2::AlarmEvent ev1 =
        qvariant_cast<datascope::domain::v2::AlarmEvent>(triggeredSpy.at(0).at(0));
    const datascope::domain::v2::AlarmEvent ev2 =
        qvariant_cast<datascope::domain::v2::AlarmEvent>(triggeredSpy.at(1).at(0));
    QVERIFY(ev1.id() != ev2.id());
}

QTEST_MAIN(TestAlarmEngine)
#include "tst_alarmengine.moc"
