"""Link/package a small split-resource IPA without repeating IL2CPP conversion."""
import argparse
import json
from pathlib import Path
import plistlib
import shutil
import time
import zipfile
from native_build import ROOT, run, write

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--work',type=Path,required=True)
parser.add_argument('--version')
parser.add_argument('--build-number')
args = parser.parse_args()
started = time.monotonic()
work = args.work.resolve(strict=True)
manifest = json.loads((work/'build.json').read_text())
assert manifest['split_resources']
assert not manifest.get('runtime_compile_required'), 'Run build.py native after refresh-runtime'
project, logs = work/'project',work/'logs'
def sync(source,target):
    if not target.is_file() or source.read_bytes()!=target.read_bytes():
        shutil.copyfile(source,target)
for source in (ROOT/'src/runtime').iterdir():
    if source.suffix in ('.mm','.h'):
        sync(source,project/'Libraries'/source.name)
sync(ROOT/'src/runtime/SplitMain.mm',project/'MainApp/main.mm')
info_path = project/'Info.plist'
info = plistlib.loads(info_path.read_bytes())
if args.version:
    manifest['version'] = args.version
if args.build_number:
    manifest['build'] = args.build_number
info.update(CFBundleShortVersionString=manifest['version'],CFBundleVersion=manifest['build'])
info_path.write_bytes(plistlib.dumps(info))
run(['ruby',ROOT/'scripts/configure_xcode.rb',project,manifest['bundle_id']],logs/'configure-xcode.log')
run(['xcodebuild','-project',project/'Unity-iPhone.xcodeproj','-scheme','Unity-iPhone',
     '-configuration','Release','-sdk','iphoneos','-destination','generic/platform=iOS',
     '-derivedDataPath',work/'DerivedData','CODE_SIGNING_ALLOWED=NO','CODE_SIGNING_REQUIRED=NO','CODE_SIGN_IDENTITY=','build'],logs/'xcodebuild.log',work)
app = work/'DerivedData/Build/Products/Release-iphoneos/Unity-iPhone.app'
run(['xcrun','lipo','-info',app/'Frameworks/UnityFramework.framework/UnityFramework'],logs/'architecture.log')
assert 'arm64' in (logs/'architecture.log').read_text()
assert not list((app/'Data').glob('level*'))
assert not list((app/'Data').glob('*.assets'))
assert not (app/'Data/Raw').exists()
assert (app/'Data/Managed/Metadata/global-metadata.dat').is_file()
assert (app/'HollowKnightResourceContract.json').is_file()
package = work/'package/Payload/HollowKnight.app'
if package.exists():
    assert package.resolve().is_relative_to(work/'package') and not package.is_symlink()
    shutil.rmtree(package)
package.parent.mkdir(parents=True,exist_ok=True)
run(['ditto',app,package],logs/'package-copy.log')
destination = work/'output'
destination.mkdir(exist_ok=True)
ipa = destination/f'HollowKnight-{manifest["version"]}-split-unsigned.ipa'
temporary = destination/'program.partial.zip'
if temporary.exists():
    temporary.unlink()
run(['/usr/bin/zip','-qry',temporary,'Payload'],logs/'package.log',work/'package')
with zipfile.ZipFile(temporary) as archive:
    assert archive.testzip() is None
    prefix = 'Payload/HollowKnight.app/'
    names = archive.namelist()
    assert prefix+'HollowKnightResourceContract.json' in names
    assert not any(name.startswith(prefix+'Data/level') or name.startswith(prefix+'Data/Raw/') or name.endswith('.resS') for name in names)
    info = plistlib.loads(archive.read(prefix+'Info.plist'))
    assert info['CFBundleIdentifier']==manifest['bundle_id']
    assert info['CFBundleShortVersionString']==manifest['version']
    contract = json.loads(archive.read(prefix+'HollowKnightResourceContract.json'))
    assert contract['compatibility']==manifest['resource_contract']['compatibility']
temporary.replace(ipa)
manifest.update(native_built=True,ipa=str(ipa),ipa_bytes=ipa.stat().st_size,
                last_build_seconds=round(time.monotonic()-started,2),device_verified=False)
write(work/'build.json',manifest)
print('SPLIT_IPA='+str(ipa),flush=True)
