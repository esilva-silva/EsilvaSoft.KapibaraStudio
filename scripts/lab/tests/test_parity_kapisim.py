import copy
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import parity_kapisim


class ParityKapiSimTests(unittest.TestCase):
    def cases(self):
        return [{"id": f"case-{i}", "status": "Denied", "errorCode": "PermissionDenied",
                 "toolResponse": '{"status":"Denied","result":"PermissionDenied","error":"PermissionDenied","truncated":false}'}
                for i in range(500)]

    def test_permission_extra_fails_even_above_percentage_gate(self):
        real = self.cases()
        simulator = copy.deepcopy(real)
        simulator[0]["status"] = "Succeeded"
        report = parity_kapisim.compare(real, simulator)
        self.assertEqual(report["agreement"], .998)
        self.assertFalse(report["passed"])
        self.assertEqual(report["permissionViolations"], ["case-0"])

    def test_missing_duplicate_and_extra_ids_are_refused(self):
        real = self.cases()
        with self.assertRaises(ValueError):
            parity_kapisim.compare(real, real[:-1])
        simulator = copy.deepcopy(real)
        simulator[-1]["id"] = simulator[0]["id"]
        with self.assertRaises(ValueError):
            parity_kapisim.compare(real, simulator)

    def test_envelope_cannot_disguise_an_extra_permission(self):
        real = self.cases()
        simulator = copy.deepcopy(real)
        simulator[0]["toolResponse"] = simulator[0]["toolResponse"].replace('"status":"Denied"', '"status":"Succeeded"')
        self.assertFalse(parity_kapisim.compare(real, simulator)["passed"])

    def test_empty_filtered_success_cannot_release_extra_data(self):
        real = self.cases()
        real[0].update(status="Succeeded", errorCode=None, scenario="permission_missing",
            toolResponse='{"status":"Succeeded","result":"{\\"connections\\":[]}","error":null,"truncated":false}')
        simulator = copy.deepcopy(real)
        simulator[0]["toolResponse"] = real[0]["toolResponse"].replace('[]', '[1]')
        report = parity_kapisim.compare(real, simulator)
        self.assertEqual(report["agreement"], .998)
        self.assertEqual(report["permissionViolations"], ["case-0"])
        self.assertFalse(report["passed"])

    def test_identical_calls_pass_and_result_changes_fail(self):
        real = self.cases()
        self.assertTrue(parity_kapisim.compare(real, real)["passed"])
        simulator = copy.deepcopy(real)
        for case in simulator[:6]:
            case["toolResponse"] = case["toolResponse"].replace('"truncated":false', '"truncated":true')
        self.assertFalse(parity_kapisim.compare(real, simulator)["passed"])

    def test_volatile_fields_do_not_hide_document_values(self):
        a = '{"status":"Succeeded","result":"{\\"executionId\\":\\"a\\",\\"results\\":[\\"42\\"]}","error":null,"truncated":false}'
        self.assertEqual(parity_kapisim.normalize(a), parity_kapisim.normalize(a.replace('\\"a\\"', '\\"b\\"')))
        self.assertNotEqual(parity_kapisim.normalize(a), parity_kapisim.normalize(a.replace('42', '43')))


if __name__ == "__main__":
    unittest.main()
