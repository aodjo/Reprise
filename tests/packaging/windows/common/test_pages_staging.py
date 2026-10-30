"""Exercise the actual Pages workflow staging code against isolated package trees."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import textwrap
import unittest

ROOT = Path(__file__).resolve().parents[4]


class PagesStagingTests(unittest.TestCase):
    """Keep active downloads and Linux content safe while retaining original package history."""

    def test_only_staged_old_windows_downloads_are_pruned(self):
        """Every channel stays separate, the active feed wins, and source/Linux bytes are preserved."""
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            source = base / 'repo'
            source.mkdir()
            (base / 'runner').mkdir()
            linux = source / 'pool/main/reprise.deb'
            linux.parent.mkdir(parents=True)
            linux.write_bytes(b'unchanged Linux package')
            originals = {}
            for channel in ('alpha', 'beta', 'release'):
                for arch in ('x64', 'arm64'):
                    packages = source / f'windows/{channel}/{arch}/packages'
                    packages.mkdir(parents=True)
                    for number in range(1, 6):
                        name = f'Reprise-{channel}-{arch}-2026102900000{number}-Setup.exe'
                        (packages / name).write_bytes(name.encode())
                        originals[packages / name] = name.encode()
                    # Protect a current feed even when its timestamp is not among the newest three.
                    name = f'Reprise-{channel}-{arch}-20261029000001-Setup.exe'
                    (packages.parent / 'update.json').write_text(json.dumps({'fileName': name}))
            result = self.stage(source, base / 'runner')
            self.assertEqual(0, result.returncode, result.stderr)
            staged = base / 'runner/reprise-pages'
            self.assertEqual(linux.read_bytes(), (staged / linux.relative_to(source)).read_bytes())
            for path, content in originals.items():
                self.assertEqual(content, path.read_bytes())
                output = staged / path.relative_to(source)
                if '000002-Setup' in path.name:
                    self.assertFalse(output.exists())
                else:
                    self.assertEqual(content, output.read_bytes())

    def test_missing_current_download_fails_before_publication(self):
        """A corrupt manifest cannot silently remove or publish an unavailable active installer."""
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            packages = base / 'repo/windows/alpha/x64/packages'
            packages.mkdir(parents=True)
            (packages.parent / 'update.json').write_text(json.dumps({'fileName': 'missing-Setup.exe'}))
            (base / 'runner').mkdir()
            result = self.stage(base / 'repo', base / 'runner')
            self.assertNotEqual(0, result.returncode)
            self.assertIn('Current Windows download is missing', result.stderr)

    @staticmethod
    def stage(source, runner):
        """Execute the workflow's actual embedded Python with a task-owned runner directory."""
        workflow = (ROOT / '.github/workflows/apt-pages.yml').read_text()
        code = textwrap.dedent(workflow.split("          python3 - <<'PY'\n", 1)[1].split('\n          PY', 1)[0])
        return subprocess.run([sys.executable, '-c', code], cwd=source,
                              env={**os.environ, 'RUNNER_TEMP': str(runner)}, capture_output=True, text=True)
