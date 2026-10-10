@echo off
rem Builds a P1c probe (one C file including p1c_common.h) with MSVC 2022 x64.
rem   p1c_build_probe.bat <probe.c> <out.exe>
setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat" x64 >nul 2>nul
if errorlevel 1 (echo vcvarsall failed & exit /b 1)
for %%F in ("%~2") do set OUTDIR=%%~dpF
if not exist "%OUTDIR%" mkdir "%OUTDIR%"
pushd "%OUTDIR%"
cl /nologo /O2 /fp:precise /W3 /D_CRT_SECURE_NO_WARNINGS /I"%~dp0." "%~f1" /Fe:"%~2" /link /nologo
set RC=%errorlevel%
popd
exit /b %RC%
