# ============================================================
# CMakeLists.txt
# C++ 完整语言学习项目 — 构建配置
# 要求：CMake 3.20+，支持 C++20
# ============================================================

cmake_minimum_required(VERSION 3.20)
project(CppMasterClass VERSION 1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# 收集所有源文件
file(GLOB SOURCES "src/*.cpp")

# 生成可执行文件
add_executable(CppMasterClass ${SOURCES})

# 多线程支持
find_package(Threads REQUIRED)
target_link_libraries(CppMasterClass PRIVATE Threads::Threads)

# 编译选项
if(MSVC)
    target_compile_options(CppMasterClass PRIVATE /W4 /utf-8)
else()
    target_compile_options(CppMasterClass PRIVATE -Wall -Wextra -pthread)
endif()
