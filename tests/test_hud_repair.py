from pathlib import Path
import os
import re
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import quoteattr

ROOT=Path(__file__).resolve().parents[1]

class HudRepairTests(unittest.TestCase):
    def test_legacy_missing_cells_modern_art_and_bounds(self):
        dotnet=shutil.which('dotnet')
        if not dotnet: self.skipTest('Requires .NET SDK 8 or newer')
        sdks=subprocess.check_output([dotnet,'--list-sdks'],text=True)
        majors=[int(m) for m in re.findall(r'^(\d+)\.',sdks,re.M) if int(m)>=8]
        if not majors: self.skipTest('Requires .NET SDK 8 or newer')
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); framework=f'net{max(majors)}.0'
            includes=''.join(f'<Compile Include={quoteattr(str(ROOT/f))} />' for f in
                ['src/runtime/NativeHudFrameRepair.cs','tests/HudFrameRepairTests.cs'])
            (root/'HudChecks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                f'<OutputType>Exe</OutputType><TargetFramework>{framework}</TargetFramework>'
                '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit>'
                f'</PropertyGroup><ItemGroup>{includes}</ItemGroup></Project>',encoding='utf-8')
            (root/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
            env=dict(os.environ,DOTNET_CLI_HOME=str(root),DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_GENERATE_ASPNET_CERTIFICATE='false',DOTNET_CLI_UI_LANGUAGE='en')
            for command in ([dotnet,'build','--nologo','-v:q'],[dotnet,str(root/f'bin/Debug/{framework}/HudChecks.dll')]):
                result=subprocess.run(command,cwd=root,env=env,capture_output=True,encoding='utf-8',errors='replace',timeout=120)
                self.assertEqual(result.returncode,0,result.stdout+result.stderr)

if __name__=='__main__': unittest.main()
