/**
 * @file settingspage.h
 * @brief 设置页（V2 重构：真实配置中心，替代 P5 纯占位骨架）
 *
 * ────────────────────────────────────────────────────────────
 * 为什么重构（对应审查 P0-2：设置页是纯空壳）
 * ────────────────────────────────────────────────────────────
 * V1 的 SettingsPage 只有占位文字，没有任何配置能力。
 * 真实工业监控软件的设置页必须能"编辑系统级配置 → 持久化 → 立即生效"。
 *
 * 本页提供的配置项（全部写入 ConfigManager，重启不丢失）：
 *   1. 界面主题（深色 / 亮色）—— 保存后立即切换（发 themeChanged 给主窗口）；
 *   2. 连接超时默认值（ms）    —— 供设备管理页新建设备时作为默认超时；
 *   3. 采样率（Hz）            —— 采集配置（数据记录元数据使用）；
 *   4. 日志级别                —— 日志过滤（写入日志目录 / 后续 LogManager 生效）。
 *
 * 数据流（对应 WPF 的 Settings/Options 窗口）：
 *   控件(QSpinBox/QComboBox) → 保存按钮 → ConfigManager.setValue + sync
 *   → themeChanged 信号 → 主窗口应用主题
 *
 * ── 设计要点 ──
 *   - 单一数据源：所有配置项只通过 ConfigManager 读写，页面不缓存第二份副本；
 *   - 立即生效：主题切换不走"重启生效"，保存即发信号（真实软件的即时反馈）；
 *   - 恢复默认：把配置重置为编译期默认常量，再写回存储。
 */

#pragma once

#include <QWidget>

class QSpinBox;
class QComboBox;
class QPushButton;
class QLabel;

namespace datascope {
namespace ui {

/**
 * @class SettingsPage
 * @brief 设置页（V2）：系统级配置编辑 + 持久化 + 即时生效
 *
 * 布局（表单单列 + 底部按钮区 + 状态提示区）：
 *   表单  主题 / 连接超时 / 采样率 / 日志级别
 *   按钮  保存设置 / 恢复默认
 */
class SettingsPage : public QWidget
{
    Q_OBJECT

public:
    /**
     * @brief 构造设置页
     * @param parent 父控件（主窗口 QTabWidget 页签容器）
     */
    explicit SettingsPage(QWidget *parent = nullptr);

    // 禁止拷贝：QObject 派生类不可复制
    Q_DISABLE_COPY(SettingsPage)

signals:
    /**
     * @brief 主题已更改（保存后立即发出）
     * @param theme "dark"（深色）或 "light"（亮色）；主窗口据此切换样式表
     */
    void themeChanged(const QString &theme);

private slots:
    void onSave();                 // 保存：写 ConfigManager + sync + 发信号
    void onResetDefaults();        // 恢复默认：重置为常量默认值并写回

private:
    void buildUi();                // 表单 + 按钮 + 状态提示
    void loadConfig();             // 构造：从 ConfigManager 读现有配置填充控件
    void applyTheme();             // 依据当前下拉值发 themeChanged（信号非 const，不能 const）

    // ---- 配置键（集中定义，避免魔法字符串散落）----
    static QString kThemeKey();        // "ui/theme"
    static QString kTimeoutKey();      // "device/defaultTimeoutMs"
    static QString kSampleRateKey();   // "acq/sampleRateHz"
    static QString kLogLevelKey();     // "log/level"

    // ---- 控件 ----
    QComboBox  *m_themeCombo = nullptr;   ///< 主题：深色/亮色
    QSpinBox   *m_timeoutSpin = nullptr;  ///< 连接超时（ms）
    QSpinBox   *m_sampleRateSpin = nullptr;///< 采样率（Hz）
    QComboBox  *m_logLevelCombo = nullptr;///< 日志级别
    QPushButton *m_saveBtn = nullptr;     ///< 保存设置
    QPushButton *m_resetBtn = nullptr;    ///< 恢复默认
    QLabel     *m_statusLabel = nullptr;  ///< 状态提示（保存成功/校验错误）
};

} // namespace ui
} // namespace datascope
