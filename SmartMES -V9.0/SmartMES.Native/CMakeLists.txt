cmake_minimum_required(VERSION 3.15)
project(SmartMES.Native CXX)

# 编译为共享库（.dll on Windows, .so on Linux）
add_library(SmartMES.Native SHARED SmartMES.Native.cpp)

# C++17标准
set_target_properties(SmartMES.Native PROPERTIES
    CXX_STANDARD 17
    CXX_STANDARD_REQUIRED ON
)

# 优化选项
if(MSVC)
    target_compile_options(SmartMES.Native PRIVATE /O2 /W3)
else()
    target_compile_options(SmartMES.Native PRIVATE -O2 -Wall)
endif()

# 输出目录：直接输出到UI项目的运行目录
set_target_properties(SmartMES.Native PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_SOURCE_DIR}/../SmartMES.UI/bin/Debug/net9.0-windows"
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_SOURCE_DIR}/../SmartMES.UI/bin/Debug/net9.0-windows"
)

message(STATUS "SmartMES.Native DLL will be output to UI bin directory")
