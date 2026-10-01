from live import *
import time
array=BASE+0xa046fd0
chunks=m.ptr(array); count=m.u32(array+20)
objects={}
for ci in range((count+65535)//65536):
    p=m.ptr(chunks+ci*8)
    data=m.read(p,min(65536,count-ci*65536)*24)
    for j in range(0,len(data),24):
        op=struct.unpack_from('<Q',data,j)[0]
        if not op:continue
        b=m.read(op,40)
        if len(b)<40:continue
        _,flags,index,cp,ni,number,outer=struct.unpack('<QIIQIIQ',b)
        objects[op]={'name':name(ni)+(f'_{number-1}' if number else ''),'class':cp,'outer':outer,'index':index,'flags':flags}
def full(p):
    parts=[]; seen=set()
    while p and p not in seen:
        seen.add(p); o=objects.get(p)
        if not o:parts.append(hex(p)); break
        parts.append(o['name']);p=o['outer']
    return '.'.join(reversed(parts))
for p,o in objects.items():
    o['path']=full(p)
    o['class_name']=objects.get(o['class'],{}).get('name',hex(o['class']))
    o['addr']=hex(p)
(OUT/'objects.json').write_text(json.dumps(list(objects.values()),indent=2))
target=re.compile(r'(?i)(lockhealth|lockstamina|sethealthpercent|setstaminapercent|debugteleportplayer|rebelcheatmanager|attributeset|rebelplayer|infinite|godmode|mapmarker)')
with (OUT/'targets.txt').open('w',encoding='utf-8') as f:
    for p,o in objects.items():
        if target.search(o['path']):f.write(f'{p:x} {o["class_name"]} {o["path"]}\n')
print('Saved',len(objects),'objects and targets',flush=True)
