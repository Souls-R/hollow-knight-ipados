"""Bundle an already-built program and its compatible resource pack in one IPA."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import zipfile

from split_package import clone_file


def bundle(work, output):
    work=Path(work).expanduser().resolve(strict=True)
    output=Path(output).expanduser().resolve()
    manifest=json.loads((work/'build.json').read_text())
    if not manifest.get('native_built') or manifest.get('runtime_compile_required'):
        raise ValueError('Build the matching native program first')
    if not manifest.get('embedded_resource_support'):
        raise ValueError('Relink with the bundled-resource launcher before packaging')
    pack=Path(manifest['resource_folder']).resolve(strict=True)
    source=work/'DerivedData/Build/Products/Release-iphoneos/Unity-iPhone.app'
    contract=json.loads((source/'HollowKnightResourceContract.json').read_text())
    identity=json.loads((pack/'pack.json').read_text())
    index=json.loads((pack/'resource-index.json').read_text())
    if identity.get('compatibility')!=contract['compatibility'] or index.get('files')!=contract['files']:
        raise ValueError('Program and resource pack are incompatible')
    stage=work/'bundled-package'
    if stage.exists() or output.exists():
        raise ValueError('Use fresh packaging/output paths; preserve the previous IPA')
    files=[]
    seen=set()
    for record in contract['files']:
        name=record['path']; parts=name.split('/')
        if not name.startswith('Data/') or '\\' in name or any(p in ('','.','..') for p in parts) or name in seen:
            raise ValueError('Invalid or duplicate resource path')
        seen.add(name)
        item=pack/name
        if any((pack/Path(*parts[:i])).is_symlink() for i in range(1,len(parts)+1)):
            raise ValueError('Resource symlinks are not supported')
        if not item.is_file() or item.stat().st_size!=record['bytes']:
            raise ValueError('Incomplete resource pack: '+name)
        files.append((item,name))
    app=stage/'Payload/HollowKnight.app'
    app.parent.mkdir(parents=True)
    subprocess.run(['ditto',str(source),str(app)],check=True)
    bundled=app/'HollowKnightResources-v1'
    for item,name in files:
        target=bundled/name
        target.parent.mkdir(parents=True,exist_ok=True)
        clone_file(item,target)
    for name in ('pack.json','resource-index.json'):
        shutil.copyfile(pack/name,bundled/name)
    contract['embedded']=True
    (app/'HollowKnightResourceContract.json').write_text(json.dumps(contract,indent=2),encoding='utf-8')
    output.parent.mkdir(parents=True,exist_ok=True)
    partial=output.with_suffix(output.suffix+'.partial.zip')
    if partial.exists(): raise ValueError('Previous unfinished package exists')
    subprocess.run(['/usr/bin/zip','-qry',str(partial),'Payload'],cwd=stage,check=True)
    with zipfile.ZipFile(partial) as archive:
        prefix='Payload/HollowKnight.app/HollowKnightResources-v1/'
        for item,name in files:
            if archive.getinfo(prefix+name).file_size!=item.stat().st_size:
                raise ValueError('Incomplete packaged resource: '+name)
    partial.replace(output)
    print('BUNDLED_IPA='+str(output),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    bundle(args.work,args.output)
