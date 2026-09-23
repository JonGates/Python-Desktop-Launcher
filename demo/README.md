# 文本工坊 Demo

源代码只使用 Python 标准库。初次运行请在启动器的「环境管理」中创建项目 `.venv`。

回到「运行项目」，选择「处理文本」并开始运行。每处理一行才输出实际完成量；可以在高级参数中调整延迟。

默认输出 `demo/output/result.txt`、`demo/output/summary.json`。第二次运行需要勾选覆盖，或换目录。Demo 不修改输入文件，不联网。输出文件逐个原子写入，**两个文件不是整体事务**；任务被强停或后一步写入失败时，可能只存在第一个文件。禁用覆盖使用同卷硬链接确保不误覆盖，NTFS / 常见 Linux 文件系统可用；不支持硬链接的卷会明确失败。正式业务请使用适合自身数据的一致性策略。

项目终端可依次运行：

```powershell
python demo/environment_probe.py
python demo/interactive_demo.py
python
```

演示密钥只显示是否提供，不把实际值输出到日志或写入 summary。

可脱离启动器直接使用：

```powershell
python demo/process_text.py --input demo/sample.txt --output demo/output --mode uppercase
```
