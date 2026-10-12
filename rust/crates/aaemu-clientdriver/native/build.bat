@echo off
rem Builds the 32-bit in-client driver aadriver.dll with MSVC (x86).
rem Output: %AADRIVER_OUT% (default: <target-dir>\aadriver or .\out next to this script).
setlocal
set "VCVARS="
for %%Y in (2022 2019) do (
  for %%E in (Community Professional Enterprise BuildTools) do (
    if not defined VCVARS if exist "%ProgramFiles%\Microsoft Visual Studio\%%Y\%%E\VC\Auxiliary\Build\vcvars32.bat" set "VCVARS=%ProgramFiles%\Microsoft Visual Studio\%%Y\%%E\VC\Auxiliary\Build\vcvars32.bat"
    if not defined VCVARS if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\%%Y\%%E\VC\Auxiliary\Build\vcvars32.bat" set "VCVARS=%ProgramFiles(x86)%\Microsoft Visual Studio\%%Y\%%E\VC\Auxiliary\Build\vcvars32.bat"
  )
)
if not defined VCVARS ( echo [!] vcvars32.bat not found - install the MSVC x86 build tools & exit /b 1 )
call "%VCVARS%" >nul
if not defined AADRIVER_OUT set "AADRIVER_OUT=%~dp0out"
if not exist "%AADRIVER_OUT%" mkdir "%AADRIVER_OUT%"
cl /nologo /O2 /MT /EHsc /W4 /LD "%~dp0aadriver.cpp" "%~dp0visuals.cpp" "%~dp0pakio.cpp" "%~dp0startup.cpp" "%~dp0input.cpp" "%~dp0audio.cpp" /Fo"%AADRIVER_OUT%\\" /Fe"%AADRIVER_OUT%\aadriver.dll" /link /NOLOGO user32.lib kernel32.lib d3d11.lib dxgi.lib gdiplus.lib ole32.lib
if errorlevel 1 ( echo [!] driver build failed & exit /b 1 )
cl /nologo /O2 /MT /EHsc /W4 "%~dp0startup-test.cpp" /Fo"%AADRIVER_OUT%\\" /Fe"%AADRIVER_OUT%\startup-test.exe" /link /NOLOGO kernel32.lib
if errorlevel 1 ( echo [!] startup tests build failed & exit /b 1 )
"%AADRIVER_OUT%\startup-test.exe"
if errorlevel 1 ( echo [!] startup tests failed & exit /b 1 )
cl /nologo /O2 /MT /EHsc /W4 "%~dp0input-test.cpp" /Fo"%AADRIVER_OUT%\\" /Fe"%AADRIVER_OUT%\input-test.exe" /link /NOLOGO kernel32.lib
if errorlevel 1 ( echo [!] input tests build failed & exit /b 1 )
"%AADRIVER_OUT%\input-test.exe"
if errorlevel 1 ( echo [!] input tests failed & exit /b 1 )
cl /nologo /O2 /MT /EHsc /W4 "%~dp0audio-test.cpp" /Fo"%AADRIVER_OUT%\\" /Fe"%AADRIVER_OUT%\audio-test.exe" /link /NOLOGO kernel32.lib ole32.lib
if errorlevel 1 ( echo [!] audio tests build failed & exit /b 1 )
"%AADRIVER_OUT%\audio-test.exe"
if errorlevel 1 ( echo [!] audio tests failed & exit /b 1 )
cl /nologo /O2 /MT /W3 "%~dp0aainject.cpp" /Fo"%AADRIVER_OUT%\\" /Fe"%AADRIVER_OUT%\aainject.exe" /link /NOLOGO kernel32.lib
if errorlevel 1 ( echo [!] injector build failed & exit /b 1 )
echo [ok] %AADRIVER_OUT%\aadriver.dll
echo [ok] %AADRIVER_OUT%\aainject.exe
endlocal
