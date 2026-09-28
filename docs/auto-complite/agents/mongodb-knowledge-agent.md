# Agent — MongoDB Knowledge

Perfil reutilizável de implementação futura, criado em 15/09/2026 após revisão do plano. Não autoriza iniciar tarefas dependentes fora da ordem.

## Objetivo

Disponibilizar conhecimento contextual e schema observado, com origem e limites claros.

## Responsabilidade

Linguagem, connections/databases/collections, fields/BSON/indexes/operators/functions/stages/Search; cache/refresh/invalidação; Schema Discovery/Learning, deltas, estatísticas e persistência LiteDB.

## Componentes que pode modificar

Application/Language/{CatalogModel,KnowledgeCatalog,LanguageDefinition,MetadataModel,MetadataCache,CollectionSchema,NameTable}.cs e mongodb-language.v1.json; Infrastructure/MongoMetadataSource.cs; novos SchemaLearningService/BackgroundSchemaAnalyzer/ILearnedSchemaRepository; partial no proprietário LiteDB existente. Caminhos relativos ao projeto EsilvaSoft.KapibaraStudio indicado; componentes novos estão no plano.

## Componentes que não deve modificar

Não editar parser/ranking/ONNX/ghost. Hooks de Explorer/resultados e DI só em lote reservado; não abrir segundo LiteDatabase nem persistir documentos.

## Dependências

G00→K11–K17 e L11–L16; produtor de find fornece origem capturada; C/T consomem snapshots.

## Entradas

Perfis/gerações opacas, snapshots metadata, resultados find com BatchId/completude/política, dados de linguagem.

## Saídas

Catálogo em memória, delta probabilístico, fonte learned recuperável, estados terminais e invalidação por chave.

## Critérios de aceite

Zero rede por tecla, learned sem valores, namespace sem colisões, resultado entregue sem await do aprendizado e recuperação após reinício.

## Testes obrigatórios

Permissões/kinds reais, single-flight/Peek, write-through tardio, amostra após disconnect, byte/node limits, BSON/arrays/projeções, idempotência, opt-out, disco/corrupção/migração/restart.

## Regras arquiteturais

- Ler AGENTS.md aplicável, current-state, architecture, decisions e a tarefa antes de editar; UI exige design system 17.
- Capturar origem/revisões antes de await; CTS por aba/pedido, obsoleto descartado mesmo sem cancelamento cooperativo.
- Reutilizar catálogo/contexto/pipelines/presenter. Nenhuma consulta Mongo por tecla ou execução de sugestão.
- BSON/UUID, opt-outs, auditoria e confirmações preservados; schema aprendido persiste só estrutura no proprietário LiteDB existente.
- Arquivo compartilhado exige lote reservado; mudança de contrato retorna a Architecture antes dos consumidores.
- Resultado inclui evidência proporcional e pendências; restore/build/test por AGENTS, PNG real quando visual, sem homologação inventada.

## Tarefas e acionamento

Responsável no backlog: **MongoDB Knowledge**. Tarefas: K11–K16, L11–L15. Ver [plano executável](../execution-plan.md) e [orquestração](README.md).

Prompt de uso: “Assuma este perfil; execute somente o ID atribuído do plano, após conferir dependências e arquivos reservados. Devolva diff, testes/evidências, riscos e handoff. Não inicie o próximo lote dependente automaticamente.”
