#!/usr/bin/env python3
"""Build a personal Hollow Knight iPadOS app from your own macOS game files."""
import argparse
from pathlib import Path
import runpy
import sys

COMMANDS = {
    'resources': ('convert_resources.py', [], 'Convert original game resources'),
    'prepare': ('native_build.py', ['prepare'], 'Generate the split iOS project and resource folder'),
    'native': ('native_build.py', ['native'], 'Compile and package the unsigned program IPA'),
    'resource-zip': ('split_package.py', ['zip'], 'Archive resources for Files import'),
    'skin-zip': ('skin_package.py', [], 'Package a Custom Knight PNG skin for in-game switching'),
    'enemy-hud-assets': ('enemy_hud_assets.py', [], 'Extract artwork from your EnemyHPBar mod into the local app'),
    'refresh-runtime': ('refresh_runtime.py', [], 'Regenerate compatibility code and matched metadata'),
    'relink': ('build_split.py', [], 'Rebuild the native launcher using cached game libraries'),
}

def main():
    if not __debug__:
        raise SystemExit('Run without Python -O; conversion and patch assertions are required.')
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    for name, (_, _, description) in COMMANDS.items():
        commands.add_parser(name, help=description, add_help=False)
    if len(sys.argv) < 2 or sys.argv[1] in ('-h', '--help'):
        parser.parse_args()
        return
    command = sys.argv[1]
    if command not in COMMANDS:
        parser.error('Unknown command: ' + command)
    script, prefix, _ = COMMANDS[command]
    scripts = Path(__file__).resolve().parent/'scripts'
    sys.path.insert(0, str(scripts))
    sys.argv = [str(scripts/script), *prefix, *sys.argv[2:]]
    runpy.run_path(str(scripts/script), run_name='__main__')

if __name__ == '__main__':
    main()
