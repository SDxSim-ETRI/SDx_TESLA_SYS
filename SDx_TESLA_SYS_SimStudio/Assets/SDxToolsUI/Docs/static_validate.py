#!/usr/bin/env python3
"""Package-integrity/source checks only. No Unity installation needed."""
from pathlib import Path
import hashlib
import json
import sys

def main():
    root = Path(__file__).resolve().parents[1]
    manifest = json.loads((root / 'Docs/package_manifest.json').read_text(encoding='utf-8'))
    errors = []
    for rel, expected in manifest['sha256'].items():
        path = root / rel
        if not path.is_file():
            errors.append('Missing file: ' + rel)
        elif hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            errors.append('Changed file: ' + rel)
    source = (root / 'Runtime/SDxToolsPanel.cs').read_text(encoding='utf-8')
    for required in ['SetRosConnected(bool connected)', 'onRosFunctionRequested',
                     'SDxTextInputBackend.Auto', 'DrawSingleLine', 'S(32)']:
        if required not in source:
            errors.append('Missing required source token: ' + required)
    composer = (root / 'Runtime/SDxHangulComposer.cs').read_text(encoding='utf-8')
    field = (root / 'Runtime/SDxKoreanTextField.cs').read_text(encoding='utf-8')
    for required in ['RecomposeLooseJamo', 'ContainsLooseJamo', 'modern conjoining leading Jamo']:
        if required not in composer:
            errors.append('Missing Unicode Jamo processing in composer: ' + required)
    if 'SDxHangulComposer.RecomposeLooseJamo(result)' not in field:
        errors.append('Missing IMGUI fallback in text field.')
    if 'DrawReadonlyPath' in source or 'GUI.TextArea' in source:
        errors.append('Old large field implementation is still present.')
    if errors:
        print('\n'.join(errors), file=sys.stderr)
        return 1
    print('PACKAGE CHECK PASSED: v' + manifest.get('package_version', manifest.get('version', 'unknown')))
    print('This does not compile or run Unity. Run Tools > SDx > Run Text Input Self Checks in Unity.')
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
