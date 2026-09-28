# Spike MCP C# — lote 0 da fase 7

**Pesquisa isolada, ampliada em Windows em 23/09/2026.** SDK .NET `10.0.401`, target `net10.0`, MCP C# `2.2.0`: restore locked e build passaram; **10/10 testes passaram**, sem avisos de compilação. São 4 testes originais SDK↔SDK e 6 testes adicionais com cliente BCL independente. Não implementa o servidor MCP do produto nem conclui o lote 0, a fase 7 ou AC-01..AC-20.

`McpSdkSpike.slnx` está fora de `EsilvaSoft.SlopStudio.slnx`. Os arquivos locais `Directory.Build.props` e `Directory.Packages.props` interrompem a herança das configurações/pacotes do produto. Os três `packages.lock.json` são exclusivos do spike. `NuGet.Config` usa somente nuget.org e cache local ignorado. A licença do EsilvaSoft.SlopStudio continua MIT.

## Prova observada

O harness C# inicia um servidor C# filho com ambiente reduzido à allowlist do SDK. `StreamClientTransport` recebe os pipes reais stdin/stdout do processo; o servidor usa `StdioServerTransport`. A captura registra bytes reais, valida framing JSON por linha, IDs correspondentes e distinção entre as eras. O harness fecha stdin explicitamente porque o transporte de streams não é proprietário dos pipes; EOF encerra o servidor com código 0. Cada execução tem processo e deadline próprios; falhas encerram o filho no teardown.

| Cliente C# 2.2.0 | Servidor C# 2.2.0 | Transporte | Resultado Windows |
| --- | --- | --- | --- |
| Fixado 2025-11-25 | Dual-era, versão não fixada | STDIO real | `initialize`, `notifications/initialized`, `tools/list` vazio; sem metadata moderna; stdout JSON-RPC e stderr vazio; EOF código 0 |
| Fixado 2026-07-28 | Dual-era, versão não fixada | STDIO real | `server/discover`, `tools/list` vazio; versão em `_meta` por request; sem `initialize`; stdout JSON-RPC e stderr vazio; EOF código 0 |
| Fixado 2025-11-25 | Fixado 2026-07-28 | STDIO real | Incompatibilidade rejeitada por exceção MCP |
| Fixado 2026-07-28 | Fixado 2025-11-25 | STDIO real | Incompatibilidade rejeitada por exceção MCP; não aceitou downgrade |

As fixtures JSON em `fixtures/` são expectativas **autoria do spike** baseadas nas especificações [2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle) e [2026-07-28](https://modelcontextprotocol.io/specification/2026-07-28/basic/versioning). Não são a suíte oficial completa de conformidade nem schemas oficiais vendorizados. Os transcripts observados e sintéticos estão em [evidence/windows](evidence/windows); não são golden files usados para ocultar diferenças. Nova execução anexa seu wire ao TRX e grava em `Tests/bin/Debug/net10.0/artifacts/mcp-wire`.

O servidor só publica identidade de fixture e lista vazia. Não contém tools de dados, conexões MongoDB, LiteDB, broker, UI, credenciais, login, modelo, provider ou HTTP. Não executa chamadas pagas.

## Cliente independente BCL — L0-MCP-INDEPENDENT-CLIENT

[IndependentClient](IndependentClient/IndependentClient.csproj) é um executável .NET sem `PackageReference`, sem referência de projeto ao servidor e sem uso de tipos `ModelContextProtocol`. O [lock vazio](IndependentClient/packages.lock.json) e o `.deps.json` gerado confirmam ausência de dependências NuGet. Não adicionou pacote ao inventário existente. O harness de testes inicia esse executável em processo separado; ele próprio inicia o servidor e serializa/valida JSON-RPC usando somente `System.Text.Json`, streams e `Process` da BCL. Assim a implementação cliente não compartilha o serializer nem o negociador do SDK servidor.

| Cliente BCL fixado | Servidor SDK 2.2.0 | Resultado observado em Windows |
| --- | --- | --- |
| 2025-11-25 | Dual-era | Descoberta/listagem vazia, identidade/capabilities e IDs válidos; EOF código 0 |
| 2026-07-28 | Dual-era | Descoberta/listagem vazia, metadata e supportedVersions válidos; EOF código 0 |
| 2025-11-25 | Fixado 2025-11-25 | Mesmo sucesso sem negociação para outra revisão |
| 2026-07-28 | Fixado 2026-07-28 | Mesmo sucesso sem negociação para outra revisão |
| 2025-11-25 | Fixado 2026-07-28 | Erro rejeitado; harness retorna código 4 e `ProbeRejected`, sem sucesso nem fallback |
| 2026-07-28 | Fixado 2025-11-25 | Erro rejeitado; harness retorna código 4 e `ProbeRejected`, sem sucesso nem fallback |

O fluxo legado envia `initialize`, espera a revisão exata/identidade/capabilities, envia `notifications/initialized` e então `tools/list`. O moderno envia `server/discover` e `tools/list`, com metadata própria por request; não envia `initialize` nem `notifications/initialized`. Não misturar as eras para aparentar compatibilidade. Referências de formato: [lifecycle legado](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle) e [versionamento moderno](https://modelcontextprotocol.io/specification/2026-07-28/basic/versioning).

Cada processo usa ambiente mínimo, deadline de 15 segundos no cliente e proteção de 25 segundos no teste. O leitor independente rejeita frames acima de 65.536 caracteres, JSON inválido, ID inesperado, erro de protocolo, lista não vazia, stderr ou stdout residual. Os limites são do harness, não provam hardening do servidor. Fecha stdin para observar EOF natural e encerra a árvore do subprocesso em falhas. Não copia credenciais de ambiente, não executa tools e não lê arquivos de configuração do produto.

Mensagens observadas e sintéticas foram preservadas em [evidence/windows/independent](evidence/windows/independent); são JSON parseado dos pipes reais, não schemas oficiais nem snapshots usados como asserção. Os testes geram novamente oito JSONLs (duas direções × quatro cenários de sucesso) em `Tests/bin/Debug/net10.0/artifacts/mcp-independent-wire` e os anexam ao TRX. O stdout do **servidor** contém exclusivamente mensagens JSON-RPC. O stdout do **cliente de diagnóstico** contém um único resumo JSON; stderr contém somente código de falha sanitizado se houver rejeição.

Após build, execução avulsa em PowerShell:

```powershell
$mcpServer = (Resolve-Path eng/spikes/phase-07/mcp-sdk/Server/bin/Debug/net10.0/EsilvaSoft.SlopStudio.Spikes.McpServer.dll).Path
dotnet eng/spikes/phase-07/mcp-sdk/IndependentClient/bin/Debug/net10.0/EsilvaSoft.SlopStudio.Spikes.McpIndependentClient.dll $mcpServer 2025-11-25 2025-11-25
dotnet eng/spikes/phase-07/mcp-sdk/IndependentClient/bin/Debug/net10.0/EsilvaSoft.SlopStudio.Spikes.McpIndependentClient.dll $mcpServer 2026-07-28 2026-07-28
```

Omitir o terceiro argumento seleciona o servidor dual-era. Em Linux, fornecer o caminho absoluto equivalente para `server.dll`; execução Linux ainda pendente. O uso do `dotnet` do ambiente é restrito a este harness de desenvolvimento e não define política de descoberta de executáveis para o produto.

## Reprodução

Da raiz do repositório, em Windows ou Linux com .NET 10:

```text
dotnet restore eng/spikes/phase-07/mcp-sdk/McpSdkSpike.slnx --locked-mode --configfile eng/spikes/phase-07/mcp-sdk/NuGet.Config
dotnet build eng/spikes/phase-07/mcp-sdk/McpSdkSpike.slnx --no-restore
dotnet test eng/spikes/phase-07/mcp-sdk/McpSdkSpike.slnx --no-build --no-restore --logger "trx;LogFileName=mcp-sdk.trx"
```

Output final observado:

```text
Compilação com êxito.
    0 Aviso(s)
    0 Erro(s)
Aprovado! – Com falha: 0, Aprovado: 10, Ignorado: 0, Total: 10
```

TRX local: `Tests/TestResults/mcp-sdk.trx` (ignorado pelo Git). O sandbox inicialmente impediu leitura do NuGet.Config do usuário; restore foi reexecutado com permissão de ferramenta e a configuração exclusiva do spike. Não houve bypass de certificado ou alteração de configuração global. Build e testes rodaram sem elevação.

Em PowerShell 7, `./eng/spikes/phase-07/mcp-sdk/Collect-DependencyEvidence.ps1` recompõe [dependency-evidence.json](dependency-evidence.json) a partir dos locks e pacotes restaurados. Falha se o `contentHash` do metadata NuGet divergir do lock. Registra separadamente o hash de conteúdo NuGet, SHA-512 do arquivo de pacote e SHA-256 calculado do arquivo; essas representações não são intercambiáveis em pacotes assinados. Registra origem, commit quando fornecido pelo pacote, licenças, hashes de notices e bibliotecas nativas encontradas via PE sem metadata/arquivos SO e dylib. É inventário de pesquisa, **não SBOM de distribuição aprovado**.

## Versão, origem e licenças

[ModelContextProtocol 2.2.0 no NuGet](https://www.nuget.org/packages/ModelContextProtocol/2.2.0), publicação estável de 13/08/2026, autor/publisher `ModelContextProtocol`. A [release v2.2.0](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.2.0) e o nuspec do pacote apontam para `6fa3825973949a9c4f0cd8af344e15a8db09dc35`. O `.nupkg` restaurado tem SHA-256 `d9704e12d5412c97147c74dd89524410bbad86e09bc96472134d9c7269d5c6fc`. Os clientes fixam a versão explicitamente, conforme [McpClientOptions da tag](https://github.com/modelcontextprotocol/csharp-sdk/blob/v2.2.0/src/ModelContextProtocol.Core/Client/McpClientOptions.cs), evitando interpretar fallback automático como prova da revisão escolhida.

Os dois nuspec MCP declaram `Apache-2.0`. A [licença do commit](https://raw.githubusercontent.com/modelcontextprotocol/csharp-sdk/6fa3825973949a9c4f0cd8af344e15a8db09dc35/LICENSE) também documenta contribuições legadas MIT e documentação CC-BY-4.0. Cópia integral preservada em [licenses/ModelContextProtocol-2.2.0-LICENSE](licenses/ModelContextProtocol-2.2.0-LICENSE), SHA-256 `7c6a686d2b34b86ad8c726e0ac4d76903b525f251d73e617f8db3879fa8f11fc`. Os pacotes MCP restaurados não contêm NOTICE separado; não presumir que essa observação dispensa revisão de distribuição.

Inventário efetivamente resolvido: **25 pacotes, 12 no grafo do servidor e 13 somente de teste**. Licenças abaixo são as declaradas nos nuspec reais. Versões/hash de cada pacote constam nos locks e no inventário JSON; não se atribuiu automaticamente a licença do MCP às transitivas.

| Pacote | Versão | Escopo | Licença declarada |
| --- | --- | --- | --- |
| ModelContextProtocol | 2.2.0 | Servidor | Apache-2.0, observar licença legada acima |
| ModelContextProtocol.Core | 2.2.0 | Servidor/transitiva | Apache-2.0, observar licença legada acima |
| Microsoft.Extensions.AI.Abstractions | 10.8.3 | Servidor/transitiva | MIT |
| Microsoft.Extensions.Caching.Abstractions | 10.0.10 | Servidor/transitiva | MIT |
| Microsoft.Extensions.Configuration.Abstractions | 10.0.10 | Servidor/transitiva | MIT |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.10 | Servidor/transitiva | MIT |
| Microsoft.Extensions.Diagnostics.Abstractions | 10.0.10 | Servidor/transitiva | MIT |
| Microsoft.Extensions.FileProviders.Abstractions | 10.0.10 | Servidor/transitiva | MIT |
| Microsoft.Extensions.Hosting.Abstractions | 10.0.10 | Servidor/transitiva | MIT |
| Microsoft.Extensions.Logging.Abstractions | 10.0.10 | Servidor/transitiva | MIT |
| Microsoft.Extensions.Options | 10.0.10 | Servidor/transitiva | MIT |
| Microsoft.Extensions.Primitives | 10.0.10 | Servidor/transitiva | MIT |
| Microsoft.NET.Test.Sdk | 17.14.1 | Teste | MIT |
| NUnit | 4.6.1 | Teste | MIT |
| NUnit3TestAdapter | 5.0.0 | Teste | MIT |
| Microsoft.ApplicationInsights | 2.22.0 | Teste/transitiva | MIT |
| Microsoft.CodeCoverage | 17.14.1 | Teste/transitiva | MIT |
| Microsoft.Testing.Extensions.Telemetry | 1.5.3 | Teste/transitiva | MIT |
| Microsoft.Testing.Extensions.TrxReport.Abstractions | 1.5.3 | Teste/transitiva | MIT |
| Microsoft.Testing.Extensions.VSTestBridge | 1.5.3 | Teste/transitiva | MIT |
| Microsoft.Testing.Platform | 1.5.3 | Teste/transitiva | MIT |
| Microsoft.Testing.Platform.MSBuild | 1.5.3 | Teste/transitiva | MIT |
| Microsoft.TestPlatform.ObjectModel | 17.14.1 | Teste/transitiva | MIT |
| Microsoft.TestPlatform.TestHost | 17.14.1 | Teste/transitiva | MIT |
| Newtonsoft.Json | 13.0.3 | Teste/transitiva | MIT |

Nenhum binário nativo foi encontrado nos 12 pacotes do grafo do servidor. O harness de testes inclui componentes nativos de CodeCoverage e TestHost; o inventário registra caminhos, mas não homologa esses componentes para distribuição. Vários pacotes Microsoft e NUnit incluem notices de terceiros; seus hashes estão no inventário. Preservar licença/copyright MIT e Apache, atribuições/notices aplicáveis e identificar modificações continua requisito antes de empacotar. Nenhum candidato foi promovido à solução nem ao instalador.

Consulta observada em 22/09/2026:

```text
dotnet list eng/spikes/phase-07/mcp-sdk/McpSdkSpike.slnx package --vulnerable --include-transitive --no-restore --config eng/spikes/phase-07/mcp-sdk/NuGet.Config
O projeto fornecido `Server` não tem nenhum pacote vulnerável, considerando as fontes atuais.
O projeto fornecido `Tests` não tem nenhum pacote vulnerável, considerando as fontes atuais.
```

Isso representa o feed consultado, não uma garantia de ausência de vulnerabilidades.

## Limites e próximos gates

- Linux nativo ainda não executado; código portável não é evidência de suporte Linux.
- Codex, Claude, Copilot e outros clientes externos não homologados; há evidência SDK↔SDK e cliente BCL independente↔SDK 2.2.0, somente para descoberta/listagem sintética vazia.
- Cancelamento concorrente, limites de payload, crash, duas sessões, broker/autenticação, ferramentas e autorização permanecem fora deste spike mínimo.
- Schemas oficiais integrais, suíte de conformidade, SBOM final por RID e revisão dos notices na distribuição permanecem gates.
- AC-01..AC-20 permanecem pendentes. Esta evidência satisfaz somente a prova mínima de descoberta STDIO das duas eras no host Windows observado.
