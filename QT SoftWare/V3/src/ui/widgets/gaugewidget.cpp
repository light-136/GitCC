/**
 * @file gaugewidget.cpp
 * @brief V3 UI 层 —— 自绘仪表盘控件实现
 *
 * ── 开发思路 ──
 * 几何约定：
 *   - 表盘为顶部 270° 弧：起始角 225°（7:30 方向），顺时针扫 270° 至 -45°（4:30 方向），
 *     中途穿过 90°（12 点钟顶部）。Qt 的 drawArc 以 0°=3 点钟、逆时针为正，故 span 取负。
 *   - 值归一化 f∈[0,1] → 指针角 θ = 225° - 270°·f（f=0 左下，f=0.5 顶部，f=1 右下）。
 *   - 极坐标换算：Qt 坐标系 y 轴向下，故 y = cy - r·sinθ（θ=90° 时指向上方）。
 * 刻度：主刻度 5 段（6 个主刻度带数值）+ 每段 4 条次刻度。
 */

#include "ui/widgets/gaugewidget.h"
#include "ui/theme.h"

#include <QPainter>
#include <QPaintEvent>
#include <QtMath>

namespace dscope {
namespace ui {

namespace {

constexpr double kStartAngle = 225.0;   ///< 表盘起始角（左下，7:30 方向）
constexpr double kSweep      = -270.0;  ///< 顺时针扫 270°（穿过顶部）

/** @brief 极坐标 → 笛卡尔坐标（Qt 角度制：0°=3 点钟，逆时针为正，y 轴向下） */
QPointF polarPoint(const QPointF &center, double degrees, double radius)
{
    const double rad = qDegreesToRadians(degrees);
    return QPointF(center.x() + radius * qCos(rad),
                   center.y() - radius * qSin(rad));
}

} // namespace

GaugeWidget::GaugeWidget(QWidget *parent)
    : QWidget(parent)
{
    // 深色背景（调色板方式，不写裸 hex）
    QPalette pal = palette();
    pal.setColor(QPalette::Window, Theme::background());
    setPalette(pal);
    setAutoFillBackground(true);
}

void GaugeWidget::setRange(double min, double max)
{
    m_min = min;
    // 越界防御：量程必须为正，否则归一化除零
    m_max = (max <= min) ? (min + 1.0) : max;
    update();
}

void GaugeWidget::setValue(double v)
{
    m_value = v;
    update();
}

void GaugeWidget::setUnit(const QString &unit)
{
    m_unit = unit;
    update();
}

void GaugeWidget::setTitle(const QString &title)
{
    m_title = title;
    update();
}

QSize GaugeWidget::minimumSizeHint() const
{
    return QSize(120, 150);
}

double GaugeWidget::normalizedValue() const
{
    const double span = m_max - m_min;
    if (span <= 0.0)
        return 0.0;
    const double fraction = (m_value - m_min) / span;
    if (fraction < 0.0) return 0.0;
    if (fraction > 1.0) return 1.0;
    return fraction;
}

double GaugeWidget::angleForFraction(double fraction) const
{
    // f=0 → 225°（左下）；f=0.5 → 90°（顶部）；f=1 → -45°（右下）
    return kStartAngle + kSweep * fraction;
}

void GaugeWidget::paintEvent(QPaintEvent *event)
{
    Q_UNUSED(event);

    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing, true);

    const QRectF full = rect();
    const double titleHeight = 18.0;

    // 1) 标题（通道名）
    if (!m_title.isEmpty()) {
        painter.setPen(Theme::textPrimary());
        painter.setFont(Theme::titleFont());
        painter.drawText(QRectF(full.left(), full.top(), full.width(), titleHeight),
                         Qt::AlignCenter, m_title);
    }

    // 2) 表盘几何：圆心略上移，给下方数值/单位留空间
    const double cx = full.center().x();
    const double cy = titleHeight + (full.height() - titleHeight) * 0.52;
    const QPointF center(cx, cy);
    const double radius = qMin(full.width() * 0.42, (full.height() - titleHeight) * 0.34);

    // 弧带
    const QRectF arcRect(center.x() - radius, center.y() - radius, radius * 2, radius * 2);
    painter.setPen(QPen(Theme::gaugeArc(), 6, Qt::SolidLine, Qt::RoundCap));
    painter.drawArc(arcRect, int(kStartAngle * 16), int(kSweep * 16));

    // 3) 刻度（主刻度带数值 + 次刻度）
    const double rOuter = radius - 4;      // 刻度外端
    const double rMajor = radius - 14;     // 主刻度内端
    const double rMinor = radius - 9;      // 次刻度内端
    const double rLabel = radius - 26;     // 主刻度数值半径

    const int majorSegments = 5;           // 5 段 → 6 个主刻度
    const int minorPerMajor = 4;           // 每段 4 条次刻度

    painter.setFont(Theme::axisFont());

    // 主刻度 + 数值
    for (int i = 0; i <= majorSegments; ++i) {
        const double fraction = double(i) / majorSegments;
        const double degrees  = angleForFraction(fraction);

        painter.setPen(Theme::gaugeTick());
        painter.drawLine(polarPoint(center, degrees, rMajor),
                         polarPoint(center, degrees, rOuter));

        const double value = m_min + fraction * (m_max - m_min);
        const QPointF labelPos = polarPoint(center, degrees, rLabel);
        painter.setPen(Theme::axisText());
        painter.drawText(QRectF(labelPos.x() - 20, labelPos.y() - 8, 40, 16),
                         Qt::AlignCenter, QString::number(value, 'f', 0));
    }

    // 次刻度（主刻度之间）
    painter.setPen(Theme::gaugeTick());
    for (int i = 0; i < majorSegments; ++i) {
        for (int j = 1; j < minorPerMajor; ++j) {
            const double fraction = (double(i) + double(j) / minorPerMajor) / majorSegments;
            const double degrees  = angleForFraction(fraction);
            painter.drawLine(polarPoint(center, degrees, rMinor),
                             polarPoint(center, degrees, rOuter));
        }
    }

    // 4) 指针（尾部略过圆心，头部指向当前值）
    const double needleLength = radius - 16;
    const double needleDegrees = angleForFraction(normalizedValue());
    painter.setPen(QPen(Theme::gaugeNeedle(), 2.4, Qt::SolidLine, Qt::RoundCap));
    painter.drawLine(polarPoint(center, needleDegrees, -6),
                     polarPoint(center, needleDegrees, needleLength));

    // 中心轴帽
    painter.setPen(Qt::NoPen);
    painter.setBrush(Theme::gaugeNeedle());
    painter.drawEllipse(center, 4, 4);

    // 5) 数值 + 单位（弧下方开口处，指针尾部区域）
    const double valueY = center.y() + radius * 0.30;
    painter.setPen(Theme::textPrimary());
    painter.setFont(Theme::valueFont());
    painter.drawText(QRectF(center.x() - radius, valueY, radius * 2, 24),
                     Qt::AlignCenter, QString::number(m_value, 'f', 1));

    if (!m_unit.isEmpty()) {
        painter.setPen(Theme::textSecondary());
        painter.setFont(Theme::axisFont());
        painter.drawText(QRectF(center.x() - radius, valueY + 24, radius * 2, 16),
                         Qt::AlignCenter, m_unit);
    }
}

} // namespace ui
} // namespace dscope
