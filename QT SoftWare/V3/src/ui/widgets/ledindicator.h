/**
 * @file ledindicator.h
 * @brief V3 UI 层 —— 自绘报警 LED 指示灯
 *
 * ── 开发思路 ──
 * LED 是"状态离散控件"：报警态只有开/关两种，与实时值的高频刷新不同，它只需在
 * 状态翻转时重绘一次。因此 setAlarm 内部做"变化检测"，值没变就不 update()——
 * 这正好落实 UI 架构审查"LED 离散重绘"的纪律，也避免与曲线每帧重绘叠加无谓开销。
 *
 * 视觉：报警开 → 报警红（亮），正常 → 正常绿（暗）。用径向渐变模拟 LED 中心高亮、
 * 边缘渐暗的发光效果。
 *
 * ── WPF 对照 ──
 *   自绘 LedIndicator  ↔  WPF 中按 IsAlarm 布尔量切换模板的指示灯控件（红/绿两态）。
 */

#pragma once

#include <QWidget>

namespace dscope {
namespace ui {

/**
 * @class LedIndicator
 * @brief 报警 LED 指示灯（自绘：径向渐变圆，红亮=报警 / 绿暗=正常）
 */
class LedIndicator : public QWidget
{
    Q_OBJECT

public:
    explicit LedIndicator(QWidget *parent = nullptr);

    /** @brief 设置报警态（仅状态翻转时重绘） */
    void setAlarm(bool on);

    /** @brief 当前是否报警 */
    bool isAlarm() const;

    QSize sizeHint() const override;
    QSize minimumSizeHint() const override;

protected:
    void paintEvent(QPaintEvent *event) override;

private:
    bool m_alarm = false;   ///< 当前报警态（false=正常绿暗，true=报警红亮）
};

} // namespace ui
} // namespace dscope
