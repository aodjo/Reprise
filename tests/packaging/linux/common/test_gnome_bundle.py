"""Exercise the bundled installer without changing the host GNOME session."""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


# Repository root, independent of the caller's working directory.
ROOT = Path(__file__).resolve().parents[4]


class GnomeBundleTests(unittest.TestCase):
    """Verify installation, upgrades, and existing GNOME preferences."""

    def setUp(self):
        """Create an isolated package, home directory, and GNOME command seams."""
        self.temporary = tempfile.TemporaryDirectory(prefix="reprise-package-")
        self.root = Path(self.temporary.name)
        self.app = self.root / "app"
        self.app.mkdir()
        self.bundle = self.app / "gnome-extension"
        shutil.copytree(ROOT / "packaging/linux/gnome-extension", self.bundle)
        self.helper = self.app / "install-gnome-extension.sh"
        shutil.copy2(ROOT / "packaging/linux/install-gnome-extension.sh", self.helper)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.settings = self.root / "enabled-extensions"
        self.settings.write_text("@as []")
        self.command("gnome-extensions", '#!/bin/sh\nexit "${FAKE_ENABLE_EXIT:-1}"\n')
        self.command("gsettings", '''#!/bin/sh
case "$1" in
    get) cat "$FAKE_SETTINGS" ;;
    set) printf '%s' "$4" > "$FAKE_SETTINGS" ;;
    *) exit 1 ;;
esac
''')
        self.env = {
            **os.environ,
            "PATH": f"{self.bin}:{os.environ['PATH']}",
            "HOME": str(self.root / "home"),
            "XDG_DATA_HOME": str(self.root / "data"),
            "FAKE_SETTINGS": str(self.settings),
            "FAKE_ENABLE_EXIT": "1",
        }
        self.target = self.root / "data/gnome-shell/extensions/reprise@junx.dev"

    def tearDown(self):
        """Remove only this test's temporary installation."""
        self.temporary.cleanup()

    def command(self, name, content):
        """Install a fake session command in the test's private PATH."""
        path = self.bin / name
        path.write_text(content)
        path.chmod(0o755)

    def install(self, *args, helper=None, check=True):
        """Run the real packaged helper against the isolated user directory."""
        return subprocess.run(
            ["bash", str(helper or self.helper), *args], env=self.env,
            text=True, capture_output=True, check=check,
        )

    def test_first_install_remembers_enable_for_next_session(self):
        """A shell that has not discovered the extension needs no manual enable."""
        self.install()
        self.assertEqual((self.bundle / "extension.js").read_bytes(),
                         (self.target / "extension.js").read_bytes())
        self.assertEqual("['reprise@junx.dev']", self.settings.read_text())

    def test_existing_extensions_are_preserved(self):
        """Enabling Reprise does not replace the user's other extensions."""
        self.settings.write_text("['another@example.org']")
        self.install()
        self.assertEqual("['another@example.org', 'reprise@junx.dev']", self.settings.read_text())
        self.install()
        self.assertEqual("['another@example.org', 'reprise@junx.dev']", self.settings.read_text())

    def test_same_version_update_replaces_old_script(self):
        """Content fixes arrive even when the extension version stays the same."""
        self.install()
        (self.target / "extension.js").write_text("old character slicing")
        self.env["FAKE_ENABLE_EXIT"] = "0"
        result = self.install()
        self.assertEqual((self.bundle / "extension.js").read_bytes(),
                         (self.target / "extension.js").read_bytes())
        self.assertIn("Log out and back in", result.stdout)

    def test_unchanged_files_are_not_rewritten(self):
        """Starting an unchanged build does not create another update."""
        self.install()
        script = self.target / "extension.js"
        os.utime(script, (1_700_000_000, 1_700_000_000))
        self.install()
        self.assertEqual(1_700_000_000, script.stat().st_mtime)

    def test_debian_uses_system_copy_without_user_override(self):
        """A fresh Debian install does not add a redundant per-user copy."""
        self.install("--system")
        self.assertFalse(self.target.exists())
        self.assertIn("reprise@junx.dev", self.settings.read_text())

    def test_debian_upgrade_refreshes_existing_user_override(self):
        """The package install hook replaces an old manually installed copy."""
        self.target.mkdir(parents=True)
        (self.target / "extension.js").write_text("old character slicing")
        self.install("--system")
        self.assertEqual((self.bundle / "extension.js").read_bytes(),
                         (self.target / "extension.js").read_bytes())

    def test_archive_helper_finds_bundle_inside_app(self):
        """The convenience helper in a tar archive uses the published files."""
        helper = self.root / self.helper.name
        shutil.copy2(self.helper, helper)
        self.install(helper=helper)
        self.assertTrue((self.target / "metadata.json").exists())

    def test_incomplete_bundle_does_not_damage_installed_copy(self):
        """A missing packaged file fails before touching a working installation."""
        self.install()
        old = (self.target / "metadata.json").read_bytes()
        (self.bundle / "metadata.json").write_text("new metadata")
        (self.bundle / "extension.js").unlink()
        result = self.install(check=False)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(old, (self.target / "metadata.json").read_bytes())


if __name__ == "__main__":
    unittest.main()
