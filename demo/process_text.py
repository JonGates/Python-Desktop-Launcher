"""Standard-library demo for Project Launcher. No GUI dependency and no network access.

The launcher does not require this protocol; ordinary scripts work without modification.
This demo opts in to @@launcher: JSON progress events to show a real progress bar.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import sys
import tempfile
import time


def parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="Project Launcher 文本处理 Demo")
    p.add_argument("--input", required=True, type=Path)
    p.add_argument("--output", required=True, type=Path)
    p.add_argument("--mode", choices=("uppercase", "lowercase", "prefix"), default="uppercase")
    p.add_argument("--prefix", default="[Launcher] ")
    p.add_argument("--repeat", type=int, choices=range(1, 21), default=1)
    p.add_argument("--delay", type=float, default=0.0)
    p.add_argument("--overwrite", action="store_true")
    p.add_argument("--notes", default="")
    p.add_argument("--fail", action="store_true")
    return p


def atomic_text(path: Path, text: str, overwrite: bool) -> None:
    """Replace one file atomically; refuse an existing target when overwrite is disabled."""
    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=path.parent, delete=False, prefix=".launcher-demo-") as out:
            temporary = Path(out.name)
            out.write(text)
            out.flush()
            os.fsync(out.fileno())
        if overwrite:
            os.replace(temporary, path)
        else:
            # Hard-link creation is atomic and refuses an existing destination. Same-directory
            # files are on the same volume. On volumes without hard links report the error,
            # rather than silently falling back to an unsafe overwrite.
            os.link(temporary, path)
            temporary.unlink()
        temporary = None
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main(argv: list[str] | None = None) -> int:
    args = parser().parse_args(argv)
    try:
        if not 0 <= args.delay <= 2:
            raise ValueError("delay 必须在 0–2 秒之间。")
        if args.fail:
            print("演示失败：脚本按要求以退出码 7 结束。", file=sys.stderr, flush=True)
            return 7
        source = args.input.resolve(strict=True)
        if not source.is_file():
            raise ValueError("输入路径必须是文件。")
        content = source.read_text(encoding="utf-8-sig")
        if len(content) > 2_000_000:
            raise ValueError("Demo 只处理不超过 200 万字符的文本。")
        output = args.output.resolve()
        targets = [output / "result.txt", output / "summary.json"]
        if source in targets:
            raise ValueError("输入文件与输出目标相同；请使用不同的输出目录。")
        if not args.overwrite and any(path.exists() for path in targets):
            raise FileExistsError("输出已存在：请更换目录，或明确勾选「覆盖已有结果」。")
        lines = content.splitlines() or [""]
        total = len(lines) * args.repeat
        result: list[str] = []
        print(f"输入：{source}\n模式：{args.mode}\n计划处理：{total} 行", flush=True)
        print("演示密钥：" + ("已提供（不回显）" if os.environ.get("DEMO_API_TOKEN") else "未提供"), flush=True)
        for round_index in range(args.repeat):
            for index, line in enumerate(lines):
                value = line.upper() if args.mode == "uppercase" else line.lower() if args.mode == "lowercase" else args.prefix + line
                result.append(value)
                if args.delay:
                    time.sleep(args.delay)
                completed = round_index * len(lines) + index + 1
                print("@@launcher:" + json.dumps({"progress": completed, "total": total, "message": f"正在处理 {completed} / {total} 行"}, ensure_ascii=False), flush=True)
        output.mkdir(parents=True, exist_ok=True)
        summary = {"source": str(source), "mode": args.mode, "lines": total, "repeat": args.repeat, "notes": args.notes, "python": sys.executable}
        atomic_text(targets[0], "\n".join(result) + "\n", args.overwrite)
        atomic_text(targets[1], json.dumps(summary, ensure_ascii=False, indent=2) + "\n", args.overwrite)
        print(f"完成：{targets[0]}\n摘要：{targets[1]}", flush=True)
        return 0
    except (OSError, ValueError, UnicodeError) as exc:
        print(f"处理失败：{exc}", file=sys.stderr, flush=True)
        return 2
    except KeyboardInterrupt:
        print("操作已中断。", file=sys.stderr, flush=True)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
