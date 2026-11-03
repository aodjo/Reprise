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
            index = source / 'dists/alpha/main/binary-amd64/Packages'
            index.parent.mkdir(parents=True)
            index.write_text('Package: reprise\nFilename: pool/main/reprise.deb\n')
            archived_linux = linux.with_name('archived.deb')
            archived_linux.write_bytes(b'archived Linux package')
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
            self.assertTrue(archived_linux.exists())
            self.assertFalse((staged / archived_linux.relative_to(source)).exists())
            for path, content in originals.items():
                self.assertEqual(content, path.read_bytes())
                output = staged / path.relative_to(source)
                if any(stamp in path.name for stamp in ('000002-Setup', '000003-Setup')):
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

    def test_capacity_pressure_preserves_every_active_channel_download(self):
        """A new release fits by dropping optional history without changing any source files."""
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            source = base / 'repo'
            originals = []
            active = []
            for channel in ('alpha', 'beta', 'release'):
                packages = source / f'windows/{channel}/x64/packages'
                packages.mkdir(parents=True)
                for stamp in ('20261101000000', '20261103000000'):
                    path = packages / f'Reprise-{channel}-{stamp}-Setup.exe'
                    path.write_bytes(b'x' * 100)
                    originals.append(path)
                active.append(path)
                (packages.parent / 'update.json').write_text(json.dumps({'fileName': path.name}))
            limit = sum(p.stat().st_size for p in source.rglob('*') if p.is_file()) - 150
            (base / 'runner').mkdir()
            result = self.stage(source, base / 'runner', limit=limit)
            self.assertEqual(0, result.returncode, result.stderr)
            staged = base / 'runner/reprise-pages'
            self.assertLessEqual(sum(p.stat().st_size for p in staged.rglob('*') if p.is_file()), limit)
            self.assertLess(len(list(staged.glob('windows/*/*/packages/*.exe'))), len(originals))
            for path in active:
                self.assertEqual(path.read_bytes(), (staged / path.relative_to(source)).read_bytes())
            for path in originals:
                self.assertEqual(b'x' * 100, path.read_bytes())

    def test_required_downloads_over_budget_fail_without_dropping_current(self):
        """An undersized site cannot hide the active installer to make publication appear valid."""
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            packages = base / 'repo/windows/release/x64/packages'
            packages.mkdir(parents=True)
            current = packages / 'Reprise-20261103000000-Setup.exe'
            current.write_bytes(b'x' * 100)
            (packages.parent / 'update.json').write_text(json.dumps({'fileName': current.name}))
            (base / 'runner').mkdir()
            result = self.stage(base / 'repo', base / 'runner', limit=10)
            self.assertNotEqual(0, result.returncode)
            self.assertIn('Pages content exceeds', result.stderr)
            self.assertTrue((base / 'runner/reprise-pages' / current.relative_to(base / 'repo')).is_file())
            self.assertEqual(b'x' * 100, current.read_bytes())

    @staticmethod
    def stage(source, runner, limit=None):
        """Execute the actual workflow, optionally reducing its budget to avoid gigabyte fixtures."""
        workflow = (ROOT / '.github/workflows/apt-pages.yml').read_text()
        code = textwrap.dedent(workflow.split("          python3 - <<'PY'\n", 1)[1].split('\n          PY', 1)[0])
        if limit is not None:
            code = code.replace('1024 ** 3', str(limit))
        return subprocess.run([sys.executable, '-c', code], cwd=source,
                              env={**os.environ, 'RUNNER_TEMP': str(runner)}, capture_output=True, text=True)
