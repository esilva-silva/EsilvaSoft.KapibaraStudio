# Decisões propostas

Estado: **Plano revisado** em 15/09/2026. AC-03/04/05/06/11/14 têm base implementada na Fase 1, com aceite parcial e divergências registradas ([estado e desvios](phases/phase-1-data-traditional.md#estado-da-implementação)). AC-08 (política de atalhos, lote W0) tem base implementada e validada em 18/09/2026, com o detalhe de estado registrado na própria decisão abaixo. Ao serem aceitas, promover para [10 — ADRs](../10-decisoes-arquiteturais.md) com numeração oficial e revisão explícita das ADRs afetadas; essa promoção ainda não foi feita.

## AC-01 — Contratos in-process inspirados em LSP

**Contexto.** A IDE é um processo .NET único; LSP define conceitos maduros de completion e inline completion.
**Decisão.** Contratos internos espelham LSP (gatilho, lista incompleta, faixa insert/replace, resolve tardio, inline separado), sem servidor LSP.
**Alternativas.** Servidor LSP em processo separado (rejeitado: serialização, sincronização de documento e ciclo de vida sem benefício atual).
**Consequências.** Exposição futura via LSP continua possível por adaptador.

## AC-02 — Parser tolerante próprio sobre lexer compartilhado

**Contexto.** O cursor quase sempre está em código incompleto; Acornima lança erro nesse caso; tree-sitter exige binários nativos por RID.
**Decisão.** Extrair o lexer do highlighting e construir parser tolerante para o subconjunto necessário, com reparse por statement.
**Alternativas.** Acornima (sem tolerância), tree-sitter (portabilidade/publicação ARM64), regex por caso (rejeitada pela meta e pelo P-01).
**Consequências.** Manutenção de gramática própria limitada; testes de propriedade e diferencial obrigatórios; Acornima continua na validação/formatação.

## AC-03 — Linguagem MongoDB como dados versionados

**Contexto.** Vocabulário duplicado em sete lugares; regras de contexto tenderiam a `if`/`switch`.
**Decisão.** Arquivo embutido com símbolos, assinaturas, shapes e snippets; fonte única para autocomplete e projeção do highlighting; teste de contrato com o Console.
**Alternativas.** Listas C# por serviço (atual); gerar a partir da documentação oficial em tempo de execução (rejeitado: rede e instabilidade).
**Consequências.** Adicionar comando é editar dados + caso de teste; revisão do arquivo a cada versão de servidor suportada.

## AC-04 — Sem novo projeto de domínio

**Contexto.** `AGENTS.md` e [05](../05-arquitetura.md) desencorajam projetos paralelos até haver contrato estável.
**Decisão.** Namespaces `Application.Language.*` sem pacotes, protegidos por teste de arquitetura. Benchmarks em projeto de ferramenta separado.
**Alternativas.** Projeto `EsilvaSoft.SlopStudio.Language` imediato (adiado até estabilizar a Fase 2).
**Consequências.** Extração futura mecânica se necessária.

## AC-05 — Política de acesso remoto do autocomplete

**Contexto.** [21](../21-autocomplete-local.md) afirma que o autocomplete nunca executa consultas; o Explorer nunca consulta automaticamente ao abrir coleção; campos úteis exigem schema.
**Decisão.**
- Comandos de **metadados** (`listDatabases`/`listCollections` nameOnly, `listCollections` com options por banco, `listIndexes`) podem ser carregados sob demanda **somente para o perfil já conectado da aba**, deduplicados, com TTL, prioridade baixa e cancelamento.
- **Amostragem de documentos** só por ação explícita ("Amostrar schema") ou opt-in por conexão, com pipeline que devolve apenas nomes e tipos.
- Valores de documentos nunca são armazenados no catálogo nem enviados ao modelo.
**Alternativas.** Proibir qualquer chamada (catálogo ficaria limitado aos nós expandidos); amostrar automaticamente (viola invariantes).
**Consequências.** Revisa a redação de [21](../21-autocomplete-local.md) para distinguir metadados de consultas a dados.

## AC-06 — Tabelas ordenadas por escopo

**Decisão.** Arrays ordenados com busca binária para prefixo e índice de camel humps; substring limitada ao escopo.
**Alternativas.** Trie/FST (sem ganho para escopos ≤ 10⁴ reconstruídos por inteiro), varredura linear (custo por tecla).
**Consequências.** Validação pelo benchmark da Fase 1; troca por outra estrutura não altera contratos.

## AC-07 — `CompletionWindow` e snippets do AvaloniaEdit

**Decisão.** Substituir `MenuFlyout` por `CompletionWindow` com ranking próprio e snippets nativos (`SnippetInputHandler`).
**Alternativas.** Controle próprio de lista (mais código e acessibilidade a refazer); manter `MenuFlyout` (sem filtro, navegação ou placeholders).
**Consequências.** Estilização e verificação de virtualização no Headless; atualização do design system.

## AC-08 — Atalhos

**Estado: implementado (W0, 18/09/2026).** Build 0 avisos; suíte 1170 aprovados, 0 falhas.

**Decisão revisada.** `Ctrl+.` foi removido dos padrões por decisão explícita do produto; `Ctrl+Espaço` é o único gatilho padrão da lista básica, `Ctrl+;` pede IA (reconhecido, mas sem runtime nesta entrega — nunca insere `;`, nunca abre a lista no lugar). Doze comandos (`EditorCommandIds`) organizados em quatro escopos (`EditorCommandScope { Global, List, Snippet, Inline }`), cada um resolvido isoladamente por `EditorCommandDispatcher.Match`; o mesmo gesto pode ser padrão em escopos diferentes (`Tab`/`Esc` servem lista, snippet e ghost), mas duplicidade dentro do mesmo escopo torna a sessão ilegível. Registro persistido de forma aditiva em `EditorKeyBindings` (`CurrentVersion = 1`, sem migração de dado), sem tela de edição nesta meta. Como os padrões nunca são gravados na sessão, um override anterior de `Ctrl+.` continua legível e funcional, sem ser removido, adicionado ou reescrito. Pontuação casa pelo símbolo produzido pelo layout, com o símbolo físico como alternativa; teclas nomeadas casam por identidade de tecla — corrigido um defeito em que a decisão anterior (`||` entre as duas comparações) deixava `Ctrl` sozinho abrir a lista e uma tecla desconhecida aceitar o ghost, por `null == null` casar com qualquer gesto quando o evento não carregava tecla nem símbolo. Ver [editor-integration.md](editor-integration.md#arbitragem-de-teclado).
**Limitação conhecida, não resolvida.** Sem `Ctrl+.`, `Ctrl+Espaço` é o único disparo padrão do básico e colide com a troca de IME em Windows e Linux. Esta meta não entrega tela de edição de atalhos; o único contorno é um override manual em `EditorKeyBindings`.
**Alternativas.** Manter `Ctrl+.` como principal (rejeitado nesta revisão pelo produto); casar só por `Key` (quebra em layouts como ABNT2).
**Consequências.** Homologação nativa de layouts, plataformas e IME reais permanece pendente (ABNT2/US em Windows; X11/Wayland em Linux); teste Headless não substitui essa homologação.

## AC-09 — Caminhos com ponto sempre entre aspas

**Contexto.** A meta usa `{ Cliente.Id: … }`, que é sintaxe inválida em JavaScript.
**Decisão.** O parser recupera o padrão como caminho de campo e a sugestão insere `"Cliente.Id"`, substituindo o trecho digitado.
**Consequências.** Exemplos da documentação e snippets usam aspas em caminhos.

## AC-10 — Contratos de contexto de IA versionados por modelo

**Contexto.** Pacotes SlopCoder foram treinados com o cabeçalho atual de `AutocompleteContextBuilder`.
**Decisão.** `contextContract` opcional no metadata do modelo; ausência = `editor-context-v1`, congelado com teste byte a byte. Novos formatos só após avaliação.
**Consequências.** Formatos novos para modelos base; modelos ajustados podem exigir novo treino para migrar.

## AC-11 — Métricas locais com `System.Diagnostics.Metrics`

**Decisão.** `Meter`/`ActivitySource` com lista fechada de tags, sem texto nem nomes de objetos do usuário; coleta em memória, sem upload.
**Alternativas.** Estender apenas `Trace` (não agregável); telemetria remota (fora da política de privacidade).

## AC-12 — Dois preemptivos e híbrido sequencial

**Decisão revista.** TraditionalPreemptiveCompletionProvider e AiPreemptiveCompletionProvider independentes, com contexto/catálogo e presenter comuns. Tradicional forte publica e encerra; IA só sem candidato forte, LoadedOnly/Background após debounce. Não trocar ghost visível no padrão; extensão concorrente fica experimental. Movimento sem edição não dispara; typeahead só de candidato concluído. Flags/defaults em configuration.md.

## AC-13 — Ghost text no layout do AvaloniaEdit

**Decisão.** Elemento visual gerado na posição do cursor e objeto inline para linhas seguintes, substituindo a sobreposição que redesenha o sufixo.
**Consequências.** Risco de caret/hit-testing multilinha a validar; alternativa em camada de fundo documentada.

## AC-14 — Orçamentos provisórios e baseline

**Decisão.** Números em [performance.md](performance.md) são provisórios; a Fase 1 mede o código atual e as fases revisam os orçamentos com dados antes do aceite.

## AC-15 — Prefix cache como experimento

**Decisão.** Reuso de KV com `Generator.RewindTo`, desligado por padrão; habilitado por provider somente com teste de equivalência greedy aprovado e ganho de TTFT medido.

## AC-16 — Uso recente em memória

**Decisão.** Estatística de aceite apenas na sessão; persistência futura por opt-in, somente nomes, respeitando opt-outs de histórico.

## AC-17 — Lista tradicional não abre automaticamente por padrão

**Decisão.** Opção `CompletionAutoOpenOnTrigger` (`.`/`$`) desligada até haver métricas de aceite e ruído; evita competir com o ghost.

## AC-18 — `Ctrl+;` como prévia inline

**Decisão.** A IA explícita apresenta uma prévia inline rica com indicador e streaming; se indisponível, abre a lista tradicional com o motivo.
**Alternativas.** Lista de sugestões de IA (modelos pequenos greedy geram um candidato; lista com um item é pior que prévia).

## Divergências em relação à meta

| Meta | Decisão | Justificativa |
| --- | --- | --- |
| `{ Cliente.Id: … }` | Caminho entre aspas | JavaScript inválido (AC-09) |
| Criar `IAutocompleteModel`, `IModelTokenizer`, `IInferenceRuntime`, `IModelContextBuilder` | Manter tipos existentes e mapear nomes | Abstrações equivalentes já existem ([architecture.md](architecture.md#mapeamento-de-nomes-da-meta)) |
| Atalhos configuráveis | Registro mínimo sem tela | Não havia sistema; escopo controlado (AC-08) |
| Formato YAML de contexto | Um de cinco formatos avaliados | Decisão empírica e compatível com modelos treinados (AC-10) |
| Fase 2 inclui parser | Mantido, com extração do lexer | Evita segundo lexer (AC-02) |
| Catálogo consulta metadados | Com restrição de conexão conectada e sem amostragem automática | Invariantes do produto (AC-05) |
| Estrutura de catálogo de exemplo | Símbolos + escopos + shapes + evidências | Representa contexto válido, não só nomes (AC-03) |
| `Enter` aceita conforme editor | Configurável, padrão aceitar | Comportamento do `CompletionWindow` |
| Tradicional só explícito | Revisto: tradicional explícito e preemptivo contextual independentes | AC-12; AC-17 trata apenas popup automático |

## Relação com ADRs existentes

| ADR | Efeito se aceitas |
| --- | --- |
| ADR-007 | Mantida: determinístico local como base; ampliada com catálogo e shapes |
| ADR-023 | Mantida: editor-first; snippets e diagnóstico contextual detalhados |
| ADR-027 / ADR-030 | Revisadas: dicionário e contexto de IA passam a vir do Context Engine e de contratos versionados |
| ADR-031 | Revisada: lexer passa a ser compartilhado com o parser; descrições de TextBox já são históricas |
| ADR-033 / ADR-037 | Mantidas: serviço central e adapters; extensões de runtime e metadata |
| ADR-036 | Mantida: operações de metadados usam a barra com prioridade baixa |


## Decisões adicionais da revisão

### AC-19 — Reconciliar base implementada antes de expandir

Consolidar gerações do cache, identidade de ambiente, Peek, cobertura/truncamento e limites de mescla. Não reimplementar catálogo/benchmarks nem exigir lock-free. Fase 1 permanece aceite parcial até evidência pendente; K11–K17.

### AC-20 — Quatro providers, dois pipelines

ICompletionProvider comum; TraditionalCompletionProvider/TraditionalPreemptiveCompletionProvider usam CompletionService. AiCompletionProvider/AiPreemptiveCompletionProvider usam AiGenerationPipeline. Shared stamp/context/schema/caches/ranking/snippets/metrics/presenter; nenhuma árvore própria por provider. Score IA não comparável ao tradicional.

### AC-21 — LoadedOnly atômico e cache tokenizado correto

Política de carga sob gate com revisão de modelo; automático não carrega/troca nem faz fallback que inicialize sessão. Tokenizer pertence ao runtime; blocos BPE só concatenados com fronteiras provadas. Prefix cache opcional medido, não prerequisite de IA explícita.

### AC-22 — Entrega incremental sem ciclo de UI

Presenter prototipado no 2; 5.1 pode seguir a 2 paralelamente a 3. 5.2 após 4 e coordinator; 5.3 integra. Remover legado só após último chamador/paridade. Adiar histórico de valores, Backspace restaurador, atalhos extras, green tree completa e cinco contratos IA de produção sem medição.

### AC-23 — Preferências independentes com migração explícita

Usar campos v1 existentes para modelo/provider/orçamento; flags aditivas separadas para os dois automáticos. Novo usuário: IA inline false; v1 legado preserva intenção por presença do campo, sujeito a LoadedOnly. Matriz de precedência em configuration.md; não sobrescrever sessão ilegível.

Estas decisões atualizam o plano; não marcam UI ou providers implementados. Relação documental com ADR-007/027/030/031/033/037 registrada em docs/10; evidências futuras exigidas por tarefa.


## AC-24 — Aprendizado contínuo de find e persistência LiteDB

Novo requisito de 15/09/2026 substitui adiamento da persistência de schema. Reutilizar amostra dos resultados já retornados, enfileirar sem bloquear consulta/UI, extrair deltas probabilísticos e persistir estrutura/estatísticas no proprietário LiteDB existente. Identidade segura de origem, transação/idempotência, projeção/cobertura, opt-outs e limites definidos em schema-learning.md. Campos aprendidos são observados, não schema rígido nem prova de ausência. Nenhuma consulta adicional automática. Tarefas L11–L16, dono MongoDB Knowledge, com testes/performance desde o hook.

## Pendências arquiteturais registradas

**Revisão de código de 18/09/2026.**

Estas quatro entradas não são decisões fechadas de escopo novo: são pendências identificadas na revisão de código de K11/K14/K16/L, registradas com seu próprio gatilho de reabertura para não serem perdidas nem confundidas com defeito atual.

### PEND-K11-SECRET

**`ConnectionIdentity` não muda; revisão de segredo é tratada por invalidação.** `ConnectionIdentity` permanece derivada da configuração bruta (`ProfileId` + `TargetHost` + fingerprint da connection string salva). Revisão de ENV/cofre é tratada por **invalidação**, não por identidade: salvar ambientes dispara `InvalidateEnvironment` → `MetadataCache.Disconnect(profileId)`.

**Gatilho de reabertura.** Surgir um chamador real de `IConnectionSecretStore.SetPassword`/`Remove` capaz de trocar credencial **sem** desconectar o perfil; nesse dia o chamador passa a ser obrigado a publicar `new MetadataInvalidation(profileId, MetadataChange.Connection)` no `IMetadataInvalidationBus`, e ainda assim **não** é preciso alterar `ConnectionIdentity`. Vira defeito real somente se alguém servir metadado de um cache não invalidado após troca de credencial.

### PEND-K11-L

**`ConnectionIdentity` não pode virar chave persistida de schema aprendido.** A fase L não pode usá-la para esse fim. L14 define chave própria e estável (`ProfileId` + `Database` + `Collection` + versão de schema), sem URI, credencial, host resolvido ou revisão volátil. A identidade governa apenas escopo em memória.

**Resolvida em 18/09/2026 por [DEC-L-KEY](#dec-l-key), com uma emenda.** A chave é `ProfileId` + `Database` + `Collection`; a "versão de schema" citada acima passa a ser **versão da codificação da chave** (byte `0x01` inicial), e a versão de formato do conteúdo vira campo do documento — versão de formato na identidade produziria órfãos em massa a cada migração, o mesmo defeito que esta pendência existe para evitar. `ConnectionIdentity` continua fora do disco, e `SourceGenerationId` vira coluna não-chave com confiança derivada ([DEC-L-TRUST](#dec-l-trust)).

### PEND-K14-KIND

**`CollectionKind` fica `Unknown` até a definição carregar; perda aceita da API escolhida.** A listagem de coleções devolve `CollectionKind.Unknown` até a definição ser carregada. `MongoDB.Driver 3.11.1` não expõe `nameOnly`/`authorizedCollections` em `ListCollectionsOptions`, e as operações de baixo nível são internas; obter o tipo no mesmo custo exigiria `RunCommandAsync("listCollections", nameOnly: true, authorizedCollections: true)` **com drenagem manual de cursor (`getMore`/`killCursors`)**, sob pena de truncar a listagem. Perda aceita como consequência consciente da API escolhida; `WithKnownKinds` retro-preenche o tipo quando a definição chega.

**Gatilho de reabertura.** Qualquer regra de **comportamento** (não de ícone) passar a depender do tipo antes da definição — por exemplo suprimir sugestões de escrita em view/time series. A hipótese de "a fase L pular views no aprendizado" foi **avaliada e descartada** em 18/09/2026 por [DEC-L-KIND](#dec-l-kind): a fase L não consulta o tipo e não gatilha esta pendência. Implementação então restrita a `MongoMetadataSource.cs`, com teste contra MongoDB real em banco com mais coleções que um lote.

### PEND-K16-QUOTA

**`KnowledgeCatalog.Query` sem cota por tipo/fonte; pré-requisito de L15.** `KnowledgeCatalog.Query` drena as fontes em ordem até `MaximumCandidates`, sem cota por tipo ou por fonte. Sem efeito enquanto as fontes atuais não disputam os mesmos kinds.

**Gatilho.** É **pré-requisito de entrada** de `LearnedSchemaCatalogSource` (L15) — quando duas fontes passarem a produzir `Field`/`Collection` para o mesmo alvo, a ausência de cota deixa uma fonte ocultar a outra, e o truncamento deixa de ser reportável com honestidade. Implementar como **K16-b antes de L15**, não depois (ver [execution-plan.md](execution-plan.md)).

## Correções da revisão W1-K/W2a/W3 (18/09/2026)

### DEC-INLINE-LOADEDONLY

**A política de carga é parâmetro de `GenerateAsync`, não filtro do chamador.** `ILocalAiModelService.GenerateAsync` recebe `AiModelLoadPolicy`. Com `LoadedOnly`, o serviço atende **somente** se o modelo da chave exata (pasta + aceleração + execution provider) já estiver carregado; caso contrário lança `LocalModelUnavailableException` com `UnavailableReason` tipado, sem passar por `EnsureLoadedAsync` — portanto sem carregar, descarregar ou trocar o modelo de outra funcionalidade. O caminho automático (ghost) usa `LoadedOnly`; o pedido explícito continua podendo carregar sob demanda. Filtrar por `LocalModelStatus.State` no chamador é insuficiente: `Status` reflete *algum* modelo pronto, não o desta chave.

### DEC-INLINE-SOURCES

**`CompletionSourcePolicy` transporta a decisão de `InlineCompletionPolicy` até o serviço.** As origens permitidas (dicionário, IA) e a permissão de carga são decididas uma única vez pela política do ghost e viajam como valor por `CompletionSession` → `IAutocompleteService` → `AiAutocompleteProvider`. Nenhum consumidor recombina flags e nenhuma origem desligada reaparece por atalho interno (era o caso do dicionário em `GetImmediateCompletion` com `InlineUseTraditional = false`).

### DEC-INLINE-INCOMPLETE

**`CompletionList.IsIncomplete` cobre as três formas de corte.** Além de `CatalogCompleteness != Complete`, também o esgotamento de `MaximumCandidates` (que uma fonte única não reporta como `Partial`) e o corte por `MaximumItems` com mais candidatos do que os devolvidos. O portão de confiança do ghost depende de "o conjunto inteiro foi visto" para afirmar continuação única; sem os dois últimos sinais ele afirmaria unicidade a partir de uma amostra.

### DEC-INLINE-SNAPSHOT

**O caminho automático recebe o `ITextSnapshot` do editor.** `WorkspaceTabViewModel.GetInlineCompletionAsync` tem sobrecarga por snapshot, alimentada com o `AvaloniaTextSnapshot` do documento. Só assim `TokenCache` reaproveita a lexificação da tecla anterior — um `StringTextSnapshot` novo por tecla nunca casa com a entrada anterior. A sobrecarga por texto mantém contador e identidade de documento **próprios** do automático, separados dos da lista explícita.

## Rodada de arquitetura de 18/09/2026 — identidade do schema aprendido (desbloqueio de L14)

Resolve a incompatibilidade entre a chave lógica de [schema-learning.md](schema-learning.md) (`ProfileId`,
`SourceGenerationId`, `Database`, `Collection`) e [PEND-K11-L](#pend-k11-l), que proíbe revisão volátil em chave
persistida. Rodada de decisão: nenhuma implementação de produto foi escrita.

### DEC-L-KEY

**A chave persistida do schema aprendido é `(ProfileId, Database, Collection)`; `SourceGenerationId` sai da chave.**
Proposta **aprovada com ajustes**. Razão técnica: uma revisão de origem na chave transforma cada troca de credencial,
URI, `TargetHost` ou ambiente em um conjunto de linhas inalcançáveis — não referenciáveis por nenhuma consulta futura,
não contabilizáveis pela cota de bytes, não removíveis por "Limpar aprendizado" e não renomeáveis por DDL. Órfão não é
invalidação: é vazamento durável de estrutura sob uma identidade que ninguém mais sabe formar. Com a chave estável, o
mesmo documento continua endereçável e o vínculo com a origem passa a ser **estado derivado** (DEC-L-TRUST), que é o
único lugar onde uma revisão volátil pode viver sem corromper a identidade.

Três ajustes sobre a proposta recebida:

1. **`_id` é a codificação canônica, não o hash dela.** Hash é irreversível: impede enumerar namespaces de um perfil,
   impede o rename atômico previsto em schema-learning.md e troca uma colisão improvável por corrupção silenciosa
   entre duas coleções distintas. A codificação com prefixo de comprimento — que é a parte certa da proposta — já
   resolve a ambiguidade `("a", "b.c")` × `("a.b", "c")` sem hash algum.
2. **O `_id` é `BsonType.Binary`, não string.** LiteDB compara chaves de índice pela colação do banco, que por padrão
   é cultura corrente **com `IgnoreCase`**. Nomes de coleção do MongoDB são ordinais e sensíveis a maiúsculas:
   `Orders` e `orders` são namespaces diferentes e colidiriam em um `_id` string. Binário é comparado byte a byte por
   construção, imune à colação, e não exige alterar a colação global do arquivo compartilhado.
3. **`SchemaFormatVersion` não entra na identidade.** Versão na chave faz de toda migração um órfão em massa — o
   mesmo defeito que esta decisão existe para eliminar. O que entra na codificação é um byte inicial de **versão da
   codificação da chave** (`0x01`), que só muda se a regra de codificação de nomes mudar. A versão de formato do
   **conteúdo** é campo do documento, com migração aditiva versionada no proprietário já registrado; versão futura ou
   ilegível isola o documento como indisponível, sem sobrescrever com vazio.

Contrato:

```csharp
readonly record struct LearnedSchemaKey(Guid ProfileId, string Database, string Collection);
```

`Database`/`Collection` comparados com `StringComparer.Ordinal`. Codificação canônica do `_id`:

```text
0x01 ‖ ProfileId (16 bytes, ordem de bytes estável e explícita) ‖ len32(utf8(Database)) ‖ utf8(Database)
     ‖ len32(utf8(Collection)) ‖ utf8(Collection)
```

Documento `learnedSchemaNamespaces` — campos permitidos: `_id` (binário acima), `ProfileId` (**indexado**, única
forma durável de varrer/limpar por perfil), `Database`, `Collection`, `SchemaFormatVersion`, `Revision`,
`LastObservedGenerationId` (opaco), `FirstLearnedUtc`, `LastObservedUtc`, `CompleteDocumentObservations`,
`SampledBatches`, `SkippedDocuments`, `IsTruncated`.

**Nunca podem aparecer no documento**, em nenhum campo, nem derivados: URI ou qualquer fragmento dela, credencial,
host resolvido, `TargetHost`, nome amigável do perfil, `ConnectionIdentity` ou seu `Fingerprint` (que é SHA-256 da
connection string salva — persistir isso é persistir hash de credencial em repouso), valores de ENV, `_id` de
documento do MongoDB, hash de documento, valores/literais/filtros/prompts, enum aprendido de resultados,
`CollectionKind` (ver DEC-L-KIND).

### DEC-L-TRUST

**A confiança do snapshot é derivada na hidratação, não uma bandeira persistida.** `IsUnverified` gravado é rejeitado:
é estado derivável que só pode dessincronizar, e exige uma escrita em massa por perfil a cada edição de origem, com
janela de falha parcial. O único dado persistido é `LastObservedGenerationId`. Dois eixos ortogonais, ambos calculados
em memória no momento de servir:

- **Origem** — `LastObservedGenerationId` × `SourceGenerationId` corrente do perfil: `Current` ou `Superseded`.
- **Sessão** — este processo já conectou com sucesso ao perfil sob a geração corrente: `Confirmed` ou `Unconfirmed`.

Três estados servíveis:

| Estado | Situação | Comportamento no autocomplete |
| --- | --- | --- |
| `Current` + `Confirmed` | Conectado, origem a mesma que produziu as observações | Servido como evidência aprendida normal |
| `Current` + `Unconfirmed` | Reinício/offline; origem não contradita, apenas não confirmada | **Servido** como evidência histórica, marcado, sem afirmar cobertura nem conexão ativa |
| `Superseded` | Geração do perfil avançou; as observações descrevem outra origem | **Não servido**. Preservado em disco, nunca apagado por isso |

Isto responde o caso concreto: apontar o mesmo `ProfileId` para outro servidor com os mesmos nomes de banco/coleção é
**edição de origem**, logo nova geração, logo `Superseded` — a estrutura do servidor antigo nunca é oferecida como se
fosse do novo. `Unverified` sozinho seria insuficiente exatamente aí: uma bandeira única não distingue "ninguém
confirmou ainda" de "sabidamente outra origem", e serviria campos inexistentes justamente no início da sessão, quando
a fonte aprendida é a única a responder por `Field`.

Apresentação de `Unconfirmed`: `SymbolTraits.Stale` no símbolo e descrição declarando observação histórica, com a
redação de schema-learning.md ("observações de documentos analisadas"), nunca "total de documentos da coleção".
Nenhum ganho de confiança/ranking sobre evidência viva.

`Superseded` não é apagado nem mesclado. No **primeiro delta comitado** sob a geração nova, o namespace sofre
*rollover* dentro da mesma transação: campos e totais anteriores são descartados e reconstruídos a partir da geração
corrente, `FirstLearnedUtc` preservado só como informação. Somar contagens de duas origens no mesmo denominador é a
corrupção estatística que a chave estável não pode reintroduzir pela porta dos fundos.

Consequência aceita e explícita: **rotacionar a senha de um perfil custa o histórico aprendido dele.** Preservá-lo
exigiria gravar no documento um discriminador de host/URI para distinguir "mesmo servidor, outra credencial" —
precisamente o que DEC-L-KEY proíbe. O custo é limitado (a estrutura se reconstrói pelo uso normal) e a alternativa
troca um dado reconstruível por um vazamento permanente.

### DEC-L-GENERATION

**`SourceGenerationId` é `Guid` durável do perfil, renovado pelo repositório, não pelo chamador.** Hoje o conceito não
existe em nenhum arquivo de `src/`; L não pode referenciá-lo antes de existir. Ele **não** pode ser derivado da
connection string nem de `ConnectionIdentity.Fingerprint`, e **não** pode ser por processo — valor novo a cada
inicialização tornaria tudo `Superseded` em todo lançamento, destruindo a persistência que esta fase existe para
entregar.

Renovação acontece no caminho de gravação do perfil em `LiteDbConnectionProfileRepository.ConnectionProfiles`: ao
persistir um perfil cujo `ConnectionString`, `TargetHost` ou `Environment` difere do documento armazenado, o
repositório atribui um `Guid` novo. Nenhum ViewModel decide isso, nenhum arquivo do Desktop é reservado, e nenhum
chamador pode esquecer. Renomear perfil, favoritar, mudar cor/pasta ou `LastConnectedAt` **não** renovam.

Isto exige alterar `C/ConnectionProfile.cs` e o `ConnectionProfileDocument` com migração aditiva — arquivos
compartilhados, portanto **lote próprio L14-a, antes de L14**. Amarração com [PEND-K11-SECRET](#pend-k11-secret): se
um dia surgir chamador real de `IConnectionSecretStore.SetPassword`/`Remove` capaz de trocar credencial sem
desconectar, esse chamador passa a ser obrigado também a renovar a geração, além de publicar a invalidação. Continua
sem tocar em `ConnectionIdentity`.

### DEC-L-RETENTION

**Descarte automático só por evento de identidade, DDL confirmado, idade ou teto de bytes.**

Apaga automaticamente: perfil removido (cascata por `ProfileId` indexado, na mesma transação da remoção, mais varredura
de inicialização para linhas cujo `ProfileId` não tem perfil — é o único vínculo durável e sem ela a linha é órfã);
`drop` de coleção confirmado (namespace); `drop database` confirmado (descendentes); `LastObservedUtc` acima de 90
dias (elegível a evicção em lotes pequenos, fora de render/tecla, na inicialização ou em ocioso; a partir de 30 dias
ainda é servido, marcado `stale`); teto global de 128 MiB, evictando **namespaces inteiros** por observação mais
antiga — nunca campos avulsos de um namespace, o que deixaria denominadores sem os numeradores. `learnedSchemaBatches`
retém por uma janela estritamente maior que a janela máxima de retry e é podado por `committedAt`.

**Não** apaga: geração superada (DEC-L-TRUST), desconexão, desligamento de coleta ou de persistência, versão de
formato futura/ilegível. Todos esses exigem o comando explícito "Limpar aprendizado" — desligar opção não pode apagar
dado do usuário em silêncio.

`rename` com origem e destino conhecidos é reescrita de `_id` (delete + insert) em uma transação — trivial agora que a
chave não carrega revisão. Se o destino já existir, o destino **prevalece** e a origem é removida: mesclar dois
namespaces somaria observações de coleções diferentes. Origem ou destino desconhecido invalida ambos, como já previsto.

### DEC-L-KIND

**[PEND-K14-KIND](#pend-k14-kind) não é gatilhada por esta fase: o aprendizado não pula views nem time series.**
Aprende de qualquer namespace cuja `ResultOrigin` seja confiável, independentemente de `CollectionKind`. Três razões:

1. O gatilho registrado em PEND-K14-KIND é regra de **comportamento de escrita** (suprimir sugestão de escrita em
   view/time series), não de leitura. Aprender estrutura de um `find` é leitura.
2. A chave é por namespace. Os campos observados em uma view são afirmação correta sobre **aquela view**, que é o
   namespace onde o usuário digita; não são atribuídos à coleção de origem. Suprimi-los removeria autocomplete
   exatamente onde o usuário está trabalhando, sem ganho de correção.
3. Condicionar o aprendizado ao tipo exigiria que o analisador conhecesse o tipo do namespace — que hoje é `Unknown`
   até a definição carregar. Ou o aprendizado passaria a depender de uma definição em cache que pode não existir
   (aprendizado não determinístico, dependente de o usuário ter expandido a árvore antes), ou emitiria consulta
   própria, violando "zero queries adicionais". Nenhuma das duas é aceitável para comprar uma supressão indesejada.

Corolário: `CollectionKind` **não** é persistido no documento aprendido. Tipo é dado vivo do catálogo, muda por DDL
sem observação e não deve existir em duas cópias com prazos de validade diferentes.

### DEC-L-MERGE

**Confirmado: o schema aprendido não entra em `MergedSchema`/`FieldNode` de `MetadataCatalogSource`.** O raciocínio
recebido está certo e o código o torna concreto — `SchemaBuilder.AddSchema` faz `_sampleSize += schema.SampleSize`,
`Copy` faz `child.Occurrences += field.Occurrences` e uniona `Evidence`. Um nó visto por amostra e por aprendizado
passaria a somar numeradores de duas populações sobre o denominador da amostra; `FieldNode.Occurrence` aplica
`Math.Min(1, ...)`, então o erro **não** apareceria como valor absurdo: apareceria como "100%" convincente. Um
denominador errado que se auto-oculta é pior que um ausente.

A fonte aprendida constrói o **próprio** `CollectionSchema`, com `EvidenceSources.Learned` (bit novo, `64`) e
`SampleSize = 0`, de modo que `Occurrence` seja `null` por construção e nenhuma porcentagem aprendida vaze pelo
`Describe` de `MetadataCatalogSource`; a frequência aprendida é apresentada pela própria fonte, com denominador e
redação próprios. `LearnedSchemaCatalogSource` é registrado como `ICatalogSource` independente e disputa a cota por
fonte de [PEND-K16-QUOTA](#pend-k16-quota) / K16-b, já entregue em `KnowledgeCatalog.CollectShared`. L15 deve incluir
um teste que falhe se um `CollectionSchema` com `EvidenceSources.Learned` chegar a `SchemaBuilder.AddSchema`.

### PEND-L15-DEDUP

**Duas fontes de `Field` produzem dois candidatos para o mesmo campo; falta regra de precedência.** Com
`LearnedSchemaCatalogSource` registrado, `MetadataCatalogSource` e ela caem no **mesmo grupo** de `KnowledgeCatalog`
(compartilham `SymbolKinds.Field`) e cada uma recebe metade do orçamento. Os identificadores diferem
(`meta:…/field/x` × `learned:…/field/x`), então nada deduplica: o usuário veria `customerId` duas vezes e metade da
lista poderia ser repetição. A cota de K16-b resolve ocultação entre fontes, não duplicação.

**Gatilho.** É requisito de saída de L15, não defeito atual. A regra deve ser decidida antes de L15 fechar: preferir o
símbolo de evidência viva e dobrar a anotação aprendida dentro da descrição dele, ou unificar por
`(Scope, SymbolKind, Name)` na camada de apresentação/ranking. Decidir onde — catálogo, ranking ou presenter — é
mudança de contrato e volta para Architecture antes dos consumidores.

### DEC-L-DOCLINKS

**Links de `docs/auto-complite/` para `src/` estão defasados após a criação de `Autocomplete.Core`.** Corrigir antes
de L11, para nenhum lote reservar arquivo inexistente. Alvos reais verificados em 18/09/2026:

| Link defasado | Documento | Alvo real |
| --- | --- | --- |
| `Core/StructuredResults.cs` | schema-learning.md | dividido em `Core/StructuredResultSet.cs`, `Core/StructuredResultDocument.cs`, `Core/ResultOrigin.cs` |
| `Application/Language/CollectionSchema.cs` | schema-learning.md, current-state.md | `Autocomplete.Core/CollectionSchema.cs` |
| `Application/Language/MetadataCache.cs` | current-state.md | `Application/MetadataCache.cs` (sem `Language/`) |
| `Application/Language/KnowledgeCatalog.cs` | current-state.md | `Autocomplete.Core/KnowledgeCatalog.cs` |
| `Application/Language/CatalogModel.cs` | current-state.md, execution-plan.md (G00) | não existe; dividido em `Autocomplete.Core/CatalogQuery.cs`, `CatalogResult.cs`, `CatalogCandidate.cs`, `CatalogSymbol.cs`, `CatalogScope.cs`, `CatalogMatch.cs`, `CatalogCompleteness.cs` |
| `Application/Language/MetadataModel.cs` | execution-plan.md (K11, K14) | não existe; dividido em `Autocomplete.Core/MetadataKey.cs`, `MetadataView.cs`, `MetadataScope.cs`, `MetadataAccess.cs`, `MetadataFreshness.cs`, `MetadataChange.cs`, `MetadataInvalidation.cs` |
| `Application/Language/LanguageDefinition.cs` | current-state.md, knowledge-catalog.md | `Autocomplete.Core/LanguageDefinition.cs` |
| `Application/Language/mongodb-language.v1.json` | current-state.md, knowledge-catalog.md | `Autocomplete.Core/mongodb-language.v1.json` |
| `Application/SyntaxHighlighting/SyntaxHighlightingService.cs` | context-engine.md | `Autocomplete.Core/SyntaxHighlighting/SyntaxHighlightingService.cs` |
| `Core/Autocomplete.cs` | current-state.md | não existe; dividido em `Core/AutocompleteSettings.cs`, `Core/AutocompleteMode.cs` |
| `Infrastructure/OnnxLocalModelRuntime.cs`, `ModelAdapters.cs`, `DeepSeekModelTokenizer.cs`, `AiProviderSelector.cs`, `OnnxHardwareProbe.cs` | current-state.md | todos em `Infrastructure.LocalAi/` |

`schema-learning.md`, `current-state.md`, `knowledge-catalog.md` e `context-engine.md` não pertencem a Architecture; a
correção acompanha o lote do dono de cada documento. As linhas de `execution-plan.md` foram corrigidas nesta rodada.

## Fase 3 — contexto para IA (lote A31c, 19/09/2026)

### DEC-A31C-CONTEXTCONTRACT

**Compatibilidade de contrato de prompt é decidida por três casos, e ausência de declaração nunca é incompatibilidade.**
Implementa [AC-10](#ac-10--contratos-de-contexto-de-ia-versionados-por-modelo) na validação estrutural do catálogo.
`slopstudio-model.json` ganha dois campos opcionais e aditivos: `contextContract` (identificador do contrato de prompt
esperado, máximo 64 caracteres, mesma disciplina de `architecture`) e `supportsRepositoryContext` (booleano).

| Caso | Resultado | Razão |
| --- | --- | --- |
| Pacote sem `slopstudio-model.json`, ou com metadata sem `contextContract` | `Valid`; contrato efetivo `editor-context-v1` | O pacote real `SlopCoder-Mongo-0.5B-ONNX-int4` não tem manifesto e a pasta continua sendo a identidade do modelo. Silêncio é "não declarado", não "declarou algo que não entendemos" — rejeitar aqui quebraria todo pacote anterior à existência do campo. |
| `contextContract` reconhecido (hoje só `editor-context-v1`) | `Valid` | Contrato congelado por teste-ouro byte a byte; é o contrato de treino dos pacotes SlopCoder. |
| `contextContract` declarado e desconhecido por esta versão | `Invalid`, com mensagem citando o valor declarado e os contratos suportados | Uma declaração explícita é uma exigência do pacote. Servir o prompt v1 a um modelo treinado em outro formato produziria saída silenciosamente degradada; falhar cedo, com o identificador visível, é o único resultado honesto. |

**Onde.** A lista de contratos implementados vive em `LocalAi.Core/LocalModelContextContracts.cs` (`EditorContextV1`,
`Supported`, `IsSupported`, `Resolve`) e a decisão é aplicada em `Infrastructure.LocalAi/LocalModelCatalog.Validate`,
logo após a leitura do manifesto e antes de qualquer trabalho de tokenizer. A checagem é puramente em memória: **nenhum
peso é baixado, lido ou carregado para avaliar compatibilidade**. Reutiliza `LocalModelState.Invalid` →
`LocalModelValidity.Invalid`, o par já usado para manifesto malformado; nenhum estado novo foi criado. `IModelAdapter`
não recebeu o metadata: o adapter valida arquitetura/tokenizer a partir de `ModelFolder`, enquanto o contrato de prompt
é propriedade do manifesto, e misturar os dois obrigaria a alargar o contrato do adapter sem ganho.

**Comparação de identificador.** Diferença de caixa (`Editor-Context-V1`) é aceita e normalizada, seguindo os mapas de
`capabilities`/`hardware` do mesmo leitor; só um identificador de fato diferente rejeita o pacote.

**`supportsRepositoryContext` com três estados.** `null` (ausente), `true` e `false` são distintos na estrutura, como
as flags inline de `AutocompleteSettings`. Para decisão de comportamento `null` vale `false`, mas a distinção é
preservada para que um pacote futuro possa negar o suporte explicitamente sem ser confundido com um pacote antigo.
O campo ainda não altera comportamento nenhum: quem o consome é o lote dos contratos experimentais (A34b).

**Consequências.** Introduzir um contrato novo exige publicá-lo em `LocalModelContextContracts.Supported` no mesmo
release em que os pacotes passam a declará-lo; um pacote que declare o contrato antes do suporte aparece ao usuário
como `Invalid` com a causa escrita, e não como um modelo que gera mal.

## Fase 1 — Schema Learning (lote L15, 19/09/2026)

### DEC-L15-DEDUP

**Resolve [PEND-L15-DEDUP](#pend-l15-dedup): opção 1 — preferir o símbolo de evidência viva, dobrar a anotação
aprendida na descrição dele — implementada inteiramente dentro de `LearnedSchemaCatalogSource`, sem tocar
`KnowledgeCatalog`, `CompletionRanker` ou `MetadataCatalogSource`.** A unificação por `(Scope, SymbolKind, Name)`
na camada de apresentação/ranking (a outra opção do pend) exigiria mudar um contrato compartilhado e consumido por
lotes ainda não fechados (T02/T03), o que a própria pendência já dizia exigir revisão de Architecture antes dos
consumidores; a opção 1 não muda nenhum contrato existente e cabe inteiramente no arquivo novo deste lote.

**Mecânica.** Antes de acrescentar um candidato aprendido ao `sink` compartilhado, `LearnedSchemaCatalogSource`
varre os candidatos já ali presentes por um símbolo de `SymbolKind.Field` com o mesmo `CatalogScope` (conexão,
banco, coleção) e o mesmo `Name` ordinal. Se encontrar — hoje, só `MetadataCatalogSource` pode ter posto um ali —
substitui-o por uma cópia com `Detail` concatenado (`"{detalhe vivo} · {anotação aprendida}"`), `Evidence` com o
bit `Learned` acrescentado por OR e `Flags` (inclui `SymbolTraits.Stale` quando `Current+Unconfirmed`) também por
OR; nunca cria uma segunda entrada para o mesmo campo. Quando nenhum símbolo vivo existe ainda para aquele nome —
o caso comum de aprendido sem conexão ativa, ou campo nunca visto pela evidência viva — o candidato aprendido
autônomo é adicionado normalmente, como qualquer outra fonte.

**Pré-condição operacional.** `LearnedSchemaCatalogSource` precisa rodar depois de `MetadataCatalogSource` na
ordem de fontes passada a `KnowledgeCatalog` para que o símbolo vivo já esteja no `sink` quando a fonte aprendida
o varre; isso é responsabilidade da composição. **Atualização (lote de ativação de DI, 19/09/2026): conectado.**
`ServiceCollectionExtensions.AddSlopStudioInfrastructure` registra `LanguageCatalogSource`, depois
`MetadataCatalogSource`, e só então `LearnedSchemaCatalogSource` (comentário no próprio registro amarra a ordem a
esta decisão); `KnowledgeCatalog` consome `IEnumerable<ICatalogSource>` na ordem de registro. Se a ordem inverter,
o pior caso é a duplicata voltar a aparecer — uma regressão visível e coberta por teste dedicado
(`DedupPrefersLiveEvidenceAndFoldsLearnedAnnotationIntoIt`) — nunca perda de dado, já que nenhum campo deixa de
ser servido nesse cenário.

**Consequências.** A cota por fonte de K16-b continua vendo dois candidatos até o momento da dobra: a dobra
acontece por candidato já presente no `sink`, não por fonte inteira, então o truncamento reportado pela cota
continua honesto (nenhuma fonte é escondida por esta regra). Uma fonte aprendida futura para outro `SymbolKind`
(por exemplo `Index`) precisaria da mesma regra adaptada ou de uma extração para um helper compartilhado, se um
terceiro produtor do mesmo `SymbolKind.Field` aparecer.

### DEC-L15-OPTOUT-WIRING

**Resolve as duas pendências reais apontadas pelo lote de integração:** `ILearnedSchemaOptOut.ApplyPreferences`/
`SetServingExcluded` não eram chamados por ninguém, e o LRU de `LearnedSchemaCatalogSource` não esquecia um perfil
excluído dentro da mesma instância.

**Opt-out por conexão ligado em `WorkspaceViewModel`, replicando o precedente de `SchemaSamplingProfileIds`.**
`InitializeAsync` chama `_learnedSchemaOptOut?.ApplyPreferences(session.Preferences)` no mesmo ponto em que aplica
`SchemaSamplingProfileIds`; `SaveSessionAsync` grava `LearnedSchemaExcludedProfileIds = _learnedSchemaOptOut?.ExcludedProfiles.ToArray() ?? []`
no mesmo objeto `WorkspacePreferences`, espelhando `SchemaSamplingProfileIds = Metadata.SchemaSamplingProfiles.ToArray()`.
Um novo método público `SetLearnedSchemaExcludedAsync(Guid, bool)` (`WorkspaceViewModel.Explorer.cs`) chama
`SetServingExcluded` e persiste, no mesmo formato de `SetSchemaSamplingAllowedAsync`. `ILearnedSchemaOptOut` chega ao
`WorkspaceViewModel` por injeção opcional (`ILearnedSchemaOptOut? learnedSchemaOptOut = null`); a composição real
(`App.axaml.cs`) resolve o singleton já registrado por `AddSlopStudioInfrastructure` automaticamente. **Não existe
ainda nenhuma superfície de UI para alternar essa exclusão por conexão** — nem no comportamento do "novo lote", nem
antes dele; apenas o dado é lido/gravado corretamente e `SetLearnedSchemaExcludedAsync` fica pronto para uma tela
futura (ou para um comando de teste). Isso é aceitável porque o interruptor geral
(`AutocompleteSettings.LearnedSchemaEnabled`) já cobre o caso comum e tem tela própria; a exclusão por conexão é
trabalho futuro explícito de UI, não desta ativação de fiação.

**Invalidação do LRU em exclusão de perfil: método público `InvalidateProfile(Guid)`, não um evento.**
`IConnectionProfileRepository` não publica nenhum evento de exclusão (só `GetAllAsync`/`SaveAsync`/`DeleteAsync`);
inventar um agora exigiria um contrato novo consumido por uma única fonte de catálogo, quando `WorkspaceService`
já é o único ponto de passagem de toda exclusão de perfil do produto (`DeleteProfileAsync`, chamado hoje pelo fluxo
de perfis do desktop). Por isso `LearnedSchemaCatalogSource.InvalidateProfile(Guid profileId)` — que remove sob o
mesmo `_gate` toda entrada do LRU cujo `Key.ProfileId` combine — é chamado diretamente de
`WorkspaceService.DeleteProfileAsync`, depois de `profiles.DeleteAsync` ter sucesso, por um parâmetro opcional
`LearnedSchemaCatalogSource? learnedSchemaCatalog = null` do construtor. `ServiceCollectionExtensions` passou a
registrar `LearnedSchemaCatalogSource` como singleton concreto (antes só existia como `ICatalogSource` via fábrica),
para que o mesmo singleton sirva tanto o catálogo quanto este gancho de invalidação — nenhuma segunda instância, sem
segunda fonte de verdade (LiteDB já refletia a exclusão; isto só esvazia o cache em memória). Uma hidratação em voo
para o slot removido não corrompe nada: `HydrateAsync` já descarta por referência qualquer resultado cujo slot não
esteja mais em `_cache` (a mesma regra "delta tardio não grava" de uma eviction normal do LRU).

## Fechamento da meta — Fase L + Fase 3 (19/09/2026)

Esta seção não abre decisão nova nem reabre nenhuma `DEC-*` já fechada: ela **fecha o ciclo de revisão** da meta,
registrando (a) o destino de cada regra que os lotes anteriores deixaram apenas como convenção em relatório e
(b) as pendências que a meta decidiu conscientemente **não** resolver, reunidas num só lugar para que nenhuma
dependa de ser lembrada por um relatório antigo.

### Regras travadas por teste

Nove regras arquiteturais estavam vivas só como texto de relatório — o pior estado possível para um invariante,
porque um invariante que só existe em prosa é indistinguível de um que já foi quebrado. O arquivo de destino é
`tests/EsilvaSoft.SlopStudio.UnitTests/AutocompleteArchitectureTests.cs`, que já era o guarda de fronteira do
núcleo determinístico.

| # | Regra | Destino | Resultado |
| --- | --- | --- | --- |
| 1 | Núcleo de linguagem nunca nomeia o runtime de IA | `LanguageLayerNeverReferencesTheAiRuntime` (estendido) | Travada. A lista de tipos proibidos passou a incluir `ITokenCounter`, `TokenizedBlockCache`, `ITokenBoundaryOracle`, `IAiContextContract`, `AiPromptRequest` e `AiPromptResult`. **Não existe tipo chamado `AiPrompt`**: o que o relatório do lote chamava assim são `AiPromptRequest`/`AiPromptResult`, de `Application/AiContext/AiContextPipeline.cs`. |
| 2 | `LocalAi.Core` nunca referencia semântica MongoDB | `LocalAiCoreNeverReferencesMongoSemantics` (novo) | Travada, em três frentes: referência de assembly, membro declarado que nomeie tipo de `Autocomplete.Core`, e varredura textual do vocabulário de domínio (`CompletionContext`, `MongoSyntaxTree`, `AiFact`, `CollectionSchema`, `AutocompleteRequest`, `LanguageDefinition`, `SymbolKinds`, `EditorDialects`, `MongoDB`) dentro dos fontes do projeto — a varredura textual existe porque redeclarar o conceito localmente é o jeito de trazer a semântica para dentro sem precisar de referência. |
| 3 | Contrato de contexto só é composto em `Application` | `AiContextContractIsComposedOnlyInApplication` (novo) | Travada. Os sete tipos (interface, contrato v1, resolver e os quatro experimentais) são declarados em `Application`; nenhum tipo de `Autocomplete.Core`, `LocalAi.Core` ou `Infrastructure.LocalAi` nomeia algo do namespace; e os dois núcleos não referenciam sequer o assembly `Application`. **`Infrastructure.LocalAi` está acima de `Application` na cadeia e pode referenciar o assembly** — o que ele não pode, e é o que o teste afirma, é nomear um contrato. |
| 4 | Só `editor-context-v1` registrado em produção | `OnlyEditorContextV1IsRegisteredInProduction` (novo) | Travada a partir da raiz de composição real (`AddSlopStudioInfrastructure` + `AddSlopStudioLocalAiInfrastructure`, as mesmas duas chamadas de `App.axaml.cs`). Os descritores são inspecionados **antes** de construir o provedor: a pergunta é sobre registro, não sobre resolução, e assim o teste não precisa abrir o LiteDB do workspace. Complementada pela verificação positiva de que `AiContextContractResolver.Implemented` e `LocalModelContextContracts.Supported` contêm exatamente um item. |
| 5 | Contratos experimentais inalcançáveis por configuração | — | **Já coberta, sem duplicata.** `AiContext/Experimental/ExperimentalContextReachabilityTests.cs` cobre os quatro `ContractId` em `DeclaringTheContractInAPackageIsRefused` (`Resolve` lança, `TryResolve` devolve falso, `IsSupported` falso), `ContractIsNeitherSupportedNorImplemented`, `NoServiceCollectionExtensionMentionsTheExperimentalFormats` e `NoProductionTypeReferencesTheExperimentalContracts`. Nada faltava; repetir aqui só criaria duas verdades para manter. |
| 6 | Cobertura de todo subespaço do núcleo | `LanguageCoreCoversEverySubNamespace` (estendido) | Travada. O teste é um *guarda do próprio guarda*: ele exige que os subespaços existam, para que o filtro por prefixo dos testes de isolamento nunca varra o vazio e passe por omissão. Passou a exigir `.Facts` ao lado de `.Context`/`.Completion`/`.Syntax`/`.Text`. |
| 7 | Seleção de fatos síncrona e offline | — | **Já coberta, sem duplicata.** `Facts/AiFactSelectionTests.EverythingUnderFactsIsSynchronousAndOffline` varre **o namespace `Autocomplete.Core.Facts` inteiro** por reflexão, não uma lista de tipos: `EditorWindowBuilder` e `SimilarStatementFinder` (lote A32b) já estão dentro do alcance dela, e `SimilarStatementFinderTests.TheEditorWindowAndTheSimilarityFinderAreSynchronousAndOffline` ainda os nomeia explicitamente. Não há lacuna. |
| 8 | Chave de schema aprendido sem segredo de conexão | `LearnedSchemaKeyCarriesNoConnectionSecret` (novo) | Travada, materializando [PEND-K11-L](#pend-k11-l) como rede de segurança de namespace inteiro (os testes de L11 e `LearnedSchemaTrustTests` cobriam tipo a tipo). Nenhum campo ou propriedade de `Application/SchemaLearning/**` pode ter nome contendo `ConnectionString`, `Uri`, `Url`, `Credential`, `Password`, `Secret`, `Fingerprint`, `ConnectionIdentity`, `ResolvedHost`, `TargetHost`, `Endpoint`, `ProfileName`, `DisplayName`, `FriendlyName`, `UserName`, `Login`, `AuthMechanism`, `Certificate` ou `Vault`, nem tipo `ConnectionProfile`/`MongoUrl`/`MongoClientSettings`/`NetworkCredential`/`Uri`/`X509Certificate2`. Inclui a afirmação positiva de [DEC-L-KEY](#dec-l-key): a chave continua sendo exatamente `ProfileId` + `Database` + `Collection`. |
| 9 | Uma única `LiteDatabase` em todo o produto | `OnlyOneLiteDatabaseIsEverConstructed` (novo) | Travada. Varredura textual de `src/**/*.cs` (sem `obj`/`bin`) exigindo **exatamente uma** ocorrência de `new LiteDatabase(`. Hoje ela está em `LiteDbConnectionProfileRepository.cs:38`. A varredura é textual de propósito: o dono da instância é um detalhe de um construtor e não aparece como membro em metadados de tipo, e a violação que importa é uma construção nova em qualquer arquivo. É o invariante mais caro de quebrar do repositório — um segundo `LiteDatabase` em modo `Direct` sobre o mesmo arquivo falha em execução, no computador do usuário, com a sessão dele dentro. |

Resultado: **11 testes em `AutocompleteArchitectureTests`, todos aprovados, build com 0 avisos**. Nenhuma das nove
regras revelou violação no código de produção — todas descreviam o estado que o código já tinha, que é exatamente
a condição para travá-las sem alterar comportamento.

### Pendências conscientemente em aberto ao fim da meta

Nenhuma destas é defeito não visto: todas foram decididas, registradas no lote de origem e são repetidas aqui só
para ficarem num lugar só. Cada uma traz o que a destrava.

1. **Terminadores do prompt de produção v1 — contrato determinístico.** `AutocompleteContextBuilder.Build` escreve
   o cabeçalho com CRLF fixo, enquanto `ModelPrefix` mantém os delimitadores literais `"\n"`. O CRLF preserva os bytes
   dos pacotes SlopCoder treinados no Windows e evita que o mesmo modelo receba prompts diferentes conforme o sistema
   operacional. `Ai/EditorContextV1GoldenTests` congela essa composição com `text` + `eol` (H = CRLF do contrato,
   L = `"\n"`) e dois SHA-256 (materialização all-LF e all-CRLF), agora verificados de forma idêntica em Windows e
   Linux. Os quatro formatos experimentais de A34b já nascem somente com `"\n"`.
2. **Critério de aceite 7 da Fase 3 (formato alternativo vencendo em modelo base) — não atingível nesta meta.**
   Só existem pacotes SlopCoder treinados no contrato v1; medir qualquer formato novo contra eles enviesa a
   comparação a favor do v1 por construção, e o resultado não significaria nada. Os quatro formatos foram
   congelados com golden byte a byte e deixados **inalcançáveis por qualquer rota de produção** (regra 5 acima),
   reduzindo a avaliação de A34c a rodar a comparação. *Destrava:* disponibilidade de um Qwen2.5-Coder **base**
   (não fine-tuned no v1) para servir de árbitro neutro.
3. **UI de opt-out de schema aprendido por conexão — ainda ausente.** Confirmado após o lote de fechamento de
   pendências de UI/LRU: [DEC-L15-OPTOUT-WIRING](#dec-l15-optout-wiring) ligou a fiação completa
   (`ApplyPreferences`, `ExcludedProfiles`, `SetServingExcluded`, persistência em
   `WorkspacePreferences.LearnedSchemaExcludedProfileIds` e `WorkspaceViewModel.SetLearnedSchemaExcludedAsync`),
   mas **nenhuma view expõe o interruptor**: não há binding para ele em `Desktop/Views`. Aceitável porque o
   interruptor geral (`AutocompleteSettings.LearnedSchemaEnabled`) já cobre o caso comum e tem tela própria; a
   exclusão por conexão fica como trabalho explícito de UI, com o dado já sendo lido e gravado corretamente.
   *Destrava:* um lote de UI/UX que decida a superfície (menu de contexto do explorer por perfil, ou linha na tela
   de preferências) — a camada de ViewModel já está pronta e testada.
4. **Orçamento de 64 KB de alocação por tecla — estourado em dois componentes, medido e não corrigido.** O benchmark
   da Fase L mede `LearnedSchemaCatalogSource.Collect` com LRU quente em **52,82 KB / 98,99 KB / 98,99 KB** para
   50 / 500 / 5 000 campos aprendidos: passa dos 64 KB a partir de ~500 campos. É a mesma classe de defeito já
   registrada para `MetadataCatalogSource.Describe(FieldNode)`, que aloca **174,72 KB** por consulta a partir de
   ~1 000 campos — em ambos os casos o texto de apresentação é montado **por candidato**, inclusive para os que o
   ranqueamento vai descartar. A correção estrutural (diferir `Detail` para os sobreviventes do top-K, como já foi
   feito nos realces do `CompletionRanker`) é conhecida e não foi aplicada em nenhum dos dois. Não é regressão da
   meta: o número já existia e a meta o mediu em vez de o presumir. *Destrava:* um lote de desempenho que aplique o
   adiamento do `Detail` nas duas fontes e estenda a guarda permanente de `NameTableAllocationTests` a elas.

## Fase 4 — IA explícita (lote R41, 19/09/2026)

### DEC-R41-REASONS

**A recusa da IA local é um valor tipado; a mensagem deriva do motivo, nunca o contrário.**
`LocalModelUnavailableReason` deixa de ter três valores e passa a cobrir, de forma **aditiva** (nenhum valor existente
foi removido, renomeado ou reordenado), toda a [matriz de fallback](ai-autocomplete.md#fallback) que a IA explícita
precisa distinguir para escolher a mensagem da lista tradicional:

| Motivo | Quando | Efeito colateral |
| --- | --- | --- |
| `NoModelConfigured` | Nenhum modelo selecionado (chave com pasta vazia) | Nenhum |
| `ModelInvalid` | Reprovação estrutural do catálogo: arquivos ausentes, arquitetura não suportada, tokenizer incompatível e **contrato de contexto declarado e desconhecido** ([DEC-A31C-CONTEXTCONTRACT](#dec-a31c-contextcontract)) | Abre janela de recusa |
| `CapabilityMissing` | O modelo carregou mas não declara a capacidade do papel pedido | Nenhum — não é falha |
| `ProviderUnavailable` | Hardware/execution provider exigido não executa o modelo (`AiProviderUnavailableException`, em todo construtor) | Abre janela de recusa |
| `Cooldown` | Janela de recusa aberta por uma falha **transitória** desta chave | — |
| `NotLoaded` / `DifferentConfiguration` | `LoadedOnly` sem a chave exata carregada ([DEC-INLINE-LOADEDONLY](#dec-inline-loadedonly)); `DifferentConfiguration` é o refinamento "há outro modelo carregado" | Nenhum |
| `ContextOverflow` | O pedido não cabe na janela do modelo (`LocalModelContextException`) | Nenhum — o modelo continua carregado e servindo pedidos menores |
| `RuntimeFailure` | Erro nativo de inicialização ou inferência, já convertido em mensagem segura | Descarrega e abre janela de recusa |

**Contrato de contexto desconhecido bloqueia geração e carga, não só a listagem.** A decisão A31c era aplicada em
`LocalModelCatalog.Validate`, e `LocalAiModelService` já validava por ali antes de carregar — mas devolvia o motivo
como não classificado. Agora `GenerateAsync` e `LoadModelAsync` recusam com `ModelInvalid` e a mensagem do catálogo
(com o identificador declarado), sem inicializar runtime nenhum: **nenhum peso é lido para descobrir que o contrato
não serve**.

**Janela de contexto.** `ContextOverflow` é motivo próprio e nunca se confunde com `ModelInvalid`: o pacote está
correto e o pedido é que não cabe. A autoridade sobre o tamanho real da janela continua sendo o runtime
(`context_length` do `genai_config.json`, verificado em `OnnxLocalModelRuntime`), e **não** `RecommendedContextTokens`
do manifesto — esse campo é um *padrão de preferência* consumido pela tela de configuração, e recusar contra uma
recomendação rejeitaria pedidos que o modelo atende. Por isso o serviço não replica a aritmética da janela: traduz a
exceção do runtime em motivo tipado e garante que essa tradução não descarrega o modelo nem abre janela de recusa.

**Versão de pacote de modelo ("pacote antigo") não tem equivalente a `SchemaFormatVersion`.** Não existe, e não foi
criado aqui, um número de formato para o pacote de modelo: `slopstudio-model.json` é inteiramente opcional, campos
desconhecidos são ignorados por compatibilidade progressiva e `metadata.version` é texto livre do publicador. O
mecanismo de compatibilidade dos pacotes é o `contextContract` de A31c, que já distingue "não declarou" (válido, v1)
de "declarou algo que esta versão não implementa" (`ModelInvalid`). Um pacote antigo, portanto, continua válido por
construção, e um pacote *novo demais* é rejeitado pelo contrato — não por um número de versão paralelo.

### DEC-R41-COOLDOWN

**A janela de recusa é por chave de modelo, de 30 s, aberta por uma única falha, com relógio injetado; e a janela é
o mecanismo, não o motivo.** O mecanismo já existia em `LocalAiModelService` (`_failedKey`/`_retryAfter`) e não foi
substituído — foi revisado, parametrizado por escrito e tornado observável. Parâmetros, agora explícitos:

| Parâmetro | Valor | Razão |
| --- | --- | --- |
| N (falhas para abrir a janela) | **1** | O serviço já descarrega o modelo na primeira falha de geração; um contador ≥ 2 exigiria recarregar o modelo só para falhar de novo, pagando segundos de carga por tentativa. A falha de IA local é quase sempre determinística (provider ausente, memória insuficiente, exportação incompatível), não intermitente como uma rede. |
| Janela de contagem | **não se aplica** | Com N = 1 não há contagem; o "reset" é qualquer sucesso de carga, `UnloadModelAsync`, `SwitchModelAsync` ou `TestModelAsync`, que chamam `ClearRetry`. Salvar preferências ou testar o modelo é a ação explícita do usuário dizendo "tente de novo agora" e zera a espera imediatamente. |
| Duração | **30 s** (`RetryDelay`) | Valor já praticado e documentado em [onnx-strategy.md](onnx-strategy.md); curto o bastante para não parecer travado e longo o bastante para não reentrar em carga a cada tecla. |
| Escopo | **chave** = pasta + aceleração + execution provider | A mesma identidade de `LoadedOnly`. Trocar de acelerador ou de modelo é uma configuração diferente e não herda a punição da anterior. |
| Relógio | `TimeProvider` injetado | Nunca `DateTime.Now`: a expiração é avançada pelo teste, sem espera real nem tolerância de tempo de parede. |

**A causa durável sobrevive à janela.** Guardar só "falhou às 12h00" faz o segundo pedido responder "aguarde 30 s" a
quem acabou de escolher um pacote inválido ou um provider que esta máquina não tem — problemas que não se resolvem
sozinhos em 30 s. O serviço passa a guardar também a causa (`_failedCause`) e, durante a janela, **repete a causa
durável** (`ModelInvalid`, `ProviderUnavailable`, `NoModelConfigured`) com `RetryAfter` preenchido; só uma falha
transitória de runtime aparece como `Cooldown`. Assim a linha "Cooldown após falha → lista + tempo restante" da matriz
de fallback recebe o tempo restante em todos os casos, sem perder a razão real em nenhum.

**A janela também vale sob `LoadedOnly`.** Sem isso, o caminho automático relataria "nenhum modelo carregado" logo
depois de uma falha — verdade literal e explicação errada. `RequireLoaded` verifica, nesta ordem: chave exata
carregada → sem seleção (`NoModelConfigured`) → janela ativa (causa registrada) → outro modelo carregado
(`DifferentConfiguration`) → nada carregado (`NotLoaded`). A ordem não altera o invariante de DEC-INLINE-LOADEDONLY:
em nenhum desses ramos há carga, descarga ou troca de modelo.

## Fase 4 — IA explícita (lote R42, 19/09/2026)

### DEC-R42-INCREMENTAL

**O texto da geração é montado token a token; a sequência inteira nunca é redecodificada.** O runtime decodificava,
a cada token gerado, toda a saída desde o início da geração — uma vez para procurar o sufixo de parada e outra no
fim — o que custa O(n²) em uma geração de n tokens e é exatamente o que o critério de aceite 6 da fase proíbe.
`ITokenizer` ganha, de forma **aditiva**, `CreateIncrementalDecoder()` (`IIncrementalDecoder`: `Append(int)` devolve
só o texto novo, `Flush()` encerra), com implementação padrão que redecodifica tudo — nenhum tokenizador existente
quebra, e quem não tem streaming apenas não ganha o benefício.

| Tokenizador | Decodificação incremental | Custo por token |
| --- | --- | --- |
| `OnnxModelTokenizer` (GenAI) | `Tokenizer.CreateStream()` → `TokenizerStream.Decode(id)`, que existe no pacote 0.15.2 e mantém no nativo os bytes de um caractere ainda aberto | constante |
| `DeepSeekModelTokenizer` (BPE em .NET) | peças do próprio vocabulário em bytes, retidas por `Utf8IncrementalBuffer` até fechar o caractere | constante |
| Qualquer outro `ITokenizer` | `BatchFallbackIncrementalDecoder` (padrão da interface) | linear no acumulado |

**Unicode fragmentado é problema de bytes, não de `char`.** Um acento, um CJK ou um emoji nasce partido entre tokens
de um BPE byte-level; decodificar o token sozinho produziria `U+FFFD` na prévia. `Utf8IncrementalBuffer` segura os
bytes de uma sequência incompleta e só entrega texto em fronteira de caractere, reproduzindo a regra de *maximal
subpart* do `Encoding.UTF8` para bytes inválidos — é isso que garante que a concatenação dos pedaços seja idêntica à
decodificação em lote, inclusive quando a saída é inválida. O decodificador padrão retém também um substituto alto
ou um `U+FFFD` no fim do texto, pelo mesmo motivo: são a marca de um caractere que ainda não fechou. É o mesmo
cuidado de [DEC-L-KEY](#dec-l-key)/`TokenizedBlockCache` com pares substitutos, um nível abaixo (UTF-8, não UTF-16).

**A parada por texto passa a olhar a cauda.** Procurar o sufixo no texto inteiro a cada token teria o mesmo custo
quadrático que a decodificação. Como qualquer ocorrência nova termina dentro do pedaço recém-decodificado, basta
manter uma cauda do tamanho da maior sequência de parada menos um. O conjunto de tokens de parada (`_stops`)
continua calculado uma única vez por inicialização, pelo adapter.

### DEC-R42-STREAMASYNC

**`StreamAsync` é o único caminho de geração; `GenerateAsync` é a concatenação dele.** A interface
`ILocalModelRuntime` ganha `StreamAsync` com **corpo padrão** que faz uma chamada a `GenerateAsync` e devolve tudo
num único `GeneratedChunk` final — runtimes falsos e futuros continuam válidos sem escrever nada. O
`OnnxLocalModelRuntime` sobrescreve com streaming real e implementa `GenerateAsync` **em cima** do mesmo laço
interno, em vez de manter dois caminhos de geração que poderiam divergir em parada, contagem de tokens ou métricas.

**O fallback para CPU não é igual nos dois modos, de propósito.** Sem streaming, uma falha nativa do provider
acelerado repete o pedido inteiro na CPU: ninguém viu nada, nada se duplica. Em streaming, o pedido só é refeito se
**nenhum pedaço foi entregue**; depois do primeiro texto exibido, reiniciar duplicaria o que o usuário já está
lendo, então a falha sobe e a UI decide. `AiProviderUnavailableException` e os motivos de
[DEC-R41-REASONS](#dec-r41-reasons) continuam sendo o vocabulário; nada de novo foi criado aqui.

**Abandonar a enumeração encerra a sessão nativa.** O laço nativo roda numa thread de trabalho e publica em um
canal; o iterador só lê. Sair do `await foreach` sem consumir tudo dispara `terminate_session`, espera o gerador ser
descartado e libera o decodificador incremental antes de devolver o controle — sem exceção não observada. É o mesmo
tratamento do `Esc`, e o teste com modelo real confirma que o runtime volta a servir no pedido seguinte.

### DEC-R42-PROMPTTOKENS

**Campos aditivos em `ModelGenerationRequest`, nenhum obrigatório.** `PromptTokens` (`IReadOnlyList<int>?`) permite
entregar o prompt já tokenizado — quem montou o contexto na Fase 3 não paga a tokenização duas vezes; o runtime
continua dono da janela e recusa com `LocalModelContextException` o que não couber. `StopSequences`
(`IReadOnlyList<string>?`) acrescenta paradas por texto às do modelo, sem substituir a parada pelo sufixo do editor.
`PrefixCache` (`PrefixCacheState?`) é **inerte nesta entrega**: existe só para que a assinatura não mude quando R43
implementar o experimento de reuso de KV; nenhum runtime lê o valor, e prometer o contrário seria maquiar R43.

## Fase 4 — IA explícita (lote A42, 19/09/2026)

### DEC-A42-PRESENTER

**A prévia de `Ctrl+;` tem presenter próprio; a *superfície* é que continua única.** O
`CompletionWindowPresenter` guarda lista filtrada por prefixo e seleção estável por identidade de símbolo — aqui não
há lista, item, filtro nem seleção, só um texto que cresce por pedaços e que vale enquanto o documento não mudar.
Reaproveitá-lo significaria esvaziar metade dele. O novo `AiCompletionPreviewPresenter` guarda estado
(`Idle`/`Generating`/`Preview`), texto acumulado, documento/cursor capturados e uma geração monotônica; a Fase 4 já
previa "indicador de geração (renderizador de fundo)" como componente novo, distinto do ghost determinístico da
Fase 5.1.

O invariante "um presenter, uma edição" é lido como **uma apresentação visível por vez**, e é garantido por
construção: `Ctrl+;` começa por `InvalidateCompletion()` (fecha lista, apaga ghost, cancela pedido anterior) e,
enquanto a prévia está ativa, `CaptureInlineState().ExplicitRequestPending` é verdadeiro, de modo que o ghost
automático sequer é pedido. O painel novo (`AiCompletionPanel`, em uma camada própria) usa os mesmos tokens do painel
da lista — `PanelBackground`, `ControlBorderBrush`, `Syntax.GhostText`, `AccentBrush` —, sem cor fixa.

### DEC-A42-ARBITRATION

**`Ctrl+;` não é bloqueado pela lista aberta: ele a fecha e assume a superfície.** É o que
[architecture.md](architecture.md#ia-explícita-ctrl) já registrava ("`Ctrl+Espaço` cancela prévia/IA pendente e abre
lista; `Ctrl+;` fecha lista e substitui ghost"), e a precedência Lista › Snippet › Inline › Global continua intacta:
o comando é do escopo `Global` e só é alcançado quando nenhum estado ativo reivindicou a tecla. A recíproca também
vale — `Ctrl+Espaço` durante a geração cancela o pedido explícito e abre a lista.

`Tab` e `Esc` da prévia são os comandos do escopo `Inline` (`editor.inline.accept`/`editor.inline.dismiss`), não
comandos novos: AC-18 define `Ctrl+;` como uma prévia inline, e prévia e ghost nunca estão ativos ao mesmo tempo, de
modo que não há ambiguidade a resolver. Um reatalho de qualquer um dos dois continua valendo, inclusive no rótulo da
prévia, que é derivado dos gestos efetivos da aba.

### DEC-A42-FALLBACK-LINE

**O motivo vive em um campo da lista, não em uma escrita única na linha de estado.** Refiltrar a lista reescreve
aquela linha; guardar o motivo em `_traditionalCompletionReason` e recompô-lo a cada atualização é o que faz a
explicação sobreviver ao usuário digitar com a lista aberta. A tradução motivo → texto está em
`AiCompletionFallbackMessages` e é decidida **só** por `AiCompletionFailure` + `LocalModelUnavailableReason`
(DEC-R41-REASONS); a mensagem do serviço só é usada quando não há motivo tipado nenhum. `RetryAfter` no futuro
acrescenta "Nova tentativa em N s" a qualquer motivo, que é como a linha de cooldown da matriz recebe o tempo
restante sem perder a causa durável.

Duas condições que não são recusa do modelo entram pela mesma porta: IA desligada nas preferências (`Enabled` falso
ou modo `Basic`) e aba sem provider explícito. Nenhuma abre diálogo; todas abrem a lista com uma linha.

### DEC-A42-UNDO

**O aceite é uma inserção dentro de um único `RunUpdate`, sem aceite incremental.** Um `Ctrl+Z` desfaz a prévia
inteira, igual ao aceite pela lista (HDL-08). O `IncrementalTab` do ghost não se aplica: o candidato explícito é
multilinha e entregá-lo por palavra deixaria um fragmento estruturalmente quebrado justamente no caso em que o
usuário pediu a sugestão inteira. A prévia nunca é aceita sozinha, nunca executa nada e desaparece sem tocar no
documento se não for confirmada.

**Resultado obsoleto.** O mecanismo é o que já existia: `InvalidateCompletion` — chamado por edição, movimento de
cursor, troca de seleção, perda de foco, troca de aba e mudança de preferências — agora também cancela o token da IA
e reseta o presenter, o que **avança a geração**. Cada atualização recebida é conferida contra essa geração e contra
documento/cursor/foco/aba antes de desenhar, então um provedor que ignore o cancelamento continua gerando para
ninguém.
## Fase 4 — IA explícita (lote A43, 19/09/2026)

### DEC-A43-TIMEOUT

**O prazo rígido corta a espera, não o resultado: vencido, o que já era válido vira candidato.** A matriz de
fallback pede "mantém prévia parcial válida; sem prévia, lista", e a leitura literal disso é que o tempo esgotado
**não é uma falha** quando sobrou texto aproveitável. O `AiGenerationPipeline` fecha a geração com o mesmo
`CompletionOutputProcessor.CleanStructured` de sempre e devolve um `AiCompletionCandidate` marcado
(`TimedOut = true`, `IsComplete = false`); só quando a limpeza não deixa nada é que aparece
`AiCompletionFailure.Timeout` e a lista tradicional abre. Um pedido com `RequireComplete` recusa direto, porque
para ele um pedaço nunca serviu.

| Parâmetro | Valor | Razão |
| --- | --- | --- |
| Onde vive | `AutocompleteSettings.AiTimeoutMilliseconds` (aditivo, ausente = 10 000) | É o campo que [configuration.md](configuration.md#opções-necessárias) já previa; criar uma constante interna deixaria a documentação mentindo sobre ser configurável |
| Faixa | 1 000–60 000 ms, em `Validate()` | A mesma faixa proposta na tabela de opções |
| Escopo da medida | ponta a ponta, do primeiro `MoveNextAsync` ao último pedaço — fila, carga e geração inclusive | O usuário espera o relógio de parede dele, não o do runtime |
| Relógio | `TimeProvider` injetado no pipeline | Nunca `Task.Delay` real: o teste avança dez segundos sem dormir um |
| Quem cancela | um `CancellationTokenSource` ligado ao do chamador | O runtime é interrompido pelo mesmo caminho do `Esc` (DEC-R42-STREAMASYNC); nada precisou mudar em `LocalAiModelService` |

**Prazo vencido e `Esc` não se confundem.** O pipeline só trata como prazo o cancelamento em que o token do
chamador continua intacto. Um `Esc` durante uma geração que já produziu texto continua descartando tudo — aceitar o
parcial ali seria transformar "desisti" em "aceito o que tiver".

### DEC-A43-INDICATOR

**No primeiro segundo não se desenha nada, a menos que o resultado já esteja pronto.** A tabela de tempos pede um
segundo de atraso para o indicador; aplicá-lo só ao texto "gerando…", e não ao painel, deixaria o painel piscando
vazio, que é exatamente o ruído que o atraso existe para evitar. A regra implementada é uma só e vale para as duas
esperas (carga e geração): o painel aparece quando o temporizador de 1 s vence **ou** quando há candidato final. Uma
geração que termina em 300 ms não mostra indicador nenhum — o usuário vê a sugestão aparecer.

O temporizador é criado por um `TimeProvider` da própria view (`_aiClock`), pela mesma razão do prazo rígido: teste
determinístico, sem tempo de parede. Isso reabre — de propósito — três asserções de A42 que mediam o indicador
"imediatamente"; elas passaram a avançar o relógio, e o que se afirma agora é mais forte: em uma geração rápida a
linha de estado **nunca chega a conter** "Gerando".

### DEC-A43-OVERFLOW

**`ContextOverflow` reduz o orçamento uma vez, e só antes de existir prévia.** A redução é a mesma da Fase 3 — o
`AiContextPipeline` corta fatos e encolhe a janela do editor sozinho quando recebe um orçamento menor —, de modo que
a segunda tentativa não é um caminho novo: é o mesmo `Prepare` com `contextTokens × 0,75`
(`AiContextPipeline.WindowShrinkFactor`, a constante que a fase já usava para encolher). Uma única repetição, nunca
um laço: se o orçamento reduzido ainda não cabe, a recusa sai com `LocalModelUnavailableReason.ContextOverflow`
intacto, que é o que a linha da matriz manda mostrar.

Duas guardas: a repetição não acontece depois de algum pedaço já ter sido publicado (repetir reescreveria o texto
que o usuário está lendo — o mesmo motivo pelo qual o runtime não reinicia em streaming, DEC-R42-STREAMASYNC), e
não acontece quando o orçamento reduzido ficaria abaixo do mínimo utilizável, caso em que insistir só trocaria a
mensagem certa por outra.

### DEC-A43-PREEMPTION

**Preempção é descarte silencioso na tela e falha tipada no contrato.** `LocalModelPreemptedException` deixa de cair
no ramo genérico de `LocalModelUnavailableException` (que abriria a lista tradicional explicando uma "recusa do
modelo" que não houve) e passa a produzir `AiCompletionFailure.Preempted`. A interface trata esse valor como um
cancelamento: apaga o indicador e não abre lista nem escreve linha de estado. A distinção continua existindo para
quem observa diagnóstico — `LocalAiModelService` já registra `ai.generation.preempted` e a métrica com
`reason=preempted` —, e é isso que separa "o usuário desistiu" de "fui preemptado" sem inventar uma diferença
visual que a matriz não pede.

### DEC-A43-TRADITIONAL

**`TraditionalEnabled` existe como preferência e o fallback da IA a respeita; o gate do `Ctrl+Espaço` continua
pendente em T08.** O campo entra em `AutocompleteSettings` no mesmo formato anulável das flags inline (ausente =
true, false explícito preservado), como manda [configuration.md](configuration.md#migração). Com a lista desligada,
uma falha de `Ctrl+;` mostra **uma linha de estado** com o motivo da IA mais "Lista tradicional desligada nas
preferências" e **não** abre lista nenhuma — o fallback não reabre, nem "só desta vez", uma apresentação que o
usuário desligou, e não altera a preferência para conseguir mostrá-la.

O que este lote **não** fez, e fica registrado como pendência de T08: `Ctrl+Espaço` pedido diretamente ainda abre a
lista mesmo com a flag falsa (`traditional.manual = Enabled && TraditionalEnabled` só está aplicado no caminho de
fallback da IA), e a flag continua sem controle visual em `AutocompleteSettingsWindow`, exatamente como as três
flags inline de 5.1.

## Fase 7 — chat de agentes local e concorrência com autocomplete (lote 9, P7-L09-ONNX, 25/09/2026)

### DEC-P9-QUEUEBUDGET

**Achado.** Até este lote, `LocalAiModelService` só preemptava em uma direção: um pedido
`AiRequestPriority.Interactive` que chega cancela uma geração `Background` **ativa**
(`PreemptBackground`, DEC-A43-PREEMPTION), mas nada limitava a espera de um pedido `Background` que chega **depois**
que um turno `Interactive` já tomou a fila do proprietário único (`PriorityGate` é um mutex de um único titular; ver
[`LocalAgentProvider`](../../src/EsilvaSoft.SlopStudio.Application/Agents/LocalAgentProvider.cs)). Antes do chat de
agentes local (lote 9) isso não era visível na prática: a única geração interativa concorrente era a proposta curta
de `LocalModelAiChatService` (até 256–1024 tokens, um bloco só; serviço **removido em 25/09/2026 pela [ADR-055](../10-decisoes-arquiteturais.md#adr-055--remoção-do-assistente-ia-por-aba-25092026)**). O chat de agentes usa o mesmo
`AiRequestPriority.Interactive` e o mesmo `StreamAsync`, mas em streaming — uma conversa pode segurar a fila por
muito mais tempo. Sem limite, o autocomplete ambiente (`AiAutocompleteProvider`, sempre `Background`) ficaria
esperando atrás do chat pela duração inteira do turno, em vez de abster-se e tentar de novo na próxima pausa de
digitação.

**Correção mínima.** `WaitForTurnAsync` (novo, privado) intercala um orçamento de fila só para
`AiRequestPriority.Background`: um `CancellationTokenSource(BackgroundQueueBudget, _clock)` (500 ms, o mesmo padrão
de relógio injetável do prazo rígido em DEC-A43-TIMEOUT) ligado ao token do pedido. Vencido o orçamento **enquanto
ainda espera a fila** — nunca depois de já ter entrado, e nunca por cancelamento do chamador ou do desligamento —, o
pedido desiste com o mesmo `LocalModelPreemptedException` silencioso de uma preempção ativa (DEC-A43-PREEMPTION):
mesmo tipo, mesmo tratamento em `AiCompletionFailure.Preempted`, mesma abstenção sem mensagem. Sem contenção
(`_gate` livre), a espera resolve na hora e o orçamento nunca dispara — não há custo no caminho comum. `Interactive`
continua esperando sem limite, porque é ação explícita (chat, `Ctrl+;`, teste de modelo) e o usuário já está
esperando por ela.

| Parâmetro | Valor | Razão |
| --- | --- | --- |
| Escopo | só a espera pela fila (`PriorityGate.WaitAsync`), nunca a geração em si nem o carregamento do modelo | O autocomplete que já está gerando continua sujeito só à preempção existente (DEC-A43-PREEMPTION); o orçamento novo resolve especificamente a fila |
| Valor | 500 ms, constante interna (não é opção de `AutocompleteSettings`, igual a `RetryDelay`) | Não é uma preferência de produto: é o comportamento do proprietário único diante de contenção |
| Relógio | `TimeProvider` já injetado em `LocalAiModelService` | Mesmo padrão de DEC-A43-TIMEOUT: teste avança o relógio, sem dormir tempo real |
| Falha | `LocalModelPreemptedException` (não uma `OperationCanceledException` crua) | Reaproveita o contrato e o tratamento silencioso que a interface já dá a preempção — nenhuma mudança em `AiCompletionFailure`/UI |

Testes em `LocalAiOfflinePreemptionTests` provam, com `StreamingRuntimeFake`/`ManualTimeProvider` (sem modelo ONNX
real): o pedido de fundo continua esperando pouco antes do orçamento vencer, desiste sozinho ao vencer (o turno
interativo segue rodando, intocado) e é servido imediatamente quando a fila está livre; o mesmo vale pelo caminho
completo de `AiAutocompleteProvider.GetCompletionAsync`, que devolve `null` (abstenção silenciosa) em vez de propagar
exceção. `LocalAgentProviderTests` (19 testes) e `LocalAiModelServiceStreamingTests` continuam verdes sem alteração —
o orçamento não muda nenhum caminho `Interactive`.

### DEC-P9-OFFLINE

**Sem achado, evidência de invariante existente.** `LocalAgentProvider` já não tinha, antes deste lote, nenhuma
referência a `OpenAiAgentProvider`/`ClaudeAgentProvider`: seu único construtor recebe `ILocalAiModelService` e
`IAutocompleteService`, e `AgentRuntime`/`AgentProviderCatalog` tratam cada provider de forma independente — a
indisponibilidade de um (rede ausente, credencial inválida, `CreateSessionAsync` lançando) nunca é propagada aos
demais (`AgentProviderCatalog.GetStatusAsync` já captura exceção por provider; `AgentRuntime.StartSessionAsync`
falha por sessão, não por runtime inteiro). `LocalAiOfflinePreemptionTests` registra isso como evidência automatizada
de AC-16 (chat local completa um turno inteiro com dois providers externos falsos registrados no mesmo catálogo e
runtime, ambos totalmente indisponíveis) para que uma futura integração real do adapter OpenAI/Claude (lotes 7/8) não
regrida esse isolamento silenciosamente.
