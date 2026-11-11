import argparse, json, logging, os, sys, time
from pathlib import Path
from .core import palette, render, validate_config
from PIL import Image

log=logging.getLogger('wallpaper_aura')

def read_config(path):
    with open(path,encoding='utf-8-sig') as f: c=json.load(f)
    validate_config(c)
    return c

def desktop_wallpaper():
    from ctypes import POINTER, c_wchar_p, c_uint
    import comtypes, comtypes.client
    from comtypes import GUID, COMMETHOD, HRESULT
    class Wallpaper(comtypes.IUnknown):
        _iid_=GUID('{B92B56A9-8B55-4E14-9A89-0199BBB6F93B}')
        _methods_=[COMMETHOD([],HRESULT,'SetWallpaper',(['in'],c_wchar_p,'monitor'),(['in'],c_wchar_p,'wallpaper')),COMMETHOD([],HRESULT,'GetWallpaper',(['in'],c_wchar_p,'monitor'),(['out'],POINTER(c_wchar_p),'wallpaper')),COMMETHOD([],HRESULT,'GetMonitorDevicePathAt',(['in'],c_uint,'index'),(['out'],POINTER(c_wchar_p),'path')),COMMETHOD([],HRESULT,'GetMonitorDevicePathCount',(['out'],POINTER(c_uint),'count'))]
    w=comtypes.client.CreateObject(GUID('{C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD}'),interface=Wallpaper)
    if not w.GetMonitorDevicePathCount(): raise RuntimeError('No monitors found')
    return w.GetWallpaper(w.GetMonitorDevicePathAt(0))

def connect(c):
    from openrgb import OpenRGBClient
    client=OpenRGBClient(name='WallpaperAura', protocol_version=4)
    matches=[x for x in client.devices if x.name==c['device_name']]
    if len(matches)!=1:
        client.comms.stop_connection(); raise RuntimeError(f"Expected exactly one device named {c['device_name']!r}; use list to inspect")
    d=matches[0]
    sizes=[c['board_led_count']]+c['header_led_counts']
    if len(d.zones)!=len(sizes):
        client.comms.stop_connection(); raise RuntimeError(f'Expected {len(sizes)} zones, got {len(d.zones)}')
    if len(d.zones[0].leds)!=sizes[0]:
        client.comms.stop_connection(); raise RuntimeError('Board LED count does not match the configuration')
    if any(len(z.leds)!=n for z,n in zip(d.zones[1:],sizes[1:])):
        if not c.get('allow_resize',False):
            client.comms.stop_connection(); raise RuntimeError('Header LED counts differ; check wiring and set allow_resize to true if these counts are correct')
        for z,n in zip(d.zones[1:],sizes[1:]):
            if len(z.leds)!=n: z.resize(n)
        client.comms.stop_connection(); time.sleep(.5)
        client=OpenRGBClient(name='WallpaperAura',protocol_version=4)
        matches=[x for x in client.devices if x.name==c['device_name']]
        if len(matches)!=1: raise RuntimeError('Device disappeared after resizing')
        d=matches[0]
    if [len(z.leds) for z in d.zones]!=sizes or len(d.leds)!=sum(sizes):
        client.comms.stop_connection(); raise RuntimeError('Device zones do not match configured LED counts')
    d.set_mode('Direct')
    return client,d

def run(c):
    from openrgb.utils import RGBColor
    client=None; d=None; current=None; last=None; last_sent=0; failures=0
    try:
        while True:
            try:
                if d is None:
                    client,d=connect(c); last=None; failures=0; log.info('Connected to %s',d.name)
                path=desktop_wallpaper(); stat=os.stat(path); key=(path,stat.st_mtime_ns,stat.st_size)
                if key!=last:
                    with Image.open(path) as im: target=palette(im,c.get('mirror',True))
                    start=current or target
                    for step in range(1,9):
                        t=step/8
                        current=[tuple(round(a+(b-a)*t) for a,b in zip(x,y)) for x,y in zip(start,target)]
                        d.set_colors([RGBColor(*v) for v in render(current,c)],fast=True); time.sleep(.15)
                    current=target; last=key; last_sent=time.monotonic(); log.info('Wallpaper applied: %s',path)
                elif time.monotonic()-last_sent>10:
                    d.set_colors([RGBColor(*v) for v in render(current,c)],fast=True); last_sent=time.monotonic()
                time.sleep(2)
            except Exception:
                log.exception('Controller error')
                if client:
                    try: client.comms.stop_connection()
                    except Exception: pass
                d=None; client=None; failures+=1
                if failures>=3: raise
                time.sleep(3)
    finally:
        if client:
            try: client.comms.stop_connection()
            except Exception: pass

def main():
    parser=argparse.ArgumentParser(prog='wallpaper-aura')
    sub=parser.add_subparsers(dest='command',required=True)
    sub.add_parser('list',help='List OpenRGB devices and zones without changing them')
    p=sub.add_parser('init',help='Create an example EX5 Pro profile'); p.add_argument('--output',required=True)
    p=sub.add_parser('run',help='Synchronize lighting with desktop wallpaper'); p.add_argument('--config',required=True)
    p=sub.add_parser('preview',help='Print calculated RGB values without touching hardware'); p.add_argument('image'); p.add_argument('--config',required=True)
    args=parser.parse_args(); logging.basicConfig(level=logging.INFO,format='%(asctime)s %(levelname)s %(message)s')
    try:
        if args.command=='init':
            from importlib.resources import files
            template=files('wallpaper_aura').joinpath('ex5-pro-example.json').read_text(encoding='utf-8-sig')
            with open(args.output,'x',encoding='utf-8') as f: f.write(template)
            print(f'Created {args.output}; review the device and wiring before running.')
        elif args.command=='list':
            from openrgb import OpenRGBClient
            client=OpenRGBClient(name='WallpaperAura-inspect',protocol_version=4)
            try:
                for d in client.devices:
                    print(d.name)
                    for i,z in enumerate(d.zones): print(f'  zone {i}: {z.name}: {len(z.leds)} LEDs')
            finally: client.comms.stop_connection()
        else:
            c=read_config(args.config)
            if args.command=='preview':
                with Image.open(args.image) as im: colors=render(palette(im,c.get('mirror',True)),c)
                print(json.dumps(colors))
            else: run(c)
    except KeyboardInterrupt: return 0
    except Exception as e: log.error('%s',e); return 1
    return 0

if __name__=='__main__': sys.exit(main())
