"""Prova independente de hashes/IDs; usa a referência externa somente para leitura.

Mapeamento: prefix/suffix preservados; context -> editor; context.language ->
language. Os 700 casos chat/correction usam outro contrato e são substituídos
por samples generate (statement separado em caretUtf16), sem execução MongoDB.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import tempfile
import sys

import kapilab


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def text_hash(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def safe_path(path: Path, workspace: Path, *, output: bool = False) -> Path:
    absolute = Path(os.path.abspath(path))
    for parent in (absolute, *absolute.parents):
        if parent.is_symlink() or parent.is_junction():
            raise ValueError("Links e junctions não são permitidos.")
    if absolute.is_relative_to(workspace / "data/eval/blind"):
        raise ValueError("Conjunto cego bloqueado.")
    if output and not any(absolute.is_relative_to(workspace / folder) for folder in ("data/lab", "reports/lab", "tmp")):
        raise ValueError("Saída fora do laboratório.")
    return absolute


def write_text_atomic(path: Path, value: str, workspace: Path) -> None:
    path = safe_path(path, workspace, output=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", newline="\n",
                                         dir=path.parent, prefix=path.name + ".", suffix=".tmp", delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(value)
        safe_path(path, workspace, output=True)
        temporary.replace(path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def write_jsonl(path: Path, records: list[dict], workspace: Path) -> None:
    write_text_atomic(path, "".join(json.dumps(record, ensure_ascii=True) + "\n" for record in records), workspace)


def validate_artifacts(destination: Path, workspace: Path, inputs: list[Path]) -> None:
    for name in ("render-ide.jsonl", "render-py.jsonl", "tokens-ide.jsonl", "tokens-py.jsonl", "diff.json", "summary.md"):
        output = safe_path(destination / name, workspace, output=True)
        if output in inputs:
            raise ValueError("Saída não pode sobrescrever um insumo.")


def read_jsonl(path: Path) -> list[dict]:
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line]


def map_record(record: dict, reference) -> tuple[dict, dict]:
    if record["type"] not in ("fim", "completion"):
        raise ValueError("Registro incompatível com editor-context-v1.")
    context = dict(record.get("context") or {})
    context.setdefault("language", reference.LANGUAGE_CONSOLE)
    # Context headers are materialized by the IDE as CRLF, regardless of the corpus hint.
    context["newline"] = "\r\n"
    example = {**record, "context": context}
    return {"id": record["id"], "type": record["type"], "prefix": record["prefix"],
            "suffix": record["suffix"], "language": context["language"], "editor": context}, example


def run(args) -> int:
    workspace = Path(os.path.abspath(args.workspace))
    corpus = safe_path(Path(args.corpus), workspace)
    reference_path = safe_path(Path(args.reference), workspace)
    package = safe_path(Path(args.package), workspace)
    tokenizer_path = safe_path(Path(args.tokenizer), workspace)
    destination = safe_path(workspace / args.out, workspace, output=True)
    validate_artifacts(destination, workspace, [corpus, reference_path])
    destination.mkdir(parents=True, exist_ok=True)
    spec = importlib.util.spec_from_file_location("parity_external_contracts", reference_path)
    reference = importlib.util.module_from_spec(spec)
    previous_bytecode = sys.dont_write_bytecode
    try:
        sys.dont_write_bytecode = True
        spec.loader.exec_module(reference)
    finally:
        sys.dont_write_bytecode = previous_bytecode
    from transformers import AutoTokenizer
    tokenizer = AutoTokenizer.from_pretrained(str(tokenizer_path), local_files_only=True, trust_remote_code=False)
    if digest(tokenizer_path / "tokenizer.json") != digest(package / "tokenizer.json"):
        raise ValueError("Tokenizer HF difere do pacote ONNX.")
    contract = reference.QwenFim(tokenizer)
    def cli(arguments, expect=0):
        return kapilab.run(*arguments, workspace=workspace, backend="Cpu", expected_backend="Cpu",
                           dotnet_executable=(shutil.which(args.dotnet) or args.dotnet) if args.dotnet else None, timeout=600, expect=expect)
    environment = json.loads(cli(["env"])[1])
    records = read_jsonl(corpus)
    selected = [record for record in records if record["type"] in ("fim", "completion")]
    lf_contexts = sum((record.get("context") or {}).get("newline") == "\n" for record in selected)
    missing_contexts = sum(not record.get("context") for record in selected)
    if len(selected) != 1300 or len(records) != 2000:
        raise ValueError("Corpus esperado: 2.000 registros, 1.300 autocomplete.")
    # Raw editor text is transient, needed by contract tokenize; reports retain only hashes and IDs.
    temporary_root = safe_path(workspace / "tmp", workspace, output=True)
    temporary_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="parity-", dir=temporary_root) as temporary:
        scratch = Path(temporary)
        samples_path = scratch / "samples.jsonl"
        cli(["samples", "generate", "--seed", "20261010", "--count", "700", "--out", str(samples_path)])
        for sample in read_jsonl(samples_path):
            # Samples are ASCII today; UTF-16 slicing keeps the mapping explicit.
            encoded = sample["statement"].encode("utf-16-le")
            caret = sample["caretUtf16"] * 2
            selected.append({"id": "parity-synthetic-" + sample["id"], "type": "fim",
                             "prefix": encoded[:caret].decode("utf-16-le"), "suffix": encoded[caret:].decode("utf-16-le"),
                             "context": {"language": reference.LANGUAGE_SCRIPT, "known_names": [sample["collection"]]}})
        mapped = [map_record(record, reference) for record in selected]
        input_path, raw_path = scratch / "input.jsonl", scratch / "render.jsonl"
        write_jsonl(input_path, [item[0] for item in mapped], workspace)
        cli(["contract", "render", "--contract", "editor-context-v1", "--in", str(input_path), "--out", str(raw_path)])
        ide_render = read_jsonl(raw_path)
        python_render = []
        for _, example in mapped:
            prefix, suffix = reference.prompt_text(example)
            python_render.append({"id": example["id"], "modelPrefixSha256": text_hash(prefix), "suffixSha256": text_hash(suffix)})
        write_jsonl(destination / "render-ide.jsonl", [{key: row[key] for key in ("id", "modelPrefixSha256", "suffixSha256")} for row in ide_render], workspace)
        write_jsonl(destination / "render-py.jsonl", python_render, workspace)
        cli(["contract", "tokenize", "--package", str(package), "--in", str(raw_path), "--context-tokens", "2048",
             "--completion-tokens", "32", "--out", str(destination / "tokens-ide.jsonl")])
        # Identity is provenance only; compute file hashes independently rather than copying IDE hashes.
        identity = {"family": "Qwen2.5-Coder", "promptFormat": "qwen-fim", "genAiConfigSha256": digest(package / "genai_config.json"),
                    "tokenizerSha256": digest(tokenizer_path / "tokenizer.json"),
                    "tokenizerConfigSha256": digest(package / "tokenizer_config.json"), "adapterConfigSha256": ""}
        python_tokens = []
        for (_, example), rendered in zip(mapped, python_render, strict=True):
            python_tokens.append({"schema": "kapilab-tokenized-contract-v2", "contract": "editor-context-v1", **rendered,
                                  "contextTokens": 2048, "requestedContextTokens": 2048, "completionTokens": 32,
                                  "tokenizer": identity, "tokenIds": reference.prompt_ids(contract, example, 2048)})
        write_jsonl(destination / "tokens-py.jsonl", python_tokens, workspace)
        try:
            cli(["contract", "diff", "--expected", str(destination / "tokens-py.jsonl"),
                 "--actual", str(destination / "tokens-ide.jsonl"), "--out", str(destination / "diff.json")])
            exit_code = 0
        except kapilab.KapiLabError as error:
            if error.returncode != 11:
                raise
            exit_code = 11
    diff = json.loads((destination / "diff.json").read_text(encoding="utf-8"))
    render_differences = sum(a != b for a, b in zip(read_jsonl(destination / "render-ide.jsonl"), python_render, strict=True))
    summary = (f"# Paridade editor-context-v1\n\n"
               f"Registros: 2.000 (1.300 reais fim/completion + 700 sintéticos; seed 20261010).\n"
               f"Divergências de render: {render_differences}; divergências de tokens/contrato: {len(diff['differences'])}; código diff: {exit_code}.\n\n"
               "Chat/correction (700) excluídos: a referência usa LocalModelAiChatService/AiEditorContext; "
               "local-agent-fim-v0 captura LocalAgentProvider, outro contrato. Não é paridade de chat nem aceite de 2.000 registros reais.\n\n"
               "Mapeamento: prefix/suffix preservados; context → editor; context.language → language; "
               f"CRLF fixo no cabeçalho ({lf_contexts} contextos originalmente LF rematerializados); {missing_contexts} contextos ausentes recebem cabeçalho padrão da IDE; "
               "prefix/suffix não são normalizados; samples statement separado no caret UTF-16. "
               "AutoTokenizer local + prompt_text/prompt_ids/QwenFim da referência externa. "
               "Orçamento solicitado e esperado 2048, completion 32; diff verifica também orçamento efetivo.\n\n"
               f"IDE: {environment.get('ideCommit')}; dirty: {environment.get('ideDirty')}; GenAI: {environment.get('genAiVersion')}.\n\n")
    for label, path in (("Corpus", corpus), ("Referência Python", reference_path), ("Tokenizer HF", tokenizer_path / "tokenizer.json"),
                        ("Tokenizer ONNX", package / "tokenizer.json"), ("GenAI config", package / "genai_config.json")):
        summary += f"- {label}: SHA-256 `{digest(path)}`\n"
    for path in sorted(destination.glob("*.json*")):
        summary += f"- {path.name}: SHA-256 `{digest(path)}`\n"
    write_text_atomic(destination / "summary.md", summary, workspace)
    print(json.dumps({"records": 2000, "render_differences": render_differences, "token_differences": len(diff["differences"]), "exit": exit_code}))
    return 11 if render_differences else exit_code


def main() -> int:
    parser = argparse.ArgumentParser(description="Prova local de paridade; não imprime prompts.")
    parser.add_argument("--workspace", required=True)
    parser.add_argument("--corpus", required=True)
    parser.add_argument("--reference", required=True)
    parser.add_argument("--package", required=True)
    parser.add_argument("--tokenizer", required=True)
    parser.add_argument("--out", default="reports/lab/parity-editor-context-v1")
    parser.add_argument("--dotnet", help="Opt-in developer mode do wrapper; padrão apphost publicado/lock.")
    args = parser.parse_args()
    try:
        return run(args)
    except Exception as error:
        # Third-party errors may include text/token fragments. Only expose the exception type.
        print(json.dumps({"error_type": type(error).__name__, "exit": 1}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
