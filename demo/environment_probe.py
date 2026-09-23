"""Print interpreter identity, not arbitrary environment variables or secrets."""
import json
import os
import sys

print(json.dumps({
    "executable": sys.executable,
    "prefix": sys.prefix,
    "base_prefix": sys.base_prefix,
    "version": sys.version.split()[0],
    "cwd": os.getcwd(),
    "VIRTUAL_ENV": os.environ.get("VIRTUAL_ENV"),
    "UV_PROJECT_ENVIRONMENT": os.environ.get("UV_PROJECT_ENVIRONMENT"),
    "UV_PROJECT": os.environ.get("UV_PROJECT"),
}, ensure_ascii=False, indent=2), flush=True)
