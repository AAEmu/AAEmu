@echo off
rem Builds physoracle.exe (and the tbbmalloc_proxy stub) with MSVC x64.
rem Output: %AAEMU_PHYS_ORACLE_OUT% or D:\aaemu-target\physics\oracle\ (never committed).
setlocal
set SRC=%~dp0
if "%AAEMU_PHYS_ORACLE_OUT%"=="" (set OUT=D:\aaemu-target\physics\oracle) else (set OUT=%AAEMU_PHYS_ORACLE_OUT%)
if not exist "%OUT%" mkdir "%OUT%"
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat" x64 >nul 2>nul
if errorlevel 1 (echo vcvarsall failed & exit /b 1)
pushd "%OUT%"
rem /fp:precise keeps single-precision SSE semantics for the probe input generator
cl /nologo /O2 /fp:precise /W3 /D_CRT_SECURE_NO_WARNINGS "%SRC%physoracle.c" /Fe:physoracle.exe /link /nologo
if errorlevel 1 (popd & exit /b 1)
cl /nologo /LD /O2 "%SRC%tbbstub.c" /Fe:tbbmalloc_proxy.dll /link /nologo
if errorlevel 1 (popd & exit /b 1)
popd
echo built %OUT%\physoracle.exe
