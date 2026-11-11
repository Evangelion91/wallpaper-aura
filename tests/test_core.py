import unittest
from PIL import Image
from wallpaper_aura.core import palette, led_color, render, validate_config

CFG = {'device_name':'Example', 'board_led_count':4, 'header_led_counts':[24,3,20], 'fans':[{'header':0,'start':0,'length':8},{'header':0,'start':8,'length':8},{'header':0,'start':16,'length':8}], 'gpu_header':2,'gpu_segments':[[0,6],[6,20]],'gpu_brightness':.95,'board_brightness':.95,'mirror':True,'allow_resize':False}

class PatternTests(unittest.TestCase):
    def test_colour_mirroring_and_fan_pattern(self):
        image=Image.new('RGB',(120,80),'red')
        for x in range(60,120):
            for y in range(80): image.putpixel((x,y),(0,0,255))
        p=palette(image,True)
        self.assertGreater(p[0][2],p[0][0])
        self.assertGreater(p[59][0],p[59][2])
        self.assertEqual(len(p),68)
    def test_fans_share_eight_points_and_output_has_exact_length(self):
        colors=render([(i%256,0,0) for i in range(60)]+[(0,i,0) for i in range(8)],CFG)
        self.assertEqual(len(colors),4+24+3+20)
        self.assertEqual(colors[4:12],colors[12:20])
        self.assertEqual(colors[4:12],colors[20:28])
    def test_invalid_fan_rejected(self):
        cfg=dict(CFG,fans=[{'header':0,'start':23,'length':8}])
        with self.assertRaises(ValueError): validate_config(cfg)
    def test_invalid_gpu_segment_rejected(self):
        cfg=dict(CFG,gpu_segments=[[19,21]])
        with self.assertRaises(ValueError): validate_config(cfg)
    def test_saturated_colour_is_not_white(self):
        c=led_color(1,.2,.2)
        self.assertGreater(c[0],c[1]*2)
        self.assertLessEqual(max(c),44)
if __name__=='__main__': unittest.main()
