/**
 * @file ledindicator.cpp
 * @brief LED 指示灯控件实现
 *
 * 绘制顺序（paintEvent）：
 *   1. 灯座（外圈）：亮时是灯色的暗化版，灭时是深灰；
 *   2. 灯芯（内圈）：QRadialGradient 径向渐变制造立体/发光效果；
 *   3. 高光点：仅亮时在左上方加一个小高光，强化"发光球面"质感。
 *
 * 教学点（对照 WPF）：
 *   - QRadialGradient ≈ RadialGradientBrush；同心圆 ≈ Ellipse 叠加；
 *   - 亮/灭只影响状态成员 m_on，重绘完全由 paintEvent 决定，状态与表现解耦。
 */

#include "ui/widgets/ledindicator.h"

#include <QPainter>
#include <QRadialGradient>
#include <QPointF>

namespace datascope {
namespace ui {

LedIndicator::LedIndicator(QWidget *parent)
    : QWidget(parent)
{
    // 控件自身背景透明（默认），整体底色由父容器 QSS 决定
}

void LedIndicator::setState(bool on)
{
    if (m_on == on)
        return;                     // 状态没变，避免无谓重绘
    m_on = on;
    update();
}

void LedIndicator::setColor(const QColor &color)
{
    if (m_color == color)
        return;
    m_color = color;
    update();
}

bool LedIndicator::isOn() const
{
    return m_on;
}

QSize LedIndicator::sizeHint() const
{
    return QSize(20, 20);
}

QSize LedIndicator::minimumSizeHint() const
{
    return QSize(12, 12);
}

void LedIndicator::paintEvent(QPaintEvent *event)
{
    Q_UNUSED(event);
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing, true);   // 抗锯齿

    const qreal side = qMin(width(), height());
    if (side <= 2.0)
        return;                                            // 控件过小，放弃绘制
    const QPointF center(rect().center());
    const qreal radius = side / 2.0 - 2.0;                 // 四周留 2px 边距

    // ---- 1. 灯座（外圈）：亮时是灯色的暗化版，灭时是深灰 ----
    painter.setPen(QPen(QColor(0x3a, 0x4a, 0x5a), 1.0));
    painter.setBrush(m_on ? m_color.darker(120) : QColor(0x2c, 0x3e, 0x50));
    painter.drawEllipse(center, radius, radius);

    // ---- 2. 灯芯（内圈）：径向渐变制造立体/发光效果 ----
    QRadialGradient grad(center, radius);
    if (m_on) {
        // 亮：内亮外暗的发光渐变（教学点：QRadialGradient 的 0→1 颜色过渡）
        grad.setColorAt(0.0, m_color.lighter(170));
        grad.setColorAt(0.55, m_color);
        grad.setColorAt(1.0, m_color.darker(140));
    } else {
        // 灭：整体暗灰，保留微弱立体感
        grad.setColorAt(0.0, QColor(0x4a, 0x5a, 0x6a));
        grad.setColorAt(1.0, QColor(0x1e, 0x2a, 0x38));
    }
    painter.setPen(Qt::NoPen);
    painter.setBrush(grad);
    painter.drawEllipse(center, radius * 0.74, radius * 0.74);

    // ---- 3. 高光点：仅亮时在左上方加一个小高光，强化球面质感 ----
    if (m_on) {
        painter.setBrush(QColor(255, 255, 255, 150));
        painter.drawEllipse(center + QPointF(-radius * 0.32, -radius * 0.32),
                            radius * 0.16, radius * 0.10);
    }
}

} // namespace ui
} // namespace datascope
