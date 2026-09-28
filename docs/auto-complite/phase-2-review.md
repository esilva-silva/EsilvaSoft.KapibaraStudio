# Revisão independente — parser e completion da Fase 2

Data: 15/09/2026. Perfil: Testing/Performance. Plataforma: Windows x64, AMD64 Family 25 Model 97 (Ryzen 9 7900), Windows 10.0.26200, SDK .NET 10.0.401. Base Git: `2e4f6ea`, worktree sujo.

Esta revisão é somente leitura sobre implementação e testes. A única alteração produzida por ela é este relatório. A Fase 2 **não está concluída**.

## Escopo e fotografia do lote

O worktree mudou durante a inspeção: `SyntaxTreeCache`, provider, snippets, usage tracker e respectivos testes apareceram depois do primeiro inventário e depois de uma primeira compilação bem-sucedida. Por isso, resultados anteriores a essa chegada são registrados abaixo como históricos do lote intermediário, não como validação do snapshot final. A última compilação feita sobre os arquivos mais recentes é o gate autoritativo desta revisão.

Arquivos de produto detectados no lote:

- `src/EsilvaSoft.KapibaraStudio.Application/Language/Syntax/MongoSyntaxTree.cs`
- `src/EsilvaSoft.KapibaraStudio.Application/Language/Syntax/TolerantParser.cs`
- `src/EsilvaSoft.KapibaraStudio.Application/Language/Syntax/SyntaxTreeCache.cs`
- `src/EsilvaSoft.KapibaraStudio.Application/Language/Completion/CompletionContracts.cs`
- `src/EsilvaSoft.KapibaraStudio.Application/Language/Completion/CompletionProvider.cs`
- `src/EsilvaSoft.KapibaraStudio.Application/Language/Completion/CompletionRanker.cs`
- `src/EsilvaSoft.KapibaraStudio.Application/Language/Completion/CompletionService.cs`
- `src/EsilvaSoft.KapibaraStudio.Application/Language/Completion/CompletionUsageTracker.cs`
- `src/EsilvaSoft.KapibaraStudio.Application/Language/Completion/RankingProfile.cs`
- `src/EsilvaSoft.KapibaraStudio.Application/Language/Completion/SnippetTemplate.cs`

Testes detectados no lote:

- `tests/EsilvaSoft.KapibaraStudio.UnitTests/Language/Syntax/TolerantParserTests.cs`
- `tests/EsilvaSoft.KapibaraStudio.UnitTests/Language/Syntax/SyntaxTreeCacheTests.cs`
- `tests/EsilvaSoft.KapibaraStudio.UnitTests/Language/Completion/CompletionProviderTests.cs`
- `tests/EsilvaSoft.KapibaraStudio.UnitTests/Language/Completion/CompletionRankerTests.cs`
- `tests/EsilvaSoft.KapibaraStudio.UnitTests/Language/Completion/CompletionServiceTests.cs`
- `tests/EsilvaSoft.KapibaraStudio.UnitTests/Language/Completion/CompletionUsageTrackerTests.cs`
- `tests/EsilvaSoft.KapibaraStudio.UnitTests/Language/Completion/SnippetTemplateTests.cs`

Documentação modificada já presente no worktree: `docs/auto-complite/phases/phase-2-traditional-autocomplete.md` e o índice gerado `docs/assets/documents.js`. O diretório não rastreado `.claude/` foi considerado alheio a esta revisão.

## Resultado executivo

O código de produto de Application compila, mas a solução no estado mais recente não compila: três erros `CA1861` em `SnippetTemplateTests.cs` (linhas 17, 20 e 34). Assim, nenhum teste novo de completion/cache pode ser considerado validado no snapshot final.

Há dois defeitos estruturais reproduzidos no parser, além de lacunas contratuais relevantes em completion. O cache atual protege razoavelmente contra publicação tardia dentro de sua própria API, porém faz parse completo e ainda não demonstra o comportamento incremental nem os orçamentos de C25. Os novos contratos de request/stamp não estão integrados ao editor, portanto ainda não provam descarte de resposta obsoleta de ponta a ponta.

## Achados e riscos concretos

### Críticos/altos

1. **Gate de build vermelho no snapshot mais recente.** `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -p:UsedAvaloniaProducts=` termina com três erros `CA1861` em `SnippetTemplateTests.cs`. A suíte executada antes da chegada desses arquivos não valida o lote atual.

2. **Um ponto e vírgula dentro de bloco/grupo quebra a hierarquia de statements e spans.** `TolerantParser.cs:91-96` encerra statement para qualquer `;`, sem exigir `stack.Count == 0`. Reprodução sem alterar testes, com `function f(){ const x=1; return x; }`: três statements foram gerados; o último apresentou quatro filhos fora do span do pai. Isso contradiz o contrato de `MongoSyntaxNode` e invalida consumidores que localizam o statement do cursor.

3. **Recuperação de fechamento ausente descarta o conteúdo interno.** Em `TolerantParser.cs:104-113`, cada frame não fechado é reconstruído com `Children = []`; os tokens acumulados deixam de ser alcançáveis pela árvore. Reprodução com `db.c.find({ a: { b: 1` encontrou zero nós `Object` alcançáveis, embora haja objetos e chamada internos. Isso impede justamente a análise interna esperada para statement opaco/incompleto.

4. **Spans e modelo sintático ainda não cumprem C22/C23.** A árvore usa spans absolutos, não larguras relativas; não há nós gramaticais para chamada, membro, propriedade ou argumento. `DottedKeyRecovery` é emitido genericamente após ponto (`TolerantParser.cs:41-42`), inclusive para acesso válido como `db.Clientes`, sem provar que se trata de chave pontuada inválida. ASI, fronteiras por statement, template/regex versus divisão e linha gigante sem fronteira não estão semanticamente tratados.

5. **Credencial/URI entra no snapshot lógico de completion.** `CompletionContext.Connection` guarda um `ConnectionProfile` completo (`CompletionContracts.cs:50-51`), e `CompletionRequest`/`CompletionResponse` retêm esse contexto. Como `ConnectionProfile` contém `ConnectionString`, o contrato de pedido passa a carregar credenciais, contrariando a regra de que snapshots não contêm credenciais. Mesmo sem log atual, aumenta superfície de retenção, dump e diagnóstico. O catálogo necessita identidade/estado capturados, não necessariamente a URI dentro do request apresentável.

6. **Descarte obsoleto e cancelamento não estão provados de ponta a ponta.** `CompletionResponse.IsFor` apenas oferece uma comparação que o consumidor deverá chamar; não há presenter/coordenador usando-a. Não existe teste com provider lento que ignore o token e complete após A→B→A, nem integração demonstrando CTS isolado por aba. `SyntaxTreeCache` descarta parse antigo na publicação e espera com polling cancelável de 25 ms, mas isso cobre apenas a árvore, não catálogo, ranking, resolve tardio ou UI.

7. **Faixas de inserção/substituição são insuficientes.** `CompletionContext` possui somente `ReplaceSpan`, sem caret, `InsertSpan`, quote style ou papel. `CompletionService.cs:55-57` atribui o mesmo span a `InsertRange` e `ReplaceRange` para todo item. Isso não representa as linhas obrigatórias de aspas e faixas, como coleção com caractere inválido, token no meio e conversão de `Cliente.` em `"Cliente.Id"`; a faixa específica por item não pode ser derivada apenas desse contrato. Também não há validação do span contra o snapshot no momento da aplicação.

8. **Seleção de fontes/Peek está incompleta.** `CompletionContext.LocalSymbols` existe, mas `CompletionService` não o consulta nem o repassa; variáveis/símbolos locais podem desaparecer. O catálogo aplica um limite global na ordem de registro das fontes (`KnowledgeCatalog.cs:25-32`), permitindo que uma fonte anterior esgote `MaximumCandidates` e cause fome de schema/snippets. O provider não implementa cotas nem o caso “candidato relevante além do corte”.

9. **Fluxo `Peek`/refresh incompleto.** O contexto usa `MetadataAccess.Peek` por padrão e o provider apenas propaga esse valor. Isso é correto para refiltro/automático, mas a invocação explícita precisa solicitar refresh limitado (`LoadIfNeeded`) e retornar o snapshot atual. Não há orquestração de `Changed`, reconsulta condicionada ao mesmo stamp nem distinção explícita entre abertura e refiltro. Em `Unavailable` com `Peek`, `IsIncomplete=true` pode ser devolvido sem qualquer carga em andamento que gere `Changed`.

### Médios

10. **O parser/cache ainda é O(documento) por nova versão.** `TolerantParser.Parse` materializa `snapshot.GetText(0, snapshot.Length)`, tokeniza e cria nós/tokens para o documento inteiro antes de qualquer reuso. `SyntaxTreeCache` declara que nunca retorna `Incremental`; alterações conhecidas também fazem parse completo. Isso mantém cópia integral, alocação proporcional e latência sem limite para 1 MB/linha gigante. O teste de 1 MB verifica correção básica, não p95, alocação ou janela limitada.

11. **Ranking aloca e ordena mais que o desenho prevê.** Para cada candidato compatível é criado um `Dictionary<string,double>` de contribuições, embora ele seja descartado na saída; camel humps cria listas e arrays; ao final há ordenação integral com LINQ, não top-K parcial. `AutocompleteMetrics.RankingDuration` não é registrado. Sem benchmark de 20/200/2.000 candidatos, o orçamento de 200→100 e ≤64 KB por tecla não tem evidência.

12. **Ranking ainda depende de texto localizado e não expõe explicação.** A incompatibilidade de tipo é inferida procurando `"tipo incompatível"` em `LabelDetail` (`CompletionRanker.cs:26`), em vez de shape/tipo estruturado. `RankingProfile.UsageWeight` e `CompletionUsageTracker` não estão integrados. As contribuições calculadas não chegam a uma API diagnóstica. MRR/top-1/top-5 e zero item inválido no top-5 não foram medidos.

13. **Classificação de fonte perde snippets.** `CompletionService.cs:57` classifica qualquer símbolo sem evidência de schema como `Catalog`, inclusive `SymbolKind.Snippet`; assim `SourceSnippet` não é aplicado e a origem mostrada/medida fica incorreta.

14. **Uso recente cresce sem limite.** `CompletionUsageTracker` usa `ConcurrentDictionary` sem teto, expiração ou limpeza. Embora seja somente em memória e nomes sejam permitidos pela AC-16, bancos, coleções, shapes e símbolos ficam retidos por toda a sessão. Falta medir 2/10 abas e alternância de namespaces. O sinal negativo também pode crescer sem limite útil; não há clamp nem teste de muitas sequências aceite/undo.

15. **Snippet entregue é apenas parser/expansor.** Não há placeholders tipados de UUID/ObjectId via `IdentifierRepresentationService`, `SnippetInserter`, integração AvaloniaEdit, navegação Tab/Shift+Tab, escolha visual, escaping BSON/aspas, uma unidade de undo ou prova de que inserir nunca executa. O parser também não recebe `CancellationToken`/limite de tamanho; para catálogo confiável isso é risco menor, mas deve permanecer bounded.

16. **Imutabilidade é contratual, não totalmente imposta.** `MongoSyntaxTree.Tokens` e listas de filhos são expostos como `IReadOnlyList`, porém podem referenciar coleções mutáveis recebidas pelo construtor. Não há cópia defensiva/coleção imutável no contrato público; um consumidor que recupere o tipo concreto pode corromper a árvore compartilhada do cache.

### Privacidade — evidência e pendências

- Positivo: o parser não avalia código; diagnósticos não incluem trechos do documento; métricas atuais do catálogo usam somente completude. `CompletionUsageTracker` não persiste dados e não recebe valores de documentos por desenho.
- Risco aberto: `CompletionContext.Connection` retém a URI; não há teste sentinela serializando/inspecionando request, response, itens, métricas e logs para provar ausência de URI, credenciais e valores. `LocalSchemas`/`LocalSymbols` são contratos públicos e precisam de teste negativo que impeça valores documentais em `Detail`, snippet, ranking ou diagnóstico.
- Nenhuma chamada Mongo automática foi observada diretamente no novo service; contudo, a política depende de `CatalogAccess` fornecido pelo chamador ainda inexistente. Fakes atuais verificam só `Peek` padrão e kinds, não “fonte que lança se houver I/O”, refiltro, refresh explícito e reconsulta terminal.

## Comandos executados

1. Inventário: `git status --short`, `rg --files`, buscas `rg` por C22–T08, parser, completion, cache, Peek, fontes, métricas e integração. Resultado: worktree sujo e lote em expansão durante a revisão.
2. Leitura integral do texto anexado e dos perfis `testing-agent.md`/`performance-agent.md`; leitura de `execution-plan.md`, `current-state.md`, `architecture.md`, `decisions.md`, `testing.md`, `performance.md`, documentos de contexto/ranking/editor e arquivos do lote.
3. `dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode -p:UsedAvaloniaProducts=`: primeira tentativa falhou porque o sandbox não podia ler `%APPDATA%/NuGet/NuGet.Config`; repetição autorizada fora do sandbox passou.
4. Primeira compilação, antes da chegada dos arquivos adicionais: `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -p:UsedAvaloniaProducts=` — passou, 0 avisos/0 erros. Não representa o snapshot final.
5. `dotnet test ...UnitTests.csproj --no-build --no-restore -p:UsedAvaloniaProducts= --filter FullyQualifiedName~TolerantParserTests` — 8/8 passaram no lote intermediário.
6. `dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore -p:UsedAvaloniaProducts=` — 1.032 passaram em 52 s no lote intermediário. A saída listou testes opt-in como “Ignorado”, embora o resumo final tenha informado 0 ignorados; não foi investigado antes da mudança do worktree.
7. Reprodução por reflexão sobre a DLL de Application, sem criar teste/arquivo: `function f(){ const x=1; return x; }` produziu três statements e quatro violações de contenção; `db.c.find({ a: { b: 1` produziu zero objetos alcançáveis.
8. Última compilação, após a chegada de cache/provider/snippets/testes: `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -p:UsedAvaloniaProducts=` — **falhou** com `CA1861` em `SnippetTemplateTests.cs:17`, `:20` e `:34`. Este é o resultado vigente.

Não foram executados benchmarks: o snapshot final não compila, não existe benchmark específico de parser/ranking no lote detectado e o pedido de encerramento antecipou inspeções adicionais. Não foram feitas homologação nativa, MongoDB real, leitor de tela ou PNGs; nada disso é declarado aprovado.

## Atualização pós-revisão

Após os achados acima, o gate local foi corrigido: os testes de `SnippetTemplate` deixaram de usar matrizes constantes em chamadas repetidas (CA1861), e a validação específica de Completion passou em 19/19. Os dois defeitos do parser foram reproduzidos como testes (`DoesNotSplitStatementsAtSemicolonsInsideAGroup` e `MissingCloseKeepsNestedObjectsReachable`) e corrigidos; os testes de parser passaram em 10/10 e os testes do `SyntaxTreeCache` em 21/21. `MongoSyntaxTree` agora copia defensivamente tokens, diagnósticos e filhos. `CompletionContext` não retém mais `ConnectionProfile`/URI; o serviço resolve o perfil somente por um `ICompletionProfileResolver` transitório. A suíte ampla ainda não é um gate verde neste diretório de artefatos externo porque testes de golden procuram a raiz do repositório e testes Mongo reais exigem seu ambiente próprio; isso permanece pendência explícita, não aprovação.

## Pendências por tarefa C22–T08

| ID | Estado desta revisão | Evidência/lacuna para aceite |
| --- | --- | --- |
| C22 | Parcial, com defeitos reproduzidos | Há parser estrutural, diagnósticos e limite nominal 512. Corrigir hierarquia com `;`, preservar filhos em missing-close, implementar fronteiras/ASI/opaque confiável, regex/template/linha gigante e spans relativos/monotônicos. Propriedade atual cobre uma única string; faltam corpus e sementes independentes. |
| C23 | Inicial | `CompletionContextEngine` cobre papéis lexicais, prefixo, spans, alvo estático e shapes de parâmetros; o menu de Ctrl+Espaço já o consulta. Ainda faltam memoização, escopos JS completos e ligação conservadora de `PipelineInfo`. |
| C24 | Inicial | `ShapeWalker` dirigido pelo catálogo e `PipelineInfo` conservador existem, com cobertura para filtro, pipeline, grupo, lookup e count. Ainda faltam ligação ao parser/contexto real, facet/unwind/replaceRoot, tipos BSON completos e paridade com `AggregationFieldInference`. |
| C25 | Estrutura inicial, não incremental | `SyntaxTreeCache` reutiliza versão idêntica, isola documentos e descarta publicação antiga; testes foram detectados, mas não compilados no gate final. Toda nova versão faz parse completo; faltam checkpoints/reparse por statement, equivalência incremental real, limites de ressincronização, benchmark 1 KiB–1 MB/posição, múltiplas abas e p95/alocação. |
| T01 | Inicial | A aba captura snapshot/escopo antes do provider tradicional e o `MenuFlyout` descarta uma resposta tardia de provider não cooperativo após alteração do editor. Ainda faltam `AvaloniaTextSnapshot`, `EditorRequestScope` no caminho visual, presenter e cobertura de ABA/execução/sessão. |
| T02 | Parcial | Há `CompletionService`/`TraditionalCompletionProvider`, filtro por kinds e propagação de `CatalogAccess`. Faltam contexto C24, símbolos locais, política completa de fontes/cotas, refresh explícito + `Changed`, candidato além do corte, ação de amostragem e prova de zero I/O no refiltro. |
| T03 | Parcial | Há `RankingProfile`, ranker determinístico básico e tracker em memória. Faltam filtros rígidos por shape/tipo/versionamento, uso integrado, cotas/recall, top-K parcial, explicação pública, corpus separado e gates MRR/top-1/top-5. Sem benchmark/percentis. |
| T04 | Parcial mínimo | Há contratos de edit e parser/expansor LSP. Ausentes faixas por item, quoting, placeholders tipados UUID/ObjectId, inserter AvaloniaEdit, Tab/Shift+Tab, escolha, undo único, BSON/escaping e teste de não execução. |
| T05 | Inicial | A lista contextual sobreposta usa presenter, preserva seleção por `SymbolId`, filtra texto e trata ↑/↓/Enter/Tab/Esc; snippets navegam por Tab/Shift+Tab e desfazem em uma operação. Faltam virtualização, detalhe/documentação tardia, acessibilidade, falhas e a matriz visual completa. |
| T06 | Não iniciado | `MenuFlyout`, `ShowSuggestions`, `ConsoleAutocompleteService`, `AggregationCompletionContext` e chamadores legados permanecem. Não remover antes da paridade de C23–C25/T05. |
| T07 | Não detectado | Ausente presenter inline compartilhado e seus testes de caret, multilinha, sufixo, scroll, copy/undo e PNGs. Ghost atual continua legado. |
| T08 | Core pré-existente parcial; integração pendente | A documentação registra flags/preferências Core, mas esta revisão não revalidou os 54 testes isoladamente no snapshot final. Ausentes `EditorCommandDispatcher`, abertura por Ctrl+./Ctrl+Espaço, arbitragem/foco e integração Ctrl+;. Manter como pendente até build verde e testes atuais. |

## Próximo gate recomendado

Restabelecer build verde sem enfraquecer analisadores; adicionar reproduções independentes dos dois defeitos do parser; fechar o contrato de contexto/spans e a identidade de conexão sem URI; então validar cache/provider/ranker com fakes de fontes disjuntas, Peek/LoadIfNeeded, `Changed`, provider não cooperativo e sentinelas de privacidade. Só depois executar benchmarks completos e integração/UI. Isso é uma sequência de pendências, não uma declaração de conclusão da Fase 2.
