"""Prepare an external resource pack directly, without building a full IPA first."""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
CORE = {'RuntimeInitializeOnLoads.json', 'ScriptingAssemblies.json', 'boot.config', 'app.info',
        'Resources/unity default resources'}

def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')

def is_core(relative):
    return relative in CORE or relative.startswith('Managed/')

def clone_file(source, target):
    # APFS clones share physical storage until either file is changed.
    if sys.platform == 'darwin':
        clone = ctypes.CDLL(None, use_errno=True).clonefile
        clone.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_int]
        if clone(os.fsencode(source), os.fsencode(target), 0) == 0:
            shutil.copystat(source, target)
            return target
    return shutil.copy2(source, target)

def resource_plan(data):
    routes = []
    for source in sorted(data.rglob('*')):
        if not source.is_file(): continue
        relative = source.relative_to(data).as_posix()
        if is_core(relative): continue
        if relative.startswith('StreamingAssets/'): relative = 'Raw/'+relative[len('StreamingAssets/'):]
        routes.append((source, 'Data/'+relative, source.stat().st_size))
    routes.sort(key=lambda row: row[1])
    names = [row[1] for row in routes]
    if len(names) != len(set(names)): raise ValueError('Duplicate resource destination')
    records = [dict(path=relative, bytes=size) for _, relative, size in routes]
    # This is a small manifest identity, not a hash of the bulk game files.
    compatibility = hashlib.sha256(json.dumps(dict(game='1.5.12620', unity='6000.0.61f1', policy=1, files=records), sort_keys=True).encode()).hexdigest()
    identity = dict(schema=1, pack_id='hollow-knight-ios-resources-v1', compatibility=compatibility)
    return routes, records, identity

def prepare_direct(work, resources, pack_output, manifest):
    if pack_output.exists(): raise ValueError('Resource output already exists')
    if not (resources/'resources.json').is_file(): raise ValueError('Convert resources first')
    data = resources/'Data'; project = work/'project'; pack = pack_output/'HollowKnightResources-v1'
    routes, records, identity = resource_plan(data)
    if not records: raise ValueError('Empty resource dataset')
    for source, relative, _ in routes:
        target = pack/relative; target.parent.mkdir(parents=True, exist_ok=True)
        clone_file(source, target)
    write(pack/'pack.json', identity)
    write(pack/'resource-index.json', dict(**identity, files=records))
    write(project/'HollowKnightResourceContract.json', dict(**identity, folder=pack.name, files=records))
    core = project/'Data'
    for relative in CORE:
        source = data/relative
        if source.is_file():
            target = core/relative; target.parent.mkdir(parents=True, exist_ok=True)
            clone_file(source, target)
    shutil.copytree(work/'metadata', core/'Managed', dirs_exist_ok=True)
    game = Path(manifest['game_data'])
    assemblies = json.loads((game/'ScriptingAssemblies.json').read_text())
    index = assemblies['names'].index('Assembly-CSharp.dll')
    assemblies['names'].append('NativeCompatibility.dll'); assemblies['types'].append(assemblies['types'][index])
    write(core/'ScriptingAssemblies.json', assemblies)
    callbacks = json.loads((game/'RuntimeInitializeOnLoads.json').read_text())
    callbacks['root'] += [dict(assemblyName='NativeCompatibility', nameSpace='', className='HollowKnightBootstrap', methodName=name, loadTypes=kind, isUnityClass=False) for name, kind in [('Install', 3), ('Boot', 0)]]
    write(core/'RuntimeInitializeOnLoads.json', callbacks)
    default = core/'Resources/unity default resources'; default.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(Path(manifest['ios_support'])/'Data/Resources/unity default resources', default)
    (core/'boot.config').write_text('gfx-threading-mode=4\nwait-for-native-debugger=0\nhdr-display-enabled=0\nscripting-runtime-version=latest\n', encoding='utf-8')
    shutil.copyfile(ROOT/'src/runtime/SplitMain.mm', project/'MainApp/main.mm')
    filesystem = project/'Classes/Unity/Filesystem.mm'; text = filesystem.read_text(encoding='utf-8')
    start = text.index('extern "C" const char* UnityDataBundleDir()')
    end = text.index('#define RETURN_SPECIAL_DIR', start)
    replacement = 'extern "C" const char* UnityDataBundleDir()\n{\n    static const char* dir = NULL;\n    if (dir == NULL) dir = AllocCString(HKRuntimeRoot());\n    return dir;\n}\n\n'
    filesystem.write_text('#import "NativeSplitPaths.h"\n'+text[:start]+replacement+text[end:], encoding='utf-8')
    manifest.update(split_resources=True, resource_contract=identity, resource_folder=str(pack))
    write(pack_output/'resource-pack-report.json', dict(**identity, files=len(records), bytes=sum(r['bytes'] for r in records)))
    print(f'Resource folder ready: {len(records)} files', flush=True)

def resource_zip(args):
    resources = args.resources.expanduser().resolve(strict=True)
    pack = resources if resources.name == 'HollowKnightResources-v1' else resources/'HollowKnightResources-v1'
    assert (pack/'pack.json').is_file() and (pack/'resource-index.json').is_file()
    output = args.output.expanduser().resolve()
    if output.exists() or output.is_relative_to(pack): raise ValueError('Use a new ZIP path outside the resource folder')
    output.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(['/usr/bin/zip', '-qry', str(output), pack.name], cwd=pack.parent, check=True)
    with zipfile.ZipFile(output) as archive:
        names = archive.namelist()
        assert pack.name+'/pack.json' in names
        assert not any('/Managed/' in name for name in names)
        for record in json.loads((pack/'resource-index.json').read_text())['files']:
            assert archive.getinfo(pack.name+'/'+record['path']).file_size == record['bytes']
    print('RESOURCES_ZIP='+str(output), flush=True)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    p = commands.add_parser('zip'); p.add_argument('--resources', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    resource_zip(parser.parse_args())
