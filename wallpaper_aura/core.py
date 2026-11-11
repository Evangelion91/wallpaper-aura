"""Convert wallpaper colours into individually addressable LED colours."""
import colorsys
from PIL import Image, ImageOps

def led_color(r, g, b):
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    if s > .04 and v > .08:
        s = max(s, .55)
    return tuple(round(x * 255) for x in colorsys.hsv_to_rgb(h, min(1., s * 1.1), min(.17, v ** .72 * .17)))

def palette(image, mirror=True):
    image = ImageOps.exif_transpose(image).convert('RGB')
    if mirror: image = ImageOps.mirror(image)
    image.thumbnail((1200, 800))
    fan_image=image.resize((600,400),Image.Resampling.LANCZOS)
    points=[(.50,.40),(.72,.48),(.80,.58),(.73,.75),(.50,.90),(.27,.75),(.20,.58),(.28,.48)]
    fan=[]
    for x,y in points:
        cx,cy=round(x*599),round(y*399)
        pixel=fan_image.crop((max(0,cx-5),max(0,cy-5),min(600,cx+6),min(400,cy+6))).resize((1,1),Image.Resampling.BOX).getpixel((0,0))
        fan.append(led_color(*(c/255 for c in pixel)))
    strips=image.resize((60,80),Image.Resampling.BOX)
    out=[]
    for x in range(60):
        col=sorted((strips.getpixel((x,y)) for y in range(80)),key=lambda p:.2126*p[0]+.7152*p[1]+.0722*p[2],reverse=True)[:8]
        out.append(led_color(*(sum(p[i] for p in col)/len(col)/255 for i in range(3))))
    return out+fan

def validate_config(c):
    counts=c['header_led_counts']
    if not c.get('device_name') or not isinstance(counts,list) or not counts or any(not isinstance(x,int) or isinstance(x,bool) or x<1 or x>2048 for x in counts):
        raise ValueError('device_name and valid header_led_counts are required')
    if not isinstance(c['board_led_count'],int) or c['board_led_count']<0: raise ValueError('invalid board_led_count')
    for fan in c.get('fans',[]):
        h,start,length=fan['header'],fan['start'],fan['length']
        if not isinstance(h,int) or h<0 or h>=len(counts) or not isinstance(start,int) or start<0 or length!=8 or start+length>counts[h]: raise ValueError('each fan must fit its header and have eight addresses')
    h=c.get('gpu_header')
    if h is not None:
        if not isinstance(h,int) or h<0 or h>=len(counts): raise ValueError('invalid gpu_header')
        for a,b in c.get('gpu_segments',[]):
            if not isinstance(a,int) or not isinstance(b,int) or not 0<=a<b<=counts[h]: raise ValueError('invalid gpu segment')
    for name in ('gpu_brightness','board_brightness'):
        if not 0 <= c.get(name,.95) <= 1: raise ValueError('invalid brightness')

def normalize(color, level):
    peak=max(color)
    return tuple(round(x*255*level/peak) for x in color) if peak else (0,0,0)

def render(values,c):
    validate_config(c)
    if len(values)!=68: raise ValueError('expected 60 strip and 8 fan colours')
    strips, fan=values[:60],values[60:]
    avg=tuple(round(sum(p[i] for p in strips)/60) for i in range(3))
    board=[normalize(avg,c.get('board_brightness',.95))]*c['board_led_count']
    groups=[[strips[round(i*59/max(1,n-1))] for i in range(n)] for n in c['header_led_counts']]
    for f in c.get('fans',[]): groups[f['header']][f['start']:f['start']+8]=fan
    h=c.get('gpu_header')
    if h is not None:
        for start,end in c.get('gpu_segments',[]):
            for i in range(start,end): groups[h][i]=strips[round((i-start)*59/max(1,end-start-1))]
        groups[h]=[normalize(x,c.get('gpu_brightness',.95)) for x in groups[h]]
    return board + [x for group in groups for x in group]
