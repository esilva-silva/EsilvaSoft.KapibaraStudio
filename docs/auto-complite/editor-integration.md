# Integração com o editor

Camada Desktop. Converte contratos da Application em recursos nativos do AvaloniaEdit, arbitra teclado e aplica atalhos. Nenhuma regra MongoDB vive aqui.

## Recursos do AvaloniaEdit 12.0.0 propostos

Presença reportada na inspeção anterior do assembly; integração ainda depende de protótipo no pacote fixado, não só disponibilidade de nomes:

| Recurso | Uso |
| --- | --- |
| `TextDocument.CreateSnapshot`, `ITextSourceVersion`, `TextChangeEventArgs` | `AvaloniaTextSnapshot` sem copiar o documento |
| `CompletionWindow`, `CompletionList`, `ICompletionData` (`Priority`, `Complete`) | Lista tradicional |
| `Snippet`, `SnippetReplaceableTextElement`, `SnippetBoundElement`, `SnippetCaretElement`, `InsertionContext`, `SnippetInputHandler` | Placeholders e navegação por Tab |
| `VisualLineElementGenerator`, `FormattedTextElement`, `InlineObjectElement` | Ghost text no layout do editor |
| `IBackgroundRenderer`, `KnownLayer` | Indicador de geração em andamento |
| `TextAnchor` | Posição da sugestão estável durante typeahead |
| `Document.RunUpdate`, `UndoStack` | Inserção como uma unidade de desfazer |

## Snapshot

`AvaloniaTextSnapshot : ITextSnapshot` encapsula `ITextSource` e `ITextSourceVersion`. A propriedade `Text` ligada ao view model continua existindo para execução, rascunhos e testes, mas o autocomplete deixa de depender dela.

## Lista (`CompletionWindowPresenter`)

- Recebe `CompletionList` e cria `ICompletionData` com rótulo, detalhe, selo de tipo em texto (sem novos ícones), destaque dos intervalos casados e descrição tardia.
- O filtro interno do `CompletionList` fica desativado; a cada tecla o presenter pede ao `CompletionService` o refinamento do contexto estreitado e substitui os itens preservando a seleção por `SymbolId`. Assim, a ordem da tela é sempre a do [ranking](ranking.md).
- `StartOffset`/`EndOffset` da janela seguem `ReplaceSpan`; `Complete` aplica `CompletionEdit` (texto ou snippet).
- Estilo por recursos semânticos existentes de `App.axaml` e [17 — Design system](../17-design-system-ui-ux.md); verificação de contraste nos dois temas.
- Acessibilidade: `AutomationProperties.Name` com rótulo, tipo e detalhe; anúncio do total de itens. Leitor de tela nativo não é homologado por Headless.
- Risco a validar: virtualização e desempenho do `ListBox` interno com 100 itens e trocas frequentes.

## Snippets

`SnippetInserter` converte `SnippetTemplate` em `Snippet` do AvaloniaEdit:

| Sintaxe | Elemento |
| --- | --- |
| Texto | `SnippetTextElement` |
| `${n:placeholder}` (primeira ocorrência) | `SnippetReplaceableTextElement` |
| `$n` repetido | `SnippetBoundElement` ligado ao anterior |
| `$0` | `SnippetCaretElement` |
| `${n\|a,b\|}` | Placeholder com o primeiro valor + lista tradicional de escolhas ao entrar no placeholder |

A inserção ocorre dentro de `Document.RunUpdate`. Ao encerrar a sessão de snippet, o ghost e a lista são recalculados normalmente.

## Ghost text

Substitui a sobreposição `InlineCompletionTextBlock` + `Canvas` ([preemptive-autocomplete.md](preemptive-autocomplete.md#renderização)):

- **Primeira linha:** `GhostTextElementGenerator` insere um `FormattedTextElement` na posição do `TextAnchor` da sugestão, com brush `Syntax.GhostText`; o texto real após o cursor é deslocado pelo próprio layout, sem redesenho do sufixo.
- **Linhas seguintes:** um `InlineObjectElement` no fim da linha hospeda um bloco de texto multilinha (altura extra da linha visual). Alternativa se houver problema de caret/hit-testing: renderizador em camada de fundo.
- O documento nunca é alterado pela prévia; seleção, busca, cópia e undo ignoram o ghost.
- Limite inicial de linhas exibidas: 8 (provisório).

## Arbitragem de teclado

**Estado: implementado (lote W0, 18/09/2026).** Build 0 avisos; suíte 1170 aprovados, 0 falhas.

`EditorCommandScope { Global, List, Snippet, Inline }` substitui a tabela de precedência descritiva por resolução por escopo: `EditorCommandDispatcher.Match(keyEvent, scope)` responde só dentro do escopo consultado, e quem decide qual escopo consultar primeiro é o chamador do editor (lista aberta › sessão de snippet › ghost visível › global). O mesmo gesto pode ser o padrão de comandos diferentes em escopos diferentes — é isso que permite `Tab` aceitar item da lista, avançar placeholder de snippet e aceitar ghost, e `Esc` fechar cada um desses três estados; duplicidade **dentro do mesmo escopo** é inválida e nunca é resolvida silenciosamente ([`EditorKeyBindings`](../../src/EsilvaSoft.KapibaraStudio.Core/EditorKeyBindings.cs)).

Doze comandos ([`EditorCommandIds`](../../src/EsilvaSoft.KapibaraStudio.Core/EditorCommandIds.cs)), todos rebindáveis:

| Comando | Escopo | Padrão |
| --- | --- | --- |
| `editor.completion.show` | Global | `Ctrl+Espaço` |
| `editor.completion.ai` | Global | `Ctrl+;` |
| `editor.completion.next` | List | `↓` |
| `editor.completion.previous` | List | `↑` |
| `editor.completion.accept` | List | `Tab` |
| `editor.completion.accept.enter` | List | `Enter` (governado por `CompletionEnterAccepts`) |
| `editor.completion.close` | List | `Esc` |
| `editor.snippet.next` | Snippet | `Tab` |
| `editor.snippet.previous` | Snippet | `Shift+Tab` |
| `editor.snippet.cancel` | Snippet | `Esc` |
| `editor.inline.accept` | Inline | `Tab` |
| `editor.inline.dismiss` | Inline | `Esc` |

`F6`, `Ctrl+Enter`, `F5`, `Ctrl+T/W/O/S` e `Ctrl+Tab` permanecem inalterados e fora deste registro. `editor.completion.ai` já é reconhecido e marca a tecla como tratada, mas não tem runtime nesta entrega: nunca insere `;`, nunca abre a lista tradicional no lugar e nunca dispara consulta; a aba apenas informa indisponibilidade de forma discreta. A IA explícita propriamente dita pertence à Fase 4 e continua **não implementada**.

## Atalhos

`EditorKeyBindings { Version = 1, Bindings: comando → gestos }` é aditivo em `WorkspacePreferences`, sem tela de edição nesta meta.

- **Persistência:** ausente → padrões. Validação segue o padrão existente: comando desconhecido, lista de gestos nula, gesto malformado, gesto repetido/atribuído a dois comandos **no mesmo escopo** ou versão diferente de 1 tornam a sessão ilegível, com falha visível, e ela nunca é sobrescrita (leitura, gravação e autosave). `EditorKeyBindings.CurrentVersion` permanece `1`; não há migração de dado.
- **Compatibilidade com `Ctrl+.`:** `Ctrl+.` foi removido dos padrões (decisão explícita do produto, ver [AC-08](decisions.md#ac-08--atalhos)). Como os padrões nunca são materializados na sessão, `editor.completion.show: ["Ctrl+."]` só existe em disco como override explícito que o usuário já tinha salvo; esse override continua legível e funcional, e nunca é removido, adicionado ou reescrito em uma sessão existente.
- **Despacho:** `EditorCommandDispatcher.Match` recebe o evento normalizado (`EditorKeyEvent`) e o escopo já escolhido pelo chamador; `WorkspaceTabView.Autocomplete.cs` consulta List, depois Snippet, depois Inline e por fim Global.
- **Layout de teclado (correção de defeito):** a decisão de casamento passou a ser pela **espécie do gesto**, não por uma cadeia de `||`. Gesto de tecla nomeada (`Tab`, `Enter`, `Esc`, setas) casa por identidade de tecla (`PhysicalKey`); gesto de pontuação (`.`, `;`, `]`, `[`, `\`) casa pelo símbolo produzido pelo layout ativo e, na ausência, pelo símbolo físico. Um evento sem tecla e sem símbolo (`EditorKeyEvent.HasTrigger == false`, como um modificador pressionado sozinho) nunca casa com nada. Antes da correção, `Matches` terminava em `gesture.Key == keyEvent.PhysicalKey || gesture.Symbol == keyEvent.PhysicalSymbol`; uma tecla modificadora produz `Symbol`, `PhysicalKey` e `PhysicalSymbol` todos `null`, então `null == null` casava indevidamente com `Ctrl+.` e com `Ctrl+Espaço` — `Ctrl` sozinho abria a lista. O mesmo defeito fazia uma tecla desconhecida sem modificador casar com `Tab` e aceitar o ghost text. Dois outros defeitos foram corrigidos no mesmo lote: gestos ligados a `Enter` nunca casavam porque o enum `Key` do Avalonia nomeia o valor físico `"Return"` e `EditorKey` o chama de `Enter`; e a navegação por `↑`/`↓` era um no-op silencioso porque substituir o `ItemsSource` por um array com a mesma instância selecionada fazia o `ListBox` reemitir `SelectionChanged` e desfazer o movimento recém-aplicado.
- **Conflitos conhecidos (limitação, não resolvida):** com `Ctrl+.` fora dos padrões, `Ctrl+Espaço` é o único disparo padrão do autocomplete básico explícito e **colide com a troca de método de entrada (IME) em Windows e Linux**. Esta meta não entrega tela de edição de atalhos; o único contorno para quem é afetado é um override manual em `EditorKeyBindings` gravando outro gesto para `editor.completion.show`. `Ctrl+;` pode colidir com pontuação de alguns IMEs asiáticos.
- **Documentação:** tabela de atalhos de [17 — Design system](../17-design-system-ui-ux.md) e correção de decisão em [AC-08](decisions.md#ac-08--atalhos) atualizadas nesta entrega.
- **Pendente:** homologação em layouts físicos reais (ABNT2 e US em Windows; X11 e Wayland em Linux), IME real e leitor de tela. Teste Headless não substitui nenhuma dessas.

## Foco, seleção e IME

- Sugestões só com foco no editor e seleção vazia (regra atual).
- Durante composição de IME (texto em pré-edição), ghost e lista automática ficam inibidos.
- Perda de foco fecha lista e ghost, exceto quando o foco vai para a própria janela de completion.
- Troca de aba, modo ou destino invalida tudo (regra atual).

## Temas

Recursos `Syntax.GhostText` (existente), selos de tipo e fundo da lista via recursos semânticos Light/Dark. Evidência PNG nos 18 cenários (tema × tamanho × escala) exigida pelo `AGENTS.md`.


## Presenter compartilhado e gate visual

Prototipar na Fase 2 (T07), antes de 4 e 5.1: uma API de apresentação, sem dependência de ONNX. IA explícita pode publicar atualizações validadas/coalescidas; automático publica candidato estável. Traditional Preemptive possui o presenter; durante T07 atua em lote acordado, sem disputar WorkspaceTabView com Traditional Completion.

VisualLineElementGenerator não é garantia de ghost de largura documental zero: validar fim de linha, posição antes do sufixo, caret/hit-testing e seleção no pacote 12.0.0. Se falhar, adaptar renderer de fundo/overlay existente e registrar decisão; remoção não é gate superior à correção. Nunca inserir texto real só para desenhar prévia.

Primeira entrega só inserção no cursor; edição que altera trecho anterior (aspas em Customer.Id) fica na lista explícita. Mesma operação de edição deve ser visualizada e aplicada, com undo único. Foco, Esc, F6, IME e templates Light/Dark preservados. Inspecionar PNGs reais 2 temas × 3 tamanhos × 3 escalas; nativo/assistivo é homologação separada.

Snapshot evita materialização no novo provider, mas base.Text ainda atende binding; medir evento completo antes de prometer custo constante. Aplicação/aceite conferem stamp no dispatcher. [Configuração](configuration.md) governa flags/atalhos; atalhos opcionais da tabela de arbitragem são extensão futura, não entrega inicial.
