/**
 * @file settingspage.cpp
 * @brief 设置页实现（V2：真实配置中心）
 *
 * 配置的读取/写入完全委托 ConfigManager（线程安全 + 类型安全 + 自动落盘），
 * 页面只负责"编辑 → 校验 → 写入 → 通知生效"，不缓存第二份副本。
 * 对应 WPF 的设置窗口：控件绑定配置属性，保存时统一写回配置存储。
 */

#include "ui/pages/settingspage.h"

#include "infrastructure/configmanager.h"
#include "infrastructure/logmanager.h"

#include <QComboBox>
#include <QSpinBox>
#include <QPushButton>
#include <QLabel>
#include <QFormLayout>
#include <QVBoxLayout>
#include <QHBoxLayout>

namespace datascope {
namespace ui {

// ---- 编译期默认常量（恢复默认按钮的基准值）----
namespace {
const QString kThemeDark  = QStringLiteral("dark");   ///< 深色主题
const QString kThemeLight = QStringLiteral("light");  ///< 亮色主题
const int   kDefaultTimeoutMs  = 5000;  ///< 默认连接超时（毫秒）
const int   kDefaultSampleRate = 20;   ///< 默认采样率（Hz）
const QString kDefaultLogLevel = QStringLiteral("Info"); ///< 默认日志级别
} // namespace

// ---- 配置键定义（静态成员函数，避免匿名常量与类内声明不一致）----
QString SettingsPage::kThemeKey()      { return QStringLiteral("ui/theme"); }
QString SettingsPage::kTimeoutKey()    { return QStringLiteral("device/defaultTimeoutMs"); }
QString SettingsPage::kSampleRateKey() { return QStringLiteral("acq/sampleRateHz"); }
QString SettingsPage::kLogLevelKey()   { return QStringLiteral("log/level"); }

SettingsPage::SettingsPage(QWidget *parent)
    : QWidget(parent)
{
    setObjectName(QStringLiteral("settingsPage"));
    buildUi();
    loadConfig();   // 启动时加载已保存配置
}

void SettingsPage::buildUi()
{
    auto *layout = new QVBoxLayout(this);

    // ---- 标题 ----
    auto *title = new QLabel(tr("系统设置"), this);
    title->setObjectName(QStringLiteral("pageTitle"));
    title->setAlignment(Qt::AlignCenter);
    layout->addWidget(title);

    // ---- 配置表单 ----
    // 主题：深色（默认）/ 亮色 —— 保存后立即生效
    m_themeCombo = new QComboBox(this);
    m_themeCombo->addItem(tr("深色"), kThemeDark);
    m_themeCombo->addItem(tr("亮色"), kThemeLight);

    // 连接超时默认值：新建设备时作为默认连接超时（DevicePage 读取）
    m_timeoutSpin = new QSpinBox(this);
    m_timeoutSpin->setRange(100, 60000);
    m_timeoutSpin->setSingleStep(500);
    m_timeoutSpin->setSuffix(tr(" ms"));

    // 采样率：采集配置（Hz）
    m_sampleRateSpin = new QSpinBox(this);
    m_sampleRateSpin->setRange(1, 1000);
    m_sampleRateSpin->setSuffix(tr(" Hz"));

    // 日志级别：过滤写盘的日志（LogManager 使用）
    m_logLevelCombo = new QComboBox(this);
    for (const QString &level : {QStringLiteral("Trace"), QStringLiteral("Debug"),
                                 QStringLiteral("Info"), QStringLiteral("Warn"),
                                 QStringLiteral("Error"), QStringLiteral("Fatal")})
        m_logLevelCombo->addItem(level, level);

    auto *form = new QFormLayout;
    form->addRow(tr("界面主题："),     m_themeCombo);
    form->addRow(tr("连接超时默认："), m_timeoutSpin);
    form->addRow(tr("采样率："),       m_sampleRateSpin);
    form->addRow(tr("日志级别："),     m_logLevelCombo);
    layout->addLayout(form);

    // ---- 按钮区 ----
    m_saveBtn = new QPushButton(tr("保存设置"), this);
    m_saveBtn->setDefault(true);
    m_resetBtn = new QPushButton(tr("恢复默认"), this);

    connect(m_saveBtn, &QPushButton::clicked, this, &SettingsPage::onSave);
    connect(m_resetBtn, &QPushButton::clicked, this, &SettingsPage::onResetDefaults);

    auto *buttons = new QHBoxLayout;
    buttons->addWidget(m_saveBtn);
    buttons->addWidget(m_resetBtn);
    layout->addLayout(buttons);

    // ---- 状态提示区 ----
    m_statusLabel = new QLabel(tr("修改配置后点击保存生效"), this);
    m_statusLabel->setObjectName(QStringLiteral("pagePlaceholder"));
    m_statusLabel->setWordWrap(true);
    layout->addWidget(m_statusLabel, 1);
}

void SettingsPage::loadConfig()
{
    // 从 ConfigManager 读取现有配置（无则用默认值），填充控件
    const QString theme = datascope::infrastructure::ConfigManager::instance()
        .stringValue(kThemeKey(), kThemeDark);
    m_themeCombo->setCurrentIndex(m_themeCombo->findData(theme) >= 0
                                      ? m_themeCombo->findData(theme)
                                      : m_themeCombo->findData(kThemeDark));

    m_timeoutSpin->setValue(datascope::infrastructure::ConfigManager::instance()
        .intValue(kTimeoutKey(), kDefaultTimeoutMs));
    m_sampleRateSpin->setValue(datascope::infrastructure::ConfigManager::instance()
        .intValue(kSampleRateKey(), kDefaultSampleRate));

    const QString level = datascope::infrastructure::ConfigManager::instance()
        .stringValue(kLogLevelKey(), kDefaultLogLevel);
    m_logLevelCombo->setCurrentIndex(m_logLevelCombo->findData(level) >= 0
                                         ? m_logLevelCombo->findData(level)
                                         : m_logLevelCombo->findData(kDefaultLogLevel));
}

void SettingsPage::onSave()
{
    auto &cfg = datascope::infrastructure::ConfigManager::instance();

    // 校验：采样率必须为正（QSpinBox 已限制，双保险）
    if (m_sampleRateSpin->value() <= 0) {
        m_statusLabel->setText(tr("错误：采样率必须为正整数"));
        return;
    }

    // 写回配置（setValue 内部 QSettings 自动落盘 + sync 强制立即写文件）
    cfg.setValue(kThemeKey(),       m_themeCombo->currentData().toString());
    cfg.setValue(kTimeoutKey(),     m_timeoutSpin->value());
    cfg.setValue(kSampleRateKey(),  m_sampleRateSpin->value());
    cfg.setValue(kLogLevelKey(),    m_logLevelCombo->currentData().toString());
    cfg.sync();

    // 记录一条日志（真实动作，供日志面板追踪配置变更）
    datascope::infrastructure::LogManager::instance()
        .info(QStringLiteral("SettingsPage"),
              QStringLiteral("配置已保存：主题=%1 超时=%2ms 采样率=%3Hz 日志级别=%4")
                  .arg(m_themeCombo->currentData().toString())
                  .arg(m_timeoutSpin->value())
                  .arg(m_sampleRateSpin->value())
                  .arg(m_logLevelCombo->currentData().toString()));

    applyTheme();  // 主题立即生效（其他配置项由消费方按需读取）
    m_statusLabel->setText(tr("设置已保存并生效"));
}

void SettingsPage::onResetDefaults()
{
    // 恢复默认：控件重置为编译期默认值，再走一遍保存逻辑（写回 + 通知）
    m_themeCombo->setCurrentIndex(m_themeCombo->findData(kThemeDark));
    m_timeoutSpin->setValue(kDefaultTimeoutMs);
    m_sampleRateSpin->setValue(kDefaultSampleRate);
    m_logLevelCombo->setCurrentIndex(m_logLevelCombo->findData(kDefaultLogLevel));
    onSave();
    m_statusLabel->setText(tr("已恢复默认设置"));
}

void SettingsPage::applyTheme()
{
    // 通知主窗口切换主题（深色 → 加载 QSS；亮色 → 系统默认样式）
    // 注意：信号是非 const 成员，本方法不能声明 const
    emit themeChanged(m_themeCombo->currentData().toString());
}

} // namespace ui
} // namespace datascope
