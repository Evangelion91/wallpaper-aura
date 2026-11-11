import json, sys, tempfile, unittest
from pathlib import Path
from unittest.mock import patch
from wallpaper_aura.cli import main

class InitTests(unittest.TestCase):
    def test_init_creates_valid_profile_without_overwriting(self):
        with tempfile.TemporaryDirectory() as td:
            path=Path(td)/'profile.json'
            with patch.object(sys,'argv',['wallpaper-aura','init','--output',str(path)]):
                self.assertEqual(main(),0)
                data=json.loads(path.read_text(encoding='utf-8'))
                self.assertEqual(len(data['fans']),3)
                path.write_text('existing')
                self.assertEqual(main(),1)
                self.assertEqual(path.read_text(),'existing')
