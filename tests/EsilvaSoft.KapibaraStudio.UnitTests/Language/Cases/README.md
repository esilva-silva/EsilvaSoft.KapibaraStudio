# Corpus de fixtures da Fase 2 — contexto, faixas e ranking

Estado: **executado desde 21/09/2026**. O runner cobre parsing, papéis de contexto, shapes, kinds, aspas, `ReplaceSpan`, tipos BSON e ranking determinístico. As expectativas continuam derivadas da especificação (`docs/auto-complite/*`, `docs/06-editor-bson-e-uuid.md`, `mongodb-language.v1.json`) e do comportamento do MongoDB — nunca de código novo.

Regras (AGENTS.md):

- Não altere uma expectativa para acompanhar a implementação. Mudança só com justificativa na PR e referência a uma decisão desta página (D-xx) ou a uma revisão da especificação.
- Nenhum valor real: nomes, schemas e metadados são sintéticos. O único valor de identificador usado é o UUID zero (`00000000-0000-0000-0000-000000000000`).
- Uma fixture que falha por falta de **dado** no catálogo (ver D-06 a D-10) indica que falta o dado, não que a expectativa está errada.

## Estrutura

```text
Language/Cases/
  README.md                  este documento (formato e decisões)
  editor-arbitration.md      plano Headless: arbitragem de teclado e preferências (2.5)
  *.case                     fixtures de contexto, lista e faixas (2.3/2.4)
  schemas/
    clientes.json            schema sintético Projetos/Clientes
    pedidos.json             schema sintético Projetos/Pedidos
    projetos.json            schema sintético Projetos/Projetos
    workspace.json           perfis, bancos e coleções carregados no cache
    workspace-loading.json   variante: coleções de Projetos em Loading
    workspace-stale.json     variante: coleções de Projetos em Stale
    workspace-failed.json    variante: coleções de Projetos em Failed
  ranking/
    calibration/*.case       conjunto-ouro para calibrar pesos (60 %)
    validation/*.case        conjunto-ouro só para medir (40 %)
```

Caminhos citados dentro das fixtures (`schemas/...`) são relativos a `Language/Cases/`, inclusive nos arquivos de `ranking/`.

O projeto de testes copia `Language/Cases/**` para a saída com `CopyToOutputDirectory`; o runner carrega o corpus a partir desse diretório.

## Formato `.case`

### Leitura do arquivo

1. O arquivo é UTF-8 sem BOM. O runner converte CRLF em LF **antes** de interpretar; o fim de linha real do documento vem de `// eol:`.
2. **Cabeçalho**: o maior prefixo contíguo de linhas no formato `// <chave>: <valor>` em que `<chave>` pertence à tabela de cabeçalho. A primeira linha que não casa inicia o corpo. Assim, um corpo pode começar com comentário (`// db.Pedidos.aggregate([|`), desde que a linha não seja `// <chave de cabeçalho>: `.
3. **Expectativas**: o maior sufixo contíguo de linhas `// expect.<chave>: <valor>`, sem linhas em branco.
4. **Corpo**: as linhas entre os dois blocos, unidas por `\n`, sem quebra de linha final. Nunca é vazio; pode ser apenas o cursor (`|`).
5. **Diretiva `// @fill: N`**: uma linha do corpo exatamente nesse formato é substituída por `k = ceil(N / 40)` linhas idênticas `db.Pedidos.find({ total: { $gt: 0 } });` (39 caracteres + separador). As cópias se unem às demais linhas por `\n`. Os statements de preenchimento sempre apontam para `Pedidos`, para provar que o alvo do cursor não é herdado deles.
6. **`// eol: CRLF`**: depois do passo 5, todo `\n` do corpo vira `\r\n`. O padrão é LF.
7. **Cursor**: o marcador padrão é `|`. Precisa haver **exatamente uma** ocorrência no corpo expandido. O runner remove o marcador; a posição dele, em unidades UTF-16 do texto final, é o `Caret` (mesmos offsets do AvaloniaEdit).
8. **Escape do marcador**: se o texto precisar de `|` literal (por exemplo `${1|a,b|}` ou `||`), declare `// caret: ▌` (qualquer caractere único). Todo `|` passa a ser literal no corpo e em `expect.applied`, e `▌` marca o cursor. Não há escape por barra invertida porque ela conflitaria com strings e regex de JavaScript. Nenhuma fixture atual precisa disso.
9. **Texto exato**: `«…»` numa única linha, conteúdo literal e sem escapes; não pode conter `»` nem quebra de linha. `«»` é o texto vazio.
10. **Listas** usam `, ` como separador; itens não contêm vírgula.
11. Chaves repetíveis: `schema`, `spec`, `note`, `expect.insert`, `expect.applied`, `expect.detail` e `expect.editRange`. As demais aparecem no máximo uma vez.

### Cabeçalho

| Chave | Uso | Semântica |
| --- | --- | --- |
| `case` | Obrigatória | Igual ao nome do arquivo sem `.case`; única em todo o corpus |
| `mode` | Obrigatória | `Console` → `EditorDialects.Console`; `Script` → `EditorDialects.MongoshScript`; `Agregação` → `EditorDialects.AggregationJson` (nomes dos modos da aba) |
| `target` | Obrigatória | `TabTarget` da aba no formato `conexao/banco/colecao`. Segmentos finais podem faltar (`servidor-alfa/Projetos` = aba sem coleção). `-` = aba sem perfil. Nomes não contêm `/` |
| `connected` | Opcional (padrão `true`) | `false` = perfil da aba desconectado. Nada dessa conexão entra no cache, nem schemas declarados, porque `IMetadataCache.Disconnect` remove tudo do perfil. As demais conexões seguem o workspace |
| `metadata` | Opcional | Arquivo de workspace (formato abaixo). Sem ele, o cache não tem bancos nem listas de coleções; as conexões citadas em `target`/`schema` existem como perfis conectados com escopos `Unknown` |
| `schema` | Opcional, repetível, lista | Schemas carregados como evidência da coleção indicada em `namespace` de cada arquivo. Declarar um schema faz a coleção existir no cache com definição e índices `Fresh` (ou o `state` do arquivo), mesmo sem `metadata` |
| `trigger` | Opcional | `Invoked` (padrão), `Automatic` ou `TriggerCharacter(x)`. Reservada; nenhuma fixture a usa |
| `eol` | Opcional | `LF` (padrão) ou `CRLF` |
| `caret` | Opcional | Marcador alternativo do cursor (item 8) |
| `group` | Obrigatória só em `ranking/` | Grupo de métrica: `FilterKey`, `OperatorObject`, `DatabaseMember`, `CollectionMember`, `PipelineElement`, `TypedValue`, `Update`, `SortProjection`, `LookupBody`, `GroupBody` |
| `spec` | Recomendada, repetível | Seção da especificação ou comportamento MongoDB que justifica as expectativas |
| `note` | Opcional, repetível | Explicação; cita D-xx quando depende de decisão pendente |

Ambiente implícito de todas as fixtures:

- modo de identificador `Standard` e representação UUID `Standard`;
- `AutocompleteSettings` padrão;
- versão do servidor desconhecida, logo nada é filtrado por versão;
- `CompletionUsageTracker` vazio;
- `TimeProvider` fixo;
- invocação explícita (`Ctrl+.`).

### Expectativas

A coluna "Etapa" indica quem consegue verificar a chave:

- **2.3**: somente `CompletionContext` (ou `PipelineInfo`), sem catálogo;
- **2.4**: `CompletionList` do `CompletionService`;
- **rank**: métricas do conjunto-ouro.

O runner da 2.3 ignora chaves 2.4 e rank; o da 2.4 verifica todas.

| Chave | Etapa | Sintaxe | Semântica exata |
| --- | --- | --- | --- |
| `expect.role` | 2.3 | Nome | `Role` igual ao `CursorRole` (`StatementStart`, `MemberAccess`, `IndexerString`, `CallArgument`, `PropertyKey`, `PropertyKeyString`, `PropertyValue`, `ArrayElement`, `StringArgument`, `FieldReferenceString`, `VariableReferenceString`, `NonCompletable`) |
| `expect.receiver` | 2.3 | Nome | Tipo da expressão à esquerda do membro (`Database`, `Collection`, `Cursor`, `Connection`), como em `Scopes`/assinaturas do catálogo |
| `expect.shape` | 2.3 | ShapeId | `Expected.ShapeId` igual ao nome do shape em `mongodb-language.v1.json` |
| `expect.kinds` | 2.3 | Lista de `SymbolKind` | `Expected.SymbolKinds` contém **todos** os tipos listados |
| `expect.kinds.not` | 2.3 | Lista de `SymbolKind` | `Expected.SymbolKinds` não contém **nenhum** |
| `expect.parentField` | 2.3 | Caminho ou `-` | `ParentField` igual ao caminho; `-` = nulo. Caminhos usam a notação de referência de campo abaixo |
| `expect.valueTypes` | 2.3 | Lista ou `-` | Conjunto **exato** dos tipos BSON do campo pai, com os nomes usados nos schemas (`uuid`, `string`, `int`…). `-` = tipo desconhecido/vazio |
| `expect.objectDepth` | 2.3 | Inteiro | `ObjectDepth` |
| `expect.target` | 2.3 | `con/banco/col Confiança` | `Target` igual ao namespace. Coleção vazia se escreve `con/banco/`. A confiança (`Explicit`, `Inferred`, `TabDefault`, `Unknown`) é opcional; sem ela, não é verificada. `Unknown` sozinho = `Target` nulo **ou** `Confidence == Unknown` (D-15) |
| `expect.target.not` | 2.3 | `con/banco/col` | O alvo não pode ser esse namespace, qualquer que seja a confiança |
| `expect.replace` | 2.3 | `«texto»` | Texto coberto por `ReplaceSpan`; o span contém o cursor (`Start ≤ Caret ≤ End`). É a faixa do contexto: token parcial, e dentro de string somente o conteúdo (D-12) |
| `expect.insertSpan` | 2.3 | `«texto»` | Texto coberto por `InsertSpan`, que termina no cursor |
| `expect.quote` | 2.3 | `None`/`Double`/`Single` | Delimitador da string que envolve o token do cursor; `None` fora de string. O estilo dominante de inserção é verificado por `expect.insert` (D-04) |
| `expect.stage` | 2.3 | `<índice> <nome>` | Índice (base 0) e nome do stage que contém o cursor em `Pipeline`; nome `-` quando o objeto do stage ainda não tem chave |
| `expect.fields` | 2.3 | Lista ou `(unknown)` | Conjunto **exato** de `PipelineInfo.ApplyFields(sourceFields, foreignFields)` no stage do cursor. `sourceFields` = caminhos (`CollectionSchema.Paths()`) da coleção de entrada do pipeline: o alvo do statement, ou a coleção `from` num sub-pipeline de `$lookup`. `foreignFields(c)` = caminhos do schema declarado de `c` (vazio se ausente). `(unknown)` = resultado vazio e, se o contrato expuser estado, `Unknown` |
| `expect.fields.include` / `.exclude` | 2.3 | Lista | Contém todos / não contém nenhum |
| `expect.variables` | 2.3 | Lista ou `-` | Conjunto **exato** dos nomes (sem `$$`) em `Variables`. Variáveis de sistema não entram; `-` = vazio |
| `expect.existingKeys` | 2.3 | Lista | As chaves já presentes no objeto do cursor (em `Expected`) incluem todas as listadas |
| `expect.diagnostics` | 2.3 | Lista de códigos | `Diagnostics` contém cada código (nomes propostos em D-21) |
| `expect.present` | 2.4 | Lista de itens | Cada item aparece em `CompletionList.Items` |
| `expect.absent` | 2.4 | Lista de itens | Nenhum item aparece. Se a lista vier truncada, o runner reconsulta sem limite; se não puder, o resultado é inconclusivo, nunca aprovado |
| `expect.top5` | 2.4 | Lista de itens | Todos estão entre os 5 primeiros; a ordem entre eles não é verificada. Com 5 itens listados, equivale ao conjunto exato do top-5 |
| `expect.top5.not` | 2.4 | Lista de itens | Nenhum está entre os 5 primeiros (penalidade de tipo, sem remoção) |
| `expect.empty` | 2.4 | `true` | `Items` vazio; a lista não abre |
| `expect.incomplete` | 2.4 | `true`/`false` | `CompletionList.IsIncomplete` |
| `expect.listState` | 2.4 | Nome | Estado ou linha de ação da lista (D-21) |
| `expect.detail` | 2.4 | `<item> => «substring»` | `LabelDetail` do item contém o texto |
| `expect.insert` | 2.4 | `<item> => text «…»` ou `<item> => snippet «…»` | Texto da `CompletionEdit`; `snippet` = sintaxe LSP (`$1`, `${1:x}`, `$0`, `\$`) |
| `expect.editRange` | 2.4 | `<item> => «texto»` | Texto coberto pela faixa de substituição **do item**, quando ela difere de `ReplaceSpan` (coleção com caractere inválido, caminho com ponto) |
| `expect.applied` | 2.4 | `<item> => «linha»` | Aplica a edição do item sobre um `StringTextSnapshot` e compara a **linha** do documento que contém o cursor final. `|` marca o cursor final; sem `|`, o cursor não é verificado. Em snippet, placeholders assumem o texto padrão e o cursor fica no início do primeiro tab stop (`$1`, ou `$0` se não houver) |
| `expect.best` | rank | Item | Item correto para top-1/top-5 |
| `expect.acceptable` | rank | Lista de itens | Itens aceitáveis para MRR; `best` sempre é aceitável |
| `expect.invalidTop5` | rank | Lista de itens | Nunca podem aparecer no top-5. Só entra item **inválido para o shape** (filtro rígido: papel/shape, dialeto, chave exclusiva, namespace); item apenas penalizado não entra |

### Referência a itens

| Forma | Casa |
| --- | --- |
| `Label` | Qualquer item com `Label` igual (ordinal) |
| `Kind:Label` | Item com `Kind` igual ao nome do `SymbolKind` e `Label` igual; desambigua rótulos repetidos (`QueryOperator:$gt` × `ExpressionOperator:$gt`, `UpdateOperator:$set` × `AggregationStage:$set`) |
| `Field:caminho` | Item `Field` pelo **caminho** (`FieldNode.Path`), não pelo rótulo, cuja origem é o alvo resolvido do contexto ou os campos derivados do pipeline |
| `Field:caminho@Colecao` | Item `Field` do schema da coleção indicada, na mesma conexão e banco da aba. Serve para provar o filtro rígido de namespace |
| `Field:["a.b"]` | Caminho por segmentos em JSON. Obrigatório para nome com ponto literal; `Field:["Cliente","Id"]` equivale a `Field:Cliente.Id` (D-17) |
| `Snippet:id` | Snippet pelo id do catálogo (`stage.match`, `filter.equals`…), qualquer que seja o rótulo |
| `Collection:nome`, `Database:nome`, `Connection:nome`, `Index:nome` | Item do tipo correspondente pelo nome |

### Métricas do conjunto-ouro

Por grupo e por divisão (`calibration`, `validation`):

- **top-1**: percentual de casos em que `best` é o primeiro item;
- **top-5**: percentual em que `best` está entre os 5 primeiros;
- **MRR**: média de `1/posição` do primeiro item de `acceptable`, com zero quando ausente (ranking.md §Confiança);
- **gate**: nenhum item de `invalidTop5` no top-5, 0 % em todo o conjunto; é invariante, não métrica calibrável.

A calibração (busca em grade de `RankingProfile`) usa **somente** `calibration/`. A `validation/` só é medida e comparada antes/depois, nunca usada para escolher pesos.

Os valores de MRR/top-1/top-5 ainda não são baseline desta entrega; o runner atual valida presença/ordenação publicada, sem executar gates de performance.

## Schema sintético (`slop-case-schema/1`)

```json
{
  "format": "slop-case-schema/1",
  "namespace": { "connection": "servidor-alfa", "database": "Projetos", "collection": "Clientes" },
  "state": "Fresh",
  "sampleSize": 100,
  "fields": [
    { "path": "status", "types": { "string": 97 }, "occurrences": 97, "evidence": ["sample", "index"] },
    { "path": "items", "types": ["array"], "array": true, "arrayOfDocuments": true, "evidence": ["validator"] },
    { "segments": ["versao.schema"], "types": { "int": 100 }, "occurrences": 100, "evidence": ["sample"] }
  ],
  "indexes": [ { "name": "status_1_criadoEm_-1", "keys": { "status": 1, "criadoEm": -1 }, "unique": false } ]
}
```

| Campo | Semântica |
| --- | --- |
| `namespace` | Conexão, banco e coleção a que o schema pertence |
| `state` | Frescor de definição, índices e amostra: `Fresh` (padrão), `Stale`, `Loading`, `Failed` |
| `sampleSize` | Tamanho da amostra (0 sem amostra) |
| `fields[].path` | Caminho com ponto. Todo pai de um caminho aninhado aparece antes como campo |
| `fields[].segments` | Alternativa a `path`, obrigatória quando algum nome contém `.` literal |
| `fields[].types` | Array de nomes BSON (contagem 1 cada, evidência declarada) ou objeto nome → contagem (evidência de amostra; a soma é igual a `occurrences`). `uuid` = binData subtype 3/4, mesmo nome devolvido por `SchemaBuilder.ExtendedJsonType` (D-16) |
| `fields[].occurrences` | Documentos da amostra com o campo; exige evidência `sample` |
| `fields[].elementTypes` | Tipos dos elementos de array escalar |
| `fields[].array` / `arrayOfDocuments` / `required` | `FieldTraits.Array` / `ArrayOfDocuments` / `Required` (este só com evidência `validator`) |
| `fields[].enumLiterals` | Literais declarados por validator (`FieldTraits.Enum`) |
| `fields[].evidence` | `validator`, `index`, `results`, `sample`, `history` (`EvidenceSources`). `index` aparece se e somente se o caminho é chave de algum índice |
| `indexes[]` | `name`, `keys` (objeto ordenado), `unique` opcional |

Conversão para os tipos reais, usando só a API pública de `SchemaBuilder` (Application não tem `InternalsVisibleTo` para os testes):

- **validator**: montar um `$jsonSchema` com `properties` aninhadas. Para `arrayOfDocuments`, usar `items.properties`; usar `bsonType` a partir de `types` (`uuid` → `binData`, ver D-16), mais `required` e `enum`. Depois chamar `AddJsonSchema`.
- **index**: `new IndexInfo(Name, KeysJson, Options: "", Definition: "", Unique, Sparse: false, Ttl: "", PartialFilter: "")` e depois `AddIndexes`.
- **sample**: gerar `sampleSize` instâncias de `SampledDocument`. O campo aparece nos `occurrences` primeiros documentos, com tipos distribuídos na ordem declarada das contagens. Arrays usam `SampledElement` com `elementTypes`. Depois chamar `AddSample`.
- **results/history**: não usados nesta entrega.
- **metadados**: `CollectionMetadata(Kind, Validator)` e o índice vão para o cache falso com o `state` do arquivo.
- **coleção sem schema declarado**: numa coleção listada no workspace sem `// schema:`, definição e índices são `Fresh` e **vazios**, sem amostra. É o estado "campos desconhecidos" (D-23).

## Workspace sintético (`slop-case-workspace/1`)

```json
{
  "format": "slop-case-workspace/1",
  "connections": [
    { "name": "servidor-alfa", "connected": true, "serverVersion": null,
      "databases": { "state": "Fresh", "items": [
        { "name": "Projetos", "collections": { "state": "Fresh", "items": [ { "name": "Clientes", "kind": "Collection" } ] } } ] } }
  ]
}
```

| Campo | Semântica |
| --- | --- |
| `connections[].name` | Nome do perfil (`ConnectionProfile.Name`). O runner cria perfis sem URI real e identidade estável por nome |
| `connected` | Estado do perfil. `// connected:` do cabeçalho sobrescreve apenas o perfil da aba |
| `serverVersion` | `null` = desconhecida |
| `databases.state` / `collections.state` | `MetadataFreshness` do escopo (`Fresh`, `Stale`, `Loading`, `Failed`, `Unknown`) |
| `items` | Valor do escopo; `null` = sem valor (por exemplo, `Loading` inicial ou `Failed`) |
| `kind` | `CollectionKind`: `Collection`, `View`, `TimeSeries` |

O workspace nunca contém schema. Schemas entram apenas por `// schema:`, para que cada fixture declare explicitamente a evidência disponível.

Dados sintéticos:

| Perfil | Banco | Coleções |
| --- | --- | --- |
| `servidor-alfa` | `Projetos` | `Clientes`, `Pedidos`, `Projetos`, `pedidos-2025`, `historico-clientes` (view) |
| `servidor-alfa` | `Vendas` | `Faturas`, `Metas` |
| `relatorios` | `Vendas` | `Faturas`, `Comissoes` (time series) |

## Mapeamento dos casos de `MongoCompletionTargetTests`

Os nomes em inglês foram trocados pelos sintéticos pt-BR:

- `Dev` → `servidor-alfa`, `shop` → `Projetos`
- `Prod` → `relatorios`, `other` → `Vendas`
- `orders` → `Pedidos`, `customers` → `Clientes`, `customer-history` → `historico-clientes`
- `variable` → `variavel`

A aba dos casos migrados mantém o destino original (banco, sem coleção).

| Teste atual | Fixture |
| --- | --- |
| `ExplicitPaths…("db.orders.aggregate([{ $match: {")` | `target-explicit-member-aggregate` |
| `ExplicitPaths…("db.getCollection('customer-history').find({")` | `target-explicit-getcollection-string` |
| `ExplicitPaths…("ConnectionPool.Prod.other.orders.aggregate([")` | `target-explicit-connectionpool` |
| `ExplicitPaths…("getConnection('Prod').getDatabase('other').getCollection('orders').aggregate([")` | `target-explicit-getconnection-chain` |
| `ExplicitPaths…("db.orders.find({}); db.customers.find({")` | `target-multiple-statements` |
| `UnknownOrCommented…("// db.orders.aggregate([")` | `target-commented-unknown` |
| `UnknownOrCommented…("'db.orders.aggregate(['")` | `target-string-literal-unknown` |
| `UnknownOrCommented…("db.getCollection(variable).find({")` | `target-getcollection-variable-unknown` |
| `UnknownOrCommented…("getConnection(variable).getDatabase('other').orders.find({")` | `target-getconnection-variable-unknown` |
| `UnknownOrCommented…("db.orders.find({}); alias.aggregate([")` | `target-undeclared-alias-unknown` |
| `LookupUsesRelatedCollectionResults…` (foreignField × localField) | `doc06-lookup-foreign-field-single-quote`, `agg-lookup-local-field` |

`BsonWrappersAreNotSuggestedAsDocumentFields` e `ResultFieldsFollowCollectionAndDiscardOldProfileSnapshot` testam inferência de resultados e troca de perfil. São comportamento, não contexto de cursor, e continuam como testes de código.

## Inventário

| Categoria | Arquivos | Conteúdo (positivos e negativos) |
| --- | --- | --- |
| `members-` | 15 | `db.`, prefixo com caixa, `db.Pedidos.`, cursor após `find({})`, `findOne` sem cursor (neg.), `getConnection("relatorios").`, `ConnectionPool.relatorios.Vendas.`, `ConnectionPool.`, `db["historico-clientes"].`, `db["`, `getSiblingDB("Vendas").Me`, `getCollection("`, receptor desconhecido (neg.), escopo Loading/Stale/Failed |
| `filter-` | 15 | Raiz (com operadores inválidos na raiz), OperatorObject UUID/string/array/campo desconhecido, `$and` com array, `$or` em posição de elemento (neg.), `$elemMatch`, caminho com ponto entre aspas em array, recuperação sem aspas, `$expr` objeto e referência, valor UUID, sem schema, `$text` |
| `update-` | 11 | `$set` com prefixo, operadores de 1º nível, `$push` + `$each`, `$set` sem modificadores (neg.), posicional em array, posicional em subdocumento (neg.), `items.$[linha].`, `UpdateOptions`, pipeline de update, `replaceOne` (neg.), operadores de update em filtro (neg.) |
| `agg-` | 22 | Chave de stage, chave exclusiva (neg.), elemento com snippets, `$match` após `$sort`, `$project`, `$group` (`_id`, referência, corpo, acumulador × expressão), `$lookup` (localField, corpo, sub-pipeline, `let`/`$$`), `$$` sem `let` (neg.), campos após `$unwind`/`$facet`/dentro de ramo/`$count`/desconhecido/`$lookup`+`$unwind`/`$replaceRoot`/`$set`, modo Agregação TabDefault |
| `cursor-` | 8 | Valor de sort, chaves de sort com namespace, `project`, `hint` por nome e por padrão, `collation`, `limit` (neg.), projeção de `find` |
| `search-` | 6 | Corpo de `$search`, `compound`, elemento de `must`, `text.path`, campos após `$search` Unknown, operadores Search fora de `$search` (neg.) |
| `noncompletable-` | 11 | Comentário de linha e bloco, número (argumento e valor), regex, string comum, string com `db.` (Script), template literal; 3 controles positivos (após comentário, divisão × regex, após regex) |
| `target-` | 24 | Os 10 casos migrados, alias `const` Inferred, `let` reatribuído, parâmetro que sombreia, `const` em bloco, `const` após uso, `getCollection(constante)` Unknown, `getSiblingDB`, indexador, documento > 64 KiB (cursor no fim, no meio, alias distante), 1 MiB, CRLF + acentos, par substituto UTF-16 |
| `incomplete-` | 7 | Fechamento ausente no fim e antes do statement seguinte, vírgula dupla, vírgula faltando, string não fechada na linha anterior, pipeline truncado, token inesperado |
| `dialect-` | 13 | `db.Projetos.` (3 modos), início de statement (3 modos), `[ { } ]` (3 modos), cursor no Script, `getConnection` no Script, `getSiblingDB` Script × Console |
| `doc06-` | 13 | Os 11 casos obrigatórios de 06; `$lookup` e ponto literal têm duas fixtures cada |
| `meta-` | 11 | Os 10 exemplos da meta/context-engine; `"Cliente.|` e `Cliente.|` são arquivos separados |
| `ranges-` | 18 | As 9 linhas da tabela de faixas e aspas (algumas com variantes) + replace × insert no meio do token |
| `ranking/calibration` | 30 | 3 por grupo × 10 grupos |
| `ranking/validation` | 20 | 2 por grupo × 10 grupos; nenhum corpo repetido da calibração |
| **Total** | **224** | + 7 JSON sintéticos |

### Casos obrigatórios de 06

| Caso | Fixture |
| --- | --- |
| `{ "sta` em filtro | `doc06-filter-sta` |
| `{ "age": { "$g` | `doc06-age-gt-operator` |
| Update no primeiro nível | `doc06-update-first-level` |
| Pipeline após `$project` | `doc06-pipeline-after-project` |
| `$group` e estágio seguinte | `doc06-group-next-stage` |
| `$lookup` | `doc06-lookup-from`, `doc06-lookup-foreign-field-single-quote` |
| `$$` | `doc06-variables-filter-as` (e `agg-let-variable`) |
| `items.$[item]` | `doc06-array-filters-identifier` |
| Pipeline de update | `doc06-update-pipeline` |
| Sem conexão | `doc06-disconnected` |
| Campo com ponto literal | `doc06-literal-dot-not-a-path`, `doc06-literal-dot-getfield` |

### Exemplos da meta (context-engine.md §Exemplos)

| Exemplo | Fixture |
| --- | --- |
| `db.Clientes.find({ | })` | `meta-find-filter-root` |
| `Id: { | }` UUID | `meta-operator-object-uuid`, `filter-operator-object-uuid` (formato literal de testing.md) |
| `db.|` | `meta-db-members` |
| `db.Clientes.|` | `meta-collection-members` |
| `"Cliente.|` e `Cliente.|` | `meta-dotted-quoted`, `meta-dotted-unquoted` |
| `$match` | `meta-agg-match` |
| `$group` acumulador | `meta-agg-group-accumulator` |
| `$lookup` `foreignField` | `meta-agg-lookup-foreignfield` |
| `updateOne … $set` | `meta-update-set` |
| `find({}).sort({ | })` | `meta-cursor-sort` |

### Tabela de faixas e aspas (traditional-autocomplete.md)

| Linha da tabela | Fixtures |
| --- | --- |
| 1 · Coleção identificadora | `ranges-collection-identifier` |
| 2 · Coleção com caractere inválido | `ranges-collection-invalid-char`, `ranges-collection-invalid-char-empty-member` |
| 3 · Campo simples conforme estilo | `ranges-field-unquoted-style`, `ranges-field-double-quoted-style`, `ranges-field-single-quoted-style` |
| 4 · Caminho com ponto | `ranges-dotted-from-unquoted` (critério de aceite 6), `ranges-dotted-from-open-quote` |
| 5 · Dentro de string aberta | `ranges-string-open-without-closing`, `ranges-string-open-with-closing`, `ranges-dotted-closed-quote` (sobreposição 4/5) |
| 6 · Operador conforme estilo | `ranges-operator-unquoted-style`, `ranges-operator-quoted-style`, `ranges-operator-dollar-optional` |
| 7 · Referência de campo | `ranges-field-reference` |
| 8 · Método sem `(` | `ranges-method-without-paren-snippet` |
| 9 · Método com `(` | `ranges-method-with-paren-name-only` |

## Decisões pendentes

Cada item registra a ambiguidade, a opção adotada nas fixtures e quem precisa confirmar. Se a decisão final for outra, altere as fixtures citadas **com referência a este item** na PR.

| Id | Ambiguidade ou contradição | Opção adotada | Fixtures | Confirmar com |
| --- | --- | --- | --- | --- |
| D-01 | O exemplo de testing.md põe `$size` em `expect.absent` para UUID. ranking.md §1 manda penalizar incompatibilidade de tipo sem remover; context-engine diz "penalizados ou removidos" | ranking.md: `$size`/`$regex` em `expect.top5.not`, nunca em `absent` | `filter-operator-object-uuid`, `meta-operator-object-uuid` | Architecture |
| D-02 | O top-5 esperado para UUID (`$eq, $in, $ne, $nin, $exists`) não sai dos dados atuais: `$gt/$gte/$lt/$lte` têm `applicableTypes` vazio (qualquer tipo) e rótulos mais curtos que `$exists`, então o desempate os coloca no top-5 | Mantida a expectativa da especificação. Exige dado, por exemplo operadores de faixa sem compatibilidade plena com `binData/uuid` | `filter-operator-object-uuid` | Knowledge + Ranking |
| D-03 | Chave com ponto sem aspas: context-engine diz "papel `PropertyKeyString` (ou `DottedKeyRecovery`)", mas `DottedKeyRecovery` aparece como recuperação e não como papel | Papel `PropertyKey` + diagnóstico `DottedKeyRecovery` | `meta-dotted-unquoted`, `filter-dotted-unquoted-recovery`, `ranges-dotted-from-unquoted` | Context |
| D-04 | A tabela de papéis não tem papel para string em posição de valor com shape de nome (`foreignField: "…"`, `from: "…"`, `path: "…"`, `$getField: "…"`). A semântica de `QuoteStyle` (aspas que envolvem × estilo a inserir) também não é explícita | `PropertyValue` com `Quote` = delimitador envolvente; o estilo dominante é verificado só por `expect.insert` | `meta-agg-lookup-foreignfield`, `doc06-lookup-*`, `search-text-path`, `doc06-literal-dot-getfield` | Context |
| D-05 | As linhas 4 (caminho com ponto) e 5 (string aberta) da tabela de faixas se sobrepõem quando a string já fecha adiante; a posição final do cursor difere conforme a linha aplicada | Afirmar só o texto final, sem cursor | `ranges-dotted-closed-quote` | Completion |
| D-06 | `OperatorObject` inclui a categoria `evaluation`, que traz `$expr`, `$jsonSchema`, `$text`, `$where` e `$comment`, todos válidos só no nível do filtro no MongoDB | Comportamento MongoDB: `$expr` inválido dentro de `{ campo: { … } }`. Exige restringir nomes no shape | `rank-opobj-int-exists` | Knowledge |
| D-07 | `$not` (categoria logical) é válido em `{ campo: { $not: { … } } }`, mas `OperatorObject` exclui `logical` | Comportamento MongoDB: `$not` presente; `$and`/`$or`/`$nor` ausentes | `filter-operator-object-string` | Knowledge |
| D-08 | `$elemMatch.valueShape = Filter` sem escopo de elemento; knowledge-catalog diz que `FieldPath` usa o escopo atual, inclusive elemento de array | Chaves relativas ao elemento (`items.*`); campos raiz ausentes | `filter-elemmatch` | Knowledge + Context |
| D-09 | O catálogo não liga `$each/$position/$slice/$sort` ao valor de `$push`/`$addToSet` (`FieldValueMap → FieldValue`) | Comportamento MongoDB: modificadores presentes só nesses dois operadores | `update-push-each`, `update-set-object-no-modifiers-negative`, `rank-update-addtoset-each` | Knowledge |
| D-10 | Faltam dados: literais de `SortDirection` (`1`, `-1`, `{ $meta }`); `hint` com parâmetro `Any` (nome de índice / padrão de chave); `path` de Search com valor `Any` (campo); escopo de variável em `$filter.as`/`$map.as` (valor `Any`) | Expectativas pelo comportamento MongoDB; cada uma exige shape/dado novo | `cursor-sort-value`, `cursor-hint-*`, `search-text-path`, `doc06-variables-filter-as` | Knowledge |
| D-11 | Alvo de acesso a membro de `db.` (sem coleção) não aparece na tabela de resolução; a confiança fica indefinida | Namespace de banco (`con/banco/`) sem confiança verificada | `meta-db-members`, `members-getsiblingdb`, `members-connectionpool-database` | Context |
| D-12 | A tabela de faixas descreve faixas por item (coleção inválida inclui `.`; caminho com ponto inclui a aspa aberta), mas `CompletionContext` tem um único `ReplaceSpan` | `ReplaceSpan` = token parcial (só o conteúdo dentro de string); a faixa específica do item fica em `CompletionEdit` (`expect.editRange`) | `ranges-collection-invalid-char*`, `meta-dotted-quoted`, `ranges-dotted-from-open-quote` | Architecture (contrato `CompletionEdit`) |
| D-13 | Array literal solto (`[ { | } ]`) no Console/Script: a inferência legada trata prefixos `[` como pipeline; context-engine deriva o shape da assinatura da chamada | Sem assinatura não há shape: nenhum stage nem campo fora do modo Agregação | `dialect-pipeline-array-*` | Context |
| D-14 | Shadowing de `const` em bloco e alias declarado fora da janela > 64 KiB: a especificação proíbe herdar destino errado, mas não fixa se o resultado é `Inferred` ou `Unknown` | Só `expect.target.not` (a const externa ou o preenchimento nunca podem ser o alvo) | `target-block-shadow-not-outer`, `target-large-document-distant-alias` | Context |
| D-15 | Alvo desconhecido: `Target` nulo × `NamespaceTarget` com `Confidence = Unknown` | Ambos aceitos por `expect.target: Unknown` | todas as `target-*-unknown`, `members-unknown-receiver-negative` | Architecture |
| D-16 | Nome do tipo UUID: `SchemaBuilder` só produz `uuid` a partir de resultados (evidência `Results`); o validator produz `binData` sem subtype; a meta mostra "binData · UUID". Sem `InternalsVisibleTo`, o runner não cria `uuid` com evidência de validator | Schemas e `expect.valueTypes` usam `uuid`. É preciso API pública (ou `InternalsVisibleTo`) para declarar subtype sem inventar evidência de resultados | `clientes.json`, `pedidos.json`, `projetos.json`, `*-uuid*` | Knowledge + Architecture |
| D-17 | Nome com ponto literal: `FieldNode.Find` e `SchemaBuilder.AddPath` dividem por `.`, logo `versao.schema` literal e `versao` → `schema` colidem | Identidade por segmentos (`segments`, `Field:["…"]`), como pede context-engine §Revisão | `doc06-literal-dot-*`, `clientes.json` | Knowledge |
| D-18 | Formato do detalhe de campo amostrado; o exemplo é "string · 98%" | Afirmar substrings `string` e `97%` | `doc06-filter-sta` | Completion/UI |
| D-19 | Chaves `Fixed` de shapes (`from`, `localField`, `upsert`, `locale`, `_id`, `index`) e literais (`1`, `-1`) não têm `SymbolKind` | Casamento só por rótulo | `agg-lookup-body-keys`, `update-options-keys`, `cursor-collation-keys`, `cursor-sort-value`, `rank-lookup-*`, `rank-group-id-key` | Knowledge |
| D-20 | Rótulo e tipo dos identificadores de `arrayFilters` e das variáveis de escopo: `SymbolKind` não tem `ScopedVariable`, embora knowledge-catalog o cite | Rótulos `$[item]` e `$$item`; tipo não verificado | `doc06-array-filters-identifier`, `doc06-variables-filter-as`, `agg-let-variable` | Knowledge |
| D-21 | Nomes de estados da lista e de diagnósticos não definidos em contrato | Propostos: estados `LoadingScope`, `Stale`, `EmptyComplete`, `UnknownFields` (ação "Amostrar schema"), `Disconnected`, `MetadataFailure`, `PipelineFieldsUnknown`; diagnósticos `MissingClose`, `SkippedTokens`, `UnterminatedString`, `DottedKeyRecovery`, `UnexpectedComma`, `MissingComma`. O runner pode mapear nomes, mas não fundir estados distintos | `members-db-collections-*`, `doc06-disconnected`, `filter-root-without-schema`, `agg-fields-unknown-transform`, `incomplete-*` | Completion + Context |
| D-22 | traditional-autocomplete diz "Conexão desconectada: somente linguagem + cache existente", mas `IMetadataCache.Disconnect` remove tudo do perfil | Nada da conexão desconectada no cache | `doc06-disconnected` | Knowledge |
| D-23 | "Campos desconhecidos (sem schema)" × escopo ainda não carregado | Coleção sem `// schema:` = evidência conhecida e vazia (estado `UnknownFields`, não `Loading`) | `filter-root-without-schema` | Completion |
| D-24 | Posição do cursor ao aceitar dentro de string aberta que recebe a aspa de fechamento | Cursor depois da aspa inserida | `doc06-filter-sta`, `doc06-age-gt-operator`, `ranges-string-open-without-closing` | Completion |
| D-25 | Estilo dominante sem nenhuma chave no objeto/statement; caminho com ponto quando o estilo dominante é aspas simples (`'Cliente.Id'` × `"Cliente.Id"`, AC-09) | Nenhuma fixture afirma esses casos | — | Completion |
| D-26 | context-engine põe `$search` como Unknown para campos seguintes, embora o MongoDB preserve os documentos | Especificação (conservador) | `search-fields-after-search-unknown` | Context |

## O que falta cobrir

- **Runner**: parser do formato, cópia dos arquivos para a saída, validação de contexto, ranking, edições e expansão de snippets. A integração completa com metadata persistido, namespaces de coleção, subpipelines complexos e relatório MRR/top-K permanece pendente.
- **Operadores e stages sem fixture**: `$unionWith` (alvo `coll` e sub-pipeline), `$graphLookup`, `$replaceWith`, `$setWindowFields`, `$bucket`, `$merge`/`$out` com marca `Write`, e literais de `enum` em valor (`Pedidos.status`).
- **Fontes sem fixture**: `EnvironmentKey` (`ENV.get("|")`) e `LocalVariable` (sugestão de declarações locais).
- **Detalhe e snippets**: detalhe de tipos polimórficos ("+1 tipo" para `age`); snippet tipado `filter.condition` com `UUID("…")` vindo de `IdentifierRepresentationService`; marca `Write` em snippets de escrita; placeholder de escolha `${1|a,b|}`.
- **Catálogo**: filtro por versão do servidor (`since`/`until`), penalidade de depreciado (`count`, `$where`), truncamento acima de 200 candidatos e reconsulta ao estreitar/alargar prefixo.
- **Comportamento sequencial, fora do formato de fixture de ponto único**: estreitamento com lista aberta sem reparse; `IsIncomplete` + reconsulta ao `Changed` com a mesma versão; Request A/B com provider que ignora cancelamento; digitação sem chamada remota; efeito do `CompletionUsageTracker` e do sinal aceito-e-desfeito; propriedade e diferencial do parser com sementes.
- **Sintaxe JavaScript**: ASI, template com `${}` e mais casos de regex × divisão.
- **Estilo dominante** sem chaves e aspas simples com caminho com ponto (D-25).
- **Desempenho**: orçamentos de 64 KiB e 1 MiB são benchmark; as fixtures grandes só verificam o contexto correto.
- **Fora deste corpus**: IA explícita/preemptiva é exercitada por testes Application/Desktop próprios; Linux/ARM, modelos reais e performance permanecem fora da meta atual.
