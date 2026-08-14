/**
 * @file tst_widgets.cpp
 * @brief P14 自绘控件单元测试（Qt Test 框架）
 *
 * 测试范围（只测"逻辑"，不测像素）：
 *   - LineChartWidget：追加/查询、滚动淘汰、清空、非法量程、空数据
 *   - GaugeWidget：默认量程、读写往返、越界钳制、NaN 保护
 *   - LedIndicator：默认灭、setState 切换
 *
 * 教学点：
 *   - 自绘控件把"状态与算法"（成员变量与坐标映射）和"绘制"（paintEvent）
 *     解耦，因此逻辑可以在不显示窗口、不截图的情况下直接断言；
 *   - QTEST_MAIN 在链接 Qt5::Widgets 时会自动创建 QApplication
 *     （对应 WPF 测试项目里的 STA UI 线程），GUI 对象可安全实例化。
 */

#include <QtTest>
#include "ui/widgets/linechartwidget.h"
#include "ui/widgets/gaugewidget.h"
#include "ui/widgets/ledindicator.h"

#include <limits>   // std::numeric_limits（生成 NaN）

using datascope::ui::LineChartWidget;
using datascope::ui::GaugeWidget;
using datascope::ui::LedIndicator;

namespace {
// 生成一个 quiet NaN（不可表示的浮点数），用于 NaN 保护用例
double nanValue()
{
    return std::numeric_limits<double>::quiet_NaN();
}
} // namespace

// 测试类：继承 QObject，每个 private slot 就是一个测试用例
class TestWidgets : public QObject
{
    Q_OBJECT

private slots:
    // ---- LineChartWidget ----
    void linechart_appendAndQuery();        // 追加后点数/末值正确
    void linechart_scrollEvict();           // 超过缓冲上限后滚动淘汰
    void linechart_clear();                 // 清空数据归零
    void linechart_invalidRangeIgnored();   // 非法量程不生效
    void linechart_emptyLastValue();        // 空数据 lastValue()==0

    // ---- GaugeWidget ----
    void gauge_defaultRange();              // 默认量程 0-100
    void gauge_rangeAndValueRoundTrip();    // 量程/取值读写往返
    void gauge_valueClamped();              // 越界值被钳制
    void gauge_nanValueSafe();              // NaN 不崩溃，钳制到下限

    // ---- LedIndicator ----
    void led_defaultOff();                  // 默认灭
    void led_setState();                    // setState 切换
};

void TestWidgets::linechart_appendAndQuery()
{
    LineChartWidget chart;

    // 初始为空
    QCOMPARE(chart.pointCount(), 0);

    // 追加一个点
    chart.appendPoint(10.0);
    QCOMPARE(chart.pointCount(), 1);
    QCOMPARE(chart.lastValue(), 10.0);

    // 追加多个点，lastValue 始终是最近一个
    chart.appendPoint(20.0);
    chart.appendPoint(30.0);
    QCOMPARE(chart.pointCount(), 3);
    QCOMPARE(chart.lastValue(), 30.0);

    // 缓冲上限默认 500
    QCOMPARE(chart.maxPoints(), 500);
}

void TestWidgets::linechart_scrollEvict()
{
    LineChartWidget chart;
    const int limit = chart.maxPoints();

    // 追加 limit + 10 个点：最旧的 10 个应被淘汰
    for (int i = 0; i < limit + 10; ++i)
        chart.appendPoint(double(i));

    // 点数被钳制在上限，不随追加继续增长
    QCOMPARE(chart.pointCount(), limit);
    // 最新的点（limit+9）仍在缓冲尾部
    QCOMPARE(chart.lastValue(), double(limit + 9));
}

void TestWidgets::linechart_clear()
{
    LineChartWidget chart;
    chart.appendPoint(1.0);
    chart.appendPoint(2.0);
    QCOMPARE(chart.pointCount(), 2);

    chart.clearData();
    QCOMPARE(chart.pointCount(), 0);
    QCOMPARE(chart.lastValue(), 0.0);
}

void TestWidgets::linechart_invalidRangeIgnored()
{
    LineChartWidget chart;

    // 默认量程 0-100
    QCOMPARE(chart.minimum(), 0.0);
    QCOMPARE(chart.maximum(), 100.0);

    // min > max：非法，量程保持不变
    chart.setRange(100.0, 50.0);
    QCOMPARE(chart.minimum(), 0.0);
    QCOMPARE(chart.maximum(), 100.0);

    // min == max：非法，量程保持不变
    chart.setRange(50.0, 50.0);
    QCOMPARE(chart.minimum(), 0.0);
    QCOMPARE(chart.maximum(), 100.0);

    // 合法量程生效
    chart.setRange(-10.0, 10.0);
    QCOMPARE(chart.minimum(), -10.0);
    QCOMPARE(chart.maximum(), 10.0);
}

void TestWidgets::linechart_emptyLastValue()
{
    LineChartWidget chart;
    QCOMPARE(chart.lastValue(), 0.0);   // 空数据返回 0
}

void TestWidgets::gauge_defaultRange()
{
    GaugeWidget gauge;
    QCOMPARE(gauge.minimum(), 0.0);
    QCOMPARE(gauge.maximum(), 100.0);
    QCOMPARE(gauge.value(), 0.0);
}

void TestWidgets::gauge_rangeAndValueRoundTrip()
{
    GaugeWidget gauge;
    gauge.setRange(-50.0, 50.0);
    QCOMPARE(gauge.minimum(), -50.0);
    QCOMPARE(gauge.maximum(), 50.0);

    gauge.setValue(25.0);
    QCOMPARE(gauge.value(), 25.0);
}

void TestWidgets::gauge_valueClamped()
{
    GaugeWidget gauge;   // 默认量程 0-100

    gauge.setValue(150.0);
    QCOMPARE(gauge.value(), 100.0);   // 上界钳制

    gauge.setValue(-20.0);
    QCOMPARE(gauge.value(), 0.0);     // 下界钳制
}

void TestWidgets::gauge_nanValueSafe()
{
    GaugeWidget gauge;
    gauge.setRange(10.0, 20.0);

    gauge.setValue(nanValue());       // 传入 NaN 不应崩溃
    QCOMPARE(gauge.value(), 10.0);    // NaN 被钳制到量程下限
}

void TestWidgets::led_defaultOff()
{
    LedIndicator led;
    QVERIFY(!led.isOn());
}

void TestWidgets::led_setState()
{
    LedIndicator led;

    led.setState(true);
    QVERIFY(led.isOn());

    led.setState(false);
    QVERIFY(!led.isOn());

    // setColor 不应影响亮灭状态
    led.setState(true);
    led.setColor(QColor(0xff, 0x00, 0x00));
    QVERIFY(led.isOn());
}

// 生成 main()：QTEST_MAIN 为测试类生成独立可执行文件入口
QTEST_MAIN(TestWidgets)
#include "tst_widgets.moc"
