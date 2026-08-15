/**
 * @file theme.cpp
 * @brief V3 UI 层 —— 主题 token 实现（唯一允许出现 hex 色值的文件）
 *
 * ── 开发思路 ──
 * 全项目只有本文件写具体色值（QColor(0x..) 形式），其余文件一律 `Theme::xxx()` 取用。
 * 配色基调为工业深色：深灰背景 + 高对比多通道曲线 + 语义色（报警红亮 / 正常绿暗）。
 */

#include "ui/theme.h"

#include <QVector>

namespace dscope {
namespace ui {

namespace {

/**
 * @brief 多通道曲线配色表（深色背景上高对比、彼此易区分）
 * @note 函数内 static 局部变量：首次调用构造一次，线程安全（C++11 起保证）。
 */
const QVector<QColor> &channelPalette()
{
    static const QVector<QColor> palette = {
        QColor(0x00, 0xE5, 0xFF),   // 0 青
        QColor(0xFF, 0xB3, 0x00),   // 1 琥珀
        QColor(0xFF, 0x40, 0x81),   // 2 品红
        QColor(0xB3, 0x88, 0xFF),   // 3 紫
        QColor(0x69, 0xF0, 0xAE),   // 4 绿
        QColor(0xFF, 0x8A, 0x65),   // 5 珊瑚
        QColor(0x40, 0xC4, 0xFF),   // 6 亮蓝
        QColor(0xFF, 0xFF, 0x8D),   // 7 黄
    };
    return palette;
}

} // namespace

// ──────────────────────────────────────────── 颜色 ────────────────────────────────────────────

QColor Theme::background()         { return QColor(0x1E, 0x1F, 0x22); }   // 深灰
QColor Theme::panelBackground()    { return QColor(0x2B, 0x2D, 0x31); }   // 面板深灰
QColor Theme::border()             { return QColor(0x3F, 0x42, 0x47); }   // 描边
QColor Theme::gridLine()           { return QColor(0x3A, 0x3D, 0x42); }   // 网格线
QColor Theme::axisText()           { return QColor(0x9A, 0xA0, 0xA6); }   // 坐标文字
QColor Theme::textPrimary()        { return QColor(0xE6, 0xE8, 0xEB); }   // 主文字
QColor Theme::textSecondary()      { return QColor(0x9A, 0xA0, 0xA6); }   // 次文字
QColor Theme::alarmRed()           { return QColor(0xFF, 0x4D, 0x4F); }   // 报警红（亮）
QColor Theme::normalGreen()        { return QColor(0x2E, 0x7D, 0x32); }   // 正常绿（暗）
QColor Theme::gaugeArc()           { return QColor(0x3F, 0x42, 0x47); }   // 仪表弧带
QColor Theme::gaugeTick()          { return QColor(0x9A, 0xA0, 0xA6); }   // 仪表刻度
QColor Theme::gaugeNeedle()        { return QColor(0xFF, 0x8A, 0x65); }   // 仪表指针（珊瑚）
QColor Theme::statusConnected()    { return QColor(0x2E, 0xCC, 0x71); }   // 已连接绿
QColor Theme::statusDisconnected() { return QColor(0x9A, 0xA0, 0xA6); }   // 未连接灰
QColor Theme::statusError()        { return QColor(0xFF, 0x4D, 0x4F); }   // 错误红

QColor Theme::curveChannel(int index)
{
    const QVector<QColor> &palette = channelPalette();
    if (palette.isEmpty())
        return textPrimary();                                   // 防御：空表兜底
    return palette.at(index % palette.size());                  // 循环取色，通道数可超表长
}

int Theme::channelPaletteSize()
{
    return channelPalette().size();
}

// ──────────────────────────────────────────── 字体 ────────────────────────────────────────────

QFont Theme::axisFont()
{
    QFont font(QStringLiteral("Consolas"));   // 等宽，坐标/时间对齐
    font.setPointSize(8);
    return font;
}

QFont Theme::titleFont()
{
    QFont font(QStringLiteral("Microsoft YaHei"));   // 中文标题
    font.setPointSize(9);
    font.setBold(true);
    return font;
}

QFont Theme::valueFont()
{
    QFont font(QStringLiteral("Consolas"));   // 等宽，数值稳定不跳动
    font.setPointSize(14);
    font.setBold(true);
    return font;
}

} // namespace ui
} // namespace dscope
