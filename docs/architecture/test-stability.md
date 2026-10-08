# Estabilidade dos testes e benchmarks manuais — 01/10/2026

## Teardown LiteDB no Windows — investigação de 07/10/2026

**Ocorrência na validação do catálogo seguro (ADR-066):** `PersistedSecretScanTests.EncodedCanaryIsDetected` falhou no mesmo `Directory.Delete`/`WORKSPACE-LOG.DB.tmp` em uso. UnitTests 3.442 e Agents 403 passaram; integração teve 1.018 aprovados/1 falha/5 ignorados. O caso isolado passou 1/1; a reexecução completa da integração passou 1.019/0 falhas/5 ignorados, sem mudar produção, asserções ou cleanup. [Logs/TRX e alcance](agent-tool-safety.md#evidência-automatizada--windows-07102026). A passagem posterior não identifica o proprietário do handle nem encerra a intermitência.

As execuções integrais atuais falharam em `Directory.Delete` de duas fixtures distintas, com `WORKSPACE-LOG.DB.tmp` em uso: `LiteDbAgentSchemaSamplingConsentTests.DeletingAProfileKeepsALegacyDocumentAsLegacyWhileRemovingItsGrants` (`artifacts/phase5-tool08-roundtrip/phase5-current-full-integration.trx`) e, na execução serial posterior, `ConnectionCredentialRecoveryTests.CrashAfterOsWriteResumesMigrationAndCleansUncommittedSaveAfterRestart` (`phase5-current-full-integration-serial.trx` no mesmo diretório). Ambos passaram isolados nos TRX `lite-db-delete-profile-isolated.trx` e `credential-recovery-cleanup-isolated.trx`. A passagem isolada não encerra a intermitência nem transforma essas execuções integrais em aprovação.

A revisão dos dois caminhos confirmou diretórios temporários únicos, acesso offline somente entre proprietários fechados e descarte em ordem: o `using var reopened` termina antes do `Workspace.Dispose`. O proprietário `LiteDbConnectionProfileRepository` executa operações e `Dispose` sob o mesmo `_gate`; os métodos usados aguardam o trabalho de persistência/cofre e as transações locais terminam dentro desse gate, sem `await` na transação. A recuperação de credenciais em background só é iniciada pela composição DI, ausente nessas duas instanciações diretas. Nenhum segundo proprietário ativo ou tarefa de acesso ao arquivo sem espera foi identificado nesses caminhos. Na fonte fixada [LiteDB 5.0.21, DiskService.Dispose](https://github.com/litedb-org/LiteDB/blob/v5.0.21/LiteDB/Engine/Disk/DiskService.cs#L309), os pools são descartados antes da exclusão do log; [StreamPool.Dispose](https://github.com/litedb-org/LiteDB/blob/v5.0.21/LiteDB/Engine/Disk/StreamFactory/StreamPool.cs#L56) fecha leitores e writer sincronamente. Isso descreve o contrato revisado, sem provar qual handle permaneceu aberto durante a falha.

Após cada execução histórica não havia processo dotnet/testhost ativo, e a inspeção posterior encontrou a pasta vazia. Os TRXs históricos contêm a exceção e o nome do arquivo, mas não PID, handle ou rastreamento de acesso no instante da recusa; portanto não permitem atribuir o bloqueio retrospectivamente ao testhost, ao LiteDB, ao Windows Search, antivírus ou outro processo. Esses possíveis agentes externos são hipóteses, sem evidência de autoria. Para identificar a causa é necessária captura do caminho absoluto e do proprietário do handle enquanto a falha está presente. Nenhuma correção de produção foi aplicada sem essa evidência; fixtures, asserções e cleanup imediato foram preservados, sem retries para ocultar a falha.

**Atualização F5-L00 — 07/10/2026:** as tentativas no sandbox falharam antes da discovery: o log VSTest mostra `testhost.exe` iniciado, listener em `127.0.0.1` e ausência de handshake até timeout. Fora do sandbox, o foco auxiliar `LegacyConnectionCredentialMigrationTests` passou 8/8, os dois testes previamente associados às falhas passaram 2/2 e IntegrationTests integral teve 1.026 resultados Passed, zero Failed e 76 resultados individuais NotExecuted. O TRX tem total 1.102, mas seu `ResultSummary/Counters` contradiz os resultados individuais ao registrar `notExecuted="0"`; o console reportou cinco ignorados. A conexão do testhost no sandbox e o handle LiteDB são problemas distintos: o primeiro foi isolado pela execução externa; o segundo continua sem autoria porque a exceção não se reproduziu. [TRX, log diagnóstico e consolidação](../phases/phase-05-v0.9.0/meta-de-encerramento.md#investigação-de-teardown--07102026). O código de produção permanece sem alteração.

## Continuação: descarte do cache no Ubuntu — 03/10/2026

O ZIP `logs_100615514147.zip` mostra **3.379 aprovados, uma falha e 20 ignorados** no Ubuntu. `DisposingCacheDrainsLoadsWaitingOnTheGlobalGateWithoutPublishingLateResults` observou duas chamadas à origem em vez de uma. As etapas seguintes não executaram após a falha unitária.

O descarte remove as conexões sob o lock, mas cancela seus tokens sequencialmente fora dele. Ao terminar a carga da primeira conexão, uma carga de outra conexão pode adquirir a vaga global antes do cancelamento do próprio token. Além disso, adquirir uma vaga não garante que o cancelamento ainda não tenha ocorrido. Agora, após adquirir os dois semáforos, a carga verifica o token e se o cache ainda mantém exatamente a conexão capturada, antes de acessar a origem. A liberação dos semáforos e a rejeição de resultados antigos permanecem.

`DisposalRejectsQueuedLoadsBeforeTheirConnectionTokenIsCancelled` controla a ordem com um callback de cancelamento: libera a origem da primeira conexão e aguarda a segunda carga terminar antes de permitir o cancelamento da segunda conexão. O teste reproduziu as duas chamadas incorretas no Windows antes da correção e passou depois. Nenhuma asserção ou timeout do teste original foi relaxado.

Restore locked e build Release com `UsedAvaloniaProducts=` passaram; build com zero avisos/erros. Os testes focados de metadados passaram **27/27**. A solução completa no Windows passou com **4.569 aprovados, zero falhas e 36 ignorados reportados**: UnitTests 3.381/20 ignorados, Agents 288/0 e IntegrationTests 900/16. Benchmarks não executaram. Após reforçar apenas o cleanup do novo teste, build e foco 27/27 foram reconferidos. Evidências em `TestResults/LinuxCache/{Before,Focused,FocusedFinal,Final}` e `TestResults/linux-cache-*.log`. A reexecução nativa no Ubuntu permanece pendente; a reprodução local não homologa Linux, MongoDB ou providers reais.

## Continuação: CI 36931654539

O ZIP `logs_100037908780.zip`, da [execução 36931654539](https://github.com/esilva-silva/EsilvaSoft.KapibaraStudio/actions/runs/36931654539), mostra Windows aprovado e Ubuntu com **3.358 aprovados, 2 falhas e 20 ignorados** na unidade. As etapas seguintes no Ubuntu foram puladas após a falha; esse run não comprova Agents ou a guarda de fontes no Linux.

- `CancellingTheTurnDuringTheWaitDeniesWithoutClaimingAnUncertainWrite`: a ponte respondia `Denied` ao cancelamento, mas o coordenador podia receber essa resposta antes de `WaitAsync` observar o token. O resultado/auditoria virava recusa em vez de cancelamento. O coordenador agora confere o token depois da resposta, sob o bloqueio que decide o ticket. Dois casos determinísticos fazem o prompt cancelar e devolver recusa/concessão já concluídas: ambos falharam antes da correção (`Rejected`/`Granted`, com ticket no segundo) e passaram depois (`Cancelled`, sem ticket). O runtime continua verificando auditoria `Cancelled`, nenhuma escrita e nenhuma alegação de rollback.
- `SecondWriteOfTheSameSessionIsBusyWhileTheFirstAwaitsApproval`: o script anunciava ambas as escritas sem aguardar a primeira entrar na aprovação. O runtime despacha em tarefas concorrentes; a ordem do stream não determina quem adquire a vaga. Se a segunda adquirir primeiro, ela aguarda o prompt que o teste só liberava depois de observar `Busy` para essa mesma chamada, terminando por timeout/`ToolOutcomeUnknown`. Agora um sinal do prompt libera o anúncio da segunda escrita somente enquanto a primeira já aguarda aprovação. As asserções de `Busy`, um prompt, sucesso da primeira e uma única escrita permanecem; o gate é liberado em `finally`.

A [implementação oficial de `Task.WaitAsync` no .NET 10](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs#L2583) retorna uma tarefa já concluída antes de consultar o cancelamento. O teste de regressão controla essa ordem diretamente, sem depender do sistema operacional ou de pausas reais. Nenhum timeout ou expectativa funcional foi relaxado; benchmarks continuam fora de CI/release.

Validação focada Windows: **36/36**, incluindo os dois casos novos e as duas fixtures do CI. Três execuções adicionais com `DOTNET_PROCESSOR_COUNT=1`, `2` e `4` passaram **36/36** cada, exercitando diferentes configurações do pool. Restore locked passou após permitir o acesso ao NuGet.Config bloqueado pelo sandbox; build Release com `UsedAvaloniaProducts=` passou com zero avisos/erros. A solução completa passou: **4.509 aprovados, zero falhas e 25 ignorados reportados** (UnitTests 3.362/20 ignorados, Agents 254/0, Integration 893/5). Benchmarks não foram executados; `IsTestProject=false` foi reconferido. Evidências em `TestResults/CI-36931654539/{Before,Focused,Scheduling,Final}` e `TestResults/ci-36931654539-*.log`. O novo diff ainda depende da reexecução do CI Ubuntu. Não há homologação de providers/MongoDB reais nem alteração visual.

## Evidência e diagnóstico

Os logs `logs_100011460831` mostram UnitTests com 3.360 aprovados/20 ignorados e Infrastructure.Agents.Tests com 254 aprovados nos dois sistemas. A falha Windows ocorreu em `UnitSystemBoundaryIntegrationTests`: `AgentMcpChannelProvisionerLifecycleTests` e `LinuxAgentAdapterContractTests` normalizavam caminhos relativos usando o diretório atual. Linux não executava essa guarda. Isso era diferença de cobertura, não evidência de uma falha unitária exclusiva do Windows.

Os caminhos desses cenários agora são absolutos sintéticos. O CI executa as mesmas duas suítes unitárias, em etapas separadas, nos dois sistemas. Windows executa também a integração; Linux executa somente a guarda estática de fontes, além da unidade. Essa guarda lê o repositório e não inicia CLI, conexão, cofre ou runtime Linux. A lista explícita de projetos exclui o projeto Benchmarks e também se aplica à release, que reutiliza `ci.yml`.

## Correções de sincronização

- Os testes de desconexão capturam a tarefa da carga antes de desconectar e aguardam seu término, incluindo a carga na fila. Contar retornos do fake não demonstra que o cache terminou de descartar/publicar o resultado.
- A espera real de 50 ms escondia uma corrida: `SemaphoreSlim.Dispose()` podia ocorrer enquanto `WaitAsync()` ainda estava pendente. O cache registra as cargas antes de agendá-las, cancela a conexão imediatamente e só descarta os semáforos da conexão e global depois da última carga. Nenhum resultado da conexão removida é aceito; cargas que ignoram cancelamento conservam seus recursos até terminar.
- O teste dos escopos de metadados bloqueia a origem até capturar as tarefas. Assim o estado `Loading` não depende de o pool executar a origem antes da asserção. Esse cenário permite três cargas simultâneas para observar os três escopos de campo; o limite padrão do produto continua dois por conexão e seus testes próprios são preservados.
- Autosave recebe `TimeProvider` opcional, com `TimeProvider.System` na aplicação. A política de 750 ms permanece. Os testes avançam o relógio, verificam o reinício do debounce, aguardam o sinal de conclusão e cobrem falha visível, retry e descarte com gravação pendente.
- O editor expõe a tarefa da última sugestão automática, incluindo publicação ou rejeição. A fixture Headless processa os jobs do dispatcher e aguarda essa tarefa para conferir ghost, opt-in, supressão e resposta antiga. A lista explícita usa espera por condição com watchdog monotônico. Saíram a espera ativa e os loops de duração fixa dessa fixture.
- Preempção ONNX já usava relógio manual; saíram as pausas reais que antecediam seu avanço. O teste de consumo concorrente de aprovação usa barreira assíncrona em vez de bloquear threads do pool. A análise propositalmente bloqueada tem um watchdog para não prender o runner indefinidamente.
- Ocorrência histórica adicional, sem snapshot/TRX identificado: o acompanhamento registrou bloqueio de `WORKSPACE-LOG.DB.tmp` na limpeza de `LegacyConnectionCredentialMigrationTests`. Não confundir esse relato com os dois casos rastreados nos TRXs integrais de 07/10 acima nem com o foco auxiliar 8/8 desta revisão. A pasta é exclusiva, o owner já foi descartado e a remoção verifica containment. Somente erros Windows 32/33 de compartilhamento/bloqueio são repetidos por até cinco segundos; erros de permissão e bloqueios persistentes falham. A causa externa do bloqueio não foi determinada. A instrumentação opt-in em IntegrationTests foi validada com processo filho segurando arquivo sintético (13/13 no foco); o Restart Manager identificou o usuário sintético, mas a falha histórica não foi reproduzida. Nenhum corpo de teste é repetido para transformar falha em sucesso.

## Separação de desempenho

O projeto Benchmarks continua compilável na solução, mas `IsTestProject=false` impede sua execução padrão por `dotnet test`. Seus testes NUnit de ferramentas exigem `EnableBenchmarkTests=true` explicitamente em desenvolvimento. Nenhum workflow usa esse opt-in. `IsPublishable=false` e `IsPackable=false` continuam vigentes.

`NameTableAllocationTests` e a medição de latência de `TraditionalPreemptiveCompletionProvider` foram transferidos da unidade para Benchmarks, mantendo os orçamentos e asserções de medição. O teste Headless de p95 de apresentação, que depende da UI, permanece na integração com categoria `Benchmark` e atributo `Explicit`. Os casos funcionais continuam automáticos; nenhuma golden ou expectativa funcional foi relaxada.

Comandos manuais, fora de CI/release:

```powershell
# BenchmarkDotNet, em máquina de desenvolvimento com configuração registrada
dotnet run -c Release --project tests/EsilvaSoft.KapibaraStudio.Benchmarks -- --filter "*"

# Testes das ferramentas, incluindo orçamento de alocação e latência sintética
dotnet test tests/EsilvaSoft.KapibaraStudio.Benchmarks -c Release -p:EnableBenchmarkTests=true

# Medição de apresentação Headless selecionada nominalmente
dotnet test tests/EsilvaSoft.KapibaraStudio.IntegrationTests -c Release --filter "FullyQualifiedName~InlineGhostPresentationP95FromEditIsWithinTwentyMilliseconds"
```

Os comandos acima são instruções de desenvolvimento; medições não foram executadas nesta revisão.

## Regras para novos testes

1. Validar comportamento observável, com dados e dependências próprios do cenário. Usar fakes de portas sem fallback para host, rede, processos, contas ou modelos reais.
2. Controlar prazo de produto com relógio injetado. Sincronizar início, liberação e término com tarefas/eventos/barreiras. Um timeout real é proteção contra travamento, não orçamento de desempenho nem substituto de sincronização.
3. Quando for preciso observar uma condição, usar tempo monotônico e diagnóstico claro. Não supor que uma pausa fixa dê tempo suficiente ao pool em qualquer runner.
4. Capturar as tarefas antes de invalidar/desconectar. Aguardar consumidores e produtores relevantes antes de conferir ausência de efeitos; liberar gates e descartar recursos mesmo quando uma asserção falhar.
5. Compartilhamento de UI/localização exige isolamento deliberado. Não habilitar paralelismo global sem revisar estado mutável de cada fixture; NUnit mantém testes sequenciais por padrão e só permite paralelismo opt-in.
6. Não usar p95, JIT, resolução de timer, GC ou velocidade da máquina como gate unitário. Registrar máquina, build e dataset nas medições manuais. Dados de duração simulados podem continuar em testes de regras e contratos.
7. Recursos reais pertencem à integração, com nomes exclusivos, limpeza contida e proprietários descartados antes da remoção. Retry só de uma operação de infraestrutura explicitamente transitória, limitado e observável; nunca repetir toda a suíte para ocultar falhas.
8. Registrar seed em geração aleatória e limites de validação. Uma suíte verde em Windows não comprova Linux nativo, MongoDB, cofre ou acessibilidade.

Referências primárias: [boas práticas .NET](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices), [TimeProvider e testes determinísticos](https://devblogs.microsoft.com/dotnet/fake-it-til-you-make-it-to-production/), [paralelismo NUnit](https://docs.nunit.org/articles/nunit/technical-notes/usage/Framework-Parallel-Test-Execution.html), [execução BenchmarkDotNet](https://benchmarkdotnet.org/articles/guides/how-to-run.html) e [restrição de Dispose do SemaphoreSlim](https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim).

## Validação e limites da revisão inicial

Restore da solução em `--locked-mode` passou. Build Release com `--no-restore -p:UsedAvaloniaProducts=`: zero avisos/erros. A primeira tentativa de restore no sandbox não conseguia ler NuGet.Config; a repetição com acesso permitido passou. O download oficial de build também exigiu acesso fora do sandbox. Uma recompilação intermediária colidiu com DLLs usadas pelo testhost; o build final ocorreu após o término da execução, sem bloqueios.

| Execução final Windows | Aprovados | Falhas | Ignorados reportados |
| --- | ---: | ---: | ---: |
| UnitTests | 3.360 | 0 | 20 |
| Infrastructure.Agents.Tests | 254 | 0 | 0 |
| IntegrationTests | 893 | 0 | 5 |
| Total | 4.507 | 0 | 25 |

A suíte unitária completa passou também em três execuções adicionais: 3.360 aprovados/20 ignorados por execução, zero falhas. A guarda de fontes passou 6/6 e voltou a executar na integração integral. O `dotnet test` da solução iniciou somente as três assemblies acima. A avaliação MSBuild confirmou `IsTestProject=false` por padrão em Benchmarks e `true` com opt-in, sem executar suas medições. PNGs reais `workspace-Light-960x620-1.png` e `workspace-Dark-960x620-1.png` da última execução foram inspecionados.

Artefatos locais: `TestResults/stability-build.log`, `TestResults/stability-final.log` e TRX em `TestResults/Stability/Final` e `TestResults/Stability/Repeat`. A primeira execução integral reproduziu o bloqueio de cleanup registrado acima; a execução final após o tratamento limitado passou. Isso não determina a causa do bloqueio nem garante ausência de toda falha futura.

A revisão dos padrões não demonstra ausência de toda dependência transitiva do host. Outras fixtures conservam watchdogs/polling, e adapters reais exigem homologação própria. Não há distribuição WSL instalada neste host; a execução nativa Linux das alterações depende do CI. Layout, temas, atalhos e autenticação não mudaram.
