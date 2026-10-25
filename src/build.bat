@chcp 65001 >nul
@echo off
setlocal
rem ================================================================
rem  build.bat —— 使用 Windows 系统自带的 .NET Framework csc.exe
rem  编译 BankSwitcher.exe（无需安装任何 SDK / Visual Studio）。
rem  双击本文件或在本目录执行即可，输出到本目录。
rem ================================================================

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [错误] 未找到 .NET Framework 自带的 csc.exe，请确认系统为 Windows 7 及以上。
  exit /b 1
)

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /warn:4 ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /win32manifest:"%~dp0app.manifest" ^
  /out:"%~dp0BankSwitcher.exe" "%~dp0BankSwitcher.cs"
if errorlevel 1 (
  echo 编译失败。
  exit /b 1
)

echo 编译成功：%~dp0BankSwitcher.exe
echo 提示：可用 “BankSwitcher.exe --selftest 自检日志.txt” 做无头自检。
endlocal
