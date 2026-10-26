#!/usr/bin/env python3
"""Install the additive Scenario 1 package; no scene/model/native-library edits."""
from __future__ import annotations
import argparse
import datetime
import json
from pathlib import Path
import shutil
import sys

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, required=True)
    args = parser.parse_args()
    project = args.project.expanduser().resolve()
    source = Path(__file__).resolve().parent.parent
    destination = project / 'Assets/SDxScenario1'
    if not (project / 'ProjectSettings/ProjectVersion.txt').is_file():
        raise SystemExit('Not a Unity project: ' + str(project))
    for f in ['Assets/SDxToolsUI/Runtime/SDxToolsPanel.cs',
              'Assets/SDxDoorInteraction/Runtime/SDxPassiveSlidingDoor.cs',
              'Assets/Plugins/x86_64/libmujoco.so']:
        if not (project / f).is_file():
            raise SystemExit('Required baseline file missing: ' + f)
    duplicates = [p for p in (project/'Assets').rglob('SDxScenario1Controller.cs')
                  if destination not in p.parents]
    if duplicates:
        raise SystemExit('Duplicate controller elsewhere: ' + str(duplicates[0]))
    assembly = 'Mujoco.Runtime'
    candidates = list((project/'Packages').glob('**/Mujoco.Runtime.asmdef'))
    candidates += list((project/'Library/PackageCache').glob('*/Runtime/Mujoco.Runtime.asmdef'))
    if candidates:
        assembly = json.loads(candidates[0].read_text())['name']
        print('Using installed MuJoCo assembly:', assembly)
    else:
        print('MuJoCo cache not present; using assembly name:', assembly)
    if source != destination.resolve():
        if destination.exists():
            stamp = datetime.datetime.now().strftime('%Y%m%d_%H%M%S_%f')
            backup = project.parent / ('SDxScenario1_CodeBackup_' + stamp)
            shutil.copytree(destination, backup)
            print('Previous package backed up:', backup)
        shutil.copytree(source, destination, dirs_exist_ok=True)
    definition = destination / 'Core/SDx.Scenario1.asmdef'
    content = json.loads(definition.read_text())
    content['references'] = [assembly]
    definition.write_text(json.dumps(content, indent=2) + '\n')
    print('Installed:', destination)
    print('Open Unity and use Tools > SDx > Scenario 1 > Prepare Current Scene.')
    print('No world, robot, GUI, native library or ProjectSettings files were changed.')

if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, KeyError) as error:
        print('INSTALL ERROR:', error, file=sys.stderr)
        raise SystemExit(1)
