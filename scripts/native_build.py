"""Prepare and compile the supported game into a split native iOS app."""
import argparse
import json
import os
from pathlib import Path
import platform
import plistlib
import re
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
VERSION = '6000.0.61f1'
os.environ['DOTNET_ReadyToRun'] = '0'

def run(command, log, cwd=None):
    log.parent.mkdir(parents=True, exist_ok=True)
    print(f'Running {Path(str(command[0])).name}; log={log}', flush=True)
    with log.open('w', encoding='utf-8') as stream:
        result = subprocess.run([str(x) for x in command], stdout=stream, stderr=subprocess.STDOUT, cwd=cwd)
    if result.returncode:
        raise RuntimeError(log.read_text(encoding='utf-8', errors='replace')[-8000:])

def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2), encoding='utf-8')

def compiler(work):
    names = ['deploy_arm64', 'deploy'] if platform.machine() == 'arm64' else ['deploy_x86_64', 'deploy']
    for name in names:
        deploy = work/'IL2CPP/build'/name
        if (deploy/'il2cpp').is_file(): return deploy
    raise ValueError('No IL2CPP host compiler for ' + platform.machine())

def patch_generated(cpp):
    from patch_animator_compat import apply as animator
    from patch_hollow_knight import apply as compatibility
    from patch_runtime_diagnostics import apply as diagnostics
    from patch_media_paths import apply as media_paths
    patches = dict(animator=animator(cpp), hollow_knight=compatibility(cpp),
                   diagnostics=diagnostics(cpp), media_paths=media_paths(cpp))
    pattern = re.compile(r'IL2CPP_EXTERN_C IL2CPP_METHOD_ATTR void DefaultContractResolver_GetCallbackMethodsForType_[^(]+\([^\n]+\)\s*\n\{')
    matches = [(p, m) for p in cpp.glob('Newtonsoft.Json*.cpp') for m in pattern.finditer(p.read_text(encoding='utf-8'))]
    assert len(matches) == 1
    path, match = matches[0]
    source = path.read_text(encoding='utf-8')
    end = source.index('\nIL2CPP_EXTERN_C IL2CPP_METHOD_ATTR', match.end())
    source = source[:match.start()] + '\n#pragma clang optimize off\n' + source[match.start():end] + '\n#pragma clang optimize on\n' + source[end:]
    path.write_text(source, encoding='utf-8')
    patches['json_callback_optimization'] = path.name
    return patches

def convert(work, jobs):
    cpp = work/'project/Il2CppOutput'
    run([compiler(work)/'il2cpp', '--convert-to-cpp', '--platform=iOS', '--architecture=arm64',
         '--dotnetprofile=unityaot-macos', '--static-lib-il2-cpp', '--directory='+str(work/'managed'),
         '--generatedcppdir='+str(cpp), '--data-folder='+str(work/'metadata'),
         '--emit-null-checks', '--enable-array-bounds-check', '--enable-stacktrace', '--jobs='+str(jobs)], work/'logs/convert.log', work)
    write(work/'patches.json', patch_generated(cpp))

def compile_helper(work, manifest):
    managed = work/'managed'
    helper = ['-nologo', '-target:library', '-nostdlib+', '-optimize+', '-unsafe+', '-define:UNITY_IOS',
              '-out:"'+str(managed/'NativeCompatibility.dll')+'"']
    helper += ['-r:"'+str(p)+'"' for p in sorted(managed.glob('*.dll')) if p.name != 'NativeCompatibility.dll']
    helper += ['"'+str(p)+'"' for p in sorted((ROOT/'src/runtime').glob('*.cs'))]
    input_source = Path(manifest['input_system'])/'InputSystem/Plugins/iOS/IOSGameController.cs'
    if not input_source.is_file(): raise ValueError('Input System package source missing: '+str(input_source))
    helper.append('"'+str(input_source)+'"')
    response = work/'helper.rsp'; response.write_text('\n'.join(helper), encoding='utf-8')
    host = Path(manifest['host_tools'])
    run([host/'NetCoreRuntime/dotnet', host/'DotNetSdkRoslyn/csc.dll', '@'+str(response)], work/'logs/helper.log')

def prepare(args):
    import UnityPy
    from split_package import prepare_direct
    from launch_assets import configure
    game = args.game_data.expanduser().resolve(strict=True)
    ios = args.ios_support.expanduser().resolve(strict=True)
    host = args.host_tools.expanduser().resolve(strict=True)
    input_system = args.input_system.expanduser().resolve(strict=True)
    resources = args.resources.expanduser().resolve(strict=True)
    output = args.output.expanduser().resolve()
    pack_output = args.pack_output.expanduser().resolve()
    protected = (ROOT, game, ios, host, input_system, resources)
    for target in (output, pack_output):
        if target.exists() or any(target.is_relative_to(p) or p.is_relative_to(target) for p in protected):
            raise ValueError('Use fresh output directories outside source/game/tools: '+str(target))
    if output.is_relative_to(pack_output) or pack_output.is_relative_to(output):
        raise ValueError('Native and resource output directories must be separate')
    if not re.fullmatch(r'[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+', args.bundle_id):
        raise ValueError('Use a reverse-DNS bundle ID')
    env = UnityPy.load(str(game/'globalgamemanagers')); af, = env.files.values()
    if af.unity_version != VERSION: raise ValueError('Requires Unity '+VERSION+', got '+af.unity_version)
    assert (ios/'Trampoline/Libraries/libiPhone-lib.a').is_file()
    manifest = dict(unity=VERSION, game_data=str(game), ios_support=str(ios), host_tools=str(host),
                    input_system=str(input_system), bundle_id=args.bundle_id, version=args.version,
                    build=args.build_number, prepared=True, native_built=False, device_verified=False)
    managed = output/'managed'; managed.mkdir(parents=True)
    for folder in (game/'Managed', host/'MonoBleedingEdge/lib/mono/unityaot-macos',
                   host/'MonoBleedingEdge/lib/mono/unityaot-macos/Facades', ios/'Variations/il2cpp/Releasearm64_managed'):
        assert folder.is_dir(), folder
        for source in folder.glob('*.dll'): shutil.copyfile(source, managed/source.name)
    compile_helper(output, manifest)
    project = output/'project'
    shutil.copytree(ios/'Trampoline', project, ignore=shutil.ignore_patterns('*sim*', '*-dev.a'))
    shutil.copytree(ios/'il2cpp', output/'IL2CPP', ignore=shutil.ignore_patterns('*.pdb'))
    convert(output, args.jobs)
    preprocessor = project/'Classes/Preprocessor.h'; text = preprocessor.read_text(encoding='utf-8')
    assert '#define PLATFORM_IOS    0' in text
    preprocessor.write_text(text.replace('#define PLATFORM_IOS    0', '#define PLATFORM_IOS    1').replace('#define UNITY_USES_IAD 1', '#define UNITY_USES_IAD 0'), encoding='utf-8')
    for source in (ROOT/'src/runtime').iterdir():
        if source.suffix in ('.mm', '.h'): shutil.copyfile(source, project/'Libraries'/source.name)
    info_path = project/'Info.plist'; info = plistlib.loads(info_path.read_bytes())
    info.update(CFBundleIdentifier=args.bundle_id, CFBundleDisplayName='Hollow Knight',
                CFBundleShortVersionString=args.version, CFBundleVersion=args.build_number,
                UIFileSharingEnabled=True, LSSupportsOpeningDocumentsInPlace=True, UIRequiresFullScreen=True,
                UISupportedInterfaceOrientations=['UIInterfaceOrientationLandscapeLeft', 'UIInterfaceOrientationLandscapeRight'])
    info_path.write_bytes(plistlib.dumps(info))
    prepare_direct(output, resources, pack_output, manifest)
    configure(project, game)
    write(output/'build.json', manifest)
    print('HOLLOW_KNIGHT_SPLIT_PROJECT_PREPARED', flush=True)

def native(args):
    work = args.work.expanduser().resolve(strict=True)
    manifest = json.loads((work/'build.json').read_text())
    assert manifest['unity'] == VERSION and manifest['split_resources']
    project = work/'project'; logs = work/'logs'; native = work/'native-output'
    native.mkdir(exist_ok=True); (work/'bee-cache').mkdir(exist_ok=True)
    run([compiler(work)/'il2cpp', '--compile-cpp', '--platform=iOS', '--architecture=arm64', '--configuration=Release',
         '--dotnetprofile=unityaot-macos', '--static-lib-il2-cpp', '--jobs='+str(args.jobs), '--verbose',
         '--custom-il2-cpp-root='+str(work/'IL2CPP'), '--baselib-directory='+str(project/'Libraries'),
         '--generatedcppdir='+str(project/'Il2CppOutput'), '--outputpath='+str(native/'libGameAssembly.a'),
         '--cachedirectory='+str(work/'bee-cache')], logs/'il2cpp-native.log', work)
    for name in ('libGameAssembly.a', 'il2cpp.a', 'baselib.a'): shutil.copyfile(native/name, project/'Libraries'/name)
    manifest['runtime_compile_required'] = False
    write(work/'build.json', manifest)
    run([sys.executable, ROOT/'scripts/register_modules.py', '--project', project, '--logs', logs], logs/'register-modules.log')
    run([sys.executable, ROOT/'scripts/build_split.py', '--work', work], logs/'package-split.log')
    print('IPA='+json.loads((work/'build.json').read_text())['ipa'], flush=True)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    p = commands.add_parser('prepare')
    for flag in ('game-data', 'ios-support', 'host-tools', 'input-system', 'resources', 'output', 'pack-output'):
        p.add_argument('--'+flag, type=Path, required=True)
    p.add_argument('--bundle-id', required=True)
    p.add_argument('--version', default='0.2.3'); p.add_argument('--build-number', default='7')
    p.add_argument('--jobs', type=int, choices=range(1, 33), default=4)
    n = commands.add_parser('native'); n.add_argument('--work', type=Path, required=True)
    n.add_argument('--jobs', type=int, choices=range(1, 33), default=4)
    args = parser.parse_args()
    (prepare if args.command == 'prepare' else native)(args)
