"""Exercise incremental apt publication with real Debian packaging tools."""

from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


# Source checkout containing the repository generator under test.
ROOT = Path(__file__).resolve().parents[4]
# Native tools required for these Linux-only integration checks.
TOOLS = ("dpkg-deb", "dpkg-scanpackages", "apt-ftparchive")


@unittest.skipUnless(all(shutil.which(tool) for tool in TOOLS), "Debian repository tools are required")
class AptRepoTests(unittest.TestCase):
    """Verify that an AMD64 update preserves existing packages and other channels."""

    def setUp(self):
        """Create isolated inputs and an existing two-architecture channel."""
        self.temporary = tempfile.TemporaryDirectory(prefix="reprise-apt-test-")
        self.root = Path(self.temporary.name)
        self.initial = self.root / "initial"
        self.incoming = self.root / "incoming"
        self.site = self.root / "site"
        self.initial.mkdir()
        self.incoming.mkdir()
        self.package(self.initial, "1.0~alpha.1", "amd64")
        self.arm = self.package(self.initial, "1.0~alpha.1", "arm64")
        self.build(self.initial)
        self.stable = self.site / "dists/stable/retained"
        self.stable.parent.mkdir(parents=True)
        self.stable.write_text("stable is unchanged")

    def tearDown(self):
        """Remove only this test's repository and package fixtures."""
        self.temporary.cleanup()

    def package(self, directory, version, architecture, name="reprise", payload="application"):
        """Build one real Debian fixture with the requested package metadata."""
        staging = self.root / f"staging-{name}-{version}-{architecture}"
        (staging / "DEBIAN").mkdir(parents=True, exist_ok=True)
        (staging / "DEBIAN/control").write_text(
            f"Package: {name}\nVersion: {version}\nArchitecture: {architecture}\n"
            "Maintainer: Test <test@example.invalid>\nDescription: Repository integration fixture\n"
        )
        (staging / "application").write_text(payload)
        output = directory / f"{name}_{version}_{architecture}.deb"
        subprocess.run(["dpkg-deb", "--root-owner-group", "--build", str(staging), str(output)],
                       check=True, capture_output=True)
        return output

    def build(self, packages, success=True, channel="develop"):
        """Run the real generator and assert its expected outcome."""
        result = subprocess.run(["bash", str(ROOT / "packaging/linux/build-apt-repo.sh"),
                                 str(packages), str(self.site), "https://example.invalid/Reprise", channel],
                                text=True, capture_output=True)
        if success:
            self.assertEqual(result.returncode, 0, result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0)

    def index(self, architecture, channel="develop"):
        """Read the native package index generated for one architecture."""
        return (self.site / f"dists/{channel}/main/binary-{architecture}/Packages").read_text()

    def test_amd64_update_preserves_arm64_and_cached_downloads(self):
        """Publishing AMD64 alone retains ARM64 and old files referenced by cached indexes."""
        package = self.package(self.incoming, "2.0~alpha.1", "amd64")
        self.build(self.incoming)
        self.assertIn("Version: 2.0~alpha.1\n", self.index("amd64"))
        self.assertIn("Version: 1.0~alpha.1\n", self.index("arm64"))
        pool = self.site / "pool/develop/main/r/reprise"
        self.assertTrue((pool / "reprise_1.0~alpha.1_amd64.deb").is_file())
        self.assertEqual((pool / self.arm.name).read_bytes(), self.arm.read_bytes())
        self.assertEqual((pool / package.name).read_bytes(), package.read_bytes())
        self.assertEqual(self.stable.read_text(), "stable is unchanged")

    def test_unrelated_package_leaves_current_repository_unchanged(self):
        """Validation rejects an unrelated app before replacing any index."""
        before = self.index("amd64")
        self.package(self.incoming, "2.0~alpha.1", "amd64", name="another-app")
        self.build(self.incoming, success=False)
        self.assertEqual(self.index("amd64"), before)
        self.assertFalse((self.site / "pool/develop/main/r/reprise/another-app_2.0~alpha.1_amd64.deb").exists())

    def test_reused_filename_cannot_replace_published_bytes(self):
        """A reused version with changed bytes fails before altering the published package."""
        before = self.index("amd64")
        pool = self.site / "pool/develop/main/r/reprise/reprise_1.0~alpha.1_amd64.deb"
        original = pool.read_bytes()
        self.package(self.incoming, "1.0~alpha.1", "amd64", payload="different application")
        self.build(self.incoming, success=False)
        self.assertEqual(self.index("amd64"), before)
        self.assertEqual(pool.read_bytes(), original)

    def test_version_ordering_is_debian_ordering(self):
        """A newer epoch beats a numerically larger upstream version during indexing."""
        self.package(self.incoming, "1:1.0~alpha.1", "amd64")
        self.package(self.incoming, "99.0~alpha.1", "amd64")
        self.build(self.incoming)
        self.assertIn("Version: 1:1.0~alpha.1\n", self.index("amd64"))
        self.assertNotIn("Version: 99.0~alpha.1\n", self.index("amd64"))

    def test_wrong_stage_is_rejected_before_any_index_changes(self):
        """A beta package cannot enter alpha, including the legacy develop directory."""
        before = self.index("amd64")
        self.package(self.incoming, "3.0~beta.1", "amd64")
        self.build(self.incoming, success=False)
        self.assertEqual(self.index("amd64"), before)

    def test_release_rejects_alpha_and_beta_packages(self):
        """Neither prerelease stage can be published in the release pool."""
        for stage in ("alpha", "beta"):
            with self.subTest(stage=stage):
                incoming = self.root / f"wrong-{stage}"
                incoming.mkdir()
                self.package(incoming, f"3.0~{stage}.1", "amd64")
                self.build(incoming, success=False, channel="release")
                self.assertFalse((self.site / "dists/release/Release").exists())

    def test_migration_keeps_stages_and_legacy_clients_separate(self):
        """Canonical indexes stay pure and existing develop clients receive the alpha migration."""
        self.package(self.incoming, "2.0~alpha.1", "amd64")
        self.prepare(self.incoming, "alpha")
        alpha = self.index("amd64", "alpha")
        self.assertIn("Version: 2.0~alpha.1\n", alpha)
        self.assertIn("pool/alpha/", alpha)
        self.assertIn("Version: 1.0~alpha.1\n", self.index("arm64", "alpha"))
        self.assertIn("Version: 2.0~alpha.1\n", self.index("amd64", "develop"))
        self.assertIn("pool/develop/", self.index("amd64", "develop"))
        self.assertEqual(self.index("amd64", "beta"), "")
        self.assertEqual(self.index("amd64", "release"), "")
        beta = self.root / "beta"
        beta.mkdir()
        self.package(beta, "3.0~beta.1", "amd64")
        self.prepare(beta, "beta")
        self.assertEqual(self.index("amd64", "alpha"), alpha)
        self.assertIn("Version: 3.0~beta.1\n", self.index("amd64", "beta"))
        self.assertEqual(self.index("amd64", "release"), "")
        self.assertNotIn("beta", self.index("amd64", "develop"))
        release = self.root / "release"
        release.mkdir()
        self.package(release, "3.0", "amd64")
        self.prepare(release, "release")
        self.assertIn("Version: 3.0\n", self.index("amd64", "release"))
        self.assertIn("Version: 3.0\n", self.index("amd64", "stable"))
        self.assertEqual(self.index("amd64", "alpha"), alpha)

    def prepare(self, packages, channel):
        """Run the same migration and alias generation used by local and CI publication."""
        result = subprocess.run(["python3", str(ROOT / "packaging/linux/prepare-update-repo.py"),
                                 str(packages), str(self.site), "https://example.invalid/Reprise", channel],
                                text=True, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == "__main__":
    unittest.main()
