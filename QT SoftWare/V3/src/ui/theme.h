/**
 * @file theme.h
 * @brief V3 UI 层 —— 主题 token 单一来源（颜色 + 字体）
 *
 * ── 开发思路 ──
 * V1/V2 的历史病灶：同一个颜色 hex 散落在 QSS、自绘控件、页面三处，换主题必然漏改，
 * 且"报警红/正常绿"在 C++ 里以 `QColor(0x..)` 硬编码重复出现。V3 把全部颜色/字体
 * 收敛为 `Theme` 这一处 token 源：任何绘制/布局代码只能通过 `Theme::xxx()` 取值，
 * 禁止手写裸 hex 或 `QColor(0x..)` 字面量。
 *
 * 落地方式取舍（对照 UI 架构审查"攻击点 5"）：
 *   审查建议"单一 C++ Theme 源 + QSS 模板替换 + 自绘控件注入"。本阶段自绘三控件
 *   （LineChartWidget/GaugeWidget/LedIndicator）的接口被任务锁定，没有 Theme 构造注入
 *   参数位，故采用"全静态方法"：无实例、无单例状态，每次调用返回常量 token。它依然
 *   满足"单一来源、禁裸 hex"，且比"传 Theme 指针"少一个生命周期参数。后续若做亮/暗
 *   双主题，可在此类加一个"当前主题枚举 + 两套色表"即可，调用点零改动。
 *
 * ── WPF 对照 ──
 *   Theme 静态方法  ↔  WPF 的 ResourceDictionary / DynamicResource：颜色在单一资源处
 *                      定义，控件只引用 key，不写死色值。
 */

#pragma once

#include <QColor>
#include <QFont>

namespace dscope {
namespace ui {

/**
 * @class Theme
 * @brief 颜色/字体 token 的单一来源（全静态，无状态，不可实例化）
 */
class Theme
{
public:
    // ────────────────────────────────────────────────────────────
    // 颜色 token（全部 QColor，禁止调用方手写 hex）
    // ────────────────────────────────────────────────────────────
    static QColor background();              ///< 全局深色背景
    static QColor panelBackground();         ///< 面板/卡片/绘图区背景
    static QColor border();                  ///< 面板/绘图区描边
    static QColor gridLine();                ///< 曲线网格线
    static QColor axisText();                ///< 坐标轴/刻度文字
    static QColor textPrimary();             ///< 主文字
    static QColor textSecondary();           ///< 次文字
    static QColor curveChannel(int index);   ///< 多通道曲线配色（按 index 循环取色）
    static QColor alarmRed();                ///< 报警红（亮，触发态）
    static QColor normalGreen();             ///< 正常绿（暗，非报警态）
    static QColor gaugeArc();                ///< 仪表弧带
    static QColor gaugeTick();               ///< 仪表刻度
    static QColor gaugeNeedle();             ///< 仪表指针
    static QColor statusConnected();         ///< 状态：已连接
    static QColor statusDisconnected();      ///< 状态：未连接
    static QColor statusError();             ///< 状态：错误
    static int    channelPaletteSize();      ///< 曲线配色表大小

    // ────────────────────────────────────────────────────────────
    // 字体 token
    // ────────────────────────────────────────────────────────────
    static QFont axisFont();                 ///< 坐标轴/刻度/时间小字（等宽）
    static QFont titleFont();                ///< 标题/通道名（中文）
    static QFont valueFont();                ///< 仪表读数/数值大字（等宽）

private:
    Theme() = delete;   // 纯静态工具类，禁止实例化
};

} // namespace ui
} // namespace dscope
