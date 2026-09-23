from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "demo" / "process_text.py"


class DemoTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory(prefix="Launcher 中文 demo ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / "输入 文件.txt"
        self.source.write_text("Hello\n中文 World\n", encoding="utf-8")
        self.output = self.root / "输出 folder"

    def run_demo(self, *extra: str, token: str | None = None) -> subprocess.CompletedProcess[str]:
        env = dict(os.environ, PYTHONUTF8="1")
        if token is not None:
            env["DEMO_API_TOKEN"] = token
        return subprocess.run([sys.executable, str(SCRIPT), "--input", str(self.source), "--output", str(self.output), *extra],
                              text=True, encoding="utf-8", capture_output=True, env=env, timeout=12)

    def test_unicode_and_spaces(self) -> None:
        r = self.run_demo()
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual((self.output / "result.txt").read_text(encoding="utf-8"), "HELLO\n中文 WORLD\n")

    def test_lowercase(self) -> None:
        self.assertEqual(self.run_demo("--mode", "lowercase").returncode, 0)
        self.assertIn("hello", (self.output / "result.txt").read_text(encoding="utf-8"))

    def test_prefix_preserves_whitespace_and_metacharacters(self) -> None:
        prefix = " [x] & ; "
        self.assertEqual(self.run_demo("--mode", "prefix", "--prefix", prefix).returncode, 0)
        self.assertTrue((self.output / "result.txt").read_text(encoding="utf-8").startswith(prefix + "Hello"))

    def test_repeat_and_actual_progress(self) -> None:
        r = self.run_demo("--repeat", "3")
        self.assertEqual(r.returncode, 0)
        events = [json.loads(line.removeprefix("@@launcher:")) for line in r.stdout.splitlines() if line.startswith("@@launcher:")]
        self.assertEqual(len(events), 6)
        self.assertEqual(events[-1]["progress"], 6)
        self.assertEqual(events[-1]["total"], 6)

    def test_refuses_overwrite(self) -> None:
        self.assertEqual(self.run_demo().returncode, 0)
        before = (self.output / "result.txt").read_bytes()
        self.assertEqual(self.run_demo("--mode", "lowercase").returncode, 2)
        self.assertEqual(before, (self.output / "result.txt").read_bytes())

    def test_explicit_overwrite(self) -> None:
        self.assertEqual(self.run_demo().returncode, 0)
        self.assertEqual(self.run_demo("--mode", "lowercase", "--overwrite").returncode, 0)
        self.assertTrue((self.output / "result.txt").read_text(encoding="utf-8").startswith("hello"))

    def test_failure_code_and_no_output(self) -> None:
        self.assertEqual(self.run_demo("--fail").returncode, 7)
        self.assertFalse(self.output.exists())

    def test_secret_not_leaked(self) -> None:
        token = "test-token-never-print-921"
        r = self.run_demo(token=token)
        self.assertEqual(r.returncode, 0)
        self.assertNotIn(token, r.stdout + r.stderr)
        self.assertNotIn(token, (self.output / "summary.json").read_text(encoding="utf-8"))

    def test_notes_multiline_and_blank_lines(self) -> None:
        notes = "测试 note\nsecond line"
        self.assertEqual(self.run_demo("--notes", notes).returncode, 0)
        self.assertEqual(json.loads((self.output / "summary.json").read_text(encoding="utf-8"))["notes"], notes)

    def test_missing_input_failure(self) -> None:
        self.source.unlink()
        self.assertEqual(self.run_demo().returncode, 2)

    def test_bad_delay(self) -> None:
        self.assertEqual(self.run_demo("--delay", "-1").returncode, 2)
        self.assertEqual(self.run_demo("--delay", "nan").returncode, 2)

    def test_input_output_collision_refused(self) -> None:
        self.output.mkdir()
        self.source = self.output / "result.txt"
        self.source.write_text("precious original", encoding="utf-8")
        self.assertEqual(self.run_demo("--overwrite").returncode, 2)
        self.assertEqual(self.source.read_text(encoding="utf-8"), "precious original")

    def test_environment_probe_json(self) -> None:
        r = subprocess.run([sys.executable, str(ROOT / "demo" / "environment_probe.py")], text=True, encoding="utf-8", capture_output=True, timeout=10)
        self.assertEqual(r.returncode, 0)
        self.assertEqual(json.loads(r.stdout)["executable"], sys.executable)


if __name__ == "__main__":
    unittest.main(verbosity=2)
