import sys
from pathlib import Path
import tempfile
import unittest
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import kapilab
import parity_editor_context_v1 as parity


class ParityPreparationTests(unittest.TestCase):
    def test_mapping_preserves_crlf_and_editor_context(self):
        record = {"id": "fixture", "type": "fim", "prefix": "ação\r\n", "suffix": "😀",
                  "context": {"language": "json", "known_names": ["collection"]}}
        mapped, example = parity.map_record(record, SimpleNamespace(LANGUAGE_CONSOLE="console"))
        self.assertEqual(mapped["prefix"], "ação\r\n")
        self.assertEqual(mapped["suffix"], "😀")
        self.assertEqual(mapped["editor"], example["context"])
        self.assertEqual(mapped["language"], "json")
        self.assertEqual(mapped["editor"]["newline"], "\r\n")

    def test_mapping_refuses_different_contract_and_materializes_crlf(self):
        reference = SimpleNamespace(LANGUAGE_CONSOLE="console")
        with self.assertRaises(ValueError):
            parity.map_record({"type": "chat"}, reference)
        mapped, _ = parity.map_record({"id": "x", "type": "fim", "prefix": "", "suffix": "", "context": {"newline": "\n"}}, reference)
        self.assertEqual(mapped["editor"]["newline"], "\r\n")

    def test_output_guard_and_atomic_jsonl_roundtrip(self):
        temporary_root = Path(__file__).resolve().parents[3] / "tmp"
        temporary_root.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=temporary_root) as temporary:
            root = Path(temporary)
            with self.assertRaises(ValueError):
                parity.safe_path(root / "outside.jsonl", root, output=True)
            with self.assertRaises(ValueError):
                parity.safe_path(root / "data/eval/blind/test.jsonl", root)
            path = root / "reports/lab/render.jsonl"
            path.parent.mkdir(parents=True)
            parity.safe_path(path, root, output=True)
            records = [{"id": "x", "value": "á\r\n😀"}]
            parity.write_jsonl(path, records)
            self.assertEqual(parity.read_jsonl(path), records)
            self.assertFalse(path.with_suffix(".jsonl.tmp").exists())

    def test_wrapper_binds_nested_catalog_summary(self):
        self.assertEqual(kapilab._command_name(["catalog", "knowledge", "export"]), "catalog.knowledge.export")
        self.assertEqual(kapilab._command_name(("catalog", "knowledge", "check")), "catalog.knowledge.check")


if __name__ == "__main__":
    unittest.main()
