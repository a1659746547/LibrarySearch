"""Independent binary audit. Supply official C50380974 source JSON and its legacy PcbLib."""
import argparse,copy,json,math,pathlib,shutil,struct,subprocess,hashlib
import olefile
parser=argparse.ArgumentParser();parser.add_argument('--helper',required=True,type=pathlib.Path);parser.add_argument('--source',required=True,type=pathlib.Path);parser.add_argument('--library',required=True,type=pathlib.Path);parser.add_argument('--output',required=True,type=pathlib.Path);args=parser.parse_args()
args.output.mkdir(parents=True,exist_ok=True);key='USB-C-SMD_LCK-TCF809-L124YL-G1/Data';envelope=json.loads(args.source.read_text(encoding='utf-8-sig'))
with olefile.OleFileIO(args.library) as original: original_streams={tuple(p):original.openstream(p).read() for p in original.listdir()}
raw=original_streams[tuple(key.split('/'))];offsets={}
for number in ('1','2','3','4'):
 needle=b'\x02'+struct.pack('<I',2)+b'\x01'+number.encode();start=raw.find(needle);assert start>=0;pos=start+1;blocks=[]
 for _ in range(6):
  size=struct.unpack_from('<I',raw,pos)[0]&0xffffff;pos+=4;blocks.append((pos,size));pos+=size
 assert blocks[4][1]==202 and blocks[5][1]>=596
 offsets[number]=(blocks[4][0],blocks[5][0]+267)
count=0
for label,angle,pad_rotation in [('vertical',90,0),('horizontal',0,0),('diagonal',45,0),('rotated-pad',45,30),('reverse-diagonal',135,90)]:
 data=copy.deepcopy(envelope);expected={};target=args.output/(label+'.PcbLib');source=args.output/(label+'.json');shutil.copyfile(args.library,target);input_stream=bytearray(raw)
 shapes=data['result']['packageDetail']['dataStr']['shape']
 for i,text in enumerate(shapes):
  if not text.startswith('PAD~'):continue
  parts=text.split('~');number=parts[8]
  if parts[6]!='11' or number not in offsets:continue
  cx,cy=float(parts[2]),float(parts[3]);radius,length=float(parts[9]),float(parts[13]);distance=length-2*radius;dx=math.cos(math.radians(angle))*distance/2;dy=-math.sin(math.radians(angle))*distance/2
  parts[14]=f'{cx-dx} {cy-dy} {cx+dx} {cy+dy}';parts[11]=str(pad_rotation);shapes[i]='~'.join(parts);main,hole=offsets[number];struct.pack_into('<d',input_stream,main+52,pad_rotation);expected[hole]=(angle-pad_rotation)%180
 with olefile.OleFileIO(target,write_mode=True) as f:f.write_stream(key,bytes(input_stream))
 source.write_text(json.dumps(data),encoding='utf-8');subprocess.run([str(args.helper),'--lcsc-id','C50380974','--source',str(source),'--library',str(target)],check=True,capture_output=True)
 with olefile.OleFileIO(target) as f:
  result=f.openstream(key).read();assert len(result)==len(input_stream)
  for offset,value in expected.items():assert abs(struct.unpack_from('<d',result,offset)[0]-value)<1e-7
  allowed={i for offset in expected for i in range(offset,offset+8)};assert all(i in allowed for i,(a,b) in enumerate(zip(input_stream,result)) if a!=b)
  for stream,before in original_streams.items():
   if '/'.join(stream)!=key:assert f.openstream(stream).read()==before
 digest=hashlib.sha256(target.read_bytes()).hexdigest();subprocess.run([str(args.helper),'--lcsc-id','C50380974','--source',str(source),'--library',str(target)],check=True,capture_output=True);assert hashlib.sha256(target.read_bytes()).hexdigest()==digest
 count+=1;print('PASS:',label,'four slot angles, unchanged non-angle bytes/other streams, idempotence')
for label in ('wrong-code','missing-points','dimension-mismatch','offset-hole'):
 data=copy.deepcopy(envelope);source=args.output/(label+'.json');target=args.output/(label+'.PcbLib');shutil.copyfile(args.library,target)
 if label=='wrong-code':data['result']['lcsc']['number']='C1'
 else:
  shapes=data['result']['packageDetail']['dataStr']['shape']
  for i,text in enumerate(shapes):
   if text.startswith('PAD~'):
    parts=text.split('~')
    if parts[6]=='11' and parts[8]=='1':
     if label=='missing-points':parts[14]=''
     elif label=='dimension-mismatch':parts[13]='99'
     else:parts[14]='1 2 1 3'
     shapes[i]='~'.join(parts)
 source.write_text(json.dumps(data),encoding='utf-8');digest=hashlib.sha256(target.read_bytes()).hexdigest();result=subprocess.run([str(args.helper),'--lcsc-id','C50380974','--source',str(source),'--library',str(target)],capture_output=True);assert result.returncode!=0 and hashlib.sha256(target.read_bytes()).hexdigest()==digest
 count+=1;print('PASS:',label,'rejected without modifying input library')
print('RESULT:',count,'binary fixture groups passed')
