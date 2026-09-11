cmake_minimum_required(VERSION 3.16)
project(uplr_converter CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_executable(uplr_converter uplr_converter.cpp)

# 源码为 UTF-8（含中文注释/字符串）：MSVC 需 /utf-8 避免代码页误读（C4819）
if(MSVC)
    target_compile_options(uplr_converter PRIVATE /utf-8)
endif()
