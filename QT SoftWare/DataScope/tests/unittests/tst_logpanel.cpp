/**
 * @file tst_logpanel.cpp
 * @brief 日志面板单测（V2-执行③：审查 P1"无日志区域"修复的测试保障）
 *
 * 测试策略（黑盒 UI 测试）：
 *   - LogPanel 内部控件为私有成员，通过 objectName + findChild 定位 ——
 *     对应 WPF 自动化测试里 FindName 定位 UI 元素，不改测试 API 白盒依赖；
 *   - 每次用例都 init LogManager 到独立临时目录（进程级单例，杜绝污染）；
 *   - 断言基于视图文本（toPlainText），不截图 —— 只测逻辑不测像素。
 *
 * 覆盖：
 *   1. 日志实时上屏：info 日志出现在视图（含级别/模块/正文）；
 *   2. 级别过滤：切到 Warn+ 后 Info 消失、Error 保留；
 *   3. 清空按钮：一键清空缓冲与视图；
 *   4. 缓冲有界：超过上限后最旧日志被淘汰，视图不无限增长。
 */

#include <QtTest>

#include <QComboBox>
#include <QDir>
#include <QPlainTextEdit>
#include <QPushButton>
#include <QTemporaryDir>

#include "ui/widgets/logpanel.h"
#include "infrastructure/logmanager.h"

using datascope::ui::LogPanel;
using datascope::infrastructure::LogManager;
using datascope::infrastructure::LogLevel;

class TestLogPanel : public QObject
{
    Q_OBJECT

private slots:
    void logAppearsInView();          // 1. 日志实时上屏
    void filterHidesInfoKeepsError(); // 2. 级别过滤
    void clearButtonEmptiesView();    // 3. 清空按钮
    void bufferStaysBounded();        // 4. 缓冲有界
};

// ================= 1. 日志实时上屏 =================

void TestLogPanel::logAppearsInView()
{
    // 隔离：独立临时目录，单例重新 init
    QTemporaryDir tempDir(QDir::tempPath() + QStringLiteral("/dscore_logpanel_XXXXXX"));
    QVERIFY(tempDir.isValid());
    LogManager::instance().init(tempDir.path());

    LogPanel panel;   // 构造即订阅全局日志流

    // 发一条 Info 日志 → 应立即出现在面板视图
    LogManager::instance().info(QStringLiteral("ModA"), QStringLiteral("hello log panel"));

    QPlainTextEdit *view = panel.findChild<QPlainTextEdit*>(QStringLiteral("logView"));
    QVERIFY2(view, "面板应包含日志视图控件（objectName=logView）");
    const QString text = view->toPlainText();
    QVERIFY2(text.contains(QStringLiteral("hello log panel")), "正文应上屏");
    QVERIFY2(text.contains(QStringLiteral("[ModA]")), "模块名应上屏");
    QVERIFY2(text.contains(QStringLiteral("[Info]")), "级别标签应上屏");
}

// ================= 2. 级别过滤 =================

void TestLogPanel::filterHidesInfoKeepsError()
{
    QTemporaryDir tempDir(QDir::tempPath() + QStringLiteral("/dscore_logpanel_XXXXXX"));
    QVERIFY(tempDir.isValid());
    LogManager::instance().init(tempDir.path());

    LogPanel panel;
    LogManager::instance().info(QStringLiteral("ModA"), QStringLiteral("info line"));
    LogManager::instance().error(QStringLiteral("ModA"), QStringLiteral("error line"));

    QComboBox *combo = panel.findChild<QComboBox*>(QStringLiteral("logFilterCombo"));
    QVERIFY2(combo, "面板应包含过滤下拉（objectName=logFilterCombo）");

    // 切到 Warn+（下拉第 4 项）：Info 应被隐藏，Error 保留
    combo->setCurrentIndex(3);

    QPlainTextEdit *view = panel.findChild<QPlainTextEdit*>(QStringLiteral("logView"));
    const QString text = view->toPlainText();
    QVERIFY2(!text.contains(QStringLiteral("info line")), "Warn+ 过滤下 Info 不应显示");
    QVERIFY2(text.contains(QStringLiteral("error line")), "Warn+ 过滤下 Error 应保留");
}

// ================= 3. 清空按钮 =================

void TestLogPanel::clearButtonEmptiesView()
{
    QTemporaryDir tempDir(QDir::tempPath() + QStringLiteral("/dscore_logpanel_XXXXXX"));
    QVERIFY(tempDir.isValid());
    LogManager::instance().init(tempDir.path());

    LogPanel panel;
    LogManager::instance().info(QStringLiteral("ModA"), QStringLiteral("to be cleared"));

    QPlainTextEdit *view = panel.findChild<QPlainTextEdit*>(QStringLiteral("logView"));
    QVERIFY(!view->toPlainText().isEmpty());   // 清空前有内容

    QPushButton *clearBtn = panel.findChild<QPushButton*>(QStringLiteral("logClearButton"));
    QVERIFY2(clearBtn, "面板应包含清空按钮（objectName=logClearButton）");
    clearBtn->click();   // 模拟用户点击

    QVERIFY2(view->toPlainText().isEmpty(), "点击清空后视图应为空");
}

// ================= 4. 缓冲有界 =================

void TestLogPanel::bufferStaysBounded()
{
    QTemporaryDir tempDir(QDir::tempPath() + QStringLiteral("/dscore_logpanel_XXXXXX"));
    QVERIFY(tempDir.isValid());
    LogManager::instance().init(tempDir.path());

    LogPanel panel;

    // 发超过缓冲上限的日志（上限 1000，与 LogPanel::kMaxEntries 对齐）
    constexpr int kMaxEntries = 1000;
    for (int i = 0; i < kMaxEntries + 50; ++i)
        LogManager::instance().info(QStringLiteral("Load"),
                                    QStringLiteral("msg %1").arg(i));

    QPlainTextEdit *view = panel.findChild<QPlainTextEdit*>(QStringLiteral("logView"));
    // 视图行数不随日志无限增长，被钳制在上限内
    const QStringList lines =
        view->toPlainText().split(QLatin1Char('\n'), QString::SkipEmptyParts);
    QVERIFY2(lines.size() <= kMaxEntries, "视图行数应被缓冲上限约束");

    // 最旧日志被淘汰，最新日志保留
    QVERIFY2(!view->toPlainText().contains(QStringLiteral("msg 0")),
             "最旧的 msg 0 应被淘汰");
    QVERIFY2(view->toPlainText().contains(
                 QStringLiteral("msg %1").arg(kMaxEntries + 49)),
             "最新的日志应保留");
}

// 生成 main()：QTEST_MAIN 自动创建 QApplication（GUI 控件可实例化）
QTEST_MAIN(TestLogPanel)
#include "tst_logpanel.moc"
