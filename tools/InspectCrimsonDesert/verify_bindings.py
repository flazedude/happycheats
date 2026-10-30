"""Compare native trainer guards with an executable on disk; never open a process."""
import argparse
import hashlib
import json
import mmap
from pathlib import Path
import re
import struct


def number(value):
    return int(value, 16 if value.lower().startswith('0x') else 10)


def uncomment(text):
    tokens = r'"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|//[^\n]*|/\*.*?\*/'
    return re.sub(tokens, lambda m: ' ' if m[0].startswith('/') else m[0], text, flags=re.S)


def byte_arrays(body):
    arrays = {}
    pattern = r'(?:const|constexpr)\s+std::array<unsigned\s+char,\s*(\d+)>\s+(\w+)\s*\{\{(.*?)\}\};'
    for size, name, values in re.findall(pattern, body, re.S):
        items = values.strip().rstrip(',').split(',')
        if any(not re.fullmatch(r'\s*(?:0x[\da-fA-F]+|\d+)\s*', item) for item in items):
            raise ValueError('Unsupported byte initializer: ' + name)
        value = bytes(number(item.strip()) for item in items)
        if name in arrays or len(value) != int(size) or not value:
            raise ValueError('Duplicate array or invalid byte count: ' + name)
        arrays[name] = value
    return arrays


def source_guards(root):
    native = root / 'modules/crimson-desert/native'
    inputs = {native / name: (native / name).read_bytes()
              for name in ('trainer.cpp', 'trainer_logic.h', 'build_guard.h')}
    texts = [uncomment(value.decode('utf-8-sig')) for value in inputs.values()]
    constants = {}
    for name, value in re.findall(r'constexpr\s+[\w:]+\s+(\w+)\s*=\s*(0x[\da-fA-F]+|\d+)\s*;', '\n'.join(texts)):
        if name in constants and constants[name] != number(value):
            raise ValueError('Conflicting constant: ' + name)
        constants[name] = number(value)
    functions = dict(re.findall(r'^bool\s+(Validate\w+)\(\)\s*\{(.*?)^\}', texts[0], re.M | re.S))
    required = {'ValidateBaseBindings', 'ValidateMarkerBindings',
                'ValidateOuterMovementBinding', 'ValidateClientBindings'}
    validators = {name: body for name, body in functions.items() if 'Binding' in name}
    if not required <= validators.keys():
        raise ValueError('Required native binding validator not parsed')
    bindings = []
    for function, body in validators.items():
        arrays = byte_arrays(body)
        calls = re.findall(r'\bMatches\(([^,]+),\s*(\w+)\)', body)
        if not calls or len(calls) != len(re.findall(r'\bMatches\s*\(', body)) or set(arrays) != {name for _, name in calls}:
            raise ValueError('Unparsed or unused binding guard in ' + function)
        for expression, name in calls:
            match = re.fullmatch(r'g_game\+(?:crimson_logic::)?(\w+)(?:-(\d+))?', re.sub(r'\s+', '', expression))
            if not match:
                raise ValueError('Unsupported binding address: ' + expression)
            token, subtract = match.groups()
            rva = constants[token] if token in constants else number(token)
            bindings.append((function, name, rva - int(subtract or 0), arrays[name]))
    # ASLR-adjusted runtime slot checks must also be proved against the file's
    # preferred image base. Parsing byte guards alone missed these class gates.
    client_body = validators['ValidateClientBindings']
    pointer_guards = re.findall(
        r'Pointer\(g_game\+crimson_logic::(\w+)\+(0x[\da-fA-F]+),\s*(\w+)\)\s*&&\s*'
        r'\3==g_game\+crimson_logic::(\w+)', client_body)
    if len(pointer_guards) != 2 or len(re.findall(r'\bPointer\s*\(', client_body)) != len(pointer_guards):
        raise ValueError('Unparsed client vtable slot guard')
    if {name for name, _, _, _ in pointer_guards} != {'ClientOwnerVtableRva', 'ClientChildOwnerVtableRva'}:
        raise ValueError('Required client class slot guard missing')
    for name, offset, _, target in pointer_guards:
        bindings.append(('ValidateClientBindings', name + '.local_control_slot',
                         constants[name] + number(offset), constants[target]))
    # Count the Matches template declaration too; future unparsed call sites fail closed.
    if len(re.findall(r'\bMatches\s*\(', texts[0])) != len(bindings) - len(pointer_guards) + 1:
        raise ValueError('Matches call outside parsed binding validators')
    digest = byte_arrays(functions.get('ValidateExecutableHash', '')).get('expected', b'')
    if len(digest) != 32:
        raise ValueError('Native executable SHA-256 guard not parsed')
    expected = {name: constants[key] for name, key in (
        ('machine', 'ExpectedMachine'), ('timestamp', 'ExpectedTimestamp'), ('image_size', 'ExpectedImageSize'))}
    expected['sha256'] = digest.hex()
    return expected, bindings, inputs


def pe_metadata(data):
    def unpack(fmt, offset):
        if offset < 0 or offset + struct.calcsize(fmt) > len(data):
            raise ValueError('Truncated PE structure')
        return struct.unpack_from(fmt, data, offset)
    if data[:2] != b'MZ':
        raise ValueError('Invalid DOS signature')
    pe, = unpack('<I', 0x3c)
    if pe > 0x100000 or data[pe:pe + 4] != b'PE\0\0':
        raise ValueError('Invalid PE signature or offset')
    machine, count, timestamp, _, _, opt_size, _ = unpack('<HHIIIHH', pe + 4)
    opt = pe + 24
    if opt_size < 112 or unpack('<H', opt)[0] != 0x20b or not 0 < count <= 96:
        raise ValueError('Invalid PE32+ header or section count')
    image_size, = unpack('<I', opt + 56)
    sections = []
    for i in range(count):
        name, virtual_size, rva, raw_size, raw = unpack('<8sIIII', opt + opt_size + i * 40)
        unpack('<I', opt + opt_size + i * 40 + 36)
        if raw_size and raw + raw_size > len(data):
            raise ValueError('Raw section extends beyond file')
        sections.append((name.rstrip(b'\0').decode('ascii', errors='replace'), rva, virtual_size, raw, raw_size))
    return dict(machine=machine, timestamp=timestamp, image_size=image_size), sections


def map_rva(sections, rva, length, file_size):
    if rva < 0 or length <= 0:
        raise ValueError('Invalid RVA range')
    matches = [s for s in sections if s[1] < rva + length and rva < s[1] + min(s[2], s[4])]
    if len(matches) != 1 or not (matches[0][1] <= rva and rva + length <= matches[0][1] + min(matches[0][2], matches[0][4])):
        raise ValueError(f'RVA {rva:#x} length {length} lacks unique raw-backed section')
    section = matches[0]
    offset = section[3] + rva - section[1]
    if offset < 0 or offset + length > file_size:
        raise ValueError('Mapped bytes extend beyond file')
    return offset, section[0]


def verify(exe, root):
    expected, bindings, inputs = source_guards(root)
    before = exe.stat()
    checks = []
    with exe.open('rb') as handle, mmap.mmap(handle.fileno(), 0, access=mmap.ACCESS_READ) as data:
        observed, sections = pe_metadata(data)
        observed['sha256'] = hashlib.sha256(data).hexdigest()
        pe, = struct.unpack_from('<I', data, 0x3c)
        preferred_base, = struct.unpack_from('<Q', data, pe + 24 + 24)
        for name, value in expected.items():
            checks.append(dict(name=name, expected=value, observed=observed[name], match=value == observed[name]))
        for function, name, rva, value in bindings:
            if isinstance(value, int):
                value = struct.pack('<Q', preferred_base + value)
            item = dict(name=function + '.' + name, rva=hex(rva), expected=value.hex(' '))
            try:
                offset, section = map_rva(sections, rva, len(value), len(data))
                actual = bytes(data[offset:offset + len(value)])
                item.update(file_offset=hex(offset), section=section, observed=actual.hex(' '), match=actual == value)
            except ValueError as error:
                item.update(match=False, error=str(error))
            checks.append(item)
    after = exe.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise ValueError('Executable changed during verification')
    if any(path.read_bytes() != value for path, value in inputs.items()):
        raise ValueError('Native source changed during verification')
    return dict(schema_version=1, executable=str(exe), source_root=str(root), passed=all(c['match'] for c in checks),
                check_count=len(checks), match_count=sum(c['match'] for c in checks), checks=checks,
                source_inputs=[dict(path=str(p.relative_to(root)), sha256=hashlib.sha256(v).hexdigest()) for p, v in inputs.items()],
                limitations='Exact disk guards only; field semantics, runtime hooks and gameplay are not validated.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('exe', type=Path)
    parser.add_argument('--source-root', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--output', type=Path, help='Optional JSON report; stdout always receives JSON')
    args = parser.parse_args()
    exe, root = args.exe.resolve(), args.source_root.resolve()
    try:
        if args.output and (args.output.resolve() == exe or args.output.suffix.lower() != '.json'
                            or (args.output.exists() and args.output.samefile(exe))):
            raise ValueError('Report must be a .json file distinct from the executable')
        report = verify(exe, root)
        code = 0 if report['passed'] else 1
    except (OSError, ValueError, KeyError, struct.error) as error:
        report, code = dict(passed=False, error=str(error)), 2
    text = json.dumps(report, indent=2) + '\n'
    print(text, end='')
    if args.output and code != 2:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text, encoding='utf-8')
    return code


if __name__ == '__main__':
    raise SystemExit(main())
