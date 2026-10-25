# 构建指南

## 概述

本指南说明如何从源文件构建完整的 CRAC/FCC 双题库模拟考试系统。

## 前置条件

### 必需文件（需自行获取）

| 文件 | 来源 | 用途 |
|---|---|---|
| SimuSetup_x86.msi | CRAC 官方 | 原版软件安装包 |
| CRAC 2025 题库 PDF × 4 | CRAC 官方 | 中国内地题库 |
| FAA DOCX × 3 | [NCVEC](https://www.ncvec.org/) | 美国题库 |

### 工具链

- Python 3.12+（含 pdfminer.six, pypdf）
- .NET Framework 4.x（Windows 自带 csc.exe）
- PowerShell 5.1+
- [dnlib](https://github.com/0xd4d/dnlib)（IL 手术）

## 构建步骤

### 第 1 步：解包原版软件

```powershell
# MSI 管理员安装（解包）
msiexec /a SimuSetup_x86.msi /qn TARGETDIR=D:uild_extracted
```

### 第 2 步：下载 dnlib

```powershell
Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/dnlib/4.4.0" -OutFile dnlib.zip
Expand-Archive dnlib.zip dnlib_tmp
Copy-Item dnlib_tmp/lib/netstandard2.0/dnlib.dll _work/dl/dnlib/
```

### 第 3 步：解析题库

```powershell
# 提取 CRAC PDF 文本
python src/parse_crac.py

# 提取 CRAC 附图
python src/extract_figs3.py

# 解析 FAA DOCX
python src/parse_faa2.py

# 提取 FAA 附图
python src/extract_e_imgs.py
```

### 第 4 步：构建题库数据库

```powershell
# 构建 CRAC + FAA 题库（含 CorrectOptions 列）
python src/build_dbs.py

# 构建双语版本（应用中文翻译）
python src/build_bilingual.py

# 准备纯英文/双语变体
python src/toggle_prep.py
```

### 第 5 步：IL 手术（修改 exe）

```powershell
# 需要先运行 publicize.ps1 生成 Simulation.ref.dll
# 详见 surgery.ps1 注释
pwsh src/surgery.ps1
```

### 第 6 步：编译单文件启动器

```powershell
# 压缩载荷
python -c "import gzip; ..."

# 编译 Launcher.exe
pwsh src/build_launcher.ps1
```

## 验证

构建完成后应得到：
- `Simulation_patched.exe` — 修改后的模拟考试程序
- `SimPatches.dll` — 补丁库
- `Launcher.exe` — 单文件启动器（内嵌全部）

## 故障排除

| 问题 | 解决方案 |
|---|---|
| csc 编译错误 | 确认 .NET Framework 路径正确 |
| dnlib 加载失败 | 使用 netstandard2.0 版本 |
| SQLite 报错 | 检查 System.Data.SQLite.dll 和 SQLite.Interop.dll |
| UIA 测试失败 | 以非管理员权限运行 |
