# CRAC/FCC 业余无线电模拟考试系统 · BI2XM 魔改版
# CRAC/FCC Amateur Radio Exam Simulator · BI2XM Modified Edition

[![License: CC BY-NC-SA 4.0](https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey.svg)](https://creativecommons.org/licenses/by-nc-sa/4.0/deed.zh-hans)
[![License: AGPL v3](https://img.shields.io/badge/Supplement-AGPL%20v3-blue.svg)](https://www.gnu.org/licenses/agpl-3.0)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-blue.svg)]()
[![Version](https://img.shields.io/badge/Version-1.0.0-green.svg)](../../releases)
[![Auto Update](https://github.com/sliverwolf233/crac-fcc-exam-mod/actions/workflows/update-pools.yml/badge.svg)](../../actions/workflows/update-pools.yml)

> ⚠️ **仅供学习使用，严禁售卖 / For study only. No selling.**

---

## 许可 / Licensing

本项目采用**双许可**模式 / This project uses **dual licensing**:

| 场景 / Scenario | 许可 / License |
|---|---|
| 商业行为（任何形式）| CC BY-NC-SA 4.0 → **禁止** |
| Commercial activity | CC BY-NC-SA 4.0 → **Prohibited** |
| 非商业 + 网络服务 | CC BY-NC-SA 4.0 + AGPL v3 网络条款 → **须开源** |
| Non-commercial + network | CC BY-NC-SA 4.0 + AGPL v3 → **Must open-source** |
| 非商业 + 本地使用 | CC BY-NC-SA 4.0 → 署名 + 相同方式共享 |
| Non-commercial + local | CC BY-NC-SA 4.0 → Attribution + ShareAlike |

**简言之：商业一律禁止，非商业须开源共享。**
**In short: no commercial use; non-commercial requires open sharing.**

详见 [LICENSE](LICENSE) / See [LICENSE](LICENSE)

---

## 中文说明

### 功能

- 🌏 **双国家题库** — CRAC 2025（A/B/C）+ FCC（T/G/E），一键切换
- ✅ **真·多选题** — 独立勾选/取消，全对才得分
- 🌐 **中英双语（可开关）** — FCC T级 409 题中文翻译
- 🔒 **隐私外置** — 进度存 personal.dat，工作区零隐私
- 📦 **单文件启动器** — 一个 exe 双击即用
- 🚫 **免 UAC** + 🆔 **身份证校验** + 📊 **实时进度**

### 快速开始

**单文件版**：下载 `release/Launcher.exe` → 双击 → 选题库 → 启动

**文件夹版**：下载 `release/` 全部 → 运行 `install-first-run.bat`

### FCC 等级对应

| 界面 | CRAC | FCC |
|---|---|---|
| A类 | A类（初等）| T = Technician |
| B类 | B类（中等）| G = General |
| C类 | C类（高等）| E = Amateur Extra |

---

## English

### Features

- 🌏 **Dual Banks** — CRAC 2025 (A/B/C) + FCC (T/G/E), one-click switch
- ✅ **True Multi-Select** — Independent toggle, exact match scoring
- 🌐 **Bilingual** — FCC Technician 409 questions with Chinese
- 🔒 **Privacy External** — personal.dat, clean workspace
- 📦 **Single File** — one exe, double-click to run
- 🚫 **No UAC** + 🆔 **ID Validation** + 📊 **Live Progress**

### Quick Start

**Single File**: Download `release/Launcher.exe` → Double-click → Select bank → Start

---

## 自动更新 / Auto Update

每月 1 日自动从 NCVEC 爬取最新题库（GitHub Actions），
检测到新版本时自动创建 Pull Request。

Monthly auto-fetch from NCVEC via GitHub Actions.
Creates a PR when new pool versions are detected.

[![Auto Update](https://github.com/sliverwolf233/crac-fcc-exam-mod/actions/workflows/update-pools.yml/badge.svg)](../../actions/workflows/update-pools.yml)

---

## 版权归属 / Copyright Attribution

| 内容 / Content | 版权 / Copyright |
|---|---|
| 原版软件 / Original software | © CRAC |
| CRAC 题库 / CRAC bank | © CRAC |
| FCC 题库 / FCC pools | © NCVEC |
| 魔改代码 / Modifications | [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/deed.zh-hans) + [AGPL v3](https://www.gnu.org/licenses/agpl-3.0) |

## 构建 / Building

参见 [docs/BUILD.md](docs/BUILD.md)

## 更新日志 / Changelog

参见 [CHANGELOG.md](CHANGELOG.md)

## 贡献 / Contributing

欢迎 [Issue](../../issues/new/choose) 和 Pull Request。

## 免责 / Disclaimer

本工具仅供学习研究，使用者需自行确保合规。
For educational use only. Users must ensure compliance.
