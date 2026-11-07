# Task-R1 — inspeção L1 e compatibilidade

Snapshot de origem: `8361f73518b0ed2a0c931ea4348d557daf863786`; Windows, 06/11/2026. Alterações preexistentes: pasta `tasks/` não rastreada. Inspeção de fontes, não homologação.

## REM-006

Estratégia revisada antes da implementação: [compatibilidade dos modos legados](compatibilidade-modos-legados.md). A inspeção confirmou modos ocultos porém ainda executáveis por restauração, cadeia exclusiva de processo mongosh e consumidores compartilhados de aggregate, inferência, arquivos e histórico. Recuperação aprovada: Texto com aviso explícito, conteúdo intacto e campo aditivo `LegacyMode`; escolher Console requer ação do usuário. A origem permanece no snapshot após essa escolha, e o aviso de execução desativada só aparece enquanto o modo é Texto. Sessão continua versão 2, sem nova conexão ou exclusão LiteDB.

## Preparação L2

Cadeia exclusiva retirada: `IScriptExecutionService`, `IMongoshScriptProcessRunner`, `MongoshScriptExecutionService`, `LocalMongoshScriptProcessRunner`, `MongoshScriptTemplate`, `MongoshOutputParser`, `ParsedMongoshOutput` e `ScriptExecutionResult`; despachos Script/Agregação, comando Explain do editor separado e bindings exclusivos. Contratos aggregate/explain e inferência do Console permanecem. Dialetos léxicos do autocomplete mantêm consumidores no catálogo de sintaxe e arquivos de corpus; não representam fluxo executável do Studio.

Testes exclusivos de processo/template removidos. Cobertura compartilhada preservada em `ConsoleWorkspaceTests.ConsoleTabsKeepCapturedDestinationAndCancelIndependently`, `ResultPanelTests.TabsKeepTheirOwnResultsViewAndSelectionWhenResponsesArriveOutOfOrder`, `UuidConsoleTests.ConsoleConstructorsSendExplicitRepresentationsAndStringsStayStrings`, testes restantes de `BsonDatePresentationTests`, `EnvironmentVaultTests` e testes Console de ambientes. `AdvancedAggregationUiTests` passa a verificar inferência no Console e recuperação visual do pipeline legado. Novos `LegacyDraftRecoveryTests` verificam texto/origem/metadados/entrada e roundtrip, opt-outs e bloqueio dos modos retirados.

Validação da implementação ainda pendente neste registro. A proteção existente de sessão ilegível e falha de persistência é mantida; testes novos não significam homologação MongoDB real, Linux, leitor de tela ou diálogos nativos.

## Compilação L2

`dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -p:UsedAvaloniaProducts=` em Windows/.NET SDK local: aprovado, zero avisos e erros. O parâmetro contorna apenas a tarefa externa Avalonia. Testes e inspeção visual seguem pendentes de execução no conjunto. O controle de entrada JSON legado permanece disponível em Opções durante recuperação, com opt-in revogável.

Cobertura equivalente adicional preservada: `ConsoleRuntimeTests.DateCallsAndConstructorsSendBsonDatesToTheDriver` e `ConsoleRuntimeTests.EnvironmentSnapshotAndExtendedJsonRemainStableDuringExecution`. O teste LiteDB real `TextSessionPersistenceTests.LegacyRecoverySurvivesRealLiteDbOwnerReopening` cobre os dois modos, reabertura do proprietário e igualdade integral dos metadados.

## Evidência L2 final

Build solução aprovado, zero avisos/erros. Regeneração visual final compilou IntegrationTests com `-p:UsedAvaloniaProducts= -p:UseSharedCompilation=false /nr:false /m:1` após servidor de build compartilhado ficar sem progresso; analisadores permaneceram ativos.

Regressões focadas: UnitTests 37/37 aprovados, `TestResults/task-r1-l2-sol.trx`; IntegrationTests 13/13 aprovados no arquivo homônimo (recuperação LiteDB, sessões ilegíveis, isolamento de execução/ambiente, renderização e guarda de fronteira); reexecução final somente entrada JSON: 1/1 aprovado, `IntegrationTests/TestResults/task-r1-l2-input-sol.trx`.

PNGs finais de entrada: `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/bin/Debug/net10.0/ui-evidence/render-f560418649e9414da831efb7687657ae/legacy-input-{idioma}-{tema}-{escala}.png`, 24 combinações (4 idiomas, claro/escuro, 100/150/200%, janela960×620). Inspecionados diretamente pt-BR/claro100% e zh-CN/escuro200%: editor inteiro dentro do flyout, checkbox legível abaixo, botão adaptar alcançável e sem rolagem horizontal global. O teste também seleciona texto para cópia, edita e revoga/restaura opt-in. A largura do conteúdo foi limitada localmente ao flyout, sem alterar janela/editor principal.

Recente com entrada JSON persistida abre como Texto/origem Script recuperável; arquivo normal continua Console. Testes verificam ambos sem executar nem alterar estado salvo. Texto/origem dos históricos de execução passam pela mesma recuperação. `WorkspaceToolsWindow` perdeu também a superfície separada de pipeline e comandos exclusivos correspondentes.

Limite: Console não expõe explain; permanecem explain de consulta compartilhado e contratos/executor de explain de agregação cobertos por chamadas diretas em integração. Não há alegação de UX explain no Console. Homologação nativa, Linux, MongoDB real, clipboard real e leitor de tela continuam pendentes. Suite completa final fica a cargo do fechamento L5.
