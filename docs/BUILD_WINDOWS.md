# Windows 构建、发布与验收

## 构建环境

基线为 .NET 10 SDK；源代码包含 WPF、P/Invoke ConPTY 和 Windows Job Object。推荐 Windows 11 x64 首轮验证。发布可以选择 win-x64 或 win-arm64；arm64 没有在本次环境验证，不代表切换 RID 后所有终端交互已自动通过。

本次已在 Windows x64 / .NET SDK 10.0.204 上完成 Release 编译、Core 与 Windows 原生规格测试、18 张 WPF 深浅主题渲染检查，以及自包含单文件 EXE / ZIP 发布。`tools/Test-Portable.ps1` 验证复制单个 EXE 后的首次绑定、没有环境、取消、已有配置重开和正常退出。人工 DPI、IME 和完整业务工作流尚未验收，详见 [测试记录](TESTING.md)。

## 一键构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Build.ps1
```

每个 dotnet 命令均检查退出码。默认流程是 restore → build 整个 solution → 运行 Core 规格测试 → 分别 publish GUI 和 CLI → 复制到 portable / demo → 压缩与生成 SHA256。

原始发布参数：

```powershell
dotnet publish src/ProjectLauncher.Desktop/ProjectLauncher.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -o artifacts/publish-Desktop-win-x64
```

CLI 对应 `src/ProjectLauncher.Cli/ProjectLauncher.Cli.csproj`。调试符号配置为 embedded。单文件压缩会增加启动解压工作；需要追求启动速度时可关闭 `EnableCompressionInSingleFile`，但要重新测体积和实际启动时间。

`Build.ps1 -SkipTests` 仅供本地开发排查，不适合作为可交付版本的验证依据。不要在红色测试仍失败时加开关掩盖问题。

## 开发与测试

```powershell
dotnet run --project src/ProjectLauncher.Desktop -- --project .
dotnet run --project tests/ProjectLauncher.Specs -c Release
dotnet run --project tests/ProjectLauncher.Windows.Specs -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-Windows.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-Portable.ps1
```

`Test-Windows.ps1` 把文本日志和 WPF 渲染图片写到 `artifacts/test-reports`。UI 检查使用独立的测试项目，不执行 Demo 业务；首次配置截图使用明确的测试环境文件。终端截图使用合成渲染检查文本，不是 ConPTY 测试的替代。`Test-Portable.ps1` 要求先成功发布，并需要本机 Python；它在 `artifacts/portable-test-*` 中创建隔离的真实测试虚拟环境，通过 Windows UI Automation 操作复制后的 EXE。它会打开测试窗口，结束后正常关闭。

项目同时提供 GitHub Actions 工作流；推送到 `main` / `master` 会触发，也可手动运行。工作流不使用部署凭证，只构建并上传 workflow artifacts。远程工作流结果须在 GitHub 上单独核对，不能由本地测试推断。

## VS / Codex 二开

可用支持 .NET 10 SDK 的 IDE 打开 `ProjectLauncher.sln`；不把特定 IDE 版本写死。所有必要流程也可通过 dotnet CLI 完成。运行入口 Desktop，发布入口也是 Desktop；不要把 Core 项目当作应用直接发布。

NuGet 直接依赖固定到 YamlDotNet 18.1.0。已成功还原并将六个项目实际生成的 `packages.lock.json` 纳入源码；后续 CI 可改为 locked restore。SDK 本身更新也要重新执行验证。

## 分发注意

真正的 portable 单 EXE 可复用于多个项目；每个项目的 YAML 放在 EXE 旁，不编进程序。CLI 是另一份可选单 EXE。若拿开发 apphost 单独分发，它可能因缺 DLL / runtimeconfig 而打不开；GUI 的接入功能会拒绝复制非单文件开发版本。

目标机不必装 .NET 的前提是使用这里的 self-contained 发布，且 OS / CPU 匹配；项目 Python / uv / 第三方 CLI 仍需要部署。不要把运行时依赖缺失与启动器依赖混为一谈。

单文件可能在 `%TEMP%/.net` 中展开原生依赖；不要把“单文件”宣传成运行中没有临时目录。首次启动时间、文件体积和安全软件提示都必须在实际生成物上测量。本包不进行代码签名，也不承诺不会触发 SmartScreen。正式分发需建立自己的签名、校验和升级流程，不能用关闭安全软件作为正常安装步骤。

保留源代码许可、YamlDotNet 许可和 .NET 自包含运行时需要的第三方 notices；本包提供许可资料说明，构建脚本还会从已还原的包收集可见 notices。业务项目本身的许可证独立处理。

## 官方依据

- [.NET 单文件部署](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [.NET 10 WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)
- [EnableWindowsTargeting 说明](https://learn.microsoft.com/en-us/dotnet/core/tools/sdk-errors/netsdk1100)

官方文档中的机制与本包实际已完成的测试是两件事。本包已成功发布并通过上述自动测试，仍需要完成人工验收。

## 重复构建不会打包用户运行数据

发布封装使用新的 `artifacts/stage-随机值` 暂存目录，只复制白名单中的 EXE、配置、Demo 源文件和文档。原有 `artifacts/portable` 与 `artifacts/demo` 会先改名为 `.previous-时间-随机值` 备份，再放入新包。旧 Demo 的 `.venv`、日志、输出及用户预设不进入新 ZIP；确认不再需要时，由你自行清理这些备份。
