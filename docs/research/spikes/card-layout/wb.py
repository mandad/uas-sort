import sys,re,html,json,urllib.request,urllib.parse,hashlib,os
D=os.path.dirname(os.path.abspath(__file__))+'/wb'
UA={'User-Agent':'Mozilla/5.0'}
def get(u):
    req=urllib.request.Request(u,headers=UA)
    return urllib.request.urlopen(req,timeout=60).read().decode('utf-8','ignore')
def snap(url):
    j=json.loads(get('https://archive.org/wayback/available?url='+urllib.parse.quote(url,safe='')))
    s=j.get('archived_snapshots',{}).get('closest')
    return s and s['url']
def clean(b):
    t=re.sub(r'<br\s*/?>','\n',b); t=re.sub(r'<(script|style)[^>]*>.*?</\1>','',t,flags=re.S); t=re.sub(r'<[^>]+>','',t); t=html.unescape(t)
    return re.sub(r'\n\s*\n+','\n',t).strip()
for url in sys.argv[1:]:
    s=snap(url)
    print('#####',url,'->',s)
    if not s: continue
    fn=D+'/'+hashlib.md5(url.encode()).hexdigest()+'.html'
    try:
        h=get(s); open(fn,'w').write(h)
    except Exception as e:
        print('ERR',e); continue
    m=re.search(r'<title>(.*?)</title>',h,re.S); print('TITLE',m and clean(m.group(1)))
    bodies=re.findall(r'<article class="message-body js-selectToQuote">(.*?)</article>',h,re.S) or re.findall(r'<td class="t_f"[^>]*>(.*?)</td>',h,re.S)
    dates=re.findall(r'<time[^>]*datetime="([^"]+)"',h)
    for i,b in enumerate(bodies):
        t=clean(b)
        t=re.sub(r'(?s)said:\s*(.*?)Click to expand\.\.\.','said: [quote]',t)
        print('== post',i, (dates[i] if i<len(dates) else ''));print(t[:1500])
