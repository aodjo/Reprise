#!/usr/bin/env python3
"""Build a self-contained Windows application, per-user installer, and portable archive."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import zipfile
import xml.etree.ElementTree as ET


# Source checkout holding the shared projects and original application icon.
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "packaging/linux"))
from update_channels import for_version


def run(arguments):
    """Run build commands with literal arguments and preserve readable compiler output."""
    subprocess.run(arguments, cwd=ROOT, check=True)


def build(architecture, build_number, publish_only=False):
    """Publish one architecture and generate a verified, immutable Windows installer artifact."""
    if not re.fullmatch(r"[0-9]{14}", build_number):
        raise ValueError("Build number must be a 14-digit UTC timestamp.")
    properties = ET.parse(ROOT / "Directory.Build.props").getroot()
    prefix = properties.findtext(".//VersionPrefix")
    suffix = next(node.text or "" for node in properties.findall(".//VersionSuffix") if not node.attrib)
    version = prefix + (f"-{suffix}.{build_number}" if suffix else "")
    channel = for_version(version)
    output = ROOT / f"Artifacts/windows-{architecture}" / build_number
    app = output / "app"
    output.mkdir(parents=True, exist_ok=True)
    if app.exists():
        shutil.rmtree(app)
    icon = output / "Reprise.ico"
    png = (ROOT / "BrowserExtension/Chromium/icons/icon128.png").read_bytes()
    width, height = struct.unpack(">II", png[16:24])
    icon.write_bytes(struct.pack("<HHH", 0, 1, 1) +
                     struct.pack("<BBBBHHII", width % 256, height % 256, 0, 0, 1, 32, len(png), 22) + png)
    run(["dotnet", "publish", "src/Reprise.Platform.Windows/Reprise.Platform.Windows.csproj",
         "-c", "Release", "-r", f"win-{architecture}", "--self-contained", "true",
         f"-p:RepriseBuildNumber={build_number}", f"-p:ApplicationIcon={icon}", "-o", str(app)])
    metadata = {"schemaVersion": 1, "platform": "windows", "channel": channel,
                "architecture": architecture, "version": version, "buildNumber": build_number}
    (app / "reprise-build.json").write_text(json.dumps(metadata, indent=2) + "\n")
    if publish_only:
        return output
    compiler = shutil.which("makensis") or shutil.which("makensis.exe")
    if compiler is None:
        for path in (Path("C:/Program Files (x86)/NSIS/makensis.exe"), Path("C:/Program Files/NSIS/makensis.exe")):
            if path.is_file():
                compiler = str(path)
                break
    if compiler is None:
        raise ValueError("NSIS is required to build the installer (brew install makensis or install NSIS on Windows).")
    filename = f"Reprise-{version}-win-{architecture}-{build_number}-Setup.exe"
    installer = output / filename
    removal = output / "uninstall-files.nsh"
    files = sorted(path.relative_to(app) for path in app.rglob("*") if path.is_file())
    directories = sorted((path.relative_to(app) for path in app.rglob("*") if path.is_dir()),
                         key=lambda path: len(path.parts), reverse=True)
    removal.write_text("\n".join(
        [f'Delete "$INSTDIR\\{str(path).replace(chr(47), chr(92))}"' for path in files] +
        [f'RMDir "$INSTDIR\\{str(path).replace(chr(47), chr(92))}"' for path in directories]) + "\n")
    definitions = {"APP_DIR": app, "OUTPUT_FILE": installer, "APP_VERSION": version,
                   "FILE_VERSION": ".".join((prefix.split(".") + ["0"] * 4)[:4]),
                   "BUILD_NUMBER": build_number, "CHANNEL": channel, "ARCH": architecture,
                   "ICON_FILE": icon, "UNINSTALL_FILES": removal, "LICENSE_FILE": ROOT / "LICENSE"}
    flag = "/" if sys.platform == "win32" else "-"
    run([compiler, flag + "V2", flag + "WX", flag + "INPUTCHARSET", "UTF8",
         *[f"{flag}D{key}={value}" for key, value in definitions.items()],
         str(ROOT / "packaging/windows/installer.nsi")])
    with installer.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    metadata.update({"fileName": filename, "sha256": digest, "size": installer.stat().st_size,
                     "url": f"https://aodjo.github.io/Reprise/windows/{channel}/{architecture}/packages/{filename}"})
    (output / "update.json").write_text(json.dumps(metadata, indent=2) + "\n")
    archive = output / f"Reprise-{version}-win-{architecture}-{build_number}.zip"
    with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
        for path in files:
            bundle.write(app / path, Path("Reprise") / path)
        bundle.write(ROOT / "LICENSE", "Reprise/LICENSE.txt")
    print(installer)
    return output


def main():
    """Build the requested process architecture, defaulting to Windows x64."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--architecture", choices=("x64", "arm64", "x86"), default="x64")
    parser.add_argument("--build-number", default=datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S"))
    parser.add_argument("--publish-only", action="store_true", help="Publish application files without requiring NSIS")
    args = parser.parse_args()
    try:
        build(args.architecture, args.build_number, args.publish_only)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"{error}\n")


if __name__ == "__main__":
    main()
