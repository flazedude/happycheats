"""Offline Lua 5.4 fixtures. No game process, network or installed game files are used."""
from pathlib import Path
import os
import sys

root = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(root / 'artifacts/astroneer/python'))
try:
    from lupa.lua54 import LuaRuntime
except ImportError:
    raise SystemExit('Lua test runtime missing. Install: python -m pip install --target artifacts/astroneer/python lupa==2.6')
os.chdir(root)
scripts = root / 'modules/phantom-blade/Scripts'
for path in sorted(scripts.glob('*.lua')):
    LuaRuntime().execute('assert(load(...))', path.read_text(encoding='utf-8'))
tests = sorted(Path(__file__).parent.glob('*_test.lua'))
for path in tests:
    LuaRuntime().execute(path.read_text(encoding='utf-8'))
print(f'Phantom Blade Zero: syntax checked {len(list(scripts.glob("*.lua")))} Lua files; {len(tests)} offline suites passed.')
