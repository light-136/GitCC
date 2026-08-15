/**
 * @file gaugewidget.h
 * @brief V3 UI 层 —— 自绘仪表盘控件（表盘 + 刻度 + 指针）
 *
 * ── 开发思路 ──
 * 仪表盘是"单通道瞬时值"的指针化展示：给定量程 [min,max] 与当前值，把值归一化到
 * [0,1]，再映射到一段 270° 顶部圆弧（速度表式：左下方起始，顺时针穿过顶部到右下）。
 * `paintEvent` 只读自身成员（m_min/m_max/m_value/m_unit/m_title），不做业务判断。
 *
 * 采用顶部 270° 弧（而非整圆），是因为整圆 360° 会有一个"背面"区间不可读，270° 顶部
 * 弧与速度表一致，是工业仪表最直观的形态。
 *
 * ── WPF 对照 ──
 *   自绘 GaugeWidget  ↔  WPF 中自绘的 RadialGauge 控件：量程/值/单位/标题为依赖属性，
 *                        重写 OnRender 画弧、刻度、指针。
 */

#pragma once

#include <QWidget>
#include <QString>

namespace dscope {
namespace ui {

/**
 * @class GaugeWidget
 * @brief 单通道仪表盘（自绘：表盘弧带 + 主/次刻度 + 指针 + 数值/单位）
 */
class GaugeWidget : public QWidget
{
    Q_OBJECT

public:
    explicit GaugeWidget(QWidget *parent = nullptr);

    /** @brief 设置量程（越界防御：max <= min 时自动校正为 min+1） */
    void setRange(double min, double max);

    /** @brief 设置当前值（指针位置按量程归一化，越界裁剪） */
    void setValue(double v);

    /** @brief 设置工程单位（如 "℃"） */
    void setUnit(const QString &unit);

    /** @brief 设置标题（通道名，画在表盘顶部） */
    void setTitle(const QString &title);

    QSize minimumSizeHint() const override;

protected:
    void paintEvent(QPaintEvent *event) override;

private:
    /** @brief 把当前值归一化到 [0,1]（越界裁剪） */
    double normalizedValue() const;

    /** @brief 把 [0,1] 归一值映射为指针角度（Qt 角度制：0°=3 点钟，逆时针为正） */
    double angleForFraction(double fraction) const;

    double  m_min   = 0.0;      ///< 量程下限
    double  m_max   = 100.0;    ///< 量程上限
    double  m_value = 0.0;      ///< 当前值
    QString m_unit;             ///< 工程单位
    QString m_title;            ///< 标题（通道名）
};

} // namespace ui
} // namespace dscope
