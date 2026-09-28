import sys; sys.path.insert(0,'../grouping')
import grouping as G
from datetime import datetime, timedelta, timezone
UTC=timezone.utc
ZACHAR=(57.5368,-153.7484)
def vid(name, lat, lon):
    dc=G.drone_clock_from_name(name); return G.RawItem(name,'video',name,100_000_000,'dji',(dc+timedelta(hours=4,minutes=5)).replace(tzinfo=UTC),mvhd_utc=(dc+timedelta(hours=4)).replace(tzinfo=UTC),lat=lat,lon=lon)
def dng(name, lat, lon):
    dc=G.drone_clock_from_name(name); return G.RawItem(name,'photo',name,27_000_000,'dji',(dc+timedelta(hours=4)).replace(tzinfo=UTC),exif_dto=dc,lat=lat,lon=lon)
# Library: older folder only (watermark Sep 1)
lib=[G.LibFile('2026/2026-09/2026-09-01 Old/DJI_20260901120000_0001_D.MP4',5,datetime(2026,9,1,16,5,tzinfo=UTC))]
card=[dng('DJI_20261002140000_0010_D.DNG',*ZACHAR),   # photo-only day Oct 2
      vid('DJI_20261004140000_0020_D.MP4',*ZACHAR)]   # new video Oct 4
# RUN 1: empty ledger
vg,pg,items,L=G.run(card,lib,'Picture Offload/',ledger=())
print('run1', [(i.raw.name[-12:],i.status,i.reason) for i in items if i.kind=='photo'], 'wm',L.watermark(timedelta(hours=-4)))
# user copies ONLY the video (photo left unticked / not copied, no decision record)
v=[i for i in items if i.kind=='video'][0]
led=[G.LedgerRec(v.raw.name,v.raw.size,v.utc,'video','2026/2026-10/2026-10-04 X',*ZACHAR,'America/Anchorage')]
lib2=lib+[G.LibFile('2026/2026-10/2026-10-04 X/'+v.raw.name,v.raw.size,v.raw.mtime_utc)]
vg,pg,items,L=G.run(card,lib2,'Picture Offload/',ledger=led)
print('run2', [(i.raw.name[-12:],i.status,i.reason) for i in items if i.kind=='photo'], 'wm',L.watermark(timedelta(hours=-4)))
