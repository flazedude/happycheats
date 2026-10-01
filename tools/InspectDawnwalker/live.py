from read_game import *
PID,BASE=running_game()
m=Memory(PID)
POOL=BASE+0xa197480
blocks=struct.unpack('<8192Q',m.read(POOL+16,8192*8))
cache={}
def name(index):
    if index in cache:return cache[index]
    bi=index>>16; off=(index&65535)*2
    if bi>=len(blocks) or not blocks[bi]:return f'<invalid:{index:x}>'
    p=blocks[bi]+off
    head=m.read(p,2)
    if len(head)!=2:return '<unreadable>'
    h=struct.unpack('<H',head)[0]; n=h>>6
    b=m.read(p+2,n*(2 if h&1 else 1))
    try:s=b.decode('utf-16-le' if h&1 else 'utf-8')
    except:s=repr(b)
    cache[index]=s
    return s
def oname(addr):return name(m.u32(addr+24)) if addr else '<null>'
def oclass(addr):return oname(m.ptr(addr+16))
if __name__=='__main__':
    print('POOL',m.read(POOL,32).hex(),name(0),name(3),name(10))
    data=m.read(BASE+0x9c1b000,0x6a0000)
    for i in range(0,len(data)-32,8):
        p,pre,maxel,numel,maxchunks,numchunks=struct.unpack_from('<QQIIII',data,i)
        if not (0x10000<p<0x7fffffffffff and 10000<numel<10000000 and maxel>=numel and maxel<20000000 and 1<numchunks<500 and maxchunks>=numchunks and maxchunks<1024):continue
        ch=m.ptr(p); first=m.read(ch,48)
        print('OBJECTS candidate',hex(0x9c1b000+i),hex(p),pre,maxel,numel,maxchunks,numchunks,'chunk',hex(ch),'first',first.hex(),flush=True)
        if len(first)>=8:
            obj=struct.unpack_from('<Q',first)[0]
            print(hex(obj),m.read(obj,40).hex(),oname(obj),oclass(obj),flush=True)
