import os, time, logging, argparse
from pathlib import Path
from ctypes import POINTER, c_wchar_p, c_uint
import comtypes, comtypes.client
from comtypes import GUID, COMMETHOD, HRESULT
from PIL import Image, ImageOps, ImageFilter, ImageEnhance
root=Path(__file__).resolve().parent
class Wallpaper(comtypes.IUnknown):
    _iid_=GUID('{B92B56A9-8B55-4E14-9A89-0199BBB6F93B}')
    _methods_=[COMMETHOD([],HRESULT,'SetWallpaper',(['in'],c_wchar_p,'monitor'),(['in'],c_wchar_p,'wallpaper')), COMMETHOD([],HRESULT,'GetWallpaper',(['in'],c_wchar_p,'monitor'),(['out'],POINTER(c_wchar_p),'wallpaper')), COMMETHOD([],HRESULT,'GetMonitorDevicePathAt',(['in'],c_uint,'index'),(['out'],POINTER(c_wchar_p),'path')), COMMETHOD([],HRESULT,'GetMonitorDevicePathCount',(['out'],POINTER(c_uint),'count'))]
def render(path, destination):
    with Image.open(path) as im:
        im=ImageOps.mirror(ImageOps.exif_transpose(im).convert('RGB'))
        im.thumbnail((500,350),Image.Resampling.LANCZOS)
        im=ImageOps.fit(im,(148,72),method=Image.Resampling.LANCZOS,centering=(.5,.65))
        im=im.resize((12,6),Image.Resampling.BOX).resize((1480,720),Image.Resampling.BICUBIC)
        im=im.filter(ImageFilter.GaussianBlur(55))
        im=ImageEnhance.Color(im).enhance(1.2)
        im=ImageEnhance.Brightness(im).enhance(.58)
        temp=destination.with_suffix('.tmp')
        im.save(temp,format='JPEG',quality=85,optimize=True)
        os.replace(temp,destination)
def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=root/'wallpaper-current.jpg')
    args=parser.parse_args()
    logging.basicConfig(level=logging.INFO,format='%(asctime)s %(message)s')
    w=comtypes.client.CreateObject(GUID('{C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD}'),interface=Wallpaper)
    last=None
    while True:
        try:
            if not w.GetMonitorDevicePathCount(): raise RuntimeError('No monitor')
            path=w.GetWallpaper(w.GetMonitorDevicePathAt(0)); stat=os.stat(path)
            key=(path,stat.st_mtime_ns,stat.st_size)
            if key!=last:
                render(path,args.output); last=key; logging.info('Rendered wallpaper palette: %s',path)
        except Exception: logging.exception('Could not render wallpaper')
        time.sleep(2)
if __name__=='__main__': main()
