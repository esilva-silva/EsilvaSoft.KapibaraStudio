from __future__ import annotations

import json
import hashlib
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from scripts.lab import kapilab


def summary_line(exit_code: int, command: str) -> str:
    return json.dumps({"cmd": command, "exit": exit_code, "run_id": "test-run", "out": "-"})


class KapiLabWrapperTests(unittest.TestCase):
    def test_distribution_lock_verifies_the_published_tree(self) -> None:
        with tempfile.TemporaryDirectory(prefix="kapilab-lock-", dir=kapilab.REPO_ROOT) as temporary:
            directory = Path(temporary) / "publish"
            directory.mkdir()
            executable = directory / "KapiLab.exe"
            dependency = directory / "KapiLab.dll"
            executable.write_bytes(b"apphost")
            dependency.write_bytes(b"published assembly")
            lock = Path(temporary) / "kapilab.lock.json"
            entry = {
                "backend": "Cpu",
                "rid": "win-x64",
                "executable": "publish/KapiLab.exe",
                "sha256": hashlib.sha256(executable.read_bytes()).hexdigest(),
                "contentSha256": kapilab._published_tree_sha256(directory),
                "genAiVersion": "0.15.2",
                "ideCommit": "test-commit",
            }
            lock.write_text(
                json.dumps({"schema": "kapilab-distribution-lock-v1", "entries": [entry]}),
                encoding="utf-8",
            )
            with patch.object(kapilab, "DISTRIBUTION_LOCK", lock):
                resolved, _ = kapilab._resolve_published("Cpu")
                self.assertEqual(resolved, executable)
                dependency.write_bytes(b"tampered assembly")
                with self.assertRaises(kapilab.KapiLabLockError):
                    kapilab._resolve_published("Cpu")

    def test_invokes_without_shell_and_appends_workspace(self) -> None:
        completed = kapilab.subprocess.CompletedProcess(
            args=[], returncode=0, stdout='{"schema":"example"}\n', stderr=summary_line(0, "env")
        )
        with patch.object(kapilab.subprocess, "run", return_value=completed) as invoke:
            result = kapilab.run("env", workspace="workspace", timeout=12)

        self.assertEqual(result[0], 0)
        self.assertEqual(json.loads(result[1])["schema"], "example")
        self.assertEqual(result[2]["run_id"], "test-run")
        kwargs = invoke.call_args.kwargs
        self.assertFalse(kwargs.get("shell", False))
        command = invoke.call_args.args[0]
        self.assertEqual(command[-2:], ["--workspace", str(Path("workspace").resolve())])
        self.assertTrue(Path(command[0]).is_absolute())
        self.assertEqual(Path(command[0]).name, "EsilvaSoft.KapibaraStudio.KapiLab.exe")

    def test_maps_privacy_contract_and_gate_codes_to_typed_errors(self) -> None:
        for exit_code, expected_type in (
            (6, kapilab.GateFailed),
            (7, kapilab.ContractDrift),
            (8, kapilab.PrivacyViolation),
            (9, kapilab.GpuBusy),
        ):
            completed = kapilab.subprocess.CompletedProcess(
                args=[], returncode=exit_code, stdout="", stderr=summary_line(exit_code, "catalog.check")
            )
            with self.subTest(exit_code=exit_code), patch.object(kapilab.subprocess, "run", return_value=completed):
                with self.assertRaises(expected_type) as raised:
                    kapilab.run("catalog", "check")
                self.assertEqual(raised.exception.returncode, exit_code)

    def test_expected_negative_code_is_returned_without_exception(self) -> None:
        completed = kapilab.subprocess.CompletedProcess(
            args=[], returncode=8, stdout="", stderr=summary_line(8, "catalog.check")
        )
        with patch.object(kapilab.subprocess, "run", return_value=completed):
            returncode, stdout, summary = kapilab.run("catalog", "check", expect=8)
        self.assertEqual(returncode, 8)
        self.assertEqual(stdout, "")
        self.assertEqual(summary["exit"], 8)

    def test_backend_mismatch_fails_before_requested_command(self) -> None:
        completed = kapilab.subprocess.CompletedProcess(
            args=[], returncode=7, stdout="{}\n", stderr=summary_line(7, "env")
        )
        with patch.object(kapilab.subprocess, "run", return_value=completed) as invoke:
            with self.assertRaises(kapilab.ContractDrift):
                kapilab.run("catalog", "plans", expected_backend="Cpu")
        self.assertIn("env", invoke.call_args.args[0])
        self.assertIn("--expect-backend", invoke.call_args.args[0])

    def test_child_environment_is_allowlisted_and_summary_is_bound_to_command(self) -> None:
        completed = kapilab.subprocess.CompletedProcess(
            args=[], returncode=0, stdout="{}\n", stderr=summary_line(0, "catalog.plans")
        )
        dotnet = kapilab._resolve_dotnet(None)
        with patch.dict(kapilab.os.environ, {"UNRELATED_SECRET": "not-forwarded"}):
            with patch.object(kapilab.subprocess, "run", return_value=completed) as invoke:
                kapilab.run(
                    "catalog", "plans", dotnet_executable=dotnet,
                    environment={"KAPILAB_MONGO_URI": "mongodb://127.0.0.1/test"},
                )
        child_environment = invoke.call_args.kwargs["env"]
        self.assertNotIn("UNRELATED_SECRET", child_environment)
        self.assertEqual(child_environment["KAPILAB_MONGO_URI"], "mongodb://127.0.0.1/test")
        self.assertTrue(Path(invoke.call_args.args[0][0]).is_absolute())

        with self.assertRaises(ValueError):
            kapilab.run("env", environment={"UNRELATED_SECRET": "must-not-pass"}, dotnet_executable=dotnet)
        for remote_uri in (
            "mongodb://example.com/test",
            "mongodb+srv://localhost/test",
            "mongodb://127.0.0.1:27017,example.com:27017/test",
        ):
            with self.subTest(uri=remote_uri), self.assertRaises(ValueError):
                kapilab.run("env", environment={"KAPILAB_MONGO_URI": remote_uri}, dotnet_executable=dotnet)

        mismatched = kapilab.subprocess.CompletedProcess(
            args=[], returncode=0, stdout="{}\n", stderr=summary_line(0, "catalog.plans")
        )
        with patch.object(kapilab.subprocess, "run", return_value=mismatched):
            with self.assertRaises(kapilab.KapiLabProtocolError):
                kapilab.run("env", dotnet_executable=dotnet)

    def test_timeout_has_distinct_error(self) -> None:
        with patch.object(kapilab.subprocess, "run", side_effect=kapilab.subprocess.TimeoutExpired("dotnet", 1)):
            with self.assertRaises(kapilab.KapiLabTimeout):
                kapilab.run("env", timeout=1)
        for invalid in (float("nan"), float("inf"), float("-inf"), True, 0, -1):
            with self.subTest(timeout=invalid), self.assertRaises(ValueError):
                kapilab.run("env", timeout=invalid)

    def test_matrix_run_command_has_a_bound_wrapper_name(self) -> None:
        completed = kapilab.subprocess.CompletedProcess(
            args=[], returncode=0, stdout='{"schema":"kapilab-process-matrix-v1"}\n',
            stderr=summary_line(0, "matrix.run"),
        )
        with patch.object(kapilab.subprocess, "run", return_value=completed) as invoke:
            returncode, stdout, summary = kapilab.run("matrix", "run", dotnet_executable=kapilab._resolve_dotnet(None))
        self.assertEqual(returncode, 0)
        self.assertEqual(summary["cmd"], "matrix.run")
        self.assertEqual(json.loads(stdout)["schema"], "kapilab-process-matrix-v1")
        self.assertIn("matrix", invoke.call_args.args[0])

    def test_published_matrix_run_executes_explicit_child_and_validates_report(self) -> None:
        if os.name != "nt":
            self.skipTest("The checked-in published KapiLab lock currently targets win-x64.")
        child = kapilab.REPO_ROOT / "tools/KapiLab.Tests/bin/Debug/net10.0/EsilvaSoft.KapibaraStudio.KapiLab.GpuLockChild.exe"
        if not child.is_file():
            self.skipTest("Build KapiLab.Tests to enable subprocess matrix coverage.")

        environment_names = ["SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "PATH", "DOTNET_ROOT", "DOTNET_ROOT_X64"]
        case = {
            "id": "fixture-success",
            "executable": str(child),
            "arguments": ["matrix-success"],
            "workingDirectory": ".",
            "timeoutSeconds": 10,
            "environmentAllowlist": environment_names,
            "environment": {},
        }
        with tempfile.TemporaryDirectory(prefix="kapilab-matrix-wrapper-", dir=kapilab.REPO_ROOT) as temporary:
            workspace = Path(temporary)
            input_file = workspace / "data" / "lab" / "matrix.json"
            input_file.parent.mkdir(parents=True)
            input_file.write_text(json.dumps({"schema": "kapilab-process-matrix-v1", "cases": [case]}), encoding="utf-8")
            returncode, stdout, summary = kapilab.run(
                "matrix", "run", "--in", "data/lab/matrix.json", backend="Cpu", workspace=workspace, timeout=30,
            )

        report = json.loads(stdout)
        self.assertEqual(returncode, 0)
        self.assertEqual(summary["cmd"], "matrix.run")
        self.assertEqual(report["schema"], "kapilab-process-matrix-v1")
        self.assertTrue(report["complete"])
        self.assertEqual(len(report["cases"]), 1)
        self.assertEqual(report["cases"][0]["id"], "fixture-success")
        self.assertEqual(report["cases"][0]["status"], "succeeded")
        self.assertEqual(report["cases"][0]["exitCode"], 0)
        self.assertGreaterEqual(report["cases"][0]["durationMs"], 0)

    def test_published_bench_matrix_runs_cells_and_validates_partial_report(self) -> None:
        if os.name != "nt":
            self.skipTest("The checked-in published KapiLab lock currently targets win-x64.")
        published = kapilab.REPO_ROOT / "artifacts/f7b/publish/Cpu/win-x64/EsilvaSoft.KapibaraStudio.KapiLab.exe"
        if not published.is_file():
            self.skipTest("Publish KapiLab into artifacts/f7b to enable subprocess integration coverage.")
        kapilab._resolve_published("Cpu")

        with tempfile.TemporaryDirectory(prefix="kapilab-bench-matrix-wrapper-", dir=kapilab.REPO_ROOT) as temporary:
            workspace = Path(temporary)
            input_path = workspace / "data" / "lab" / "records.jsonl"
            matrix_path = workspace / "data" / "lab" / "matrix.json"
            input_path.parent.mkdir(parents=True)
            input_path.write_text('{"prefix":"x","suffix":""}\n', encoding="utf-8")
            matrix_path.write_text(json.dumps({"cells": [
                {"package": "data/lab/missing-a", "hardware": "cpu", "scenario": "synthetic"},
                {"package": "data/lab/missing-b", "hardware": "cpu", "scenario": "synthetic"},
            ]}), encoding="utf-8")
            returncode, stdout, summary = kapilab.run(
                "bench", "autocomplete", "--matrix", "data/lab/matrix.json", "--in", "data/lab/records.jsonl",
                "--out", "reports/lab/bench-matrix-report.json", backend="Cpu", workspace=workspace, timeout=60, expect=3,
            )
            report = json.loads((workspace / "reports" / "lab" / "bench-matrix-report.json").read_text(encoding="utf-8"))

        summary_report = json.loads(stdout)
        self.assertEqual(returncode, 3)
        self.assertEqual(summary["cmd"], "bench.autocomplete")
        self.assertEqual(summary_report["schema"], "kapilab-bench-matrix-v1")
        self.assertTrue(report["complete"])
        self.assertEqual([cell["id"] for cell in report["cells"]], ["cell-01", "cell-02"])
        self.assertTrue(all(cell["status"] == "failed" and cell["exitCode"] == 3 for cell in report["cells"]))

    @unittest.skipUnless(
        (kapilab.REPO_ROOT / "tools/KapiLab/bin/Debug/net10.0/EsilvaSoft.KapibaraStudio.KapiLab.dll").is_file(),
        "Build KapiLab first to enable subprocess integration coverage.",
    )
    def test_integration_runs_two_hundred_plan_records_and_validates_schema(self) -> None:
        modes = ("Planning", "Agent", "Automatic", "AskConfirmations")
        cases = [
            {
                "id": f"case-{index:03d}",
                "mode": modes[index % len(modes)],
                "permissions": {"providerId": "wrapper-test"},
                "hasWorkspaceFolder": index % 3 != 0,
                "productToolsAvailable": index % 4 != 3,
                "nativeToolsAvailable": False,
                "requireExternalDestinationConsent": index == 0,
            }
            for index in range(200)
        ]
        with tempfile.TemporaryDirectory(prefix="kapilab-wrapper-", dir=kapilab.REPO_ROOT) as temporary:
            workspace = Path(temporary)
            input_file = workspace / "data" / "lab" / "cases.json"
            input_file.parent.mkdir(parents=True)
            input_file.write_text(
                json.dumps({"schema": "kapilab-agent-plan-cases-v1", "cases": cases}),
                encoding="utf-8",
            )
            _, environment_json, _ = kapilab.run("env")
            environment = json.loads(environment_json)
            returncode, stdout, summary = kapilab.run(
                "catalog", "plans", "--in", "data/lab/cases.json", workspace=workspace, timeout=60,
                expected_backend=environment["backend"], expected_genai=environment["genAiVersion"],
            )

        output = json.loads(stdout)
        self.assertEqual(returncode, 0)
        self.assertEqual(summary["cmd"], "catalog.plans")
        self.assertEqual(output["schema"], "kapilab-agent-plan-snapshot-v1")
        self.assertEqual(output["evidenceKind"], "synthetic-policy-input")
        self.assertFalse(output["toolInvocationPerformed"])
        self.assertEqual(len(output["cases"]), 200)
        self.assertEqual({item["mode"] for item in output["cases"]}, set(modes))
        self.assertTrue(any(item["blockReason"] == "ConsentMissing" for item in output["cases"]))
        self.assertTrue(any(item["proposalHandling"] == "ReviewRequired" for item in output["cases"]))
        self.assertTrue(any(item["proposalHandling"] == "AutoApplyToBuffer" for item in output["cases"]))
        self.assertTrue(any(item["productTools"] for item in output["cases"]))
        self.assertTrue(any(not item["productTools"] for item in output["cases"]))

        sample_code, sample_stdout, sample_summary = kapilab.run(
            "samples", "generate", "--seed", "123", "--count", "1", workspace=workspace, timeout=60
        )
        self.assertEqual(sample_code, 0)
        self.assertEqual(sample_summary["cmd"], "samples.generate")
        self.assertIn('"execution":"skipped"', sample_stdout)

        export_code, export_stdout, export_summary = kapilab.run(
            "catalog", "export", "--provider", "local", "--surface", "in-process",
            backend="Cpu", workspace=workspace, timeout=60,
        )
        export = json.loads(export_stdout)
        self.assertEqual(export_code, 0)
        self.assertEqual(export_summary["cmd"], "catalog.export")
        self.assertEqual(export["scope"], "provider-maximum")
        self.assertFalse(export["complete"])
        self.assertGreater(len(export["tools"]), 0)

    def test_published_agent_replay_is_synthetic_and_rejects_invalid_fixture(self) -> None:
        if os.name != "nt":
            self.skipTest("The checked-in published KapiLab lock currently targets win-x64.")
        published = kapilab.REPO_ROOT / "artifacts/f7b/publish/Cpu/win-x64/EsilvaSoft.KapibaraStudio.KapiLab.exe"
        if not published.is_file():
            self.skipTest("Publish KapiLab into artifacts/f7b to enable subprocess integration coverage.")

        # Resolve through the distribution-lock helper: integrity drift should fail the test.
        kapilab._resolve_published("Cpu")
        expected_data = {"connections": [], "truncated": False}
        expected_hash = hashlib.sha256(
            json.dumps(expected_data, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
        ).hexdigest()
        valid_cases = {
            "schema": "kapilab-agent-replay-cases-v1",
            "version": 1,
            "cases": [{
                "id": "synthetic-list-connections",
                "toolName": "list_connections",
                "argumentsJson": "{}",
                "expectedResult": {"status": "Succeeded", "data": expected_data},
                "expectedResultSha256": expected_hash,
            }],
        }
        invalid_cases = {**valid_cases, "unexpected": "reject-unknown-fields"}

        with tempfile.TemporaryDirectory(prefix="kapilab-replay-wrapper-", dir=kapilab.REPO_ROOT) as temporary:
            workspace = Path(temporary)
            input_file = workspace / "data" / "lab" / "replay.json"
            input_file.parent.mkdir(parents=True)
            input_file.write_text(json.dumps(valid_cases), encoding="utf-8")
            returncode, stdout, summary = kapilab.run(
                "agent", "replay", "--in", "data/lab/replay.json",
                backend="Cpu", workspace=workspace, timeout=60,
            )

            output = json.loads(stdout)
            self.assertEqual(returncode, 0)
            self.assertEqual(summary["cmd"], "agent.replay")
            self.assertEqual(output["schema"], "kapilab-agent-replay-report-v2")
            self.assertEqual(output["evidenceKind"], "synthetic-agent-runtime-boundary")
            self.assertFalse(output["grantsSupported"])
            self.assertIsNone(output["taskSuccess"])
            self.assertEqual(len(output["cases"]), 1)
            result = output["cases"][0]
            self.assertEqual(result["id"], "synthetic-list-connections")
            self.assertEqual(result["status"], "Succeeded")
            self.assertEqual(result["actualResultSha256"], expected_hash)
            self.assertEqual(result["expectedResultSha256"], expected_hash)
            self.assertTrue(result["matches"])

            metadata_data = {"names": [], "truncated": False}
            metadata_hash = hashlib.sha256(
                json.dumps(metadata_data, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
            ).hexdigest()
            metadata_cases = {
                "schema": "kapilab-agent-replay-cases-v2",
                "version": 2,
                "cases": [
                    {
                        "id": "synthetic-list-databases-grant",
                        "toolName": "list_databases",
                        "argumentsJson": '{"connectionId":"$fixture"}',
                        "permissionDecision": "grant",
                        "expectedResult": {"status": "Succeeded", "data": metadata_data},
                        "expectedResultSha256": metadata_hash,
                    },
                    {
                        "id": "synthetic-list-databases-deny",
                        "toolName": "list_databases",
                        "argumentsJson": '{"connectionId":"$fixture"}',
                        "permissionDecision": "deny",
                        "expectedResult": {"status": "Denied", "errorCode": "PermissionDenied", "data": None},
                        "expectedResultSha256": None,
                    },
                ],
            }
            input_file.write_text(json.dumps(metadata_cases), encoding="utf-8")
            metadata_code, metadata_stdout, metadata_summary = kapilab.run(
                "agent", "replay", "--in", "data/lab/replay.json",
                backend="Cpu", workspace=workspace, timeout=60,
            )
            metadata_output = json.loads(metadata_stdout)
            self.assertEqual(metadata_code, 0)
            self.assertEqual(metadata_summary["cmd"], "agent.replay")
            self.assertEqual(metadata_output["schema"], "kapilab-agent-replay-report-v2")
            grant, denial = metadata_output["cases"]
            self.assertEqual(grant["status"], "Succeeded")
            self.assertEqual(grant["actualResultSha256"], metadata_hash)
            self.assertEqual(grant["auditDecisionReason"], "PolicyAllowed")
            self.assertEqual(grant["terminalAuditCount"], 1)
            self.assertEqual(grant["metadataHandlerCalls"], 1)
            self.assertTrue(grant["matches"])
            self.assertEqual(denial["status"], "Denied")
            self.assertEqual(denial["errorCode"], "PermissionDenied")
            self.assertEqual(denial["auditDecisionReason"], "PermissionMissing")
            self.assertEqual(denial["terminalAuditCount"], 1)
            self.assertEqual(denial["metadataHandlerCalls"], 0)
            self.assertTrue(denial["matches"])

            input_file.write_text(json.dumps(invalid_cases), encoding="utf-8")
            invalid_code, invalid_stdout, invalid_summary = kapilab.run(
                "agent", "replay", "--in", "data/lab/replay.json",
                backend="Cpu", workspace=workspace, timeout=60, expect=3,
            )
            self.assertEqual(invalid_code, 3)
            self.assertEqual(invalid_summary["cmd"], "agent.replay")
            self.assertEqual(invalid_summary["exit"], 3)
            self.assertEqual(invalid_stdout, "")

            invalid_types = {**valid_cases, "schema": 1}
            input_file.write_text(json.dumps(invalid_types), encoding="utf-8")
            invalid_type_code, invalid_type_stdout, invalid_type_summary = kapilab.run(
                "agent", "replay", "--in", "data/lab/replay.json",
                backend="Cpu", workspace=workspace, timeout=60, expect=3,
            )
            self.assertEqual(invalid_type_code, 3)
            self.assertEqual(invalid_type_summary["cmd"], "agent.replay")
            self.assertEqual(invalid_type_summary["exit"], 3)
            self.assertEqual(invalid_type_stdout, "")

    def test_published_report_evaluate_and_merge_cover_local_synthetic_contract(self) -> None:
        if os.name != "nt":
            self.skipTest("The checked-in published KapiLab lock currently targets win-x64.")
        published = kapilab.REPO_ROOT / "artifacts/f7b/publish/Cpu/win-x64/EsilvaSoft.KapibaraStudio.KapiLab.exe"
        if not published.is_file():
            self.skipTest("Publish KapiLab into artifacts/f7b to enable report integration coverage.")
        kapilab._resolve_published("Cpu")

        artifact_bytes = b"synthetic local metric artifact v1\n"
        artifact_sha256 = hashlib.sha256(artifact_bytes).hexdigest()
        gates = {
            "schema": "kapilab-gates-v1",
            "version": "synthetic-local-fixture-v1",
            # This is a hash of the explicit local fixture marker, not an external gates.yaml.
            "source_hash": hashlib.sha256(b"KapiLab Python wrapper synthetic report fixture v1").hexdigest(),
            "gates": [{
                "id": "synthetic-latency-limit",
                "owner": "synthetic-fixture",
                "required": True,
                "metric_id": "synthetic_latency_ms",
                "operator": "lte",
                "threshold": 100,
            }],
        }

        def evidence(run_id: str, value: int) -> dict[str, object]:
            return {
                "schema": "kapilab-run-evidence-v3",
                "status": "completed",
                "run_id": run_id,
                "qualified": True,
                "identity": {
                    "build_fingerprint": "synthetic-build-fingerprint",
                    "package_fingerprint": "synthetic-package-fingerprint",
                    "backend": "Cpu",
                    "provider": "synthetic-local",
                },
                "metrics": [{
                    "id": "synthetic_latency_ms",
                    "value": value,
                    "evidence_ref": "data/lab/metric-artifact.bin#synthetic_latency_ms",
                    "evidence_sha256": artifact_sha256,
                    "qualified": True,
                }],
            }

        with tempfile.TemporaryDirectory(prefix="kapilab-report-wrapper-", dir=kapilab.REPO_ROOT) as temporary:
            workspace = Path(temporary)
            data_dir = workspace / "data" / "lab"
            reports_dir = workspace / "reports" / "lab"
            data_dir.mkdir(parents=True)
            reports_dir.mkdir(parents=True)
            (data_dir / "metric-artifact.bin").write_bytes(artifact_bytes)
            (data_dir / "gates.json").write_text(json.dumps(gates), encoding="utf-8")

            for run_id, value in (("synthetic-pass-a", 90), ("synthetic-pass-b", 80)):
                (data_dir / f"{run_id}.json").write_text(json.dumps(evidence(run_id, value)), encoding="utf-8")
                code, stdout, summary = kapilab.run(
                    "report", "evaluate", "--gates", "data/lab/gates.json",
                    "--evidence", f"data/lab/{run_id}.json", "--out", f"reports/lab/{run_id}.report.json",
                    backend="Cpu", workspace=workspace, timeout=60,
                )
                report = json.loads((reports_dir / f"{run_id}.report.json").read_text(encoding="utf-8"))
                self.assertEqual(code, 0)
                self.assertEqual(summary["cmd"], "report.evaluate")
                self.assertEqual(report["schema"], "kapilab-report-v3")
                self.assertEqual(report["runId"], run_id)
                self.assertEqual(report["gateSetVersion"], gates["version"])
                self.assertEqual(report["gateSourceHash"], gates["source_hash"])
                self.assertEqual(report["identity"]["buildFingerprint"], "synthetic-build-fingerprint")
                self.assertEqual(report["identity"]["packageFingerprint"], "synthetic-package-fingerprint")
                self.assertTrue(report["qualified"])
                self.assertTrue(report["complete"])
                self.assertTrue(report["passed"])
                self.assertEqual(report["gates"][0]["owner"], "synthetic-fixture")
                self.assertEqual(report["gates"][0]["status"], "passed")
                self.assertEqual(report["gates"][0]["evidenceArtifactSha256"], artifact_sha256)
                self.assertEqual(json.loads(stdout)["schema"], "kapilab-report-v3")

            failed_run_id = "synthetic-required-gate-failure"
            (data_dir / f"{failed_run_id}.json").write_text(json.dumps(evidence(failed_run_id, 101)), encoding="utf-8")
            failed_code, failed_stdout, failed_summary = kapilab.run(
                "report", "evaluate", "--gates", "data/lab/gates.json",
                "--evidence", f"data/lab/{failed_run_id}.json", "--out", f"reports/lab/{failed_run_id}.report.json",
                backend="Cpu", workspace=workspace, timeout=60, expect=6,
            )
            failed_report = json.loads((reports_dir / f"{failed_run_id}.report.json").read_text(encoding="utf-8"))
            self.assertEqual(failed_code, 6)
            self.assertEqual(failed_summary["cmd"], "report.evaluate")
            self.assertEqual(failed_report["schema"], "kapilab-report-v3")
            self.assertEqual(failed_report["runId"], failed_run_id)
            self.assertEqual(failed_report["gateSourceHash"], gates["source_hash"])
            self.assertEqual(failed_report["identity"]["backend"], "Cpu")
            self.assertTrue(failed_report["qualified"])
            self.assertTrue(failed_report["complete"])
            self.assertFalse(failed_report["passed"])
            self.assertEqual(failed_report["outcome"], "failed")
            self.assertEqual(failed_report["gates"][0]["status"], "failed")
            self.assertEqual(failed_report["gates"][0]["owner"], "synthetic-fixture")
            self.assertEqual(json.loads(failed_stdout)["schema"], "kapilab-report-v3")

            merge_code, merge_stdout, merge_summary = kapilab.run(
                "report", "merge", "reports/lab/synthetic-pass-a.report.json", "reports/lab/synthetic-pass-b.report.json",
                "--out", "reports/lab/merged.json", backend="Cpu", workspace=workspace, timeout=60,
            )
            merged = json.loads((reports_dir / "merged.json").read_text(encoding="utf-8"))
            self.assertEqual(merge_code, 0)
            self.assertEqual(merge_summary["cmd"], "report.merge")
            self.assertEqual(merged["schema"], "kapilab-merged-report-v1")
            self.assertEqual(merged["gateSetVersion"], gates["version"])
            self.assertEqual(merged["gateSourceHash"], gates["source_hash"])
            self.assertEqual(merged["identity"]["buildFingerprint"], "synthetic-build-fingerprint")
            self.assertEqual(merged["identity"]["packageFingerprint"], "synthetic-package-fingerprint")
            self.assertTrue(merged["qualified"])
            self.assertTrue(merged["complete"])
            self.assertTrue(merged["passed"])
            self.assertEqual([item["runId"] for item in merged["reports"]], ["synthetic-pass-a", "synthetic-pass-b"])
            self.assertTrue(all(item["gates"][0]["owner"] == "synthetic-fixture" for item in merged["reports"]))
            self.assertEqual(json.loads(merge_stdout)["schema"], "kapilab-merged-report-v1")

            merged_failure_code, _, merged_failure_summary = kapilab.run(
                "report", "merge", "reports/lab/synthetic-pass-a.report.json", f"reports/lab/{failed_run_id}.report.json",
                "--out", "reports/lab/merged-failed.json", backend="Cpu", workspace=workspace, timeout=60, expect=6,
            )
            merged_failure = json.loads((reports_dir / "merged-failed.json").read_text(encoding="utf-8"))
            self.assertEqual(merged_failure_code, 6)
            self.assertEqual(merged_failure_summary["cmd"], "report.merge")
            self.assertEqual(merged_failure["schema"], "kapilab-merged-report-v1")
            self.assertFalse(merged_failure["passed"])
            self.assertEqual(merged_failure["outcome"], "failed")
            self.assertEqual(len(merged_failure["reports"]), 2)

            # Invalid input must fail closed without replacing an existing output file.
            preserved = "preexisting-output-must-survive-invalid-input"
            evaluate_output = reports_dir / "preserved-evaluate.json"
            evaluate_output.write_text(preserved, encoding="utf-8")
            invalid_evidence = data_dir / "invalid-evidence.json"
            invalid_evidence.write_text('{"schema":"unknown-evidence-schema"}', encoding="utf-8")
            invalid_eval_code, invalid_eval_stdout, invalid_eval_summary = kapilab.run(
                "report", "evaluate", "--gates", "data/lab/gates.json", "--evidence", "data/lab/invalid-evidence.json",
                "--out", "reports/lab/preserved-evaluate.json", backend="Cpu", workspace=workspace, timeout=60, expect=3,
            )
            self.assertEqual(invalid_eval_code, 3)
            self.assertEqual(invalid_eval_summary["cmd"], "report.evaluate")
            self.assertEqual(invalid_eval_stdout, "")
            self.assertEqual(evaluate_output.read_text(encoding="utf-8"), preserved)

            merge_output = reports_dir / "preserved-merge.json"
            merge_output.write_text(preserved, encoding="utf-8")
            invalid_report = reports_dir / "invalid-report.json"
            invalid_report.write_text('{"schema":"unknown-report-schema"}', encoding="utf-8")
            invalid_merge_code, invalid_merge_stdout, invalid_merge_summary = kapilab.run(
                "report", "merge", "reports/lab/synthetic-pass-a.report.json", "reports/lab/invalid-report.json",
                "--out", "reports/lab/preserved-merge.json", backend="Cpu", workspace=workspace, timeout=60, expect=3,
            )
            self.assertEqual(invalid_merge_code, 3)
            self.assertEqual(invalid_merge_summary["cmd"], "report.merge")
            self.assertEqual(invalid_merge_stdout, "")
            self.assertEqual(merge_output.read_text(encoding="utf-8"), preserved)

    def test_published_catalog_invoke_reports_policy_audit_without_mongo_access(self) -> None:
        if os.name != "nt":
            self.skipTest("The checked-in published KapiLab lock currently targets win-x64.")
        published = kapilab.REPO_ROOT / "artifacts/f7b/publish/Cpu/win-x64/EsilvaSoft.KapibaraStudio.KapiLab.exe"
        if not published.is_file():
            self.skipTest("Publish KapiLab into artifacts/f7b to enable subprocess integration coverage.")
        kapilab._resolve_published("Cpu")

        cases = {
            "schema": "kapilab-agent-catalog-invocation-cases-v1",
            "version": 1,
            "cases": [
                {
                    "id": "connections-allowed",
                    "toolName": "list_connections",
                    "expectedStatus": "Succeeded",
                    "expectedErrorCode": None,
                },
                {
                    "id": "databases-denied",
                    "toolName": "list_databases",
                    "expectedStatus": "Denied",
                    "expectedErrorCode": "PermissionDenied",
                },
            ],
        }
        invalid_cases = {
            "schema": cases["schema"],
            "version": cases["version"],
            "cases": [{
                "id": "non-allowlisted",
                "toolName": "mongo_find",
                "expectedStatus": "Succeeded",
                "expectedErrorCode": None,
            }],
        }

        with tempfile.TemporaryDirectory(prefix="kapilab-catalog-invoke-wrapper-", dir=kapilab.REPO_ROOT) as temporary:
            workspace = Path(temporary)
            input_file = workspace / "data" / "lab" / "invocations.json"
            input_file.parent.mkdir(parents=True)
            input_file.write_text(json.dumps(cases), encoding="utf-8")
            returncode, stdout, summary = kapilab.run(
                "catalog", "invoke", "--in", "data/lab/invocations.json",
                backend="Cpu", workspace=workspace, timeout=60, expect=0,
            )

            output = json.loads(stdout)
            self.assertEqual(returncode, 0)
            self.assertEqual(summary["cmd"], "catalog.invoke")
            self.assertEqual(output["schema"], "kapilab-agent-catalog-invocation-report-v1")
            self.assertEqual(output["evidenceKind"], "synthetic-agent-runtime-registry")
            self.assertFalse(output["grantsSupported"])
            self.assertFalse(output["mongoDocumentsAccessed"])
            self.assertEqual(len(output["cases"]), 2)
            allowed, denied = output["cases"]
            self.assertEqual(allowed["status"], "Succeeded")
            self.assertEqual(allowed["auditOutcome"], "Succeeded")
            self.assertEqual(allowed["auditDecisionReason"], "PolicyAllowed")
            self.assertTrue(allowed["matches"])
            self.assertEqual(denied["status"], "Denied")
            self.assertEqual(denied["auditOutcome"], "Denied")
            self.assertEqual(denied["auditDecisionReason"], "PermissionMissing")
            self.assertTrue(denied["matches"])

            input_file.write_text(json.dumps(invalid_cases), encoding="utf-8")
            invalid_code, invalid_stdout, invalid_summary = kapilab.run(
                "catalog", "invoke", "--in", "data/lab/invocations.json",
                backend="Cpu", workspace=workspace, timeout=60, expect=3,
            )
            self.assertEqual(invalid_code, 3)
            self.assertEqual(invalid_summary["cmd"], "catalog.invoke")
            self.assertEqual(invalid_summary["exit"], 3)
            self.assertEqual(invalid_stdout, "")


if __name__ == "__main__":
    unittest.main()
