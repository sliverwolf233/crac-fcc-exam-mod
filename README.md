# CRAC/FCC 业余无线电模拟考试系统 · BI2XM 魔改版

[![License: CC BY-NC-SA 4.0](https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey.svg)](https://creativecommons.org/licenses/by-nc-sa/4.0/)

> ⚠️ **本工具仅供学习使用，严禁售卖**

将 CRAC 2021 版模拟考试软件适配为支持 **CRAC 2025（中国内地）** 和 **FCC/FAA（美国）**
双题库的模拟考试系统，同时增强多选题、中英双语、单文件启动等功能。

## 功能概览

| 功能 | 说明 |
|---|---|
| 双国家题库 | 中国大陆 CRAC 2025（A/B/C 三类）/ 美国 FCC（T/G/E 三级） |
| 真·多选题 | 独立勾选 + 再次点击取消，全对才得分 |
| 中英双语（可开关） | FCC T 级 409 题全量中文翻译，可随时切换纯英文 |
| 隐私外置 | 登录记忆与学习进度保存到 personal.dat，工作区零隐私 |
| 单文件启动器 | 一个 exe 内嵌全部内容，双击即用 |
| 免 UAC | 无需管理员权限 |
| 身份证校验 | GB 11643 校验和验证 |

## 快速开始

### 前置条件（需自行获取，本项目不分发）

| 文件 | 来源 | 版权 |
|---|---|---|
| 原版安装包 | CRAC 官方渠道 | © CRAC |
| CRAC 2025 题库 PDF | CRAC 官方 | © CRAC |
| FCC 题库 DOCX | [NCVEC 官网](https://www.ncvec.org/) | © NCVEC |

### 构建步骤

```bash
git clone https://github.com/sliverwolf233/crac-fcc-exam-mod.git
cd crac-fcc-exam-mod

# 1. 将原版软件解包到 _extracted/（参考 docs/extract.md）
# 2. 将题库源文件放到 "题库源/" 目录
# 3. 运行构建：
python src/build_dbs.py       # 构建题库
pwsh src/surgery.ps1          # IL 手术（修改 exe）
pwsh src/build_launcher.ps1   # 编译单文件启动器
```

详细构建说明见 [docs/BUILD.md](docs/BUILD.md)

## 多重版权

本项目采用[多重版权声明](LICENSE)，各部分版权归各自所有者：
- 原版软件与 CRAC 题库：© 中国无线电协会
- FCC 题库：© NCVEC
- 魔改代码/翻译/脚本：[CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/deed.zh-hans)

## 贡献

欢迎提交 Issue 和 Pull Request。翻译纠错、新题库适配等均欢迎。

## 免责声明

本工具仅供学习研究。使用者需自行确保符合所在地区的无线电法规。
