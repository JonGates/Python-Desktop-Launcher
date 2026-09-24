#!/usr/bin/env python3
"""Source/asset consistency checks. This is NOT a C# compiler or a WPF test.
Run with Python 3.11+; optional PyYAML adds configuration-fixture structure checks.
"""
from __future__ import annotations
import ast
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'
checks: list[str] = []
errors: list[str] = []
notes: list[str] = []

def check(ok: bool, message: str) -> None:
    (checks if ok else errors).append(message)

def main() -> int:
    check('<Version>1.1.0</Version>' in (ROOT/'Directory.Build.props').read_text('utf-8-sig'), 'Release version is 1.1.0')
    build_script = (ROOT/'tools/Build.ps1').read_text('utf-8-sig')
    check("'win-x86'" in build_script, 'Build supports Windows x86')
    check("'README.zh-CN.md'" in build_script and "'docs/images'" in build_script, 'Build packages bilingual quickstart assets')
    xmls = sorted(set(ROOT.glob('src/**/*.xaml')) | set(ROOT.glob('**/*.csproj')) |
                  {ROOT / 'Directory.Build.props', ROOT / 'NuGet.Config', ROOT / 'src/ProjectLauncher.Desktop/app.manifest'})
    trees = {}
    for path in xmls:
        try:
            trees[path] = ET.parse(path)
            check(True, f'XML syntax: {path.relative_to(ROOT)}')
        except ET.ParseError as exc:
            check(False, f'XML syntax: {path.relative_to(ROOT)}: {exc}')
    for path, tree in trees.items():
        for ref in tree.iter('ProjectReference'):
            check((path.parent / ref.attrib['Include'].replace('\\', '/')).resolve().is_file(), f'Project reference: {path.relative_to(ROOT)} -> {ref.attrib["Include"]}')
        if path.suffix == '.xaml' and (klass := tree.getroot().get(X + 'Class')):
            code = path.with_name(path.name + '.cs')
            check(code.is_file(), f'XAML code-behind exists: {klass}')
            if code.is_file():
                text = code.read_text('utf-8-sig')
                check(bool(re.search(r'partial\s+class\s+' + re.escape(klass.rsplit('.', 1)[1]) + r'\b', text)), f'Partial class declaration: {klass}')
                for node in tree.iter():
                    for attr, value in node.attrib.items():
                        if attr in {'Click','Checked','Unchecked','Loaded','Unloaded','SizeChanged','SelectionChanged','TextChanged','PasswordChanged','KeyDown','PreviewKeyDown','Closing','Closed','MouseLeftButtonDown','ValueChanged'} and re.fullmatch(r'\w+', value):
                            check(bool(re.search(r'\b' + re.escape(value) + r'\s*\(', text)), f'Event handler: {path.name}: {value}')
                names = [node.get(X+'Name') for node in tree.iter() if node.get(X+'Name')]
                # Local template name scopes may repeat; source views here deliberately do not.
                check(len(names) == len(set(names)), f'Unique named elements: {path.name}')
        for node in tree.iter():
            if node.tag.endswith('TextBox') and node.get('IsReadOnly') == 'True' and node.get('Text','').startswith('{Binding'):
                check('Mode=OneWay' in node.attrib['Text'], f'Read-only binding direction: {path.name}: {node.get(X+"Name",node.attrib["Text"])}')
    keys = {n.get(X+'Key') for t in trees.values() for n in t.iter() if n.get(X+'Key')}
    for path in ROOT.glob('src/**/*.xaml'):
        for key in re.findall(r'\{(?:Static|Dynamic)Resource\s+([\w.]+)\}', path.read_text('utf-8-sig')):
            check(key in keys, f'Resource key: {path.name}: {key}')
    for path in ROOT.glob('src/**/*.cs'):
        for key in re.findall(r'(?:FindResource|TryFindResource)\("([\w.]+)"\)', path.read_text('utf-8-sig')):
            check(key in keys, f'Code resource key: {path.name}: {key}')
    theme_keys = []
    for name in ('Dark.xaml','Light.xaml'):
        t = trees[ROOT/'src/ProjectLauncher.Desktop/Themes'/name]
        theme_keys.append({n.get(X+'Key') for n in t.iter() if n.get(X+'Key')})
    check(theme_keys[0] == theme_keys[1], 'Dark/light semantic resource sets match')
    sln = (ROOT/'ProjectLauncher.sln').read_text()
    for rel in re.findall(r'"([^"\r\n]+\.csproj)"', sln):
        check((ROOT/rel.replace('\\','/')).is_file(), f'Solution project exists: {rel}')
    for path in list(ROOT.glob('src/**/*.cs')) + list(ROOT.glob('tools/*.ps1')):
        text = path.read_text('utf-8-sig')
        check('NotImplementedException' not in text and not re.search(r'\bTODO\b', text), f'No TODO/NotImplemented placeholders: {path.relative_to(ROOT)}')
    for path in list(ROOT.glob('demo/*.py')) + list(ROOT.glob('tests/demo/*.py')) + list(ROOT.glob('tools/*.py')) + list(ROOT.glob('examples/**/*.py')):
        try:
            ast.parse(path.read_text('utf-8-sig'), filename=str(path))
            check(True, f'Python syntax: {path.relative_to(ROOT)}')
        except SyntaxError as exc:
            check(False, f'Python syntax: {path.relative_to(ROOT)}: {exc}')
    for path in ROOT.glob('tools/*.ps1'):
        check(path.read_bytes().startswith(b'\xef\xbb\xbf'), f'PowerShell UTF-8 BOM for Windows PS 5.1: {path.name}')
    try:
        import yaml
    except ImportError:
        notes.append('PyYAML not installed: YAML fixture structure checks NOT run. C# configuration tests remain mandatory.')
    else:
        for path in [ROOT/'launcher.yaml', *ROOT.glob('examples/**/*.yaml'), *ROOT.glob('tests/fixtures/*.yaml')]:
            try:
                c = yaml.safe_load(path.read_text('utf-8-sig'))
                ps = [p['name'] for p in c.get('parameters', [])]
                check(c.get('schema_version',1)==1 and len(ps)==len(set(ps)), f'YAML fixture schema/unique parameters: {path.relative_to(ROOT)}')
                for action in c['actions']:
                    check(isinstance(action['argv'],list) and len(action['argv'])>0, f'YAML argv list: {path.name}: {action["id"]}')
                    check(action.get('parameters') is None or all(n in ps for n in action['parameters']), f'YAML parameter references: {path.name}: {action["id"]}')
            except Exception as exc:
                check(False, f'YAML structure: {path}: {exc}')
        notes.append('YAML fixture checks used PyYAML; they do not verify YamlDotNet behavior or C# validation.')
    for path in [ROOT/'README.md', ROOT/'README.zh-CN.md', *ROOT.glob('docs/*.md'), ROOT/'AGENTS.md']:
        text = re.sub(r'```.*?```','',path.read_text('utf-8-sig'),flags=re.S)
        for link in re.findall(r'\]\(([^)]+)\)', text):
            if '://' in link or link.startswith('#'):
                continue
            clean = link.split('#',1)[0]
            if clean:
                check((path.parent/clean).exists(), f'Documentation link: {path.name}: {clean}')
    check(not list(ROOT.glob('**/*.ttf')) and not list(ROOT.glob('**/*.otf')) and not list(ROOT.glob('**/*.woff*')), 'No font files bundled')
    print('Project Launcher static source checks — NOT a C# compilation or Windows test')
    for error in errors: print('FAIL:', error)
    for note in notes: print('NOTE:', note)
    print(f'CHECKS: {len(checks)+len(errors)} | PASSED: {len(checks)} | FAILED: {len(errors)}')
    print('NOT RUN HERE: dotnet build, C# specs, WPF rendering, Windows ConPTY / Job / lease integration.')
    return 1 if errors else 0

if __name__ == '__main__':
    sys.exit(main())
