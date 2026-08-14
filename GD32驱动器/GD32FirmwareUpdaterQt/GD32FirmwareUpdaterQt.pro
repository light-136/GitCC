# ============================================================
#  GD32 低压驱动器固件升级工具（Qt版） — 主工程文件
#
#  【Qt知识点】qmake 工程文件语法：
#  - QT 变量声明使用的模块（编译时链接，类似 C# 的 using/引用）
#  - CONFIG 声明编译选项（c++17 类似 csproj 的 LangVersion）
#  - TARGET 生成的可执行文件名（类似 AssemblyName）
#  - TEMPLATE = app 表示生成应用程序；若为 lib 则生成库
#  - SOURCES/HEADERS 声明源码（qmake 会自动做依赖跟踪 + MOC 处理）
#
#  目标工具链：Qt 5.12.2 MinGW 7.3.0 64bit（本机 D:/Qt）
#  author: Claude (仿写自 GD32FirmwareUpdater WPF 版)
# ============================================================

QT += core gui serialport
# serialport：Qt 串口模块，对应 C# 的 System.IO.Ports

greaterThan(QT_MAJOR_VERSION, 4): QT += widgets

TARGET = GD32FirmwareUpdaterQt
TEMPLATE = app

# 启用 C++17（配合 MinGW 7.3，标准库特性齐全）
CONFIG += c++17

# 源码根目录（相对 include 的搜索路径）
INCLUDEPATH += src

# ---------------- 源文件清单 ----------------
SOURCES += \
    src/main.cpp \
    src/MainWindow.cpp \
    src/protocol/CrcCalculator.cpp \
    src/protocol/ProtocolFrame.cpp \
    src/services/SerialPortService.cpp \
    src/services/FirmwareParser.cpp \
    src/services/FirmwareUpgradeService.cpp \
    src/models/UpgradePacketModel.cpp

HEADERS += \
    src/MainWindow.h \
    src/protocol/CommandDefinitions.h \
    src/protocol/CrcCalculator.h \
    src/protocol/ProtocolFrame.h \
    src/services/SerialPortService.h \
    src/services/FirmwareParser.h \
    src/services/FirmwareUpgradeService.h \
    src/models/SerialPortConfig.h \
    src/models/FirmwareInfo.h \
    src/models/UpgradePacketModel.h

# ---------------- 部署配置 ----------------
# Windows 平台编译时，自动把输出复制到 build 目录下便于整理
DESTDIR = build
OBJECTS_DIR = build/obj
MOC_DIR = build/moc
RCC_DIR = build/rcc
UI_DIR = build/ui
