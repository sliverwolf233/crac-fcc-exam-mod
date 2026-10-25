# 更新日志 / Changelog

所有显著变更都记录在此文件中。
All notable changes are documented in this file.

格式基于 [Keep a Changelog](https://keepachangelog.com/)，
版本号遵循 [Semantic Versioning](https://semver.org/)。

## [1.0.0] - 2026-10-25

### 新增 / Added

- **双国家题库** / Dual country question banks
  - CRAC 2025（A 683题 / B 1143题 / C 1282题，含 542 道真·多选题）
  - FCC（T 409题 / G 423题 / E 599题）
  - 启动器一键切换，各自独立保存学习进度

- **真·多选题** / True multi-select support
  - 独立勾选与取消（RadioButton 独立分组）
  - 判分规则：与标准答案完全一致才得分
  - 错题本正确显示答案组合（如 BC）

- **中英双语（可开关）** / Bilingual toggle
  - FCC Technician 级 409 题全量中文翻译
  - 启动器复选框切换，进度自动迁移

- **单文件启动器** / Single-file launcher
  - 一个 exe（~14MB）内嵌全部程序与题库
  - 首次运行自动展开
  - 集成题库选择、导入、双语开关

- **隐私外置** / Privacy externalization
  - 登录记忆与进度保存到 personal.dat
  - 退出后自动清除工作区隐私数据
  - 启动时自动恢复

- **免 UAC** / No UAC
  - manifest 从 requireAdministrator 降为 asInvoker

- **身份证校验** / ID card validation
  - GB 11643 校验和验证
  - 护照等非大陆证件格式放行

- **实时进度标题** / Live progress title
  - 考试模式：第X题/共Y题 · 已答Z题
  - 学习模式：学习中 · 第X题/共Y题
  - 主页：累计学习时间统计

- **加载遮罩修复** / Loading overlay fix
  - 修复灰带问题（grdGrayLayer 未正确隐藏）

- **题库导入** / Bank import
  - 支持导入自定义 .sqlite3 题库

### 修复 / Fixed

- 单选题同时勾选多个选项导致判错的问题
- 多选题取消选中（再次点击已勾选项）
- 错题本多选题正确答案显示为单字母的问题
- 考后解析列表多选题勾选状态不完整的问题
- 标题区灰带（加载遮罩残留）

### 技术细节 / Technical Details

- IL 手术工具：dnlib 4.4.0
- 手术点位：4 个方法体替换、3 个前言注入、2 个判分段替换
- 补丁库：SimPatches.dll（约 21KB）
- 修改 exe：Simulation_patched.exe（SHA256 变更，MVID 不变）
- 题库格式：SQLite，Question 表新增 CorrectOptions 列

## [未发布 / Unreleased]

### 计划 / Planned

- FCC General / Extra 级中文翻译
- 深色模式
- 导出成绩报告（PDF）
- macOS / Linux 支持（需要 Wine）
