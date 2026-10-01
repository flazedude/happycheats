from live import *
objects={int(o['addr'],16):o for o in json.loads((OUT/'objects.json').read_text())}
def path(p):return objects.get(p,{}).get('path',hex(p))
def fields(p):
    result=[];f=m.ptr(p+0x50);seen=set()
    while f and f not in seen:
        seen.add(f)
        b=m.read(f,0x80)
        if len(b)<0x80:break
        tp=name(m.u32(m.ptr(f+8)))
        item={'name':name(m.u32(f+0x20)),'type':tp,'offset':m.u32(f+0x44),'size':m.u32(f+0x34),'flags':hex(m.ptr(f+0x38)),'address':hex(f)}
        if tp in ('ObjectProperty','ClassProperty','StructProperty'):item['target']=path(m.ptr(f+0x70))
        if tp=='BoolProperty':item['bool_info']=m.read(f+0x70,4).hex()
        result.append(item);f=m.ptr(f+0x18)
    return result
def meta(p,o):
    r={'name':o['path'],'class':o['class_name'],'addr':hex(p),'size':m.u32(p+0x58),'fields':fields(p)}
    if o['class_name']=='Function':r.update(flags=hex(m.u32(p+0xb0)),num_params=m.read(p+0xb4,1)[0],params_size=int.from_bytes(m.read(p+0xb6,2),'little'),native_rva=hex(m.ptr(p+0xd8)-BASE))
    else:r['super']=path(m.ptr(p+0x40))
    return r
selected=[]
for p,o in objects.items():
    if o['class_name'] in ('Class','ScriptStruct','Function') and re.search(r'(/Script/Dawnwalker\.(DawnwalkerPlayer|DawnwalkerCharacterBase)|/Script/DogwoodCombat.CombatComponentBase|/Script/DogwoodStats\.(PlayerAttributeSet|CharacterBaseAttributeSet|VampireAttributeSet|BloodBarComponent)|/Script/DogwoodMap\.(MappinSystemBlueprintLibrary|Mappin|Player))',o['path']):
        selected.append(meta(p,o))
(OUT/'metadata.json').write_text(json.dumps(selected,indent=2))
for x in selected:
    if any(t in x['name'] for t in ('LockHealth','LockStamina','SetHealthPercent','SetStaminaPercent','DebugTeleportPlayer')):print(json.dumps(x,indent=2))
print('Saved',len(selected))
