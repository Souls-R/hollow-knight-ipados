"""Run native dispatch/runtime regressions against a private, staged Mod build."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess

ROOT=Path(__file__).resolve().parents[1]

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--stage',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--dotnet',type=Path,required=True)
    parser.add_argument('--csc',type=Path,required=True)
    parser.add_argument('--runtime-version',required=True)
    args=parser.parse_args()
    stage=args.stage.resolve(strict=True);output=args.output.resolve()
    if output.exists():raise ValueError('Use a fresh regression output directory')
    output.mkdir(parents=True)
    for path in stage.glob('*.dll'):
        if path.name not in ('mscorlib.dll','netstandard.dll') and not path.name.startswith('System'):
            shutil.copyfile(path,output/path.name)
    for name in ('NativeHookRegression','NativeModRuntimeRegression'):
        arguments=['-nologo','-target:exe','-nostdlib+','-out:'+str(output/(name+'.dll'))]
        arguments+=['-r:'+str(path) for path in sorted(stage.glob('*.dll'))]
        arguments+=[str(ROOT/'tests'/(name+'.cs'))]
        response=output/(name+'.rsp');response.write_text('\n'.join('"'+arg+'"' for arg in arguments))
        subprocess.run([str(args.dotnet),str(args.csc),'@'+str(response)],check=True)
        (output/(name+'.runtimeconfig.json')).write_text(json.dumps(dict(runtimeOptions=dict(tfm='net10.0',framework=dict(name='Microsoft.NETCore.App',version=args.runtime_version)))))
        subprocess.run([str(args.dotnet),str(output/(name+'.dll'))],cwd=output,check=True)
