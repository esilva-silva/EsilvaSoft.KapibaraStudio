# Plano Headless — arbitragem de teclado e preferências (Fase 2.5)

Estado: escrito em 15/09/2026 como **plano de testes** antes da implementação do `EditorCommandDispatcher`, do `CompletionWindowPresenter` e de `EditorKeyBindings`. **Revisado em 18/09/2026 (lote W0)**, quando os atalhos passaram a ser resolvidos por escopo: parte dos casos já tem teste correspondente na suíte, e os que continuam sem cobertura seguem valendo como plano. Um caso listado aqui **não** é evidência de execução.

## Comandos e escopos (contrato vigente)

Doze comandos rebindáveis por `EditorKeyBindings`, distribuídos em quatro `EditorCommandScope`. A precedência de resolução é **lista aberta › sessão de snippet › ghost visível › global**; o dispatcher consulta um único escopo por vez e **não** tem fallback implícito para `Global`.

| Comando | Escopo | Padrão |
| --- | --- | --- |
| `editor.completion.show` | Global | `Ctrl+Espaço` |
| `editor.completion.ai` | Global | `Ctrl+;` |
| `editor.completion.next` | List | `↓` |
| `editor.completion.previous` | List | `↑` |
| `editor.completion.accept` | List | `Tab` |
| `editor.completion.accept.enter` | List | `Enter` |
| `editor.completion.close` | List | `Esc` |
| `editor.snippet.next` | Snippet | `Tab` |
| `editor.snippet.previous` | Snippet | `Shift+Tab` |
| `editor.snippet.cancel` | Snippet | `Esc` |
| `editor.inline.accept` | Inline | `Tab` |
| `editor.inline.dismiss` | Inline | `Esc` |

`Ctrl+.` **deixou de ser padrão**; continua válido apenas como override já gravado pelo usuário, que nunca é removido nem reescrito. Um evento sem tecla e sem símbolo — uma modificadora pressionada sozinha — não casa com gesto nenhum.

Fontes:

- editor-integration.md §Arbitragem de teclado, §Atalhos e §Foco, seleção e IME;
- testing.md §Editor (Headless) e §Concorrência;
- configuration.md;
- traditional-autocomplete.md §Refinamento e §Snippets;
- phase-2 §Critérios de aceite 4, 5, 7, 8, 10 e 12.

Headless não homologa layout ABNT2/US nativo, IME real, leitor de tela nem Linux. Esses itens ficam na matriz de validação como homologação manual.

## Convenções

| Sigla | Estado (prioridade decrescente) |
| --- | --- |
| L | Lista aberta (`CompletionWindow` visível) |
| S | Sessão de snippet ativa |
| G | Ghost visível (preemptivo atual, provider falso) |
| IA | IA local automática ou explícita; o caminho automático usa `LoadedOnly` |
| N | Nenhum |

Resultado observável = um ou mais destes sinais, capturados no teste:

- texto do documento;
- posição do cursor e seleção;
- visibilidade da lista e item selecionado;
- quantidade de passos de desfazer;
- visibilidade do ghost;
- chamadas registradas por fontes/serviços falsos;
- início ou cancelamento de execução.

Nenhum teste lê estado interno privado para decidir aprovação.

**Base comum**: aba Console conectada a um perfil falso `servidor-alfa`, destino `Projetos/Clientes`, schema de `schemas/clientes.json` em cache e `AutocompleteSettings` padrão. O teste envia teclas pelo `TopLevel` Headless. Qualquer outra pré-condição aparece na linha.

## Matriz de arbitragem

Uma linha de teste por célula relevante. Na tabela original, "—" significa que a tecla não tem ação própria naquele estado; o caso correspondente registra a decisão adotada.

| Id | Estado | Tecla | Pré-condição | Ação | Resultado observável esperado |
| --- | --- | --- | --- | --- | --- |
| ARB-01 | L | `Tab` | `db.Clientes.find({ No| })`, lista aberta, `Nome` selecionado | `Tab` | Texto `db.Clientes.find({ Nome| })`; lista fechada; nenhum `\t` inserido; um único passo de desfazer restaura `No` |
| ARB-02 | S | `Tab` | Snippet `filter.range` inserido; placeholder 1 selecionado | `Tab` | Seleção passa ao placeholder 2; texto inalterado |
| ARB-03 | G | `Tab` | Ghost falso visível após `db.Clientes.find({ sta| })` com sugestão `tus: ` | `Tab` | Ghost aceito (inteiro ou incremental conforme `IncrementalTab`); texto contém a parte aceita; comportamento atual preservado |
| ARB-04 | N | `Tab` | Sem lista, snippet nem ghost | `Tab` | Comportamento atual do editor (indentação); nenhuma lista abre |
| ARB-05 | L + S | `Tab` | Snippet ativo no placeholder 1 e lista aberta dentro dele | `Tab`, depois `Tab` | 1º `Tab` aceita o item da lista e mantém o snippet ativo; 2º `Tab` vai ao placeholder 2 (prioridade L › S) |
| ARB-06 | L | `Shift+Tab` | Lista aberta | `Shift+Tab` | Nenhum item aceito e seleção da lista inalterada; a tecla segue para o próximo estado ativo ou para o comportamento normal (decisão A-01) |
| ARB-07 | S | `Shift+Tab` | Snippet no placeholder 2 | `Shift+Tab` | Seleção volta ao placeholder 1; texto inalterado |
| ARB-08 | G | `Shift+Tab` | Ghost visível | `Shift+Tab` | Comportamento normal; o ghost **não** é aceito |
| ARB-09 | N | `Shift+Tab` | Nenhum | `Shift+Tab` | Comportamento atual do editor |
| ARB-10 | L | `Enter` | Lista aberta; `CompletionEnterAccepts = true` (padrão) | `Enter` | Item aceito; nenhuma quebra de linha inserida; lista fechada |
| ARB-11 | L | `Enter` | Lista aberta; `CompletionEnterAccepts = false` | `Enter` | Lista fechada sem aceitar; quebra de linha inserida (decisão A-02) |
| ARB-12 | S | `Enter` | Snippet ativo | `Enter` | Sessão de snippet encerrada (novo `Tab` não navega placeholders) e quebra de linha inserida |
| ARB-13 | G | `Enter` | Ghost visível | `Enter` | Quebra de linha inserida; texto do ghost **não** inserido; ghost oculto |
| ARB-14 | N | `Enter` | Nenhum | `Enter` | Quebra de linha normal |
| ARB-15 | L | `Esc` | Lista aberta **e** execução falsa em andamento na aba | `Esc` | Lista fechada; texto inalterado; a execução **continua** (a lista consome o `Esc`) |
| ARB-16 | S | `Esc` | Snippet ativo | `Esc` | Sessão encerrada; texto inalterado |
| ARB-17 | G | `Esc` | Ghost visível | `Esc` | Ghost descartado; texto inalterado |
| ARB-18 | N | `Esc` | Execução falsa em andamento | `Esc` | Cancelamento solicitado à execução (regra global existente) |
| ARB-19 | L | `↓` / `↑` | Lista aberta com ≥ 2 itens, 1º selecionado | `↓`, depois `↑` | Seleção vai ao 2º item e volta ao 1º; cursor e texto do editor inalterados |
| ARB-20 | S | `↓` | Snippet ativo em documento de 2 linhas | `↓` | Cursor desce uma linha (comportamento normal); texto inalterado |
| ARB-21 | G | `↓` | Ghost visível em documento de 2 linhas | `↓` | Ghost descartado e cursor movido; nenhuma nova requisição de ghost só pelo movimento |
| ARB-22 | N | `↓` | Nenhum | `↓` | Cursor desce uma linha |
| ARB-23 | L | `Ctrl+Espaço` | Lista aberta com `Nome` selecionado | `Ctrl+Espaço` | Nova requisição (serviço falso registra 2 pedidos com stamps distintos); lista continua aberta; seleção preservada por `SymbolId` |
| ARB-24 | S | `Ctrl+Espaço` | Snippet ativo no placeholder 1 | `Ctrl+Espaço` | Lista abre no placeholder; snippet continua ativo |
| ARB-25 | G | `Ctrl+Espaço` | Ghost visível | `Ctrl+Espaço` | Ghost oculto e lista aberta |
| ARB-26 | N | `Ctrl+Espaço` | `db.Clientes.find({ | })` | `Ctrl+Espaço` | Lista aberta com itens de `meta-find-filter-root` (critério 4) |
| ARB-27 | L | `Ctrl+.` | Igual ARB-23, sessão com override `editor.completion.show: ["Ctrl+."]` | `Ctrl+.` | Igual ARB-23. Sem esse override gravado, `Ctrl+.` não faz nada |
| ARB-28 | S | `Ctrl+.` | Igual ARB-24, com o mesmo override | `Ctrl+.` | Igual ARB-24 |
| ARB-29 | G | `Ctrl+.` | Igual ARB-25, com o mesmo override | `Ctrl+.` | Igual ARB-25 |
| ARB-30 | N | `Ctrl+.` | Igual ARB-26, com o mesmo override | `Ctrl+.` | Igual ARB-26 (critério 4) |

### Fora do escopo da Fase 2

Listados apenas para rastreabilidade; não viram teste nesta fase:

- a coluna de geração automática da IA: o fluxo atual tem estado de geração, cancelamento por `Esc`, descarte por edição/geração e aceite por `Tab`; a execução com modelo ONNX real permanece fora da validação automatizada;
- a linha **`Ctrl+;`** em L, S, G e N: o gesto é reconhecido e consumido (PREF-09), abre a prévia da IA explícita quando há provider e cai para o fallback tradicional quando necessário;
- **`Alt+]` / `Alt+[`**: alternativas adiadas;
- **`Ctrl+→`**: aceite por palavra adiado.

A verificação ligada a `Ctrl+;` é negativa (PREF-09): o gesto não aciona `editor.completion.show`, não insere `;` e não abre lista.

### Atalhos globais inalterados com lista aberta

| Id | Pré-condição | Ação | Resultado observável esperado |
| --- | --- | --- | --- |
| GLB-01 | Lista aberta | `F6` | Foco sai do editor como hoje; a lista fecha por perda de foco |
| GLB-02 | Lista aberta | `Ctrl+Enter` | Execução da seleção/documento iniciada como hoje (decisão A-03 sobre o fechamento da lista) |
| GLB-03 | Lista aberta | `F5` | Execução iniciada como hoje |
| GLB-04 | Lista aberta; outra aba aberta | `Ctrl+Tab` | Aba trocada; a lista fecha; um resultado tardio da requisição anterior não é aplicado na aba nova |
| GLB-05 | Lista aberta | `Ctrl+T` / `Ctrl+W` / `Ctrl+O` / `Ctrl+S` | Mesmos comandos de hoje; nenhum item aceito |

## Comportamento Headless complementar

| Id | Pré-condição | Ação | Resultado observável esperado | Critério |
| --- | --- | --- | --- | --- |
| HDL-01 | `db.Clientes.find({ N| })`, lista aberta | Digitar `o` | Lista refinada; serviço falso registra estreitamento sem nova análise do documento; ordem igual à devolvida pelo ranking | traditional §Refinamento |
| HDL-02 | Lista aberta em chave | Digitar `:` (e, em testes separados, `,` `{` `}` `(` `)` e espaço fora de string) | Lista fecha; o caractere é inserido | traditional §Refinamento |
| HDL-03 | `db.Clientes.find({ No| })`, lista aberta | `Backspace` ×3 | Lista fecha ao passar do início do token | traditional §Refinamento |
| HDL-04 | Item aceito | `Ctrl+Z` uma vez | Texto e cursor exatamente como antes do aceite | Critério 5 |
| HDL-05 | Snippet de teste com `$1` repetido | Digitar no 1º placeholder | Cópias espelhadas atualizadas juntas | testing §Editor |
| HDL-06 | Snippet com `$0` | `Tab` até o fim | Cursor na posição de `$0`; sessão encerrada | testing §Editor |
| HDL-07 | Snippet com `${1|a,b|}` | Entrar no placeholder | Texto `a` e lista tradicional com `a`, `b` aberta | editor-integration §Snippets |
| HDL-08 | Snippet inserido | `Ctrl+Z` uma vez | Snippet inteiro removido em uma unidade | Critério 5 |
| HDL-09 | Evento com `KeySymbol = "."` e `PhysicalKey` diferente do US (simulação ABNT2) + `Ctrl` | Enviar | Lista abre (casamento por `KeySymbol`) | testing §Editor |
| HDL-10 | Evento sem `KeySymbol`, `PhysicalKey` de `.` + `Ctrl` | Enviar | Lista abre (alternativa `PhysicalKey`) | editor-integration §Atalhos |
| HDL-11 | Evento com `PhysicalKey` da tecla `;` US mas `KeySymbol` de outro caractere + `Ctrl` | Enviar | Nem `editor.completion.ai` nem `editor.completion.show` disparam | editor-integration §Atalhos |
| HDL-12 | Lista aberta | Foco vai para outro controle | Lista fecha; ao focar a própria janela da lista, ela permanece | editor-integration §Foco |
| HDL-13 | Seleção não vazia no editor | `Ctrl+.` | Lista não abre | editor-integration §Foco |
| HDL-14 | Lista aberta | Trocar modo da aba ou destino | Lista fecha; resultado pendente descartado | editor-integration §Foco |
| HDL-15 | Provider falso lento que **ignora** cancelamento; requisição A pendente | Digitar fora do token (requisição B) e depois concluir A | A lista mostra somente o resultado de B | Critério 7 |
| HDL-16 | Fonte de metadados falsa contando chamadas; lista aberta | Digitar 10 caracteres de identificador | Zero chamadas remotas durante a digitação | Critério 8 |
| HDL-17 | `CompletionAutoOpenOnTrigger = false` (padrão) | Digitar `db.` | Lista não abre | AC-17 |
| HDL-18 | `CompletionAutoOpenOnTrigger = true` | Digitar `db.` | Lista abre | configuration.md |
| HDL-19 | Composição IME ativa | — | Não automatizável de forma confiável em Headless; homologação manual | editor-integration §IME |
| HDL-20 | Lista aberta com 100 itens | Rolar e trocar itens a cada tecla | Sem exceção; tempo registrado para o risco de virtualização | editor-integration §Lista |

### PNGs

Critério de aceite 10:

- **Cenários**: 2 temas (Light/Dark) × 3 tamanhos × 3 escalas = 18.
- **Estados capturados em cada cenário**:
  - `PNG-L`: lista aberta com detalhe do item destacado;
  - `PNG-D`: documentação tardia visível;
  - `PNG-S`: snippet ativo com placeholders.
- **Evidência**: inspeção dos arquivos reais gerados, registrada no handoff. Contraste pelos recursos semânticos de `App.axaml`.

## Preferências (`EditorKeyBindings` e `AutocompleteSettings`)

Premissa (G00): o comportamento vale para Core + Desktop, com o padrão de validação já existente em `WorkspacePreferences.ValidateUuid` e similares:

- valor inválido torna a sessão ilegível;
- a falha fica visível;
- o arquivo nunca é sobrescrito.

| Id | Pré-condição (sessão v1 salva) | Ação | Resultado observável esperado |
| --- | --- | --- | --- |
| PREF-01 | Sem `EditorKeyBindings` | Carregar e enviar `Ctrl+Espaço`, `Ctrl+.`, `Tab` (ghost), `Esc` (ghost) | Padrões ativos: `Ctrl+Espaço` abre a lista e `Ctrl+.` **não** abre (deixou de ser padrão); `Tab` aceita e `Esc` descarta o ghost |
| PREF-02 | `Bindings["editor.completion.show"] = ["Ctrl+Shift+Space"]` | Carregar; enviar `Ctrl+Shift+Espaço` e `Ctrl+Espaço` | O novo gesto abre a lista; `Ctrl+Espaço` não abre (a lista do comando é substituída, decisão A-04); os outros comandos mantêm o padrão |
| PREF-02b | `Bindings["editor.completion.show"] = ["Ctrl+."]` | Carregar; enviar `Ctrl+.` e `Ctrl+Espaço` | O override gravado continua válido e abre a lista; `Ctrl+Espaço` deixa de abrir. A sessão permanece legível e **nunca** é reescrita para remover `Ctrl+.` |
| PREF-03 | `EditorKeyBindings.Version = 2` | Carregar e depois alterar outra preferência | Sessão ilegível e falha visível; bytes do arquivo idênticos após a tentativa de salvar |
| PREF-04 | Gesto inválido (`"Ctrl+"`, `"Ctrl+Banana"`) | Carregar | Ilegível e não sobrescrita (como PREF-03) |
| PREF-05 | Comando desconhecido (`"editor.completion.xyz"`) | Carregar | Ilegível e não sobrescrita (decisão A-05) |
| PREF-06 | `Bindings = null` | Carregar | Ilegível e não sobrescrita |
| PREF-07 | Mesmo gesto em dois comandos **do mesmo escopo** (ex.: `Tab` em `editor.completion.accept` e `editor.completion.next`, ambos `List`) | Carregar | Ilegível e não sobrescrita (decisão A-06) |
| PREF-07b | Mesmo gesto em comandos de **escopos diferentes** (`Tab` em `editor.completion.accept` (List), `editor.snippet.next` (Snippet) e `editor.inline.accept` (Inline)) | Carregar | Válido: é o próprio padrão. A precedência lista › snippet › ghost › global decide qual comando recebe a tecla |
| PREF-08 | Bindings válidos com gestos de pontuação (`Ctrl+.`, `Ctrl+;`) | Salvar e recarregar | Round-trip idêntico; o Core guarda texto, sem tipo Avalonia (teste de arquitetura) |
| PREF-09 | Padrões | Enviar `Ctrl+;` | `editor.completion.show` não dispara. `editor.completion.ai` casa, marca a tecla como tratada e informa indisponibilidade: **nenhum `;` é inserido**, a lista tradicional não abre e nenhuma consulta é executada. O fluxo de IA em si não existe nesta entrega |
| PREF-14 | Padrões | Pressionar e soltar `Ctrl` sozinho (idem `Shift`, `Alt`) | Nenhum comando casa em nenhum escopo: a lista não abre, nenhum ghost é gerado e nenhum parsing ocorre |
| PREF-10 | `Bindings["editor.inline.accept"] = ["Ctrl+Enter"]` | Com lista aberta, enviar `Tab` | `Tab` ainda aceita o item da lista: a arbitragem da lista não depende de `editor.inline.accept` (decisão A-07) |
| PREF-11 | Falha de gravação simulada | Alterar atalho/configuração | Erro visível; estado em memória preservado; arquivo anterior intacto |
| PREF-12 | `AutocompleteSettings` v1 sem `CompletionEnterAccepts`/`CompletionAutoOpenOnTrigger` | Carregar | `true`/`false` efetivos (padrões); ausência distinguida de valor explícito (configuration.md §Migração) |
| PREF-13 | `CompletionEnterAccepts = false` explícito | Salvar e recarregar | Valor `false` preservado; ARB-11 vale |

## Decisões pendentes da arbitragem

| Id | Ambiguidade | Opção adotada no plano |
| --- | --- | --- |
| A-01 | "—" para `Shift+Tab` com lista aberta | A lista não consome a tecla nem aceita item; o evento segue a prioridade seguinte |
| A-02 | Efeito de `Enter` com lista aberta e `CompletionEnterAccepts = false` | Fecha a lista sem aceitar e insere quebra de linha |
| A-03 | `Ctrl+Enter`/`F5` com lista aberta: a lista deve fechar? | Afirmar só que o comando global executa como hoje; fechamento não verificado |
| A-04 | Gestos de um comando em `Bindings`: substituem ou somam aos padrões? | Substituem os daquele comando; comandos ausentes usam os padrões |
| A-05 | Comando desconhecido em `Bindings` | Sessão ilegível (mesmo padrão de enum desconhecido de `IdentifierMode`) |
| A-06 | Mesmo gesto em dois comandos | **Revisada no lote W0.** Ilegível apenas quando os dois comandos estão no **mesmo** `EditorCommandScope`; entre escopos diferentes é legítimo e resolvido por precedência, que é o que permite `Tab` servir lista, snippet e ghost e `Esc` servir três estados. Lista vazia de gestos continua válida e deixa o comando sem atalho |
| A-07 | `Tab` na lista vem de `editor.inline.accept` ou da própria arbitragem? | Da arbitragem da lista; `editor.inline.accept` governa só o ghost |
| A-08 | `↓` com snippet ativo encerra a sessão de snippet? | Não verificado; só movimento do cursor e texto inalterado |
