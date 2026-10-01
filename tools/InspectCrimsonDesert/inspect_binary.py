"""Read-only PE and byte-signature inspection; never opens a game process."""
import argparse
import hashlib
import json
import math
import mmap
from pathlib import Path
import re
import struct
from collections import Counter


def inspect(path, source):
    with path.open('rb') as handle, mmap.mmap(handle.fileno(), 0, access=mmap.ACCESS_READ) as data:
        pe = struct.unpack_from('<I', data, 0x3c)[0]
        if data[pe:pe + 4] != b'PE\0\0':
            raise ValueError('Not a PE file')
        machine, count, timestamp, _, _, opt_size, flags = struct.unpack_from('<HHIIIHH', data, pe + 4)
        opt = pe + 24
        base = struct.unpack_from('<Q', data, opt + 24)[0]
        entry = struct.unpack_from('<I', data, opt + 16)[0]
        image_size = struct.unpack_from('<I', data, opt + 56)[0]
        sections = []
        for i in range(count):
            at = opt + opt_size + 40 * i
            name, virtual_size, rva, raw_size, raw = struct.unpack_from('<8sIIII', data, at)
            chars = struct.unpack_from('<I', data, at + 36)[0]
            sample = data[raw:raw + min(raw_size, 1024 * 1024)]
            entropy = -sum((n / len(sample)) * math.log2(n / len(sample)) for n in Counter(sample).values()) if sample else 0
            sections.append(dict(name=name.rstrip(b'\0').decode('ascii', errors='replace'), rva=hex(rva), virtual_size=virtual_size, raw=hex(raw), raw_size=raw_size, characteristics=hex(chars), sample_entropy=round(entropy, 3)))

        def locate(offset):
            for section in sections:
                raw = int(section['raw'], 16)
                if raw <= offset < raw + section['raw_size']:
                    rva = int(section['rva'], 16) + offset - raw
                    return dict(file_offset=hex(offset), rva=hex(rva), va=hex(base + rva), section=section['name'])
            return dict(file_offset=hex(offset), section=None)

        def scan(pattern):
            values = [None if '?' in value or '*' in value else int(value, 16) for value in pattern.split()]
            runs = []
            start = 0
            while start < len(values):
                if values[start] is None:
                    start += 1
                    continue
                end = start + 1
                while end < len(values) and values[end] is not None:
                    end += 1
                runs.append((start, bytes(values[start:end])))
                start = end
            if not runs:
                return []
            offset, anchor = max(runs, key=lambda x: len(x[1]))
            found = []
            pos = 0
            while True:
                hit = data.find(anchor, pos)
                if hit < 0:
                    break
                pos = hit + 1
                hit -= offset
                if hit >= 0 and hit + len(values) <= len(data) and all(v is None or data[hit + i] == v for i, v in enumerate(values)):
                    found.append(locate(hit))
            return found

        source_text = source.read_text(encoding='utf-8-sig')
        signatures = []
        for match in re.finditer(r'const char\*\s+(kSig_\w+)\s*=\s*((?:"[^"\n]*"\s*)+);', source_text):
            name, quoted = match.groups()
            pattern = ' '.join(re.findall(r'"([^"\n]*)"', quoted))
            if re.fullmatch(r'[0-9a-fA-F?* ]+', pattern):
                matches = scan(pattern)
                signatures.append(dict(name=name, pattern=pattern, match_count=len(matches), matches=matches[:256]))
        for i, match in enumerate(re.finditer(r'\{"([0-9A-F? ]+)", (0x[0-9A-F]+|[0-9]+)\}', source_text)):
            pattern, mov_offset = match.groups()
            matches = scan(pattern)
            for result in matches:
                inst = int(result['file_offset'], 16) + int(mov_offset, 0)
                disp = struct.unpack_from('<i', data, inst + 3)[0]
                result['rip_target_rva'] = hex(int(result['rva'], 16) + int(mov_offset, 0) + 7 + disp)
            signatures.append(dict(name=f'CharMgrAnchor{i}', pattern=pattern, match_count=len(matches), matches=matches[:256]))
        return dict(path=str(path), size=len(data), sha256=hashlib.sha256(data).hexdigest(), machine=hex(machine), timestamp=timestamp, preferred_base=hex(base), entry_rva=hex(entry), image_size=image_size, sections=sections, source=str(source), signatures=signatures)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('exe', type=Path)
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    report = inspect(args.exe, args.source)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({k: report[k] for k in ('size', 'sha256', 'entry_rva', 'sections')}, indent=2))
    for signature in report['signatures']:
        print(signature['name'], signature['match_count'], signature['matches'][:3])
