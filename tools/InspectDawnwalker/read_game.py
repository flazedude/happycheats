"""Read-only discovery for the locally installed Dawnwalker build.

Never opens PROCESS_VM_WRITE/OPERATION handles or invokes in-game code.
"""
import ctypes as c
from ctypes import wintypes as w
import pathlib, struct, json, sys, hashlib, re
import pefile

GAME = pathlib.Path(r'F:\Games\steamapps\steamapps\common\The Blood of Dawnwalker\Dawnwalker\Binaries\Win64\Dawnwalker.exe')
OUT = pathlib.Path(__file__).resolve().parents[2] / 'artifacts' / 'dawnwalker'
OUT.mkdir(parents=True, exist_ok=True)
k = c.WinDLL('kernel32', use_last_error=True)
k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
k.OpenProcess.restype = w.HANDLE
k.ReadProcessMemory.argtypes = [w.HANDLE, c.c_void_p, c.c_void_p, c.c_size_t, c.POINTER(c.c_size_t)]
k.ReadProcessMemory.restype = w.BOOL
k.CloseHandle.argtypes = [w.HANDLE]
k.CreateToolhelp32Snapshot.argtypes = [w.DWORD,w.DWORD]
k.CreateToolhelp32Snapshot.restype = w.HANDLE
class ProcessEntry(c.Structure):
    _fields_=[('dwSize',w.DWORD),('cntUsage',w.DWORD),('pid',w.DWORD),('heap',c.c_size_t),('module',w.DWORD),('threads',w.DWORD),('parent',w.DWORD),('priority',w.LONG),('flags',w.DWORD),('exe',w.WCHAR*260)]
class ModuleEntry(c.Structure):
    _fields_=[('dwSize',w.DWORD),('module',w.DWORD),('pid',w.DWORD),('globalUsage',w.DWORD),('processUsage',w.DWORD),('base',c.c_void_p),('size',w.DWORD),('handle',w.HMODULE),('name',w.WCHAR*256),('path',w.WCHAR*260)]
for fn in ('Process32FirstW','Process32NextW'):
    getattr(k,fn).argtypes=[w.HANDLE,c.POINTER(ProcessEntry)]
    getattr(k,fn).restype=w.BOOL
for fn in ('Module32FirstW','Module32NextW'):
    getattr(k,fn).argtypes=[w.HANDLE,c.POINTER(ModuleEntry)]
    getattr(k,fn).restype=w.BOOL
def running_game():
    snapshot=k.CreateToolhelp32Snapshot(2,0)
    if snapshot == c.c_void_p(-1).value: raise c.WinError(c.get_last_error())
    found=[]
    try:
        entry=ProcessEntry();entry.dwSize=c.sizeof(entry)
        ok=k.Process32FirstW(snapshot,c.byref(entry))
        while ok:
            if entry.exe.lower()=='dawnwalker.exe': found.append(entry.pid)
            ok=k.Process32NextW(snapshot,c.byref(entry))
    finally:k.CloseHandle(snapshot)
    if len(found)!=1: raise RuntimeError('Exactly one Dawnwalker.exe process must already be running; none will be started.')
    pid=found[0];snapshot=k.CreateToolhelp32Snapshot(0x18,pid)
    if snapshot == c.c_void_p(-1).value: raise c.WinError(c.get_last_error())
    try:
        entry=ModuleEntry();entry.dwSize=c.sizeof(entry)
        if not k.Module32FirstW(snapshot,c.byref(entry)):raise c.WinError(c.get_last_error())
        path=pathlib.Path(entry.path)
        digest=hashlib.sha256(path.read_bytes()).hexdigest()
        if digest!='cb9b7d7bd88a6754c0a9c08318aa64d5013ddfd92d5badcae84e1b4ea980dcfc':
            raise RuntimeError('The executable differs from the inspected build. Discover RVAs again before using these helpers.')
        return pid,entry.base
    finally:k.CloseHandle(snapshot)
class MBI(c.Structure):
    _fields_ = [('BaseAddress', c.c_void_p), ('AllocationBase', c.c_void_p), ('AllocationProtect', w.DWORD), ('PartitionId', w.WORD), ('RegionSize', c.c_size_t), ('State', w.DWORD), ('Protect', w.DWORD), ('Type', w.DWORD)]
k.VirtualQueryEx.argtypes = [w.HANDLE, c.c_void_p, c.POINTER(MBI), c.c_size_t]
k.VirtualQueryEx.restype = c.c_size_t
class Memory:
    def __init__(self, pid):
        self.handle = k.OpenProcess(0x1010, False, pid)
        if not self.handle: raise c.WinError(c.get_last_error())
    def close(self):
        if self.handle:k.CloseHandle(self.handle);self.handle=None
    def __del__(self):self.close()
    def read(self, addr, size):
        b = c.create_string_buffer(size); n = c.c_size_t()
        k.ReadProcessMemory(self.handle, addr, b, size, c.byref(n))
        return b.raw[:n.value]
    def ptr(self, addr):
        b=self.read(addr,8)
        return struct.unpack('<Q',b)[0] if len(b)==8 else 0
    def u32(self, addr):
        b=self.read(addr,4)
        return struct.unpack('<I',b)[0] if len(b)==4 else 0
    def regions(self):
        a=0
        while a < 0x7FFFFFFFFFFF:
            m=MBI()
            if not k.VirtualQueryEx(self.handle,a,c.byref(m),c.sizeof(m)): break
            yield m
            a=(m.BaseAddress or 0)+m.RegionSize

def strings(data, wide=False):
    pat=rb'(?:[\x20-\x7e]\x00){5,}' if wide else rb'[\x20-\x7e]{5,}'
    for m in re.finditer(pat,data): yield m.start(),m.group().decode('utf-16-le' if wide else 'ascii')

def main():
    pe=pefile.PE(str(GAME), fast_load=True); data=GAME.read_bytes()
    sections=[dict(name=s.Name.rstrip(b'\0').decode(),rva=s.VirtualAddress,raw=s.PointerToRawData,size=s.SizeOfRawData,vsize=s.Misc_VirtualSize) for s in pe.sections]
    report=dict(exe=str(GAME),sha256=hashlib.sha256(data).hexdigest(),imagebase=pe.OPTIONAL_HEADER.ImageBase,sections=sections)
    (OUT/'binary.json').write_text(json.dumps(report,indent=2))
    for wide in [False,True]:
        with (OUT/('strings-wide.txt' if wide else 'strings.txt')).open('w',encoding='utf-8') as f:
            for off,t in strings(data,wide):
                if re.search(r'(?i)(health|stamina|teleport|godmode|cheatmanager|godmode|invincib|infinite|mapmarker|blood|rebel.*player|engineversion|\+\+ue)',t):
                    f.write(f'{pe.get_rva_from_offset(off):08x} {t}\n')
    print(json.dumps(report,indent=2))

if __name__=='__main__': main()

