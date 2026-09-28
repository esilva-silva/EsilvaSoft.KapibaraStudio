# Descoberta dinâmica e aprendizado de schema

Complemento incorporado em **15/09/2026**. **Implementado no núcleo Application/Infrastructure**: substitui a decisão anterior de adiar persistência de schema. Não adiciona consultas MongoDB automáticas. Fonte principal são documentos retornados por `find` executado pelo usuário; conhecimento probabilístico fica disponível após reiniciar, pelo LiteDB já aberto em DI. A integração com o catálogo e a completion possui teste dedicado; o corpus de linguagem cobre o fluxo local e os cenários de metadata, schema aprendido e `$lookup` estrangeiro têm testes determinísticos complementares.

O aceite atual cobre contratos, análise incremental, opt-outs, geração/retention, proprietário LiteDB único, composição das fontes e consumo pelo autocomplete. Não cobre benchmark/gates de desempenho, MongoDB real ou validação multiplataforma.

## Base real e pontos de integração

[StructuredResultSet.cs](../../src/EsilvaSoft.KapibaraStudio.Core/StructuredResultSet.cs), [StructuredResultDocument.cs](../../src/EsilvaSoft.KapibaraStudio.Core/StructuredResultDocument.cs) e [ResultOrigin.cs](../../src/EsilvaSoft.KapibaraStudio.Core/ResultOrigin.cs) já oferecem `StructuredResultSet`, `ResultOrigin` (ProfileId, perfil capturado, banco/coleção), Method, Completeness, IsTruncated e documentos EJSON. Console classifica find/findOne como Complete ou PartialProjection; aggregate como Derived. Reutilizar esses contratos, sem deduzir namespace da seleção atual do Explorer.

[CollectionSchema.cs](../../src/EsilvaSoft.KapibaraStudio.Autocomplete.Core/CollectionSchema.cs) já conhece nomes, tipos BSON, arrays, evidência e ocorrência em amostras, mas não FirstSeen/LastSeen persistentes, deltas duráveis ou aprendizado por execução. Sua Merge atual reconstrói por união; não usá-la para somar todo o histórico a cada consulta.

[LiteDbConnectionProfileRepository](../../src/EsilvaSoft.KapibaraStudio.Infrastructure/LiteDbConnectionProfileRepository.cs) é proprietário único do arquivo e possui caminho assíncrono com sincronização. Estender esse proprietário (preferencialmente partial com arquivo específico), registrando `ILearnedSchemaRepository` na **mesma instância**. Não criar `new LiteDatabase`, segundo arquivo de sessão ou dependência comercial.

## Fluxo e isolamento

```text
find concluído → resultados entregues normalmente ao usuário
             └→ TryEnqueue envelope limitado (sem await de análise/persistência)
                  → BackgroundSchemaAnalyzer
                  → SchemaObservationDelta (somente estrutura/estatísticas)
                  → merge incremental + commit LiteDB
                  → snapshot de aprendizado em memória + CatalogChanged
                  → tradicional explícito/preemptivo e IA explícita/preemptiva
```

Hook após disponibilizar o resultado, uma vez por página/conjunto, em coordenador Application consumido pelos produtores de resultados. Desktop pode notificar entrega, mas não percorre JSON ou faz I/O. Métodos sem origem/método confiáveis não entram até adaptador fornecer contrato. Estado por execução capturado antes de awaits.

`TryEnqueue` faz no máximo uma seleção limitada de envelopes independentes contendo apenas as strings JSON selecionadas (ou árvores `JsonElement` clonadas), `BatchId` e a origem escalar. Não enfileirar `StructuredResultDocument`: seu `Set` aponta para `StructuredResultSet.Documents` e manteria o conjunto inteiro vivo. Não copiar/serializar o resultado inteiro nem aguardar espaço. A primeira entrega aceita somente find/findOne Complete e documentos válidos, sem copiar valores para armazenamento persistente. Trabalho de aprendizagem não é parte do sucesso da consulta: falha/fila cheia não transforma resultado em erro.

## Fila e amostragem

Parâmetros iniciais internos, a medir: até 32 documentos por lote, 64 KiB por documento, 1 MiB retido por lote, 8 MiB globais de referências contabilizadas, 32 lotes; o primeiro limite atingido prevalece. Profundidade 12, 10 000 nós por coleção, limite de elementos de array por documento. Escolher posições distribuídas pela página com semente derivada do BatchId, não só os primeiros documentos. Essa amostra continua enviesada pela query, sort/limit e permissões.

Worker único inicial, prioridade de fundo; fila cheia descarta lote novo com contador local, sem bloquear ou sobrescrever lote em processamento. Só cópias/envelopes selecionados ficam vivos até análise; liberar imediatamente ao extrair delta. Um teste com resultado grande e `WeakReference`/bytes retidos comprova que o `StructuredResultSet` não fica preso pela fila. JSON inválido/excessivo conta como skipped, nunca como ausência de campo. Não percorrer arrays ilimitados, não usar BSON original como payload persistente e não iniciar trabalho por tecla.

Produtor recebe ExecutionId + ResultSetNumber + PageSequence + origem + completude + política capturada. BatchId idempotente impede reprocessar o mesmo evento/retry. Não armazenar `_id`, hash do documento ou valores para deduplicar pessoas/documentos. Repetir find em outra execução produz novas **observações**, não prova documentos únicos; a UI deve dizer “observações de documentos analisadas”, não “total de documentos da coleção”.

## Identidade segura

Chave lógica: `(ProfileId, Database, Collection)`, conforme [DEC-L-KEY](decisions.md#dec-l-key). **`SourceGenerationId` não faz parte da chave**: revisão volátil na identidade transformaria cada troca de credencial, URI, `TargetHost` ou ambiente em linhas inalcançáveis — não referenciáveis, não contabilizáveis pela cota, não removíveis por Limpar aprendizado e não renomeáveis por DDL. Ele é coluna não-chave (`LastObservedGenerationId`) e a confiança é derivada ([DEC-L-TRUST](decisions.md#dec-l-trust)). Database/Collection com comparação ordinal; mesmo nome em outro servidor/banco não colide. Não usar nome amigável, URI textual ou hash sensível como chave durável.

O `_id` é a **codificação canônica** da chave — não o hash dela, que impediria enumerar namespaces de um perfil e o rename atômico — gravada como binário BSON, porque a colação padrão do LiteDB é cultura corrente com `IgnoreCase` e fundiria `Orders` com `orders`. A codificação é `0x01 ‖ ProfileId (16 bytes, big-endian RFC 4122) ‖ len32be(utf8(Database)) ‖ utf8(Database) ‖ len32be(utf8(Collection)) ‖ utf8(Collection)`; o byte inicial é versão **da codificação da chave**, nunca versão de formato do conteúdo, que é campo do documento. Os prefixos de comprimento são o que separa `("a", "b.c")` de `("a.b", "c")`. Contrato em `Application/SchemaLearning/LearnedSchemaKey.cs`.

Perfil renomeado, favoritado ou com cor/pasta alterada mantém identidade e geração. Edição de origem/ENV renova a geração no caminho de gravação do próprio repositório de perfis ([DEC-L-GENERATION](decisions.md#dec-l-generation)). A confiança do snapshot **não é bandeira persistida**: é derivada na hidratação em dois eixos ortogonais — origem (`Current` × `Superseded`, comparando `LastObservedGenerationId` com a geração corrente) e sessão (`Confirmed` × `Unconfirmed`). `Current`+`Unconfirmed` é servido como evidência histórica marcada, sem afirmar cobertura nem conexão ativa; `Superseded` não é servido e também não é apagado, sofrendo *rollover* no primeiro delta comitado sob a geração nova.

DDL confirmado ([DEC-L-RETENTION](decisions.md#dec-l-retention)): drop de coleção apaga o namespace; drop database apaga os descendentes; rename com origem e destino conhecidos é reescrita do `_id` (delete + insert) em uma transação, e se o destino já existir **o destino prevalece** e a origem é removida, porque mesclar somaria observações de coleções diferentes no mesmo denominador; origem ou destino desconhecido invalida ambos. Mudanças externas não observadas são tratadas por envelhecimento, sem polling/change streams novos. Desconectar cancela lotes pendentes da geração, mas não apaga aprendizado persistido; remover perfil/limpar aprendizado apaga por comando.

## Representação probabilística

Modelo de domínio proposto, independente de LiteDB/driver:

```text
LearnedSchemaSnapshot
  SchemaKey, FormatVersion, Revision, UpdatedAt, Coverage = Observed
  CompleteDocumentObservations, SampledBatches, SkippedDocuments, IsTruncated
  Fields
    PathSegments                  // não string ambígua separada por ponto
    Types: BSON type → DocumentOccurrences
    PresentDocumentObservations
    EligibleDocumentObservations
    FirstSeenUtc, LastSeenUtc
    IsArray, ArrayElementTypes, ArrayDocumentObservations, ArrayTruncated
    Evidence = LearnedResults
```

FieldId é derivado de codificação sem ambiguidade dos segmentos (comprimentos/escape versionado), não de juntar nomes com `.`. `Customer.Id` aninhado e campo literal `"Customer.Id"` são distintos. Nomes de propriedades numéricas e `a[0]` não viram índices de array. Tipos conservam BSON (`objectId`, `date`, int32/int64/decimal, null, binData/subtype quando disponível); string com aparência de data/UUID continua string. Wrappers EJSON não viram campos artificiais.

Contar presença no máximo uma vez por documento/caminho. Um campo em múltiplos elementos de array pode ter vários tipos no mesmo documento; guardar denominadores próprios: distribuição de tipos de elementos e probabilidade de presença são estatísticas distintas, sem forçar soma de percentuais a 100% quando observações multivalor coexistem.

Para escalar, guardar o contador total de observações completas elegíveis na coleção; frequência de campo = presença/total. Se o caminho foi truncado pelo orçamento, sua ausência não é observação válida; registrar cobertura parcial e não reduzir frequência como se ausente. Inicialmente descartar da estatística de ausência documentos cuja análise estrutural truncou; ainda podem fornecer presença com denominador separado. Distinguir missing de null.

Exemplo: 1 200 observações elegíveis de Customer.Id, 1 164 UUID e 36 string → 97%/3% entre observações desse campo, **não** estimativa garantida da coleção. Confidence deriva de tamanho amostral, frescor, cobertura e origem, sem confundir score de ranking com probabilidade. Não tornar Required aprendido: só validator declara Required. Índices não adicionam ocorrências; fontes mantêm estatísticas separadas.

## Projeções, consultas derivadas e privacidade

Primeira entrega persistente: somente Complete find/findOne com origem inequívoca. PartialProjection/Derived/Unknown permanecem evidência temporária da aba. Inclusão/exclusão computada, aggregate, group, lookup/facet e retorno de script não contam como documentos brutos da coleção. Extensão futura para projeções precisa máscara de cobertura explícita e testes; campos omitidos não podem diminuir frequência.

Nunca persistir resultados, JSON, filtros, prompts, literais, `_id`, valores de ENV ou credenciais. Nomes/tipos/contagens/datas bastam. Campos enum aprendidos a partir dos resultados são proibidos; enum declarado do validator continua sua fonte independente e política própria. Nomes também podem ser sensíveis: opções globais/por conexão de aprendizado/persistência e comando Limpar aprendizado. Desligar UseResultPanelContext impede nova coleta dessa fonte; respeitar restrições gerais/por conexão de privacidade aplicáveis, sem inferir autorização de persistir Input.

`SchemaLearningEnabled` e `SchemaLearningPersistenceEnabled` são true por padrão para esta funcionalidade, com exclusões por ProfileId; desligar coleta não apaga silenciosamente dados antigos. Desligar persistência permite a análise limitada do lote, mas não publica nem mantém um snapshot transitório: o resultado é contabilizado como não persistido e dados já gravados exigem o comando Limpar. Capturar política e conferir revisão novamente antes do commit; opt-out durante análise impede gravação.

## Merge e persistência incremental

`SchemaObservationDelta` contém apenas campos observados e incrementos/datas/cobertura. Serializar atualizações por SchemaKey; batch ID registrado junto do incremento em transação pelo proprietário LiteDB. Retry reutiliza BatchId; repetir delta confirmado não incrementa. Coalescer deltas adjacentes da mesma chave antes do commit, conservando IDs, sem descartar contagens.

Coleções propostas:

| Coleção local | Conteúdo / índice |
| --- | --- |
| learnedSchemaNamespaces | `_id` binário com a codificação canônica da chave, ProfileId (indexado, única forma durável de varrer/limpar por perfil), Database, Collection, SchemaFormatVersion, Revision, LastObservedGenerationId (opaco, **não-chave**), FirstLearnedUtc, LastObservedUtc e totais |
| learnedSchemaFields | ID namespace + FieldId, segmentos/ParentId/SearchName normalizado, estatísticas; índice NamespaceId e ParentLookupKey |
| learnedSchemaBatches | BatchId, NamespaceId, committedAt; dedup de retries com retenção limitada |

Atualizar apenas campos/totais tocados; não regravar schema inteiro a cada consulta. Transação curta por lote. Política inicial: flush em até 2 s ou lote limitado de 128 deltas, limite de escrita medido para não atrasar autosave/consulta de histórico. Não usar o lock LiteDB enquanto extrai schema; não aguardar flush no caminho dos resultados.

Prefixos do autocomplete consultam `NameTable` em memória, não LiteDB por tecla. Hidratar somente namespace demandado no background, com LRU de snapshots e limite de bytes; índice NamespaceId/ParentLookupKey evita scan de todas as conexões. Atualizar ramos tocados e reconstruir apenas suas tabelas fora da UI; revisão publicada atomicamente após commit. Persistência desligada ou falha de commit não publica revisão: o lote é contado como não persistido/falho, nunca é anunciado como durável e não altera o catálogo servido.

FormatVersion=1 próprio do learned schema; migrações aditivas versionadas no proprietário já registrado. Schema futuro/ilegível é isolado como indisponível, preservado sem sobrescrever vazio. Campos contadores usam inteiros 64-bit com saturação/flag, datas UTC. Limites iniciais: 64 MiB em memória, 128 MiB de cache persistido global, retenção de evidência por 90 dias com LastSeen e indicação stale; a calibrar. Evicção/limpeza em pequenos lotes, jamais dentro de render/tecla. Retenção de BatchId deve superar a janela máxima de retry; retries mais velhos são descartados, não reaplicados sem prova.

Encerramento: parar produção, tentar flush limitado (2 s propostos) no fluxo de fechamento; consulta não espera. Se falhar, exibir pendência/descartar somente cache de aprendizado não confirmado, nunca rascunhos. Sem write-ahead de documentos brutos.

## Catálogo e confiança

`LearnedSchemaCatalogSource` é `ICatalogSource` independente e consome snapshots do serviço de aprendizado. Por [DEC-L-MERGE](decisions.md#dec-l-merge) o schema aprendido **não** entra em `MergedSchema`/`FieldNode` de `MetadataCatalogSource`: a fonte constrói o próprio `CollectionSchema` com `EvidenceSources.Learned` e `SampleSize = 0`, para que nenhuma porcentagem aprendida some numeradores de duas populações sobre o denominador da amostra. Merge de apresentação une evidências por campo, mas mantém contadores learned separados de validator/índice/amostra explícita. Não somar frequências de origens incompatíveis. Complete do cache não significa coleção completa; aprendido tem cobertura Observed e decaimento por frescor.

Revisão do aprendido invalida contexto/ranking/AI facts da coleção, não todos os editores. Ghost já visível não muda pelo refresh; nova oportunidade usa revisão nova. IA recebe somente subconjunto relevante de nomes/tipos, nunca toda base persistida. Sem conexão, linguagem e aprendido histórico funcionam offline, sem iniciar MongoDB.

## Testes, benchmarks e agentes

Knowledge é dono de analyzer, deltas, serviço/fonte e persistência no proprietário existente; Architecture revisa contratos/lifetime; Testing mantém fixtures; Performance mede impacto desde primeiro hook. Tarefas **L11–L16** em [execution-plan.md](execution-plan.md). **L11–L16 implementados no escopo automatizado**: contratos, analyzer, admissão, persistência no LiteDB já registrado, fonte de catálogo, integração no fluxo de resultado e invalidação/retention têm cobertura unitária e de integração. Permanecem como ampliação mais corpus de metadata/schema, cenários de restart/erro e homologação real, todos fora dos gates de performance desta meta.

Aceite mínimo: resultado entregue com analyzer/repositório bloqueados; fila cheia não bloqueia UI; zero queries adicionais; isolamento de 3 namespaces homônimos; projeção não polui schema; BSON/UUID/array/missing/null; repetição BatchId; observações repetidas declaradas; envelope não retém `StructuredResultSet` (teste de WeakReference/limite de bytes); concorrência, disconnect/opt-out/drop/rename durante commit; recuperação após restart; corrupção/migração/erro de disco visíveis; mesmo proprietário LiteDB; arquivo/snapshot sem valores de fixtures.

Medir find→resultado antes/depois, TryEnqueue p95 (meta <1 ms), CPU/alocações por documento, backlog/drops, deltas/commit, tempo sob lock, autosave concorrente, memória retida e hidratação fria/prefixo quente. p95 de consulta não deve regredir >5% em máquina de referência sem investigação; metas são hipóteses, não resultados desta revisão.
