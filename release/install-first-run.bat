@chcp 65001 >nul
@echo off
setlocal enableextensions
rem ================================================================
rem  install-first-run.bat —— 题库切换器 安装 / 首次初始化脚本
rem
rem  用法：把整个包（BankSwitcher.exe、install-first-run.bat、
rem        banks 文件夹）复制到模拟考试软件安装目录
rem        （即 Simulation.exe 所在目录），然后双击运行本脚本。
rem
rem  功能：1) 把题库文件 CRAC.sqlite3 / FAA.sqlite3 复制到安装目录
rem           的 banks\ 下（若已就地则跳过）；
rem        2) 调用 BankSwitcher.exe --init 首次激活默认题库
rem           “中国大陆 CRAC 2025”（若安装目录原有 SimulationSys.sqlite3
rem           会先自动备份为 banks\original-backup.sqlite3）。
rem ================================================================

set "SCRIPTDIR=%~dp0"

rem ---- 管理员权限自检：不足则通过 UAC 自动提权重启本脚本 ----
rem （原 Simulation.exe 清单即要求管理员运行，写入安装目录也需要该权限）
powershell -NoProfile -Command "exit [int](-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))"
if errorlevel 1 (
  echo [提示] 本脚本需要管理员权限才能写入安装目录。
  echo         正在请求提升，请在弹出的 UAC 窗口中点“是”，脚本将在新窗口中继续...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b 1
)

rem ---- 源题库目录自适应：优先“本目录\banks”，其次“上一级\banks”（仓库开发布局） ----
set "SRC=%SCRIPTDIR%banks"
if not exist "%SRC%\CRAC.sqlite3" (
  if exist "%SCRIPTDIR%..\banks\CRAC.sqlite3" set "SRC=%SCRIPTDIR%..\banks"
)
if not exist "%SRC%\CRAC.sqlite3" (
  echo [错误] 未找到题库源文件 banks\CRAC.sqlite3
  echo        请确认 banks 文件夹与本脚本位于同一目录（或上一级目录）。
  goto :fail
)

set "DST=%SCRIPTDIR%banks"

echo 安装目录：%SCRIPTDIR%
echo 题库来源：%SRC%
echo.

if /i "%SRC%"=="%DST%" (
  echo 题库文件已位于安装目录，跳过复制。
) else (
  if not exist "%DST%" mkdir "%DST%"
  echo 正在复制题库文件，请稍候...
  copy /y "%SRC%\CRAC.sqlite3" "%DST%\CRAC.sqlite3" >nul
  if errorlevel 1 goto :copyfail
  copy /y "%SRC%\FAA.sqlite3" "%DST%\FAA.sqlite3" >nul
  if errorlevel 1 goto :copyfail
  echo 题库文件复制完成。
)
echo.

if not exist "%SCRIPTDIR%BankSwitcher.exe" (
  echo [错误] 未找到 BankSwitcher.exe，请确认它与本脚本位于同一目录。
  goto :fail
)

if not exist "%SCRIPTDIR%Simulation.exe" (
  echo [警告] 当前目录未找到 Simulation.exe：
  echo         %SCRIPTDIR%
  echo         请确认本脚本已放到模拟考试软件安装目录，否则切换器无法启动软件。
  echo.
)

echo 正在进行首次激活（默认题库：中国大陆 CRAC 2025）...
"%SCRIPTDIR%BankSwitcher.exe" --init
if errorlevel 1 (
  echo.
  type "%SCRIPTDIR%BankSwitcher-init.log" 2>nul
  echo.
  echo [错误] 首次激活失败，请根据上方日志处理后再运行本脚本。
  goto :fail
)

echo.
echo ================================================================
echo  安装完成！以后双击 BankSwitcher.exe 即可在两个题库间切换。
echo  当前题库：中国大陆 CRAC 2025
echo ================================================================
pause
exit /b 0

:copyfail
echo [错误] 复制题库文件失败，请检查磁盘空间与目录权限。
:fail
echo.
pause
exit /b 1
