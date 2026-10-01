"""Read-only PE/Unreal registration inspection. Never opens or launches a process."""
from pathlib import Path
import argparse, hashlib, json, re, struct
import pefile

DEFAULT = Path(r"F:\Games\steamapps\steamapps\common\Hotel Architect\HotelArchitect\Binaries\Win64\HotelArchitect-Win64-Shipping.exe")
EXPECTED_SHA256 = 'e0b6e24e2d0d3ca466b9dd08ed83bc989594b6b17c31cdc3b63f543355621ae8'
parser = argparse.ArgumentParser()
parser.add_argument("--exe", type=Path, default=DEFAULT)
parser.add_argument("--strings", nargs="*", default=[])
parser.add_argument("--near", type=lambda x: int(x,0))
parser.add_argument("--length", type=lambda x: int(x,0), default=0x600)
parser.add_argument("--refs", nargs="*", default=[])
parser.add_argument("--dump", action="store_true")
parser.add_argument("--pointers", nargs="*", type=lambda x: int(x,0), default=[])
parser.add_argument("--disasm", type=lambda x: int(x,0))
parser.add_argument("--props", nargs="*", type=lambda x: int(x,0), default=[])
parser.add_argument("--qwords", type=lambda x: int(x,0))
parser.add_argument("--fingerprint", action="store_true")
parser.add_argument("--mood-writes", action="store_true")
parser.add_argument("--clock-writes", action="store_true")
parser.add_argument("--riprefs", nargs="*", type=lambda x:int(x,0), default=[])
parser.add_argument("--verify", action="store_true")
args = parser.parse_args()
data = args.exe.read_bytes()
if args.dump:
    lines=[]
    region=data[0x7f80000:0x8100000]
    lines += [f'{0x7f80000+m.start():x} {m.group().decode()}' for m in re.finditer(rb'[A-Za-z][A-Za-z0-9_/:.]{5,}',region)]
    lines += [f'{m.start():x} {m.group().decode("utf-16le")}' for m in re.finditer(rb'(?:[A-Za-z_/:.][\x00]){8,}',data) if b'H\x00a\x00' in m.group() or b'A\x00g\x00i\x00n\x00g' in m.group()]
    Path('artifacts/hotel-architect/ha-strings.txt').write_text('\n'.join(lines))
pe = pefile.PE(data=data, fast_load=True)
base = pe.OPTIONAL_HEADER.ImageBase
def va(off): return base + pe.get_rva_from_offset(off)
def off(addr): return pe.get_offset_from_rva(addr-base)
if args.fingerprint:
    if hashlib.sha256(data).hexdigest()!=EXPECTED_SHA256:
        raise SystemExit('Unsupported executable: do not publish pinned field evidence for a different SHA-256.')
    report=dict(executable=str(args.exe),sha256=hashlib.sha256(data).hexdigest(),file_size=len(data),timestamp=pe.FILE_HEADER.TimeDateStamp,size_of_image=pe.OPTIONAL_HEADER.SizeOfImage,image_base=hex(base),steam_app_id=1602000,steam_build_id=25340122,engine='5.7 CL-51494982',live_tested=False)
    def sentinel(name,rva,length,**extra):
        p=off(base+rva)
        return dict(name=name,rva=hex(rva),file_offset=hex(p),hex=data[p:p+length].hex(),**extra)
    report['sentinels']=[
        sentinel('GetLevelAndExperience',0x48bbc40,40,fields={'CurrentXP':{'offset':0x768,'type':'IntProperty'},'Level':{'offset':0x76c,'type':'IntProperty'}},owner='/Script/HotelArchitect.HaStaffCharacter'),
        sentinel('NativeSetTimeOfDayHours',0x4a143e0,44,fields={'HourlyAccumulatorSeconds':{'offset':0x494,'type':'FloatProperty'},'DayTimeSeconds':{'offset':0x498,'type':'FloatProperty'},'PreviousGameHoursTime':{'offset':0x4a0,'type':'FloatProperty'}},owner='/Script/HotelArchitect.HaGameState',hours_to_seconds=3600),
        sentinel('GetActiveDifficultySettings',0x4a11b00,8,active_settings_rva='0x9323f20'),
        sentinel('bEnableAgingBoolSetter',0x4789490,5,struct_offset=1),
        sentinel('AgingGateBeforeDailyRetirementDecrement',0x4a13165,16,target_rva='0x9323f21'),
        sentinel('DailyRetirementCounterDecrement',0x4a1324d,33,field_offset='0x770'),
    ]
    aging_section=next(s for s in pe.sections if s.VirtualAddress<=0x9323f21<s.VirtualAddress+s.Misc_VirtualSize)
    report['aging']={'enabled_byte_rva':'0x9323f21','size':1,'meaning':{'0':'disabled','1':'enabled'},'section':aging_section.Name.rstrip(b'\0').decode(),'section_rva':hex(aging_section.VirtualAddress),'section_virtual_size':hex(aging_section.Misc_VirtualSize),'section_characteristics':hex(aging_section.Characteristics),'writes_require_original_capture':True,'getter_returns_copy_to_blueprints':True}
    Path('artifacts/hotel-architect/verified-build.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report,indent=2))
if args.mood_writes:
    for m in re.finditer(rb'\xf3\x0f\x11[\x80-\xbf]\xf0\x05\x00\x00',data):print(hex(va(m.start())),hex(m.start()))
if args.clock_writes:
    for m in re.finditer(rb'(?:\xf3\x0f\x11[\x80-\xbf]|\xc7[\x80-\xbf])\x9c\x04\x00\x00',data):print(hex(va(m.start())),hex(m.start()))
if args.riprefs:
    text=next(s for s in pe.sections if s.Name.startswith(b'.text'))
    start=text.PointerToRawData;end=start+text.SizeOfRawData
    for m in re.finditer(rb'(?:[\x48\x4c][\x8d\x8b\x89][\x05\x0d\x15\x1d\x25\x2d\x35\x3d]|\x80[\x3d\x25\x0d]|\x0f\xb6[\x05\x0d\x15\x1d\x25\x2d\x35\x3d])....',data[start:end],re.S):
        p=start+m.start(); raw=m.group();target=va(p)+len(raw)+(1 if raw[0]==0x80 else 0)+struct.unpack_from('<i',raw,len(raw)-4)[0]
        if target in args.riprefs: print(hex(va(p)),hex(target),raw.hex(' '))
def cstring(addr):
    try:
        p=off(addr); return data[p:data.index(b'\0',p)].decode('ascii')
    except Exception: return None
if args.qwords:
    start=off(args.qwords) if args.qwords>base else args.qwords
    for at in range(start,start+args.length,8):
        value=struct.unpack_from('<Q',data,at)[0]
        s=cstring(value)
        if s is None:
            try:
                p=off(value);end=p
                while data[end:end+2]!=b'\0\0' and end-p<128:end+=2
                s=data[p:end].decode('utf-16le')
                if not re.fullmatch('[A-Za-z_/:.0-9]{3,}',s):s=None
            except Exception:pass
        print(hex(va(at)),hex(value),repr(s))
if args.disasm:
    import capstone
    md=capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    start=off(args.disasm) if args.disasm>base else args.disasm
    for i in md.disasm(data[start:start+args.length],va(start)): print(hex(i.address),i.mnemonic,i.op_str)
for address in args.pointers:
    ptr=address if address>base else va(address)
    for r in re.finditer(re.escape(struct.pack('<Q',ptr)),data):
        q=r.start(); print('PTR',hex(q),hex(va(q)),data[max(0,q-32):q+80].hex(' '))
def property_at(p):
    try:
        name=cstring(struct.unpack_from('<Q',data,p)[0])
        flags,kind,objflags=struct.unpack_from('<QII',data,p+16)
        if name and re.fullmatch('[A-Za-z_][A-Za-z0-9_]*',name) and objflags==0x45 and 0<kind<100:
            return dict(name=name,kind=kind,offset=hex(struct.unpack_from('<i',data,p+50)[0]),flags=hex(flags),descriptor=hex(p))
    except Exception: pass
if args.verify:
    checks=[('SHA-256',hashlib.sha256(data).hexdigest()==EXPECTED_SHA256),('64-bit PE',pe.FILE_HEADER.Machine==0x8664)]
    expected=[
        (0x8009fa0,'DayNumber',4,0x480),(0x8009fd8,'Hour',3,0x488),(0x800a018,'Minute',3,0x48c),
        (0x800a050,'GameHoursTime',10,0x49c),(0x800a0d0,'Money',4,0x4a8),(0x800a1b0,'Stars',3,0x4d0),
        (0x7fa07d8,'DaysPassed',3,0x30),(0x7fbb2a0,'RestroomNeed',10,0x5ec),(0x7fbb2d8,'Mood',10,0x5f0),
        (0x7fbb310,'Health',10,0x5f4),(0x80b82a0,'DaysUntilRetirement',3,0x770),(0x80b83e0,'Energy',10,0x7a4),
        (0x8013970,'RoomValueMultiplier',10,0x3d4),(0x8013df0,'Cleanliness',10,0x4c4),
        (0x8013ea0,'AttractivenessValue',3,0x4d4),(0x8013f50,'NegativeAttractivenessValue',3,0x4e0),
        (0x8014240,'Condition',10,0x500),(0x80652a0,'SelectedCharacter',14,0x764),
        (0x80651e0,'SelectedGridItem',14,0x748),(0x80b41f0,'HaGameState',18,0x410),
    ]
    for p,name,kind,offset in expected:
        actual=property_at(p)
        checks.append((name,bool(actual and actual['name']==name and actual['kind']==kind and actual['offset']==hex(offset))))
    expected_code={0x48bbc40:'4885c9741b8b41080fbae01e72128b816c07000089028b8168070000418900c333c08902418900c3',0x4a143e0:'0f28c1f30f11899c040000f30f5905a5ca6e03f30f1189a0040000f30f118194040000f30f118198040000c3',0x4a11b00:'488d0519249104c3',0x4789490:'c6410101c3',0x4a13165:'803db50d910400488bf80f8402010000'}
    for rva,raw in expected_code.items():
        value=bytes.fromhex(raw);p=off(base+rva)
        checks.append((f'Code sentinel {rva:x}',data[p:p+len(value)]==value))
    result={'static_contract_valid':all(ok for _,ok in checks),'live_tested':False,'checks':[{'name':name,'valid':ok} for name,ok in checks]}
    Path('artifacts/hotel-architect/static-contract.json').write_text(json.dumps(result,indent=2))
    print(json.dumps(result,indent=2))
    if not result['static_contract_valid']: raise SystemExit(1)
for p in args.props:
    p=off(p) if p>base else p
    for q in range(max(p-args.length,0),p+args.length,8):
        try: result=property_at(off(struct.unpack_from('<Q',data,q)[0]))
        except Exception: result=None
        if result: print(hex(q),json.dumps(result))
if args.near is not None:
    for m in re.finditer(rb'[\x20-\x7e]{3,}',data[max(0,args.near-args.length):args.near+args.length]):
        print(hex(max(0,args.near-args.length)+m.start()),m.group().decode())
for target in args.strings+args.refs:
    for m in re.finditer(re.escape(target.encode()+b'\0'),data):
        p=m.start()
        if p and data[p-1]!=0: continue
        print('STRING',target,hex(p),hex(va(p)))
        if target not in args.refs: continue
        needle=struct.pack('<Q',va(p))
        for r in re.finditer(re.escape(needle),data):
            q=r.start(); print('REF',hex(q),hex(va(q)),data[max(0,q-16):q+80].hex(' '))
