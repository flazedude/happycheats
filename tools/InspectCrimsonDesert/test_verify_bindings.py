"""Focused source-parser and file-mapping checks; no game files are required."""
from pathlib import Path
import tempfile
import unittest

from verify_bindings import byte_arrays, map_rva, source_guards


class BindingVerifierTests(unittest.TestCase):
    def test_initializer_requires_exact_literal_bytes(self):
        self.assertEqual(byte_arrays('const std::array<unsigned char,2> x{{0x48, 139}};'), {'x': b'\x48\x8b'})
        for values in ('1', '1,2,3', '1,256', '1,constant'):
            with self.subTest(values=values), self.assertRaises(ValueError):
                byte_arrays('const std::array<unsigned char,2> x{{' + values + '}};')

    def test_mapping_requires_unique_complete_raw_bytes(self):
        section = ('code', 0x1000, 0x300, 0x200, 0x200)
        self.assertEqual(map_rva([section], 0x1010, 0x10, 0x400), (0x210, 'code'))
        for sections, rva, size, file_size in (
            ([section], 0x11ff, 2, 0x400),  # Would read virtual zero-fill.
            ([section], 0x1010, 0x10, 0x218),  # Truncated file.
            ([section, section], 0x1010, 0x10, 0x400),  # Ambiguous sections.
            ([section, ('overlap', 0x1018, 8, 0x300, 8)], 0x1010, 0x10, 0x400),
            ([section], 0x0fff, 1, 0x400),
            ([section], -1, 1, 0x400),
            ([section], 0x1010, 0, 0x400),
        ):
            with self.subTest(rva=rva, size=size), self.assertRaises(ValueError):
                map_rva(sections, rva, size, file_size)

    def test_native_guards_parse_and_unrecognized_changes_fail_closed(self):
        root = Path(__file__).resolve().parents[2]
        expected, bindings, inputs = source_guards(root)
        self.assertEqual(set(expected), {'machine', 'timestamp', 'image_size', 'sha256'})
        self.assertTrue(bindings)
        slots = {name: (rva, target) for _, name, rva, target in bindings if isinstance(target, int)}
        self.assertEqual(slots, {
            'ClientOwnerVtableRva.local_control_slot': (0x59056D8 + 0x178, 0x5B20D0),
            'ClientChildOwnerVtableRva.local_control_slot': (0x5905BE0 + 0x178, 0x5B20D0)})
        source_path = root / 'modules/crimson-desert/native/trainer.cpp'
        original = inputs[source_path].decode('utf-8-sig')
        mutations = (
            original + '\nbool Extra() { return Matches(g_game+1, ignored); }\n',
            original.replace('bool ValidateBaseBindings() {', 'bool ValidateBaseBindings() {\nconst std::array<unsigned char,1> unused{{1}};', 1),
            original.replace('bool ValidateBaseBindings()', 'bool MissingValidator()', 1),
            original.replace('Matches(g_game+', 'Matches(g_game+UnsupportedNamespace::', 1),
            original.replace('Pointer(g_game+crimson_logic::ClientChildOwnerVtableRva+0x178,childLocalControlTarget)', 'true', 1),
        )
        with tempfile.TemporaryDirectory() as directory:
            copied = Path(directory)
            for path, value in inputs.items():
                target = copied / path.relative_to(root)
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(value)
            for index, mutation in enumerate(mutations):
                (copied / source_path.relative_to(root)).write_text(mutation, encoding='utf-8')
                with self.subTest(mutation=index), self.assertRaises(ValueError):
                    source_guards(copied)


if __name__ == '__main__':
    unittest.main()
