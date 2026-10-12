"""Run with python -I rust/tools/port_audit/features_test.py."""

import sys
sys.dont_write_bytecode = True
import collections
import importlib.util
import pathlib
import sqlite3
import tempfile
import unittest
from contextlib import closing

spec = importlib.util.spec_from_file_location("features", pathlib.Path(__file__).with_name("features.py"))
features = importlib.util.module_from_spec(spec)
spec.loader.exec_module(features)


class InventoryTests(unittest.TestCase):
    def test_reserved_packets_and_fixture_mismatch_are_distinguished(self):
        text = "pub mod cs { pub const CSExample: u16 = 0x001; pub const CSReserved: u16 = 0xfff; } pub mod sc {}"
        refs = collections.defaultdict(set)
        result = features.packets(text, "1\t0x002\tCSExample", refs, refs)
        self.assertTrue(result[0]["supported_1_2"])
        self.assertFalse(result[0]["fixture_opcode_matches"])
        self.assertFalse(result[1]["supported_1_2"])
        self.assertEqual(result[0]["behavior_verification"], "requires_review")

    def test_read_only_table_inventory_quotes_identifiers(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "compact.sqlite3"
            with closing(sqlite3.connect(path)) as conn:
                conn.executescript('CREATE TABLE "odd""name" (id INT); INSERT INTO "odd""name" VALUES (1);')
            before = features.sha256(path)
            refs = collections.defaultdict(set)
            tables = features.compact_tables(path, refs, refs, refs)
            self.assertEqual((tables[0]["name"], tables[0]["row_count"]), ('odd"name', 1))
            self.assertEqual(before, features.sha256(path))

    def test_test_only_mentions_do_not_count_as_runtime_use(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            source = root / "rust/crates/example/src"
            source.mkdir(parents=True)
            (source / "lib.rs").write_text('fn load() { let sql = "SELECT * FROM real_table"; }\n#[cfg(test)] mod tests { let x = "SELECT * FROM test_table"; }')
            words, queries, tests, _ = features.sources(root)
            self.assertTrue(queries["real_table"])
            self.assertFalse(words["test_table"])
            self.assertTrue(tests["test_table"])


if __name__ == "__main__":
    unittest.main()
