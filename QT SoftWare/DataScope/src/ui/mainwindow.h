/**
 * @file mainwindow.h
 * @brief 主窗口声明（P1 为空壳版本，P5 扩展为完整主框架）
 *
 * 对应 WPF 的 MainWindow.xaml + code-behind。
 * 差异提醒（"不可简单照搬"点之一）：
 *   WPF 用 XAML 声明 UI；Qt Widgets 用 C++ 代码构建 UI。
 *   本类继承 QMainWindow 获得：菜单栏 / 工具栏 / 状态栏 / 停靠窗口 四大区域骨架。
 */

#pragma once

#include <QMainWindow>

/**
 * @class MainWindow
 * @brief 应用主窗口
 *
 * Q_OBJECT 宏是 Qt 元对象系统的入口：
 *   - 启用信号槽、属性系统、元对象信息（对应 C# 反射能力）；
 *   - 编译时由 AUTOMOC 调用 moc 生成 *_moc.cpp 元对象代码。
 */
class MainWindow : public QMainWindow
{
    Q_OBJECT

public:
    /** @brief 构造：初始化 UI（P1 仅设置标题与初始尺寸） */
    explicit MainWindow(QWidget *parent = nullptr);
    /** @brief 析构：Qt 对象树自动清理子对象，一般无需手动 delete */
    ~MainWindow() override;

    // 禁止拷贝：QObject 不允许复制（与 C# 引用语义不同，Qt 强制唯一所有权）
    Q_DISABLE_COPY(MainWindow)
};
