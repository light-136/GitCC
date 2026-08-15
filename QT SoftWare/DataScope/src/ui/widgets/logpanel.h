/**
 * @file logpanel.h
 * @brief 日志面板（V2-执行③：审查 P1"无日志区域"遗留项落地）
 *
 * ────────────────────────────────────────────────────────────
 * 工程问题（为什么需要它）
 * ────────────────────────────────────────────────────────────
 * 真实工业监控软件必须能看到系统运行日志：连接成功/失败、协议错误、采集状态，
 * 排查问题全靠日志。V1 只有 LogManager 写文件，UI 看不到实时日志流 ——
 * 审查 P1"无日志区域"项要求补上实时可视化日志面板。
 *
 * ────────────────────────────────────────────────────────────
 * 设计（对照 WPF / C#）
 * ────────────────────────────────────────────────────────────
 *   - 观察者模式：订阅 LogManager::messageLogged 信号（≈ C# event += 回调），
 *     日志源源不断流入，UI 只做展示，不持有写日志能力；
 *   - QPlainTextEdit（只读）：大量文本场景用纯文本控件比 QTextEdit(富文本)
 *     性能好得多 —— 对应 C# 的 TextBox(ReadOnly) vs RichTextBox 的取舍；
 *   - 级别过滤：Warn+ / Error+ 只看严重日志，对应日志软件的 Level 过滤；
 *   - 环形缓冲：保留最近 kMaxEntries 条，内存有界，过滤切换时重建视图
 *     （不缓存全部日志 —— 工业软件常驻运行，日志可能几十万条）。
 */

#pragma once

#include <QVector>
#include <QWidget>

#include "infrastructure/logmanager.h"   // LogLevel 枚举（信号参数类型）

class QPlainTextEdit;
class QComboBox;
class QPushButton;
class QLabel;

namespace datascope {
namespace ui {

/**
 * @class LogPanel
 * @brief 实时日志面板：订阅 LogManager::messageLogged 并着色展示
 *
 * 使用方法：直接 new 后放进任意布局（主窗口以"日志"页签承载）。
 * 无需手动接信号 —— 构造时自动订阅全局 LogManager 单例。
 */
class LogPanel : public QWidget
{
    Q_OBJECT

public:
    /**
     * @brief 构造日志面板（自动订阅 LogManager 信号）
     * @param parent 父控件（对象树托管）
     */
    explicit LogPanel(QWidget *parent = nullptr);

    Q_DISABLE_COPY(LogPanel)

public slots:
    /**
     * @brief 接收一条日志（由 LogManager::messageLogged 触发）
     * @param level   日志级别（决定显示与否与颜色）
     * @param module  来源模块名
     * @param message 日志正文
     */
    void appendLog(infrastructure::LogLevel level,
                   const QString &module,
                   const QString &message);

private:
    /** @brief 单条日志记录（环形缓冲的元素） */
    struct LogEntry {
        infrastructure::LogLevel level;   ///< 日志级别
        QString module;                   ///< 来源模块
        QString message;                  ///< 正文
    };

    /** @brief 构建顶栏（过滤下拉 + 清空 + 计数）与日志视图 */
    void buildUi();

    /** @brief 过滤级别变化：重建视图（只显示 level >= 过滤值） */
    void onFilterChanged();

    /** @brief 清空缓冲与视图 */
    void clearLogs();

    /** @brief 依据当前过滤级别重建整个视图（缓冲 → 视图全量刷新） */
    void rebuildView();

    /**
     * @brief 向视图追加一行日志（着色 + 格式 + 自动滚底）
     * @param entry 日志条目
     */
    void appendLine(const LogEntry &entry);

    /** @brief 更新计数标签（总条数 / 显示条数） */
    void updateCountLabel();

    /** @brief 日志级别 → 显示颜色（深色主题下的可读配色） */
    static QColor colorForLevel(infrastructure::LogLevel level);

    /** @brief 日志级别 → 显示名 */
    static QString levelName(infrastructure::LogLevel level);

    // ---- 控件 ----
    QPlainTextEdit *m_view       = nullptr;   ///< 只读日志视图（等宽字体）
    QComboBox      *m_filterBox  = nullptr;   ///< 级别过滤下拉
    QPushButton    *m_clearBtn   = nullptr;   ///< 清空按钮
    QLabel         *m_countLabel = nullptr;   ///< 条数标签

    // ---- 数据 ----
    QVector<LogEntry> m_buffer;              ///< 环形缓冲（有界内存）
    infrastructure::LogLevel m_filter = infrastructure::LogLevel::Trace;  ///< 当前显示最低级别

    /** @brief 缓冲上限：保留最近 1000 条（工业软件常驻运行，内存有界） */
    static constexpr int kMaxEntries = 1000;
};

} // namespace ui
} // namespace datascope
