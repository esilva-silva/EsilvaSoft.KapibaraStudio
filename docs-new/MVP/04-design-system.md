# Design system

## Organização e medidas

Preservar experiência desktop de edição prolongada: barra superior, lateral Conexões/Arquivos, abas com destino explícito, editor acima dos resultados e barra de estado. Explorer navega; área central prioriza trabalho aberto. Abrir/trocar pasta não fecha documentos nem executa conteúdo.

Janela inicial 1440×900, mínima 960×620; Explorer inicial 260, ajustável 200–420; editor/resultados em torno de 60/40, mínimos 180/140; divisores de 5 unidades lógicas. Modal deve caber na proprietária com conteúdo rolável. Controles usam recursos/medidas existentes e espaçamentos múltiplos de 4.

Painel Agente IA fica à direita, largura inicial 380, mínima 320 e máxima 560; preserva pelo menos 690 para a área de abas. Sem espaço, ocupa superfície própria com retorno explícito ao editor. Não restaurar assistente por aba nem criar chat separado de workflow.

## Hierarquia, tema e localização

Usar recursos semânticos dinâmicos, temas claro/escuro e estados distinguíveis por texto/ícone, além de cor. Tipografia do painel: Inter 13, metadados 12, título 16 e código 14, respeitando fallback CJK. Código, ghost text, seleção e diagnóstico preservam contraste e a geometria do texto; referência mínima de contraste textual 4,5:1.

pt-BR inicial, en fallback e suporte existente es/zh-CN. Trocar idioma atualiza rótulos/estados/acessibilidade sem alterar scripts, resultados, identificadores BSON ou credenciais. Textos longos quebram; rolagem é local; ações de confirmação/aplicar/cancelar permanecem alcançáveis.

## Interação

Destino aparece no cabeçalho; execução/carregamento, ausência de modelo, erro, resultado vazio, rascunho pendente e falha de persistência são explícitos. Confirmação não confunde negação, expiração e indisponibilidade. Propostas mostram diff, conflito e Undo, sem salvar/consultar automaticamente.

Autocomplete normal usa ghost determinístico e lista Ctrl+Espaço. IA só é chamada por `Ctrl+;`; Tab aceita prévia, Escape descarta. Descartar prévia/lista tem precedência sobre cancelar consulta. Ctrl+. não é padrão. Ctrl+Espaço pode conflitar com IME: existe override persistido, sem prometer tela de edição de atalhos.

Agente: Ctrl+Shift+A abre/foca/recolhe conforme foco; Enter quebra linha e Ctrl+Enter envia no composer. Escape no composer não cancela turno. Cancelar execução é ação explícita. Atalhos do editor não executam consulta enquanto foco pertence ao agente. F6 permite sair do editor/retornar da superfície própria.

## Verificação visual

Mudanças exigem PNGs reais gerados pelos testes, inspeção documentada de claro/escuro, quatro idiomas, textos longos, estados relevantes e escalas 100/150/200% em dimensões aplicáveis. Não alterar goldens para esconder regressão. Headless não substitui foco, teclado, IME, leitor de tela nem diálogos nativos.

Fonte histórica consolidada: [design system anterior](../../docs/17-design-system-ui-ux.md). Notas cronológicas antigas não reativam modos ou fluxos substituídos.

## Tokens e medidas detalhadas

Barra superior 40/inferior 36; ação Cancelar tem alvo de 28 unidades lógicas. Controles comuns 32, linhas Explorer 28 e cantos raio 4. Modal conexões 800×560, limitada à proprietária. Editor/saídas rolam independentemente, sem rolagem global central.

| Token | Claro | Escuro |
| --- | --- | --- |
| WorkspaceBackground | #F7F9FC | #0B1020 |
| PanelBackground | #FFFFFF | #151D2E |
| SecondaryBackground | #EEF3F9 | #111827 |
| PrimaryText | #162033 | #F1F5F9 |
| SecondaryText | #475569 | #94A3B8 |
| AccentBrush | #1B6EDC | #38A8FF |
| OnAccentBrush | #FFFFFF | #0F172A |
| SelectionBrush | #D9EAFE | #193B67 |
| SuccessBrush | #166534 | #3DDC97 |
| WarningBrush | #92400E | #FBBF24 |
| ErrorBrush | #B91C1C | #FB7185 |
| DividerBrush | #CBD5E1 | #334155 |
| ControlBorderBrush | #64748B | #94A3B8 |
| HoverBrush | #E6EEF8 | #26334A |
| AccentHoverBrush | #155EC4 | #60B9FF |
| AccentPressedBrush | #124FA6 | #2589E8 |
| BrandAccentBrush | #7357E8 | #9B7BFF |

Metadados selecionados usam PrimaryText. Foco/limites necessários ao controle: contraste 3:1; textos/placeholders: 4,5:1, inclusive botão normal/hover/pressionado. Sistema é tema inicial, com alternância imediata/persistida.

Código usa 14/entrelinha 21, ajustável 12–20 com proporção 1,5; fallback Cascadia Mono, JetBrains Mono, Consolas, DejaVu Sans Mono, monospace. Preservar MongoTextEditor/AvaloniaEdit; descrições históricas de TextBox não são implementação vigente.

Ensaiar 960×620, 1366×768 e 1920×1080, escalas 100/150/200%, claro/escuro e idiomas aplicáveis. São tamanhos de teste, distintos da janela inicial 1440×900. Conferir alvo Cancelar 28, foco visível, Tab/Shift+Tab, ações alcançáveis na rolagem e retorno do modal à origem.

Barra de estado mostra texto, progresso conhecido/indeterminado, percentual quando conhecido, descrição com dica e operações adicionais, sem overlay bloqueante. Rascunho permanece visível. Nenhuma dessas matrizes é declarada reexecutada nesta revisão.
