"""Run the real Civ VI Lua bridge against offline API fixtures, without a game."""
from pathlib import Path
import json
import os
import re
import sys

root = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(root / "artifacts/astroneer/python"))
try:
    from lupa.lua54 import LuaRuntime
except ImportError:
    raise SystemExit("Lua test runtime missing: install lupa==2.6 into artifacts/astroneer/python.")
os.chdir(root)
source = root / "modules/civilization-vi/Scripts/runtime.lua"
LuaRuntime().execute("assert(load(...))", source.read_text(encoding="utf-8"))
# Test the exact expression shipped by the client, so a corrected Lua fixture
# cannot hide a regression in the separately embedded InGame request.
module_source = (root / "src/HappyCheats.Core/CivilizationVIModule.cs").read_text(encoding="utf-8")
selection_match = re.search(
    r'internal const string SelectionExpression\s*=\s*((?:"(?:\\.|[^"\\])*"\s*\+?\s*)+);',
    module_source,
)
if selection_match is None:
    raise SystemExit("Cannot extract the client SelectionExpression string literals.")
selection_expression = "".join(
    json.loads(literal)
    for literal in re.findall(r'"(?:\\.|[^"\\])*"', selection_match.group(1))
)
tests = sorted(Path(__file__).parent.glob("*_test.lua"))
for path in tests:
    lua = LuaRuntime()
    lua.globals().decode = lambda text: lua.table_from(json.loads(text), recursive=True)
    lua.globals().selection_expression = selection_expression
    lua.execute(path.read_text(encoding="utf-8"))
print(f"Civilization VI: real Lua syntax checked; {len(tests)} offline suite(s) passed.")
