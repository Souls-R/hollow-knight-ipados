"""Extract PNG artwork from a user-supplied EnemyHPBar mod without running it."""
import argparse
import io
from pathlib import Path
import zipfile
from PIL import Image

NAMES={
    'DamageValues.Resources.bg.png':'bg.png',
    'DamageValues.Resources.mg.png':'mg.png',
    'DamageValues.Resources.ol.png':'ol.png',
    'DamageValues.Resources.bossbg.png':'bossbg.png',
    'DamageValues.Resources.bossfg.png':'bossfg.png',
    'DamageValues.Resources.bossol.png':'bossol.png',
    'DamageValues.Assets.Default.png':'digits.png',
}

def extract(source,work):
    import dnfile
    source,work=Path(source).resolve(strict=True),Path(work).resolve(strict=True)
    if not (work/'project/Info.plist').is_file(): raise ValueError('Use a prepared native build directory')
    if source.suffix.lower()=='.zip':
        with zipfile.ZipFile(source) as z:
            entries=[e for e in z.infolist() if e.filename.replace('\\','/').lower().endswith('plugins/enemyhpbar.dll')]
            if len(entries)!=1 or entries[0].file_size>2*1024*1024: raise ValueError('Expected one EnemyHPBar.dll')
            blob=z.read(entries[0])
    else:
        if source.stat().st_size>2*1024*1024: raise ValueError('DLL too large')
        blob=source.read_bytes()
    pe=dnfile.dnPE(data=blob)
    result={}
    try:
        if not pe.net: raise ValueError('Expected a managed plugin')
        for item in pe.net.resources:
            name=str(item.name)
            if name not in NAMES: continue
            data=item.data
            if not isinstance(data,bytes) or len(data)>4*1024*1024: raise ValueError('Invalid embedded image')
            with Image.open(io.BytesIO(data)) as image:
                w,h=image.size
                if not (0<w<=2048 and 0<h<=2048): raise ValueError('Image dimensions')
                image.verify()
            result[NAMES[name]]=data
    finally: pe.close()
    if set(result)!=set(NAMES.values()): raise ValueError('Missing expected mod artwork')
    output=work/'project/NativeEnemyHud'
    output.mkdir(exist_ok=True)
    for name,data in result.items(): (output/name).write_bytes(data)
    (output/'SOURCE.txt').write_text('Artwork extracted locally from the user-provided Enemy Health Bar with Damage Number mod by HollowBoys. Original mod: https://www.nexusmods.com/hollowknight/mods/123\nNot covered by the source repository MIT license.\n',encoding='utf-8')
    return sorted(result)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--mod',type=Path,required=True)
    p.add_argument('--work',type=Path,required=True)
    args=p.parse_args()
    print('Extracted local artwork:',', '.join(extract(args.mod,args.work)))
