# 实施记录

- 新建独立源码目录，不修改此前 Python 包或用户项目。
- 已读取上一版完整配置规范并保留 legacy-v1.yaml 测试夹具。
- 已核实 .NET 10 WPF、单文件发布和 ConPTY 的官方文档。
- 当前 Linux 容器无 dotnet / csc / mono；SDK 下载与包注册表 DNS 请求失败。C# 编译、C# 测试及 Windows GUI 运行暂不可执行。
- 因此交付定位为 2.0.0-preview.1 源码预览版；提供 Windows 一键编译/验证，不能声称已生成或验证 EXE。

## 源码与文件交付阶段

- 核心、Windows 后端、WPF 四页面、可视化参数设计器、独立 CLI 和两个 C# 回归程序已写入；没有使用空的终端占位视图代替 ConPTY 后端。
- 接入功能保持旧 YAML，不复制业务环境；设置保留 v1 的 parameters:null / [] 差异、secret、不覆盖保留环境变量、条件显示与布尔语义。
- 审阅后修正：任务程序使用子环境 PATH 解析；普通 `.launcher` 不能作为业务虚拟环境根；无效进度 JSON 不抛 UI 异常；Int64 状态避免中间 double；Windows Mutex 由专用线程持有与释放；未改动的参数选项和条件保留旧 YAML 值；只读日志绑定方向显式指定。
- 构建打包改为新暂存目录；旧 portable / demo 目录保留为 previous 备份，避免把用户上次运行 Demo 的 `.venv`、日志和输出封装进发布 ZIP。增加实际还原包许可证收集。
- 已编写 13 项 Python Demo 测试并实际运行，全部通过；静态源文件/XML资源/文档/配置结构一致性检查实际通过，详见 docs/reports。C# 测试仍未运行。
- HTML 预览仅用于展示样式；四页面深色及运行页浅色已本地渲染并查看，页面上明确标注非 WPF 运行截图。不捆绑字体。
- 再次尝试 SDK 工具下载仍未获得可用文件；额外 C# 解析器安装因 DNS 失败。没有降低验证口径或伪造编译结果。
- 最终以源码预览版发布；未来实际 SDK 构建和 Windows 验收结果须追加到 docs/TESTING.md 后再调整版本状态。
