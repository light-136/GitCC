# =============================================================================
# DataScope Studio —— 工业多通道数据采集与实时监控系统
# 顶层 CMake 构建脚本（主 Agent 唯一维护的共享文件）
# 工具链：Qt 5.12.2 (mingw73_64) + MinGW 7.3.0 + CMake 3.25 + Ninja
# =============================================================================

# 最低 CMake 版本：3.16 以上才完整支持 Qt5 的 AUTOMOC/AUTORCC/AUTOUIC
cmake_minimum_required(VERSION 3.16)

# 工程名与语言
project(DataScope VERSION 0.1.0 LANGUAGES CXX)

# ----------------------------------------------------------------------------
# 编译标准：C++17
# ----------------------------------------------------------------------------
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# ----------------------------------------------------------------------------
# Qt 元对象编译三开关（AUTOMOC/AUTORCC/AUTOUIC）
# ----------------------------------------------------------------------------
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)
set(CMAKE_AUTOUIC ON)

# ----------------------------------------------------------------------------
# 查找 Qt5 模块（Test 供单测 target 使用）
# ----------------------------------------------------------------------------
find_package(Qt5 COMPONENTS Core Gui Widgets Network Test REQUIRED)

# 源文件根目录（供各 target 统一 include）
set(DS_SRC_DIR ${CMAKE_CURRENT_SOURCE_DIR}/src)

# ============================================================================
# 1) 工具库静态库：datascope_utils
#    独立成库的原因：
#      - 主程序与单测都依赖它，一个库两个消费者（对应 WPF 的"类库项目"）；
#      - 后续 protocol / domain 也作为库加入，保持模块边界清晰。
# ============================================================================
add_library(datascope_utils STATIC
    src/utils/byteutils.cpp
    src/utils/byteutils.h
    src/utils/timeutils.cpp
    src/utils/timeutils.h
)
# PUBLIC：库的使用者（主程序、测试）自动获得 src 头文件路径与 Qt5::Core
target_include_directories(datascope_utils PUBLIC ${DS_SRC_DIR})
target_link_libraries(datascope_utils PUBLIC Qt5::Core)

# ============================================================================
# 2) 基础设施静态库：datascope_infra（P6 日志 / P7 配置）
#    独立成库：主程序与单测共用；日志/配置是全工程的地基，先于业务模块稳定
# ============================================================================
add_library(datascope_infra STATIC
    src/infrastructure/logmanager.cpp
    src/infrastructure/logmanager.h
    src/infrastructure/configmanager.cpp
    src/infrastructure/configmanager.h
)
target_include_directories(datascope_infra PUBLIC ${DS_SRC_DIR})
target_link_libraries(datascope_infra PUBLIC Qt5::Core)

# ============================================================================
# 2.5) 领域模型静态库：datascope_domain（P8：Device/Channel/DataPoint）
#    独立成库：纯数据模型，只依赖 QtCore；后续 protocol / services / UI 都消费它
#    V2 增加：领域模型(domainmodel_v2) + Qt Model/View 模型(devicelistmodel)
# ============================================================================
add_library(datascope_domain STATIC
    src/domain/models.cpp
    src/domain/models.h
    src/domain/domainmodel_v2.cpp
    src/domain/domainmodel_v2.h
    src/ui/models/devicelistmodel.cpp
    src/ui/models/devicelistmodel.h
    src/ui/models/alarmeventmodel.cpp
    src/ui/models/alarmeventmodel.h
)
target_include_directories(datascope_domain PUBLIC ${DS_SRC_DIR})
target_link_libraries(datascope_domain PUBLIC Qt5::Core)

# ============================================================================
# 2.6) 协议引擎静态库：datascope_protocol（P10：Crc16/FrameBuilder/FrameParser）
#    独立成库：串口(P9)、TCP客户端(P11)、模拟设备(Simulator)都消费它
# ============================================================================
add_library(datascope_protocol STATIC
    src/protocol/protocoltypes.h
    src/protocol/crc16.cpp
    src/protocol/crc16.h
    src/protocol/framebuilder.cpp
    src/protocol/framebuilder.h
    src/protocol/frameparser.cpp
    src/protocol/frameparser.h
)
target_include_directories(datascope_protocol PUBLIC ${DS_SRC_DIR})
target_link_libraries(datascope_protocol PUBLIC Qt5::Core)

# ============================================================================
# 2.7) 服务层静态库：datascope_services（P11 TcpClient / P12 DataService+Worker）
#    主程序与测试共用；服务层组合协议引擎 + 领域模型 + 网络通信
# ============================================================================
add_library(datascope_services STATIC
    src/services/tcpclient.cpp
    src/services/tcpclient.h
    src/services/acquisitionworker.cpp
    src/services/acquisitionworker.h
    src/services/dataservice.cpp
    src/services/dataservice.h
    src/services/devicecontroller.cpp
    src/services/devicecontroller.h
    src/services/recordmanager.cpp
    src/services/recordmanager.h
    src/services/alarmengine.cpp
    src/services/alarmengine.h
)
target_include_directories(datascope_services PUBLIC ${DS_SRC_DIR})
target_link_libraries(datascope_services PUBLIC
    Qt5::Core
    Qt5::Network
    datascope_protocol
    datascope_domain
)

# ============================================================================
# 2) 主程序：DataScope
# ============================================================================
add_executable(DataScope
    # ---- 程序入口 ----
    src/app/main.cpp
    # ---- 业务层（P4：信号槽教学演示对象）----
    src/app/signalslotdemo.cpp
    src/app/signalslotdemo.h
    # ---- UI 层：主窗口 + 5 个业务页面 ----
    src/ui/mainwindow.cpp
    src/ui/mainwindow.h
    src/ui/pages/monitorpage.cpp
    src/ui/pages/monitorpage.h
    src/ui/pages/devicepage.cpp
    src/ui/pages/devicepage.h
    src/ui/pages/recordpage.cpp
    src/ui/pages/recordpage.h
    src/ui/pages/settingspage.cpp
    src/ui/pages/settingspage.h
    src/ui/pages/demopage.cpp
    src/ui/pages/demopage.h
    # ---- UI 层：自绘控件（P14：实时曲线 / 仪表盘 / LED）----
    src/ui/widgets/linechartwidget.cpp
    src/ui/widgets/linechartwidget.h
    src/ui/widgets/gaugewidget.cpp
    src/ui/widgets/gaugewidget.h
    src/ui/widgets/ledindicator.cpp
    src/ui/widgets/ledindicator.h
    # ---- 资源（AUTORCC 会把 qss 编译进可执行文件）----
    resources/resources.qrc
)

target_include_directories(DataScope PRIVATE ${DS_SRC_DIR})
target_link_libraries(DataScope PRIVATE
    Qt5::Core
    Qt5::Gui
    Qt5::Widgets
    datascope_utils
    datascope_infra
    datascope_domain
    datascope_protocol
    datascope_services
)

# ----------------------------------------------------------------------------
# 部署 Qt 运行库到目标可执行文件目录（等价手动 windeployqt）
# 每个可执行 target（主程序 + 测试）都应调用本函数，否则运行时报"找不到 Qt DLL"
# ----------------------------------------------------------------------------
function(ds_deploy_qt TARGET_NAME)
    if(WIN32)
        find_program(QT_WINDEPLOYQT windeployqt HINTS "${CMAKE_PREFIX_PATH}/bin")
        if(QT_WINDEPLOYQT)
            add_custom_command(TARGET ${TARGET_NAME} POST_BUILD
                COMMAND "${QT_WINDEPLOYQT}" --dir "$<TARGET_FILE_DIR:${TARGET_NAME}>" "$<TARGET_FILE:${TARGET_NAME}>"
                COMMENT "windeployqt: 部署 ${TARGET_NAME} 的 Qt 运行库"
                VERBATIM)
        endif()
    endif()
endfunction()

# Windows：GUI 子系统（主程序）+ 自动部署
set_target_properties(DataScope PROPERTIES WIN32_EXECUTABLE TRUE)
ds_deploy_qt(DataScope)

# ============================================================================
# 2.8) 模拟设备：Simulator（独立可执行程序，P11）
#    无硬件联调时模拟采集设备：TCP 服务端 + 正弦波数据帧
# ============================================================================
add_executable(simulator
    src/simulator/main.cpp
)
target_include_directories(simulator PRIVATE ${DS_SRC_DIR})
target_link_libraries(simulator PRIVATE
    Qt5::Network
    datascope_protocol
)
ds_deploy_qt(simulator)

# ============================================================================
# 3) 单元测试（Qt Test）
#    enable_testing() 开启 CTest 支持，add_test 注册用例
# ============================================================================
enable_testing()

# P2: ByteUtils 单测
add_executable(tst_byteutils
    tests/unittests/tst_byteutils.cpp
)
target_include_directories(tst_byteutils PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_byteutils PRIVATE Qt5::Test datascope_utils)
# 测试程序也需要 Qt DLL（Qt5Test/Qt5Core），否则 ctest 运行时报 DLL 缺失
ds_deploy_qt(tst_byteutils)
add_test(NAME tst_byteutils COMMAND tst_byteutils)

# P3: TimeUtils 单测
add_executable(tst_timeutils
    tests/unittests/tst_timeutils.cpp
)
target_include_directories(tst_timeutils PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_timeutils PRIVATE Qt5::Test datascope_utils)
ds_deploy_qt(tst_timeutils)
add_test(NAME tst_timeutils COMMAND tst_timeutils)

# P4: SignalSlotDemo 信号槽机制单测（额外链接 datascope_utils，后续会用到）
add_executable(tst_signalslotdemo
    tests/unittests/tst_signalslotdemo.cpp
    src/app/signalslotdemo.cpp
    src/app/signalslotdemo.h
)
target_include_directories(tst_signalslotdemo PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_signalslotdemo PRIVATE Qt5::Test Qt5::Core datascope_utils)
ds_deploy_qt(tst_signalslotdemo)
add_test(NAME tst_signalslotdemo COMMAND tst_signalslotdemo)

# P6: LogManager 单测
add_executable(tst_logmanager
    tests/unittests/tst_logmanager.cpp
)
target_include_directories(tst_logmanager PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_logmanager PRIVATE Qt5::Test datascope_infra)
ds_deploy_qt(tst_logmanager)
add_test(NAME tst_logmanager COMMAND tst_logmanager)

# P7: ConfigManager 单测
add_executable(tst_configmanager
    tests/unittests/tst_configmanager.cpp
)
target_include_directories(tst_configmanager PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_configmanager PRIVATE Qt5::Test datascope_infra)
ds_deploy_qt(tst_configmanager)
add_test(NAME tst_configmanager COMMAND tst_configmanager)

# P8: Domain 领域模型单测
add_executable(tst_domain
    tests/unittests/tst_domain.cpp
)
target_include_directories(tst_domain PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_domain PRIVATE Qt5::Test datascope_domain)
ds_deploy_qt(tst_domain)
add_test(NAME tst_domain COMMAND tst_domain)

# V2: V2 领域模型单测（DeviceId/DeviceState/ConnectionParams/ChannelConfig/
#      AlarmRule/AlarmEvent/AcquisitionRecord —— 领域行为与不变量）
add_executable(tst_domain_v2
    tests/unittests/tst_domain_v2.cpp
)
target_include_directories(tst_domain_v2 PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_domain_v2 PRIVATE Qt5::Test datascope_domain)
ds_deploy_qt(tst_domain_v2)
add_test(NAME tst_domain_v2 COMMAND tst_domain_v2)

# V2: 设备列表模型单测（QAbstractTableModel 行为 + dataChanged 通知）
add_executable(tst_devicelistmodel
    tests/unittests/tst_devicelistmodel.cpp
)
target_include_directories(tst_devicelistmodel PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_devicelistmodel PRIVATE Qt5::Test datascope_domain)
ds_deploy_qt(tst_devicelistmodel)
add_test(NAME tst_devicelistmodel COMMAND tst_devicelistmodel)

# V2: 报警事件模型单测（QAbstractListModel + 头插/确认/通知）
add_executable(tst_alarmeventmodel
    tests/unittests/tst_alarmeventmodel.cpp
)
target_include_directories(tst_alarmeventmodel PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_alarmeventmodel PRIVATE Qt5::Test datascope_domain)
ds_deploy_qt(tst_alarmeventmodel)
add_test(NAME tst_alarmeventmodel COMMAND tst_alarmeventmodel)

# V2: 报警引擎单测（边界触发去重/恢复/通道过滤/坏数据跳过）
add_executable(tst_alarmengine
    tests/unittests/tst_alarmengine.cpp
)
target_include_directories(tst_alarmengine PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_alarmengine PRIVATE Qt5::Test datascope_services)
ds_deploy_qt(tst_alarmengine)
add_test(NAME tst_alarmengine COMMAND tst_alarmengine)

# P10: 协议引擎单测
add_executable(tst_protocol
    tests/unittests/tst_protocol.cpp
)
target_include_directories(tst_protocol PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_protocol PRIVATE Qt5::Test datascope_protocol)
ds_deploy_qt(tst_protocol)
add_test(NAME tst_protocol COMMAND tst_protocol)

# P14: 自绘控件单测（控件继承 QWidget，需链接 Widgets 模块）
add_executable(tst_widgets
    tests/unittests/tst_widgets.cpp
    src/ui/widgets/linechartwidget.cpp
    src/ui/widgets/linechartwidget.h
    src/ui/widgets/gaugewidget.cpp
    src/ui/widgets/gaugewidget.h
    src/ui/widgets/ledindicator.cpp
    src/ui/widgets/ledindicator.h
)
target_include_directories(tst_widgets PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_widgets PRIVATE Qt5::Test Qt5::Widgets)
ds_deploy_qt(tst_widgets)
add_test(NAME tst_widgets COMMAND tst_widgets)

# P11: TcpClient 单测（回环伪设备验证半包/粘包/连接失败）
add_executable(tst_tcpclient
    tests/unittests/tst_tcpclient.cpp
    src/services/tcpclient.cpp
    src/services/tcpclient.h
)
target_include_directories(tst_tcpclient PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_tcpclient PRIVATE Qt5::Test Qt5::Network datascope_protocol)
ds_deploy_qt(tst_tcpclient)
add_test(NAME tst_tcpclient COMMAND tst_tcpclient)

# P12: DataService 数据总线单测（多线程采集链路回环）
add_executable(tst_dataservice
    tests/unittests/tst_dataservice.cpp
)
target_include_directories(tst_dataservice PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_dataservice PRIVATE Qt5::Test Qt5::Network datascope_services)
ds_deploy_qt(tst_dataservice)
add_test(NAME tst_dataservice COMMAND tst_dataservice)

# P13: DeviceController 设备状态机单测（状态转移 + 自动重连 + 数据转发）
add_executable(tst_devicecontroller
    tests/unittests/tst_devicecontroller.cpp
)
target_include_directories(tst_devicecontroller PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_devicecontroller PRIVATE Qt5::Test Qt5::Network datascope_services)
ds_deploy_qt(tst_devicecontroller)
add_test(NAME tst_devicecontroller COMMAND tst_devicecontroller)

# P15: RecordManager 记录/回放单测（CSV 落盘 + 定时重放）
add_executable(tst_recordmanager
    tests/unittests/tst_recordmanager.cpp
)
target_include_directories(tst_recordmanager PRIVATE ${DS_SRC_DIR})
target_link_libraries(tst_recordmanager PRIVATE Qt5::Test Qt5::Core datascope_services)
ds_deploy_qt(tst_recordmanager)
add_test(NAME tst_recordmanager COMMAND tst_recordmanager)
