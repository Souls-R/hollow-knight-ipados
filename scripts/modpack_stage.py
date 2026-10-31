"""Build native Mod assemblies from independently supplied, private inputs."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess

ROOT=Path(__file__).resolve().parents[1]
ASSEMBLIES=('Assembly-CSharp','PlayMaker','TeamCherry.Localization','AnyRadiance1._0',
            'DebugMod','QoL','GodSeekerPlus','Osmi','Satchel','Vasi','NativeModRuntime',
            'NativeModLoader','MonoMod.Utils','MonoMod.RuntimeDetour','MonoMod.Backports',
            'MMHOOK_Assembly-CSharp','MMHOOK_PlayMaker')

def run(command,cwd=None):
    subprocess.run([str(value) for value in command],cwd=cwd,check=True)

def build(work,dotnet,csc):
    work=work.expanduser().resolve(strict=True)
    if (work/'native-stage').exists():raise ValueError('Use a fresh work directory; preserve previous outputs')
    required=('api-reference/Vanilla/Assembly-CSharp.dll','api-reference/OutputFinal/Assembly-CSharp.dll',
              'inputs/AnyRadiance1.dll','inputs/DebugMod.dll','inputs/QoL/QoL.dll','inputs/GodSeekerPlus.dll',
              'dependencies/Satchel.dll','dependencies/Osmi.dll','dependencies/Vasi.dll')
    for name in required:
        if not (work/name).is_file():raise ValueError('Missing private input: '+name)
    project=ROOT/'tools/NativeModpackPort/NativeModpackPort.csproj'
    run([dotnet,'build',project,'-c','Release','--nologo','-p:NuGetAudit=false'])
    tool=project.parent/'bin/Release/net10.0/NativeModpackPort.dll'
    def port(command):run([dotnet,tool,command,work])
    def compile(name,sources):
        output=work/'native-compiled';output.mkdir(exist_ok=True)
        stage=work/'native-stage'
        arguments=['-nologo','-target:library','-nostdlib+','-optimize+','-unsafe+',
                   '-out:'+str(output/(name+'.dll'))]
        arguments+=['-r:'+str(path) for path in sorted(stage.glob('*.dll')) if path.stem!=name]
        arguments+=[str(path) for path in sources]
        response=output/(name+'.rsp')
        response.write_text('\n'.join('"'+arg+'"' for arg in arguments),encoding='utf-8')
        run([dotnet,csc,'@'+str(response)])
        shutil.copyfile(output/(name+'.dll'),stage/(name+'.dll'))
    def hooks():
        for folder in sorted((work/'native-hook-sources').iterdir()):compile(folder.name,sorted(folder.glob('*.cs')))
    source=ROOT/'src/modpack'
    port('hooks')
    compile('NativeModRuntime',[source/name for name in ('NativeModRuntime.cs','NativeObjectReflection.cs','NativeLegacyInput.cs')])
    compile('MonoMod.Utils',[source/'NativeUtilities.cs'])
    compile('MonoMod.RuntimeDetour',[source/'NativeDetours.cs'])
    compile('MonoMod.Backports',[source/'NativeStringFormatting.cs'])
    hooks()
    port('fixed')
    port('compat')
    port('dynamic-compat')
    port('il-events');hooks()
    port('strip-build-il')
    port('legacy-compat')
    compile('NativeModLoader',[source/'NativeLoader.cs',source/'NativeModControls.cs'])
    port('static-loader')
    port('input-adapters')
    port('finalize')
    port('serialization-fix')
    port('serialization-audit')
    port('audit-native')
    port('inventory')
    (work/'native-modpack-assemblies.json').write_text(json.dumps(ASSEMBLIES,indent=2),encoding='utf-8')
    print('NATIVE_MODPACK_STAGE_READY='+str(work/'native-stage'))

def install(stage,work):
    stage=stage.expanduser().resolve(strict=True);work=work.expanduser().resolve(strict=True)
    manifest_path=work/'build.json';manifest=json.loads(manifest_path.read_text())
    if manifest.get('unity')!='6000.0.61f1' or not manifest.get('split_resources'):
        raise ValueError('Use a matching, prepared native game work directory')
    for name in ASSEMBLIES:
        if not (stage/(name+'.dll')).is_file():raise ValueError('Incomplete native stage: '+name)
    target=work/'managed'
    backup=work/'managed-before-modpack'
    if backup.exists():raise ValueError('Previous Mod installation exists; preserve its code and metadata')
    shutil.copytree(target,backup)
    for name in ASSEMBLIES:shutil.copyfile(stage/(name+'.dll'),target/(name+'.dll'))
    path=work/'project/Data/ScriptingAssemblies.json'
    scripts=json.loads(path.read_text());kind=scripts['types'][scripts['names'].index('Assembly-CSharp.dll')]
    for name in ASSEMBLIES:
        if name+'.dll' not in scripts['names']:
            scripts['names'].append(name+'.dll');scripts['types'].append(kind)
    path.write_text(json.dumps(scripts,indent=2),encoding='utf-8')
    manifest.update(native_built=False,runtime_compile_required=True,native_modpack=list(ASSEMBLIES))
    manifest_path.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    print('Native Mod inputs installed. Run refresh-runtime, native, then bundle-ipa.')

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    sub=parser.add_subparsers(dest='command',required=True)
    build_parser=sub.add_parser('build');build_parser.add_argument('--work',type=Path,required=True)
    build_parser.add_argument('--dotnet',type=Path,default=Path(shutil.which('dotnet') or 'dotnet'))
    build_parser.add_argument('--csc',type=Path,required=True,help='Roslyn csc.dll from the installed .NET SDK')
    install_parser=sub.add_parser('install');install_parser.add_argument('--stage',type=Path,required=True)
    install_parser.add_argument('--work',type=Path,required=True)
    args=parser.parse_args()
    if args.command=='build':build(args.work,args.dotnet,args.csc)
    else:install(args.stage,args.work)
