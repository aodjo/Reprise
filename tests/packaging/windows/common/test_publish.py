"""Check Windows publication ordering and architecture isolation without publishing."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[4]
SPEC = importlib.util.spec_from_file_location('windows_publish', ROOT / 'packaging/windows/publish.py')
PUBLISH = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PUBLISH)


class PublicationOrderTests(unittest.TestCase):
    """Prevent newer timestamps from replacing a higher semantic version."""

    def test_semantic_version_precedes_build_timestamp(self):
        """A rebuilt older version cannot replace the current version."""
        self.assertGreater(PUBLISH.version_order('2.1.0-alpha.1', '20261028000000'),
                           PUBLISH.version_order('2.0.0-alpha.9', '20261029000000'))

    def test_numeric_prerelease_order(self):
        """Prerelease components compare numerically rather than lexically."""
        self.assertGreater(PUBLISH.version_order('2.0.0-alpha.10', '20261028000000'),
                           PUBLISH.version_order('2.0.0-alpha.9', '20261029000000'))

    def test_stable_rebuild_order(self):
        """Equal stable versions can still receive a later build."""
        self.assertGreater(PUBLISH.version_order('2.0.0', '20261029000000'),
                           PUBLISH.version_order('2.0.0', '20261028000000'))

    def test_unknown_prerelease_fails_closed(self):
        """Unrecognized prerelease stages never fall back to release."""
        with self.assertRaises(ValueError):
            PUBLISH.version_order('2.0.0-preview.1', '20261029000000')


class SparsePublicationTests(unittest.TestCase):
    """Exercise real Git staging so sparse publication cannot delete old Windows or Linux packages."""

    def test_sparse_publication_preserves_archives_and_materializes_requested_files(self):
        """Only requested installers enter the working tree, while committed archives survive the new publication."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)

            def git(*arguments):
                """Run Git only inside this disposable package repository."""
                return subprocess.check_output(['git', '-C', str(root), *arguments], text=True).strip()

            git('init', '--quiet')
            git('config', 'user.name', 'Publication test')
            git('config', 'user.email', 'publication@example.invalid')
            feed = root / 'windows/alpha/x64'
            (feed / 'packages').mkdir(parents=True)
            archive = feed / 'packages/old.exe'
            archive.write_bytes(b'preserved Windows archive')
            (feed / 'update.json').write_text('{}')
            (root / 'windows/index.html').write_text('old index')
            linux = root / 'pool/old.deb'
            linux.parent.mkdir()
            linux.write_bytes(b'preserved Linux archive')
            git('add', '--all')
            git('commit', '--quiet', '-m', 'test: seed package archives')
            metadata = {'channel': 'alpha', 'architecture': 'x64', 'fileName': 'next.exe'}
            git('sparse-checkout', 'set', '--no-cone', *PUBLISH.sparse_paths([metadata]))
            self.assertFalse(archive.exists())
            self.assertFalse(linux.exists())
            self.assertTrue((feed / 'update.json').exists())
            (feed / 'packages').mkdir(exist_ok=True)
            candidate = feed / 'packages/next.exe'
            candidate.write_bytes(b'new installer')
            (feed / 'update.json').write_text(json.dumps(metadata))
            git('add', '--', 'windows/alpha/x64/update.json', 'windows/alpha/x64/packages/next.exe')
            self.assertEqual(set(git('diff', '--cached', '--name-status').splitlines()),
                             {'A\twindows/alpha/x64/packages/next.exe', 'M\twindows/alpha/x64/update.json'})
            git('commit', '--quiet', '-m', 'test: publish sparse installer')
            self.assertEqual(git('show', 'HEAD:windows/alpha/x64/packages/old.exe'), 'preserved Windows archive')
            self.assertEqual(git('show', 'HEAD:pool/old.deb'), 'preserved Linux archive')
            git('sparse-checkout', 'set', '--no-cone', *PUBLISH.sparse_paths([metadata]))
            self.assertEqual(candidate.read_bytes(), b'new installer')
            archived_request = dict(metadata, fileName='old.exe')
            git('sparse-checkout', 'set', '--no-cone', *PUBLISH.sparse_paths([archived_request]))
            self.assertEqual(archive.read_bytes(), b'preserved Windows archive')


if __name__ == '__main__':
    unittest.main()
