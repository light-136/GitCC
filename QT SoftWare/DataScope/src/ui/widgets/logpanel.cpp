/**
 * @file logpanel.cpp
 * @brief 日志面板实现（V2-执行③）
 *
 * 数据流：LogManager::messageLogged(level, module, message)
 *            → appendLog（主线程直连，信号槽同线程直接调用）
 *            → 缓冲 + 着色追加到 QPlainTextEdit
 *
 * 教学点（对照 WPF）：
 *   - QTextCursor + QTextCharFormat ≈ WPF RichTextBox 的段落/文本范围着色；
 *   - QComboBox + QVariant 存枚举 ≈ WPF ComboBox 的 SelectedValue 绑定；
 *   - 环形缓冲 + 过滤重建 ≈ 日志查看器"只留最近 N 条 + 按级别筛选"。
 */

#include "ui/widgets/logpanel.h"

#include <QPlainTextEdit>
#include <QComboBox>
#include <QPushButton>
#include <QLabel>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QTextCursor>
#include <QTextCharFormat>
#include <QColor>
#include <QTime>
#include <QScrollBar>

namespace datascope {
namespace ui {

namespace {
/** @brief 过滤下拉的选项表：显示名 + 对应最低级别（常量表，便于扩展） */
const struct FilterOption {
    const char *label;                        ///< 下拉显示名
    infrastructure::LogLevel level;           ///< 该档显示的最低级别
} kFilterOptions[] = {
    { "All",    infrastructure::LogLevel::Trace },
    { "Debug+", infrastructure::LogLevel::Debug },
    { "Info+",  infrastructure::LogLevel::Info  },
    { "Warn+",  infrastructure::LogLevel::Warn  },
    { "Error+", infrastructure::LogLevel::Error },
    { "Fatal",  infrastructure::LogLevel::Fatal },
};
constexpr int kFilterCount = sizeof(kFilterOptions) / sizeof(kFilterOptions[0]);
} // namespace

LogPanel::LogPanel(QWidget *parent)
    : QWidget(parent)
{
    buildUi();

    // ---- 订阅全局日志流（观察者模式）----
    // 教学点：信号可以是"单例发广播、多个订阅者"—— LogManager 发一条日志，
    // 文件落盘（内部）+ 本面板实时展示（UI），互不干扰。
    connect(&infrastructure::LogManager::instance(),
            &infrastructure::LogManager::messageLogged,
            this, &LogPanel::appendLog);
}

// ---------------------------------------------------------------------------
// UI 构建
// ---------------------------------------------------------------------------

void LogPanel::buildUi()
{
    // ---- 顶栏：级别过滤 + 清空 + 条数 ----
    m_filterBox = new QComboBox(this);
    m_filterBox->setObjectName(QStringLiteral("logFilterCombo"));  // QSS 定位 + 测试寻址
    m_filterBox->setToolTip(tr("按日志级别过滤显示"));
    for (int i = 0; i < kFilterCount; ++i) {
        // 用 QVariant 把枚举值绑定到条目（对应 WPF ComboBox ItemSource+SelectedValue）
        m_filterBox->addItem(tr(kFilterOptions[i].label),
                             static_cast<int>(kFilterOptions[i].level));
    }
    m_filterBox->setCurrentIndex(0);   // 默认 All

    m_clearBtn = new QPushButton(tr("清空"), this);
    m_clearBtn->setObjectName(QStringLiteral("logClearButton"));   // 测试寻址
    m_clearBtn->setToolTip(tr("清空当前日志面板"));

    m_countLabel = new QLabel(tr("共 0 条"), this);
    m_countLabel->setStyleSheet(QStringLiteral("color:#8a9aaa; padding:0 8px;"));

    auto *topBar = new QHBoxLayout;
    topBar->setContentsMargins(0, 0, 0, 4);
    topBar->addWidget(m_filterBox);
    topBar->addWidget(m_clearBtn);
    topBar->addStretch(1);
    topBar->addWidget(m_countLabel);

    // ---- 日志视图：只读纯文本（等宽字体由 QSS 统一设置）----
    m_view = new QPlainTextEdit(this);
    m_view->setObjectName(QStringLiteral("logView"));          // 测试寻址
    m_view->setReadOnly(true);
    m_view->setLineWrapMode(QPlainTextEdit::NoWrap);   // 不自动换行，便于阅读原始日志

    // ---- 总布局：顶栏 + 视图 ----
    auto *layout = new QVBoxLayout(this);
    layout->setContentsMargins(8, 8, 8, 8);
    layout->addLayout(topBar);
    layout->addWidget(m_view, /*stretch=*/1);

    // ---- 信号接线 ----
    connect(m_filterBox, QOverload<int>::of(&QComboBox::currentIndexChanged),
            this, &LogPanel::onFilterChanged);
    connect(m_clearBtn, &QPushButton::clicked, this, &LogPanel::clearLogs);
}

// ---------------------------------------------------------------------------
// 日志接入
// ---------------------------------------------------------------------------

void LogPanel::appendLog(infrastructure::LogLevel level,
                         const QString &module,
                         const QString &message)
{
    LogEntry entry{ level, module, message };

    // 环形缓冲：先入队，超上限裁剪最旧的（内存有界）
    m_buffer.append(entry);
    if (m_buffer.size() > kMaxEntries) {
        m_buffer.remove(0, m_buffer.size() - kMaxEntries);
        rebuildView();   // 裁剪后缓冲与视图错位，全量重建（低频，代价可接受）
        return;
    }

    // 通过级别过滤才实时追加到视图
    if (level >= m_filter)
        appendLine(entry);
    updateCountLabel();
}

void LogPanel::appendLine(const LogEntry &entry)
{
    // 行格式：时间戳 [级别] [模块] 消息 —— 与 LogManager 落盘格式一致，便于对照
    const QString line = QStringLiteral("[%1] [%2] [%3] %4")
        .arg(QTime::currentTime().toString(QStringLiteral("HH:mm:ss.zzz")),
             levelName(entry.level),
             entry.module,
             entry.message);

    // 用文本光标定位到文档末尾插入，并按级别着色（QTextCharFormat ≈ WPF Run.Foreground）
    QTextCursor cursor(m_view->document());
    cursor.movePosition(QTextCursor::End);
    QTextCharFormat fmt;
    fmt.setForeground(colorForLevel(entry.level));
    if (entry.level == infrastructure::LogLevel::Fatal)
        fmt.setFontWeight(QFont::Bold);   // Fatal 加粗强调
    cursor.insertText(line + QLatin1Char('\n'), fmt);

    // 自动滚底：始终显示最新日志（日志查看器标准行为）
    m_view->verticalScrollBar()->setValue(m_view->verticalScrollBar()->maximum());
}

// ---------------------------------------------------------------------------
// 过滤 / 清空
// ---------------------------------------------------------------------------

void LogPanel::onFilterChanged()
{
    m_filter = static_cast<infrastructure::LogLevel>(
        m_filterBox->currentData().toInt());
    rebuildView();
}

void LogPanel::rebuildView()
{
    m_view->clear();   // 清空视图 → 按当前过滤级别重放缓冲
    for (const LogEntry &entry : m_buffer) {
        if (entry.level >= m_filter)
            appendLine(entry);
    }
    updateCountLabel();
}

void LogPanel::clearLogs()
{
    m_buffer.clear();
    m_view->clear();
    updateCountLabel();
}

void LogPanel::updateCountLabel()
{
    // 显示条数需遍历统计（仅过滤后重放时调用，量级 1000 内可接受）
    int shown = 0;
    for (const LogEntry &entry : m_buffer)
        if (entry.level >= m_filter)
            ++shown;
    m_countLabel->setText(tr("显示 %1 / 共 %2 条").arg(shown).arg(m_buffer.size()));
}

// ---------------------------------------------------------------------------
// 级别 → 颜色 / 名称
// ---------------------------------------------------------------------------

QColor LogPanel::colorForLevel(infrastructure::LogLevel level)
{
    // 深色主题配色：Trace/Debug 灰、Info 浅绿、Warn 橙、Error 红、Fatal 亮红
    switch (level) {
    case infrastructure::LogLevel::Trace:
    case infrastructure::LogLevel::Debug:
        return QColor(QStringLiteral("#7f8c8d"));
    case infrastructure::LogLevel::Info:
        return QColor(QStringLiteral("#bcd8c7"));
    case infrastructure::LogLevel::Warn:
        return QColor(QStringLiteral("#f39c12"));
    case infrastructure::LogLevel::Error:
        return QColor(QStringLiteral("#e74c3c"));
    case infrastructure::LogLevel::Fatal:
        return QColor(QStringLiteral("#ff4757"));
    }
    return QColor(QStringLiteral("#ecf0f1"));   // 防御：未知级别用浅色
}

QString LogPanel::levelName(infrastructure::LogLevel level)
{
    switch (level) {
    case infrastructure::LogLevel::Trace: return QStringLiteral("Trace");
    case infrastructure::LogLevel::Debug: return QStringLiteral("Debug");
    case infrastructure::LogLevel::Info:  return QStringLiteral("Info");
    case infrastructure::LogLevel::Warn:  return QStringLiteral("Warn");
    case infrastructure::LogLevel::Error: return QStringLiteral("Error");
    case infrastructure::LogLevel::Fatal: return QStringLiteral("Fatal");
    }
    return QStringLiteral("Unknown");
}

} // namespace ui
} // namespace datascope
