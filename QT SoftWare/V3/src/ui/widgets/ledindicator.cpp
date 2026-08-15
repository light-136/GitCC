/**
 * @file ledindicator.cpp
 * @brief V3 UI 层 —— 报警 LED 指示灯实现
 *
 * ── 开发思路 ──
 * 用 QRadialGradient 画同心渐变的圆：中心取主色高亮（lighter），边缘取主色暗化
 * （darker），模拟发光 LED；外圈再画一圈描边作为灯座轮廓。颜色只从 Theme 取值。
 */

#include "ui/widgets/ledindicator.h"
#include "ui/theme.h"

#include <QPainter>
#include <QPaintEvent>
#include <QRadialGradient>

namespace dscope {
namespace ui {

LedIndicator::LedIndicator(QWidget *parent)
    : QWidget(parent)
{
    // 透明底：由父卡片背景衬托，不单独刷背景色
    setAttribute(Qt::WA_OpaquePaintEvent, false);
}

void LedIndicator::setAlarm(bool on)
{
    if (m_alarm == on)
        return;         // 状态未翻转 → 不重绘（LED 离散重绘纪律）
    m_alarm = on;
    update();
}

bool LedIndicator::isAlarm() const
{
    return m_alarm;
}

QSize LedIndicator::sizeHint() const
{
    return QSize(22, 22);
}

QSize LedIndicator::minimumSizeHint() const
{
    return QSize(16, 16);
}

void LedIndicator::paintEvent(QPaintEvent *event)
{
    Q_UNUSED(event);

    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing, true);

    // 留 1px 内边距，给外圈描边留空间
    const QRectF area = QRectF(rect()).adjusted(1, 1, -1, -1);

    // 主色：报警红亮 / 正常绿暗（单一来源 Theme）
    const QColor base = m_alarm ? Theme::alarmRed() : Theme::normalGreen();
    const QColor center = base.lighter(170);   // 中心高亮
    const QColor edge   = base.darker(140);    // 边缘暗化

    QRadialGradient gradient(area.center(), area.width() / 2.0);
    gradient.setColorAt(0.0, center);
    gradient.setColorAt(0.55, base);
    gradient.setColorAt(1.0, edge);

    painter.setPen(Qt::NoPen);
    painter.setBrush(gradient);
    painter.drawEllipse(area);

    // 外圈灯座描边
    painter.setBrush(Qt::NoBrush);
    painter.setPen(QPen(Theme::border(), 1));
    painter.drawEllipse(area);
}

} // namespace ui
} // namespace dscope
