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
find_package(Qt5 COMPONENTS Core Gui Widgets Test REQUIRED)

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
)
# PUBLIC：库的使用者（主程序、测试）自动获得 src 头文件路径与 Qt5::Core
target_include_directories(datascope_utils PUBLIC ${DS_SRC_DIR})
target_link_libraries(datascope_utils PUBLIC Qt5::Core)

# ============================================================================
# 2) 主程序：DataScope
# ============================================================================
add_executable(DataScope
    # ---- 程序入口 ----
    src/app/main.cpp
    # ---- UI 层 ----
    src/ui/mainwindow.cpp
    src/ui/mainwindow.h
)

target_include_directories(DataScope PRIVATE ${DS_SRC_DIR})
target_link_libraries(DataScope PRIVATE
    Qt5::Core
    Qt5::Gui
    Qt5::Widgets
    datascope_utils
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
