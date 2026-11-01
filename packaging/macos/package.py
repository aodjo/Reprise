#!/usr/bin/env python3
"""Sign and notarize a built Universal application, then create signed update and DMG downloads."""

import argparse
import hashlib
import json
from pathlib import Path
import plistlib
import shutil
import subprocess


def run(arguments, capture=False):
    """Run one signing or packaging command without exporting credentials from the Keychain."""
    return subprocess.check_output(arguments) if capture else subprocess.run(arguments, check=True)


def notarize(path, profile, output):
    """Require Apple's accepted verdict before stapling a public download."""
    result = json.loads(run(['xcrun', 'notarytool', 'submit', str(path), '--keychain-profile', profile,
                             '--wait', '--output-format', 'json'], capture=True))
    output.write_text(json.dumps(result, indent=2) + '\n')
    if result.get('status') != 'Accepted':
        raise ValueError('Notarization was not accepted: ' + result.get('id', 'unknown submission'))


def package(app, output, identity, profile, tools):
    """Re-sign nested Sparkle helpers, strip debugging rights, and publish only notarized artifacts."""
    output.mkdir(parents=True, exist_ok=True)
    target = output / 'Reprise.app'
    if target.exists():
        shutil.rmtree(target)
    run(['ditto', str(app), str(target)])
    info = plistlib.loads((target / 'Contents/Info.plist').read_bytes())
    version = info['CFBundleShortVersionString']
    architectures = run(['lipo', '-archs', str(target / 'Contents/MacOS/Reprise')], capture=True).decode().split()
    if set(architectures) != {'arm64', 'x86_64'}:
        raise ValueError('A Universal arm64/x86_64 application is required.')
    framework = target / 'Contents/Frameworks/Sparkle.framework'
    common = ['codesign', '--force', '--sign', identity, '--options', 'runtime', '--timestamp']
    for relative in ['Versions/B/XPCServices/Installer.xpc', 'Versions/B/XPCServices/Downloader.xpc',
                     'Versions/B/Autoupdate', 'Versions/B/Updater.app']:
        options = ['--preserve-metadata=entitlements'] if relative.endswith('Downloader.xpc') else []
        run([*common, *options, str(framework / relative)])
    run([*common, str(framework)])
    entitlements = plistlib.loads(run(['codesign', '-d', '--entitlements', '-', '--xml', str(target)], capture=True))
    entitlements.pop('com.apple.security.get-task-allow', None)
    entitlements_file = output / 'distribution-entitlements.plist'
    entitlements_file.write_bytes(plistlib.dumps(entitlements))
    run([*common, '--entitlements', str(entitlements_file), str(target)])
    run(['codesign', '--verify', '--deep', '--strict', str(target)])
    submission = output / 'notarization-input.zip'
    run(['ditto', '-c', '-k', '--sequesterRsrc', '--keepParent', str(target), str(submission)])
    notarize(submission, profile, output / 'app-notarization.json')
    run(['xcrun', 'stapler', 'staple', str(target)])
    run(['xcrun', 'stapler', 'validate', str(target)])
    run(['spctl', '--assess', '--type', 'execute', '--verbose', str(target)])
    archive = output / f'Reprise-{version}-macos-universal.zip'
    run(['ditto', '-c', '-k', '--sequesterRsrc', '--keepParent', str(target), str(archive)])
    signature = run([str(tools / 'sign_update'), str(archive)], capture=True).decode().strip()
    (output / 'update-signature.txt').write_text(signature + '\n')
    contents = output / 'dmg-contents'
    contents.mkdir(exist_ok=True)
    run(['ditto', str(target), str(contents / 'Reprise.app')])
    applications = contents / 'Applications'
    if not applications.is_symlink():
        applications.symlink_to('/Applications')
    dmg = output / f'Reprise-{version}-macos-universal.dmg'
    run(['hdiutil', 'create', '-ov', '-volname', f'Reprise {version}', '-srcfolder', str(contents),
         '-format', 'UDZO', str(dmg)])
    run([*common, str(dmg)])
    notarize(dmg, profile, output / 'dmg-notarization.json')
    run(['xcrun', 'stapler', 'staple', str(dmg)])
    run(['xcrun', 'stapler', 'validate', str(dmg)])
    for path in (archive, dmg):
        print(path.name, path.stat().st_size, hashlib.sha256(path.read_bytes()).hexdigest(), flush=True)


def main():
    """Require explicit signing identity and an existing Keychain notarization profile."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('app', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--identity', required=True)
    parser.add_argument('--notary-profile', required=True)
    parser.add_argument('--sparkle-tools', required=True, type=Path)
    args = parser.parse_args()
    package(args.app.resolve(), args.output.resolve(), args.identity, args.notary_profile,
            args.sparkle_tools.resolve())


if __name__ == '__main__':
    main()
