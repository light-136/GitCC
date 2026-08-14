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
# 编译标准：C++17（与 Qt 6 时代兼容，Qt5.12 完全支持）
# ----------------------------------------------------------------------------
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# 关闭编译器扩展，保证跨编译器一致行为
set(CMAKE_CXX_EXTENSIONS OFF)

# ----------------------------------------------------------------------------
# Qt 元对象编译三开关：
#   AUTOMOC：自动为含 Q_OBJECT 的头文件运行 moc（元对象编译器）
#   AUTORCC：自动编译 .qrc 资源文件
#   AUTOUIC：自动编译 Qt Designer 的 .ui 文件
# 这三点是"Qt + CMake"省心组合，对应 QtCreator 新建项目的默认行为
# ----------------------------------------------------------------------------
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)
set(CMAKE_AUTOUIC ON)

# ----------------------------------------------------------------------------
# 查找 Qt5 模块。
# 注意：通过 -DCMAKE_PREFIX_PATH=D:/Qt/5.12.2/mingw73_64 在配置时告知 Qt 位置
# 这相当于 .csproj 里引用 NuGet 包，但 Qt 模块是随 SDK 附带的
# ----------------------------------------------------------------------------
find_package(Qt5 COMPONENTS Core Gui Widgets REQUIRED)

# ============================================================================
# 主程序 target：DataScope
# ============================================================================
add_executable(DataScope
    # ---- 程序入口 ----
    src/app/main.cpp
    # ---- UI 层 ----
    src/ui/mainwindow.cpp
    src/ui/mainwindow.h
)

# 让源码内可以使用 #include "ui/mainwindow.h" 这种相对 src 的写法
target_include_directories(DataScope PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/src
)

# 链接 Qt 模块（Win32 子系统，不弹控制台黑窗）
target_link_libraries(DataScope PRIVATE
    Qt5::Core
    Qt5::Gui
    Qt5::Widgets
)

# Windows 下以 GUI 子系统启动（对应 WPF 的 WinExe 输出类型）
if(WIN32)
    set_target_properties(DataScope PROPERTIES WIN32_EXECUTABLE TRUE)

    # 开发期自动部署 Qt 运行库：每次构建后把 Qt DLL + platform 插件复制到
    # exe 同目录，避免"双击找不到 Qt5Widgets.dll"（等价手动跑 windeployqt）
    find_program(QT_WINDEPLOYQT windeployqt HINTS "${CMAKE_PREFIX_PATH}/bin")
    if(QT_WINDEPLOYQT)
        add_custom_command(TARGET DataScope POST_BUILD
            COMMAND "${QT_WINDEPLOYQT}" --dir "$<TARGET_FILE_DIR:DataScope>" "$<TARGET_FILE:DataScope>"
            COMMENT "windeployqt: 部署 Qt 运行库到可执行目录"
            VERBATIM)
    endif()
endif()
