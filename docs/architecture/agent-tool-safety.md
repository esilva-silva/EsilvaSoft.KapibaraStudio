# Ferramentas de agentes: metadados e execução capturada

Decisão solicitada pelo usuário em **07/10/2026**, registrada no ADR-066. Esta política substitui a liberação anterior de consultas diretas e escritas dos agentes em todos os canais do produto. A execução MongoDB pelo usuário no editor continua com suas proteções e confirmações.

## Catálogo vigente

| Ferramenta | Origem e finalidade | Autorização e limites |
| --- | --- | --- |
| `list_connections`, `list_databases`, `list_collections` | Descoberta de metadados; não executam consulta de documentos. | `ReadMetadata`, escopo/geração/destino; paginação e limites existentes. |
| `get_indexes` | Metadados dos índices MongoDB: chaves/direções, unique/sparse/hidden, TTL e caminhos de filtros parciais. | `ReadMetadata`; não expõe valores dos filtros. |
| `get_search_indexes` | Metadados Atlas Search/vectorSearch: nome, tipo, status, queryable e caminhos dos campos mapeados. | `ReadMetadata`; entrada fechada de conexão/banco/coleção. Usa somente `$listSearchIndexes` via driver, nunca `$search` nem pipeline fornecido pelo modelo. Até 200 índices, 200 caminhos por índice, profundidade 8, 64 KiB por definição/256 KiB no total, prazo máximo 30 s e verificação do UUID da coleção antes/depois. Não expõe documentos, embeddings, storedSource ou valores arbitrários da definição. Não cria, atualiza ou remove índices. Falha/servidor sem suporte retorna erro sanitizado, sem fallback de consulta. |
| `get_cached_schema` | Cache existente ou schema já aprendido. | `ReadSchema` e consentimento de schema inferido; leitura Peek, sem refresh, aprendizado ou amostragem. |
| `get_workspace_context` | Contexto capturado da aba/arquivo do turno. | Consentimentos existentes; não devolve conteúdo de resultados. |
| `get_query_results` | Saída da última execução concluída pelo usuário, capturada da aba de origem antes do primeiro await do envio. Documentos e valores escalares permanecem em Extended JSON. | Opt-in individual + consentimento `MongoDocuments` + `ReadDocuments`; não exige nem concede `ExecuteReadQueries`. Entrada somente `resultIndex` (0..9.999), `skip` (0..100.000), `limit` (1..100; padrão 20). Nunca recebe conexão, filtro, pipeline ou código. Retorna `executionId`, `tabId`, índice/quantidade de conjuntos, `results`, `hasMore`, `truncated` e `sourceTruncated`; máximo 256 KiB. Paginação percorre apenas os dados em memória. Documento excessivo é recusado sem dividir EJSON. |
| `get_query_diagnostics` | Status, código de erro disponível, mensagens/logs e erros da mesma execução capturada. | Opt-in individual + `MongoDocuments` + `ReadDocuments` e `ReadDiagnostics`, pois logs podem conter valores. Entrada `{}`; cada texto limitado a 16.000 bytes UTF-8, com redação de segredos e truncamento explícito. Código numérico MongoDB é preservado quando o runtime o fornece; cancelamento/timeout têm códigos próprios e outras exceções têm categoria tipada. Não lê arquivos de log, histórico de outras abas nem logs globais. |
| `propose_file_edit`, `approve` | Proposta mediada de edição e confirmação humana. | Contratos vigentes; não executam MongoDB nem salvam arquivos. |

São **9 leituras selecionáveis + 1 proposta** no Copilot. O registry compartilhado tem **11 descriptors**, incluindo `approve`, que pertence ao canal de confirmação. Disponibilidade não equivale a autorização; tools de resultados/diagnósticos não estão selecionadas por padrão e seu consentimento começa desligado. Copilot e Claude mantêm seus canais existentes; nenhum provider/autenticação foi criado ou desativado.

## Ferramentas removidas e compatibilidade

`mongo_find`, `mongo_count`, `sample_documents`, `mongo_find_one`, `get_document`, `mongo_distinct`, `mongo_explain`, `get_collection_schema`, `insert_one`, `update_one`, `delete_one`, `create_index` e `drop_index` não têm descriptor, schema nem handler executável. Chamadas a esses nomes recebem `UnknownTool` antes de consultar perfil, policy, auditoria ou fonte MongoDB. Um consentimento antigo, grant, aprovação humana, flag de escrita ou estágio legado não os reativa. Nomes/valores de enum/contratos legados são mantidos somente para ler configurações e evidências históricas; os parâmetros antigos de fontes no construtor do registry são ignorados. Fontes legadas de consulta/escrita não são compostas em DI.

Permissões antigas são lidas sem sobrescrita/migração destrutiva. Nomes desconhecidos são ignorados pelo plano e desaparecem da lista selecionável. Não se converte autorização antiga de consulta em seleção automática de `get_query_results`/`get_query_diagnostics`.

## Isolamento, dados e recuperação

A aba registra um snapshot imutável ao terminar uma execução do usuário. Nova execução ou mudança de destino descarta sua referência atual; a mensagem em andamento continua vinculada ao snapshot já capturado. Execução em andamento não compartilha resultados parciais como se estivessem concluídos. Sem execução concluída, as tools retornam `available=false`, listas/textos vazios e nenhuma consulta adicional.

Todos os destinos usados pela execução — inclusive conexões secundárias e origens de cada conjunto — exigem escopo permitido e geração correspondente. Origem desconhecida, perfil excluído/trocado ou grant insuficiente suprime a saída inteira; isso também protege logs que misturam conexões. Policy e canal/snapshot são revalidados antes e depois da auditoria. Revogação não causa retry nem publicação tardia. Cancelamento não afirma rollback.

Resultados e diagnósticos são transitórios: não entram no snapshot de sessão, `AgentWorkspaceContext` serializado, resumo de auditoria ou log de ferramenta. O provider pode processar a saída autorizada; opt-outs locais não apagam dados remotos. O próximo turno pode compartilhar uma nova execução concluída mediante autorização, sem herdar tokens cancelados.

## Validação

Cobertura nova: rejeição permanente dos 13 nomes removidos em todos os estágios e ingressos; escritas negadas mesmo com fonte/approvals/flags legados compostos; captura/paginação/EJSON; ausência de execução, argumentos indevidos, opt-in/escopo/geração, revogação pós-auditoria e recuperação; cópia imutável/não serialização; projeção Atlas sem dados/storedSource e limites. O roundtrip Copilot SDK/STDIO sintético foi convertido para metadados e saídas capturadas, com correlação por request, confirmação e auditoria.

Os testes exclusivos de execução direta/escrita foram retirados com seus handlers; não são ignorados nem mantidos como aceite futuro dessas tools. Regressões de confirmação/auditoria passaram a usar índices permitidos. Evidências anteriores dessas tools nos registros da Fase 5 e ADRs permanecem históricas, sem comprovar disponibilidade atual.

### Evidência automatizada — Windows, 07/10/2026

Restore locked da solução passou. Build da solução passou com **0 avisos/0 erros**, usando `UsedAvaloniaProducts=` para a tarefa externa de telemetria Avalonia, sem desabilitar analisadores. Comandos usados:

```powershell
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -p:UsedAvaloniaProducts=
dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore -m:1 --logger trx --results-directory artifacts/agent-safe-tools/complete
```

| Primeira execução integral | Aprovados | Falhas | Ignorados reportados pelo runner |
| --- | ---: | ---: | ---: |
| Infrastructure.Agents.Tests | 403 | 0 | 0 |
| UnitTests | 3.442 | 0 | 20 |
| IntegrationTests | 1.018 | 1 | 5 |

A falha integral foi `PersistedSecretScanTests.EncodedCanaryIsDetected`: `Directory.Delete` recusou `WORKSPACE-LOG.DB.tmp` em uso durante o descarte da fixture de credenciais. É o mesmo tipo de bloqueio de teardown já [registrado na investigação de estabilidade](test-stability.md#teardown-litedb-no-windows--investigação-de-07102026); esta execução não prova autoria do handle nem suíte integral verde. Nenhuma asserção, golden ou cleanup foi relaxado. Os TRX integrais permanecem em `artifacts/agent-safe-tools/complete`, e o log em `artifacts/agent-safe-tools-complete-test.log`. A execução isolada posterior de `EncodedCanaryIsDetected` passou **1/1** (`artifacts/agent-safe-tools/cleanup-isolated`, log `artifacts/agent-safe-tools-cleanup-isolated.log`); isso não encerra a intermitência nem muda o resultado integral anterior. A reexecução completa de IntegrationTests, sem alterações em produção/asserções/cleanup, passou **1.019 aprovados/5 ignorados/0 falhas**. TRX em `artifacts/agent-safe-tools/integration-recheck`, log `artifacts/agent-safe-tools-integration-recheck.log`.

```powershell
dotnet test tests/EsilvaSoft.KapibaraStudio.IntegrationTests --no-build --no-restore --filter FullyQualifiedName~PersistedSecretScanTests.EncodedCanaryIsDetected --logger trx --results-directory artifacts/agent-safe-tools/cleanup-isolated
dotnet test tests/EsilvaSoft.KapibaraStudio.IntegrationTests --no-build --no-restore --logger trx --results-directory artifacts/agent-safe-tools/integration-recheck
```

Resultado final por assembly: **4.864 aprovados, 25 ignorados reportados e 0 falhas** (403 Agents + 3.442 UnitTests + 1.019 IntegrationTests). Os resultados anteriores não são apagados; a intermitência de teardown permanece em investigação.

Focos finais passaram: `AgentQuerySnapshotToolTests` **20/20**, incluindo falha de Intent/Outcome sem publicação, retry explícito e mudança de geração na última avaliação pós-auditoria; `AgentQueryExecutionCaptureTests` **2/2**, incluindo execução concorrente em duas abas, falha e recuperação. Logs/TRX em `artifacts/agent-safe-tools-release-gate.log`, `artifacts/agent-safe-tools-capture.log` e seus diretórios `release-gate`/`capture`. A suíte integral cobre também projeção de índices Atlas, roundtrips SDK/STDIO sintéticos, canais MCP, opt-ins e rejeição dos nomes removidos. Benchmarks e testes Explicit de serviços reais não foram selecionados.

### Inspeção visual

Os testes Headless geraram a matriz de permissões nos dois temas e a matriz existente de 216 PNGs de cota (quatro idiomas, três tamanhos, três escalas e três estados). Foram inspecionados PNGs reais da execução integral: Copilot claro e Claude escuro em pt-BR, 660×760, e catálogo Copilot zh-CN escuro, 960×760, escala 2. Consentimento novo legível e desligado, nomes do catálogo seguro com quebra de linha, rolagem local e rodapé acessíveis. A inspeção é representativa, não a revisão manual de todos os 216 frames.

Evidências em `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/bin/Debug/net10.0/ui-evidence/render-d2cbcf86ac9b46898ac8f3318caf367a/` (`agent-permissions-Light-660x760.png`, `agent-permissions-Claude-Dark-660x760.png`) e `render-d8a74ac82ab64effad265bdc503e103e/copilot-tool-budget-zh-CN-Dark-960x760-2-default.png`.

MongoDB/Atlas reais, CLI/conta oficiais, execução nativa Linux e acessibilidade/diálogos nativos continuam validações separadas; doubles e Headless não os substituem.
