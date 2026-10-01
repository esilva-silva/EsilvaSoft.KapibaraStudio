# Estabilidade dos testes e benchmarks manuais — 01/10/2026

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
- A execução integral reproduziu um bloqueio de `WORKSPACE-LOG.DB.tmp` na limpeza de `LegacyConnectionCredentialMigrationTests`. A pasta é exclusiva, o owner já foi descartado e a remoção verifica containment. Somente erros Windows 32/33 de compartilhamento/bloqueio são repetidos por até cinco segundos; erros de permissão e bloqueios persistentes falham. A causa externa do bloqueio não foi determinada. Nenhum corpo de teste é repetido para transformar falha em sucesso.

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

## Validação e limites

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
