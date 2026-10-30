"""Validate release-stage inference before a publisher can mutate its staged repository."""

from pathlib import Path
import sys
import unittest


# Source checkout providing the publication classifier.
ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(ROOT / "packaging/linux"))

from update_channels import canonicalize, for_version, from_filename


class UpdateChannelTests(unittest.TestCase):
    """Keep development prereleases out of release and beta feeds."""

    def test_versions_select_exact_stages(self):
        """Semantic and Debian labels select the same canonical stage."""
        for version, expected in (
            ("2.0.0", "release"), ("1:2.0.0-1", "release"),
            ("2.0.0.20261029+20261029", "release"),
            ("2.0.0~alpha.1+20261029", "alpha"),
            ("2.0.0-alpha.1.20261029+sha", "alpha"),
            ("2.0.0~beta.2+20261029", "beta"),
            ("2.0.0-beta.2.20261029", "beta"),
        ):
            with self.subTest(version=version):
                self.assertEqual(for_version(version), expected)

    def test_unknown_prereleases_fail_closed(self):
        """Unsupported dev, RC, or ambiguous labels are never treated as stable."""
        for version in ("", " ", "2.0.0~rc.1", "2.0.0-dev.1", "2.0.0~alphabeta.1"):
            with self.subTest(version=version), self.assertRaises(ValueError):
                for_version(version)

    def test_legacy_names_alias_only_one_stage(self):
        """Historical develop means alpha rather than a shared prerelease channel."""
        self.assertEqual(canonicalize("develop"), "alpha")
        self.assertEqual(canonicalize("stable"), "release")
        self.assertEqual(canonicalize("beta"), "beta")
        with self.assertRaises(ValueError):
            canonicalize("dev")

    def test_filenames_infer_the_package_stage(self):
        """Numbered package names retain the release stage used for native validation."""
        self.assertEqual(from_filename("reprise_2.0.0~alpha.1.20261029+20261029_amd64.deb"), "alpha")
        self.assertEqual(from_filename("reprise_2.0.0~beta.1.20261029+20261029_arm64.deb"), "beta")
        self.assertEqual(from_filename("reprise_2.0.0.20261029+20261029_amd64.deb"), "release")


if __name__ == "__main__":
    unittest.main()
