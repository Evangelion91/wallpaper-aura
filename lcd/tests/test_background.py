import importlib.util
import tempfile
import unittest
from pathlib import Path
from PIL import Image

spec=importlib.util.spec_from_file_location('lcd_wallpaper',Path(__file__).resolve().parents[1]/'lcd_wallpaper.py')
worker=importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)

class BackgroundTest(unittest.TestCase):
    def test_render_is_mirrored_bounded_and_jpeg(self):
        with tempfile.TemporaryDirectory() as tmp:
            tmp=Path(tmp); src=tmp/'source.png'; dest=tmp/'out.jpg'
            im=Image.new('RGB',(300,150),'red')
            for x in range(150,300):
                for y in range(150): im.putpixel((x,y),(0,0,255))
            im.save(src); worker.render(src,dest)
            with Image.open(dest) as out:
                self.assertEqual(out.size,(1480,720))
                self.assertEqual(out.format,'JPEG')
                a,b=out.getpixel((100,360)),out.getpixel((1380,360))
                self.assertGreater(a[2],a[0]); self.assertGreater(b[0],b[2])
            self.assertFalse(dest.with_suffix('.tmp').exists())
