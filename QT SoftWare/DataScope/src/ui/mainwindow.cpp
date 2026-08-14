/**
 * @file mainwindow.cpp
 * @brief 主窗口实现（P1 空壳版）
 */

#include "ui/mainwindow.h"

#include <QString>

MainWindow::MainWindow(QWidget *parent)
    : QMainWindow(parent)
{
    // 设置窗口标题与初始尺寸
    setWindowTitle(tr("DataScope Studio —— 工业多通道数据采集与实时监控系统"));
    resize(1280, 800);
}

MainWindow::~MainWindow() = default;
