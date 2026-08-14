/**
 * @file main.cpp
 * @brief DataScope Studio 程序入口
 *
 * 对应 WPF 中的 App.xaml + Main() 位置：
 *   - App.xaml 里的 <Application .../>          → QApplication
 *   - App.xaml.cs 里的 Main() / StartupUri       → 本文件的 app + window.show()
 *   - XAML 全局资源（App.xaml Resources）         → 后续阶段在 QApplication 上注册 QSS/图标
 *
 * Qt 程序生命周期的三个关键点（P1 重点理解）：
 *   1. QApplication 构造：初始化 GUI 子系统、高 DPI、事件系统全局实例；
 *   2. 进入事件循环 exec()：exec() 阻塞在 C++ 线程里，由 Qt 驱动事件分发，
 *      这与 C# 的 [STAThread] Main() + Application.Run() 思想一致；
 *   3. exec() 返回后：程序自然退出，QApplication 析构清理全局资源。
 */

#include <QApplication>
#include <QFont>
#include "ui/mainwindow.h"
#include "infrastructure/logmanager.h"
#include "infrastructure/configmanager.h"

int main(int argc, char *argv[])
{
    // 创建 QApplication（GUI 程序必须且只能有一个）
    QApplication app(argc, argv);

    // 设置应用元信息：后续 QSettings 默认使用 organizationName/applicationName
    // 作为 INI/注册表路径，等同 WPF 里的 AssemblyInfo + app.config
    app.setOrganizationName(QStringLiteral("DataScope"));
    app.setApplicationName(QStringLiteral("DataScope Studio"));
    app.setApplicationVersion(QStringLiteral("0.1.0"));

    // P6/P7 基础设施启动即落地：日志与配置在用户目录初始化（AppDataLocation）
    // 先 init 日志再 init 配置，保证后续任何模块写日志都有落点
    datascope::infrastructure::LogManager::instance().init();
    datascope::infrastructure::ConfigManager::instance().init();
    datascope::infrastructure::LogManager::instance().info(
        "Main", QStringLiteral("DataScope Studio 启动 v0.1.0"));

    // 创建主窗口并显示（窗口对象在栈上，由 main 结束自动析构）
    MainWindow window;
    window.show();

    // 进入 Qt 事件循环：阻塞直到所有窗口关闭
    return app.exec();
}
