"""Independent on-disk PcbLib name-table check. Development dependency: olefile.

Contract cross-check: AltiumSharp PcbLibReader.ReadSectionKeys / ReadPascalString.
No altiumrs reader or writer is used here. Does not edit input libraries.
"""
import argparse, struct, sys
from pathlib import Path
import olefile

def check(path):
    with olefile.OleFileIO(str(path)) as lib:
        def u32(data, pos):
            if pos + 4 > len(data): raise ValueError('truncated u32')
            return struct.unpack_from('<I', data, pos)[0]
        def block(data, pos):
            size = u32(data, pos); pos += 4
            if size < 1 or pos + size > len(data): raise ValueError('sized Pascal block exceeds stream')
            count = data[pos]
            if count + 1 > size: raise ValueError('Pascal string exceeds its block')
            return data[pos+1:pos+1+count].decode('cp1252'), pos + size
        mappings = {}
        if lib.exists('SectionKeys'):
            data = lib.openstream('SectionKeys').read(); count = u32(data, 0); pos = 4
            if count > len(data) // 10: raise ValueError('invalid table count')
            for _ in range(count):
                name, pos = block(data, pos); key, pos = block(data, pos)
                if not name or not key or len(key.encode('utf-16-le')) > 62: raise ValueError('empty or oversized name')
                if name.casefold() in mappings: raise ValueError('duplicate name')
                mappings[name.casefold()] = key
            if pos != len(data): raise ValueError('trailing table bytes')
        data = lib.openstream('Library/Data').read(); pos = 4 + u32(data, 0)
        count = u32(data, pos); pos += 4
        if count > len(data) // 5: raise ValueError('invalid library count')
        for _ in range(count):
            name, pos = block(data, pos)
            key = mappings.get(name.casefold(), ''.join('_' if c in '/\\:!*?"<>|' else c for c in name)[:31])
            if not lib.exists([key, 'Data']): raise ValueError('missing component storage: ' + key)
        if pos != len(data): raise ValueError('trailing Library/Data bytes')
        return count, len(mappings)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('libraries', nargs='+', type=Path)
    parser.add_argument('--expect-invalid', action='store_true')
    args = parser.parse_args(); failed = False
    for path in args.libraries:
        try:
            count, mapped = check(path)
            if args.expect_invalid: raise AssertionError('invalid fixture unexpectedly passed')
            print(f'PASS {path}: {count} footprints, {mapped} sized name mappings')
        except Exception as error:
            if args.expect_invalid and not isinstance(error, AssertionError):
                print(f'PASS invalid fixture rejected: {path}: {error}')
            else:
                print(f'FAIL {path}: {error}', file=sys.stderr); failed = True
    sys.exit(1 if failed else 0)
