"""Run inside the project terminal: python demo/interactive_demo.py"""
import sys
import time

try:
    print("\033[36mProject Launcher · 真实交互示例\033[0m", flush=True)
    print("解释器：", sys.executable)
    name = input("请输入称呼（可用下方中文输入行）：").strip() or "朋友"
    print(f"\033[32m你好，{name}！\033[0m", flush=True)
    print("下面演示回车覆盖式进度。按 Ctrl+C 可以中断。")
    for i in range(31):
        sys.stdout.write(f"\r进度：[{('#' * i).ljust(30, '.')}] {i * 100 // 30:3d}%")
        sys.stdout.flush()
        time.sleep(0.08)
    print("\n\033[35m会话没有重建；之后仍然回到原来的 Shell。\033[0m")
except (KeyboardInterrupt, EOFError):
    print("\n交互结束。")
