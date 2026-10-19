"""Derive iOS serialized files while keeping all original IDs and references."""
import argparse
from concurrent.futures import ProcessPoolExecutor
import gc
import hashlib
import json
from pathlib import Path
import shutil
import time
import UnityPy
from scripts.texture_conversion import convert_texture, texture_identity, texture_payload
from scripts.common import output_directory


def one(job):
    source, output, stamp = map(Path, job)
    stat = source.stat()
    identity = dict(bytes=stat.st_size, mtime_ns=stat.st_mtime_ns, policy=1)
    if stamp.is_file() and output.is_file():
        previous = json.loads(stamp.read_text())
        if previous['source'] == identity and previous['output_bytes'] == output.stat().st_size:
            return dict(file=source.name, reused=True)
    env = UnityPy.load(str(source))
    af, = env.files.values()
    if not hasattr(af, 'objects'):
        raise ValueError(f'Expected serialized file: {source}')
    if af.unity_version != '6000.0.61f1':
        raise ValueError(f'Unexpected game version: {af.unity_version}')
    unchanged, converted, changes = {}, {}, []
    for obj in env.objects:
        if obj.type.name != 'Texture2D':
            unchanged[obj.path_id] = hashlib.sha256(obj.get_raw_data()).digest()
            continue
        tex = obj.read()
        if tex.m_TextureFormat not in (10, 12, 25, 28):
            unchanged[obj.path_id] = hashlib.sha256(obj.get_raw_data()).digest()
            continue
        sampling = texture_identity(tex)
        changes.append(convert_texture(tex))
        converted[obj.path_id] = (sampling, tex.m_TextureFormat, hashlib.sha256(texture_payload(tex)).digest())
    af._m_target_platform = 9
    af.mark_changed()
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + '.partial')
    temporary.write_bytes(af.save())
    del env, af
    gc.collect()
    env = UnityPy.load(str(temporary))
    check, = env.files.values()
    assert check._m_target_platform == 9
    assert set(check.objects) == set(unchanged) | set(converted)
    for pid, digest in unchanged.items():
        assert hashlib.sha256(check.objects[pid].get_raw_data()).digest() == digest, (source, pid)
    for pid, (sampling, fmt, digest) in converted.items():
        tex = check.objects[pid].read()
        assert texture_identity(tex) == sampling
        assert tex.m_TextureFormat == fmt and fmt not in (10, 12, 25, 28)
        assert hashlib.sha256(texture_payload(tex)).digest() == digest
    del env, check
    temporary.replace(output)
    result = dict(file=source.name, source=identity, output_bytes=output.stat().st_size,
                  converted_textures=len(converted), unchanged_objects=len(unchanged), changes=changes)
    stamp.parent.mkdir(parents=True, exist_ok=True)
    stamp.write_text(json.dumps(result, indent=2), encoding='utf-8')
    gc.collect()
    return dict(file=source.name, textures=len(converted), reused=False)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-data', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--jobs', type=int, default=2)
    args = parser.parse_args()
    game = args.game_data.expanduser().resolve(strict=True)
    output = output_directory(args.output, game)
    data = output / 'Data'
    data.mkdir(parents=True, exist_ok=True)
    jobs = []
    for source in sorted(game.rglob('*')):
        if not source.is_file() or 'Managed' in source.relative_to(game).parts:
            continue
        relative = source.relative_to(game)
        target = data / relative
        serialized = (source.suffix == '.assets' or source.name.startswith('level') and not source.suffix
                      or source.name in ('globalgamemanagers', 'unity_builtin_extra'))
        if serialized:
            jobs.append((str(source), str(target), str(output / 'receipts' / (relative.as_posix().replace('/', '_') + '.json'))))
        elif not target.is_file() or source.stat().st_size != target.stat().st_size:
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
    started = time.monotonic()
    with ProcessPoolExecutor(max_workers=args.jobs) as pool:
        for index, result in enumerate(pool.map(one, jobs)):
            if index % 20 == 0 or result.get('textures'):
                print(f'{index+1}/{len(jobs)} {json.dumps(result)}', flush=True)
    summary = dict(serialized_files=len(jobs), seconds=round(time.monotonic()-started, 2),
                   unity='6000.0.61f1', target_platform=9, embedded_resources=True,
                   original_game_unchanged=True, device_verified=False)
    (output / 'resources.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    print(json.dumps(summary), flush=True)


if __name__ == '__main__':
    main()
