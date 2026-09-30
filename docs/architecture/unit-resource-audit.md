# Auditoria de recursos externos nos testes — 30/09/2026

Este registro acompanha a migração para testes unitários sem acesso direto a recursos do host. A guarda cobre `UnitTests`, `Infrastructure.Agents.Tests`, `TestSupport` e os arquivos `*Tests.cs` do projeto Benchmarks; não prova o comportamento de dependências transitivas nem substitui a revisão de cada composição. A meta de arquitetura continua em andamento.

## Migrações feitas

- Testes que abrem LiteDB real, exercitam recuperação/schema/histórico persistidos ou usam composição com o owner LiteDB foram movidos para `IntegrationTests`. Os casos de regras e contratos continuam em unidade com doubles. A integração usa armazenamento e pastas sintéticos; a aplicação continua com um único owner LiteDB registrado em DI.
- Fixtures de renderização que gravam PNGs, capturas explícitas de golden files e verificações que leem a árvore de fontes foram movidas para integração. Os dados de golden/case usados pela unidade são embutidos no assembly e lidos como recursos em memória. Mudanças visuais ainda exigem inspeção dos PNGs reais conforme `AGENTS.md`.
- Casos de modelos locais que carregam runtime/tokenizer ou acessam arquivos de modelos foram movidos para integração. Permanecem em unidade as regras e os casos com runtime falso.
- Os testes de Claude, Copilot, Codex, broker e mongosh que inicializam SDKs, processos ou transporte real foram separados da unidade. Providers e políticas usam portas e doubles; autenticação Claude permanece delegada à CLI oficial.
- `MongoAgentWriteSource` e `MongoOperationContext` recebem `IMongoClientPool`; os testes unitários usam pool falso. A prova de reutilização do pool concreto está na integração.
- Exportação de texto de auditoria e consultas agora passa por `ITextExportFileService`, com adapter local em `Infrastructure.System`; a unidade usa fake e a semântica de criação de arquivo está em integração.
- A composição compartilhada de workspace da unidade usa repositórios e arquivos em memória do `TestSupport`, que depende apenas de `Application`.

## Resultado da verificação estática

`eng/audit-system-boundaries.ps1` encontrou dois hits em UnitTests e um em Infrastructure.Agents.Tests; todos foram revisados semanticamente:

- `UnitTests/EmbeddedTestData.cs`: `StreamReader` lê um stream retornado por `Assembly.GetManifestResourceStream`, não um caminho do sistema de arquivos.
- `Infrastructure.Agents.Tests/OpenAi/OpenAiFixtures.cs`: `HttpClient(handler)` usa `HttpMessageHandler` falso, configurado para falhar em solicitações não roteirizadas; não cria transporte de rede.
- `UnitTests/Mcp/AgentMcpConsoleTests.cs`: `StreamWriter` escreve em `MemoryStream` controlado pelo fake de console; não abre stdout do processo.

Caminhos de fixture passaram a usar `SyntheticPaths`, inclusive a normalização de `MemoryAgentFiles`, sem consulta ao diretório atual. O contador de sensibilidade do fake é fixo, sem consultar o SO. `MongoWorkspaceService` agora aceita `IMongoClientPool`; testes que recusam operações usam pool que falha e conta chamadas, além do fake de acesso a exportação, para demonstrar zero solicitações antes da validação.

`UnitSystemBoundaryIntegrationTests` protege essa fronteira procurando chamadas comuns a arquivos, processos, console, ambiente do host, segurança nativa, runtimes reais, `AppContext.BaseDirectory` e `Path.GetFullPath` sem base explícita nos quatro conjuntos de fonte. A própria varredura roda em `IntegrationTests`, porque enumerar e ler arquivos do repositório é acesso ao sistema de arquivos. Regressões do matcher cobrem method groups de `Environment` e caminhos com diretório atual implícito. Regex não detecta todos os aliases, factories, inicializadores, DI ou acessos indiretos; novos adapters ou mudanças em composição ainda exigem revisão semântica.

## Trabalho restante

- A varredura da suíte não prova que toda dependência transitiva de um teste seja incapaz de acessar o host. Manter fakes explícitos e revisar os caminhos de composição quando novos projetos forem referenciados. `UnitSystemBoundaryIntegrationTests` passou na execução integrada atual, mas regex não prova ausência de acesso por aliases, factories, DI, SDKs ou inicializadores.
- Fora de `Infrastructure.System`, o inventário ainda lista criação de `HttpClient` condicionada ao handler injetado e composição do SDK `MongoClient`; os caminhos analisados usam handlers fakes ou não abrem conexão até uma operação explícita. Console STDIO do MCP, armazenamento ONNX e medição de memória têm adapters especializados. Hits de streams são assembly/memória e não acessam caminhos do host.
- O teste Copilot que sondava o host foi separado para integração; o runner de ONNX real e testes de escrita/leitura de relatórios Benchmarks também foram movidos para `IntegrationTests`, com diretório temporário contido para o teste de arquivo. A suíte unitária de Benchmarks agora valida apenas agregação e harness com ambiente sintético. Restore `--locked-mode` CPU passou offline usando o cache local e configuração NuGet temporária. Build CPU da solução: **0 avisos, 0 erros**. Suíte completa: Benchmarks **41/41**, Infrastructure.Agents.Tests **198/198**, UnitTests **3323 aprovados/20 ignorados**, IntegrationTests **868 aprovados/14 ignorados**. Builds cruzados `linux-x64` de Desktop e McpServer passaram. Execução/testes nativos em Linux e adapters `Explicit` continuam pendentes; este host não tem distribuição WSL instalada.

## Critério de conclusão

Concluir apenas quando os testes unitários e seus suportes não conseguirem alcançar recursos reais pelas dependências diretas ou transitivas, os efeitos de produção estiverem atrás de portas com adapters apropriados, as integrações tiverem recursos sintéticos contidos, e restore/build/suítes e validação Windows/Linux tiverem evidência registrada. Não alterar asserts ou goldens para contornar regressões.
