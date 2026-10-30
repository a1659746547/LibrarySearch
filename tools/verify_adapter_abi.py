"""Verify unchanged SDK adapters against each newly built core (dnfile 0.18)."""
from pathlib import Path
import argparse,hashlib,json,sys
p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--metadata-libs',type=Path,required=True);args=p.parse_args()
sys.path.insert(0,str(args.metadata_libs));import dnfile
root=args.root.resolve();plugin=root/'plugin'
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest().upper()
def tables(pe,name):return getattr(pe.net.mdtables,name).rows
def typename(row,pe):
    if row.__class__.__name__=='TypeSpecRow':return Signature(row.Signature.value,pe).type()
    return str(row.TypeNamespace)+'.'+str(row.TypeName)
class Signature:
    def __init__(self,blob,pe):self.b=blob;self.i=0;self.pe=pe
    def integer(self):
        x=self.b[self.i];self.i+=1
        if x<128:return x
        count=1 if x&192==128 else 3;v=x&63 if count==1 else x&31
        for _ in range(count):v=(v<<8)|self.b[self.i];self.i+=1
        return v
    def token(self):
        n=self.integer();return typename(tables(self.pe,['TypeDef','TypeRef','TypeSpec'][n&3])[(n>>2)-1],self.pe)
    def type(self):
        n=self.integer()
        names={1:'void',2:'bool',3:'char',4:'i1',5:'u1',6:'i2',7:'u2',8:'i4',9:'u4',10:'i8',11:'u8',12:'r4',13:'r8',14:'string',24:'native-int',25:'native-uint',28:'object'}
        if n in names:return names[n]
        if n in (17,18):return self.token()
        if n in (15,16,29):return str(n)+'('+self.type()+')'
        if n in (19,30):return str(n)+':'+str(self.integer())
        if n==21:
            self.integer();name=self.token();count=self.integer();return name+'<'+','.join(self.type() for _ in range(count))+'>'
        if n in (31,32):return str(n)+self.token()+self.type()
        raise ValueError(('unsupported signature element',n,self.b.hex()))
    def member(self):
        flag=self.integer()
        if flag==6:return ('field',self.type())
        generic=self.integer() if flag&16 else 0;count=self.integer();ret=self.type();params=tuple(self.type() for _ in range(count))
        assert self.i==len(self.b),(self.b.hex(),self.i)
        return ('method',flag,generic,ret,params)
def core_members(pe):
    result={}
    for t in tables(pe,'TypeDef'):
        for field in t.FieldList:result.setdefault((typename(t,pe),str(field.row.Name)),set()).add(Signature(field.row.Signature.value,pe).member())
        for method in t.MethodList:result.setdefault((typename(t,pe),str(method.row.Name)),set()).add(Signature(method.row.Signature.value,pe).member())
    return result
manifest=json.loads((plugin/'hosts/targets.json').read_text('utf-8-sig'));core_cache={};results=[]
for host in manifest['Targets']:
    host_path=plugin/host['Dll'].replace('\\','/');assert sha(host_path)==host['DllSHA256'].upper()
    runtime=host.get('ManagedRuntime','netfx48');meta='build-info.json' if runtime=='netfx48' else 'build-info-'+runtime+'.json'
    info=json.loads((plugin/'shared'/meta).read_text('utf-8-sig'));core_path=plugin/info['CorePath'].replace('\\','/')
    if runtime not in core_cache:
        new=dnfile.dnPE(str(core_path));members=core_members(new)
        old=dnfile.dnPE(str(root/'compatibility/original-cores'/Path(info['CorePath'].replace('\\','/')).name))
        identity=lambda pe:(str(tables(pe,'Assembly')[0].Name),tables(pe,'Assembly')[0].MajorVersion,tables(pe,'Assembly')[0].MinorVersion,tables(pe,'Assembly')[0].BuildNumber,tables(pe,'Assembly')[0].RevisionNumber,tables(pe,'Assembly')[0].PublicKey.value)
        assert identity(new)==identity(old),'Core assembly identity changed'
        core_cache[runtime]=(new,members)
    new,members=core_cache[runtime];adapter=dnfile.dnPE(str(host_path));checked=[]
    for ref in tables(adapter,'MemberRef'):
        parent=ref.Class.row
        if parent.__class__.__name__!='TypeRefRow' or str(parent.TypeNamespace)!='LibrarySearch':continue
        key=(typename(parent,adapter),str(ref.Name));signature=Signature(ref.Signature.value,adapter).member()
        assert signature in members.get(key,set()),(host['AdMajor'],key,signature)
        checked.append({'Type':key[0],'Member':key[1],'Signature':signature})
    assert len(checked)>=15,'Adapter does not expose expected core calls'
    results.append({'AdMajor':host['AdMajor'],'HostPath':host['Dll'],'HostSHA256':sha(host_path),'CorePath':info['CorePath'],'CoreSHA256':sha(core_path),'ManagedRuntime':runtime,'CheckedMembers':checked})
    print('PASS AD'+str(host['AdMajor']),runtime,len(checked),'SDK-adapter calls resolve against new core')
versions={json.loads((plugin/'shared'/meta).read_text('utf-8-sig'))['PluginVersion'] for meta in ['build-info.json','build-info-net6windows.json','build-info-net8windows.json']}
assert len(versions)==1,'Shared core versions differ'
report={'SchemaVersion':1,'PluginVersion':versions.pop(),'CoreBindingVersion':'1.1.0-beta.2','OriginalPackageSHA256':'FDFA6DE3C936DAA7AA92590F23B37C1E75DF0CAFED62BD2726FC1FAF8DD80360','BuildMode':'unchanged-sdk-adapters-new-abi-compatible-cores','Targets':results,'ActualADAcceptance':'pending-user-tests'}
(plugin/'hosts/compatibility.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
