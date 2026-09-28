# Consultas avançadas — antecipação técnica (histórico)

> **Reclassificado em 18/09/2026.** Este documento foi escrito quando a v0.6.0 era "consultas avançadas". No [roadmap oficial](../09-plano-de-implementacao.md) atual, a v0.6.0 é [organização dos projetos e autocomplete básico](../phases/phase-02-v0.6.0/README.md), e a agregação passou a ser **antecipação técnica sem fase atribuída** — ver [bkl-04](bkl-04-modo-aggregation.md). O conteúdo abaixo é preservado como evidência histórica da meta textual validada em 14/09/2026; ele **não** declara requisito de fase concluído, e o modo Agregação está desativado na interface.

**Meta de consultas avançadas implementada e validada no checkout em 14/09/2026.** A validação cobre o fluxo textual descrito abaixo; não é publicação de release nem certificação de todos os itens futuros do catálogo.

## Fluxo textual

O Console aceita `db.getCollection("orders").aggregate([...])`. O modo Agregação aceita o array de stages e usa a conexão, banco e coleção vinculados à aba. O Explorer continua navegando sem executar consultas. F5 executa o conteúdo completo; Ctrl+Enter mantém o contrato de seleção/statement do Console e seleção/conteúdo completo em Agregação.

O catálogo local cobre `$match`, `$group`, `$project`, `$lookup`, `$unwind`, `$facet`, `$sort`, `$limit`, `$skip`, `$set`, `$unset` e `$count`. Em Ctrl+Espaço, posições de stage, predicado, acumulador e referência `$campo` recebem sugestões distintas. O catálogo é uma ajuda lexical, não uma prova de compatibilidade com a versão do servidor ou de evolução do schema ao longo de todos os stages.

## Validação sem execução

**Opções → Validar sintaxe** funciona desconectado. A seleção, quando existe, é validada isoladamente; caso contrário, valida-se o documento inteiro. O diagnóstico informa linha/coluna relativas ao trecho validado e seleciona a posição no editor. Texto, contexto ou modo alterados durante a operação invalidam a resposta.

O parser roda em worker, aceita cancelamento e limita a entrada a 1 milhão de caracteres. No modo Agregação, verifica array de stages, um operador por documento e pipelines aninhados de `$facet`, `$lookup` e `$unionWith`. Construtores BSON não são executados. Uma mensagem de sintaxe válida não garante tipos BSON, operadores suportados, permissões nem execução bem-sucedida no servidor.

**Opções → Formatar JSON/query/script** conserva a operação já existente, incluindo seleção, cancelamento e Ctrl+Z. Syntax highlighting e pareamento usam o editor AvaloniaEdit existente.

## Proteção de escrita e análise

O executor direto de aggregation e o cursor de leitura do Console bloqueiam `$out`/`$merge` antes de enviar o pipeline. A verificação percorre posições reais de pipelines aninhados; objetos em `$literal` não são confundidos com stages. Operadores desconhecidos permanecem editáveis e sua compatibilidade é decidida pelo servidor.

No modo Agregação, **Opções → Analisar pipeline (explain)** solicita o plano do conteúdo completo com `queryPlanner`, limite de servidor de 30 segundos e o destino capturado antes do primeiro await. O plano BSON aparece em **Mensagens**, com conexão, banco e coleção. Resultados anteriores permanecem disponíveis. O cancelamento pertence à aba; uma análise não redireciona o retorno para outra aba.

Essa ação não apresenta tempo real de execução nem contagem real de documentos examinados. Não é um explain gráfico e não acrescenta `.explain()` à API JavaScript do Console. Referências oficiais: [aggregation pipelines](https://www.mongodb.com/docs/manual/core/aggregation-pipeline/), [explain](https://www.mongodb.com/docs/v8.0/reference/command/explain/) e [formatos de planos](https://www.mongodb.com/docs/manual/reference/explain-results/).

## Histórico e contexto — revisão de 14/09/2026

Console e Agregação compartilham a coleção `consoleHistory` do proprietário LiteDB já registrado. A versão 1 recebe campos aditivos `Mode`, `Collection` e `DocumentLimit`; entradas antigas continuam como Console/100. Agregação grava o texto efetivamente executado (inclusive seleção), destino capturado, duração e status de sucesso, erro ou cancelamento. Não grava resultados, URI ou valores de ENV; o ambiente da execução direta é explicitamente **não registrado**. O opt-out de histórico e a proteção de abas com documentos de resultados impedem a gravação. Erro de persistência fica em Mensagens e preserva o resultado.

**Histórico → Abrir execução em nova aba** restaura modo, destino, texto e limite sem executar. A dica da entrada traz o destino completo quando a linha trunca. Os 18 PNGs `aggregation-history-*` cobrem 600×420, 720×520 e 900×700 nos dois temas/escalas; 600 claro e 720 escuro foram inspecionados.

Campos observados em resultados são filtrados pela origem da consulta: caminhos explícitos `db`, `getCollection`, `ConnectionPool` e `getConnection(...).getDatabase(...).getCollection(...)`. Strings, comentários e argumentos dinâmicos não viram destinos. Um prefixo simples de campo usa a origem apenas quando é única no destino atual da aba. Alterações de perfil/banco/coleção invalidam resultados. Campos de wrappers Extended JSON não entram como caminhos; `total` permanece campo, `total.$numberLong` não. O mesmo filtro atende ghost text e Ctrl+Espaço. Isso ainda não infere todos os campos derivados de stages nem variáveis JavaScript arbitrárias.

Erros de comando retornados pelo driver passam por `QueryServerDiagnostics`: código numérico e orientação em pt-BR para stage/expressão desconhecidos, acumulador inválido, estrutura e variável fora de escopo. A resposta bruta do servidor não é copiada para a mensagem. A fixture real cobre stage desconhecido com dado que não pode aparecer no diagnóstico, tanto em Agregação quanto no adaptador do Console.

## Evidência e pendências

Suíte regular final deste incremento: **643 aprovados, 0 falhas**, `phase2-final-audit.trx`, incluindo fixture MongoDB 8.0.30. Build sem avisos/erros. Testes Explicit de IA não integram esse total.

- `AdvancedAggregationTests`: bloqueio de escrita antes de conectar, estrutura, literais, 12 sugestões de stages, distinção contextual, construtores BSON, posição de erros, cancelamento e snapshot de explain entre abas.
- `ConsoleMongoIntegrationTests.PhaseTwoTwelveStagesJoinArraysAndFacetUseRealServer`: servidor MongoDB portátil, fixture independente de clientes/pedidos, pipeline de 12 tipos de stages; resultado esperado Bia/5 e contagem 2; plano real, origem preservada e ausência da coleção de saída bloqueada.
- `AdvancedAggregationUiTests`: controle real desconectado, seleção do operador inválido, ausência de chamadas MongoDB, recuperação após corrigir e 18 PNGs `aggregation-diagnostic-*` em claro/escuro, 960/1366/1920 e escalas 100/150/200%.

Histórico de Agregação, diagnósticos de servidor e seleção de pipeline real possuem implementação/evidência. A fixture real também verifica sucesso e falha de seleção na aba Console sem executar nem registrar o conteúdo completo. `AggregationHistoryTests` cobre snapshot, opt-out, falha de gravação, cancelamento, reabertura sem execução e compatibilidade de entradas antigas; `MongoCompletionTargetTests` cobre caminhos explícitos e ausência de mistura de campos entre coleções/perfis.

## Evolução dos campos e joins

`AggregationFieldInference` acompanha os stages completos antes do cursor: `$group` substitui a entrada; `$project` aplica inclusão, exclusão, renomeação e caminhos aninhados; `$set` acrescenta/substitui campos usando o mesmo snapshot de entrada para todas as atribuições; `$unset` remove o caminho e seus descendentes. `$match`, `$sort`, `$limit`, `$skip` e `$unwind` conservam os caminhos conhecidos. `$count` produz seu campo de contagem.

No `$lookup`, `localField` usa campos locais e `foreignField` usa os resultados conhecidos da coleção `from` na mesma conexão/banco. O subpipeline começa com os campos estrangeiros e recebe as variáveis `let` como referências `$$`. Ao concluir, `as` acrescenta o caminho relacionado e os filhos conhecidos. `$facet` mantém cada ramo independente e expõe os caminhos de cada saída. Tudo roda localmente: nenhuma consulta é disparada para obter sugestões.

Limites: prefixo de até 65.536 caracteres, 8.192 tokens, profundidade de parsing 64 e de pipelines 32, até 512 campos inferidos. Construtores BSON são opacos; não há avaliação de JavaScript. Transformações desconhecidas descartam a forma anterior em vez de anunciá-la como certa. Campos estrangeiros não observados não são inventados. A inferência é uma ajuda estática, não garantia de schema ou de compatibilidade com qualquer versão de servidor.

## Auditoria da meta

| Requisito da meta | Evidência inspecionada no estado final |
| --- | --- |
| Construir/executar os pipelines nomeados | `PhaseTwoTwelveStagesJoinArraysAndFacetUseRealServer`: MongoDB 8.0.30, os 12 stages do plano, join/arrays/facet, saída Bia/5 e contagem 2 |
| Sugestões contextuais de campos e operadores | `AdvancedAggregationTests`, `MongoCompletionTargetTests`, `AggregationFieldInferenceTests`; namespace, predicados/acumuladores, projeções, joins, variáveis e branches de facet |
| Aplicação no editor sem perder documento | `DerivedFieldSuggestionInsertsAtTheCursorAndPreservesUndoOffline`: Ctrl+Espaço → `$total` derivado → inserir → Ctrl+Z, nenhuma chamada remota; 90,4 ms no ensaio focado Headless (`phase2-schema-ui.trx`), não benchmark nativo |
| Validação e erros úteis | `MongoCodeValidator`, `QueryServerDiagnostics`, teste de seleção do erro no editor; stage desconhecido real com dado que não pode vazar; limite/cancelamento local |
| Execução parcial sem fallback | Fixture real executa somente seleção com pipeline e verifica falha sem executar/registrar o conteúdo completo; `ConsoleUiTests` verifica Ctrl+Enter no controle real |
| Contexto fixo, concorrência e cancelamento | `ConsoleWorkspaceTests`, `WorkspaceUiTests` e `AdvancedAggregationTests`: snapshot, retornos fora de ordem, cancelamento próprio e destino capturado do explain |
| Explorer integrado sem execução implícita | `ExplorerLoadsOnlyExpandedBankAndNeverExecutesQuery` e fluxo de teclado do Console |
| Histórico e resultados | `AggregationHistoryTests`: seleção/snapshot, opt-out, falha, cancelamento, recuperação de registros antigos e reabertura sem execução; fixture real conserva BSON/resultados |
| Formatação, destaque e delimitadores | `MvpPolishTests`/`MvpPolishUiTests`, `SyntaxHighlightingTests`/`SyntaxHighlightingUiTests`: tokens BSON, comentários/regex, undo, seleção, delimitadores e AvaloniaEdit atual |
| Segurança e apresentação | Stages de escrita bloqueados antes do envio; resultados/credenciais fora do histórico; 36 PNGs específicos de diagnóstico/histórico nos dois temas e matrizes do editor atual, com inspeção visual registrada |

Restore locked aprovado; build sem restore com `-p:UsedAvaloniaProducts=` aprovado, sem avisos/erros. **650 testes aprovados, 0 falhas**, `tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/phase2-acceptance.trx`. Testes Explicit de IA estão fora dessa contagem; IA não é necessária para a meta.

O catálogo amplo conserva itens planejados que não fazem parte da meta textual solicitada: exportação C#/mongosh e explain gráfico, entre outros. O explain entregue é bruto em `queryPlanner`, com sua semântica declarada. Homologação nativa Windows/Linux, leitor de tela e cobertura de todas as versões MongoDB não são comprovadas por Headless ou pelo servidor 8.0.30; não se atribui a esta validação uma publicação ou certificação de release.
