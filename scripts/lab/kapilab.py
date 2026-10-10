"""Small, shell-free bridge from Python evaluation scripts to the local KapiLab CLI."""

from __future__ import annotations

import json
import ipaddress
import hashlib
import math
import os
import shutil
import subprocess
from pathlib import Path
from typing import Mapping, Sequence
from urllib.parse import urlsplit


REPO_ROOT = Path(__file__).resolve().parents[2]
PROJECT = REPO_ROOT / "tools" / "KapiLab" / "KapiLab.csproj"
DISTRIBUTION_LOCK = REPO_ROOT / "tools" / "kapilab.lock.json"


class KapiLabError(RuntimeError):
    """A KapiLab command returned an unexpected exit code."""

    def __init__(self, returncode: int, summary: dict[str, object] | None) -> None:
        self.returncode = returncode
        self.summary = summary
        super().__init__(f"KapiLab returned exit code {returncode}.")


class ContractDrift(KapiLabError):
    """Exit code 7: catalog or runtime contract drift."""


class GateFailed(KapiLabError):
    """Exit code 6: a required evaluation gate did not pass."""


class PrivacyViolation(KapiLabError):
    """Exit code 8: input or output was refused by laboratory policy."""


class GpuBusy(KapiLabError):
    """Exit code 9: another process owns the GPU load lock."""


class KapiLabProtocolError(RuntimeError):
    """The CLI did not emit its expected JSON summary on stderr."""


class KapiLabLockError(RuntimeError):
    """The published KapiLab executable is missing or differs from its distribution lock."""


class KapiLabTimeout(TimeoutError):
    """The CLI exceeded the caller's explicit timeout."""


_ERROR_TYPES: dict[int, type[KapiLabError]] = {
    6: GateFailed,
    7: ContractDrift,
    8: PrivacyViolation,
    9: GpuBusy,
}

_PASSTHROUGH_ENVIRONMENT = frozenset({"KAPILAB_MONGO_URI"})
_HOST_ENVIRONMENT = (
    "SystemRoot",
    "WINDIR",
    "TEMP",
    "TMP",
    "USERPROFILE",
    "APPDATA",
    "LOCALAPPDATA",
    "HOME",
    "TMPDIR",
)


def _summary(stderr: str, expected_command: str) -> dict[str, object] | None:
    for line in reversed(stderr.splitlines()):
        try:
            value = json.loads(line)
        except json.JSONDecodeError:
            continue
        if not isinstance(value, dict) or not {"cmd", "exit", "run_id", "out"} <= value.keys():
            continue
        if (
            value["cmd"] == expected_command
            and isinstance(value["exit"], int)
            and not isinstance(value["exit"], bool)
            and isinstance(value["run_id"], str)
            and bool(value["run_id"])
            and isinstance(value["out"], str)
        ):
            return value
    return None


def _resolve_dotnet(executable: str | Path | None) -> Path:
    candidate = str(executable) if executable is not None else shutil.which("dotnet")
    if not candidate:
        raise FileNotFoundError("dotnet was not found; pass dotnet_executable explicitly.")
    # strict=True traverses Program Files on Windows and may be denied by the
    # workspace sandbox even though the executable is runnable. Normalize the
    # absolute path without resolving junctions outside the workspace.
    path = Path(candidate).expanduser().resolve(strict=False)
    if not path.is_absolute() or not path.is_file():
        raise FileNotFoundError("dotnet_executable must resolve to a file.")
    return path


def _resolve_published(backend: str) -> tuple[Path, dict[str, object]]:
    try:
        lock = json.loads(DISTRIBUTION_LOCK.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise KapiLabLockError("KapiLab distribution lock is missing or invalid; run the publish step.") from error
    if not isinstance(lock, dict) or lock.get("schema") != "kapilab-distribution-lock-v1":
        raise KapiLabLockError("KapiLab distribution lock has an unsupported schema.")
    entries = lock.get("entries")
    if not isinstance(entries, list):
        raise KapiLabLockError("KapiLab distribution lock entries are invalid.")
    rid = "win-x64" if os.name == "nt" else None
    matches = [entry for entry in entries if isinstance(entry, dict) and entry.get("backend") == backend]
    if len(matches) != 1 or rid is None or matches[0].get("rid") != rid:
        raise KapiLabLockError(f"No unique KapiLab publication exists for backend {backend} on this host.")
    entry = matches[0]
    relative = entry.get("executable")
    digest = entry.get("sha256")
    content_digest = entry.get("contentSha256")
    if (
        not isinstance(relative, str)
        or Path(relative).is_absolute()
        or not isinstance(digest, str)
        or len(digest) != 64
        or any(character not in "0123456789abcdef" for character in digest.lower())
        or not isinstance(content_digest, str)
        or len(content_digest) != 64
        or any(character not in "0123456789abcdef" for character in content_digest.lower())
    ):
        raise KapiLabLockError("KapiLab distribution entry is malformed.")
    try:
        lock_root = DISTRIBUTION_LOCK.parent.resolve(strict=False)
        executable = (lock_root / relative).resolve(strict=False)
        repository = REPO_ROOT.resolve(strict=False)
        if not executable.is_relative_to(repository) or not executable.is_file():
            raise KapiLabLockError("Published KapiLab executable must remain inside this checkout.")
        hasher = hashlib.sha256()
        with executable.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                hasher.update(block)
    except OSError as error:
        raise KapiLabLockError("Published KapiLab executable is missing or unreadable.") from error
    if hasher.hexdigest() != digest.lower():
        raise KapiLabLockError("Published KapiLab executable hash does not match its lock.")
    try:
        actual_content_digest = _published_tree_sha256(executable.parent)
    except OSError as error:
        raise KapiLabLockError("Published KapiLab files are missing or unreadable.") from error
    if actual_content_digest != content_digest.lower():
        raise KapiLabLockError("Published KapiLab files do not match their distribution lock.")
    if not isinstance(entry.get("genAiVersion"), str) or not isinstance(entry.get("ideCommit"), str):
        raise KapiLabLockError("KapiLab distribution provenance is incomplete.")
    return executable, entry


def _published_tree_sha256(directory: Path) -> str:
    hasher = hashlib.sha256()
    files: list[Path] = []
    for path in directory.rglob("*"):
        if path.is_symlink():
            raise KapiLabLockError("Published KapiLab folders must not contain symbolic links.")
        if path.is_file():
            files.append(path)
    for path in sorted(files, key=lambda item: item.relative_to(directory).as_posix()):
        relative = path.relative_to(directory).as_posix().encode("utf-8")
        content_hasher = hashlib.sha256()
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                content_hasher.update(block)
        hasher.update(relative)
        hasher.update(b"\0")
        hasher.update(content_hasher.hexdigest().encode("ascii"))
        hasher.update(b"\n")
    return hasher.hexdigest()


def _controlled_environment(executable: Path, environment: Mapping[str, str] | None) -> dict[str, str]:
    supplied = {} if environment is None else dict(environment)
    unexpected = supplied.keys() - _PASSTHROUGH_ENVIRONMENT
    if unexpected:
        raise ValueError("Only KAPILAB_MONGO_URI may be supplied to the KapiLab process.")
    if any(not isinstance(value, str) or "\x00" in value for value in supplied.values()):
        raise ValueError("Environment values must be strings without NUL characters.")
    if "KAPILAB_MONGO_URI" in supplied and not _is_loopback_mongo_uri(supplied["KAPILAB_MONGO_URI"]):
        raise ValueError("KAPILAB_MONGO_URI must target a local loopback MongoDB fixture.")

    controlled = {key: os.environ[key] for key in _HOST_ENVIRONMENT if key in os.environ}
    path_entries = [str(executable.parent)]
    git = shutil.which("git")
    if git:
        path_entries.append(str(Path(git).parent))
    if os.name == "nt" and os.environ.get("SystemRoot"):
        path_entries.append(str(Path(os.environ["SystemRoot"]) / "System32"))
    controlled["PATH"] = os.pathsep.join(dict.fromkeys(path_entries))
    controlled["DOTNET_NOLOGO"] = "1"
    controlled["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    controlled.update(supplied)
    return controlled


def _is_loopback_mongo_uri(value: str) -> bool:
    try:
        parsed = urlsplit(value)
        host = parsed.hostname
        # Reject SRV and multi-host replica-set URIs: every accepted endpoint must
        # be visibly local without DNS discovery or remote failover.
        if parsed.scheme != "mongodb" or not host or "," in parsed.netloc:
            return False
        _ = parsed.port  # Validate malformed/out-of-range ports.
        return host.lower() == "localhost" or ipaddress.ip_address(host).is_loopback
    except (ValueError, UnicodeError):
        return False


def _command_name(args: Sequence[str]) -> str:
    if not args:
        raise ValueError("At least one KapiLab command is required.")
    if args[0] == "env":
        return "env"
    if args[0] in {"agent", "bench", "catalog", "contract", "guidance", "matrix", "npu", "parity", "report", "samples"}:
        if len(args) < 2:
            raise ValueError("A KapiLab subcommand is required.")
        return f"{args[0]}.{args[1]}"
    if args[0] == "model":
        if len(args) < 2:
            raise ValueError("A model subcommand is required.")
        if args[1] == "run":
            if len(args) < 3:
                raise ValueError("A model run role is required.")
            return f"model.run.{args[2]}"
        return f"model.{args[1]}"
    raise ValueError("Unsupported KapiLab command; use a known command path.")


def _invoke(
    command: list[str], expected_command: str, timeout: float, environment: Mapping[str, str]
) -> tuple[int, str, dict[str, object]]:
    try:
        completed = subprocess.run(
            command,
            cwd=REPO_ROOT,
            env=dict(environment),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout,
            check=False,
            shell=False,
        )
    except subprocess.TimeoutExpired as error:
        raise KapiLabTimeout(f"KapiLab exceeded {timeout:g} seconds.") from error
    summary = _summary(completed.stderr, expected_command)
    if summary is None:
        raise KapiLabProtocolError("KapiLab did not emit a valid command summary.")

    returncode = int(summary["exit"])
    if completed.returncode != returncode:
        raise KapiLabProtocolError("Process exit code disagrees with the KapiLab summary.")
    return returncode, completed.stdout, summary


def run(
    *args: str,
    expect: int = 0,
    workspace: str | Path | None = None,
    timeout: float = 300,
    environment: Mapping[str, str] | None = None,
    expected_backend: str | None = None,
    expected_genai: str | None = None,
    backend: str | None = None,
    dotnet_executable: str | Path | None = None,
) -> tuple[int, str, dict[str, object]]:
    """Run KapiLab without a shell and return code, stdout, and its stderr JSON summary.

    The published executable and its SHA-256 are checked against the distribution
    lock by default. Passing ``dotnet_executable`` explicitly opts into developer
    mode, which runs the current build tree with ``dotnet run --no-build``.
    Optional expected backend/GenAI values run the CLI's ``env`` gate before the
    requested command. When the command returns a code other than ``expect``, a typed
    error is raised for codes 6–9 and ``KapiLabError`` for other codes. Set ``expect``
    to a non-zero code to assert a negative-path result and receive the normal tuple.
    """
    if not args or any(not isinstance(arg, str) or "\x00" in arg for arg in args):
        raise ValueError("Provide non-empty string arguments without NUL characters.")
    if (
        not isinstance(timeout, (int, float))
        or isinstance(timeout, bool)
        or not math.isfinite(timeout)
        or timeout <= 0
    ):
        raise ValueError("timeout must be a finite number greater than zero.")

    expected_command = _command_name(args)
    selected_backend = backend or expected_backend or ("WinML" if os.name == "nt" else "Cpu")
    if expected_backend is not None and expected_backend != selected_backend:
        raise ValueError("backend and expected_backend must match.")
    if dotnet_executable is None:
        executable, locked_entry = _resolve_published(selected_backend)
        base_command = [str(executable)]
        locked_genai = str(locked_entry["genAiVersion"])
    else:
        dotnet = _resolve_dotnet(dotnet_executable)
        executable = dotnet
        base_command = [str(dotnet), "run", "--project", str(PROJECT), "--no-build", "--"]
        locked_genai = None
    child_environment = _controlled_environment(executable, environment)
    command = [*base_command, *args]
    if workspace is not None:
        command.extend(["--workspace", str(Path(workspace).resolve())])
    if expected_genai is not None and locked_genai is not None and expected_genai != locked_genai:
        raise KapiLabLockError("Expected GenAI version does not match the published KapiLab lock.")
    if expected_backend is not None or expected_genai is not None:
        check = [*base_command, "env"]
        if expected_backend is not None:
            check.extend(["--expect-backend", expected_backend])
        if expected_genai is not None:
            check.extend(["--expect-genai", expected_genai])
        if workspace is not None:
            check.extend(["--workspace", str(Path(workspace).resolve())])
        check_code, _, check_summary = _invoke(check, "env", timeout, child_environment)
        if check_code != 0:
            error_type = _ERROR_TYPES.get(check_code, KapiLabError)
            raise error_type(check_code, check_summary)

    returncode, stdout, summary = _invoke(command, expected_command, timeout, child_environment)
    if returncode != expect:
        error_type = _ERROR_TYPES.get(returncode, KapiLabError)
        raise error_type(returncode, summary)
    return returncode, stdout, summary
