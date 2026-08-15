/**
 * @file tst_alarmeventmodel.cpp
 * @brief 报警事件模型单测（Qt Test + QSignalSpy）
 *
 * 验证报警中心数据源的三类行为：
 *   1. 追加顺序：新报警在最上方（头插）；
 *   2. 显示文本：时间/级别/通道/描述拼装正确，确认后带"(已确认)"标记；
 *   3. 通知机制：appendEvent 触发行插入、setAcked 触发 dataChanged。
 *
 * 与 DeviceListModel 测试的对照（教学点）：
 *   - 表格模型测"按列取值"，列表模型测"整行文本" —— 两者 data() 的
 *     实现思路一致，只是结构不同，测试随之不同。
 */

#include <QtTest>
#include <QSignalSpy>

#include "ui/models/alarmeventmodel.h"

using datascope::domain::v2::AlarmEvent;
using datascope::domain::v2::AlarmSeverity;
using datascope::ui::models::AlarmEventModel;

class TestAlarmEventModel : public QObject
{
    Q_OBJECT

private slots:
    void emptyModel_initialState();
    void appendEvent_prependsNewest();
    void data_displayText();
    void setAcked_marksAndEmitsDataChanged();
    void clear_resetsModel();
    void outOfRange_returnsNull();
};

// 构造一条报警事件的便捷函数
AlarmEvent makeEvent(quint64 id, int channel, AlarmSeverity sev,
                     const QString &msg, const QDateTime &time)
{
    return AlarmEvent(id, 0, channel, sev, msg, time);
}

void TestAlarmEventModel::emptyModel_initialState()
{
    AlarmEventModel model;
    QCOMPARE(model.rowCount(), 0);
    QCOMPARE(model.count(), 0);
}

void TestAlarmEventModel::appendEvent_prependsNewest()
{
    AlarmEventModel model;

    // 先加一条旧的，再加一条新的
    model.appendEvent(makeEvent(1, 0, AlarmSeverity::Warning, QStringLiteral("温度偏高"),
                                QDateTime(QDate(2026, 8, 15), QTime(9, 0, 0))));
    model.appendEvent(makeEvent(2, 1, AlarmSeverity::Critical, QStringLiteral("压力过高"),
                                QDateTime(QDate(2026, 8, 15), QTime(9, 5, 0))));

    // 两条都在，最新在最上（index 0 = 压力过高）
    QCOMPARE(model.rowCount(), 2);
    QString first  = model.data(model.index(0)).toString();
    QString second = model.data(model.index(1)).toString();
    QVERIFY(first.contains(QStringLiteral("压力过高")));
    QVERIFY(second.contains(QStringLiteral("温度偏高")));
}

void TestAlarmEventModel::data_displayText()
{
    AlarmEventModel model;
    model.appendEvent(makeEvent(1, 2, AlarmSeverity::Warning, QStringLiteral("温度偏高"),
                                QDateTime(QDate(2026, 8, 15), QTime(10, 30, 0))));

    const QString text = model.data(model.index(0)).toString();

    // 文本包含：时间 / 级别 / 通道（1基）/ 描述
    QVERIFY(text.contains(QStringLiteral("[10:30:00]")));
    QVERIFY(text.contains(QStringLiteral("[警告]")));
    QVERIFY(text.contains(QStringLiteral("通道3")));  // channelIndex 2 → 显示 3
    QVERIFY(text.contains(QStringLiteral("温度偏高")));
    QVERIFY(!text.contains(QStringLiteral("已确认")));  // 未确认无标记
}

void TestAlarmEventModel::setAcked_marksAndEmitsDataChanged()
{
    AlarmEventModel model;
    model.appendEvent(makeEvent(1, 0, AlarmSeverity::Critical, QStringLiteral("严重故障"),
                                QDateTime(QDate(2026, 8, 15), QTime(8, 0, 0))));

    QSignalSpy spy(&model, &AlarmEventModel::dataChanged);

    model.setAcked(0);

    // 确认后：数据通知发出、文本带"已确认"
    QCOMPARE(spy.count(), 1);
    QVERIFY(model.data(model.index(0)).toString().contains(QStringLiteral("已确认")));

    // 越界确认：no-op，不发信号
    QSignalSpy spy2(&model, &AlarmEventModel::dataChanged);
    model.setAcked(99);
    QCOMPARE(spy2.count(), 0);
}

void TestAlarmEventModel::clear_resetsModel()
{
    AlarmEventModel model;
    model.appendEvent(makeEvent(1, 0, AlarmSeverity::Info, QStringLiteral("提示"), QDateTime::currentDateTime()));
    model.appendEvent(makeEvent(2, 1, AlarmSeverity::Warning, QStringLiteral("警告"), QDateTime::currentDateTime()));
    QCOMPARE(model.rowCount(), 2);

    model.clear();
    QCOMPARE(model.rowCount(), 0);
    QCOMPARE(model.count(), 0);

    // 清空后仍可追加（模型可复用）
    model.appendEvent(makeEvent(3, 0, AlarmSeverity::Info, QStringLiteral("新提示"), QDateTime::currentDateTime()));
    QCOMPARE(model.rowCount(), 1);
}

void TestAlarmEventModel::outOfRange_returnsNull()
{
    AlarmEventModel model;
    model.appendEvent(makeEvent(1, 0, AlarmSeverity::Info, QStringLiteral("提示"), QDateTime::currentDateTime()));

    // 越界/无效索引：返回无效 QVariant（不崩溃）
    QVERIFY(!model.data(model.index(10)).isValid());
    QVERIFY(!model.data(QModelIndex()).isValid());
}

QTEST_MAIN(TestAlarmEventModel)
#include "tst_alarmeventmodel.moc"
