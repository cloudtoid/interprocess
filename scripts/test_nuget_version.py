import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

spec = importlib.util.spec_from_file_location('nuget_version', Path(__file__).with_name('nuget-version.py'))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class NuGetVersionTests(unittest.TestCase):
    def test_new_minor_resets_patch(self):
        self.assertEqual(release.next_version('3.1.0', ['3.0.211']), ('3.1.0', '3.0.211'))
        self.assertEqual(release.next_version('3.2.0', ['3.1.12']), ('3.2.0', '3.1.12'))

    def test_numeric_order_and_prereleases(self):
        self.assertEqual(release.next_version('3.1.0', ['3.1.9', '3.1.10', '3.2.0-alpha']),
                         ('3.1.11', '3.1.10'))

    def test_reject_version_rollback(self):
        with self.assertRaises(ValueError):
            release.next_version('3.1.0', ['3.2.0'])

    def run_plan(self, changed, commit='a' * 40):
        archive = io.BytesIO()
        with zipfile.ZipFile(archive, 'w') as package:
            package.writestr('Cloudtoid.Interprocess.nuspec',
                             f'<package xmlns="urn:nuget"><metadata><repository commit="{commit}"/></metadata></package>')
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'output'
            with patch.dict(os.environ, GITHUB_OUTPUT=str(output)), \
                 patch.object(release.urllib.request, 'urlopen', side_effect=[
                     io.BytesIO(json.dumps({'versions': ['3.0.211']}).encode()),
                     io.BytesIO(archive.getvalue())]), \
                 patch.object(release.subprocess, 'run') as ancestry, \
                 patch.object(release.subprocess, 'check_output', return_value=changed) as diff:
                release.main()
                ancestry.assert_called_once_with(['git', 'merge-base', '--is-ancestor', commit, 'HEAD'], check=True)
                self.assertEqual(diff.call_args.args[0][-len(release.INPUTS):], release.INPUTS)
            return output.read_text() if output.exists() else ''

    def test_unchanged_or_retry_skips(self):
        self.assertEqual(self.run_plan(''), '')

    def test_changed_package_publishes(self):
        self.assertEqual(self.run_plan('src/dotnet/Interprocess/Queue.cs\n'), 'version=3.1.0\n')

    def test_missing_source_commit_fails_closed(self):
        with self.assertRaises(ValueError):
            self.run_plan('changed', '')


if __name__ == '__main__':
    unittest.main()
