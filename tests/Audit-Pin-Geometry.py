"""Independent SVG-line oracle for audit_pin_geometry output; no AD automation.

Usage: python Audit-Pin-Geometry.py SOURCE_DIR EMITTED_DIR [OLD_ESP_GEOMETRY_JSON] [LEGACY_GEOMETRY_DIR]
The oracle tokenizes M/L/H/V directly and compares both the inner body endpoint
and the outer electrical endpoint, not only a self-consistent pin-length formula.
"""
import collections
import json
import math
from pathlib import Path
import re
import sys

def line_endpoints(path):
    tokens=re.findall(r'[MLHVmlhv]|[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?',path)
    assert re.sub(r'[\s,]','',path)==''.join(tokens),path
    position=(0.,0.);start=None;command=None;at=0
    while at<len(tokens):
        if tokens[at].isalpha(): command=tokens[at];at+=1
        absolute=command.isupper();kind=command.upper()
        count=2 if kind in ('M','L') else 1
        values=[float(value) for value in tokens[at:at+count]];assert len(values)==count;at+=count
        if kind in ('M','L'):
            position=tuple(values[i]+(0 if absolute else position[i]) for i in range(2))
            if kind=='M': assert start is None;start=position;command='L' if absolute else 'l'
        elif kind=='H':position=(values[0]+(0 if absolute else position[0]),position[1])
        elif kind=='V':position=(position[0],values[0]+(0 if absolute else position[1]))
    assert start is not None
    return start,position

def origin_coordinate(value):
    return math.floor(value/5+.5)*5 if value>=0 else math.ceil(value/5-.5)*5

def expected_pins(data):
    drawing=data['dataStr'];bounds=drawing.get('BBox',{})
    if bounds.get('width',0)>0 or bounds.get('height',0)>0:
        origin=(float(bounds['x'])+float(bounds['width'])/2,float(bounds['y'])+float(bounds['height'])/2)
    else:origin=(float(drawing['head']['x']),float(drawing['head']['y']))
    origin=tuple(origin_coordinate(value) for value in origin)
    result={}
    def transform(point):return ((point[0]-origin[0])*10,-(point[1]-origin[1])*10)
    for part,unit in enumerate([data]+data.get('subparts',[]),1):
        for shape in unit['dataStr'].get('shape',[]):
            if not shape.startswith('P~'):continue
            segments=shape.split('^^');settings=segments[0].split('~')
            pin=segments[4].split('~')[4];path=segments[2].split('~')[0]
            start,end=line_endpoints(path)
            electrical=(float(settings[4]),float(settings[5]))
            if math.dist(start,electrical)>1e-6:start,end=end,start
            assert math.dist(start,electrical)<1e-6
            result[(part,pin)]={'Body':transform(end),'Connection':transform(start),'Length':math.dist(start,end)*10,
                'Direction':int(float(settings[6] or '0'))%360//90,'Name':segments[3].split('~')[4],
                'ShowName':segments[3].split('~')[0]=='1' and bool(segments[3].split('~')[4]),
                'ShowNumber':segments[4].split('~')[0]=='1' and bool(pin),
                'Electrical':{0:4,1:0,2:2,3:1,4:7}.get(int(settings[2] or '0'),4),
                'Hidden':settings[1].lower() not in ('show','1','true'),
                'Symbols':[3 if segments[6].split('~')[0].lower() in ('1','true') else 0,1 if segments[5].split('~')[0].lower() in ('1','true') else 0,0,0]}
    return result

vectors=((1,0),(0,1),(-1,0),(0,-1))
def differences(actual,expected):
    faults=[]
    assert len(actual)==len(expected),(len(actual),len(expected))
    for pin in actual:
        key=(pin['Part'],pin['Number']);want=expected[key];body=pin['Body'];direction=vectors[pin['Direction']]
        connection=tuple(body[i]+direction[i]*pin['Length'] for i in range(2))
        if math.dist(body,want['Body'])>1e-4 or math.dist(connection,want['Connection'])>1e-4 or abs(pin['Length']-want['Length'])>1e-4 or pin['Direction']!=want['Direction']:
            faults.append({'Pin':key,'Body':body,'ExpectedBody':want['Body'],'Connection':connection,'ExpectedConnection':want['Connection'],'Length':pin['Length'],'ExpectedLength':want['Length']})
    return faults

sources=Path(sys.argv[1]);emitted=Path(sys.argv[2]);results=[];directions=collections.Counter()
for path in sorted(sources.glob('*.json')):
    envelope=json.loads(path.read_text(encoding='utf-8-sig'));data=envelope.get('result',envelope)
    expected=expected_pins(data)
    for mode in ('classic','modern'):
        actual=json.loads((emitted/f'{path.stem}-{mode}.json').read_text(encoding='utf-8'))
        faults=differences(actual,expected);assert not faults,(path.stem,mode,faults[:3])
        for pin in actual:
            want=expected[(pin['Part'],pin['Number'])]
            show_name=want['ShowName']
            assert pin['ShowName']==show_name and pin['ShowNumber']==want['ShowNumber'],(path.stem,mode,pin,want)
            assert pin['FormalType']==1 and pin['Electrical']==want['Electrical'] and pin['Hidden']==want['Hidden'] and pin['Symbols']==want['Symbols'],(path.stem,mode,pin,want)
    classic=json.loads((emitted/f'{path.stem}-classic.json').read_text())
    modern=json.loads((emitted/f'{path.stem}-modern.json').read_text())
    electrical=lambda pins:[{key:value for key,value in pin.items() if key not in ('ShowName','ShowNumber')} for pin in pins]
    assert electrical(classic)==electrical(modern),'Appearance changed electrical geometry'
    directions.update(pin['Direction'] for pin in classic)
    # Detect affected legacy compact syntax without running the old converter.
    missed=0
    for unit in [data]+data.get('subparts',[]):
        for shape in unit['dataStr'].get('shape',[]):
            if shape.startswith('P~'):
                raw=shape.split('^^')[2].split('~')[0]
                if not any(token.startswith(('h','v')) for token in raw.split()):missed+=1
    row={'Code':path.stem,'Name':data.get('title',''),'Pins':len(expected),'LegacyUnrecognizedPaths':missed}
    if len(sys.argv)>4:
        legacy=json.loads((Path(sys.argv[4])/path.name).read_text())
        row['LegacyGeometryMismatches']=len(differences(legacy,expected))
        print(f"LEGACY {path.stem}: {row['LegacyGeometryMismatches']} actual mismatches")
    results.append(row);print(f"PASS {path.stem}: {len(expected)} pins, both endpoints and length, classic + modern; legacy unrecognized paths={missed}")

report={'Components':results,'UniquePins':sum(row['Pins'] for row in results),'Styles':2,'Directions':dict(directions)}
if len(sys.argv)>3:
    old=json.loads(Path(sys.argv[3]).read_text());data=json.loads((sources/'C2913196.json').read_text())['result']
    report['OldESP32Mismatches']=differences(old,expected_pins(data))
    print('Old ESP32 mismatches:',len(report['OldESP32Mismatches']))
(emitted/'audit-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('TOTAL:',len(results),'components;',report['UniquePins'],'unique pins;',report['UniquePins']*2,'serialized pins across two styles; directions',dict(directions))
