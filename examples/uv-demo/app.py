"""A tiny real uv project; no third-party Python packages."""
import argparse
import sys

parser = argparse.ArgumentParser()
parser.add_argument("--who", default="朋友")
args = parser.parse_args()
print(f"你好，{args.who}！")
print("解释器：", sys.executable)
print("项目环境：", sys.prefix)
