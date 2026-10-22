"""Package data-only Custom Knight PNG skins for the native in-game selector."""
import argparse
import json
from pathlib import Path, PurePosixPath
import re
import struct
import zipfile

SHEETS = {
    'Knight': (4096,4096), 'Sprint': (512,1024), 'HUD': (2048,2048),
    'VS': (2048,2048), 'Wraiths': (2048,2048), 'VoidSpells': (2048,2048),
    'Unn': (1024,1024), 'Geo': (256,512), 'Shade': (2048,2048),
    'Hatchling': (1024,1024), 'Fluke': (1024,1024), 'Shield': (1024,1024),
    'Baldur': (1024,1024), 'Grimm': (2048,2048), 'Weaver': (512,1024),
    'Liquid': (512,1024), 'Compass': (256,512), 'OrbFull': (130,125),
    'Cloak': (1024,1024), 'Shriek': (512,512), 'Wings': (1024,1024),
    'Quirrel': (1024,2048), 'Hornet': (2048,2048), 'Webbed': (1024,1024),
    'Birthplace': (1024,2048), 'DreamArrival': (1024,1024), 'Dreamnail': (1024,1024),
}
MAX_BYTES = 80*1024*1024

def validate_png(data, name):
    if len(data)<24 or len(data)>MAX_BYTES or data[:8]!=b'\x89PNG\r\n\x1a\n' or data[12:16]!=b'IHDR':
        raise ValueError(name+': invalid PNG header/size')
    size=struct.unpack('>II',data[16:24])
    expected=SHEETS[name]
    if size not in (expected,tuple(n*2 for n in expected)):
        raise ValueError(f'{name}: expected {expected} or 2x, got {size}')
    # Decode here, before delivering a broken skin to the device.
    from PIL import Image
    import io
    with Image.open(io.BytesIO(data)) as image:
        image.verify()

def package(source, output, skin_id, name, author='', url=''):
    source,output=Path(source).resolve(strict=True),Path(output).resolve()
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]{0,63}',skin_id):
        raise ValueError('Use a short alphanumeric skin ID')
    if output.exists(): raise ValueError('Output already exists')
    known={n.lower():n for n in SHEETS}
    files={}
    ignored=[]
    def add(filename,read,size):
        path=PurePosixPath(filename.replace('\\','/'))
        if path.is_absolute() or '..' in path.parts or ':' in filename: raise ValueError('Unsafe archive path')
        if path.suffix.lower()!='.png': return
        if path.stem.lower() not in known:
            ignored.append(path.name); return
        key=known[path.stem.lower()]
        if key in files: raise ValueError('Duplicate sheet: '+key)
        if size>MAX_BYTES: raise ValueError('Oversized sheet: '+key)
        value=read(); validate_png(value,key); files[key]=value
    if source.is_dir():
        for path in sorted(source.rglob('*')):
            if path.is_symlink(): raise ValueError('Symlinks are not supported')
            if path.is_file(): add(path.relative_to(source).as_posix(),path.read_bytes,path.stat().st_size)
    else:
        with zipfile.ZipFile(source) as archive:
            for entry in archive.infolist():
                if (entry.external_attr>>16)&0o170000==0o120000: raise ValueError('Symlinks are not supported')
                add(entry.filename,lambda e=entry:archive.read(e),entry.file_size)
    if 'Knight' not in files: raise ValueError('Knight.png is required')
    prefix=f'HollowKnightSkins/{skin_id}'
    metadata=dict(name=name,author=author,source=url,format='custom-knight-png-v1',sheets=sorted(files),unsupported=sorted(ignored))
    output.parent.mkdir(parents=True,exist_ok=True)
    # PNG is compressed already; avoid another slow compression pass on iPad.
    with zipfile.ZipFile(output,'w',compression=zipfile.ZIP_STORED) as archive:
        for key,value in files.items(): archive.writestr(f'{prefix}/{key}.png',value)
        archive.writestr(f'{prefix}/metadata.json',json.dumps(metadata,ensure_ascii=False,indent=2))
    return metadata

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    p.add_argument('--id',required=True)
    p.add_argument('--name',required=True)
    p.add_argument('--author',default='')
    p.add_argument('--url',default='')
    a=p.parse_args()
    print(json.dumps(package(a.source,a.output,a.id,a.name,a.author,a.url),ensure_ascii=False,indent=2))
