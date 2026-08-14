@echo off
rem Build uplr_converter (MSVC cl first, MinGW g++ second)
setlocal

where cl >nul 2>nul
if %errorlevel%==0 (
    cl /nologo /std:c++17 /O2 /EHsc /utf-8 uplr_converter.cpp /Fe:uplr_converter.exe
    if %errorlevel%==0 (
        echo BUILD OK: uplr_converter.exe
        exit /b 0
    )
    echo BUILD FAILED (cl)
    exit /b 1
)

where g++ >nul 2>nul
if %errorlevel%==0 (
    g++ -std=c++17 -O2 -finput-charset=UTF-8 uplr_converter.cpp -o uplr_converter.exe
    if %errorlevel%==0 (
        echo BUILD OK: uplr_converter.exe
        exit /b 0
    )
    echo BUILD FAILED (g++)
    exit /b 1
)

echo No C++ compiler found. Install Visual Studio Build Tools or MinGW-w64.
exit /b 1
