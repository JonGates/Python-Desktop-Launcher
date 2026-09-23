# 接入配置示例

`minimal.yaml` 和 `existing-env.yaml` 是放到已有项目后按实际入口调整的模板，不是假设 main.py 已自动存在的 Demo。

`uv-demo/` 是完整的小型 uv 项目，有真实 app.py 与 pyproject.toml。把发布后的 Launcher.exe 放入该目录，初始化/同步后运行；或直接 `uv run app.py --who "中文名字"`。该例无第三方 Python 依赖。

源码包未附自动生成的 uv.lock：第一次明确运行 uv sync 后由实际安装的 uv 生成，不虚构锁文件版本。
