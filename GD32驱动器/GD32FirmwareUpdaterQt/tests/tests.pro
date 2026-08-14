# ============================================================
#  单元测试工程（Qt Test 框架）
#
#  【Qt知识点】testlib 模块：
#  - QT += testlib 提供 QTest（类比 C# 的 xUnit/NUnit）
#  - 每个测试类继承 QObject，用 QTest::qExec 运行
#  - 直接复用 src 的协议层/解析器源码（无需单独的测试项目引用）
# ============================================================

QT += core testlib serialport
QT -= gui

CONFIG += console c++17
CONFIG -= app_bundle

TEMPLATE = app
TARGET = runtests

INCLUDEPATH += ../src

SOURCES += \
    tst_all.cpp \
    tst_crc.cpp \
    tst_frame.cpp \
    tst_parser.cpp \
    ../src/protocol/CrcCalculator.cpp \
    ../src/protocol/ProtocolFrame.cpp \
    ../src/services/FirmwareParser.cpp

HEADERS += \
    tst_crc.h \
    tst_frame.h \
    tst_parser.h

HEADERS += \
    ../src/protocol/CommandDefinitions.h \
    ../src/protocol/CrcCalculator.h \
    ../src/protocol/ProtocolFrame.h \
    ../src/services/FirmwareParser.h \
    ../src/models/FirmwareInfo.h \
    ../src/models/SerialPortConfig.h

# 统一测试入口 tst_all.cpp 依次执行各测试类；
# tst_crc/tst_frame/tst_parser 仅定义测试类（不含 main）避免链接冲突
