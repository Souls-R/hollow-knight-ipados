"""Refresh compatibility assemblies and their matched metadata in a split work tree."""
import argparse
import json
from pathlib import Path
import shutil
import time
from native_build import compile_helper, convert
from launch_assets import configure

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work', type=Path, required=True)
    parser.add_argument('--input-system', type=Path, help='Override stored Input System package location')
    parser.add_argument('--jobs', type=int, choices=range(1, 33), default=4)
    args = parser.parse_args()
    work = args.work.expanduser().resolve(strict=True)
    manifest = json.loads((work/'build.json').read_text())
    assert manifest['unity'] == '6000.0.61f1' and manifest['split_resources']
    if args.input_system: manifest['input_system'] = str(args.input_system.expanduser().resolve(strict=True))
    if 'input_system' not in manifest: parser.error('Pass --input-system for a legacy research work tree')
    # Keep generated code and metadata paired; never relink old libraries after
    # this operation without running native to compile the new code.
    compile_helper(work, manifest)
    cpp = work/'project/Il2CppOutput'
    if cpp.exists(): cpp.rename(cpp.with_name('Il2CppOutput-before-'+str(time.time_ns())))
    manifest['native_built'] = False
    manifest['runtime_compile_required'] = True
    (work/'build.json').write_text(json.dumps(manifest, indent=2))
    convert(work, args.jobs)
    shutil.copytree(work/'metadata', work/'project/Data/Managed', dirs_exist_ok=True)
    configure(work/'project', Path(manifest['game_data']))
    print('Runtime regenerated. Run build.py native before packaging.')
