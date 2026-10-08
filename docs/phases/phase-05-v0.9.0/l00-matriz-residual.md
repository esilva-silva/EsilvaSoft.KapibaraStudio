# F5-L00 — matriz residual de rastreabilidade

**Revisão documental:** 07/10/2026. **Estado:** baseline em consolidação; nenhuma implementação ou homologação foi executada por este documento.

Esta matriz relaciona separadamente os 30 requisitos da [meta de implementação](meta-de-implementacao.md) e as 11 ferramentas vigentes com os cenários V1–V9. Não cria um produto cartesiano GH × ferramenta. Os detalhes de contrato continuam nas fontes; esta página registra estado, evidência conhecida e pendência para orientar os lotes seguintes.

## Convenções de estado

- **Parcial**: há implementação, teste local, documentação ou evidência funcional citada; falta pelo menos um aceite descrito na própria linha.
- **Pendente**: a evidência exigida ainda não foi executada ou registrada. Fonte pública, código, fixture sintética, skip ou ambiente indisponível não equivalem a aprovação.
- **Aceite funcional do usuário**: homologação ampla declarada pelo usuário em 07/10/2026 para tools vigentes e conexão MongoDB; ela não aprova individualmente V1–V9 nem substitui gates técnicos.
- **V8** significa runtime oficial Windows. **V9** inclui inspeção dos PNGs aplicáveis e, quando a jornada exigir, interação nativa; PNG Headless não comprova diálogo, teclado, IME, leitor de tela, aplicação ou Undo nativos.

## GH-01..30 → evidência e lacuna

O estado abaixo resume a coluna de evidência da meta de implementação em 07/10/2026. As linhas numeradas são as fontes de requisito e evidência mais completas; evidências específicas com âncoras têm link direto. “TRX/fixture” só identifica o resultado descrito pela fonte, não confirma que o artefato ainda exista no checkout. Hash de commit não foi registrado de forma uniforme para os snapshots citados e deve ser anexado antes do aceite final.

| Requisito | Estado na baseline | Evidência rastreável | Pendência para fechar |
| --- | --- | --- | --- |
| GH-01 — instalação e descoberta da CLI | Parcial | Meta de implementação, linha GH-01: probe tipado e fixtures; `AutomaticPathDiscoverySkipsShimAndContinuesToNativeExecutable` 1/1; filtros locais 45/45, 48/48 e 10/10; 40 PNGs selecionados. | Instalação oficial, pré-requisitos PowerShell, permissões/carregamento e versão/protocolo real. A CLI presente nesta máquina não iniciou (`Access Denied`). |
| GH-02 — override e configuração da CLI | Parcial | Linha GH-02: override sem fallback, limpeza para discovery e cliente aberto mantém caminho capturado; preferências 5/5 e discovery/runtime settings 13 aprovados/1 skip. | Revalidar com CLI oficial; o skip Linux em Windows não é aprovação. |
| GH-03 — pin e compatibilidade SDK/CLI | Parcial | Linha GH-03: SDK/locks 1.0.14; compatibilidade fake STDIO 12/12 cobrindo protocolo/método/capacidade e bloqueio pré-prompt. | Versões efetivamente negociadas e matriz CLI oficial completa. |
| GH-04 — login oficial e estado posterior | Parcial | Linha GH-04: fluxos de falha/cancelamento locais; AccountCommandConcurrency/Lifecycle/Isolation 56/56, incluindo corrida de login/logout. | Expiração e timeout observados no processo/CLI oficial. Sem leitura de credenciais ou fluxo OAuth paralelo. |
| GH-05 — modalidade de conta permitida | Parcial | Linha GH-05: guardas locais `IsAuthenticated` + `AuthType=user`, checagem antes de catálogo/sessão; AccountBoundary/turno 19/19 e foco combinado 40/40. | Precedência e entitlement em conta/política oficiais. |
| GH-06 — coordenador e checagem passiva | Parcial | Linha GH-06: composição DI e status passivo auditados; filtros `AgentProviderAvailabilityServiceTests|TestConnection` 13/13 previamente registrados. | Reexecução atual não teve conclusão do testhost; CLI oficial e eventual diálogo nativo. |
| GH-07 — logout e escopo | Parcial | Linha GH-07: confirmação, invalidação antes do launcher e concorrência local; AccountCommandConcurrency/Lifecycle/Isolation 56/56. | Efeito na CLI/keyring e sessões reais após logout. Não alegar revogação OAuth/exclusão remota. |
| GH-08 — plano e política organizacional | Parcial | Linha GH-08 e guia de termos: fontes públicas revisadas em 04/10/2026; catálogo efetivo e bloqueio de modelo removido tratados localmente. | Confirmar plano/seat e policy real de organização/enterprise; revisão deve ser atualizada antes do release. |
| GH-09 — modelos elegíveis e seleção | Parcial | Linha GH-09: testes de seleção/isolation 11/11 e 25/25; regressão de recovery passou 1/1, TRX `artifacts/phase5-resume-gh09/gh09-fixed-elevated.trx`; 72 PNGs inspecionados. | Conta/policy, catálogo e runtime oficiais. |
| GH-10 — stream, ordem e terminalidade | Parcial | Linha GH-10: event replay/correlação 16/16 e focos anteriores de eventos/contratos. | Runtime oficial e falhas reais de serviço/processo. |
| GH-11 — erros sanitizados e sem replay | Parcial | [Registro GH-11](meta-de-implementacao.md#gh-11--término-sdk-durante-streaming--06102026): foco fake runtime/DTO do SDK 29/29; repro anterior 3 falhas; sem replay. | Categorias, desconexões e retry no runtime/CLI oficial. |
| GH-12 — snapshot de origem e anexos | Parcial | [Registro GH-12](meta-de-implementacao.md#gh-12--preparação-de-anexos-após-troca-de-conversa--07102026): regressão Headless 2/2; build serial isolado 0 avisos/erros; TRX `artifacts/phase5-resume-gh12/gh12-final-elevated.trx`. | Preview/UI nativo, filesystem real e runtime oficial. |
| GH-13 — schema, plano e correlação | Parcial | Linha GH-13: schema e correlação locais; roundtrip coletivo histórico de 14 tools e roundtrips individuais sintéticos registrados. | Runtime oficial, e revalidar nomes/catálogo corrente antes de reutilizar fixtures. |
| GH-14 — hooks, tools custom e agentes internos | Parcial | Linha GH-14: hooks fake STDIO e recusa dos 12 tipos concretos do SDK 20/20; foco hooks/permissões 35/35. | RPC/hooks no runtime oficial e bloqueio de built-in agents nativos em create/resume. |
| GH-15 — consistência de consentimento/grants | Parcial | Linha GH-15: foco local de política/âncoras 18/18; cobertura geral do registry seguro em [ADR-066](../../architecture/agent-tool-safety.md#validação). | Matriz de cenários por tool e negação/revogação no runtime oficial. |
| GH-16 — confirmação pontual e auditoria | Parcial | [Registro GH-16](meta-de-implementacao.md#gh-16--falha-da-ponte-sem-falsa-expiração--06102026): correção da ponte; foco final 11/11, repro anterior 2 falhas. | Clique nativo, runtime oficial e falha real da ponte/ledger. |
| GH-17 — proposta, revisão, Undo e conflito | Parcial | Matriz GH, linha GH-17, e TOOL-14: integração de origem 5/5; regressões locais 55/55 e layout 6/6 com PNGs inspecionados. | Editor, aplicação/Undo, acessibilidade e conflitos em UI nativa; runtime oficial. |
| GH-18 — BSON/EJSON e limites | Parcial | Linha GH-18: codecs/UUID/limites locais; foco BSON/EJSON 94/94. | MongoDB real, limites/timeout do servidor e runtime oficial. Consultas diretas de documentos estão fora do catálogo atual. |
| GH-19 — reserva e recuperação de sessão | Parcial | Linha GH-19: integração DI com proprietário LiteDB real 2/2 e testes fake de create/resume sem replay; TRXs citados na meta. | Runtime oficial, restart/crash do processo/SO e falhas de recuperação no ambiente real. |
| GH-20 — retenção e opt-out | Parcial | [Registro GH-20](meta-de-implementacao.md#gh-20--envio-copilot-com-histórico-desativado--07102026): foco final 12/12 e regressão chat/snapshots 32/32, incluindo troca de store e retry de cleanup. | Roteamento/limpeza no runtime oficial, restart/crash e ausência de resíduo no runtime real. |
| GH-21 — exclusão e retenção local/nativa | Parcial | Linha GH-21 e evidência do ciclo de vida/reserva em GH-19/GH-20; contratos locais de falha/retry. | Exclusão nativa oficial, crash/lock, falha de disco e retry no ambiente real; sem segunda conexão LiteDB. |
| GH-22 — isolamento de conversas e cancelamento | Parcial | Linha GH-22: regressões fake STDIO e testes de conversa isolada; GH-12 regressão 2/2 comprova origem de estado/request. | Duas conversas concorrentes e cancelamento com CLI/runtime oficial; processo/grant em dispose real. |
| GH-23 — métricas oficiais | Parcial | Linha GH-23 e meta de permissões: divulgação/métricas tipadas e estados de ausência/parcialidade cobertos localmente. | Confirmar semântica e disponibilidade da métrica na versão/runtime/conta oficiais; não inferir tokens, saldo ou cobrança. |
| GH-24 — limites, créditos e políticas | Parcial | Linha GH-24 e seção “Dados e uso” de GH-28: links oficiais localizados e avisos sem promessa de uso ilimitado/saldo. | Conta/organização e modelo reais; revalidar fontes e condições do contrato no release. |
| GH-25 — divulgação e consentimento externo | Parcial | Linha GH-25, [inspeção de divulgação GH-28](meta-de-implementacao.md#gh-28--orientação-e-renderização-de-dados-e-uso--06102026) e consentimento persistido por provider/turno. | Jornada nativa de consentimento e confirmação, leitor de tela e limites de retenção/processamento no serviço. |
| GH-26 — termos e enquadramento | Parcial | Linha GH-26 e [guia de termos](guia-termos-github-copilot.md): fontes públicas/documentação mapeadas. | Decisão do responsável de produto/organização por plano, entidade contratante, DPA e obrigações; fontes e data atuais. |
| GH-27 — licença, notices e SBOM | Parcial | Linha GH-27 e [contrato de release](../../architecture/release-copilot.md): verificadores sintéticos de licença/notices/SBOM por RID; pin SDK 1.0.14. | Executar ferramenta SBOM oficial e auditar pacotes reais, transitivas/nativos por RID e licença da CLI instalada; responsável registra decisão. |
| GH-28 — marca, orientação e materiais | Parcial | [Registro GH-28](meta-de-implementacao.md#gh-28--orientação-e-renderização-de-dados-e-uso--06102026): foco UI 2/2 e 32 PNGs de orientação em quatro idiomas/dois temas inspecionados. | Revisão de marca, matriz visual ampla e materiais de instalador/release. |
| GH-29 — estados visuais e acessibilidade | Parcial | Linha GH-29: PNGs Headless selecionados em múltiplos idiomas/temas/tamanhos; layout de confirmação e proposta testado. | Completar a matriz visual aplicável e registrar teclado/IME/leitor de tela e jornadas nativas. |
| GH-30 — artefatos e instalação por RID | Parcial | Linha GH-30 e [contrato de release](../../architecture/release-copilot.md): fixtures estruturais/verificadores por RID e checks locais em snapshots distintos. | Pacotes reais, SBOM oficial, instalação/atualização em máquinas limpas, checks no commit exato e auditoria de conteúdo/hash. |

## Ferramentas vigentes × V1..V9

**Legenda:** `P` = parcialmente evidenciado em testes locais/fixtures conforme a fonte indicada, sem aprovação da célula; `A` = pendente de evidência de runtime oficial Windows; `N` = pendente de PNG/desktop e interação nativa aplicáveis. Todas as 11 tools estão marcadas **aplicáveis** aos nove cenários: os cenários de saída/ausência de fonte continuam aplicáveis mesmo quando a tool não consulta MongoDB. Não foi marcada exceção N/A por inferência.

O status funcional declarado pelo usuário em 07/10/2026 vale para o catálogo vigente completo, mas não transforma estas células em aprovação técnica. V1–V7 são `P` porque a meta de implementação registra evidências locais por ferramenta; não há ainda um índice linha-a-linha que associe cada subcenário do vetor V à fixture/TRX e ao mesmo commit. V8/V9 permanecem pendentes conforme suas definições.

| ID / ferramenta | V1 | V2 | V3 | V4 | V5 | V6 | V7 | V8 | V9 | Evidência local específica / pendência destacada |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| TOOL-01 `list_connections` | P | P | P | P | P | P | P | A | N | [Catálogo da meta](meta-de-implementacao.md#catálogo-completo-liberável-ao-copilot), linha TOOL-01: filtro 36/36, alias redigido e zero fonte Mongo; V8/V9 pendentes. |
| TOOL-02 `list_databases` | P | P | P | P | P | P | P | A | N | Catálogo da meta, linha TOOL-02: casos novos 4/4 e foco metadata/paginação/source 27/27; servidor Mongo real, V8/V9 pendentes. |
| TOOL-03 `list_collections` | P | P | P | P | P | P | P | A | N | Catálogo da meta, linha TOOL-03: filtro combinado 31/31, sem leitura de documentos; Mongo real, V8/V9 pendentes. |
| TOOL-04 `get_indexes` | P | P | P | P | P | P | P | A | N | Catálogo da meta, linha TOOL-04: V7 4/4, TRX `artifacts/phase5-tool04/tool04-v7-elevated.trx`; cursor/servidor, V8/V9 pendentes. |
| TOOL-05 `get_cached_schema` | P | P | P | P | P | P | P | A | N | Catálogo da meta, linha TOOL-05: `AgentCachedSchemaTests` 6/6; cache/auditoria local, V8/V9 pendentes. |
| TOOL-06 `get_workspace_context` | P | P | P | P | P | P | P | A | N | Catálogo da meta, linha TOOL-06: focos contextuais 19/19, ledger LiteDB 26/26; CLI oficial, filesystem/desktop nativos pendentes. |
| TOOL-14 `propose_file_edit` | P | P | P | P | P | P | P | A | N | Catálogo da meta, linha TOOL-14: cancelamento/deadline 2/2, `ProposeFileEdit` 16/16, store 2/2, origem 5/5; Undo/editor nativos pendentes. |
| TOOL-15 `get_search_indexes` | P | P | P | P | P | P | P | A | N | Catálogo da meta, linha TOOL-15 e [ADR-066](../../architecture/agent-tool-safety.md): roundtrip sintético/projeção limitada; Atlas compatível real, V8/V9 pendentes. |
| TOOL-16 `get_query_results` | P | P | P | P | P | P | P | A | N | Catálogo da meta, linha TOOL-16 e ADR-066: saída capturada, EJSON, escopo/geração, concorrência/recovery; usuário aceitou função; runtime oficial e desktop pendentes. |
| TOOL-17 `get_query_diagnostics` | P | P | P | P | P | P | P | A | N | Catálogo da meta, linha TOOL-17 e ADR-066: logs/erros redigidos, ausência/origem/consentimento; runtime oficial/UI pendentes. |
| TOOL-18 `create_workspace_file` | P | P | P | P | P | P | P | A | N | [Validação ADR-067](../../architecture/workspace-file-creation.md#evidências-windows--07102026): 6 I/O reais, 3 roundtrips sintéticos e integração focal 26/26; CLI/conta, confirmação e filesystem nativos pendentes. |

### TOOL-07..13: histórico, sem células atuais

ADR-066 removeu `mongo_find`, `mongo_count`, `sample_documents`, `mongo_find_one`, `get_document`, `mongo_distinct` e `mongo_explain` do catálogo executável. A meta de encerramento deve conservar apenas a rejeição preventiva de nomes legados como verificação de compatibilidade, sem reabrir V1–V9 como entregas dessas ferramentas.

O roundtrip coletivo `FourteenPlannedToolsDispatchThroughRealRegistryAndReturnToTheirOwnNativeRequestIds` é **histórico do catálogo de 14 tools anterior ao ADR-066**. Ele atravessava SDK fixado + PowerShell fake + runtime/registry + fontes sintéticas e não era V8 nem aceite oficial. Depois dele, foram adicionados roundtrips individuais sintéticos para as ferramentas antigas: `mongo_count` 6/6 (regressão 39/39), `mongo_explain` 4/4 (33/33) e `mongo_distinct` 8/8 (47/47). Esses resultados também são históricos e não pertencem ao catálogo atual.

No catálogo atual, a evidência SDK/STDIO indicada para ADR-066 foi convertida para metadados e saídas capturadas. Para criação, ADR-067 documenta três roundtrips sintéticos dentro do foco de integração 26/26. Os números 6/6, 4/4 e 8/8 acima são **contagens de casos de roundtrip individual**, não cobertura de 6 ou 10 ferramentas atuais. A cobertura da tool atual é indicada pela linha TOOL-01..06/14..18 acima e deve ser rastreada por fixture atual antes de qualquer afirmação de completude.

| IDs históricos | Evidência citada | Estado para aceite atual |
| --- | --- | --- |
| TOOL-07 `mongo_find` | Fixture antiga coletiva de 14 tools. | Removida; validar `UnknownTool`/ausência de descriptor como compatibilidade, sem execução MongoDB. |
| TOOL-08 `mongo_count` | `SinglePlannedMongoCountRoundtripPreservesInt64ConsentAndCorrelatedFailures`, 6/6; regressão 39/39. | Removida; evidência preservada, não é entrega atual. |
| TOOL-09 `sample_documents` | Fixture antiga coletiva de 14 tools. | Removida; não reintroduzir amostragem/schema sampling. |
| TOOL-10 `mongo_find_one` | Fixture antiga coletiva de 14 tools. | Removida; não reintroduzir leitura direta de documentos. |
| TOOL-11 `get_document` | Fixture fake com aprovação controlada e fixture MongoReal antiga; ambas excluídas do aceite pelo ADR-066. | Removida; nenhuma fixture deve executar para fechar V8 atual. |
| TOOL-12 `mongo_distinct` | `SinglePlannedMongoDistinctRoundtripPreservesTypedValuesBudgetsAndCorrelatedDenials`, 8/8; regressão 47/47. | Removida; evidência preservada, não é entrega atual. |
| TOOL-13 `mongo_explain` | `SinglePlannedMongoExplainRoundtripKeepsDenialFailureAndApprovalCorrelated`, 4/4; regressão 33/33. | Removida; evidência preservada, não é entrega atual. |

## Inventário de fixtures oficiais e seleção segura

| Fixture | Conteúdo | Seleção/limite |
| --- | --- | --- |
| `CopilotProductToolManualTests` | Seis métodos explícitos, categorias `OfficialManual` e `OfficialCli`. Os métodos de contexto via registry, metadado de conexão (`list_connections`) e proposta são atuais; a proposta não aplica/salva. `OfficialModelCallsSyntheticDocumentReadWithOneCallApproval` declara `get_document` (removida pelo ADR-066) e não serve para aceite atual. | Selecionar método exato. Não usar filtro amplo de classe/categoria. |
| `CopilotOfficialRuntimeManualTests` | Sete métodos explícitos: conta/modelos, sessão, agentes internos, streaming volátil, persistência/resume/erase e cancelamento; sessão/prompt sintéticos sem tools de produto. | Adequada a gates de runtime/sessão descritos no próprio método; não prova chamadas de tools. |
| `CopilotReservedProductionManualTests` | Um método explícito com CLI/runtime oficial e sessão persistente reservada; prompt/contexto sintéticos sem tools. | Evidência do adapter/sessão, não da cobertura por tool. |
| `CopilotFakeStdioRuntimeTests` | A fixture é majoritariamente Integration fake. Apenas `OfficialRuntimeReportsSanitizedAccountAndModelCatalog` tem `OfficialCli` + `Explicit`. `PlannedProductToolResultIsReturnedThroughOfficialSdkRpc` usa fake client apesar do nome; métodos antigos também declaram nomes removidos nos cenários fake. | Selecionar método exato e identificar fake vs CLI. Nomes/prompt legados não significam dispatch à origem produção. |

Não há fixture-fonte `CopilotProductToolManualTests.MongoReal.cs` nem uso localizado do helper `DisposableCopilotMongoServer`; não existe fixture OfficialCli individual localizada para `get_indexes`, `get_cached_schema`, `get_search_indexes`, `get_query_results`, `get_query_diagnostics` ou `create_workspace_file`. O aceite funcional amplo do usuário continua válido, porém não substitui os cenários técnicos V8/V9 por tool. Planejar fixtures/sessões oficiais restantes sem restaurar qualquer consulta de documento removida.

Filtros positivos exatos sugeridos (não executados nesta auditoria):

```powershell
dotnet test tests/EsilvaSoft.KapibaraStudio.IntegrationTests/EsilvaSoft.KapibaraStudio.IntegrationTests.csproj --no-build --no-restore -p:UsedAvaloniaProducts= --filter 'FullyQualifiedName~CopilotProductToolManualTests.OfficialModelCallsWorkspaceContextThroughProductionRegistry'
dotnet test tests/EsilvaSoft.KapibaraStudio.IntegrationTests/EsilvaSoft.KapibaraStudio.IntegrationTests.csproj --no-build --no-restore -p:UsedAvaloniaProducts= --filter 'FullyQualifiedName~CopilotProductToolManualTests.OfficialModelCallsMongoMetadataToolWithScopedPermission'
dotnet test tests/EsilvaSoft.KapibaraStudio.IntegrationTests/EsilvaSoft.KapibaraStudio.IntegrationTests.csproj --no-build --no-restore -p:UsedAvaloniaProducts= --filter 'FullyQualifiedName~CopilotProductToolManualTests.OfficialModelProposesEditToSyntheticActiveBuffer'
```

NUnit `Explicit` requer selecionar o método intencionalmente. Esses filtros são roteiros, não evidência de execução.

## Snapshots, TRXs e consolidação

| Snapshot / fonte | Resultado documental | Uso permitido nesta matriz |
| --- | --- | --- |
| Catálogo anterior ao ADR-066; roundtrip coletivo de 14 tools | SDK fixado, PowerShell fake, runtime/registry e fontes sintéticas; correlação de request. | Evidência histórica do pipeline e correlação. Não conta como V8, não aprova tool atual e não deve somar a novas execuções. |
| 07/10 — TOOL-08/12/13 individuais | TOOL-08 6/6 e regressão 39/39; TOOL-12 8/8 e 47/47; TOOL-13 4/4 e 33/33. Todos sintéticos; os três tools foram removidos em ADR-066. | Preservar histórico com status “removida”; nenhum aceite atual. |
| 07/10 — tools seguras (ADR-066) | Restore locked e build com `UsedAvaloniaProducts=` passaram; Agents 403/403, UnitTests 3.442/20 ignorados; IntegrationTests primeira execução 1.018/1 falha/5 ignorados e reexecução 1.019/0 falhas/5 ignorados. TRXs/logs em `artifacts/agent-safe-tools/complete`, `integration-recheck`, `agent-safe-tools-complete-test.log` e `agent-safe-tools-integration-recheck.log`. | Resultados pertencem a execuções/snapshots distintos. A reexecução não apaga a falha inicial nem identifica o handle LiteDB. Não declarar uma baseline integral única verde sem commit/hash correspondente e investigação de teardown. |
| 07/10 — criação exclusiva (ADR-067) | UnitTests 3.460/20 ignorados; integração focal 26/26, incluindo seis I/O reais e três roundtrips SDK/STDIO. TRXs/logs e PNGs em `artifacts/create-workspace-file/...` e `tests/.../ui-evidence/render-39f7fb7d954f4938ba68975229e951b5/`. Suíte integral anterior teve duas falhas de cleanup; foco posterior 30/30. | Evidência focal da criação e da UI de permissão. Não promover à aprovação da suíte integral, CLI oficial, diálogo nativo ou leitor de tela. |
| 07/10 — GH-20 opt-out | Foco final 12/12; regressão chat/snapshots 32/32; build serial 0 avisos/erros. TRXs em `artifacts/phase5-gh20-optout/`. | Evidência local de reserva, separação de IDs e retry; runtime oficial/restart continuam pendentes. |
| 07/10 — F5-L00, fora do sandbox | `LegacyConnectionCredentialMigrationTests` 8/8 (foco auxiliar); dois testes previamente associados ao teardown 2/2; IntegrationTests integral: TRX tem 1.026 resultados Passed, 0 Failed e 76 resultados individuais NotExecuted em 1.102. O campo `ResultSummary/Counters.notExecuted` contradiz os resultados individuais e está em zero; console reportou 5 ignorados. TRX em `artifacts/phase5-l00-teardown/phase5-l00-integration-full.trx`. | Confirma que executar VSTest fora do sandbox permite conexão ao testhost. As tentativas dentro do sandbox falharam antes de discovery; log diagnóstico mostra listener loopback sem handshake. A falha histórica de apagar `WORKSPACE-LOG.DB.tmp` não foi reproduzida nem explicada. Não declarar causa LiteDB resolvida por esta suíte verde isolada. |
| 07/10 — diagnóstico opt-in do teardown | Build isolado de IntegrationTests (referências já compiladas) sem avisos/erros; foco 13/13, 0 falhas, 0 ignorados. TRX `artifacts/phase5-l00-restart-manager/l00-restart-manager-focused.trx`. Um processo filho segurou o arquivo sintético; Restart Manager listou seu PID. Teste verificou chamada única, arquivo/lock preservados, opt-out e exceção original preservada. | A instrumentação só observa o primeiro erro Windows 32/33 para `workspace-log.db.tmp` no diretório sintético permitido. Restart Manager reporta usuários do recurso, não o handle exato nem causalidade histórica. Causa real ainda não reproduzida; L00 continua aberto. |

Esta revisão inicial não executou restore, build, conta Copilot, CLI, MongoDB ou inspeção adicional de PNG; os testes L00 posteriores foram executados e registrados na linha acima. Resultados históricos e caminhos transcritos ainda precisam da associação definitiva a commit/hash da baseline. A intermitência `WORKSPACE-LOG.DB.tmp` segue aberta conforme [estabilidade de testes](../../architecture/test-stability.md#teardown-litedb-no-windows--investigação-de-07102026).
