# Revisão editorial L5 — referências após L2

Auditoria dos links locais em `docs/MVP/02-requisitos-e-estado.md`, `AGENTS.md` e `README.md` contra o checkout atual, após as remoções L2. Somente `docs/Remover/inventario-tecnico.md` contém links que agora apontam para arquivos excluídos. Não encontrei links locais quebrados em `docs/README.md`, `AGENTS.md` ou `README.md`.

## Links afetados no inventário

As linhas abaixo documentam fontes e testes que existiam durante a inspeção L1 e foram removidos no L2. Manter o caminho removido como texto de evidência, sem hiperlink, marcar que a unidade foi removida, e apontar os consumidores/cobertura atuais apenas para arquivos existentes.

| Local em `inventario-tecnico.md` | Links agora quebrados | Orientação editorial |
| --- | --- | --- |
| linha 66, tabela REM-006 | `src/EsilvaSoft.KapibaraStudio.Desktop/ViewModels/WorkspaceTabViewModel.Analysis.cs` | Registrar como arquivo removido no L2. Separar a decisão sobre execução agregada ainda visível em `WorkspaceToolsWindow.axaml`/`MainWindowViewModel.Query.cs` e explain mantido no serviço. |
| linha 68, tabela REM-006 | `src/EsilvaSoft.KapibaraStudio.Infrastructure/MongoshScriptExecutionService.cs`; `src/EsilvaSoft.KapibaraStudio.Application/IScriptExecutionService.cs`; `src/EsilvaSoft.KapibaraStudio.Infrastructure.System/Processes/LocalMongoshScriptProcessRunner.cs` | Manter caminhos como evidência do corte executado, sem link. Ligar só a `WorkspaceService.cs` e `ServiceCollectionExtensions.cs` atuais se a narrativa discutir a cadeia restante e DI. |
| linha 131, complemento REM-006 | `src/EsilvaSoft.KapibaraStudio.Application/IMongoshScriptProcessRunner.cs`; `src/EsilvaSoft.KapibaraStudio.Infrastructure.System/Processes/LocalMongoshScriptProcessRunner.cs` | Marcar interface/runner removidos; indicar que o serviço/registro foi cortado no L2. |
| linha 132, complemento REM-006 | `src/EsilvaSoft.KapibaraStudio.Infrastructure/MongoshScriptTemplate.cs`; `src/EsilvaSoft.KapibaraStudio.Infrastructure/MongoshOutputParser.cs`; `src/EsilvaSoft.KapibaraStudio.Infrastructure/ParsedMongoshOutput.cs` | Manter como fontes removidas e remeter à evidência de substituição de cobertura. Não criar links para um arquivo novo só porque contém casos semelhantes; linkar cobertura preservada, por exemplo `UuidConsoleTests.cs`, `BsonDatePresentationTests.cs` ou testes Console, somente para alegações que eles realmente comprovem. |
| linha 133, complemento REM-006 | `tests/EsilvaSoft.KapibaraStudio.UnitTests/MongoshOutputParserTests.cs`; `tests/EsilvaSoft.KapibaraStudio.UnitTests/MongoshProcessOrchestrationTests.cs`; `tests/EsilvaSoft.KapibaraStudio.UnitTests/MongoshScriptExecutionServiceFailureTests.cs`; `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/MongoshProcessAdapterTests.cs` | Registrar como testes eliminados junto à cadeia removida, sem link. Apontar aos testes substitutos somente para a cobertura que permaneceu. |

Os links a `WorkspaceTabViewModel.cs`, `WorkspaceDraft.cs`, `WorkspaceSession.cs`, `WorkspaceTabView.axaml`, `LocalizationViewModel.cs`, `TextSessionPersistenceTests.cs`, `AdvancedAggregationUiTests.cs` e `ConsoleMongoIntegrationTests.Aggregation.cs` continuam resolvendo; reescrever sua descrição de L1 para refletir os consumidores atuais, sem tratá-los como prova automática de validação completa.

## Recomendações para fechamento L5

- Atualizar `inventario-tecnico.md` com resultado executado por fonte: link apenas para artefatos presentes; caminhos removidos podem permanecer em crase como histórico factual, identificados como removidos no L2.
- Vincular a estratégia/resultado de compatibilidade em `compatibilidade-modos-legados.md` e a evidência L2 aceita quando existente. Não apontar para nomes de testes alternativos que não cubram o mesmo comportamento.
- Revisar a tabela REM-006: além de `WorkspaceTabViewModel.Analysis.cs`, a inspeção atual não encontra `AggregationPipeline`/`ExecuteAggregationCommand` em `WorkspaceToolsWindow.axaml` ou `MainWindowViewModel.Query.cs`; a seção agregada separada também foi retirada do Desktop. `AggregationQuery`, `IMongoWorkspaceService.AggregateAsync`/`ExplainAggregationAsync` e implementação em Infrastructure ainda existem, mas a busca atual encontra principalmente testes/serviços, enquanto o Console executa aggregate pelo runtime. Atualizar inventário para explicitar a decisão sobre APIs/serviços residuais e ancorar preservação em `ConsoleMongoIntegrationTests.Aggregation.cs`; não descrever a UI separada como ainda presente.
- Após as edições L5, repetir a verificação de links locais para `docs/MVP/02-requisitos-e-estado.md`, `AGENTS.md` e `README.md`; documentar novas remoções de fonte/teste como texto, não links mortos.

Este relatório identifica apenas referências editoriais. Não atualiza inventário, requisitos, fase, TODO ou evidências de execução; essa reconciliação é trabalho do lote L5.

## Dependências e localização residual

### Projetos e pacotes

A cadeia removida não exigia pacote NuGet exclusivo identificado nesta inspeção. `LocalMongoshScriptProcessRunner` usava `System.Diagnostics.Process`, `System.Text`, `System.Text.Json` e APIs de arquivo, todos do runtime .NET, e localizava `mongosh` como executável externo (`mongosh[.exe]` ou `SLOPDATAADMIN_MONGOSH_PATH`); não havia pacote do mongosh no projeto/lockfile. O `Infrastructure.csproj` mantém `Jint` e `MongoDB.Driver`: `Jint` segue usado por `ConsoleRuntime`, e MongoDB.Driver é infraestrutura central. `Infrastructure.System.csproj`/lockfile mantém LiteDB, Copilot SDK, SQLite, DBus e logging por outros consumidores; remover a execução externa não justifica removê-los.

Na execução, não remover projetos ou referências de projeto por nome. A cadeia foi composta por fontes dentro de projetos compartilhados; revalidar `.csproj`, `packages.lock.json`, a solução e o restante do código antes de qualquer ajuste. Não alterar lockfiles manualmente.

### Chaves de localização para revisão L5

O mapeamento L2 reportado pela inspeção encontrou sem consumidores atuais, além da definição na `LocalizationViewModel.cs`, estas chaves que podem ser retiradas após checagem final de toda a solução: `aggregationInitial`, `analysisCancelled`, `analysisFailed`, `analyzePipeline`, `analyzePipelineTip`, `analyzingPipeline`, `capturedPipelinePlan`, `executePipeline`, `inputExtendedJson`, `pipelineJsonExample`, `pipelineNone`, `pipelineResult`, `pipelineReturned`, `planAvailable`, `scriptCompleted`, `scriptConsoleHeader`, `scriptErrorsHeader`, `scriptExitCode`, `scriptResultsHeader`, `suggestStages`, `toolsAggregations`, `historyNotSaved`, `resultAggregation`, `scriptFailed`, `scriptMetrics`, `scriptMongosh`, `scriptNoDocuments`, `untitledScript` e `suggestAggregationHint`.

`scriptNoOutput` ainda aparece em inicializador de `MainWindowViewModel.ScriptResults`, e `scriptResultsPlaceholder` em inicializador de `MainWindowViewModel.Scripts`; as buscas atuais não encontraram binding consumidor. Tratar ambos como possíveis sobras, não declarar órfãos sem revisar remoção de propriedades e componentes de UI no lote L5. Chaves com "pipeline", "aggregation" ou "mongosh" não devem ser removidas em bloco: usos de views, materialização, Console e inferência continuam, por exemplo `viewPipeline`, `materializationPipeline`, `viewPipelineUpdated`, `aiCatalogPipelineUnvalidated` e `derivedExplain`.

O mapeamento é uma lista para reconciliação, não autorização de apagar automaticamente. Confirmar cada chave em código, XAML, testes, snapshots de localização e docs antes da remoção; depois executar as guardas de localização disponíveis.
