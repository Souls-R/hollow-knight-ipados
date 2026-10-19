"""Exercise the code/resource boundary with tiny fixtures, without game files."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT/'scripts'))
from split_package import resource_plan, prepare_direct

class ResourcePlanTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.data = self.root/'converted/Data'
        self.data.mkdir(parents=True)

    def tearDown(self):
        self.temp.cleanup()

    def put(self, name, value):
        path = self.data/name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(value)
        return path

    def test_assets_go_external_and_code_metadata_stays_in_program(self):
        self.put('Managed/Metadata/global-metadata.dat', b'META')
        self.put('Resources/unity default resources', b'DESKTOP')
        self.put('RuntimeInitializeOnLoads.json', b'{}')
        self.put('globalgamemanagers', b'GGM')
        self.put('level0', b'SCENE')
        movie = self.put('StreamingAssets/Intro.mp4', b'MOVIE')
        _, records, identity = resource_plan(self.data)
        self.assertEqual([r['path'] for r in records], ['Data/Raw/Intro.mp4', 'Data/globalgamemanagers', 'Data/level0'])
        self.assertEqual(identity['pack_id'], 'hollow-knight-ios-resources-v1')
        self.assertEqual(movie.read_bytes(), b'MOVIE')

    def test_duplicate_streaming_destination_is_rejected(self):
        self.put('StreamingAssets/Intro.mp4', b'A')
        self.put('Raw/Intro.mp4', b'B')
        with self.assertRaises(ValueError): resource_plan(self.data)

    def test_identity_matches_existing_ios_data_layout(self):
        source = self.put('StreamingAssets/Intro.mp4', b'MOVIE')
        self.put('level0', b'SCENE')
        _, _, before = resource_plan(self.data)
        raw = self.data/'Raw/Intro.mp4'; raw.parent.mkdir()
        source.rename(raw)
        _, _, after = resource_plan(self.data)
        self.assertEqual(before, after)

    def test_direct_prepare_pairs_code_metadata_and_external_pack(self):
        self.put('globalgamemanagers', b'GGM'); self.put('level0', b'SCENE')
        self.put('StreamingAssets/Intro.mp4', b'MOVIE')
        self.put('Resources/unity default resources', b'DESKTOP')
        (self.data.parent/'resources.json').write_text('{}')
        work = self.root/'native'; project = work/'project'
        metadata = work/'metadata/Metadata/global-metadata.dat'
        metadata.parent.mkdir(parents=True); metadata.write_bytes(b'NEW-AOT-METADATA')
        for name in ['MainApp', 'Classes/Unity']: (project/name).mkdir(parents=True)
        (project/'Classes/Unity/Filesystem.mm').write_text('extern "C" const char* UnityDataBundleDir() { return "old"; }\n#define RETURN_SPECIAL_DIR 1\n')
        game = self.root/'game'; game.mkdir()
        (game/'ScriptingAssemblies.json').write_text(json.dumps(dict(names=['Assembly-CSharp.dll'], types=[16])))
        (game/'RuntimeInitializeOnLoads.json').write_text(json.dumps(dict(root=[])))
        ios = self.root/'ios'; default = ios/'Data/Resources/unity default resources'
        default.parent.mkdir(parents=True); default.write_bytes(b'IOS')
        manifest = dict(game_data=str(game), ios_support=str(ios))
        output = self.root/'pack'
        prepare_direct(work, self.data.parent, output, manifest)
        self.assertEqual((project/'Data/Managed/Metadata/global-metadata.dat').read_bytes(), b'NEW-AOT-METADATA')
        self.assertEqual((project/'Data/Resources/unity default resources').read_bytes(), b'IOS')
        self.assertFalse((project/'Data/level0').exists())
        self.assertEqual((output/'HollowKnightResources-v1/Data/Raw/Intro.mp4').read_bytes(), b'MOVIE')
        self.assertEqual((self.data/'Resources/unity default resources').read_bytes(), b'DESKTOP')
        callbacks = json.loads((project/'Data/RuntimeInitializeOnLoads.json').read_text())['root']
        self.assertEqual([x['methodName'] for x in callbacks], ['Install', 'Boot'])
        contract = json.loads((project/'HollowKnightResourceContract.json').read_text())
        self.assertEqual(contract['compatibility'], manifest['resource_contract']['compatibility'])

if __name__ == '__main__':
    unittest.main()
