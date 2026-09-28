import struct, sys, io, time
from PIL import Image
def boxes(f, start, end):
    pos=start
    while pos+8<=end:
        f.seek(pos); h=f.read(8); size,typ=struct.unpack('>I4s',h); hdr=8
        if size==1: size=struct.unpack('>Q',f.read(8))[0]; hdr=16
        elif size==0: size=end-pos
        if size<hdr: break
        yield typ.decode('latin1'), pos, pos+hdr, pos+size
        pos+=size
def find(f, start, end, path):
    for t,p,b,e in boxes(f,start,end):
        if t==path[0]:
            if len(path)==1: return (b,e)
            nb=b+4 if t=='meta' else b
            r=find(f,nb,e,path[1:])
            if r: return r
    return None
for fn in sys.argv[1:]:
    t0=time.perf_counter()
    with open(fn,'rb') as f:
        f.seek(0,2); L=f.tell()
        out={}
        for tag in ('tnal','snal','covr'):
            r=find(f,0,L,['moov','udta','meta','ilst',tag])
            if not r: out[tag]=None; continue
            b,e=r
            # ilst item contains 'data' box: 8 hdr + 8 (type,locale)
            d=find(f,b,e,['data'])
            if d:
                f.seek(d[0]+8); data=f.read(d[1]-d[0]-8)
            else:
                f.seek(b); data=f.read(e-b)
            try:
                im=Image.open(io.BytesIO(data)); out[tag]=(im.format,im.size,len(data))
            except Exception as ex: out[tag]=('?',len(data),data[:8])
        fsid=find(f,0,L,['moov','udta','fsid'])
        if fsid:
            f.seek(fsid[0]); out['fsid']=f.read(min(200,fsid[1]-fsid[0]))
    print(fn.split('/')[-1], out, '%.1f ms'%((time.perf_counter()-t0)*1000))
