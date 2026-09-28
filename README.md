# EsilvaSoft.KapibaraStudio

EsilvaSoft.KapibaraStudio is an open-source desktop IDE for MongoDB, built with **.NET 10** and **Avalonia** for **Windows and Linux**. It focuses on a predictable database workflow, BSON-aware results, local LiteDB storage, and optional on-device AI assistance. Licensed under **MIT**.

## MongoDB desktop IDE

Kapibara Studio is an open-source MongoDB desktop IDE and GUI client for Windows and Linux. It helps developers connect to MongoDB, browse databases and collections, write and run queries, inspect BSON-aware results, edit documents with confirmations, and export result pages.

> [!CAUTION]
> **Experimental project status.** The code, tests, and documentation in this repository were produced with AI-assisted development ("vibe coding") and have not yet undergone a full human code review, security audit, or production hardening. Expect bugs, rough edges, and design decisions that may change.
>
> - Do **not** connect to production databases without backups and read-only credentials.
> - Treat every write, delete, import, and AI-generated proposal as untrusted until reviewed.
> - Connection profiles and environment variables are stored **unencrypted** in a local LiteDB file.
>
> **Version 1.0.0 will receive a full review** of code, architecture, security, tests, and documentation before it is declared stable. Until then, every release is a pre-release.

## Current implementation status (22 September 2026)

- **v0.5.0** is archived because its defined functional scope is implemented. Its real-environment validation is planned in **Phase 9 / v0.13.0**.
- **v0.6.0**, **v0.7.0** and **v0.8.0** are archived by functional scope. They cover deterministic autocomplete, explicit reviewable AI suggestions, and text-file/workspace operations. Their manual and native-environment validation remains planned in **Phase 9 / v0.13.0**.
- **v0.9.0** local AI's automated scope is implemented and tested; actual ONNX model support remains experimental pending real-model and native-environment validation in **Phase 9 / v0.13.0**. **v0.10.0** administration and maintenance is in development.
- The desktop UI supports **Portuguese (Brazil), English, Spanish and Simplified Chinese**. `pt-BR` is the initial language and **English (`en`) is the deterministic fallback**. This does not translate repository documents under `docs/`, which remain in Portuguese.

Latest automated evidence for this checkout: solution build with **0 warnings and 0 errors**; **2,699 unit tests passed, 0 failed, 20 ignored**, and **43 benchmark tests passed**. Locked restore passed against the local NuGet cache; online package vulnerability auditing was unavailable in the sandbox. The v0.8.0 release workflow generated and checksum-verified four local packages for Windows/Linux x64/ARM64. Automated and Headless evidence does not replace the open real-model and native-environment gates.

## Support the project

If Kapibara Studio is useful to you, please consider [sponsoring esilva-silva on GitHub Sponsors](https://github.com/sponsors/esilva-silva). Sponsorship helps sustain development, testing, and documentation for this open-source project.

## MVP (v0.5.0, archived)

The MVP delivers the basic daily MongoDB cycle **without requiring AI or cross-server automation**:

```text
connect → navigate → query → view → edit → export
```

Concretely: save and test connection profiles, browse databases and collections, write and run `find`/`findOne` queries with filter, sort, limit and skip, view results as JSON or a tree while preserving BSON types (ObjectId, UUID, dates), insert/update/delete with confirmation, get deterministic autocomplete, format JSON/queries/scripts, and export the loaded page to Extended JSON or CSV.

## Project phases

| Phase | Version | Goal | Status |
| --- | --- | --- | --- |
| 1 | v0.5.0 | **MVP**: connect → navigate → query → view → edit → export | Feature scope completed and [archived](docs/done/release_v0.5.0/README.md). Manual validation is planned in [Phase 9 / v0.13.0](docs/phases/phase-09-v0.13.0/README.md) |
| 2 | v0.6.0 | Project organisation and basic autocomplete | Feature scope completed and [archived](docs/done/release_v0.6.0/README.md); manual validation remains in Phase 9 |
| 3 | v0.7.0 | AI-assisted autocomplete | Feature scope completed and [archived](docs/done/release_v0.7.0/README.md); suggestions remain reviewable and are never applied automatically |
| 4 | v0.8.0 | Opening and saving text files | Feature scope completed and [archived](docs/done/release_v0.8.0/README.md); includes local single-root workspace operations |
| 5 | v0.9.0 | Local AI and contextual productivity | Automated scope implemented; experimental for real ONNX models. Includes contextual preview, reviewable proposals, inline suggestions and multi-model selection. See [implementation meta](docs/phases/phase-05-v0.9.0/meta-de-implementacao.md) |
| 6 | v0.10.0 | Administration and maintenance | In development — collections, views, validation, indexes, stats, users, roles and logical export/import |
| 7 | v0.11.0 | Claude and agent integration | Implemented in code and under validation — [Claude configuration, subscription/API modes, permissions and limits](docs/phases/phase-07-v0.11.0/README.md) |
| 8 | v0.12.0 | Simple workflow-based AI chat | Planned — a predefined flow, limited scope and controlled actions |
| 9 | v0.13.0 | Manual validation and real-environment homologation | Planned — platforms, accessibility, MongoDB/mongosh, hardware, installation and updates in real environments |
| 10 | v1.0.0 | Stability, full review, installation and updates | Planned — stable release after functional acceptance and Phase 9 |

### How to read this table

- **Active phase** — Phase 6 / v0.10.0 is in development. Phase 5's automated scope is complete; its real-model and native-environment validation remains in Phase 9.
- **Early implementation** — code that exists ahead of its phase. It is kept and tested, but does not close that phase or count as completed scope. A feature may be visible while still marked experimental.
- **Backlog** — requirements with no assigned phase, postponed, or removed from the current scope. Nothing is deleted: the implementation is preserved and isolated, only its entry points are removed. See [backlog](docs/backlog/README.md).
- **Archived release** — a version whose feature scope is closed, in [`docs/done`](docs/done/README.md). Archiving does **not** mean it has passed manual validation; those gates are consolidated in Phase 9.

### Archived v0.6.0–v0.8.0 milestones

The archived releases include isolated autocomplete cores, a tolerant parser and context engine, contextual ranking, snippets, explicit reviewable AI suggestions with fallback and cancellation, and safe text-file/workspace persistence. The deterministic language corpus reports 675 fixtures plus one ranking gate (MRR 1.000; top-1/top-5 44/44). Detailed evidence and open manual gates are tracked in the [release archive](docs/done/README.md), [autocomplete execution plan](docs/auto-complite/execution-plan.md), and [implementation tracking](docs/12-acompanhamento-da-implementacao.md).

Automated tests (including headless UI rendering) do not replace validation against real MongoDB servers, both operating systems, screen readers, and native dialogs. Those manual gates are consolidated in [Phase 9 / v0.13.0](docs/phases/phase-09-v0.13.0/README.md). See the [phase index](docs/phases/README.md), [roadmap](docs/09-plano-de-implementacao.md), [validation matrix](docs/15-matriz-de-validacao.md), and [implementation inventory](docs/24-inventario-roadmap.md) (Portuguese).

## Requirements

- **.NET SDK 10.0.400** or a later feature band (pinned in [global.json](global.json)).
- **Windows** (x64/ARM64) or **Linux** (x64/ARM64).
- A reachable **MongoDB** server.
- Optional: an **ONNX Runtime GenAI** model export for local AI. Weights are never bundled; **Preferences → Autocomplete** can download the SlopCoder-Mongo 0.5B variants from Hugging Face on request, verified by hash.

## Getting started

```bash
git clone https://github.com/esilva-silva/EsilvaSoft.KapibaraStudio.git
cd EsilvaSoft.KapibaraStudio
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet run --project src/EsilvaSoft.KapibaraStudio.Desktop
```

## How to use

### Connect and navigate

1. Click **Conexões** (Connections) → **Nova conexão**. Paste a URI such as `mongodb://user:password@server/database` and use **Preencher** to fill the fields from it. Save the profile.
2. **Testar conexão** checks the profile; **Abrir conexão** loads its databases into the Explorer.
3. Expand a database to load its collections. Double-click (or press Enter on) a collection to open a Console tab with a prepared query. **Nothing runs until you execute it.**

Credentials can be written directly in the URI or referenced from a local environment with `${ENV.get("MONGO_PASSWORD")}`. Environments still resolve at runtime, but the **Ambientes** button was removed from the top bar: an encrypted vault is not implemented, so the requirement moved to the [backlog](docs/backlog/bkl-01-key-vault-criptografico.md). Environment values are stored **unencrypted** in the local LiteDB file.

### Query and edit

Queries are written as text in the editor (there is no form-based query builder):

```javascript
db.getCollection("customers").find({ status: "active" }).sort({ name: 1 }).limit(100)
db.Customers.find({ CustomerId: UUID("00112233-4455-6677-8899-aabbccddeeff") })
db.Customers.countDocuments({ Active: true })
```

- Results appear as JSON or a tree. Right-click a document to view, copy or edit it; saving re-reads the document and detects conflicts before writing.
- Writes and destructive operations require confirmation; read-only profiles block them.
- Use **Formatar JSON/query/script** in the editor options to format the selection or the whole tab (undoable).
- **Exportar página…** writes the loaded result page to Extended JSON or CSV.

### Features outside the active phase or still experimental

Local AI is available as an experimental feature. Administrative tools and the following backlog items remain outside the current product scope:

| Capability | Where it went |
| --- | --- |
| **Tools** window — collections, indexes, administration, transfer, bulk CRUD | [Phase 6 / v0.10.0](docs/phases/phase-06-v0.10.0/README.md) and [backlog](docs/backlog/bkl-02-ferramentas-fora-de-fase.md) |
| **Environments / Key Vault** button | [Backlog](docs/backlog/bkl-01-key-vault-criptografico.md) — environment values still resolve at runtime |
| **Script** editor mode (external `mongosh`) | [Backlog](docs/backlog/bkl-03-script-engine-entre-conexoes.md) |
| **Aggregation** editor mode | [Backlog](docs/backlog/bkl-04-modo-aggregation.md) |

The editor mode selector is therefore no longer shown: **Console** is the only available mode.

### Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `F5` | Run the whole tab |
| `Ctrl+Enter` | Run the selection or the statement at the cursor |
| `Ctrl+Space` | Show suggestions |
| `Tab` / `Esc` | Accept part of an inline suggestion / dismiss it |
| `Ctrl+T` / `Ctrl+O` / `Ctrl+S` / `Ctrl+Shift+S` | New tab / open file / save file / save as |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Switch tabs |
| `Ctrl+W` | Close tab |

### Local AI (optional, experimental)

1. Put each model export in its own folder inside a models directory (default `%LOCALAPPDATA%\EsilvaSoft\KapibaraStudio\Models` on Windows, `$XDG_DATA_HOME/EsilvaSoft/KapibaraStudio/Models` on Linux). An optional `kapibarastudio-model.json` can declare a display name, capabilities and supported hardware.
2. Open **… → Preferências → Autocomplete…**, choose the models directory, click **Atualizar** and pick a model.
3. Choose hardware: **Automático** tries NPU → GPU → CPU and falls back automatically; **CPU**, **GPU** or **NPU** use only that backend and report failures instead of silently falling back.
4. Click **Testar modelo** to validate the folder, tokenizer, ONNX session, provider and a real generation, with load time, first-token latency and tokens per second.

Without a model, autocomplete keeps working with deterministic suggestions. Nothing is sent to external AI services. AI output is a proposal: always review the diff before applying it. See [local multi-model AI](docs/26-ia-local-multimodelo.md) (Portuguese).

The local AI assistant's tab context is opt-in globally and per connection. Input JSON has a separate opt-in. Before a manual request reaches the local model, the app shows the context snapshot for review; changing the instruction, editor, target or policy invalidates that preview. Applying a proposal edits the current tab as an undoable action and never runs the query. Real-model fidelity and hardware performance have not yet been homologated; see [Phase 5 evidence and limits](docs/phases/phase-05-v0.9.0/meta-de-implementacao.md).

## Commands

### Development

```bash
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore
dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore
dotnet run --project src/EsilvaSoft.KapibaraStudio.Desktop
```

In sandboxed environments that block Avalonia's build telemetry, add `-p:UsedAvaloniaProducts=` to the build. It does not disable analyzers or tests.

### ONNX Runtime backends

Each build includes exactly one native ONNX Runtime family. Pass the same value to restore, build, test and publish:

| `-p:SlopOnnxBackend=` | Package | Default on |
| --- | --- | --- |
| `WinML` | ONNX Runtime GenAI WinML (CPU + DirectML) | Windows |
| `Cpu` | ONNX Runtime GenAI (CPU) | Linux |
| `Cuda` | ONNX Runtime GenAI CUDA (NVIDIA) | — (opt-in) |

```bash
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode -p:SlopOnnxBackend=Cpu
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -p:SlopOnnxBackend=Cpu
```

### Tests with real resources

Integration tests that need external models are marked `Explicit` and run only when selected by name:

```bash
SLOP_QWEN_MODEL=/path/to/model dotnet test tests/EsilvaSoft.KapibaraStudio.UnitTests --no-build --filter "Name~RealModelTestRunsOnTheRequestedHardware"
```

`SLOP_QWEN_MODEL` points to a Qwen-compatible ONNX GenAI export and `SLOP_DEEPSEEK_MODEL` to a DeepSeek-Coder FIM export. On Windows PowerShell, set the variable first with `$env:SLOP_QWEN_MODEL = "..."`.

### Release packages

```bash
./build-release.ps1                                  # version from the latest git tag, or 0.0.0-local
./build-release.ps1 0.8.0
./build-release.ps1 0.8.0 -SkipTests -Rids win-x64,linux-x64
```

On Windows, `build-release.bat` wraps the same script. It produces self-contained single-file packages (`.zip` for Windows, `.tar.gz` for Linux) and `SHA256SUMS.txt` in `artifacts/release/<version>`.

Published packages update themselves: the app checks GitHub Releases, shows an **Atualizar** button in the top bar when a newer version exists, downloads and verifies it (SHA-256) on click, and swaps the executable when it closes. Stable installs only receive stable releases. `dotnet run` never updates; set `SLOPSTUDIO_DISABLE_UPDATES=1` to opt out. Details: [ADR-038](docs/10-decisoes-arquiteturais.md) (Portuguese).

### CI/CD

Everything runs on GitHub-hosted runners; no self-hosted or ARM hardware is required.

- [ci.yml](.github/workflows/ci.yml): on every branch push and pull request, builds and tests on Ubuntu and Windows as two parallel jobs.
- [release.yml](.github/workflows/release.yml): on a `v*` tag, packages four executables in parallel jobs while the CI runs:

  | Package | Runner | ONNX backend |
  | --- | --- | --- |
  | `win-x64`, `win-arm64` | `windows-latest` | WinML (CPU + DirectML) |
  | `linux-x64`, `linux-arm64` | `ubuntu-latest` | CPU |

  The GitHub release (packages plus `SHA256SUMS.txt`) is published only after all tests and packages succeed. Tags with a suffix, such as `v0.5.0-alpha.1`, are marked as pre-releases. **Run workflow** with a version builds the same packages as downloadable artifacts, without creating a release.

ARM64 packages are cross-compiled. 32-bit x86 is not built, because ONNX Runtime GenAI ships no 32-bit native libraries.

## Local data and privacy

- The workspace (profiles, history, drafts, preferences, audit) is stored in `workspace.db` under `%LOCALAPPDATA%\EsilvaSoft\KapibaraStudio` (Windows) or `$XDG_DATA_HOME/EsilvaSoft/KapibaraStudio` (Linux).
- Results and resolved credentials are not saved in session snapshots, but tab text is, and it may contain sensitive data you typed.
- There is no native OS credential vault yet.
- No code, query, document or credential is sent to **external** AI services. If local AI context is explicitly enabled, selected tab data is reviewed before being sent only to the selected on-device model. Credentials and result values are excluded; Input JSON requires separate opt-in. Optional ONNX and GenAI telemetry is disabled.

## Documentation

Everything except this README is written in **Portuguese (pt-BR)**, the language of the application UI. Start with the [documentation index](docs/README.md). Contributors should read [AGENTS.md](AGENTS.md) before changing code.

## License

[MIT](LICENSE) © 2026 EsilvaSoft. Third-party components keep their own licenses: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
