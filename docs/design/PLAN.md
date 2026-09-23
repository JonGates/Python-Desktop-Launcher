# C# 桌面壳实现计划

**Goal:** 用原生 Windows 桌面替换 Python GUI，沿用单项目配置并交付完整可二开源码。
**Architecture:** Core + Windows + Desktop + CLI，各自可独立测试。
**Spec:** SPEC.md

## 实施顺序
1. 配置/参数测试：旧 YAML、argv 顺序、密码、路径、缺省与空列表、错误字段、备份冲突。
2. Core 实现：安全配置、运行时环境、初始化计划、持久化、日志脱敏、进程执行。
3. 终端测试及实现：VT 光标/ANSI/滚屏/颜色，Windows ConPTY 生命周期、独立读写通道。
4. WPF UI：语义主题，四页面，可视化参数/动作编辑，配置创建/接入，历史与预设。
5. CLI、Demo、Windows 构建、自动检查、文档与交付包。
6. 全量检查：XML、路径、YAML、Python Demo、源码一致性；有 SDK 时执行 Specs 和 Windows 发布。

## Review Focus
- 带中文、空格和引号的路径必须保持一个 argv 元素。
- visible_when 隐藏参数不传入，advanced 仅影响 UI。
- 不可把 .venv 或旧 Python GUI runtime 复制到新项目。
- 状态文件中不得遗留从普通字段变成 secret 的旧预设值。
- 保存时发现磁盘配置被他人修改必须拒绝静默覆盖。
