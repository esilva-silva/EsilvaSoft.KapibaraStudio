# Meta P7-UX2 — Autoscroll, diferenças e métricas do Agente IA

Data: **03/10/2026**. Estado: **implementada no checkout; validação automatizada Windows aprovada; homologações restantes registradas**.

## Resultado esperado

O painel acompanha novas mensagens e respostas em streaming quando o leitor está no fim; apresenta adições em verde e remoções em vermelho; destaca a aplicação de propostas com verde suave; permite consultar o tamanho do contexto e o consumo informado pelo provider. Preservar a identidade do EsilvaSoft.KapibaraStudio, desktop .NET 10/Avalonia, Windows/Linux e licença MIT.

Esta meta orientou alterações de UI, Core, Application, adapters e documentação; não alterou autenticação nem fez chamadas a MongoDB ou inferência real. A captura do usuário é referência visual, não evidência de uma causa de falha em execução. A prioridade geral entre fases permanece conforme o roadmap.

## Implementação e validação em 03/10/2026

UX2-01..05 estão implementadas. A rolagem registra offset e acompanhamento em memória por conversa, calcula se o leitor estava no fim antes das alterações de layout e reavalia depois do relayout. Contagens +/− são separadas, localizadas e acompanhadas de nomes acessíveis; ações de proposta usam recursos semânticos dinâmicos. A prévia mostra bytes UTF-8 dos itens resolvidos em memória e marca arquivos não resolvidos como pendentes. O snapshot efetivamente enviado mantém medição imutável ligada à mensagem de origem, sem persistir conteúdo ou métricas. Eventos tipados são deduplicados por sessão/turno e adapters encaminham somente uso oficial suportado: Claude Code total por turno, OpenAI total por chamada e Copilot por `AssistantUsageEvent`. Uso, custo/cache e totais desconhecidos permanecem distintos; janela de contexto, cota e tokens estimados são declarados indisponíveis quando não há fonte compatível.

Restore locked e build da solução passaram. Windows: UnitTests **3.380 aprovados, 20 ignorados**; Infrastructure.Agents.Tests **288/288**; IntegrationTests **903 aprovados, 16 ignorados**; zero falhas. A cobertura inclui rolagem após layout/troca de conversa, captura UTF-8/redigida, snapshot isolado por mensagem, ausência/parcialidade/duplicação/revisão/overflow, isolamento de eventos e encaminhamento dos três adapters. A renderização Headless produziu PNGs nos idiomas pt-BR/en/es/zh-CN, temas claro/escuro, larguras de 320/380/560 e escalas de 100/150/200%; foram inspecionadas amostras pt-BR 380@100% claro/escuro e os flyouts de contexto e uso. O compositor foi compactado para preservar a prioridade visual do histórico.

O cálculo das cores semânticas de texto/fundo em base/hover/pressed resultou em contraste mínimo de 4,57:1. Não foram medidos via pixels todos os estados visuais, foco e limites; flyout via teclado, Windows nativo, leitor de tela/IME, CLI e conta oficial e runtime Linux não foram homologados. Esta entrega não afirma conclusão desses gates. Não há tokenizer compatível nesta integração; portanto nenhum valor de tokens é estimado a partir de bytes/caracteres.

## Diagnóstico do estado atual

| Área | Evidência no checkout | Consequência para a implementação |
| --- | --- | --- |
| Rolagem | `AgentChatPanel.axaml.cs` já usa `_stickToBottom`, tolerância de 8 unidades, `ScrollChanged` e `LatestMessageButton` | Corrigir e ampliar a lógica existente; reproduzir primeiro a perda de acompanhamento. O ramo de acompanhamento depende de crescimento da extensão sem mudança simultânea do offset, condição que merece cobertura de layout e virtualização |
| Teste existente | `AgentChatUiTests.StreamingCodePreservesReaderPositionAndExplicitReturnToLatest` cobre leitura no início e retorno explícito | Acrescentar casos em que o leitor já está no fim, criação de mensagens e troca de conversa; o teste existente não comprova todos esses cenários |
| Diferenças | `AgentChatProposalItem` expõe `AddedLines`, `RemovedLines`, `CountsText` e `CountsAccessibleText`; o template mostra uma string com estilo metadata | Separar a apresentação dos números para aplicar recursos semânticos, preservando a leitura acessível e contagens históricas |
| Aplicação | O cartão usa `ApplyAllCommand`, `CanApply` e os estados de revisão/aplicação; a janela de revisão oferece aplicação por hunk | Alterar apresentação, sem ampliar permissões, mudar o alvo ou habilitar proposta indisponível |
| Contexto | `AgentChatViewModel.Context.cs` resume apenas a quantidade de chips; `AgentTurnRequest` carrega mensagem, contexto autorizado, prompt e anexos resolvidos | Medir o conteúdo autorizado efetivo, distinguindo a prévia local do snapshot realmente enviado |
| Consumo | `AgentProviderEvent` e `AgentEvent` não têm payload tipado de uso; Claude mantém tokens de entrada/saída e custo em `ClaudeCodeTurnSummary` interno | Criar extensão aditiva dos contratos e encaminhamento pelo runtime; não acessar tipos de infraestrutura diretamente na UI |
| Copilot | `CopilotSubscriptionAgentSession` não encaminha métricas de uso para o contrato comum atual | Verificar eventos e campos oficiais da versão instalada/fixada antes de prometer tokens, janela ou cota |

Referências: [design system](../../17-design-system-ui-ux.md), [catálogo funcional](../../03-catalogo-funcional.md), [ADRs vigentes](../../10-decisoes-arquiteturais.md), [matriz de validação](../../15-matriz-de-validacao.md), [P7-UX](meta-ui-ux-painel-agente-ia.md) e [estado da Fase 7](README.md). A ADR-056 rege conversas globais ao workspace e snapshot por turno; registros anteriores de hospedagem por aba são históricos.

## Comportamento proposto

### 1. Acompanhamento da conversa

- No fim do histórico, acompanhar mensagem nova, crescimento do stream e cartões novos de ferramenta, proposta ou aprovação depois de o layout refletir o conteúdo.
- Usar a distância do fim anterior à atualização para decidir o acompanhamento. Mudanças de extensão, viewport ou offset causadas pelo layout não devem ser confundidas com o leitor rolando para cima.
- Quando o leitor subir, preservar posição, seleção e foco. Mostrar **Ir para a última mensagem**, com indicação textual de novas mensagens quando houver; clicar retoma o acompanhamento. Voltar manualmente ao fim também o retoma.
- Enviar uma mensagem própria leva ao fim da conversa de origem. Um evento de outra conversa não altera a rolagem da conversa visível.
- Manter posição e estado de acompanhamento por conversa em memória. Uma conversa aberta pela primeira vez mostra o fim após carregar; voltar a uma conversa já aberta restaura sua posição. Não adicionar persistência de posição nesta meta.
- Reassociar o ScrollViewer quando o template mudar e remover inscrições ao desacoplar a view. Agrupar atualizações por ciclo de layout/dispatcher, evitando scroll e reconstrução de controles a cada token.
- Não mover foco durante eventos recebidos, nem anunciar cada delta ao leitor de tela. A rolagem interna de código, argumentos e compositor continua independente.

### 2. Diferenças e ações com cores suaves

- Apresentar **+5 linhas** em verde e **−4 linhas** em vermelho, como trechos separados; manter sinais, rótulos e nome acessível completo. A cor nunca é a única informação.
- **Aplicar alterações/Aplicar pendentes** usa verde suave apenas quando habilitado. **Revisar alterações…** conserva azul suave para ação; **Descartar** permanece neutro. Usar a mesma hierarquia nas ações equivalentes da revisão por hunk.
- Proposta já aplicada, descartada, obsoleta ou histórica mantém seus estados e explicações. O botão desabilitado permanece visualmente desabilitado; verde não deve sugerir que está disponível ou que uma aplicação já ocorreu.
- Definir recursos dinâmicos em `App.axaml` para adição/remoção e para ações suaves de aplicação/revisão, incluindo foreground, background, border, hover e pressed. Reutilizar os papéis `SuccessBrush`, `ErrorBrush` e `AccentBrush`; evitar mudar globalmente a paleta de sucesso/erro do produto.
- Obter leveza principalmente com fundos discretos e menor saturação; escolher valores finais após medir contraste. Texto mínimo 4,5:1, foco/limites necessários 3:1, em claro/escuro e em normal/hover/pressed. Preservar foco Fluent e estados desabilitados.
- Não recolorir globalmente o editor ou o diff existente nesta meta; avaliar a consistência das cores de adição/remoção nas telas de revisão sem alterar sua lógica.

### 3. Tamanho do contexto da mensagem

Adicionar uma linha compacta junto do resumo **Contexto da mensagem**. Exemplo conceitual: **Contexto: 2 itens · 12,4 KiB**. Um botão **Detalhes do contexto** abre flyout; tooltip oferece apenas um resumo, sem ser o único acesso.

O detalhe identifica separadamente mensagem digitada, arquivo ativo, anexos autorizados, metadados e instruções do produto. Exibe bytes UTF-8 do conteúdo textual, quantidade de itens e tokens quando houver tokenizer compatível. Estimativas sempre recebem **≈ / estimado**, com método e limites no detalhe. Caracteres não são tokens; tamanho textual não é tamanho de transporte nem ocupação da janela inteira do modelo.

A prévia se atualiza ao editar mensagem/chips, buffer, modelo ou permissões, com debounce e descarte de respostas obsoletas. Usar buffer em memória e resolvedor limitado existente. Não reler arquivos externos a cada tecla nem iniciar CLI, inferência, rede ou MongoDB para calcular uma prévia. Conteúdo ainda não resolvido aparece como **Tamanho pendente**, não como zero. Falha de leitura/redação é explícita e não exige nova permissão só para mostrar o erro.

No envio, recomputar sobre o snapshot autorizado, resolvido e redigido realmente usado no turno, capturado antes de operações assíncronas conforme os contratos existentes. Associar esse tamanho à mensagem de origem, disponível no detalhe da mensagem enviada; alterações posteriores no editor não o modificam. Contexto mantido internamente pelo runtime, histórico remoto, instruções próprias do provider e resultados futuros de tools não entram no total local e devem ser declarados como não medidos.

### 4. Consumo atual e janela de contexto

Exibir uma ação compacta **Uso** próxima ao compositor; seu flyout mostra:

| Métrica | Regra de apresentação |
| --- | --- |
| Entrada e saída do último turno | Tokens oficiais quando encaminhados pelo adapter; atualizar durante streaming somente se houver evento oficial, senão ao término |
| Cache | Leituras/gravações de cache apenas quando presentes, com semântica validada por provider; evitar soma duplicada com entrada |
| Acumulado observado | Soma de turnos distintos com cobertura conhecida; informar **Nesta execução** ou **Parcial** se o histórico não tiver métricas |
| Janela de contexto | Ocupação e capacidade somente com fonte oficial e compatível com modelo/sessão. Tokens acumulados consumidos não medem ocupação da janela |
| Custo informado | Opcional, com moeda e origem; custo estimado pelo runtime não equivale a cobrança final da assinatura |
| Cota da conta | Somente por mecanismo público oficial que a integração suporte; ausência mostra **Não informado pelo provider**, sem inventar percentual ou saldo |

Identificar provider, modelo efetivamente informado, turno, origem, momento da atualização e cobertura completa/parcial. O total de uma mensagem pode envolver várias chamadas de modelo e tools; o adapter precisa declarar se recebeu delta, total por chamada ou total por turno.

Dados ausentes ficam nulos e textuais. Rejeitar negativos, overflow e valores inválidos. Eventos duplicados não duplicam totais; eventos atrasados ou fora de ordem ficam associados à sessão/turno/conversa originais. Cancelamento ou erro conserva uso já informado como parcial; não completar com zero. Compactação, retomada e troca de modelo/provider não reaproveitam uma capacidade ou percentual antigo.

Introduzir DTO neutro, tipado e aditivo em Core e encaminhamento explícito adapter → runtime → ViewModel. Validar formato e escopo no runtime; métricas não autorizam ferramentas, não encerram turnos e não alteram permissões. Claude reaproveita os dados já interpretados da CLI oficial. Copilot primeiro recebe levantamento e testes dos eventos oficiais da dependência atual. Providers sem suporte conservam o fluxo atual e mostram indisponibilidade explicada.

Métricas ficam em memória nesta primeira entrega. Ao reiniciar, indicar que o acumulado é parcial/nesta execução. Persistência futura exige proposta própria com DTO versionado, migração aditiva no proprietário LiteDB registrado em DI e respeito ao opt-out; não persistir payloads, anexos, resultados ou credenciais para reconstruir contagens.

## Entregas na ordem de implementação

| ID | Entrega | Critério de aceite | Estado |
| --- | --- | --- | --- |
| UX2-01 | Reproduzir e corrigir autoscroll | Mensagem nova e stream seguem o fim após layout; leitura anterior e troca de conversa preservadas | Implementada; Headless aprovada |
| UX2-02 | Contagens e ações suaves | + verde/− vermelho; aplicar verde, revisar azul; contraste, foco e indisponibilidade preservados | Implementada; Headless e localização aprovados; contraste estático mínimo 4,57:1; inspeção visual parcial |
| UX2-03 | Métricas locais de contexto | Prévia e snapshot enviado distinguíveis; bytes corretos após redação; sem leitura/rede extra nem conteúdo persistido | Implementada; unitário/Headless aprovado; tokenizer indisponível |
| UX2-04 | Contrato e adapters de uso | Payload tipado opcional, roteamento isolado e deduplicação; suporte oficial de cada provider documentado | Implementada para Claude Code, OpenAI e Copilot; testes passaram; restante indisponível |
| UX2-05 | Flyouts e detalhes por mensagem | Contexto e uso consultáveis por mouse/teclado, sem ocultar compositor ou roubar foco | Implementada; mouse/renderização Headless inspecionados; teclado/nativo pendente |
| UX2-06 | Validação e documentação final | Checks proporcionais aprovados, PNGs reais inspecionados, suporte e limites registrados por provider/SO | Geração e amostras PNG aprovadas; contraste integral e homologações nativas pendentes |

Implementação seguida na ordem de rolagem/cores e, depois, contexto/uso. Métricas permanecem condicionadas aos eventos oficiais efetivamente entregues por cada provider.

## Validação obrigatória

- Rolagem Headless com ListBox real: histórico curto passando a longo, várias mensagens/cartões, stream longo, mudanças simultâneas de extensão/offset, redimensionamento, leitura anterior, retorno ao fim, troca de conversa, view desacoplada/reaberta e template reassociado. Verificar viewport após layout, foco e seleção, sem sleeps fixos nem testes que apenas copiem a condição interna.
- Contexto: UTF-8 com acentos/CJK, buffer não salvo, remoção de chips, redação, opt-outs, limites existentes por arquivo/mensagem, arquivo ausente/alterado, erro no resolvedor e mudança concorrente de aba/conversa/modelo durante cálculo/envio. O tamanho exibido após enviar corresponde ao snapshot da origem.
- Uso: provider sem dados, zero válido versus ausente, cache, múltiplas chamadas, duplicação, ordem invertida, evento atrasado, cancelamento, erro, overflow, retomada e sessões/conversas simultâneas. Testar encaminhamento completo do adapter ao painel com fixtures sintéticas.
- PNGs reais nos dois temas, janelas 960×620, 1366×768 e 1920×1080, escalas 100/150/200%, painel estreito, flyouts abertos, textos longos, proposta disponível/desabilitada/obsoleta, streaming e métricas parciais. Inspecionar as imagens e medir contraste; não alterar golden files para esconder regressões.
- Localizar rótulos, dicas e nomes acessíveis em pt-BR/en/es/zh-CN. Flyouts abrem por teclado, Escape fecha apenas o detalhe, Tab preserva percurso e alvos mínimos de 28. Confirmar Windows manualmente; leitor de tela, diálogos nativos e runtime oficial permanecem pendentes até evidência específica. Linux respeita ADR-060 e o escopo atual: revisão/build/testes unitários, sem ampliar para integração nativa.
- Executar `dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode`, `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore` e `dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore`. Usar `-p:UsedAvaloniaProducts=` somente se o ambiente bloquear essa tarefa externa. Não habilitar benchmarks no CI/release. Testes com CLI/conta oficial continuam explícitos e fora do CI.

## Limites e conclusão

Não há novo provider, autenticação alternativa, captura de tokens, leitura de credenciais, endpoint privado ou fallback para API. A aplicação continua no buffer correto, com revisão, detecção de conflito, Undo, proteções e auditoria existentes. Explorer não executa consultas nem redireciona resultados.

Os incrementos funcionais UX2-01..05 e os gates automatizados Windows estão concluídos neste checkout; UX2-06 segue parcial até completar inspeção de contraste/estados e homologações pendentes. A UI não fecha os gates de MongoDB real, autenticação, acessibilidade nativa ou homologação comercial. Este documento registra a implementação e seus limites, sem declarar homologação universal.
