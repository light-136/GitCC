// ============================================================
//  程序入口
//
//  【Qt知识点】main 函数与事件循环：
//  - QApplication 是 GUI 程序必需的应用对象（持有全局状态、
//    事件循环、剪贴板等）。对比 WPF 的 Application 类。
//  - app.exec() 启动"事件循环"（等价 WPF 的 Dispatcher.Run），
//    之后程序靠信号/事件驱动，直到 quit() 才返回。
//
//  【Qt知识点】窗口显示流程：
//  new MainWindow() 构建窗口对象 → show() 显示 →
//  exec() 进入事件循环 → 界面响应事件。
//
//  支持命令行参数 --smoke：启动 1.5 秒后自动退出，
//  用于自动化冒烟测试（验证程序能正常启动、不崩溃）。
// ============================================================
#include <QApplication>
#include <QTimer>
#include "MainWindow.h"

int main(int argc, char *argv[])
{
    // 创建应用对象（QApplication 管理整个 GUI 程序的生命周期）
    QApplication app(argc, argv);

    // 应用元信息（供 QSettings 等使用，类比 AssemblyInfo）
    app.setApplicationName(QStringLiteral("GD32FirmwareUpdaterQt"));
    app.setApplicationVersion(QStringLiteral("1.0.0"));
    app.setOrganizationName(QStringLiteral("YuanCiZhiKong"));

    // 创建并显示主窗口
    MainWindow w;
    w.show();

    // 冒烟测试模式：1.5 秒后自动退出，用于自动化验证启动无异常
    if (app.arguments().contains(QStringLiteral("--smoke")))
        QTimer::singleShot(1500, &app, &QCoreApplication::quit);

    // 进入事件循环（此行阻塞，直到窗口关闭）
    return app.exec();
}
