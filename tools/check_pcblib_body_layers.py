"""Read-only OLE/byte checks for LibrarySearch's generated 3D Body layer pair.
Development dependency: olefile. No converter reader/writer is used.
"""
import re, struct, sys
from pathlib import Path
import olefile

def check(path):
    with olefile.OleFileIO(str(path)) as ole:
        data = ole.openstream('Library/Data').read()
        size = struct.unpack_from('<I', data)[0]
        if size < 1 or 4 + size > len(data) or data[3 + size] != 0:
            raise ValueError('invalid library parameter framing')
        params = dict(part.split('=', 1) for part in data[4:3 + size].decode('cp1252').split('|') if '=' in part)
        for cache, v8, legacy, layer_id, kind, name in [(12, 16, 62, 16908294, '3DBodyTop', 'Top 3D Body'), (13, 17, 63, 16908295, '3DBodyBottom', 'Bottom 3D Body')]:
            for prefix, separator in [(f'V9_CACHE_LAYER{cache}', '_'), (f'LAYER_V8_{v8}', '')]:
                for field, expected in [('LAYERID', str(layer_id)), ('NAME', name), ('MECHKIND', kind), ('MECHENABLED', 'TRUE')]:
                    if params.get(prefix + separator + field) != expected:
                        raise ValueError('missing or mismatched typed layer: ' + prefix + separator + field)
            if params.get(f'LAYER{legacy}MECHKIND') != kind:
                raise ValueError('legacy layer type mismatch')
        if params.get('MECHPAIR0L1') != 'MECHANICAL6' or params.get('MECHPAIR0L2') != 'MECHANICAL7':
            raise ValueError('missing legacy top/bottom pair')
        bodies = 0
        for entry in ole.listdir():
            if len(entry) != 2 or entry[0] == 'Library' or entry[1] != 'Data':
                continue
            raw = ole.openstream(entry).read()
            for match in re.finditer(rb'V7_LAYER=([^|\0]+)', raw):
                hit = match.start()
                if hit < 27 or raw[hit - 27] != 12:
                    continue
                block_size = struct.unpack_from('<I', raw, hit - 26)[0]
                param_size = struct.unpack_from('<I', raw, hit - 4)[0]
                if param_size < 1 or hit + param_size > len(raw) or raw[hit + param_size - 1] != 0 or block_size < 22 + param_size:
                    raise ValueError('invalid 3D body framing')
                layer = raw[hit - 22]
                token = match.group(1)
                if (layer, token) not in [(62, b'MECHANICAL6'), (63, b'MECHANICAL7')]:
                    raise ValueError('3D body is outside its dedicated pair')
                bodies += 1
        return bodies

if __name__ == '__main__':
    for value in sys.argv[1:]:
        path = Path(value)
        print(f'PASS {path}: {check(path)} bodies, V9/V8/legacy typed pair')
