# Contrato das ferramentas de agentes

Este documento define o catálogo seguro e suas autorizações para todos os providers. Consentimento, aprovação humana, flags de escrita ou estágios legados nunca autorizam consulta direta a documentos ou escrita MongoDB por agentes.

## Catálogo e entradas

| Ferramenta | Fonte e autorização | Limites e comportamento |
| --- | --- | --- |
| `list_connections`, `list_databases`, `list_collections` | Metadados; `ReadMetadata`. | Validar escopo, geração e destino; aplicar paginação/cotas. |
| `get_indexes` | Índices comuns; `ReadMetadata`. | Chaves/direções, unique/sparse/hidden, TTL e caminhos de filtros parciais; não expor valores dos filtros. |
| `get_search_indexes` | Índices Atlas Search/vectorSearch; `ReadMetadata`. | Apenas `$listSearchIndexes` via driver, com conexão/banco/coleção fechados. Até 200 índices, 200 caminhos por índice, profundidade 8, 64 KiB por definição, 256 KiB totais e 30 s. Conferir UUID da coleção antes/depois. Não expor documentos, embeddings, storedSource ou valores arbitrários; não criar/alterar/remover índices e não executar `$search`. Erro sanitizado sem fallback. |
| `get_cached_schema` | Cache já aprendido; `ReadSchema` e consentimento de schema inferido. | Leitura Peek; sem refresh, aprendizado ou amostragem. |
| `get_workspace_context` | Snapshot da aba/arquivo capturado para o turno. | Exige consentimentos correspondentes; não inclui resultados. |
| `get_query_results` | Última execução humana concluída, capturada antes do primeiro await do envio. | Opt-in individual, `MongoDocuments` e `ReadDocuments`; não concede `ExecuteReadQueries`. Entrada somente `resultIndex` 0..9.999, `skip` 0..100.000 e `limit` 1..100, padrão 20. Paginar apenas memória. Teto 256 KiB, sem dividir Extended JSON de documento excessivo. |
| `get_query_diagnostics` | Status, erro e mensagens da mesma execução humana capturada. | Opt-in individual, `MongoDocuments`, `ReadDocuments` e `ReadDiagnostics`. Entrada `{}`; até 16.000 bytes UTF-8 por texto, redação de segredos e truncamento explícito. Não lê logs globais, arquivos ou outras abas. |
| `propose_file_edit` | Proposta revisável para buffer. | Revisão humana, conflito e Undo; não salva arquivo nem executa MongoDB. |
| `create_workspace_file` | Arquivo novo na pasta aberta. | Opt-in e confirmação por chamada; [contrato de criação](arquivos-agentes.md). |

São nove leituras selecionáveis, uma proposta e uma criação com opt-in. `approve` integra o canal de confirmação do registry, não uma ferramenta adicional liberável do Copilot. Resultados/diagnósticos começam desmarcados e sem consentimento. Disponibilidade do descriptor não concede autorização.

`get_query_results` retorna identidade de execução/aba, índice e quantidade de conjuntos, `results`, `hasMore`, `truncated` e `sourceTruncated`. Conserva BSON por Extended JSON. Diagnósticos preservam código MongoDB quando fornecido; cancelamento/timeout têm códigos próprios, outras falhas têm categoria tipada.

## Isolamento, revogação e persistência

A aba produz snapshot imutável ao concluir execução humana. Nova execução ou mudança de destino limpa sua referência atual; turnos em andamento permanecem vinculados à captura anterior. Resultados parciais não são publicados como concluídos. Sem execução concluída, retornar `available=false` e saídas vazias, sem consulta adicional.

Todas as origens, inclusive conexões secundárias e origens por conjunto, devem estar autorizadas e na geração capturada. Origem desconhecida, perfil removido ou grant insuficiente suprime toda a saída, incluindo logs mistos. Revalidar policy, canal e snapshot antes/depois da auditoria; revogação não permite retry nem publicação tardia.

Resultados e diagnósticos são transitórios: não persistir em sessão, contexto serializado, resumo de auditoria ou log de tool. O provider pode processar a saída autorizada; opt-out local não apaga dados remotos. Um novo turno pode compartilhar nova captura autorizada, sem herdar token cancelado.

## Nomes permanentemente recusados

`mongo_find`, `mongo_count`, `sample_documents`, `mongo_find_one`, `get_document`, `mongo_distinct`, `mongo_explain`, `get_collection_schema`, `insert_one`, `update_one`, `delete_one`, `create_index` e `drop_index` não têm descriptor/schema/handler executável. Retornar `UnknownTool` antes de consultar perfil, policy, auditoria ou fonte MongoDB. Nenhum estágio, aprovação ou configuração antiga os reativa; suas fontes não são compostas em DI.

Configurações antigas são lidas sem migração destrutiva. Permissões desconhecidas são ignoradas pelo plano e não aparecem na seleção; autorização antiga de consulta nunca vira seleção automática de resultados/diagnósticos. Enums legados são compatibilidade de leitura, sem gate futuro de liberação de consultas.

## Verificação obrigatória

Verificar catálogo em todos os canais/estágios, EJSON/paginação, limites Atlas, ausência de execução capturada, origem secundária, opt-in/escopo/geração, revogação pós-auditoria, falha de Intent/Outcome e recuperação. Roundtrip sintético SDK/STDIO verifica correlação e fronteiras, sem homologar conta/CLI ou MongoDB/Atlas reais. Aceites e limitações estão na [baseline de validação](../09-baseline-de-validacao.md).
