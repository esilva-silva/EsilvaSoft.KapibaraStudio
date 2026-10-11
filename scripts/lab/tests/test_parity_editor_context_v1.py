import sys
from pathlib import Path
import tempfile
import unittest
from types import SimpleNamespace
from unittest.mock import patch

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
            parity.write_jsonl(path, records, root)
            self.assertEqual(parity.read_jsonl(path), records)
            self.assertEqual(list(path.parent.glob("*.tmp")), [])

    def test_artifact_guard_refuses_input_overwrite_and_linked_report(self):
        root = Path(__file__).resolve().parents[3]
        destination = root / "reports/lab/parity-guard-test"
        with self.assertRaises(ValueError):
            parity.validate_artifacts(destination, root, [destination / "render-py.jsonl"])
        linked = destination / "summary.md"
        with patch.object(Path, "is_symlink", lambda path: path == linked):
            with self.assertRaises(ValueError):
                parity.validate_artifacts(destination, root, [])

    def test_failed_atomic_replace_keeps_previous_report_and_cleans_temporary(self):
        temporary_root = Path(__file__).resolve().parents[3] / "tmp"
        temporary_root.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(dir=temporary_root) as temporary:
            root = Path(temporary)
            path = root / "reports/lab/summary.md"
            path.parent.mkdir(parents=True)
            path.write_text("anterior", encoding="utf-8")
            with patch.object(Path, "replace", side_effect=OSError("falha simulada")):
                with self.assertRaises(OSError):
                    parity.write_text_atomic(path, "novo", root)
            self.assertEqual(path.read_text(encoding="utf-8"), "anterior")
            self.assertEqual(list(path.parent.glob("*.tmp")), [])

    def test_wrapper_binds_nested_catalog_summary(self):
        self.assertEqual(kapilab._command_name(["catalog", "knowledge", "export"]), "catalog.knowledge.export")
        self.assertEqual(kapilab._command_name(("catalog", "knowledge", "check")), "catalog.knowledge.check")


if __name__ == "__main__":
    unittest.main()
