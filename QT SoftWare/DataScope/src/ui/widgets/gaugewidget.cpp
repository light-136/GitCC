/**
 * @file gaugewidget.cpp
 * @brief 仪表盘控件实现
 *
 * 绘制顺序（paintEvent）：
 *   1. 表盘底色弧（270° 扇形，深色描边）；
 *   2. 前景弧：当前值所对应的弧段（主色青色，按比例算跨度）；
 *   3. 刻度线（大刻度每 10%、小刻度每 5%）+ 0/25/50/75/100% 刻度标签；
 *   4. 指针（QPainterPath 三角形，从圆心指向当前值角度）；
 *   5. 表盘下方显示数值与单位。
 *
 * 角度约定（教学重点）：
 *   - Qt 的 drawArc 角度单位是 1/16 度，0° 在 3 点钟方向，正值逆时针
 *     （屏幕坐标 y 向下，逆时针在屏幕上表现为向上）；
 *   - 本表盘从 225°（左下）顺时针扫 270° 到 -45°（右下），因此跨度角用负数；
 *   - 指针角度 = 225° - 比例 × 270°，用三角函数换算为圆心→端点的方向向量。
 */

#include "ui/widgets/gaugewidget.h"

#include <QPainter>
#include <QPainterPath>
#include <QPen>
#include <QBrush>
#include <QFont>
#include <QRectF>
#include <QPointF>
#include <QtMath>

namespace {
// 匿名命名空间：文件内私有工具函数

// 把数值映射为表盘指针角度（Qt 角度约定，返回 225° 到 -45° 之间的值）
// 越界值被钳制到端点，保证指针不越出表盘
qreal valueToAngle(double value, double min, double max)
{
    const double range = max - min;
    const double safe = (range > 0.0) ? range : 1.0;   // 防御：避免除零
    double frac = (value - min) / safe;
    frac = qBound(0.0, frac, 1.0);                     // 钳制到 [0,1]
    return 225.0 - frac * 270.0;                       // 起始 225°，顺时针（减角度）扫 270°
}

// 把 Qt 角度换算为"圆心指向该角度方向的坐标偏移"（radius 为长度）
QPointF angleToOffset(qreal angleDeg, qreal radius)
{
    const qreal rad = qDegreesToRadians(angleDeg);
    // 屏幕 y 向下：sin 取负才符合 Qt "0°=3点钟、90°=12点钟" 的约定
    return QPointF(qCos(rad), -qSin(rad)) * radius;
}

// 数值格式化：整数不显示小数，非整数保留 1 位小数（刻度/数值共用）
QString formatNumber(double v)
{
    if (qAbs(v - qRound(v)) < 1e-9)
        return QString::number(qRound(v));
    return QString::number(v, 'f', 1);
}
} // namespace

namespace datascope {
namespace ui {

GaugeWidget::GaugeWidget(QWidget *parent)
    : QWidget(parent)
{
    // 控件自身背景透明（默认），整体底色由父容器 QSS 决定
}

void GaugeWidget::setRange(double min, double max)
{
    if (min >= max)
        return;                                        // 非法量程（min>=max）不生效
    m_min = min;
    m_max = max;
    m_value = qBound(m_min, m_value, m_max);           // 量程变化后重新钳制当前值
    update();
}

void GaugeWidget::setValue(double value)
{
    if (qIsNaN(value))
        value = m_min;                                 // NaN 保护：落到量程下限，避免污染状态
    m_value = qBound(m_min, value, m_max);             // 越界钳制
    update();
}

void GaugeWidget::setUnit(const QString &unit)
{
    m_unit = unit;
    update();
}

double GaugeWidget::value() const
{
    return m_value;
}

double GaugeWidget::minimum() const
{
    return m_min;
}

double GaugeWidget::maximum() const
{
    return m_max;
}

QSize GaugeWidget::sizeHint() const
{
    return QSize(200, 200);
}

QSize GaugeWidget::minimumSizeHint() const
{
    return QSize(120, 120);
}

void GaugeWidget::paintEvent(QPaintEvent *event)
{
    Q_UNUSED(event);
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing, true);   // 抗锯齿

    const QRectF rc = rect();
    const qreal side = qMin(rc.width(), rc.height());      // 取短边保证圆不变形
    if (side <= 1.0)
        return;

    // 圆心略偏上，给下方数值/单位留空间；半径取小一点保证弧线不贴边
    const QPointF center(rc.center().x(), rc.top() + side * 0.55);
    const qreal radius = side * 0.38;
    const QRectF arcRect(center.x() - radius, center.y() - radius,
                         radius * 2.0, radius * 2.0);

    // ---- 1. 表盘底色弧（270° 扇形，深色描边）----
    painter.setPen(QPen(QColor(0x2c, 0x3e, 0x50), 8.0));
    painter.setBrush(Qt::NoBrush);
    painter.drawArc(arcRect, 225 * 16, -270 * 16);         // 起始 225°，顺时针扫 270°

    // ---- 2. 前景弧：当前值所占弧段（主色青色，教学点：按比例算跨度角）----
    const double frac = qBound(0.0,
                               (m_value - m_min) / qMax(1e-9, m_max - m_min),
                               1.0);
    painter.setPen(QPen(QColor(0x16, 0xa0, 0x85), 8.0));
    painter.drawArc(arcRect, 225 * 16, -qRound(frac * 270.0 * 16.0));

    // ---- 3. 刻度线（大刻度每 10%，小刻度每 5%）----
    painter.setPen(QPen(QColor(0xcf, 0xd8, 0xe3), 1.6));
    for (int i = 0; i <= 10; ++i) {
        const qreal ang = 225.0 - i * 27.0;                // 每段 27°
        painter.drawLine(center + angleToOffset(ang, radius + 4.0),
                         center + angleToOffset(ang, radius - 6.0));
    }
    painter.setPen(QPen(QColor(0x9f, 0xb3, 0xc8), 1.0));
    for (int i = 0; i <= 20; ++i) {
        if (i % 2 == 0)
            continue;                                      // 与大刻度位置重叠处跳过
        const qreal ang = 225.0 - i * 13.5;                // 每段 13.5°
        painter.drawLine(center + angleToOffset(ang, radius + 2.0),
                         center + angleToOffset(ang, radius - 3.0));
    }

    // ---- 4. 刻度标签（0/25/50/75/100% 五处）----
    painter.setPen(QColor(0x9f, 0xb3, 0xc8));
    painter.setFont(QFont(QStringLiteral("Microsoft YaHei"), 7));
    const double span = m_max - m_min;
    for (int i = 0; i <= 4; ++i) {
        const double f = i / 4.0;
        const qreal ang = 225.0 - f * 270.0;
        const QPointF pos = center + angleToOffset(ang, radius + 18.0);
        const QString label = formatNumber(m_min + span * f);
        painter.drawText(QRectF(pos.x() - 22.0, pos.y() - 8.0, 44.0, 16.0),
                         Qt::AlignCenter, label);
    }

    // ---- 5. 指针（QPainterPath 三角形：底边垂直于指针方向，顶点指向当前值）----
    const qreal ptrAngle = valueToAngle(m_value, m_min, m_max);
    const QPointF dir = angleToOffset(ptrAngle, 1.0);       // 单位方向向量
    const QPointF perp(-dir.y(), dir.x());                  // 垂直方向（指针底边）
    const QPointF tip = center + angleToOffset(ptrAngle, radius - 14.0);
    QPainterPath needle;
    needle.moveTo(center + perp * 4.0);                     // 底边一端
    needle.lineTo(tip);                                     // 顶点（指向刻度）
    needle.lineTo(center - perp * 4.0);                     // 底边另一端
    needle.closeSubpath();
    painter.setPen(Qt::NoPen);
    painter.setBrush(QColor(0x16, 0xa0, 0x85));
    painter.drawPath(needle);

    // 指针根部中心小圆（表轴）
    painter.setBrush(QColor(0xcf, 0xd8, 0xe3));
    painter.drawEllipse(center, 3.5, 3.5);

    // ---- 6. 数值与单位（表盘下方居中）----
    painter.setPen(QColor(0xff, 0xff, 0xff));
    painter.setFont(QFont(QStringLiteral("Microsoft YaHei"), 12, QFont::Bold));
    painter.drawText(QRectF(0.0, rc.height() - 34.0, rc.width(), 20.0),
                     Qt::AlignCenter, formatNumber(m_value));
    if (!m_unit.isEmpty()) {
        painter.setPen(QColor(0x9f, 0xb3, 0xc8));
        painter.setFont(QFont(QStringLiteral("Microsoft YaHei"), 9));
        painter.drawText(QRectF(0.0, rc.height() - 16.0, rc.width(), 14.0),
                         Qt::AlignCenter, m_unit);
    }
}

} // namespace ui
} // namespace datascope
