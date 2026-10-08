# Meta F5-CLOSE — encerramento da Fase 5 / v0.9.0

**Revisão:** 07/10/2026. **Estado:** F5-L00 em andamento; matriz concluída, instrumentação diagnóstica de testes validada, causa histórica do teardown ainda aberta. Sem alteração de código de produto ou nova homologação.

**Objetivo:** concluir GitHub Copilot e a infraestrutura compartilhada necessária no EsilvaSoft.KapibaraStudio, fechar os requisitos F5-GH-01..30 e os cenários aplicáveis às 11 tools vigentes, com evidência rastreável e sem ampliar permissões. O encerramento automatizável e a homologação oficial/nativa são marcos separados; integração completa Windows depende dos dois.

Esta meta organiza o trabalho residual da [meta de implementação](meta-de-implementacao.md) e do [aceite da fase](README.md#aceite-e-limites-de-conclusão). Não substitui contratos, IDs ou evidências anteriores. A prioridade da Fase 8 permanece vigente; os testes focais/integral registrados em L00 avançam a baseline, sem mudar a ordem do roadmap.

## Resultado da revisão

| Área | Estado comprovado nos registros | Trabalho residual |
| --- | --- | --- |
| Tools e conexão MongoDB | Aceite funcional explícito do usuário em 07/10/2026 para as 11 tools vigentes. | Falhas, negação/revogação/expiração, concorrência e recuperação por cenário; não reabrir o aceite funcional sem regressão demonstrada. |
| Conta, modelos e conversa | Adapter/SDK existentes, testes locais e evidências oficiais históricas parciais. SDK fixado em 1.0.14. | Vincular evidência às versões/configuração atuais; cenários oficiais de instalação, conta, política, streaming e erros. |
| Sessões e retenção | Reserva durável com proprietário LiteDB real; opt-out, stores e isolamento cobertos por testes locais. | Roteamento e limpeza com runtime oficial, restart/crash, exclusão com falha e retry, duas conversas reais. |
| Experiência | PNGs inspecionados de estados específicos e testes Headless. | Completar matriz aplicável e validar confirmação, editor/Undo/conflitos, teclado/IME/leitor de tela no Desktop nativo. |
| Qualidade | Builds e suítes/filtros aprovados em snapshots documentados; também existem falhas intermitentes de teardown LiteDB. | Identificar a causa do handle de `WORKSPACE-LOG.DB.tmp`, corrigir e obter resultado integral no estado final. Foco isolado ou reexecução verde não encerra a investigação. |
| Distribuição e termos | Guardas de MIT/notices/SBOM por RID cobertas por fixtures. | Gerar e auditar pacotes reais, executar ferramenta SBOM oficial, verificar transitivas/nativos e registrar decisão sobre termos/licenças. |

O README mistura resultados anteriores verdes com ressalvas posteriores. Até consolidar commit, horários e TRXs, não declarar uma baseline integral atual aprovada nem somar contagens de snapshots diferentes. TOOL-07..13 e os roundtrips históricos de consulta direta não são entregas pendentes: seu catálogo foi removido pelo ADR-066. Os pacotes antigos descritos em [release Copilot](../../architecture/release-copilot.md) também não comprovam a política atual de ausência de CLI/runtime redistribuídos.

## Escopo fixado

- Copilot por assinatura, CLI oficial instalada pelo usuário, contratos `IAgentProvider`/`IAgentSession`, registry, consentimentos, auditoria, contexto, sessões e propostas existentes.
- Tools vigentes: `list_connections`, `list_databases`, `list_collections`, `get_indexes`, `get_cached_schema`, `get_workspace_context`, `propose_file_edit`, `get_search_indexes`, `get_query_results`, `get_query_diagnostics` e `create_workspace_file` (TOOL-01..06/14..18). `approve` continua no canal de confirmação; não é uma 12ª tool Copilot liberável.
- Metadados/índices podem acessar MongoDB; documentos/erros/logs vêm somente de execução humana já capturada. Cache não faz amostragem/refresh. Atlas Search usa listagem de índices, sem executar pesquisa.
- Propostas editam buffer com revisão/Undo e proteção de conflito; criação grava somente arquivo novo dentro da workspace, com opt-in e confirmação pontual, sem sobrescrita.
- Autenticação oficial `user`, sem acesso a arquivos de credenciais/tokens, fallback API/BYOK ou outro provider. Ferramentas nativas e built-in agents continuam bloqueados.
- Claude, Codex, APIs externas e MCP externo permanecem no backlog com suas implementações preservadas. Linux mantém revisão estrutural, build/cross-publish e suítes unitárias previstas; não recebe homologação nativa por evidência Windows.

Contratos: [segurança das tools / ADR-066](../../architecture/agent-tool-safety.md), [criação / ADR-067](../../architecture/workspace-file-creation.md), [ADRs](../../10-decisoes-arquiteturais.md), [catálogo](../../03-catalogo-funcional.md) e [matriz](../../15-matriz-de-validacao.md).

## Lotes de execução e saída obrigatória

Os IDs F5-L01..05 da meta de implementação são preservados. F5-L00 é uma preparação transversal adicional; sua baseline é insumo do entregável baseline/conta de L01. L05 consolida GH-30 e a revisão dos requisitos anteriores, incluindo GH-27; não cria um segundo aceite desse requisito.

Ownership de requisitos: L01 responde por GH-01..09/26..28; L02 por GH-10..14/19..22; L03 por GH-15..18 e tools vigentes; L04 por GH-23..25/29; L05 por GH-30 e consolidação final. GH-17/28 em L04 e GH-27 em L05 são revisões cruzadas cuja evidência retorna à mesma linha de requisito. L00 entrega duas tabelas: GH → status/evidência/lacuna e TOOL vigente → Vn aplicável/status/evidência/lacuna, sem combinar GH × TOOL × Vn. TOOL-07..13 recebem somente classificação histórica.

As dependências entre lotes usam a saída local necessária ao Marco A: contrato implementado, checks pertinentes e lacunas nominais registradas. Evidência oficial/nativa indisponível não impede preparar o lote seguinte, mas mantém o cenário aberto para o Marco B. A coluna de saída reúne os requisitos dos dois marcos; o orquestrador registra separadamente o aceite local e o oficial/nativo, sem declarar o lote integralmente concluído enquanto houver cenário obrigatório pendente.

| Lote / dependência | Ações concretas | Critério de saída |
| --- | --- | --- |
| F5-L00 — baseline; primeiro | Consolidar GH-01..30 e TOOL-01..06/14..18 × V1..V9, relacionando testes/evidências existentes e lacunas. Reconciliar resultados integrais por snapshot. Investigar ownership/dispose/handles do teardown LiteDB sem mascarar falhas ou abrir segundo owner. | Registro único de estados; causa da intermitência identificada e correção verificada com cenário de falha significativo e suíte integral. Nenhum histórico removido é marcado como requisito de implementação. |
| F5-L01 — conta e compatibilidade; após L00 | GH-01..09/26..28: CLI ausente/inválida/incompatível, override sem fallback, conta/login/logout/cancelamento/expiração, precedência `user`, modelos elegíveis e política corporativa. Revisar termos e inventário desde o início. | Testes locais pertinentes aprovados; matriz oficial Windows com SDK/CLI/protocolo e catálogo efetivos, sem segredos/inferência na checagem de conta. Casos sem ambiente ficam explicitamente pendentes. Responsável pela decisão de termos identificado. |
| F5-L02 — conversa, contexto e sessões; após L01 | GH-10..14/19..22: streaming/erro/processo morto sem replay; snapshot de origem; bloqueio preventivo em create/resume; duas conversas e cancelamento isolado; reserva/restart, sessão perdida/ilegível, falha de gravação/exclusão, lock ocupado e opt-out. | Demonstrar ausência de redirecionamento/replay e preservação de referências com erro visível/retry. Evidência oficial de retenção/cleanup, mudança de KeepHistory e restart/crash; nenhum resultado/credencial em snapshots. Nenhum processo/grant órfão nos cenários exercitados. |
| F5-L03 — segurança das tools; após isolamento de L02 | GH-15..18 e 11 tools: preencher somente lacunas V1..V9 aplicáveis. Retirar separadamente checkbox/consentimento/grant, revogar durante await/auditoria, expirar/rejeitar confirmação, falhar ponte/auditoria, limites/cancelamento/eventos tardios. Verificar os 13 nomes removidos e ferramentas nativas recusados antes da fonte. | Cada célula aplicável aponta a evidência individual ou pendência. Metadados reais usam fixture descartável; resultados/diagnósticos são capturados de execução humana e não geram consulta. Atlas requer ambiente compatível. Criação exclusiva, containment/links nativos, conflito/Undo de proposta têm evidência própria. |
| F5-L04 — experiência, uso e privacidade; após L02/L03 | GH-23..25/29 e revisão GH-17/28: métricas oficiais com ausência/zero/parcialidade, divulgação/consentimento, estados de conta/modelo/permissão/contexto/proposta/criação/erro/recovery. Completar renderização pt-BR/en/es/zh-CN, claro/escuro e dimensões/escalas aplicáveis. | PNGs reais inspecionados com manifesto de estados e limitações. Jornadas nativas com confirmação, revisão/aplicação/Undo, foco/rolagem/teclado/IME/leitor de tela registradas separadamente. Sem inferir tokens/saldo/cobrança ou exclusão remota pelo opt-out local. |
| F5-L05 — consolidação e release; após saídas anteriores | GH-27/30: restore/build/testes no estado final; gerar quatro RIDs com SBOM oficial, MIT/notices/transitivas/nativos/hashes, auditar single-file e ausência de CLI/runtime redistribuídos. Validar instalação/atualização aplicáveis e alinhar documentos. | Checks e pacotes reais vinculados ao commit exato, decisão de termos/licenças registrada, documentação coerente e termo de encerramento por marco. Não publicar/taguear como parte desta meta documental. |

A revisão dos testes existentes precede qualquer teste novo. Reusar, quando pertinentes, `CopilotProductToolRuntimeTests`, `AgentToolRegistryGateTests`, `CopilotFakeStdioRuntimeTests`, `AgentChatCopilotDurableReservationTests`, `CopilotProductToolManualTests`, `CopilotOfficialRuntimeManualTests` e os verificadores `eng/Test-Release*.ps1`. Fixtures oficiais antigas precisam ser verificadas contra o catálogo vigente antes de selecionar qualquer execução; não reintroduzir consultas diretas para obter V8.

L00 deve entregar também o inventário nominal de fixtures oficiais vigentes/obsoletas. A [matriz residual F5-L00](l00-matriz-residual.md) registra GH-01..30, as 11 tools vigentes × V1..V9, histórico TOOL-07..13, fixtures oficiais, snapshots/TRXs e lacunas de hash uniforme. `OfficialModelCallsSyntheticDocumentReadWithOneCallApproval` ainda referencia `get_document`, removida pelo ADR-066; seleção por classe/categoria OfficialCli ampla pode executar esse cenário obsoleto. Usar filtros por método exato conforme o inventário; não reativar consulta direta para V8.

Pontos de partida identificados por revisão estática para L00/L03: `CopilotRegistryRoundtripTests` já cobre o catálogo atual em cenários de seis/dez tools e criação exclusiva; os registros históricos de quatorze tools precisam de associação ao snapshot original, sem renomear TRXs. `AgentToolRegistryGateTests.RemovedToolsCannotBeDiscoveredOrInvokedAtAnyLegacyReleaseStage` já cobre os 13 nomes removidos. Na investigação do teardown, examinar produtores pendentes, ownership e cleanup de `LegacyConnectionCredentialMigrationTests`/`LiteDbConnectionProfileRepository`, sem atribuir causa a uma mudança recente por analogia. Em L05, usar staging/publish limpo por RID e auditar o artefato final: runtime/CLI remanescentes de `bin`/`obj` ou pacotes antigos não comprovam o comportamento do checkout atual.

**Investigação de teardown — 07/10/2026:** a revisão estática confirmou dispose do repository antes de `Workspace.DisposeAsync`, migração aguardada e retry de cleanup limitado a violações Windows 32/33 por cinco segundos. No sandbox, três tentativas (90 s, 180 s e VSTest direto com diagnóstico de 60 s) lançaram `testhost.exe`, mas ele não conectou ao listener loopback; TRXs registram zero casos. O log `artifacts/phase5-l00-teardown/vstest-diagnostic.log` confirma falha do handshake antes da descoberta dos testes. Fora do sandbox, `LegacyConnectionCredentialMigrationTests` (foco auxiliar, não um dos casos que falharam) passou **8/8**; os dois casos rastreados anteriormente passaram **2/2**. IntegrationTests integral registrou 1.026 resultados Passed, zero Failed e 76 resultados individuais NotExecuted no total de 1.102; o campo `ResultSummary/Counters.notExecuted` do próprio TRX está incorretamente em zero. A saída observada do console reportou 5 ignorados. TRX integral: `artifacts/phase5-l00-teardown/phase5-l00-integration-full.trx`.

Para observar uma ocorrência futura sem alterar limpeza, foi adicionado helper opt-in de teste `KAPIBARA_TEST_LITEDB_TEARDOWN_DIAGNOSTICS=1`, restrito ao primeiro erro Windows 32/33 e ao arquivo sintético `workspace-log.db.tmp` no diretório esperado. Uma fixture com processo filho comprovou que o Restart Manager listou o PID que mantinha o arquivo aberto; o teste também verificou chamada única, preservação do arquivo/lock, opt-out e preservação da exceção original. Foco validado: **13/13**, TRX `artifacts/phase5-l00-restart-manager/l00-restart-manager-focused.trx`; build isolado de IntegrationTests com referências de projeto já compiladas: zero avisos/erros. A API lista processos associados ao recurso, não expõe o handle bloqueador nem prova causalidade. Tentativas dentro do sandbox falharam antes de discovery e a causa histórica do handle em `WORKSPACE-LOG.DB.tmp` continua sem reprodução/identificação. Não houve alteração LiteDB de produto. L00 segue aberto até capturar e explicar uma ocorrência real ou obter evidência equivalente no estado final do código; a suíte integral verde não basta.

## Evidências e dependências

### Orquestração e divisão entre subagentes

Conforme pedido do usuário em 07/10/2026, o chat principal coordena execução, integração, evidências e aceite. Subagentes trabalham com tarefas delimitadas e arquivos atribuídos; não editam simultaneamente os mesmos arquivos. A execução de cada lote deve respeitar suas dependências, mesmo quando houver trabalho independente em paralelo.

| Responsável | Trabalho atribuído | Entrega ao orquestrador |
| --- | --- | --- |
| Luna (`gpt-6-luna`) | Inventário GH/TOOL/Vn e links de evidência em L00; rastreabilidade e consolidação documental; inventário de licenças/notices e roteiros delimitados de UI/release. Testes locais simples somente com contrato e escopo claros. | Matriz residual, divergências entre documentos e evidências, patches delimitados e resultado dos checks pertinentes. Não decide aceite jurídico/nativo. |
| Sol 6.1 (`gpt-6.1-sol`) | Diagnóstico/correção LiteDB de L00; compatibilidade/autenticação de L01; concorrência, eventos, contexto, persistência/opt-out/recovery de L02; segurança/revogação/auditoria/links de L03; problemas de UI ou empacotamento que exijam análise técnica. | Causa reproduzida, correção e regressão significativa, riscos residuais e evidências por camada. Não amplia grants para fazer teste passar. |
| Chat principal | Escolher próximo trabalho pela matriz residual, atribuir ownership, revisar patches, integrar em sequência, rodar checks finais e consolidar a revisão de L04/L05. Coordenar homologação oficial/nativa e decisões externas. | Estado único de cada requisito, resultados no estado final e registro dos Marcos A/B. |

Luna prepara inventário/roteiros enquanto Sol investiga o problema técnico selecionado. Antes de integrar, o principal confere mudanças, contratos e testes; falha em contrato/permissão/recuperação volta ao responsável técnico. A divisão não substitui os gates oficiais nem autoriza testes de conta/modelo como parte do CI.

Cada registro deve conter GH/TOOL/Vn, commit e alterações locais relevantes, data, SO/RID, SDK/CLI/protocolo/modelo quando aplicáveis, fixture, procedimento/comando, esperado/observado, TRX/PNG/ledger sanitizado e limites. Estados permitidos: aprovado, pendente, falhou ou não aplicável com justificativa de contrato. Ignorado/ambiente ausente não é aprovado. Uma evidência compartilhada pode cobrir várias células somente quando comprovar cada cenário.

| Dependência | Necessária para | Tratamento se indisponível |
| --- | --- | --- |
| CLI oficial compatível, conta Copilot e ambiente Windows com acesso permitido ao cofre | L01/L02 e V8 | Preparar fixtures e roteiro; registrar bloqueio sem contornar autenticação nem repetir inferência silenciosamente. |
| MongoDB descartável e Atlas compatível com Search/vectorSearch | Metadados/índices V8 de L03 | Não usar conexões/dados do usuário como substituto. Manter cenário/topologia pendente; não implementar fallback de consulta. |
| Desktop gráfico, permissões de links nativos e leitor de tela | V9, links e jornadas de L03/L04 | Registrar com a Fase 10; Headless não encerra esses casos. |
| Acesso a dependências/ferramenta SBOM oficial e runners de build previstos | Pacotes reais de L05 | Guardas sintéticas permanecem evidência local; geração/validação real fica pendente. |
| Responsável do produto pela decisão de termos/licenças aplicáveis | GH-26..28 e release | Preparar inventário, fontes/data e questões concretas; não inventar parecer jurídico ou aceite organizacional. |

## Verificação do estado final

```powershell
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore
dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore
```

Se a tarefa externa de telemetria Avalonia for bloqueada, aplicar `-p:UsedAvaloniaProducts=` e registrar. Respeitar a seleção de suítes de [estabilidade dos testes](../../architecture/test-stability.md); não habilitar Benchmarks/medições no CI/release. Testes oficiais de conta/modelo são explícitos e separados do CI. Reexecutar checks afetados após correção; o resultado final integral deve corresponder ao código/pacotes auditados. Skips devem ter motivo e impacto no aceite.

## Marcos de encerramento

**Marco A — implementação e aceite automatizável da Fase 5:** contratos vigentes implementados, requisitos locais e V1..V7 aplicáveis evidenciados, intermitência investigada/corrigida, checks finais aprovados, matriz visual aplicável inspecionada e artefatos estruturais reais auditados. Atualizar catálogo, plano, matriz, checklist, guia e acompanhamento; ADR/design system somente se decisões/comportamento mudarem. Emitir lista nominal das pendências oficiais/nativas ligada à Fase 10. Esse marco não autoriza afirmar integração completa ou encerrar requisitos cuja evidência exigida continua ausente.

**Marco B — integração completa Windows:** além do Marco A, fechar GH-01..30 e V8/V9 aplicáveis, retenção/recuperação/concorrência oficiais, editor/Undo/acessibilidade e instalação/atualização, com decisão final de termos/licenças e aceite registrado. As evidências podem ser executadas/coordenadas na [Fase 10](../phase-10-v0.14.0/README.md), sem duplicar o aceite funcional já dado. Não inferir suporte nativo Linux nem aprovação de topologias/planos não exercitados.

Em L05, separar auditoria técnica de inventário/licenças/notices/SBOM e pacote (Marco A) da decisão final de termos, contratos aplicáveis e uso de marca (Marco B). Se essa decisão estiver ausente, GH-26..28 e o gate integral de termos do README continuam pendentes; pacote tecnicamente verificado não constitui liberação para distribuição. Da mesma forma, V8/V9 e os cenários GH oficiais/nativos não comprovados permanecem nominalmente pendentes no Marco A e só recebem aceite no Marco B.

- [x] Escopo e aceite funcional vigente revisados; meta residual registrada.
- [x] Matriz GH-01..30 e tool vigente × V1..V9 criada em [F5-L00](l00-matriz-residual.md); identificadas as fixtures históricas removidas e lacunas de associação a commit.
- [ ] L00: baseline e investigação de teardown encerradas.
- [ ] L01: conta/compatibilidade e decisões pendentes rastreadas.
- [ ] L02: contexto, sessões, opt-out e recuperação aceitos por camada.
- [ ] L03: lacunas de segurança por tool fechadas ou explicitamente pendentes no marco correspondente.
- [ ] L04: matriz visual e jornadas nativas registradas.
- [ ] L05: checks/pacotes/documentação/termos concluídos no estado final.
- [ ] Marco A registrado com evidências e pendências nominais.
- [ ] Marco B e checklist integral da fase encerrados com evidências oficiais/nativas.

**Primeiro trabalho executável:** F5-L00. Antes de novos incrementos, produzir a matriz residual e diagnosticar a falha de teardown. Depois, executar L01 → L02 → L03 → L04 → L05, aproveitando a revisão de termos e preparação de ambientes ao longo dos lotes.
