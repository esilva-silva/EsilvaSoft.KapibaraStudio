# Dependências do Console

EsilvaSoft.SlopStudio mantém sua licença MIT. Estas dependências têm licenças próprias, conforme os manifests NuGet e os projetos de origem:

| Dependência | Versão | Licença | Autoria / origem |
| --- | --- | --- | --- |
| Jint | 4.16.0 | BSD-2-Clause | Sébastien Ros — https://github.com/sebastienros/jint |
| Acornima | 1.7.0 | BSD-3-Clause | Adam Simon — https://github.com/adams85/acornima |

Jint é o interpretador JavaScript; Acornima é seu parser transitivo. Os lockfiles fixam versões e hashes NuGet. A fixture MongoDB Community portátil fica apenas em `.cache` para homologação e não integra a distribuição da aplicação.

Esta relação documenta o incremento Console; o inventário completo do produto continua incluindo as dependências já declaradas em Directory.Packages.props e seus manifests/licenças.

## Autocomplete local

Microsoft.ML.OnnxRuntimeGenAI/Managed **0.15.2** e Microsoft.ML.OnnxRuntime/Managed **1.28.0**, Microsoft, licença MIT. [ONNX Runtime GenAI](https://github.com/microsoft/onnxruntime-genai/tree/v0.15.2), [ONNX Runtime](https://github.com/microsoft/onnxruntime). Os pacotes incluem avisos de componentes nativos transitivos que devem acompanhar distribuições derivadas. Os lockfiles fixam versões e hashes.

Pesos e tokenizers Qwen não fazem parte do repositório nem da publicação. A licença de cada modelo/exportação deve ser consultada na origem escolhida pelo usuário; a MIT do Slop Studio não relicencia modelos externos.

## Fase 7 (v0.11.0) — MCP, OpenAI e Claude incorporados

**Atualizado em 28/09/2026.** A solução incorpora dependências da Fase 7, confirmadas em `Directory.Packages.props`; a tabela registra seus projetos, licenças e fontes. O histórico detalhado de planejamento e agentes foi removido ao retomar a fase com escopo conciso. O estado vigente da integração Claude, as condições de uso e os gates de distribuição estão na [documentação atual da Fase 7](docs/phases/phase-07-v0.11.0/README.md).

| Pacote | Versão | Projeto(s) | Licença declarada | Proveniência |
| --- | --- | --- | --- | --- |
| `ModelContextProtocol.Core` | 2.2.0 | `EsilvaSoft.SlopStudio.McpServer` (direta) | Apache-2.0; LICENSE do commit `6fa3825` documenta contribuições legadas MIT e docs CC-BY-4.0 | [Spike MCP](eng/spikes/phase-07/mcp-sdk/README.md), [cópia da licença](eng/spikes/phase-07/mcp-sdk/licenses/ModelContextProtocol-2.2.0-LICENSE) |
| `Anthropic` | 12.50.0 | `EsilvaSoft.SlopStudio.Infrastructure.Agents` (direta, pasta `Anthropic/`) | MIT | [Spike Anthropic](eng/spikes/phase-07/anthropic-sdk/README.md), release `Anthropic-v12.50.0`, commit `2beeb9f9b402b1cdb8030e7cdc38cc6cae5d42aa`, [cópia da licença](eng/spikes/phase-07/anthropic-sdk/licenses/Anthropic-12.50.0-LICENSE.txt) |
| `OpenAI` | 2.14.0 | `EsilvaSoft.SlopStudio.Infrastructure.Agents` (direta, pasta `OpenAi/`) | MIT | [Spike OpenAI](eng/spikes/phase-07/openai-api/README.md), commit `2e77b08828145f658ec04e49aec87abb1543c553` |
| `System.ClientModel` | 1.15.0 | Transitiva de `OpenAI` 2.14.0 | MIT | Inventário do spike OpenAI (`eng/spikes/phase-07/openai-api/*.csv`); origem `Azure/azure-sdk-for-net` |
| `Microsoft.Extensions.AI.Abstractions` | 10.5.1 (via `Anthropic`) | Transitiva de `Anthropic` 12.50.0 | MIT | [Spike Anthropic](eng/spikes/phase-07/anthropic-sdk/README.md), [cópia da licença](eng/spikes/phase-07/anthropic-sdk/licenses/Microsoft.Extensions.AI.Abstractions-10.5.1-LICENSE.txt) |
| `Microsoft.Extensions.AI.Abstractions` | 10.8.3 (via `ModelContextProtocol.Core`) | Transitiva de `ModelContextProtocol.Core` no `McpServer` | MIT | [Spike MCP](eng/spikes/phase-07/mcp-sdk/README.md) |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.10 | Transitiva de `ModelContextProtocol.Core` no `McpServer` | MIT | [Spike MCP](eng/spikes/phase-07/mcp-sdk/README.md) |

`Microsoft.Extensions.Logging.Abstractions` também recebe `VersionOverride` 10.0.10 nos dois projetos (`McpServer`, `Infrastructure.Agents`), acima do pin central 10.0.0 usado pelo restante da solução; é a mesma licença MIT já coberta pela dependência central, sem pacote adicional. Risco registrado: `Microsoft.Extensions.AI.Abstractions` 10.5.1 (via Anthropic) é superior à 9.8.0 resolvida pelo ONNX GenAI em `UnitTests`/Desktop — qualquer composição futura que junte `Infrastructure.Agents` com o runtime ONNX local unifica em 10.x e exige revalidação offline do autocomplete/ONNX (AC-16) antes de compor no Desktop. Essa revalidação não faz parte desta atualização documental.

Nenhum dos pacotes acima traz binário nativo, segundo os inventários dos spikes correspondentes (mesmas versões exatas incorporadas). **Pendências de SBOM formal, ainda não fechadas por este aviso:** consulta de vulnerabilidades conectada (o spike MCP e o spike Anthropic reportaram "nenhum pacote vulnerável" em consultas datadas, não substituindo auditoria contínua; o spike OpenAI não concluiu auditoria por falha de TLS/credenciais do feed), SBOM por RID Windows/Linux, e cópia formal dos avisos Apache-2.0/MIT no instalador de distribuição. A licença do **EsilvaSoft.SlopStudio permanece MIT**.

## Planejamento v0.11.0 — candidatos ainda não incorporados

A pesquisa original desta seção (22/09/2026) partia da premissa de que a fase não adicionaria dependências; essa premissa foi revista em 24/09/2026. Os inventários dos pacotes incorporados continuam nas fontes dos respectivos spikes (`eng/spikes/phase-07/`). Candidatos não incorporados — Codex executável/SDK, Claude Agent SDK Python/TypeScript e runtimes adicionais — não devem ser confundidos com os pacotes já listados e não fazem parte deste aviso de distribuição.
