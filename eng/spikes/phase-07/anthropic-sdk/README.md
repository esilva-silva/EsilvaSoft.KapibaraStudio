# Spike Anthropic C# — lote 0 da Fase 7

**Resultado parcial em 22/09/2026 (America/Sao_Paulo):** versão oficial publicada selecionada, origem/tag/licenças registradas, restore travado e fluxo de configuração/streaming **compilado**. Nenhuma chamada à Claude API, API key, login, modelo, tool calling ou comportamento de streaming foi homologado. Este projeto não integra a solução nem o produto EsilvaSoft.SlopStudio.

## Versão e origem

- `Anthropic` **12.50.0**, última versão estável observada diretamente no [índice NuGet](https://api.nuget.org/v3-flatcontainer/anthropic/index.json); referência exata `[12.50.0]`, sem atualização flutuante.
- [Pacote publicado](https://www.nuget.org/packages/Anthropic/12.50.0), listado em 22/09/2026 às **16:33:21.417 UTC**. Autoria no nuspec: `Anthropic`; proprietários exibidos na galeria: `Anthropic`, `felix-anthropic`, `packy-anthropic`.
- [Release oficial Anthropic-v12.50.0](https://github.com/anthropics/anthropic-sdk-csharp/releases/tag/Anthropic-v12.50.0), publicada em **22/09/2026 16:32:16 UTC**, `prerelease=false`. A tag e o nuspec apontam para o mesmo commit **`2beeb9f9b402b1cdb8030e7cdc38cc6cae5d42aa`**.
- A [documentação oficial C#](https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/csharp) identifica a família 10+ como oficial; as versões antigas até 3.x tinham outra origem. A seleção usa documentação, publisher, repositório, tag e pacote, não somente o nome.
- SHA-256 do `.nupkg`: **`a6cf2fa227e35fa4852d9ba448cbdd7031a4499bf8395177d897bb24c919448b`**. Hashes SHA-512 do arquivo e conteúdo NuGet constam em [dependency-evidence.json](dependency-evidence.json) e [packages.lock.json](packages.lock.json). São hashes distintos porque assinatura/representação do arquivo não são o hash de conteúdo normalizado NuGet.
- `dotnet nuget verify --all`: saída sem erro nos dois pacotes. Anthropic possui assinatura de **repositório NuGet.org**, que não equivale a assinatura de autor Anthropic. A transitiva possui assinatura de autor Microsoft e de repositório NuGet.org.

A página web indexada apontava 12.49.0 e `releases/latest` pode apontar um pacote Bedrock do monorepo. A versão foi decidida pelo índice NuGet e pela release específica, ambos consultados diretamente. Uma nova coleta pode encontrar outra versão mais recente, sem alterar a referência fixada.

## Dependências e licenças

| Pacote resolvido em net10.0 | Escopo / asset compilado | Licença e obrigação |
| --- | --- | --- |
| Anthropic 12.50.0 | Direto; `lib/net9.0/Anthropic.dll` | MIT no nuspec e na [licença da tag](https://github.com/anthropics/anthropic-sdk-csharp/blob/Anthropic-v12.50.0/LICENSE); preservar copyright e permissão |
| Microsoft.Extensions.AI.Abstractions 10.5.1 | Transitivo; `lib/net10.0/Microsoft.Extensions.AI.Abstractions.dll` | MIT no nuspec e na [licença do commit](https://github.com/dotnet/extensions/blob/2d4d2df0ba38ee9aa0ed363ddab33d7ae7880b6d/LICENSE); preservar copyright e permissão |

As licenças exatas estão copiadas em [licenses](licenses/), com hashes no inventário. O arquivo `SOURCE-NOTICES` preserva o aviso do repositório dotnet/extensions no commit da transitiva; seu escopo é o repositório, sem presumir que componentes de outros projetos sejam incorporados neste pacote. Não havia LICENSE/NOTICE embutido nos dois pacotes restaurados. Não foram encontrados assets nativos nesses pacotes; isso não homologa o runtime .NET ou a distribuição por RID.

O nuspec Anthropic também declara `System.Net.ServerSentEvents 10.0.1` e `System.Text.Json 10.0.6` para net9.0. O restore com SDK .NET 10 aplica pruning de pacotes já disponíveis no framework: eles constam em `packagesToPrune` de `obj/project.assets.json` e não no lock final. Outros TFMs podem produzir outra árvore. O inventário cobre **somente net10.0 sem RID**; Windows/Linux empacotados e SBOM do produto continuam pendentes.

Consulta de vulnerabilidades NuGet com transitivas: **nenhum pacote vulnerável reportado pelas fontes atuais** nesta execução. É uma observação datada, não garantia futura. Revisar atualizações por PR com nova versão exata, hashes, licença, audit e lock; não atualizar automaticamente durante conversa. A MIT do Slop permanece inalterada. Termos e cobrança da API são uma verificação separada; licença do SDK não concede créditos nem permite reutilizar sessão claude.ai/Claude Code.

## Limite do exemplo compilado

[StreamingContract.cs](StreamingContract.cs) é uma biblioteca sem entry point: build não instancia cliente, não enumera streams e não executa código da amostra. O método recebe modelo por parâmetro, usa texto sintético, limite de tokens, timeout, retries desabilitados e `CancellationToken`. A compilação valida nomes/tipos e a assinatura de `Messages.CreateStreaming`; não valida eventos recebidos, ordem, cancelamento remoto ou tradução para eventos internos do Slop.

Na versão fixada, `new AnthropicClient()` pode resolver credenciais/perfil antes de um object initializer. O exemplo passa `ClientOptions` com `ApiKey=null` e `AuthToken=null` **explicitamente definidos antes do construtor**, impedindo a auto-resolução observada no [código da tag](https://github.com/anthropics/anthropic-sdk-csharp/blob/Anthropic-v12.50.0/src/Anthropic/AnthropicClient.cs). `WebhookKey` também é nulo; não há leitura/copiar/gravar segredo na amostra. [NoNetworkHandler.cs](NoNetworkHandler.cs) bloqueia o envio caso o método seja invocado futuramente. Esse bloqueio é uma proteção estática da amostra, não uma alegação de teste executado.

Não foram executados `dotnet run`, testes do produto, testes HTTP simulados ou chamadas API. Somente ferramentas de restore/build/verificação de pacote e coleta pública acessaram NuGet/GitHub. Não há `IChatClient` com invocação automática de funções, Agent SDK, sidecar ou referência a projetos do Slop.

## Reprodução

Requisitos: SDK .NET 10 conforme `global.json` da raiz, PowerShell 7 para a coleta opcional, rede para NuGet/GitHub. Executar **neste diretório**:

```powershell
dotnet restore AnthropicSdkSpike.csproj --configfile NuGet.Config --locked-mode
dotnet build AnthropicSdkSpike.csproj --no-restore
dotnet list AnthropicSdkSpike.csproj package --include-transitive --vulnerable --no-restore
dotnet nuget verify .cache/packages/anthropic/12.50.0/anthropic.12.50.0.nupkg --all
dotnet nuget verify .cache/packages/microsoft.extensions.ai.abstractions/10.5.1/microsoft.extensions.ai.abstractions.10.5.1.nupkg --all
./Collect-DependencyEvidence.ps1
```

A coleta lê o lock e assets, confere hash de conteúdo com metadados NuGet, SHA-512 do arquivo e commit/tag; baixa licenças das revisões fixadas e regrava o inventário com horário da observação. Não lê credenciais e não chama a Claude API. `.cache/`, `bin/` e `obj/` são ignorados pelo Git. Props e NuGet.Config locais isolam o spike dos props/CPM/ONNX do produto. O `Directory.Packages.props` **da raiz**, solução e projetos de produto permanecem intocados por este spike.

Validação realizada: Windows **10.0.26200**, SDK **10.0.401**, runtime **10.0.12**, processo **win-x64**. Restore inicial e `--locked-mode` concluíram; build terminou com **0 avisos / 0 erros**. A primeira tentativa de restore dentro do sandbox falhou por acesso ao NuGet.Config do usuário; a repetição autorizada usou o config explícito do spike e concluiu. Nenhum resultado Linux é alegado.

## Gates ainda abertos

Autenticação API Key autorizada/cofre, conta/modelo, streaming e tool calling reais, concorrência, eventos tardios, cancelamento isolado, falhas de autenticação/rede, isolamento de configuração/logs, permissões do registry, execução em Linux, SBOM/distribuição e aceite dos termos de serviço. Nenhum AC-01 a AC-20 da Fase 7 é aprovado por este spike. O resultado permite apenas avaliar o contrato compilado e a dependência candidata antes de implementar o adapter.
