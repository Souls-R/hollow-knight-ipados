from pathlib import Path
import os
import re
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import quoteattr

from scripts.common import REPO


class ControllerInputTests(unittest.TestCase):
    def test_managed_touchpad_layout_update_order_and_isolation(self):
        dotnet = shutil.which('dotnet')
        if not dotnet:
            self.skipTest('Managed controller tests require a .NET SDK (8 or newer)')
        sdks = subprocess.check_output([dotnet, '--list-sdks'], text=True)
        majors = [int(v) for v in re.findall(r'^(\d+)\.', sdks, re.MULTILINE) if int(v) >= 8]
        if not majors:
            self.skipTest('Managed controller tests require a .NET SDK (8 or newer)')
        framework = f'net{max(majors)}.0'
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            files = ['src/runtime/NativeControllerInput.cs', 'tests/ControllerInputTests.cs']
            includes = ''.join(f'<Compile Include={quoteattr(str(REPO/name))} />' for name in files)
            (root/'ControllerChecks.csproj').write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                f'<OutputType>Exe</OutputType><TargetFramework>{framework}</TargetFramework>'
                '<EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit>'
                f'</PropertyGroup><ItemGroup>{includes}</ItemGroup></Project>', encoding='utf-8')
            (root/'NuGet.Config').write_text(
                '<configuration><packageSources><clear /></packageSources></configuration>', encoding='utf-8')
            env = dict(os.environ, DOTNET_CLI_HOME=str(root), DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
                       DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_CLI_UI_LANGUAGE='en',
                       DOTNET_GENERATE_ASPNET_CERTIFICATE='false')
            for command in ([dotnet, 'build', '--nologo', '-v:q'],
                            [dotnet, str(root/f'bin/Debug/{framework}/ControllerChecks.dll')]):
                result = subprocess.run(command, cwd=root, env=env, encoding='utf-8', errors='replace',
                                        capture_output=True, timeout=120)
                self.assertEqual(result.returncode, 0, result.stdout+result.stderr)

    def test_native_touchpad_edges_and_lifecycle(self):
        compiler = shutil.which('clang++') or shutil.which('g++')
        if not compiler:
            self.skipTest('Native controller tests require a C++ compiler')
        with tempfile.TemporaryDirectory() as tmp:
            program = Path(tmp) / 'controller-state.exe'
            subprocess.run([compiler, '-std=c++17', str(REPO / 'tests/ControllerStateTests.cpp'),
                            '-o', str(program)], check=True, capture_output=True, text=True)
            result = subprocess.run([str(program)], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == '__main__':
    unittest.main()
