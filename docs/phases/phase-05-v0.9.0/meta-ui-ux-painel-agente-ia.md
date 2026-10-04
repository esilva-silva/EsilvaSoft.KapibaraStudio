# Meta P7-UX — Painel Agente IA com prioridade para a conversa

**Recorte de planejamento — 03/10/2026:** este documento conserva contratos/evidências compartilhados e IDs históricos. Na Fase 5, o aceite remanescente cobre Copilot e a infraestrutura necessária; extensões específicas e homologações dos demais providers ficam no [bkl-06](../../backlog/bkl-06-integracoes-agentes.md). Código, disponibilidade, testes e limites de validação existentes permanecem.

Data: **30/09/2026**. Estado: **implementação e validação automatizada concluídas; homologação nativa pendente**.

## Resultado esperado

Transformar o painel Agente IA em uma área de conversa legível e confortável: histórico como região principal, composição de mensagem bem delimitada e controles secundários compactos. Preservar EsilvaSoft.KapibaraStudio, desktop .NET 10/Avalonia, Windows/Linux, licença MIT, contratos dos providers e permissões existentes.

A captura fornecida fundamentou o diagnóstico. O novo layout foi implementado em Avalonia e renderizado pelos testes Headless/Skia com dados sintéticos. As alterações finais estão no checkout principal `F:\source\esilva-silva\EsilvaSoft.KapibaraStudio`, preservando o incremento P7-AUTH existente. Não houve autenticação nem consulta a MongoDB real nesta entrega.

## Diagnóstico

Na captura de 1442 × 932, o painel tem aproximadamente 340 pixels de largura. A conversa visível ocupa cerca de 190 pixels de uma altura útil de 820: aproximadamente 23%. As medidas são estimativas da imagem, não medições de layout Avalonia.

| Problema observado | Consequência | Causa compatível no código |
| --- | --- | --- |
| Provider aparece no chip e novamente no seletor; modelo e modo ocupam linhas adicionais | Repetição e excesso de controles antes da conversa | Cabeçalho empilha badges, seletores, modo, chips, resumo e aviso |
| Chips de arquivo e banco ficam longe da mensagem | Difícil entender o que será enviado | `ContextChips` pertence ao cabeçalho, não ao compositor |
| Aviso de workspace e status ocupam blocos separados | Estado operacional disputa espaço com a conversa | `ReadScopeNotice` e cartão de status ficam fora do histórico |
| “Gerando resposta...” tem grande área vazia | Progresso parece uma segunda área de conteúdo | Cartão de status independente; a causa exata da altura precisa ser medida em execução |
| Provider truncado, modo largo, botões de anexos comprimidos | Proporções não refletem o conteúdo nem a frequência de uso | Provider/modelo dividem largura igualmente; modo ocupa linha inteira; anexos usam `StackPanel` horizontal |
| Mensagens curtas parecem caixas de formulário e avisos se misturam às falas | Papéis e sequência da conversa pouco definidos | Templates com superfícies semelhantes e pouca hierarquia tipográfica |
| Histórico pequeno, compositor com várias ações externas | Poucas mensagens cabem; leitura exige rolagem frequente | Rodapé empilha bloqueio, permissões, dois anexos, entrada e ações |

A mensagem sobre perda da sessão Claude em uma conversa agora Copilot exige revisão: pode ser um registro legítimo de evento anterior, mas a captura não prova falha de roteamento. Deve identificar provider e momento de origem, sem inferir troca silenciosa de sessão.

## Composição proposta

```text
Agente IA / título da conversa       Histórico  Nova  Configurar  Fechar
Provider · modalidade · Local/Externo                 disponibilidade
─────────────────────────────────────────────────────────────────────
                        CONVERSA
Você: mensagem
Agente: resposta
Ferramenta / proposta / aprovação inline quando aplicável
Progresso discreto associado à resposta em andamento
                                           rolagem independente
─────────────────────────────────────────────────────────────────────
Contexto desta mensagem: [Arquivo ativo ×] [Banco ×] [+N / expandir]
┌ Mensagem multilinha ──────────────────────────────────────────────┐
│ Descreva o que precisa…                                           │
└──────────────────────────────────────────────────────────────────┘
Anexar…  Modo  Modelo       Permissões…             Enviar / Cancelar
```

O desenho é conceitual. Na largura mínima, a barra de ações quebra em duas linhas planejadas; não comprimir todos os seletores e botões numa única linha.

### Hierarquia e distribuição de espaço

- Manter cabeçalho, histórico flexível e compositor fixo como três regiões. O histórico recebe o espaço restante, com rolagem própria e sem rolagem global do painel.
- Resumir provider, modalidade e destino Local/Externo em uma linha compacta, com seleção acessível pelo resumo. Manter nome completo disponível por teclado e tecnologia assistiva; tooltip não é o único acesso.
- Mover modo e modelo para a barra do compositor. Usar o resumo no estado fechado e seletor/menu acessível ao ativar, evitando controles largos permanentemente visíveis.
- Estado normal/streaming usa linha compacta de até duas linhas, sem cartão alto vazio. Falhas com ação e bloqueios recebem destaque proporcional ao conteúdo.
- Avisos informativos recorrentes de workspace ficam em resumo expansível com acesso explícito ao detalhe. Bloqueios, mudança de pasta da sessão, perda de contexto e falhas de persistência permanecem visíveis. Aprovações pendentes nunca ficam escondidas em um menu.
- Reutilizar hospedagem vigente: painel inicial 380, mínimo 320, máximo 560; área de abas preservada em pelo menos 690 e superfície própria quando não houver espaço. Aumentar a largura não substitui reduzir o excesso vertical.

### Conversa e mensagens

- Distinguir usuário, agente, ferramenta e aviso por rótulos, espaço e superfícies semânticas. Evitar borda forte em toda mensagem; reservar contornos destacados para foco, aprovação e erro.
- Respostas usam a largura disponível, texto selecionável e quebra de linha. Código preserva monoespaçado e conteúdo integral, com rolagem local quando necessário. Formatação adicional deve reutilizar recursos existentes; não pressupõe adicionar biblioteca de Markdown.
- Marcar a resposta em streaming junto da própria mensagem; evitar repetir o mesmo estado em um cartão grande acima da conversa. Manter anúncio acessível de estado sem anunciar cada token.
- Autoacompanhar o stream apenas se o leitor estiver no fim. Ao ler mensagens anteriores, oferecer “Ir para a última mensagem” sem mudar foco.
- Ferramentas concluídas podem resumir detalhes com expansão; aprovações mostram operação, alvo, risco, argumentos completos acessíveis e decisões explícitas antes da execução. Propostas conservam Revisar/Aplicar/Descartar ou Manter/Reverter conforme contrato vigente.
- Avisos de retomada ou troca de provider identificam sua origem e posição cronológica. Não reescrever nem apagar registros para melhorar a aparência.

### Compositor, contexto e controles

- Uma superfície visual reúne contexto, entrada e ações. Chips ficam imediatamente acima da entrada, com remoção individual e indicador de itens adicionais expansível; o detalhe permite revisar todos os dados anexados antes do envio.
- Substituir os dois botões longos de arquivo por **Anexar…**, com opções **Arquivo do workspace…** e **Arquivo externo…**; preservar filtros, limites, exclusões e motivos de indisponibilidade.
- Oferecer um único acesso textual **Permissões…** na barra; bloqueio por consentimento mantém sua explicação e ação necessárias.
- Entrada inicialmente entre 72 e 96 unidades lógicas, crescimento limitado a 160 e rolagem interna após esse limite. Placeholder curto; ajuda **Enter: nova linha · Ctrl+Enter: enviar** fora do texto editável.
- Controles interativos têm altura coerente de 32, com espaçamento 4/8/12 e cantos de 4. Ícones precisam de nome acessível, dica e foco visível; rótulos longos não podem sobrepor controles vizinhos.
- **Enviar** é a ação primária quando permitido. Durante execução, **Cancelar execução** permanece claramente acessível; envio obedece ao bloqueio atual, sem criar fila ou nova semântica de cancelamento.
- Reutilizar tokens e tipografia do design system; retirar fundos de alto contraste sem papel semântico, preservando contraste de texto 4,5:1 e limites/foco necessários 3:1.

## Critérios de aceite

| ID | Aceite verificável |
| --- | --- |
| UX-AI-01 | Em painel 380 × 820, 100%, pt-BR, conversa normal/streaming, dois chips e entrada inicial, histórico ocupa pelo menos 55% da altura interna útil. Medir bounds do controle, não estimar pela captura. Exceções por aprovação/bloqueio ficam documentadas |
| UX-AI-02 | Nas larguras 320/380/560 e janela mínima 960 × 620, compositor e ações permanecem acessíveis, sem sobreposição, corte ou rolagem horizontal global. Não impor 55% à janela mínima ou aos estados excepcionais |
| UX-AI-03 | Estado normal não contém bloco vazio de status; “gerando” tem indicação compacta vinculada à resposta. Mensagens, avisos e ferramentas são identificáveis sem depender de cor |
| UX-AI-04 | Provider, modalidade, modelo e modo são consultáveis e alteráveis por teclado; destino externo permanece explícito; nenhuma seleção longa depende apenas de tooltip |
| UX-AI-05 | Chips próximos da entrada permitem inspecionar/remover cada item; remover Arquivo ativo mantém o gate de `active_buffer`; expansão de contexto não altera o snapshot nem envia dados |
| UX-AI-06 | Avisos críticos e ações de aprovação/cancelamento são encontráveis sem abrir configurações. Argumentos longos continuam integralmente revisáveis e confirmação mantém foco inicial seguro |
| UX-AI-07 | Texto longo, código, múltiplos anexos e todos os idiomas suportados não quebram o layout em claro/escuro e escalas 100/150/200% |
| UX-AI-08 | Streaming não toma foco nem arrasta quem lê o histórico. Enter, Ctrl+Enter, Escape, Ctrl+Shift+A e retorno ao editor preservam o comportamento documentado |
| UX-AI-09 | Resultado/proposta/erro assíncrono continua na conversa/aba de origem após trocar seleção ou conversa; falhas de gravação e histórico ilegível seguem visíveis e sem sobrescrita |

## Entregas em ordem

1. **P7-UX-01 — composição:** protótipo no Avalonia com dados sintéticos; medir espaço de histórico, reordenar regiões e revisar largura mínima antes de alterar comportamento.
2. **P7-UX-02 — conversa:** hierarquia de mensagens, progresso compacto, ferramentas/avisos e acompanhamento de streaming sem mudança indevida de foco.
3. **P7-UX-03 — compositor:** agrupar chips e ações, menu Anexar, modo/modelo compactos, altura da entrada e quebra planejada da barra.
4. **P7-UX-04 — estados e acessibilidade:** falhas, bloqueios, aprovação, propostas, nomes acessíveis, foco e idiomas. Integrar estados da meta P7-AUTH quando disponíveis, sem duplicar inicialização/conta.
5. **P7-UX-05 — validação e documentação:** gerar/inspecionar PNGs reais, corrigir problemas, executar checks e registrar evidências e limites.

## Matriz de validação e homologação

Reaproveitar `AgentChatUiTests`, `AgentChatHostUiTests` e `AgentCliAccountUiTests` em `tests/EsilvaSoft.KapibaraStudio.IntegrationTests`; revisar o que cada fixture realmente cobre antes de estender.

- Renderizar painel nas larguras 320/380/560, hospedagem em 960 × 620, 1366 × 768 e 1920 × 1080; temas claro/escuro, escalas 100/150/200%, pt-BR/en/es/zh-CN. Inspecionar PNGs gerados, com atenção ao caso próximo da captura do usuário.
- Estados: vazio, pronto, gerando, erro, conta ausente/expirada, sem consentimento, sem workspace, contexto expandido/muitos anexos, aprovação pendente/expirada/negada, proposta longa, sessão perdida e falha de persistência/histórico ilegível. Priorizar combinações extremas, registrar quais foram efetivamente executadas.
- Verificar proporção do histórico por bounds e jornadas por teclado; não alterar golden files ou asserções para esconder regressões.
- Se a reorganização mudar sessão/contexto, testar falhas, concorrência e recuperação: troca de conversa durante streaming, aba/destino alterados após captura, remoção de chip, cancelamento independente e persistência negada/ilegível.
- Executar `dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode`, `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore` e `dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore`. Se telemetria Avalonia estiver bloqueada, usar `-p:UsedAvaloniaProducts=` e registrar o motivo.
- Homologar teclado, leitor de tela, IME e diálogos nativos em Windows/Linux separadamente. Headless não comprova esses cenários nem chamadas reais Claude/Copilot/MongoDB.

## Contratos e limites

Referências: [design system](../../17-design-system-ui-ux.md), [catálogo funcional](../../03-catalogo-funcional.md), [ADRs vigentes](../../10-decisoes-arquiteturais.md), [matriz de validação](../../15-matriz-de-validacao.md), [Fase 5](README.md) e [P7-AUTH](meta-inicializacao-autenticacao-agentes.md).

Aplicar ADR-056 com revisões vigentes, ADR-057/059 para providers/permissões e ADR-060 para fronteiras de sistema. Esta proposta altera apresentação; não cria provider, autenticação, acesso nativo, autorização ou persistência paralelos. Login oficial permanece oficial; sem leitura de credenciais, tokens, endpoint privado ou fallback silencioso para API.

Explorer apenas navega; capturar contexto antes de awaits; resultado só atualiza sua origem; preservar cancelamento isolado, auditoria, Extended JSON/BSON, UUIDs, confirmações, grants e limites do runtime. Não abrir outra conexão LiteDB. Opt-outs e proteção de sessão ilegível continuam obrigatórios. Cancelamento não promete rollback.

Design system, guia, catálogo, plano, acompanhamento, matriz e Fase 5 foram atualizados. A nota na ADR-056 registra o refinamento de apresentação sem mudar contratos. Esta meta conclui a melhoria e sua validação automatizada; não encerra gates de providers da Fase 5 nem declara homologação nativa.

## Entrega e evidências — 30/09/2026

P7-UX-01/02/03 implementadas; P7-UX-04 implementada e exercitada com doubles; P7-UX-05 concluiu checks e inspeção de amostras renderizadas, mantendo homologação nativa separada. O desenho acima registra a intenção original; o compositor final dispõe contexto, modo/modelo, entrada, ajuda e ações em linhas distintas.

| Aceite | Evidência automatizada | Limite |
| --- | --- | --- |
| UX-AI-01 | Bounds 380 × 820: 491/804 = 61,07% genérico; 445/804 = 55,35% CLI simulado, claro/escuro | Referência com contexto recolhido; estados excepcionais não usam esta proporção |
| UX-AI-02/07 | 144 PNGs: dois providers × quatro idiomas × dois temas × 320/380/560 × 100/150/200%; hosting e overflow com 13 chips/entrada longa | Amostras inspecionadas; dimensões e ações verificadas automaticamente. Não equivale a todas as combinações nativas |
| UX-AI-03 | Progresso junto da resposta, superfícies distintas, aviso com hora UTC, texto/código inertemente renderizados | Parser aceita apenas cercas de código; não adiciona interpretador HTML/Markdown |
| UX-AI-04/06/08 | Fixtures existentes de foco/atalhos/conta/aprovação/proposta; retorno explícito à última mensagem; streaming preserva foco e posição | Leitor de tela, teclado nativo, IME e seletores de arquivo reais pendentes |
| UX-AI-05/09 | Remoção e contagem do contexto; snapshot existente preservado; aviso de retomada usa provider capturado e permanece na origem; falha de gravação visível durante streaming | Persistência e origem verificadas com doubles; não constitui homologação dos serviços reais |

Restore locked aprovado. Build solução com `--no-restore -p:UsedAvaloniaProducts=` aprovado, **0 avisos/erros**; parâmetro contorna apenas telemetria externa Avalonia, mantendo analisadores. Suíte completa `dotnet test ... --no-build --no-restore --logger trx --results-directory TestResults/P7UX/Final`: **41 benchmarks + 198 Agents + 3.343 unitários + 884 integração = 4.466 aprovados; 23 ignorados; 0 falhas**. Casos Explicit com runtimes/credenciais/servidores reais não foram executados.

Os TRXs por assembly estão em `TestResults/P7UX/Final`; PNGs originais em `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/bin/Debug/net10.0/ui-evidence`. Seleção inspecionada e preservada em `TestResults/P7UX/preview`: painel pt-BR, host 1366 × 768, idiomas em largura mínima e escala 200%, contexto expandido, código e falha de persistência. Não foram alterados golden files para esconder regressões.

Pendências de homologação: leitor de tela e contraste instrumental, teclado/IME e diálogos nativos em Windows/Linux, CLI autenticado Claude/Copilot e MongoDB real. Os gates existentes de P7-AUTH e dos providers permanecem abertos segundo suas evidências próprias.

## Revisão após captura do usuário — 30/09/2026

A captura em uso revelou um problema não coberto pela primeira matriz: abrir o provider exigia Flyout e depois ComboBox, gerando popups sobrepostos e uma lista excessivamente larga. O cabeçalho agora usa um único ComboBox direto; cada opção mostra nome e uma linha secundária com modalidade, destino e disponibilidade, sem repetir o nome. A lista mantém largura limitada, quebra nomes longos e permite seleção por teclado. O nome completo permanece no texto acessível do seletor.

Detalhes comuns de contexto/leitura perderam contornos redundantes; bloqueios continuam destacados. Chips de contexto têm remoção compacta de 24 unidades e padding reduzido, mantendo erro e descrição acessíveis. A expansão manual continua disponível sem modificar permissões ou o snapshot.

Build Desktop/dependências/IntegrationTests: 0 avisos/erros. **22/22 testes de painel, hosting e conta aprovados** após a revisão; o teste novo abre a lista, mede sua largura, renderiza PNG claro/escuro e seleciona por teclado sem enviar turno. TRX em `TestResults/P7UX/Revision/p7-ux-review.trx`; PNGs em `TestResults/P7UX/RevisionArtifacts`. A suíte completa acima pertence à entrega anterior. Como o aplicativo estava aberto e bloqueava seus binaries, esta revisão foi compilada com saída separada por projeto; não se encerrou a sessão do usuário. Reiniciar/recompilar o aplicativo fechado é necessário para carregar a nova versão. Homologação nativa continua pendente.
