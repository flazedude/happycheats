"""Regression tests for optional installed-game evidence; no real game is used."""
from contextlib import redirect_stdout
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import validate


class InstalledEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='HappyCheats-Witcher-validation-')
        self.addCleanup(self.temporary.cleanup)
        self.game = Path(self.temporary.name) / 'game'
        self.executable = self.game / 'bin/x64_dx12/witcher3.exe'
        self.executable.parent.mkdir(parents=True)
        self.executable.write_bytes(b'fixture: an updated unsupported game executable')

    def test_absent_game_does_not_require_installed_api_evidence(self):
        output = io.StringIO()
        with redirect_stdout(output), patch.object(validate, 'validate_game') as compare:
            self.assertFalse(validate.validate_installed_evidence(None, {'GetEngineTime'}))
        compare.assert_not_called()
        self.assertIn('SKIP', output.getvalue())

    def test_auto_discovered_supported_executable_still_validates_apis(self):
        calls = {'GetEngineTime'}
        with patch.object(validate, 'sha', return_value=validate.EXE_HASH), patch.object(validate, 'validate_game') as compare:
            self.assertTrue(validate.validate_installed_evidence(self.game, calls))
        compare.assert_called_once_with(self.game, calls)

    def test_auto_discovered_unknown_executable_reports_skip_without_compatibility(self):
        output = io.StringIO()
        with redirect_stdout(output), patch.object(validate, 'validate_game') as compare:
            self.assertFalse(validate.validate_installed_evidence(self.game, {'GetEngineTime'}))
        compare.assert_not_called()
        self.assertIn('SKIP', output.getvalue())
        self.assertIn('no compatibility is established', output.getvalue())

    def test_stale_auto_discovery_without_executable_reports_skip(self):
        self.executable.unlink()
        output = io.StringIO()
        with redirect_stdout(output), patch.object(validate, 'validate_game') as compare:
            self.assertFalse(validate.validate_installed_evidence(self.game, set()))
        compare.assert_not_called()
        self.assertIn('executable is missing', output.getvalue())

    def test_explicit_unknown_executable_still_fails_the_existing_hash_pin(self):
        with self.assertRaisesRegex(ValueError, 'Installed executable differs from verified Steam build 25575366'):
            validate.validate_installed_evidence(self.game, {'GetEngineTime'}, explicit=True)

    def test_explicit_missing_executable_still_fails(self):
        self.executable.unlink()
        with self.assertRaises(FileNotFoundError):
            validate.validate_installed_evidence(self.game, set(), explicit=True)

    def test_api_validation_failure_is_not_downgraded_to_optional_evidence(self):
        with patch.object(validate, 'sha', return_value=validate.EXE_HASH), patch.object(validate, 'validate_game', side_effect=ValueError('Changed API signature')):
            with self.assertRaisesRegex(ValueError, 'Changed API signature'):
                validate.validate_installed_evidence(self.game, {'GetEngineTime'})

    def test_required_payload_integrity_fails_before_optional_game_discovery(self):
        root = Path(self.temporary.name) / 'workspace'
        source = root / 'modules/witcher-3/happycheats.ws'
        package = root / 'payload/witcher-3'
        source.parent.mkdir(parents=True)
        package.mkdir(parents=True)
        source.write_text('source fixture', encoding='utf-8')
        (package / 'happycheats.ws').write_text('stale payload fixture', encoding='utf-8')
        manifest = {'schemaVersion': 1, 'game': 'witcher-3', 'version': '0.1.1',
                    'files': [{'path': 'happycheats.ws', 'sha256': validate.sha(source)}]}
        (package / 'package.json').write_text(json.dumps(manifest), encoding='utf-8')
        with patch.object(validate, 'ROOT', root), patch.object(validate, 'inspect_source', return_value=set()), patch.object(validate, 'find_game') as discover, patch('sys.argv', ['validate.py']):
            with self.assertRaisesRegex(ValueError, 'Stale or damaged Witcher 3 payload'):
                validate.main()
        discover.assert_not_called()


if __name__ == '__main__':
    unittest.main()
