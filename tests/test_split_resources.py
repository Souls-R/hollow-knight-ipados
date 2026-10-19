"""Execute the real Foundation path/validation layer against isolated fixtures."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
temporary = Path(tempfile.mkdtemp(prefix='hk-split-tests-'))
runner = temporary / 'main.mm'
runner.write_text('''#import "NativeSplitPaths.h"
int main(int argc,char** argv) { @autoreleasepool {
 NSString* error=nil;
 BOOL ok=HKPrepareResourceOverlay([NSString stringWithUTF8String:argv[1]],
 [NSString stringWithUTF8String:argv[2]],[NSString stringWithUTF8String:argv[3]],&error);
 if(error) fprintf(stderr,"%s\\n",error.UTF8String);
 return ok ? 0 : 3;
} }
''',encoding='utf-8')
exe = temporary / 'check'
subprocess.run(['xcrun','clang++','-std=c++17','-fobjc-arc','-framework','Foundation',
                '-I' + str(root/'src/runtime'),str(root/'src/runtime/NativeSplitResources.mm'),str(runner),'-o',str(exe)],check=True)
app = temporary / 'App v1'
docs = temporary / 'Documents'
runtime = temporary / 'Library/HollowKnightRuntime-v1'
core = app / 'Data/Managed/Metadata/global-metadata.dat'
core.parent.mkdir(parents=True)
core.write_text('CODE-V1')
identity = dict(schema=1,pack_id='test-pack',compatibility='test-v1')
records = [dict(path='Data/globalgamemanagers',bytes=4),dict(path='Data/level0',bytes=5)]
contract = dict(**identity,folder='HollowKnightResources-v1',files=records)
def save(path,value):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(value),encoding='utf-8')
save(app/'HollowKnightResourceContract.json',contract)
passed = []
def check(name,success,app_path=app):
    result = subprocess.run([str(exe),str(app_path),str(docs),str(runtime)],capture_output=True,text=True)
    assert result.returncode == (0 if success else 3),(name,result.returncode,result.stderr)
    passed.append(name)
check('missing pack is blocked',False)
pack = docs / 'HollowKnightResources-v1'
save(pack/'pack.json',dict(identity,compatibility='wrong'))
save(pack/'resource-index.json',dict(identity,files=records))
check('wrong compatibility is blocked',False)
save(pack/'pack.json',identity)
(pack/'Data').mkdir()
(pack/'Data/globalgamemanagers').write_text('DATA')
check('partial pack is blocked',False)
(pack/'Data/level0').write_text('SCENE')
check('valid pack builds overlay',True)
assert (runtime/'Data/level0').read_text()=='SCENE'
assert (runtime/'Data/level0').is_symlink()
assert (runtime/'Data/Managed/Metadata/global-metadata.dat').read_text()=='CODE-V1'
check('unchanged pack can be reopened',True)
(pack/'Data/level0').write_text('WRONGSIZE')
check('truncated or wrong size is blocked',False)
(pack/'Data/level0').unlink()
(pack/'Data/level0').symlink_to(pack/'Data/globalgamemanagers')
check('symlink resource is blocked',False)
(pack/'Data/level0').unlink()
(pack/'Data/level0').write_text('SCENE')
bad = [*records,dict(path='Data/../escape',bytes=1)]
save(app/'HollowKnightResourceContract.json',dict(contract,files=bad))
save(pack/'resource-index.json',dict(identity,files=bad))
check('path traversal is blocked',False)
bad = [*records,records[0]]
save(app/'HollowKnightResourceContract.json',dict(contract,files=bad))
save(pack/'resource-index.json',dict(identity,files=bad))
check('duplicate resource route is blocked',False)
save(app/'HollowKnightResourceContract.json',contract)
save(pack/'resource-index.json',dict(identity,files=records))
updated = temporary / 'App v2 new container'
shutil.copytree(app,updated)
(updated/'Data/Managed/Metadata/global-metadata.dat').write_text('CODE-V2')
check('app update refreshes code links without importing resources',True,updated)
assert (runtime/'Data/Managed/Metadata/global-metadata.dat').read_text()=='CODE-V2'
assert (runtime/'Data/level0').read_text()=='SCENE'
report = dict(passed=passed,count=len(passed),implementation='actual Foundation layer',device_verified=False)
print(json.dumps(report,indent=2))
shutil.rmtree(temporary)
