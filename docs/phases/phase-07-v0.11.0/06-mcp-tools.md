# Catálogo de ferramentas — proposta ancorada no código

Estado: **parcial**. **Atualizado em 25/09/2026** (documentation-agent, a partir de auditoria code-review-agent, confirmado por leitura direta de `src/EsilvaSoft.SlopStudio.Application/Agents/AgentToolRegistry.cs` e seus parciais no HEAD `b0c95e0`, branch `phase-7-i6v8dr`; commits `5136141` e `ebd8a49` de 24/09/2026): o registry interno (`AgentToolRegistry`) hoje expõe **12 tools de leitura reais**, com autorização default-deny, EJSON literal, quotas, auditoria fail-closed e limites, em três estágios fechados (`AgentToolExposureStage`):

| Estágio | Tools |
| --- | --- |
| `Metadata` | `list_connections`, `list_databases`, `list_collections` |
| `LiteralQueries` | `mongo_find`, `mongo_count` |
| `DerivedReads` | `get_collection_schema`, `sample_documents`, `mongo_find_one`, `get_document`, `mongo_distinct`, `get_indexes`, `mongo_explain` |

Um proxy MCP STDIO (`EsilvaSoft.SlopStudio.McpServer`) e um broker IPC autenticado (`AgentBrokerHost`) também existem, com testes (`Mcp/McpStdioProxyTests.cs`, `Mcp/AgentBrokerHostTests.cs`). **Nenhum desses ingressos está composto em produção**: `AddSlopStudioAgentBroker` (`src/EsilvaSoft.SlopStudio.Infrastructure/ServiceCollectionExtensions.cs:103-136`) é opt-in e não é chamada em `src/` fora dela mesma, e nada inicia o `McpServer` a partir do produto/instalador. Portanto nenhuma ferramenta MCP está executável para um cliente externo real hoje, apesar do código e dos testes existirem — ver [17-validação](17-validacao-da-meta.md) e [13-análise do código](13-analise-do-codigo.md) para o detalhamento file:line e o que falta (composição incondicional, fixtures reais por tool do `DerivedReads`, MongoDB real via MCP, decisão de gate para `LiteralQueries`). A mesma definição, política e implementação de tool deve atender MCP e chat — isso já é verdade no código (`AgentToolRegistry` único), mas nenhum ingress consome essa garantia em produção ainda. Referência de código Mongo: [IMongoWorkspaceService](../../../src/EsilvaSoft.SlopStudio.Application/IMongoWorkspaceService.cs), [WorkspaceService](../../../src/EsilvaSoft.SlopStudio.Application/WorkspaceService.cs) e [MongoWorkspaceService](../../../src/EsilvaSoft.SlopStudio.Infrastructure/MongoWorkspaceService.cs).

A tabela "Primeira entrega somente leitura" abaixo continua sendo a especificação normativa (schemas, limites, mapeamento de grants); ela já é consistente com as 12 tools implementadas, exceto por `get_connection_info`, que a especificação original propunha e ainda não foi implementada. `get_indexes` já usa DTO allowlist (`AgentMongoIndexSummary` em `AgentToolRegistry.Indexes.cs`), não índices BSON crus como a especificação original temia — confirmado por leitura do código nesta reconciliação.

## Tools do Agente IA para o Claude Code (ADR-056, 26/09/2026)

**Planejado, não implementado.** Pela [ADR-056](../../10-decisoes-arquiteturais.md#adr-056--agente-ia-integrado-conversas-persistidas-permissões-por-provider-modos-e-propostas-de-edição-26092026), o Claude Code (assinatura) passa a receber tools do produto por MCP local — `--mcp-config` inline apontando para `EsilvaSoft.SlopStudio.McpServer --stdio`, servidor `slopstudio`, `--strict-mcp-config` —, pelo mesmo broker e registry descritos acima, com canal e proof por sessão via `IAgentPrincipalAuthority`. No Linux o proof ainda não existe (`UnavailableClientTransportCredentialStore`) e as tools ficam indisponíveis com estado visível. O conjunto é exposto no estágio `Metadata`; hoje `get_indexes` pertence ao estágio `DerivedReads` e a reclassificação faz parte de P7-CL7-04 (tool-registry-agent).

| Tool / categoria | Input → output (proposta) | Origem e restrições |
| --- | --- | --- |
| `list_connections` / leitura | `{}` → `connections[{id,name,readOnly,open}]` | Existente; sem URI; alias para destino externo; filtrada pelas conexões permitidas em `AgentProviderPermissions` |
| `list_databases`, `list_collections` / leitura | C / D → `names[]` | Existentes |
| `get_indexes` / leitura | N → nome, keys, options, unique/sparse/TTL/partial | Existente (`AgentMongoIndexSummary` a partir de `IndexInfo`) |
| `get_cached_schema` / leitura | N → campos/tipos do cache, origem e data | **Nova.** Lê `IMetadataCache.GetSampledSchema` e o conhecimento aprendido (`ILearnedSchemaRepository`/`IKnowledgeCatalog`); **não amostra o banco** nem inicia aprendizagem; cache vazio retorna vazio com motivo |
| `get_workspace_context` / leitura | `{}` → pasta do workspace, arquivo ativo, aba (conexão › banco › coleção) | **Nova.** Snapshot do `IAgentWorkspaceContextSource` do Desktop, capturado na thread de UI; sem conteúdo de arquivo nem URI |
| `propose_file_edit` / proposta | `{path, edits:[{old_text,new_text}]}` ou `{path, new_content}` → `{proposalId, status:"registrada, aguardando revisão"}` | **Nova.** Categoria *proposta*: nunca grava em disco; `IAgentEditProposalSink` do Desktop cria a `AgentEditProposal`; caminho validado contra permissões e exclusões; não exposta no modo Planejamento |
| `approve` (interna) | Pedido `{tool_name, input, tool_use_id}` da CLI → `allow`/`deny` | Só para `--permission-prompt-tool mcp__slopstudio__approve` (modo Solicitar confirmações e pasta dedicada); invisível ao modelo; decisão só pelo gesto humano |

`get_collection_schema` (amostragem) continua dependendo do consentimento do P7-L02 e fica fora deste conjunto. As tools de escrita da seção [Escritas](#escritas-somente-após-permissão-aprovação-e-auditoria-homologadas) aparecem apenas na tela Permissões como **não disponíveis nesta versão** (lote 10 pendente). O `ClaudeCodeStreamTranslator` valida no `init` o conjunto exato `mcp__slopstudio__*` do plano.

## Contrato comum

Nomes wire em inglês, descrições de produto em pt-BR. Cada `AgentToolDescriptor` informa nome, versão do schema, descrição, input/output JSON Schema, risco, permissões, categorias de dados, limites e necessidade de aprovação. Não gerar tools por reflexão dos serviços existentes. Anotações MCP são informativas; a autorização real acontece no registry. Para destinos `ProviderExternal`, nomes de perfil definidos pelo usuário não são enviados: o campo `name` recebe alias estável derivado do ID (`Conexão <ID>`). O nome original fica disponível somente para destino `Local`; redaction heurística de texto livre não é garantia de ausência de segredos.

Schemas usam objetos fechados (`additionalProperties: false`) em todos os níveis. Nomes de banco/coleção são strings não vazias, limitadas a 255 caracteres como teto de entrada do produto e validadas depois pelas regras MongoDB aplicáveis. IDs de conexão são UUIDs opacos concedidos ao principal; inexistência e falta de acesso não revelam configuração. Nenhum schema contém senha, URI, token, `approved`, `principalId`, comandos shell ou caminhos livres.

Abreviações das tabelas:

* `C`: `connectionId` obrigatório; `D`: C + `database`; `N`: D + `collection`.
* `Q`: N + `filterEjson` (string, padrão `{}`), `projectionEjson?`, `sortEjson?`, `limit` (1–100, padrão 20), `skip` (0–10.000, padrão 0), `maxTimeMs` (1–30.000, padrão 5.000).
* `R`: leitura sem mutação (`READ_ONLY`); `W`: escrita (`WRITE`); `X`: destrutiva (`DESTRUCTIVE`); `A`: administrativa (`ADMINISTRATIVE`). Leitura ainda exige autorização de dados/custo.
* Mapeamento normativo de abreviações para grants da [política](08-permissoes-e-aprovacoes.md): `Metadata` = `ReadMetadata`; `Schema` = `ReadSchema`; `Documents` = **`ExecuteReadQueries` e `ReadDocuments`**, ambos exigidos; `Diagnostics` = `ReadDiagnostics`; `Insert` = `InsertDocuments`; `Update` = `UpdateDocuments`; `Delete` = `DeleteDocuments`; `CreateIndex` = `CreateIndexes`; `DropIndex` = `DropIndexes`; `Admin` = `AdministrativeOperations`. Escopo por principal/conexão/banco/coleção. `get_collection_schema` que amostra exige também `ExecuteReadQueries` e autorização explícita de amostragem local para derivar schema, sem conceder envio dos valores. Não confundir categoria de risco com grant.

Filtros, projeções, pipeline, documentos e valores BSON são **strings de Extended JSON canônico**, evitando conversão de Int64/Decimal128 para números JavaScript. `_id` também é EJSON, nunca GUID genérico. Envelope transporta UUID com subtipo binário/representação explícita. `ObjectId`, datas UTC, binários, regex, Decimal128, Int64 e UUID Standard/CSharpLegacy/JavaLegacy/PythonLegacy têm fixtures de roundtrip. Não reinterpretar valores como `LiteDB.BsonDocument`.

Antes do executor, parsing literal deve rejeitar JS/constructors, resolução de `ENV`, operadores de código de servidor e extensões do Console. O executor dedicado não chama `ResolveDynamicJson`, mesmo para strings aparentemente inofensivas: conteúdo externo não pode resolver variáveis privadas. Exceções de parse retornam código e localização, sem eco integral do argumento.

## Primeira entrega somente leitura

| Tool / risco / permissão | Input e output específico | Método existente e trabalho necessário |
| --- | --- | --- |
| `list_connections` / R / Metadata | `{}` → `connections[{id,name,readOnly}]` | `WorkspaceService.GetProfilesAsync`; exigir grant antes de revelar até nome/ID; filtrar e projetar DTO allowlist. Nunca serializar `ConnectionProfile`; limite 200 e `truncated`. Para `ProviderExternal`, `name` é alias derivado do ID; revalidar geração e campos projetados após avaliações assíncronas e antes de devolver |
| `get_connection_info` / R / Metadata | C → `{id,name,readOnly,availability}` | `GetProfilesAsync` + seleção por ID; omitir no primeiro recorte se `availability` exigir probe de rede. Se incluído depois, retornar somente estado local do broker, sem URI, host privado ou cofre |
| `list_databases` / R / Metadata | C → `names[]` | Preferir `IMongoMetadataSource.ListDatabaseNamesAsync`, que pede `AuthorizedDatabases=true`; `MongoWorkspaceService.GetDatabaseNamesAsync` atual não limita a essa opção. Aplicar escopo/grant antes do envio |
| `list_collections` / R / Metadata | D → `names[]` | Preferir `IMongoMetadataSource.ListCollectionNamesAsync`, que pede `AuthorizedCollections=true`; o caminho atual `MongoWorkspaceService.GetCollectionNamesAsync` não limita a essa opção. Aplicar grants de namespace antes do envio |
| `get_collection_schema` / R / Schema | N + `sampleSize` 1–100 padrão 20 → `fields[{path,bsonTypes,observedCount}],sampleSize,observedAt,source,isPartial` | `MongoMetadataSource.SampleSchemaAsync`, `SchemaSampleOptions` e `SchemaBuilder`; novo caso de uso dedicado, sem chamar autocomplete de UI |
| `sample_documents` / R / Documents | N + `limit` 1–20 padrão 5, `projectionEjson?` → `documentsEjson[]` | `QueryAsync` com limite fixado; primeira página, **não** afirmar amostra aleatória/representativa |
| `mongo_find` / R / Documents | Q → `documentsEjson[],returnedCount,hasMore` | `QueryAsync`/`MongoQuery`; limites MCP mais estritos que Core e parser literal novo |
| `mongo_find_one` / R / Documents | Q sem limit/skip → `documentEjson?` | Reuso de `QueryAsync` com limit=1; não há método especializado necessário |
| `get_document` / R / Documents | N + `idEjson` → `documentEjson?` | `QueryAsync` por `_id` exato; wrapper compõe filtro com codec BSON e não concatena código |
| `mongo_count` / R / Documents | N + `filterEjson`, `maxTimeMs` → `countEjson,estimated:false` | `CountDocumentsAsync`/`CollectionCountRequest`; parser literal e grants são pré-requisitos. Contagem Int64 sai como Extended JSON ou string decimal, nunca `double`/number JavaScript |
| `mongo_distinct` / R / Documents | N + `field`, `filterEjson`, `maximumValues` 1–100 padrão 20, `maxTimeMs` → `valuesEjson[],truncated` | `GetDistinctValuesAsync`; usa `$group` e limite, não presumir custo constante; adicionar limite total em bytes |
| `mongo_explain` / R / Diagnostics + Documents | Q → `planEjson,verbosity` | `ExplainAsync` usa hoje **executionStats**, logo executa trabalho de consulta; gate de custo e saneamento obrigatórios. Propor opção queryPlanner antes de tornar padrão barato |
| `get_indexes` / R / Metadata | N → índice por DTO allowlist | `GetIndexesAsync` devolve definições BSON completas que podem conter filtro parcial e valores. Não expor `indexesEjson[]` cru; aguardar schema explícito, grants, teto de saída e revisão de campos |

Os nomes da lista inicial do pedido são cobertos sem inventar serviços. Schema amostrado não é contrato completo da coleção; informar cobertura e origem. Schema aprendido em `LearnedSchemaCatalogSource` é conhecimento parcial com confiança/opt-out; não o apresentar como schema autoritativo nem iniciar aprendizagem/persistência automaticamente por uma tool.

Views exigem resolver sua cadeia de origem e pipeline para validar os namespaces efetivamente alcançados, com limite de profundidade e detecção de ciclo. Se permissões MongoDB não permitirem inspecionar essa definição, ou a revisão mudar antes do despacho, negar a tool nesse alvo (`TargetNotVerifiable`) em vez de presumir que o nome lógico elimina acessos indiretos. Isso é um gap do novo executor, não garantia do serviço atual.

## Leitura adicional após hardening

| Tool / risco / permissão | Schema específico | Reuso / gate de liberação |
| --- | --- | --- |
| `get_database_info` / R / Metadata | D → estatísticas projetadas | `GetDatabaseStatsAsync`; não enviar comando bruto completo |
| `get_collection_info` / R / Metadata | N → definição/estatísticas permitidas | `IMongoWorkspaceService.GetCollectionDefinitionAsync`, `GetCollectionStatsAsync`, `GetCollectionValidationAsync`; DTO dedicado sem valores não autorizados |
| `get_server_info` / R / Diagnostics | C → versão/topologia saneada | `GetServerStatusAsync`, `GetTopologyAsync`; excluir caminhos, hostnames privados, comandos em execução, usuários e diagnósticos sensíveis |
| `mongo_aggregate` / R / Documents | N + `pipelineEjson`, `limit` 1–100, `maxTimeMs` → página EJSON | `AggregateAsync`, `AggregationPipelineValidator`; extender contrato para MaxTime e política de namespaces antes da publicação |
| `analyze_indexes` / R / Diagnostics | N → índices e contadores de uso, sem recomendação automática garantida | `GetIndexesAsync` + `GetIndexUsageStatsAsync`; não há analisador de recomendações existente. Novo caso de uso deve indicar janela parcial/reinício dos contadores |

Aggregate não fica read-only apenas por seu nome. Reutilizar validação estrutural que bloqueia `$out`/`$merge` inclusive em pipelines aninhados; acrescentar allowlist de stages/operadores da tool. Verificar recursivamente destinos de `$lookup`/`$unionWith`, inclusive namespaces alternativos e pipelines `$facet`. Bloquear referências não autorizadas, namespaces de sistema, `$function`, `$accumulator`, `$where`, estágios administrativos e operadores não classificados. Limitar 32 stages e profundidade de 16 no adapter; o validador atual permite profundidade 64, portanto o gate MCP é deliberadamente mais restrito. Literais chamados `$out` não devem virar falso positivo de escrita. Limite de retorno não impede processamento caro, daí o deadline no servidor.

## Escritas somente após permissão, aprovação e auditoria homologadas

Todos os inputs incluem N, e revisão/precondição de documento quando aplicável. A identificação de humano e aprovação é interna, não parte do JSON da tool.

| Tool / risco / permissão | Input/output | Método existente / exigência adicional |
| --- | --- | --- |
| `insert_document` / W / Insert | `documentEjson` → `insertedIdEjson,affectedCount` | `InsertAsync`; aprovar documento exato, bloquear read-only, sem replay por retry |
| `update_document` / W / Update | `idEjson,updateEjson,expectedDocumentHash` → contagens/certeza | `UpdateAsync`/`DocumentUpdateRequest`; wrapper limita a um `_id`, sem upsert ou multi; implementar precondição atômica, não só reread seguido de escrita |
| `delete_document` / X / Delete | `idEjson,expectedDocumentHash` → contagem/certeza | `DeleteAsync`; `_id` exato, confirmação única por ação e precondição atômica |
| `create_index` / W / CreateIndex | `keysEjson,options` tipadas → nome | `CreateIndexAsync`/`IndexCreateRequest`; opções allowlist, índice unique mostra risco de falha e custo |
| `drop_index` / X / DropIndex | `indexName,expectedDefinitionHash` → status | `DropIndexAsync`/`IndexDropRequest`; impedir `_id_`, confirmação por ação e revalidar definição |

Hash é precondição lógica, não operador MongoDB nativo: antes de publicar update/delete, definir comparação atômica usando versão confiável do documento ou predicado equivalente validado. Se não houver precondição segura, retornar `PreconditionUnavailable`; não prometer proteção concorrente com apenas dois comandos separados. Retornar IDs do resultado em EJSON canônico; `MongoDocumentMutator.UpdateAsync` usa atualmente `UpsertedId?.ToString()`, razão adicional para manter upsert fora da baseline.

`drop_collection`/`drop_database` (X/Admin), `create_user`/`drop_user`/alterar roles (A/Admin), compact, killOp, profiler, import/export, insertMany/deleteMany, execução mongosh e scripts livres ficam **não registrados nesta v0.11.0 inicial**. Seus serviços existem (`MongoDatabaseAdministrator`, `MongoServerAdministrator`, `MongoDatabaseExportImportService`, `IScriptExecutionService`), mas expô-los exige escopo futuro explícito, política própria e novas provas. Em especial, `create_user` exigiria segredo de terceiro no input; não aceitar esse canal no registry inicial.

## Exemplo normativo de schema e envelope

```json
{
  "name": "mongo_find",
  "inputSchema": {
    "type": "object",
    "additionalProperties": false,
    "required": ["connectionId", "database", "collection"],
    "properties": {
      "connectionId": {"type":"string", "format":"uuid"},
      "database": {"type":"string", "minLength":1, "maxLength":255},
      "collection": {"type":"string", "minLength":1, "maxLength":255},
      "filterEjson": {"type":"string", "maxLength":65536, "default":"{}"},
      "projectionEjson": {"type":"string", "maxLength":65536},
      "sortEjson": {"type":"string", "maxLength":65536},
      "limit": {"type":"integer", "minimum":1, "maximum":100, "default":20},
      "skip": {"type":"integer", "minimum":0, "maximum":10000, "default":0},
      "maxTimeMs": {"type":"integer", "minimum":1, "maximum":30000, "default":5000}
    }
  }
}
```

```json
{
  "schemaVersion": 1,
  "requestId": "opaque-id",
  "status": "Succeeded",
  "data": {
    "documentsEjson": ["{\"_id\":{\"$oid\":\"507f1f77bcf86cd799439011\"},\"n\":{\"$numberLong\":\"9007199254740993\"}}"],
    "returnedCount": 1,
    "hasMore": false
  },
  "truncated": false,
  "elapsedMs": 12,
  "outcomeCertainty": "Known"
}
```

Na implementação cada linha do catálogo ganhará input **e output** schema fechado versionado e fixture independente; o schema ilustrativo não substitui a geração/validação desse catálogo. `hasMore` atual é indício (`documents.Count == limit`), não confirmação de documento seguinte. Chamadas subsequentes com skip são explícitas, limitadas e sem snapshot consistente prometido. Não inventar cursor durável até existir contrato de continuidade.

## Limites e falhas

Limites iniciais propostos, aplicados pelo servidor e iguais no chat: request total 64 KiB UTF-8; cada EJSON 64 Ki caracteres **e** sujeito ao total em bytes; resposta 256 KiB; find 100 documentos; sample 20; distinct 100 valores; default timeout 5 s, teto 30 s; no máximo 4 operações por conexão, 2 por sessão e 8 globais. Metadados no máximo 200 itens por chamada; resposta indica truncamento e orienta refinar escopo, sem falsa enumeração completa. Escrita individual deve caber no limite total de request, mesmo que MongoDB suporte documentos maiores. Espera por aprovação tem prazo separado de 120 s; só após aprovação começa o orçamento de execução MongoDB.

Orçamento aplicado durante serialização, sem cortar JSON no meio. Documento individual acima do limite retorna `ResultTooLarge`; quando uma página atinge limite entre documentos, retornar itens íntegros com `truncated:true` e razão. A autorização de saída precede transmissão de qualquer byte. Não fazer retry automático por truncamento. Erros normalizados: `InvalidArguments`, `UnknownTool`, `PermissionDenied`, `ApprovalDenied`, `ApprovalExpired`, `ReadOnlyConnection`, `ProfileChanged`, `Conflict`, `ResultTooLarge`, `DeadlineExceeded`, `Cancelled`, `ProviderUnavailable`, `AuditUnavailable`, `OutcomeUnknown`. Erro protocolar MCP e erro de execução de tool são mapeados conforme especificação pelo adapter, sem expor stack/URI.

Teste de cada tool compara execução interna e MCP com os mesmos grants, negações e resultado BSON. Validar documento com instrução maliciosa, `ENV` dentro de string, `$lookup` para coleção negada, execuçãoStats cara, perfil removido e cancelamento após despacho. Ver [testes](11-plano-de-testes.md) e [políticas](08-permissoes-e-aprovacoes.md).
