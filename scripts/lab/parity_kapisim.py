"""Compara 500 chamadas reais com um adaptador CLI externo kapisim (JSONL stdin/stdout).

O simulador é executado uma vez, sem shell. Não recebe as respostas de referência.
As chamadas reais usam kapilab.run e seus controles de workspace, ambiente e distribuição.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile

import kapilab
from parity_editor_context_v1 import safe_path, write_text_atomic

VOLATILE_FIELDS = frozenset({"executionId", "proposalId", "durationMs", "observedAt", "workspaceFolder", "connectionId", "id"})


def normalize(response):
    if response is None:
        return None
    envelope = json.loads(response)
    if not envelope["truncated"]:
        try:
            data = json.loads(envelope["result"])
        except (ValueError, TypeError):
            return envelope

        def visit(value):
            if isinstance(value, dict):
                return {key: "<volatile>" if key in VOLATILE_FIELDS else visit(child) for key, child in value.items()}
            if isinstance(value, list):
                return [visit(child) for child in value]
            return value
        envelope["result"] = visit(data)
    return envelope


def compare(real, simulated):
    if len(real) != 500 or len(simulated) != 500:
        raise ValueError("A paridade exige exatamente 500 respostas de cada lado.")
    observed = {item["id"]: item for item in simulated}
    if len(observed) != 500 or set(observed) != {item["id"] for item in real}:
        raise ValueError("IDs do simulador ausentes, duplicados ou extras.")
    differences, violations = [], []
    for actual in real:
        predicted = observed[actual["id"]]
        actual_response = normalize(actual["toolResponse"])
        predicted_response = normalize(predicted["toolResponse"])
        predicted_success = predicted["status"] == "Succeeded" or (
            predicted_response is not None and predicted_response.get("status") == "Succeeded")
        # Conservative gate: a success never matches a production refusal, including fail-closed UnknownTool.
        # list_connections may succeed with an empty filtered list when a grant is missing/ended.
        if predicted_success and (actual["status"] != "Succeeded" or (
            actual.get("scenario") in {"permission_missing", "grant_expired", "grant_revoked", "confirmation_denied"}
            and actual_response != predicted_response)):
            violations.append(actual["id"])
        if (actual["status"], actual["errorCode"], actual_response) != (
            predicted["status"], predicted["errorCode"], predicted_response):
            differences.append(actual["id"])
    rate = (500 - len(differences)) / 500
    return {"schema": "kapilab-kapisim-parity-v1", "calls": 500, "agreement": rate,
            "permissionViolations": violations, "differences": differences,
            "passed": rate >= 0.99 and not violations,
            "volatileFields": sorted(VOLATILE_FIELDS)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--simulator", type=Path, required=True, help="Adaptador Python externo: JSONL em stdin/stdout.")
    parser.add_argument("--fixture", type=Path, default=Path("tools/KapiLab/Fixtures/kapi-sim/cases/reference.json"))
    parser.add_argument("--workspace", type=Path, default=kapilab.REPO_ROOT)
    parser.add_argument("--out", type=Path, default=Path("reports/lab/pr-3/parity-kapisim.json"))
    parser.add_argument("--backend", choices=["Cpu", "WinML"], default="Cpu")
    parser.add_argument("--dotnet", help="Opt-in de desenvolvimento; padrão apphost com lock verificado.")
    args = parser.parse_args()
    workspace = args.workspace.resolve()
    fixture = safe_path(workspace / args.fixture, workspace)
    simulator = safe_path(args.simulator.absolute(), workspace)
    output = safe_path(workspace / args.out, workspace, output=True)
    if output in (fixture, simulator):
        raise ValueError("O relatório não pode substituir insumos.")
    fixture_bytes = fixture.read_bytes()
    reference = json.loads(fixture_bytes)
    if reference["schema"] != "kapilab-tool-fixture-v1" or not reference["cases"]:
        raise ValueError("Fixture de referência inválido.")
    generated = copy.deepcopy(reference)
    generated["cases"] = [dict(reference["cases"][index % len(reference["cases"])], id=f"parity-{index:03}") for index in range(500)]
    temporary_root = safe_path(workspace / "tmp", workspace, output=True)
    temporary_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="kapisim-", dir=temporary_root) as directory:
        cases_path = Path(directory) / "calls.json"
        write_text_atomic(cases_path, json.dumps(generated), workspace)
        _, text, summary = kapilab.run("catalog", "invoke", "--fixture", str(cases_path), workspace=workspace,
            backend=args.backend, expected_backend=args.backend, dotnet_executable=args.dotnet, timeout=300,
            environment={"KAPILAB_MONGO_URI": os.environ.get("KAPILAB_MONGO_URI", "mongodb://127.0.0.1:27017")})
        real_report = json.loads(text)
        if real_report["schema"] != "kapilab-tool-invocation-report-v2" or not real_report["grantsSynthetic"]:
            raise ValueError("Relatório real inválido.")
        # Predictions receive only fixture/cases and budgets; no oracle result or expected status.
        calls = [{key: value for key, value in case.items() if not key.startswith("expected")}
                 for case in generated["cases"]]
        request = {"schema": "kapilab-kapisim-input-v1", "domains": [json.loads((workspace / path).read_text(encoding="utf-8-sig"))
            for path in generated["domains"]], "maxToolResultBytes": generated["maxToolResultBytes"], "calls": calls}
        process = subprocess.run([sys.executable, str(simulator)], input=json.dumps(request) + "\n", text=True,
            capture_output=True, cwd=workspace, timeout=300, check=True, shell=False)
        if len(process.stdout.encode("utf-8")) > 16 * 1024 * 1024:
            raise ValueError("Saída do simulador excede 16 MiB.")
        simulated = [json.loads(line) for line in process.stdout.splitlines() if line.strip()]
        report = compare(real_report["cases"], simulated)
        report.update(fixtureSha256=hashlib.sha256(fixture_bytes).hexdigest(),
                      simulatorSha256=hashlib.sha256(simulator.read_bytes()).hexdigest(), runtime=summary)
        output.parent.mkdir(parents=True, exist_ok=True)
        write_text_atomic(output, json.dumps(report, indent=2) + "\n", workspace)
    print(f"Concordância: {report['agreement']:.2%}; permissões extras: {len(report['permissionViolations'])}.")
    return 0 if report["passed"] else 6


if __name__ == "__main__":
    raise SystemExit(main())
