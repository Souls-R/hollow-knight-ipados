import io
import json
from pathlib import Path
import re
import tempfile
import unittest
import zipfile
from PIL import Image
from scripts.skin_package import package, SHEETS

def png(size):
    buffer=io.BytesIO()
    Image.new('RGBA',size,(10,20,30,255)).save(buffer,format='PNG')
    return buffer.getvalue()

class SkinPackageTests(unittest.TestCase):
    def test_author_archive_is_flattened_and_normalized(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); source=root/'author.zip'; output=root/'skin.zip'
            with zipfile.ZipFile(source,'w') as z:
                z.writestr('Author Skin/KNIGHT.PNG',png((4096,4096)))
                z.writestr('Author Skin/HuD.png',png((2048,2048)))
                z.writestr('Author Skin/UnknownSheet.png',png((16,16)))
            result=package(source,output,'test-skin','测试皮肤','Artist','https://example.org/skin')
            self.assertEqual(result['sheets'],['HUD','Knight'])
            self.assertEqual(result['unsupported'],['UnknownSheet.png'])
            with zipfile.ZipFile(output) as z:
                self.assertIn('HollowKnightSkins/test-skin/HUD.png',z.namelist())
                metadata=json.loads(z.read('HollowKnightSkins/test-skin/metadata.json'))
                self.assertEqual(metadata['author'],'Artist')
                self.assertFalse(any('Wings' in n for n in z.namelist()))

    def test_rejects_unsafe_duplicate_or_incompatible_sheet_before_output(self):
        cases=[{'../Knight.png':b''}, {'A/Knight.png':b'', 'B/knight.png':b''}, {'Knight.png':png((32,32))}]
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            for index,files in enumerate(cases):
                source=root/f'{index}.zip'; output=root/f'{index}-out.zip'
                with zipfile.ZipFile(source,'w') as z:
                    for name,data in files.items(): z.writestr(name,data or png((4096,4096)))
                with self.assertRaises(ValueError): package(source,output,'skin','Skin')
                self.assertFalse(output.exists())

    def test_runtime_and_packager_use_identical_sheet_contract(self):
        source=(Path(__file__).resolve().parents[1]/'src/runtime/NativeSkinCatalog.cs').read_text(encoding='utf-8')
        actual={name:(int(w),int(h)) for name,w,h in re.findall(r'new Sheet\("([^"]+)", "[^"]*", (\d+), (\d+)\)',source)}
        self.assertEqual(actual,SHEETS)

if __name__=='__main__': unittest.main()
