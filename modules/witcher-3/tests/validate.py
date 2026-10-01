"""Offline integrity/API checks, deliberately not a WitcherScript interpreter/compiler."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[3]
EXE_HASH = 'C272B2C2E61F84C758E28FAB69AB2915944DD1E539DBB435FAE9FC67494C7E25'
FEATURES = {'witcher3_health', 'witcher3_stamina', 'witcher3_adrenaline', 'witcher3_toxicity',
            'witcher3_heal', 'witcher3_money', 'witcher3_xp', 'witcher3_skill'}
# Native/source declarations inspected in Steam build 25575366, version 5.0.0.1041720.
# Each entry pins the source location, minimum arity, and maximum arity.
API = {
    'EngineTimeToFloat': ('core/engineTime.ws', 1, 1),
    'GetEngineTime': ('engine/game.ws', 0, 0),
    'GetWitcherPlayer': ('game/player/playerWitcher.ws', 0, 0),
    'RandRange': ('core/math.ws', 1, 2),
    'FloatToString': ('core/string.ws', 1, 1),
    'IntToString': ('core/string.ws', 1, 1),
    'StringToFloat': ('core/string.ws', 1, 2),
    'GetStat': ('game/actor.ws', 1, 2),
    'GetStatMax': ('game/actor.ws', 1, 1),
    'ForceSetStat': ('game/actor.ws', 2, 2),
    'DrainToxicity': ('game/player/r4Player.ws', 1, 1),
    'IsBlackscreen': ('engine/game.ws', 0, 0),
    'IsAlive': ('game/actor.ws', 0, 0),
    'LoadCSVDirect': ('core/2DArray.ws', 2, 2),
    'GetNumRows': ('core/2DArray.ws', 0, 0),
    'GetValue': ('core/2DArray.ws', 2, 2),
    'GetMoney': ('game/player/player.ws', 0, 0),
    'AddMoney': ('game/player/player.ws', 1, 1),
    'AddPoints': ('game/player/playerWitcher.ws', 3, 3),
    'GetPointsTotal': ('game/player/playerWitcher.ws', 1, 1),
    'GetInGameConfigWrapper': ('game/commonGame.ws', 0, 0),
    'WriteIniFile': ('game/inGameConfigWrapper.ws', 3, 3),
}
ARRAY_API = {'Clear': (0, 0), 'Size': (0, 0), 'FindFirst': (1, 1), 'PushBack': (1, 1), 'Erase': (1, 1)}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def tokens(source):
    # Preserve quoted values as single tokens so commas/braces in protocol strings
    # cannot hide unbalanced code or alter counted call arguments.
    scanner = re.compile(r'\s+|//[^\n]*|/\*[\s\S]*?\*/|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|[A-Za-z_]\w*|\d+(?:\.\d*)?(?:[fF])?|==|!=|<=|>=|&&|\|\||\+=|[-+*/%=!<>.,;:{}()\[\]]')
    result = []
    offset = 0
    for match in scanner.finditer(source):
        require(match.start() == offset, f'Unrecognized/unterminated token at offset {offset}')
        offset = match.end()
        value = match.group()
        if not value.isspace() and not value.startswith(('//', '/*')):
            result.append(value)
    require(offset == len(source), f'Unrecognized/unterminated token at offset {offset}')
    stack = []
    matches = {}
    for index, value in enumerate(result):
        if value in ('(', '{', '['):
            stack.append((value, index))
        elif value in (')', '}', ']'):
            require(stack and {'(': ')', '{': '}', '[': ']'}[stack[-1][0]] == value, 'Unbalanced delimiters')
            _, start = stack.pop()
            matches[start] = index
    require(not stack, 'Unclosed delimiter')
    return result, matches


def arity(ts, pairs, start):
    end = pairs[start]
    if end == start + 1:
        return 0
    count, index = 1, start + 1
    while index < end:
        if ts[index] in ('(', '{', '['):
            index = pairs[index]
        elif ts[index] == ',':
            count += 1
        index += 1
    return count


def inspect_source(source):
    ts, pairs = tokens(source)
    local = {}
    for index, value in enumerate(ts[:-2]):
        if value == 'function':
            require(ts[index + 2] == '(', 'Malformed function declaration')
            local[ts[index + 1]] = arity(ts, pairs, index + 2)
    calls = set()
    for index in range(len(ts) - 1):
        name = ts[index]
        if ts[index + 1] != '(' or not re.fullmatch(r'[A-Za-z_]\w*', name):
            continue
        if name in ('if', 'while', 'for', 'switch') or (index > 0 and ts[index - 1] == 'function'):
            continue
        count = arity(ts, pairs, index + 1)
        if name in local:
            bounds = (local[name], local[name])
        elif name in ARRAY_API:
            bounds = ARRAY_API[name]
        else:
            require(name in API, f'Unreviewed API call: {name}')
            bounds = API[name][1:]
            calls.add(name)
        require(bounds[0] <= count <= bounds[1], f'{name} has {count} arguments; expected {bounds}')
    features = set(re.findall(r'"(witcher3_[a-z]+)"', source))
    require(features == FEATURES, f'Unexpected runtime controls: {features ^ FEATURES}')
    require('saved' not in ts, 'Trainer runtime state must not be saved')
    require(source.count('__HC_BRIDGE_PATH__') == 2, 'Expected two replaceable depot-relative CSV paths')
    require('WriteIniFile("modsHappyCheatsWitcher3", "Snapshot", snapshot)' in source,
            'Status INI requires basename without .ini; native writer appends extension')
    # These are source-contract assertions, not behavioral/runtime test claims.
    checks = {
        'player-session reset': r'current\s*!=\s*player',
        'lease expiry': r'if\s*\(now\s*>\s*leaseUntil\)\s*Disable\(\)',
        'bounded request age': r'now\s*-\s*issued\s*<=\s*3\.0',
        'bounded future clock': r'issued\s*<=\s*now\s*\+\s*1\.0',
        'session-matched CSV': r'GetValue\("session",\s*0\)\s*==\s*session',
        'schema guard': r'GetValue\("schema",\s*0\)\s*==\s*"1"',
        'request replay history': r'recentRequests\.FindFirst\(',
        'bounded replay history': r'recentRequests\.Size\(\)\s*>\s*32',
        'toggle value validation': r'enabled\s*!=\s*"0"\s*&&\s*enabled\s*!=\s*"1"',
        'lease and ready requirement': r'!ready\s*\|\|\s*now\s*>\s*leaseUntil',
    }
    for label, pattern in checks.items():
        require(re.search(pattern, source), f'Missing source contract: {label}')
    return calls


def find_game(explicit):
    if explicit:
        path = Path(explicit)
        require(path.is_dir(), f'Game directory does not exist: {path}')
        return path
    try:
        import winreg
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r'Software\Valve\Steam') as key:
            steam = Path(winreg.QueryValueEx(key, 'SteamPath')[0])
        libraries = [steam]
        vdf = (steam / 'steamapps/libraryfolders.vdf').read_text(encoding='utf-8')
        libraries.extend(Path(p.replace('\\\\', '\\')) for p in re.findall(r'"path"\s+"([^"]+)"', vdf))
        for library in libraries:
            manifest = library / 'steamapps/appmanifest_292030.acf'
            if manifest.is_file():
                match = re.search(r'"installdir"\s+"([^"]+)"', manifest.read_text(encoding='utf-8'))
                if match:
                    return library / 'steamapps/common' / match.group(1)
    except (ImportError, OSError):
        pass
    return None


def validate_game(game, calls):
    require(sha(game / 'bin/x64_dx12/witcher3.exe') == EXE_HASH, 'Installed executable differs from verified Steam build 25575366')
    scripts = game / 'content/content0/scripts'
    for name in calls:
        path, minimum, maximum = API[name]
        source = (scripts / path).read_text(encoding='utf-8-sig')
        match = re.search(r'\bfunction\s+' + name + r'\s*\(([^)]*)\)', source)
        require(match, f'Installed API declaration missing: {path}: {name}')
        parameters = [p.strip() for p in match.group(1).split(',') if p.strip()]
        required = sum(not re.search(r'\boptional\b', p) for p in parameters)
        require((required, len(parameters)) == (minimum, maximum), f'Changed API signature: {name}')
    player = (scripts / 'game/player/playerWitcher.ws').read_text(encoding='utf-8-sig')
    require(re.search(r'public\s+saved\s+var\s+levelManager\s*:\s*W3LevelManager', player), 'Level manager is no longer public')
    stats = (scripts / 'game/characterStats.ws').read_text(encoding='utf-8-sig')
    for name in ('BCS_Vitality', 'BCS_Stamina', 'BCS_Focus', 'BCS_Toxicity'):
        require(name in stats, f'Unknown base stat: {name}')
    points = (scripts / 'game/gameplay/leveling/levelTypes.ws').read_text(encoding='utf-8-sig')
    require('ESkillPoint' in points and 'EExperiencePoint' in points, 'Point enums changed')
    print(f'Installed v5.0 executable pin and {len(calls)} API signatures verified read-only.')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--game-directory')
    args = parser.parse_args()
    source_path = ROOT / 'modules/witcher-3/happycheats.ws'
    calls = inspect_source(source_path.read_text(encoding='utf-8-sig'))
    package = ROOT / 'payload/witcher-3'
    manifest = json.loads((package / 'package.json').read_text(encoding='utf-8-sig'))
    require(manifest['schemaVersion'] == 1 and manifest['game'] == 'witcher-3' and manifest['version'] == '0.1.1', 'Invalid payload identity')
    files = manifest['files']
    require(len(files) == 1 and files[0]['path'] == 'happycheats.ws', 'Unexpected payload content')
    require(sha(source_path) == sha(package / files[0]['path']) == files[0]['sha256'].upper(), 'Stale or damaged Witcher 3 payload')
    game = find_game(args.game_directory)
    if game:
        validate_game(game, calls)
    else:
        print('Installed-game API comparison skipped: Steam app 292030 not installed.')
    print('Payload integrity, lexical delimiters, call arities, control list and protocol source contracts verified.')
    print('STATIC VALIDATION ONLY: not WitcherScript compilation, execution, or gameplay testing.')


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError) as error:
        print(f'Witcher 3 validation failed: {error}', file=sys.stderr)
        sys.exit(1)
