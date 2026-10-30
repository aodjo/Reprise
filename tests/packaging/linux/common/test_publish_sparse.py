"""Verify that Linux publication preserves archived Windows installers without downloading them."""

import importlib.util
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

# Source checkout containing the publishing tool under test.
ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT / "packaging/linux"))
# Hyphenated command-line modules are loaded explicitly for protocol-level tests.
SPEC = importlib.util.spec_from_file_location("linux_publish_sparse", ROOT / "packaging/linux/publish-local.py")
PUBLISH = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PUBLISH)


@unittest.skipUnless(shutil.which("git"), "Git is required for sparse publication verification")
class SparsePublicationTests(unittest.TestCase):
    """Exercise real Git object filtering and staging against an isolated artifact repository."""

    def test_linux_updates_do_not_download_or_delete_windows_archives(self):
        """Excluded installer blobs stay in the committed tree after Linux's ordinary add-all staging."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            seed = root / "seed"
            seed.mkdir()

            def git(*arguments, cwd=seed):
                """Run Git without touching the user's source checkout or global configuration."""
                return subprocess.run(["git", *arguments], cwd=cwd, check=True, text=True,
                                      stdout=subprocess.PIPE, stderr=subprocess.PIPE).stdout.strip()

            git("init", "-b", "apt-repo")
            git("config", "user.name", "Reprise packaging test")
            git("config", "user.email", "test@example.invalid")
            git("config", "uploadpack.allowFilter", "true")
            windows = "windows/alpha/x64/packages/old-Setup.exe"
            for name, data in {
                windows: b"diagnostic installer bytes only",
                "windows/alpha/x64/update.json": b"{}",
                "windows/index.html": b"Windows downloads",
                "pool/alpha/old.deb": b"diagnostic package bytes only",
                "dists/alpha/Release": b"old index",
            }.items():
                path = seed / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
            git("add", "--all")
            git("commit", "-m", "test: seed artifact repository")
            installer_blob = git("rev-parse", f"HEAD:{windows}")
            site = root / "site"
            git("clone", "--no-local", "--depth", "1", "--filter=blob:none", "--sparse", "--branch", "apt-repo", str(seed), str(site), cwd=root)
            git("sparse-checkout", "set", "--no-cone", *PUBLISH.SPARSE_PATHS, cwd=site)
            self.assertFalse((site / windows).exists())
            self.assertTrue((site / "windows/alpha/x64/update.json").exists())
            self.assertTrue((site / "pool/alpha/old.deb").exists())
            self.assertIn("?" + installer_blob, git("rev-list", "--objects", "--missing=print", "HEAD", cwd=site))
            (site / "pool/alpha/new.deb").write_bytes(b"new diagnostic package")
            git("add", "--all", cwd=site)
            self.assertEqual("A\tpool/alpha/new.deb", git("diff", "--cached", "--name-status", cwd=site))
            self.assertEqual(installer_blob, git("rev-parse", f":{windows}", cwd=site))
