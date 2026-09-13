@echo off
rem Build uplr_converter (MSVC cl first, MinGW g++ second)
rem If cl is not on PATH, auto-load VS dev env via vswhere (VS2019/2022/2026).
rem NOTE: keep this file pure ASCII; cmd parses .bat with the system codepage.
setlocal enabledelayedexpansion

where cl >nul 2>nul
if %errorlevel%==0 goto :cl_build

set "VSWHERE=!ProgramFiles(x86)!\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "!VSWHERE!" (
    for /f "usebackq tokens=*" %%i in (`"!VSWHERE!" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSROOT=%%i"
)
if defined VSROOT (
    call "!VSROOT!\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
    where cl >nul 2>nul
    if !errorlevel!==0 goto :cl_build
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

:cl_build
cl /nologo /std:c++17 /O2 /EHsc /utf-8 uplr_converter.cpp /Fe:uplr_converter.exe
if %errorlevel%==0 (
    echo BUILD OK: uplr_converter.exe
    exit /b 0
)
echo BUILD FAILED (cl)
exit /b 1
