# =============================================================================
# DataScope Studio V3 —— 工业多通道数据采集与实时监控系统（V3 重建）
# 顶层 CMake 构建脚本（主 Agent 唯一维护的共享文件）
# 工具链：Qt 5.12.2 (mingw73_64) + MinGW 7.3.0 (posix-seh) + CMake + Ninja，C++17
#
# V3 架构（B 版最小专业架构）：
#   app  ──▶ ui ──▶ acquisition ──▶ domain / protocol / infra
#   4 个静态库 + 2 个可执行程序，无 Application 层、无空接口、无空 target。
# =============================================================================

cmake_minimum_required(VERSION 3.16)
project(DataScope VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)
set(CMAKE_AUTOUIC ON)

find_package(Qt5 COMPONENTS Core Gui Widgets Network Test REQUIRED)

# ----------------------------------------------------------------------------
# 工具链硬校验：换 Qt 版本或 MinGW 版本会破坏可复现构建，配置期直接拦下
# ----------------------------------------------------------------------------
if(NOT Qt5Core_VERSION VERSION_EQUAL "5.12.2")
    message(FATAL_ERROR "要求 Qt 5.12.2（当前 ${Qt5Core_VERSION}）。换版本会导致产物不可复现。")
endif()
if(NOT CMAKE_CXX_COMPILER_ID STREQUAL "GNU" OR CMAKE_CXX_COMPILER_VERSION VERSION_LESS "7.3")
    message(FATAL_ERROR "要求 MinGW-w64 7.3.0 posix-seh（当前 ${CMAKE_CXX_COMPILER_ID} ${CMAKE_CXX_COMPILER_VERSION}）。")
endif()
if(NOT CMAKE_RC_COMPILER MATCHES "mingw730_64")
    message(FATAL_ERROR "要求 windres 来自 MinGW 7.3.0（当前 ${CMAKE_RC_COMPILER}）。MSYS2 的 windres 预处理 Qt .rc 会失败；请用 build.ps1/release.ps1，或传 -DCMAKE_RC_COMPILER=<Qt>/Tools/mingw730_64/bin/windres.exe。")
endif()

# ----------------------------------------------------------------------------
# 单一版本源：project(VERSION) 是唯一版本声明，configure_file 传播到
# version.h（代码用）与 version_*.rc（PE 资源用）。代码中禁止手写版本字符串。
# ----------------------------------------------------------------------------
configure_file(
    ${CMAKE_CURRENT_SOURCE_DIR}/src/version.h.in
    ${CMAKE_BINARY_DIR}/generated/version.h
    @ONLY)

# 版本号转 RC 逗号形式（0.1.0 → 0,1,0,0），缺省 TWEAK 段补 0
if("${PROJECT_VERSION_TWEAK}" STREQUAL "")
    set(V3_VERSION_TWEAK 0)
else()
    set(V3_VERSION_TWEAK ${PROJECT_VERSION_TWEAK})
endif()
set(V3_VERSION_RC_COMMA
    "${PROJECT_VERSION_MAJOR},${PROJECT_VERSION_MINOR},${PROJECT_VERSION_PATCH},${V3_VERSION_TWEAK}")

# DataScope 主程序 PE 版本资源（FileVersion 与 version.h 同源）
set(V3_RC_INTERNAL_NAME "DataScope")
set(V3_RC_ORIGINAL_FILENAME "DataScope.exe")
configure_file(
    ${CMAKE_CURRENT_SOURCE_DIR}/src/version.rc.in
    ${CMAKE_BINARY_DIR}/generated/version_datascope.rc
    @ONLY)

# simulator PE 版本资源（FileVersion 与 version.h 同源）
set(V3_RC_INTERNAL_NAME "simulator")
set(V3_RC_ORIGINAL_FILENAME "simulator.exe")
configure_file(
    ${CMAKE_CURRENT_SOURCE_DIR}/src/version.rc.in
    ${CMAKE_BINARY_DIR}/generated/version_simulator.rc
    @ONLY)

# ----------------------------------------------------------------------------
# windeployqt 配置期硬失败（DoD R3 / ADR-10）：找不到直接 FATAL_ERROR，禁止静默跳过
# 位置由 Qt5Core_DIR 反推（Qt5Core_DIR = .../lib/cmake/Qt5Core → ../../../bin）
# ----------------------------------------------------------------------------
get_filename_component(V3_QT_BIN_DIR "${Qt5Core_DIR}/../../../bin" ABSOLUTE)
find_program(WINDEPLOYQT_EXECUTABLE windeployqt
    HINTS "${V3_QT_BIN_DIR}"
    NO_DEFAULT_PATH REQUIRED)

# ----------------------------------------------------------------------------
# 目录变量（各 target 统一 include）
# ----------------------------------------------------------------------------
set(V3_SRC ${CMAKE_CURRENT_SOURCE_DIR}/src)
set(V3_GEN ${CMAKE_BINARY_DIR}/generated)

# ============================================================================
# 静态库：dscope_domain（领域层：规则/状态/不变量，纯逻辑，只依赖 QtCore）
# ============================================================================
add_library(dscope_domain STATIC
    src/domain/errorcode.cpp
    src/domain/connectionstate.cpp
    src/domain/alarmengine.cpp
)
target_include_directories(dscope_domain PUBLIC ${V3_SRC})
target_link_libraries(dscope_domain PUBLIC Qt5::Core)

# ============================================================================
# 静态库：dscope_protocol（协议层：字节编解码，只依赖 QtCore，不依赖 domain）
# ============================================================================
add_library(dscope_protocol STATIC
    src/protocol/crc16.cpp
    src/protocol/framebuilder.cpp
    src/protocol/frameparser.cpp
    src/protocol/channelconfigcodec.cpp
)
target_include_directories(dscope_protocol PUBLIC ${V3_SRC})
target_link_libraries(dscope_protocol PUBLIC Qt5::Core)

# ============================================================================
# 静态库：dscope_acquisition（采集层：socket I/O + 跨线程编排，依赖 domain/protocol）
# ============================================================================
add_library(dscope_acquisition STATIC
    src/acquisition/acquisitionworker.cpp
)
target_include_directories(dscope_acquisition PUBLIC ${V3_SRC})
target_link_libraries(dscope_acquisition PUBLIC Qt5::Core Qt5::Network dscope_domain dscope_protocol)

# ============================================================================
# 静态库：dscope_ui（界面层：渲染 + 交互编排，依赖 domain/acquisition）
# ============================================================================
add_library(dscope_ui STATIC
    src/ui/theme.cpp
    src/ui/models/alarmeventmodel.cpp
    src/ui/widgets/linechartwidget.cpp
    src/ui/widgets/gaugewidget.cpp
    src/ui/widgets/ledindicator.cpp
    src/ui/monitorpage.cpp
    src/ui/mainwindow.cpp
)
target_include_directories(dscope_ui PUBLIC ${V3_SRC})
target_link_libraries(dscope_ui PUBLIC Qt5::Core Qt5::Gui Qt5::Widgets dscope_domain dscope_acquisition)

# ============================================================================
# 可执行程序：simulator（独立模拟设备，只依赖 protocol + QtNetwork，无 GUI）
# ============================================================================
# ----------------------------------------------------------------------------
# PE 版本资源：手动 windres 编译（不传 -I），规避 CMake 原生 RC 规则把含空格的
# 项目 include 目录传给 windres 导致预处理失败（MinGW windres 对含空格 -I 会把
# 路径拆成两段）。version.rc 只 #include <windows.h>，由 gcc 默认搜索 MinGW
# include 目录，无需额外 -I。
# ----------------------------------------------------------------------------
add_custom_command(
    OUTPUT ${CMAKE_BINARY_DIR}/version_simulator.obj
    COMMAND ${CMAKE_RC_COMPILER} -O coff
            -i ${V3_GEN}/version_simulator.rc
            -o ${CMAKE_BINARY_DIR}/version_simulator.obj
    DEPENDS ${V3_GEN}/version_simulator.rc
    COMMENT "编译 PE 版本资源 version_simulator.rc"
    VERBATIM)

add_executable(simulator
    simulator/main.cpp
    simulator/simulatordevice.cpp
    simulator/faultinjector.cpp
    ${CMAKE_BINARY_DIR}/version_simulator.obj
)
target_include_directories(simulator PRIVATE ${V3_SRC} ${V3_GEN})
target_link_libraries(simulator PRIVATE Qt5::Core Qt5::Network dscope_protocol)

# ============================================================================
# 可执行程序：DataScope（主程序）
# ============================================================================
add_custom_command(
    OUTPUT ${CMAKE_BINARY_DIR}/version_datascope.obj
    COMMAND ${CMAKE_RC_COMPILER} -O coff
            -i ${V3_GEN}/version_datascope.rc
            -o ${CMAKE_BINARY_DIR}/version_datascope.obj
    DEPENDS ${V3_GEN}/version_datascope.rc
    COMMENT "编译 PE 版本资源 version_datascope.rc"
    VERBATIM)

add_executable(DataScope
    src/app/main.cpp
    src/infra/logger.cpp
    ${CMAKE_BINARY_DIR}/version_datascope.obj
)
target_include_directories(DataScope PRIVATE ${V3_SRC} ${V3_GEN})
target_link_libraries(DataScope PRIVATE Qt5::Core Qt5::Gui Qt5::Widgets dscope_domain dscope_protocol dscope_acquisition dscope_ui)

# ----------------------------------------------------------------------------
# Windows GUI 程序（不弹控制台窗口）；Linux/macOS 下无效但无副作用
# ----------------------------------------------------------------------------
set_target_properties(DataScope PROPERTIES WIN32_EXECUTABLE TRUE)

# ============================================================================
# 单元测试：tst_v3_unit（QtTest，只链接 domain+protocol+Qt5::Test，白名单禁 Network/Widgets）
# ============================================================================
enable_testing()

add_executable(tst_v3_unit
    tests/unit/tst_v3_unit.cpp
)
target_include_directories(tst_v3_unit PRIVATE ${V3_SRC})
target_link_libraries(tst_v3_unit PRIVATE Qt5::Test dscope_domain dscope_protocol)
add_test(NAME tst_v3_unit COMMAND tst_v3_unit)

# ============================================================================
# 集成测试：tst_v3_integration（内嵌 SimulatorDevice + AcquisitionWorker，真实 TCP 回路）
# 直接编译 simulator 的两个源文件（SimulatorDevice/FaultInjector），不额外抽库
# ============================================================================
add_executable(tst_v3_integration
    tests/integration/tst_v3_integration.cpp
    simulator/simulatordevice.cpp
    simulator/faultinjector.cpp
)
target_include_directories(tst_v3_integration PRIVATE ${V3_SRC} ${CMAKE_CURRENT_SOURCE_DIR}/simulator)
target_link_libraries(tst_v3_integration PRIVATE Qt5::Test Qt5::Network dscope_acquisition dscope_protocol dscope_domain)
add_test(NAME tst_v3_integration COMMAND tst_v3_integration)

# ============================================================================
# e2e 探针（DoD R4）：对构建产物做协议级/自检级验证，注册进 ctest 门禁
#   - e2e_simulator_version：simulator --version 输出含项目版本号（命令行解析器自动支持）
#   - e2e_datascope_selftest：DataScope --selftest 输出 "SELFTEST OK"（协议不变量自检）
# ============================================================================
add_test(NAME e2e_simulator_version COMMAND simulator --version)
set_tests_properties(e2e_simulator_version PROPERTIES
    PASS_REGULAR_EXPRESSION "${PROJECT_VERSION}")

add_test(NAME e2e_datascope_selftest COMMAND DataScope --selftest)
set_tests_properties(e2e_datascope_selftest PROPERTIES
    PASS_REGULAR_EXPRESSION "SELFTEST OK")
