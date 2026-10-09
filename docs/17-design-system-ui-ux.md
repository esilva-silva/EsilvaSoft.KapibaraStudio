# Design system e revisão de UI/UX

**Fase 7A — apresentação parcialmente implementada em 09/10/2026 (ADR-070):** [meta KapiCoder-Mongo](phases/phase-07a-kapicoder-mongo/meta-de-implementacao.md) adiciona controles de orçamento e habilitação de raciocínio disponíveis conforme capacidade local, além de item recolhível transitório, nos quatro idiomas. O conteúdo permanece inerte; retenção local é opt-in, desligada por padrão, limitada a 1/7/30 dias e vedada em conversas que solicitam dados/diagnósticos capturados. Inspeção de PNGs claro/escuro e validação dos testes de retenção seguem pendentes; a composição Release agora inclui IA local e Copilot.

**F6-12/13/14 — diagnóstico de índices e histórico administrativo — 08/10/2026:** botões de uso/build na aba Índices abrem relatórios somente leitura com fonte/instante de `$indexStats`, tamanho de índices e fotografia filtrada de `currentOp`, além de sugestões conservadoras de prefixos direcionais. Histórico administrativo fica limitado em memória. Falhas por fonte ficam visíveis; a heurística não analisa workload nem remove índices. Os resultados são publicados apenas para o alvo capturado. PNGs de Índices foram inspecionados em ambos os temas; Mongo local confirmou leitura de tamanho/atividade. Build concorrente em replica set segue pendente.

**F6 — layout responsivo da janela Ferramentas — 08/10/2026:** a janela mantém rolagem vertical no conteúdo e remove a rolagem horizontal externa. As faixas extensas de criação/edição em Coleções e Administração quebram em novas linhas; Índices organiza formulário e resultado verticalmente. Capturas Headless 1100×740 em Light/Dark foram inspecionadas, inclusive início e fim rolado da aba Administração. Os PNGs comprovam layout/renderização, não operação nativa, leitor de tela ou jornada visual reativada.

**F6-24 — progresso de exportação — 08/10/2026:** a aba Transferir apresenta estado localizado e barra por coleção; a contagem atualiza em intervalos limitados e o estado concluído só aparece depois da publicação do manifesto. O botão Exportar fica desabilitado enquanto há uma exportação; o callback vincula progresso e resultado à identidade da execução e ao perfil/geração/banco que a iniciou. PNGs Headless Light/Dark com progresso e botão desabilitado foram inspecionados; a captura estática comprova disposição e leitura, não uma exportação nativa sob carga.

**Agentes em Release — 08/10/2026:** por decisão do usuário, o Desktop compilado em Release oferece somente GitHub Copilot no painel Agente IA e em Configurar agentes. O catálogo e o runtime recebem apenas esse provider; handlers de conta Codex e slots de API Key também ficam fora dessa composição. Debug conserva IA local, OpenAI API, Codex e Claude Code conforme os gates existentes. Autocomplete e infraestrutura de IA local permanecem independentes; a prioridade da Fase 8 e o backlog das demais integrações não mudam. Registros anteriores de disponibilidade são históricos para Release. Evidências e limites: [release-copilot](architecture/release-copilot.md).

**Criação de arquivos por agentes — 07/10/2026 (ADR-067):** `create_workspace_file` grava conteúdo UTF-8 em arquivo novo da workspace, sem sobrescrita, com opt-in de escrita e confirmação pontual obrigatória. Editar arquivos existentes continua usando propostas; consultas diretas/escritas MongoDB permanecem removidas. [Contrato, uso e validação](architecture/workspace-file-creation.md).

**Tools seguras — 07/10/2026 (política vigente; ADR-066):** Permissões substituem leitura direta por envio de resultados, erros e logs de consultas já executadas na aba; consentimento e confirmação de valores continuam separados e desligados por padrão. A lista mostra `get_search_indexes`, `get_query_results` e `get_query_diagnostics`, sem queries/escritas antigas. Preservar rolagem local, quebra de texto, categorias semânticas e localização pt-BR/en/es/zh-CN. Captura pertence à aba/execução originadora, sem redirecionamento ao mudar a seleção. [Contratos, compatibilidade e validação](architecture/agent-tool-safety.md).

**Inspeção visual do catálogo seguro — 07/10/2026:** PNGs Headless reais pt-BR (Copilot claro/Claude escuro, 660×760) e catálogo zh-CN escuro (960×760, escala 2) conferidos: rótulo de resultados/erros/logs legível, opt-in desligado, nomes com quebra de linha e rolagem/rodapé preservados. [Frames, matriz gerada e limites da inspeção](architecture/agent-tool-safety.md#inspeção-visual). Diálogo nativo e leitor de tela permanecem separados.

**GH-20 — Copilot com histórico desativado — 07/10/2026:** **Não guardar histórico** permite enviar o próximo turno pelo armazenamento volátil do provider. Uma mudança dessa opção substitui a sessão Copilot antes do próximo envio, mantendo a conversa visível e a referência do histórico anterior. Histórico ativo continua exigindo reserva durável antes de create/envio. IDs voláteis ficam somente em rastreamento transiente para a exclusão explícita; não passam para reserva/retomada durável, e IDs duráveis não são enviados à inicialização volátil. Falha na exclusão conserva ambas as referências para retry. A opção não apaga histórico anterior nem dados remotos. Não há novos rótulos/layout; [regressões e limites](phases/phase-05-v0.9.0/meta-de-implementacao.md#gh-20--envio-copilot-com-histórico-desativado--07102026) distinguem doubles da prova de retenção no runtime oficial.

**GH-12 — feedback de anexo pertence à conversa de origem — 07/10/2026:** uma leitura iniciada antes de trocar de conversa não deve marcar o chip do rascunho novo com erro, consumi-lo nem colocar essa conversa em **Gerando**. O envio conserva a identidade dos chips e a atualização de estado segue o turno/conversa capturados; ao retornar a uma conversa com turno ativo, o estado visível é restaurado a partir daquela conversa. Layout, textos e atalhos permanecem; [repro, regressão 2/2 e compilação](phases/phase-05-v0.9.0/meta-de-implementacao.md#gh-12--preparação-de-anexos-após-troca-de-conversa--07102026). Inspeção nativa/PNG não foi refeita.

**GH-11 — término do runtime durante resposta — 06/10/2026:** shutdown SDK recebido antes do idle encerra o turno com o diagnóstico localizado existente `CopilotSessionStreamFailed`, sem expor motivo bruto. Após envio, o relato conserva efeito possível; um novo envio do usuário inicia outro turno sem continuar trabalho pendente automaticamente. O evento tardio após idle não reabre a conversa nem converte a conclusão em erro. [Repro e cobertura 29/29](phases/phase-05-v0.9.0/meta-de-implementacao.md#gh-11--término-sdk-durante-streaming--06102026). Não altera layout ou rótulos; inspeção/recuperação com CLI e UI nativas ainda pendentes.

**GH-16 — diagnóstico de confirmação — 06/10/2026:** falha/exceção da ponte de confirmação Copilot utiliza o estado localizado existente `ConfirmationUnavailable`; expiração fica reservada ao prazo efetivamente vencido. Porta ausente e falha da ponte negam acesso sem alegar rejeição humana. Layout, rótulos e ações existentes são preservados. [Repro, cobertura 11/11 e limites](phases/phase-05-v0.9.0/meta-de-implementacao.md#gh-16--falha-da-ponte-sem-falsa-expiração--06102026); não há nova inspeção nativa.

**Orientação sobre saídas Copilot — 06/10/2026:** o flyout existente **Dados e uso** inclui texto localizado em pt-BR/en/es/zh-CN orientando revisão humana de segurança e licenças de terceiros e execução de testes pertinentes antes de incorporar conteúdo gerado ao projeto. O texto usa o estilo metadata, quebra de linha e rolagem local. O conteúdo tem largura 280 e reserva lateral de 12 para a barra vertical, evitando a rolagem horizontal externa e o corte observado no painel mínimo. Teste Headless: 2/2; 32 PNGs de início/fim da rolagem, quatro idiomas × dois temas × 380×820@100%/320×620@200%, inspecionados com orientação e links completos ao rolar. [Comando e evidências](phases/phase-05-v0.9.0/meta-de-implementacao.md#gh-28--orientação-e-renderização-de-dados-e-uso--06102026). Acessibilidade nativa, matriz visual completa e revisão de marca/release continuam pendentes.

**Campo de cota Copilot — 06/10/2026:** na seção **Tools do KapibaraStudio** da janela de permissões, mostrar **Máximo de chamadas de ferramentas por mensagem**, com padrão 100 e placeholder **Sem limite** quando vazio. Orientação de vazio e efeito em novas mensagens fica abaixo do campo. Erro de inteiro positivo usa `ErrorBrush`, anúncio acessível e desabilita salvar. O status do footer ocupa uma linha própria acima das ações, com quebra de texto, para preservar botões em janela mínima. Campo, orientação e erro acompanham a rolagem central; rótulo e ajuda têm nomes acessíveis. [Matriz e evidências](phases/phase-05-v0.9.0/meta-permissoes-tools-copilot.md#limite-configurável-solicitado-pelo-usuário--06102026).

**Feedback de limite configurado das tools — 06/10/2026:** somente quando o máximo configurado para a mensagem for atingido, o cartão `ToolCallLimitExceeded` permanece negado e mostra orientação localizada para continuar em nova mensagem. O padrão atual é 100; o campo vazio não limita a quantidade. O teto fixo anterior de 20 foi removido intencionalmente. O cartão não oferece `Permissões…` como solução para limite atingido. Preservar quebra de linha nos quatro idiomas e na matriz 320/380/560 a 100/150/200%, nos dois temas. [Investigação e evidências históricas](phases/phase-05-v0.9.0/meta-permissoes-tools-copilot.md#retomada--06102026); nenhuma nova homologação nativa.

**F5-COP-PERM — 05/10/2026:** no Copilot, o resumo de workspace usa a pasta permitida para as tools do produto; ausência de ferramentas nativas da CLI não indica pasta ausente. Cartões com `ExecutionFailed` mostram falha de execução e não oferecem revisão de permissões como solução; `NotFound` informa arquivo base ausente e o limite de propostas a arquivos existentes/buffer ativo. Os rótulos dos cartões atualizam ao trocar de idioma. Layout, cores e atalhos permanecem; [meta e evidências](phases/phase-05-v0.9.0/meta-permissoes-tools-copilot.md). Homologação na aplicação fica com o usuário.

**Aceite Copilot complementado — 03/10/2026:** a [meta da Fase 5](phases/phase-05-v0.9.0/meta-de-implementacao.md) detalha estados de conta/limite/contexto, 14 tools liberáveis, recusas, confirmação pontual, modos, propostas/Undo e evidência visual/nativa. GH-29 e V9 exigem inspeção de PNGs reais e validação nativa separada; este complemento é planejamento e não altera apresentação ou atalhos.

**Footer da revisão de propostas — 05/10/2026:** na janela mínima, mantenha o status do resultado em linha própria acima das ações; não esprema a mensagem entre botões. Hunks longos permanecem na rolagem central e os controles de aplicar/reverter/fechar ficam visíveis e alcançáveis. Foi conferido em Headless, não em janela nativa.

**Rótulos de confirmação — 04/10/2026:** textos longos de categoria devem quebrar linha nos cartões inline, mantendo visíveis a entrada rolável e as ações humanas. A matriz Headless validou o prompt Copilot em 320@200% e 560@100%, temas claro/escuro; isso não substitui teste nativo de acessibilidade.

**Divulgação Copilot “Dados e uso” — 03/10/2026:** o composer exibe um botão contextual antes do envio quando GitHub Copilot está selecionado. O flyout explica envio potencial de prompt/anexos/contexto/histórico/resultados de tools, variação de processamento e retenção por plano/modelo/conta/org, limites/créditos e alcance restrito dos opt-outs locais; inclui link para políticas e configurações oficiais. Teste Headless gera PNGs em pt-BR/en/es/zh-CN e temas claro/escuro; os oito arquivos foram inspecionados. Isso não substitui inspeção de contraste em matriz ampla nem teste nativo de foco, teclado, IME e leitor de tela.

**Links do disclosure verificados — 04/10/2026:** o flyout anterior ao envio agora separa os links oficiais de uso/AI credits e acesso a modelos/policies organizacionais; ambos aparecem localizados em quatro idiomas. O container mantém rolagem e inspeção confirmou oito PNGs claros/escuros sem corte. As fontes oficiais dizem que acesso a modelos varia por plano, superfície cliente e policy organizacional. Foco, navegação por teclado e acessibilidade nativa continuam pendentes.

**Recorte da Fase 5 — 03/10/2026:** GitHub Copilot e infraestrutura compartilhada necessária à sua conclusão permanecem na Fase 5 / v0.9.0. Claude, Codex, APIs externas e integração de clientes MCP externos passam ao [bkl-06](backlog/bkl-06-integracoes-agentes.md), sem fase/versão comprometida. Código, disponibilidade, permissões e gates existentes são preservados; IA local e workflow mantêm suas fases. Esta alteração é documental e não comprova conclusão funcional ou nova homologação.

**P7-UX2 — implementado e validado automaticamente em 03/10/2026:** [meta de autoscroll, diferenças e métricas](phases/phase-05-v0.9.0/meta-autoscroll-contexto-consumo-agente.md). A conversa acompanha o fim após layout e preserva posição/estado por conversa; +/− têm papéis verdes/vermelhos e Aplicar/Revisar têm recursos dinâmicos semânticos. Resumos compactos e flyouts complementam os detalhes por mensagem. Build/testes Windows passaram; PNGs representativos claro/escuro e flyouts foram inspecionados. Inspeção extensa de combinações/contraste e homologação nativa ainda pendentes.

**Estabilidade automatizada — 01/10/2026:** autosave conserva o debounce de 750 ms e recebe relógio opcional para testes; a sugestão automática expõe sua tarefa de conclusão para observar publicação/rejeição sem pausas fixas em Headless. Não muda apresentação, tema, atalhos ou navegação. A medição de p95 da UI passa a ser manual `Explicit`, fora de CI/release; os casos funcionais permanecem automáticos. [Política e evidências](architecture/test-stability.md).

**Release/Copilot — 01/10/2026 (histórico):** **Entrar/Sair** usavam a CLI oficial interativa incluída no pacote. Este modelo foi substituído em 02/10 pelo CLI instalado pelo usuário para autenticação e sessões. A apresentação e os estados existentes são preservados. Auto update consulta somente o repositório KapibaraStudio. [Evidências e limites](architecture/release-copilot.md).

**Copilot instalado pelo usuário — 02/10/2026:** login e sessões do Agente IA usam a CLI oficial nativa encontrada no `PATH`; o produto não baixa nem empacota executáveis Copilot. Se não houver instalação nativa, a conta mostra como instalar; se o handshake detectar incompatibilidade, a sessão falha sem fallback. Windows exige executável nativo WinGet e Linux binário nativo oficial. A UI/fluxo visual não mudou. [Detalhes e pendências](architecture/release-copilot.md).

**Caminho explícito da CLI Copilot — implementado em 03/10/2026:** as configurações da conta Agente IA oferecem **Caminho da CLI**, **Salvar caminho** e **Usar detecção automática**. O caminho detectado é mostrado sem iniciar a CLI. Um caminho salvo tem prioridade sobre PATH e, se estiver ausente ou inválido, a checagem informa falha sem selecionar outra instalação; limpar o caminho salvo reativa detecção no perfil local e depois no PATH. A alteração vale para próximas operações, enquanto sessões já iniciadas mantêm o cliente atual. O valor é preferência opcional da instalação em `WorkspacePreferences`, separada do layout/conversa do painel. [Comportamento e validação](architecture/release-copilot.md#configuração-do-executável--revisão-de-03102026).

**Diagnóstico de instalação Copilot — 04/10/2026:** a checagem passiva distingue CLI ausente, caminho salvo inválido, shim/script/arquivo incompatível, falta de acesso/execução e falha de inspeção; mostra orientação pt-BR/en/es/zh-CN sem executar o candidato. Capturas reais das configurações confirmam 40 combinações quatro idiomas × dois temas × estados. A matriz não comprova execução oficial, foco/teclado/leitor de tela nativos nem requisitos reais do PowerShell.

**Licenças de terceiros — 02/10/2026:** o menu de ações da barra superior abre **Licenças de terceiros**, janela modal com o conteúdo integral do `THIRD-PARTY-NOTICES.md` que acompanha o app e renderização dos elementos Markdown usados no inventário: títulos, parágrafos, ênfase, links com endereço visível, listas, tabelas em cartões e blocos de código. O texto continua selecionável e rolável. A janela exibe também as pendências declaradas pelo inventário; sua presença não equivale a SBOM completa nem a parecer jurídico. Se o arquivo estiver ausente na publicação, a janela informa que o pacote pode estar incompleto.

**P7-LINUX — 30/09/2026:** [meta Copilot/Claude Linux](phases/phase-05-v0.9.0/meta-linux-copilot-claude.md). A prova do canal MCP pode ser lida pelo proxy no Secret Service sem prompt/unlock, mas o gate de ferramentas Claude Linux permanece fechado até aceite nativo. Isso não altera a política de conta Copilot: no Linux a consulta oficial continua explícita por poder envolver o cofre. Não há alteração de layout/tema/atalhos nesta rodada; teclado, diálogos e leitor de tela Linux seguem pendentes.
**Revisão P7-UX após uso — 30/09/2026:** seletor direto de provider com lista única limitada, nomes sem repetição e chips menores; contornos removidos dos detalhes comuns, preservando destaque de bloqueios. Build em saída isolada (aplicativo aberto): 0 avisos/erros; 22/22 testes UI/hosting/conta aprovados e PNGs da lista aberta inspecionados. [Evidências e limites](phases/phase-05-v0.9.0/meta-ui-ux-painel-agente-ia.md#revisão-após-captura-do-usuário--30092026).

**P7-UX — implementado em 30/09/2026:** [P7-UX — painel Agente IA](phases/phase-05-v0.9.0/meta-ui-ux-painel-agente-ia.md). Três regiões: cabeçalho compacto, histórico flexível e compositor delimitado. Contexto expansível junto da entrada; provider em menu, modelo/modo no compositor, Anexar unificado. Entrada 72–160 unidades; regiões secundárias usam rolagem local em combinações extremas, mantendo ações acessíveis e histórico mínimo de 96. Histórico ≥55% no painel de referência 380 × 820. Mensagens usam superfícies semânticas, prosa com entrelinha 20 e código monoespaçado 14/21 com rolagem local. Foco visível e nomes acessíveis preservados; homologação nativa pendente.

**P7-CLAUDE — painel via CLI oficial (02/10/2026):** no painel de permissões Claude, leituras de documentos Mongo têm consentimento separado, desligado por padrão, e ferramentas selecionáveis individualmente; as ferramentas nativas Claude continuam sob suas próprias permissões. A CLI oficial conserva seus métodos nativos; custo/cobrança depende do método efetivo. A API Anthropic fica fora desse caminho e não é fallback. Em sessões retomadas, a CLI reconstrói as instruções/contexto por turno; `EndConversation` é tratado como controle oficial quando qualquer ferramenta está disponível. Schema sampling continua sob o gate de consentimento local dedicado; não deve ser apresentado como operacional enquanto esse consentimento não estiver conectado à UI. Credenciais legadas no cofre não são lidas/apagadas e conversas antigas não migram automaticamente. A integração não tem homologação oficial/comercial.

**Proposta P7-AUTH — 30/09/2026:** [inicialização automática dos agentes](phases/phase-05-v0.9.0/meta-inicializacao-autenticacao-agentes.md) prevê Verificando → pronto/login necessário/falha no painel, sem bloquear a abertura. Entrar permanece explícito. Ainda não implementado; os estados visuais exigirão renderização e inspeção conforme este documento.

**Validação de propostas — 30/09/2026:** o store mantém preferência pelo buffer da aba capturada e recusa usar disco como substituto quando essa aba não responde. Para arquivo fechado, a leitura da base passa pelo adapter limitado, com tratamento de BOM preservado e proteção contra crescimento após metadados. A composição fornece reader/probe ao registry/chat. O picker recebe catálogo limitado por interface; uma nova carga limpa imediatamente arquivos e cache de busca anteriores, inclusive quando a nova pasta é recusada. Captura exclusões antes da operação assíncrona e descarta respostas antigas/canceladas. Apresentação e atalhos permanecem os mesmos; testes de ViewModel cobrem o comportamento, sem homologar diálogo nativo.

**Persistência de sessão — 30/09/2026:** o workspace filtra o snapshot antes de chamar o repositório: resultados não são persistidos, opt-outs geral/por conexão são respeitados e uma aba ativa excluída não deixa referência inválida. O adapter aplica a mesma política defensivamente. Falha de gravação continua visível e sessão ilegível continua protegida contra sobrescrita. A extração da política pura para `Application` não altera apresentação, navegação ou atalhos. [Meta de isolamento](architecture/system-adapters.md).

> **Estado atual do Claude — 02/10/2026:** as entradas de planejamento da Fase 5 abaixo são registros históricos; algumas citam arquivos que foram removidos. O painel Agente IA deve iniciar Claude somente pelo executável oficial Claude Code; a CLI conserva os métodos nativos de autenticação, e o custo depende do método escolhido. Não sugerir que a assinatura cobre todo uso. A API Anthropic permanece fora desse caminho e nunca é fallback. A configuração mostra instalação e estado sem dados sensíveis; não consulta nem apaga credenciais legadas do cofre, e conversas antigas não são migradas automaticamente. Aprovações ficam inline na conversa, destacam operação/alvo/risco e oferecem concessão de sessão apenas para leituras. O modo Automático continua exigindo aprovação individual para operações modificadoras. A distribuição segue sem aceite comercial da Anthropic; ver [Claude no KapibaraStudio — backlog](backlog/integracoes-agentes/historico-integracoes.md#plano-e-andamento).

**GitHub Copilot por assinatura (29/09/2026):** após checagem explícita de conta/modelos, o provider permite chat, turn plans e tool calling somente pelo registry do produto e com permissões/grants do turno. Além das leituras de metadados, cache e contexto e da proposta mediada `propose_file_edit`, sete ferramentas de leitura/diagnóstico MongoDB estão disponíveis com checkbox individual, consentimento separado para enviar resultados de documentos ou explain, conexão permitida e grant temporário. `mongo_explain` não retorna documentos, mas permanece sob o mesmo consentimento e confirmação conservadores porque o plano pode expor o filtro da consulta. Testes Desktop Headless cobrem o cartão de confirmação pontual, a captura do buffer não salvo da aba ativa e a proteção do `MongoTextEditor` contra proposta obsoleta. O teste `FakeCopilotRuntimeRegistersActiveBufferProposalInProductionDesktopStore` cobre a composição fake→`WorkspaceViewModel`→registry→store; `WindowsOfficialModelProposesEditIntoActiveWorkspaceViewModelStore` valida a mesma ponte com o runtime/modelo oficial e consulta sintética. Ainda faltam consultas pelo runtime oficial contra MongoDB de teste, aplicação manual e homologação da janela nativa/leitor de tela. `get_collection_schema` segue fechado até existir consentimento local dedicado para amostragem. Escritas MongoDB e `NativeTools` (shell, leitura/escrita nativa de arquivos e rede) continuam fechadas. `target: "active_buffer"` gera proposta revisável e aplica apenas no buffer após ação do usuário. `EditNotApplicable` é motivo de proposta não aplicável, não diagnóstico de falta de editor ou grant. Uma chamada negada por permissão abre a janela de permissões para revisão; salvar não repete a chamada. Essa janela altera permissões persistentes, não concede aprovação pontual ao turno já executado. Cada turno usa o snapshot de permissões capturado antes da execução: alterações e revogações valem para novos turnos e não interrompem uma operação já autorizada. A checagem de conta não envia prompt.

Os cartões de ferramenta negada por falta de grant mostram **Permissões…** para abrir a janela diretamente. Recusa ou expiração de confirmação pontual informa que a chamada não foi executada e não oferece a janela de permissões persistentes. As caixas salvas determinam quais ferramentas entram no próximo turno; leituras de documentos MongoDB também exigem o consentimento separado de envio dos resultados. Grants efetivos ficam limitados ao turno, provider, destino, conexão e categoria autorizados. Após salvar, o usuário envia outra mensagem para repetir a operação. A lista de conexões da ferramenta respeita o mesmo filtro da janela. Erros de proposta como `EditNotApplicable` devem mostrar o motivo de aplicação/proteção; não devem ser tratados como permissão negada nem induzir o agente a afirmar que não existe editor.

Na janela de permissões do GitHub Copilot, ocultamos opções nativas de shell, leitura/escrita nativa de arquivos e rede, pois esse provider expõe somente ferramentas mediadas pelo produto. As opções persistidas continuam intactas para não alterar silenciosamente dados ao alternar providers; controles de workspace e propostas revisáveis permanecem disponíveis. O cartão de confirmação exibe os argumentos completos em campo somente leitura com rolagem, permitindo revisar exatamente a chamada antes de permitir ou negar.

Remover o chip automático **Arquivo ativo** desativa o envio do buffer para aquela mensagem e também impede `propose_file_edit` de propor uma mudança em `active_buffer` naquele turno. Recolocar o chip na mensagem seguinte permite a proposta conforme as permissões persistentes; propostas a outro arquivo do workspace continuam avaliadas pelas permissões próprias do workspace. O runtime captura essa presença no escopo imutável do turno, sem consultar a seleção posterior da aba ou do Explorer.

## IA local: consentimento, prévia e proposta — Fase 8, 22/09/2026 (painel removido em 25/09/2026)

> **Removido em 25/09/2026 (P7-L06-CLEANUP, [ADR-055](10-decisoes-arquiteturais.md#adr-055--remoção-do-assistente-ia-por-aba-25092026)).** A coluna de 240 do Assistente IA dentro da aba deixou de existir: `WorkspaceTabView` volta a ser só cabeçalho de destino + editor/resultados em toda a largura, e o chat da aba é o painel **Agente IA** (seção [Hospedagem na janela principal](#hospedagem-na-janela-principal-p7-l06-host--25092026)). Os opt-ins de contexto local descritos abaixo saíram das preferências e do cadastro de conexão; os valores persistidos são preservados sem consumidor (pendência de produto no [doc 16](phases/phase-05-v0.9.0/README.md)). O texto a seguir é histórico e as capturas `ai-chat-localization` não são mais geradas.

O Assistente IA exige opt-in global desligado por padrão e uma permissão independente por conexão. Input JSON tem opt-in adicional próprio. O primeiro envio manual mostra uma prévia scrollável do snapshot de contexto e não chama o modelo; instrução, editor, alvo/conexão ou política alterados invalidam esse snapshot. Botões **Enviar à IA local** e **Editar** deixam explícita a confirmação ou renovação da revisão. A prévia lista os campos de contexto nos quatro idiomas e usa o mesmo padrão semântico de cartão, superfície, código monoespaçado e rolagem do painel do assistente.

Propostas permanecem em cartão revisável com diff, aplicação explícita e dismiss; operações de risco pedem confirmação adicional e a inserção continua undoável. Screenshot Headless/Skia foi gerado para proposta e contexto nos quatro idiomas e temas claro/escuro. Inspeção em 1120×760 confirmou legibilidade das amostras pt-BR claro/escuro, en claro/escuro, es claro/escuro e zh-CN claro/escuro; conteúdo longo é rolável no painel. Isso não comprova leitor de tela nem layouts nativos, que permanecem na Fase 10. [ADR-052](10-decisoes-arquiteturais.md#adr-052--consentimento-local-e-prévia-do-contexto-de-chat-22092026).

## Preferências de autocomplete expostas — lote W2c, 18/09/2026

`AutocompleteSettingsWindow` ganha controles para as seis opções que já existiam em `AutocompleteSettings` mas só eram editáveis pelo JSON: `InlineEnabled`, `InlineUseTraditional`, `InlineUseAi`, `CompletionAutoOpenOnTrigger`, `CompletionEnterAccepts` e o rótulo do atraso já exposto. As três primeiras são anuláveis para distinguir "ausente" de "false explícito"; cada uma vira um CheckBox de dois estados ligado a um valor efetivo mais um botão **Usar padrão** visível só quando há override — tocar o CheckBox materializa o valor explícito, e apenas o botão devolve o campo ao estado ausente. Nenhum desses controles reescreve a sessão ao abrir a janela. Com a sugestão automática desligada, as duas opções de origem ficam desabilitadas sem perder o valor salvo, com texto explicando a dependência; outro texto esclarece que a IA automática só atua com modelo já carregado (LoadedOnly). Evidência: 18 PNGs `autocomplete-settings-*` (já existentes, regenerados) e novos `autocomplete-settings-inline-overrides-<tema>.png` em `AutocompleteUiTests`; inspeção em 660×680 claro/escuro confirmou contraste e alinhamento dos botões condicionais e a preservação do valor da IA local ao desabilitar visualmente. [Detalhe e limites](21-autocomplete-local.md).

## Consultas avançadas — incremento de 14/09/2026

O menu existente de Ctrl+Espaço passa a mostrar campos derivados dos stages anteriores, com dica **Campo conhecido no contexto do pipeline**; nenhuma nova região ou cor. Teste com o editor real confirmou inserção no cursor e undo de `$total` produzido por `$group`, sem execução ou leitura remota. A inferência respeita campos locais/estrangeiros de `$lookup` e ramos de `$facet`; limites estão em [27](backlog/27-consultas-avancadas.md).

Histórico passa a identificar **Console e Agregação — execuções**. A abertura cria nova aba no modo registrado; o seletor exibe modo e coleção, com dica do destino completo. Sem nova superfície ou mudança de medidas. Capturas reais `aggregation-history-*` nas três dimensões e escalas; inspeção de 600 claro e 720 escuro confirmou legibilidade, ações acessíveis e rolagem local.

Opções do editor recebe **Validar sintaxe** (todos os modos) e **Analisar pipeline (explain)** (Agregação). Validação funciona offline sobre seleção ou documento, publica diagnóstico no painel Erros e seleciona o trecho; sucesso vai para Mensagens. Explain exige destino conectado e usa Mensagens, preservando Resultados. Mesmo estilo neutro, recursos semânticos e rolagem do menu existente. Evidência: 18 PNGs `aggregation-diagnostic-*`; inspeção de 960 claro e 1366 escuro confirmou seleção e leitura do erro, com rolagem local na janela mínima. [Comportamento e limites](backlog/27-consultas-avancadas.md).

Decisão vigente em **10/09/2026**, aprovada para implementação. Este documento substitui as propostas anteriores de conexões permanentemente à esquerda, cadastro acima do editor e script/resultados lado a lado. Requisitos relacionados: CON-01/08, EDT-01/02/04/06 e UX-01/02.

## Objetivo e referências

Uma IDE de MongoDB para uso prolongado, com navegação previsível, destino explícito e área de edição prioritária. Não há nova implementação web: o produto permanece .NET 10/Avalonia para Windows e Linux.

- [Fluent 2: cores](https://fluent2.microsoft.design/color) e [tipografia](https://fluent2.microsoft.design/typography): papéis semânticos e hierarquia compacta. Os tamanhos abaixo são decisões deste produto, não medidas prescritas por essas fontes.
- [WCAG 2.2: contraste](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html): referência quantitativa aplicada aos controles desktop; não constitui certificação completa de acessibilidade.
- [Avalonia: variantes de tema](https://docs.avaloniaui.net/docs/styling/theme-variants): recursos dinâmicos e alternância em tempo de execução.

## Localização, acessibilidade e CJK

Os textos de produto usam um catálogo único, com chaves estáveis e atualização em execução. Os códigos suportados são `pt-BR`, `en`, `es` e `zh-CN`; `pt-BR` é o idioma inicial e `en` é o fallback para códigos inválidos ou chaves sem tradução. Uma chave ausente também no inglês aparece como `[[chave]]`, evitando silêncio. O seletor usa os nomes nativos dos idiomas e permanece acessível por teclado e automação.

A troca de idioma não altera rascunhos, resultados, credenciais ou identificadores MongoDB/BSON; ela atualiza rótulos, dicas, acessibilidade, estados, mensagens e exemplos iniciais gerados pelo produto. O layout deve ser verificado nos dois temas com textos longos e caracteres chineses; a evidência corrente é a matriz de 64 PNGs descrita no acompanhamento. Este documento e os demais `docs/**/*.md` continuam em português.

## Composição e medidas

```text
Conexões | Nova aba | Abrir | Salvar | Ferramentas   Ambientes   [Atualizar] | Tema | …
────────────────────────────────────────────────────────────────
Bancos                  │ Abas de script/consulta/agregação
Buscar / Atualizar      │ Conexão › Banco › Coleção | Contexto
                        │ Editor textual | Executar | Cancelar
Conexão aberta          │ Editor
 └ Banco                │
    └ Coleções          ├───────────────────────────────────────
                        │ Quantidade / limite / duração / exportar
                        │ Resultados | Mensagens | Erros
────────────────────────────────────────────────────────────────
Estado | Origem                          Estado do rascunho local
```

Todas as medidas são unidades lógicas, escaladas pelo Avalonia:

| Elemento | Decisão |
| --- | --- |
| Janela inicial / mínima | 1440 × 900 / 960 × 620 |
| Barra superior / status | 40 / 36 (revisão MVP abaixo) |
| Explorer | Inicial 260; ajuste entre 200 e 420 |
| Editor / resultados | Inicial 60% / 40%; painéis mínimos 180 / 140 |
| Divisores | 5; posições persistidas |
| Modal de conexões | 800 × 560, limitada à janela proprietária; conteúdo com rolagem |
| Controles / linhas do explorer | 32 / 28 |
| Espaçamento / cantos | Múltiplos de 4 / raio 4 |

A área central não usa rolagem global. Editor e saídas possuem rolagem independente. Consultas não abrem um popup de opções nem exibem campos separados para filtro, ordenação, banco ou limite: todos esses elementos são escritos no editor textual, com autocomplete e diagnóstico contextual. Ferramentas menos frequentes ficam em uma janela contextual proprietária. Formulários administrativos extensos mantêm rolagem local; docking complexo permanece fora desta entrega.

## Cores e tipografia

A identidade usa as referências locais de docs/ui: superfícies frias, azul para ação/foco e violeta para marca e títulos de abas selecionadas. O símbolo vetorial simplifica o recipiente inclinado com líquido azul/roxo. Veja o [manual de identidade](18-identidade-visual.md). Verde, âmbar e vermelho indicam estados acompanhados de texto. Cor personalizada de ambiente não substitui o nome do destino ou determina a cor do texto operacional.

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

Texto comum e placeholders: contraste mínimo 4,5:1. Foco e limites necessários à identificação dos controles: 3:1. Divisores puramente decorativos e controles desabilitados têm papéis diferentes. Em seleção, metadados usam PrimaryText para preservar contraste.

Texto sobre botão primário validado em normal, hover e pressionado nos dois temas (mínimo 4,5:1). O azul claro da referência foi escurecido para #1B6EDC no tema claro; o tema escuro usa texto #0F172A sobre #38A8FF. Estados têm recursos próprios e os adornos de foco do Fluent são preservados.

- Interface: Inter embarcada, 13; metadados: 12; títulos de modal: 16 semibold; abas: 13 medium.
- Código/resultados: 14 e entrelinha 21; ajuste de 12 a 20, com entrelinha proporcional de 1,5.
- Fallback monoespaçado: Cascadia Mono, JetBrains Mono, Consolas, DejaVu Sans Mono, monospace. A fonte de código não é uma nova dependência distribuída.
- Temas Sistema, Claro e Escuro; Sistema é o padrão. Preferência aplicada imediatamente e persistida.
- O controle de texto atual permanece; syntax highlighting foi integrado em 12/09/2026 (revisão abaixo). Folding textual e nova grade BSON não são anunciados como entregues.

## Jornadas e regras de interação

### Conexões e explorer

Conexões abre modal com busca por nome/host/pasta/ambiente, favoritos e edição completa. A lista mostra somente o host; usuário, senha e query string não aparecem no resumo. Nova conexão permite preencher a partir da URI, revisar e salvar. Testar valida o perfil selecionado. Abrir conexão carrega bancos e fecha a modal somente no sucesso; falha fica na modal.

O explorer lista todos os perfis cadastrados; bancos aparecem sob as conexões abertas. Expandir banco busca suas coleções; atualizar permite repetir após falha. A busca filtra apenas nós já carregados. Selecionar navega; Enter ou duplo clique em coleção abre/ativa o Console com `db.getCollection("colecao").find({}).limit(100)`, sem executar. Conexão/banco formam o contexto; a coleção fica no script.

Editar/remover um perfil invalida o explorer antigo e exige reabrir sua conexão antes de executar novamente. Isso impede reutilizar um banco listado de outro host ou uma política de acesso antiga. Os rascunhos continuam disponíveis.

### Workspace local e painel lateral

Uma barra vertical acessível organiza dois painéis tratados como abas: **Conexões** primeiro e **Arquivos** segundo. **Abrir pasta** (`Ctrl+Shift+O`) escolhe uma única raiz, abre o workspace local e seleciona automaticamente **Arquivos**; trocar ou fechar a pasta preserva as abas de documentos abertas. O painel não consulta MongoDB e a seleção da árvore nunca executa conteúdo.

**Arquivos** exibe a raiz, pastas antes de arquivos e filhos carregados sob demanda. Os estados vazio, carregando e erro têm texto e ação de **Atualizar**. Enter ou duplo clique abre/ativa o arquivo como texto; abrir o mesmo caminho novamente reutiliza a aba. O menu contextual oferece **Novo arquivo**, **Nova pasta**, **Renomear** e **Excluir**. Nomes duplicados, caminhos fora da raiz e alterações na própria raiz são bloqueados. A exclusão usa a lixeira do sistema e informa a falha quando ela não está disponível.

Arquivos vazios e extensões desconhecidas são válidos. O serviço preserva UTF-8 (com ou sem BOM), UTF-16/UTF-32 com BOM, quebras de linha e bytes na gravação; conteúdo binário ou codificação não reconhecida produz erro recuperável. Documento sem caminho usa **Salvar como**. Alteração externa oferece recarregar, sobrescrever explicitamente ou cancelar, e buffers abertos viram documentos sem arquivo quando o item é excluído.

### Abas e execução

Nova aba cria um editor. Cada aba possui ID, contexto, texto, arquivo, estado de alteração, resultados, mensagens, erros e cancelamento próprios. O contexto muda somente por ação explícita ou por carregar uma consulta da própria conexão; não há um modo paralelo de campos para montar a consulta.

Uma execução por aba; abas diferentes podem executar simultaneamente. O executor captura os parâmetros antes do primeiro await. Retornos fora de ordem atualizam apenas a aba originária. Scripts recebem o banco explicitamente: o runner inicializa `db` por `getSiblingDB` com literal serializado, mantendo URI e authSource. O próprio script ainda pode escolher outro banco explicitamente.

Resultados permanecem em Extended JSON, separados de stdout/stderr; consultas mantêm paginação e exportação da página. Agregação também usa o editor textual e o painel inferior. Quantidade, limite efetivo e duração aparecem junto à saída, sem transformar esses dados em campos obrigatórios do editor. Scripts sem documentos indicam o console. Erros não provocam nova execução de outro trecho.

| Atalho | Comportamento |
| --- | --- |
| F5 | Executar conteúdo completo da aba |
| Ctrl+Enter | Console: seleção ou statement no cursor; Script/Agregação: seleção ou conteúdo completo |
| Ctrl+Espaço | Abre a lista contextual de sugestões; não abre com seleção ativa nem aplica resultado de texto/destino antigo. Desde 18/09/2026 é o único gatilho padrão — `Ctrl+.` saiu dos padrões (ver limitação de IME abaixo) |
| ↑ / ↓ | Navega a lista aberta |
| Tab | Aceita item da lista, avança placeholder do snippet ou aceita o ghost (conforme o estado ativo) |
| Shift+Tab | Volta ao placeholder anterior do snippet |
| Enter | Aceita item da lista, se `CompletionEnterAccepts` (padrão habilitado) |
| Esc | Fecha a lista, encerra o snippet ou descarta o ghost (conforme o estado ativo) |
| Ctrl+T / Ctrl+O / Ctrl+S | Criar aba / abrir arquivo / salvar arquivo |
| Ctrl+Shift+S / Ctrl+Shift+O | Salvar como / abrir ou trocar pasta do workspace |
| Ctrl+Tab / Ctrl+Shift+Tab | Alternar abas |
| Ctrl+W | Fechar aba com tratamento de alterações e execução |
| F6 | Alternar foco entre editor e explorer, permitindo sair do editor que aceita Tab |
| Escape | Fechar modal; no workspace, cancelar operação da aba ativa |

Ao fechar uma aba executando: interromper e aguardar ou cancelar o fechamento. Cancelamento informa que efeitos no servidor não são revertidos e podem ser incertos. Abas alteradas oferecem salvar, descartar ou cancelar. Confirmações destrutivas, auditoria e bloqueios de somente leitura continuam nas ferramentas existentes.

Os atalhos do editor usam `EditorKeyBindings` persistido: a preferência substitui os gestos padrão de cada comando dentro do mesmo `EditorCommandScope` (Global/List/Snippet/Inline); o mesmo gesto pode continuar sendo o padrão de comandos diferentes em escopos diferentes. Uma tecla nomeada (`Tab`, `Enter`, `Esc`, setas) casa por identidade de tecla; pontuação (`.`, `;`) casa pelo símbolo produzido pelo layout ativo e, sem símbolo, pela tecla física como fallback — assim um símbolo diferente nunca dispara pelo código físico US. **Limitação conhecida:** desde que `Ctrl+.` saiu dos padrões, `Ctrl+Espaço` é o único gatilho padrão do autocomplete básico e colide com a troca de IME em Windows e Linux; não há tela de edição de atalhos nesta entrega, então o contorno é um override manual salvo em `EditorKeyBindings` (um valor `Ctrl+.` já salvo continua funcionando e nunca é reescrito). Detalhe em [auto-complite/editor-integration.md](auto-complite/editor-integration.md#atalhos) e [AC-08](auto-complite/decisions.md#ac-08--atalhos).

### Recuperação e privacidade

Coleção LiteDB adicional `workspaceSession`, documento `current`, JSON de versão 2. O mesmo repositório continua proprietário da conexão LiteDB; a migração da versão 1 é aditiva. A sessão pode guardar raiz da pasta, painel selecionado e metadados dos documentos, mas nunca resultados ou credenciais. Contratos: `IWorkspaceSessionRepository`, `WorkspaceSession`, `WorkspacePreferences`, `WorkspaceDraft`, `ITextFileService` e `IWorkspaceFileService`.

Autosave após 750 ms sem edição e no encerramento. Recuperar ordem, aba ativa, texto, contexto e arquivo; não recuperar resultados, credenciais ou conexões abertas. A entrada JSON só entra no snapshot mediante a opção Persistir entrada. Preferências de histórico são independentes do autosave.

Recuperação ligada por decisão do usuário, desativável globalmente e por conexão em Preferências. A política também é aplicada no repositório. Descartar aba remove seu rascunho. Falha de gravação permanece visível e conserva o conteúdo em memória; sessão ilegível não é sobrescrita por defaults. Texto SQL/MQL/JavaScript digitado pelo usuário pode conter dados sensíveis: o armazenamento de rascunhos não é um cofre nem promete remover segredos arbitrários do código.

## Validação e limites

Testes cobrem isolamento, resposta fora de ordem, seleção sem fallback, cancelamento, somente leitura, perfil alterado, migração aditiva, autosave, opt-in de entrada, recuperação, descarte e falhas de persistência. Renderização Avalonia Headless/Skia usa controles reais e dados de teste em 960 × 620, 1366 × 768 e 1920 × 1080, escalas 100%, 150% e 200%, nos dois temas.

As imagens ficam em `ui-evidence` no diretório de execução dos testes, excluído do controle de versão. Build, contagem final por sistema e pendências estão na [matriz de validação](15-matriz-de-validacao.md) e no [acompanhamento](12-acompanhamento-da-implementacao.md).

Homologação contra MongoDB/mongosh real, leitor de tela, diálogos nativos de arquivo e gerenciadores de janela reais continuam separadas da renderização automatizada. Não declarar suporte integral a essas jornadas apenas com testes simulados. Tabela tabular, editor avançado, cofre nativo e virtualização de documentos permanecem no backlog; a árvore de inspeção da página foi acrescentada na revisão Database Explorer abaixo.

## Prévias da implementação

Renderização automatizada com dados sintéticos, 1366 × 768, escala 100%.

![Tema claro](ui/preview-claro.png)

![Tema escuro](ui/preview-escuro.png)


## Ambientes e credenciais — revisão de 10/09/2026

Até 17/09/2026 a barra superior oferecia **Ambientes**, abrindo a modal proprietária de ambientes. Desde 18/09/2026 esse botão e o botão **Ferramentas** foram removidos da barra ([ADR-042](10-decisoes-arquiteturais.md)); a modal permanece implementada, intitulada **Ambientes locais**, sem ponto de entrada. A descrição a seguir documenta o layout preservado. Seletor de ambiente, criação customizada, lista de chaves e editor com valor mascarado; controles tipados e recursos semânticos compartilhados. **Salvar e ativar ambiente** é explícito; selecionar para editar não altera o ambiente de execução. Erros de leitura/gravação ficam visíveis. Escape fecha apenas a modal e descarta o formulário não salvo. Layout inicial 760 × 540, mínimo 600 × 420, com rolagem local; evidência em 600 × 420, 760 × 540 e 900 × 650, escalas 100/150/200%, claro/escuro.

Conexões aceita URI direta com senha ou interpolação opcional. Campos de usuário/senha são opcionais; valores digitados são codificados para URI, sem converter referências já existentes. O rótulo de ambiente no perfil não escolhe o conjunto de valores ativo. Salvar ambientes invalida destinos explorados e requer reabertura; textos e operações em andamento são preservados. O estado do ambiente ativo aparece na modal. A interface informa armazenamento local sem criptografia nativa, sem confundir esse módulo com CSFLE.


## Database Explorer — revisão de 10/09/2026

O explorer passa a listar todos os perfis, inclusive desconectados, e inclui Documentos/Índices sob cada coleção. Estados de conexão/carga/erro têm texto. Botão direito e Shift+F10 dão acesso a menus específicos. **Detalhes do item** ocupa uma região recolhível de até 240 unidades, com conteúdo rolável; o restante da árvore conserva sua própria rolagem. Mantida a janela mínima 960 × 620 e o editor acima dos resultados.

A saída ganha **Documentos**, com lista da página à esquerda, campos estruturados à direita e ações compactas em WrapPanel. Resultados JSON, mensagens e erros continuam separados. Editor de documento em modal proprietária de 760 × 560 (mínimo 600 × 420), destino visível, confirmação explícita e bloqueio de fechamento durante operação. Seleção de instância é uma modal contextual com host/papel e explicação de capacidade; a aba identifica seleção automática, direta na URI ou host explícito.

Evidência: 18 PNGs de explorer/documentos (claro/escuro × 960/1366/1920 × 100/150/200%), editor de documento nos dois temas, menus reais e encaminhamento às ferramentas testados. Não houve modificação de golden files. [Guia e prévias](19-database-explorer.md).

## Console — revisão de 11/09/2026

Console é o modo inicial da aba e substitui Consulta JSON na seleção. Cabeçalho conexão › banco; Destino… permite trocar ambos. Coleção não é campo obrigatório. Os comandos de consulta ficam no texto; Opções contém apenas limites de segurança do Console e preferência de histórico.

Resultados mostram expressões numeradas e a conexão/namespace quando conhecidos. Documentos oferece seletor do conjunto e conserva a origem para edição. Mensagens recebe console.log/warn/error. Confirmação de escrita é uma modal proprietária com contexto real; Escape nega o envio e cancelamento/timeout fecha a confirmação.

Ctrl+Enter usa seleção ou statement identificado pelo parser; F5 executa tudo. Autocomplete obtém metadados assincronamente e descarta sugestões se texto/destino mudar. Rascunho JSON convertido fica alterado, sem execução automática. Renderização e teclado são verificados em controles reais, nos 18 cenários de tema/tamanho/escala. [Console](20-console.md).

## Autocomplete local — revisão de 11/09/2026

Preferências contém Autocomplete…, modal proprietária 660 × 680, mínimo 520 × 420, conteúdo rolável e ações/status no rodapé. Campos tipados usam o catálogo em pt-BR, en, es e zh-CN: modo, diretório externo, modelos encontrados, hardware, contexto/geração/atraso. Estados do runtime e falhas de gravação são textuais. Recursos semânticos dos temas existentes; nenhum indicador apenas por cor.

TextBox preservado. Ghost text no cursor, com fonte/entrelinha do código e SecondaryText; contexto existente conserva PrimaryText. Projeção visual recortada ao viewport, incluindo múltiplas linhas e sufixo, sem alterar documento. Tab avança por partes lógicas, Escape descarta antes de cancelar consulta e Ctrl+Espaço conserva menu com metadados. F6 continua saindo do editor. Preferências oferece opções independentes para dicionário, Input, campos dos Resultados, contexto ampliado e Tab incremental. Renderização em 18 combinações do workspace e 18 da modal, com controles reais, escalas 100/150/200% e dois temas. [Especificação e limites](21-autocomplete-local.md).

## UUID/GUID — revisão de 11/09/2026

Preferências passa a ter largura 560 (mínimo 460 × 420, altura máxima 760) e conteúdo rolável; Escape fecha. **Representação UUID padrão** fica após as opções de rascunho. O editor de conexão recebe **Representação UUID desta conexão**, com **Usar preferência global**, abaixo de Favorita/Somente leitura. Os dois usam `UuidRepresentationPanel`: seletor tipado, prévia do UUID `00112233-4455-6677-8899-aabbccddeeff` nas quatro formas em fonte de código 13, subtype e bytes em metadados. A linha efetiva recebe semibold e o texto “Selecionada”, sem depender só de cor. O literal ocupa a largura inteira da linha e não é truncado. Status de gravação usa ErrorBrush apenas junto de mensagem textual.

A métrica dos resultados acrescenta “UUID <representação>” e a contagem de legados de origem desconhecida; o texto trunca com reticências e mantém a dica completa. A árvore de Documentos exibe UUID binário como folha com o construtor. Evidência: 18 PNGs para cada superfície (Documentos 960/1366/1920, Preferências 460×420/560×680/900×760, conexão 600×420/800×560/900×650; claro/escuro; 100/150/200%) em `ui-evidence/uuid-*.png`.

## Resultados JSON e árvore — revisão de 11/09/2026

O cabeçalho da saída recebe o seletor segmentado **JSON | Árvore** antes das métricas: `RadioButton.segment`, altura 32, opção ativa com SelectionBrush, borda AccentBrush e semibold, sem depender só de cor. **Copiar JSON** age no documento selecionado e explica por dica quando não há seleção. Escolher uma visualização traz Resultados à frente. Visualização, seleção e expansão ficam por aba, apenas em memória.

- **JSON:** TextBox somente leitura, fonte de código configurável, rolagem horizontal e vertical. Indentação de 2; wrappers Extended JSON (`$oid`, `$date`, `$binary`, `$numberLong`…) em uma linha e tokens copiados sem conversão. No Console, cada conjunto recebe o comentário `// [n] conexão › banco › coleção · método · N documento(s) · limitado · projeção parcial`. O cursor seleciona o documento; seleção feita na árvore ou em Documentos reposiciona o cursor.
- **Árvore:** TreeView com PanelBackground e borda ControlBorderBrush. Cada linha tem nome (semibold em conjunto e documento), chip de tipo (metadata 12 sobre SecondaryBackground; PrimaryText quando selecionada) e valor em fonte de código, truncado em 240 caracteres com dica completa. Console agrupa por conjunto; demais modos listam documentos. Filhos são criados ao expandir; o primeiro conjunto com documentos e um documento único abrem por padrão. Avisos são linhas de texto: sem documentos, resultado limitado, projeção parcial, agregação e JSON inválido.
- **Menu do documento:** botão direito, Shift+F10 e tecla Menu, na árvore (item apontado ou selecionado) e no JSON (documento no cursor; o clique direito move o cursor). Legenda com documento e `_id`; **Visualizar documento em JSON**, **Abrir documento para edição** com motivo textual quando indisponível e **Copiar JSON**; no JSON também **Copiar texto selecionado**. Fechar o menu devolve o foco.
- **Visualização JSON:** modal proprietária 760 × 560, mínimo 600 × 420; título 16 semibold, aviso de somente leitura, origem, conexão › banco › coleção, identidade e apresentação UUID; TextBox somente leitura; **Copiar JSON** primário, **Fechar** e Escape.
- **Edição:** a modal de documento existente ganha linha de política, texto indentado sem quebra e **Salvar…**, desabilitado com dica em conexão somente leitura ou fechada.

Evidência: 72 PNGs (JSON e árvore em 960 × 620, 1366 × 768 e 1920 × 1080; modais em 600 × 420, 760 × 560 e 900 × 650; claro/escuro; 100/150/200%) em `ui-evidence/results-*.png` e `ui-evidence/result-document-*.png`.

![Resultados em árvore, tema claro](ui/resultados-arvore-claro.png)

![Resultados em árvore, tema escuro](ui/resultados-arvore-escuro.png)


## Syntax highlighting — revisão de 12/09/2026

TextBox.syntax conserva edição/undo e usa SyntaxTextPresenter. Recursos Syntax.* nos dois temas diferenciam propriedades/strings, valores, operadores/stages, tipos BSON e namespaces conhecidos. Delimitadores junto do cursor recebem cor e sublinhado; ghost text usa recurso próprio e conserva as cores do texto existente. Resultados, modais, árvores e ferramentas reutilizam o mecanismo. Classificação em worker, cache por linha e spans do viewport protegem documentos extensos; o layout nativo do TextBox ainda não é virtualizado. Contraste mínimo 4,5:1 sobre PanelBackground e matrizes de PNGs reais de workspace/modal. [Arquitetura e limites](22-syntax-highlighting.md).

## Identificadores — revisão de 12/09/2026

Preferências mostram primeiro **Representação padrão de identificadores** e depois **Representação UUID · Binary BSON**. O seletor de modo usa os rótulos “Standard · ObjectId + UUID v4”, “ObjectId · MongoDB ObjectId” e “UUID v4 · BSON subtype 4”; abaixo vêm a explicação do modo selecionado (texto quebrável) e o card **Prévia do modo selecionado**, com as seções **ObjectId** (construtor, hex e UUID equivalente; cada linha com rótulo `metadata`, código em CodeFont 13 e detalhe) e **UUID** (UUID v4 na representação atual). Standard mostra as duas seções; ObjectId oculta a seção UUID e troca a comparação das quatro formas por uma frase; UUID v4 oculta a seção ObjectId. O status de gravação usa a mesma cor de erro do painel UUID. A árvore de Resultados acrescenta “· UUID …” (o UUID equivalente) ao valor do ObjectId somente em UUID v4, curto o bastante para a coluna de valor de 720 px; a dica mantém o texto integral; a métrica passa a “IDs <modo> · UUID <representação>”. O menu do documento ganha **Copiar _id**, **Copiar consulta por _id** e, em UUID v4, **Copiar UUID equivalente do _id**. Nas Ferramentas, **Gerar identificador** e a linha **Interpretar** ficam na aba Documentos, com rótulo truncável e dica. Evidência: `ui-evidence/identifier-results-*.png` (960/1366/1920) e `identifier-preferences-<modo>-*.png` (460×420, 560×680, 900×760), claro/escuro, 100/150/200%.

## IA ONNX compartilhada — revisão de 13/09/2026

A composição do editor e do Assistente IA permaneceu até 25/09/2026, quando o painel foi removido (ADR-055); o modelo de chat passou a servir só ao provider local do Agente IA. Preferências → Autocomplete seleciona modelo e CPU/GPU para ambas as jornadas. O status identifica o provider efetivo e o fallback GPU → CPU; modo básico no chat é explicitamente identificado. Propostas ONNX exigem revisão e confirmação existentes; resposta incompleta ou contexto acima do limite gera erro textual. O pacote SlopCoder é FIM, com fidelidade a instruções de chat ainda não homologada. [Uso e limites](23-onnx-slopcoder.md).


## IA local multimodelo — revisão de 13/09/2026

A modal passa a se chamar Autocomplete e IA local, mantendo 660 × 680, mínimo 520 × 420, conteúdo rolável e ações no rodapé. Ordem: opções do autocomplete e do Assistente, modo, diretório de modelos (placeholder com o padrão, Procurar…), modelo (lista pelo nome da pasta ou do metadata, Atualizar, Outra pasta…), detalhes e pastas ignoradas em texto metadata, hardware com dispositivos detectados, perfil de estimativa com os sete tiers pré-carregados, orçamentos, estado do modelo e resultado do teste. O tier detectado seleciona o perfil correspondente e preenche contexto/geração automaticamente, respeitando janela, teto de saída e overhead do modelo. Os orçamentos são ComboBox editáveis: sugestões e livre digitação para contexto e geração; os campos exibem somente dígitos, sem ponto de milhar. Valores maiores continuam digitáveis quando o modelo suporta, enquanto a memória orienta a recomendação. O rodapé mostra só a última mensagem (até três linhas) e os botões; estado e relatório longos ficam no conteúdo rolável para não ocupar a janela mínima. Perfis manuais são apenas estimativas e não alteram o provider real.

Hardware ausente aparece como "GPU — indisponível" e fica desabilitado na lista; nenhum estado depende só de cor. Estados de modelo, fallback e falha de provider são frases com motivo e alternativa. A carga usa a barra inferior global existente. Relatório de teste é selecionável para cópia. [Especificação](26-ia-local-multimodelo.md).

## Datas BSON — revisão de 13/09/2026

Resultados, árvore, visualização, edição e cópias apresentam datas como `ISODate("2024-12-30T20:56:44.999Z")`, com data, hora, segundos, milissegundos e timezone UTC explícito. BSON não conserva o fuso original; entradas com offset são normalizadas para o instante UTC equivalente. Strings comuns permanecem strings; valores fora do intervalo do .NET conservam Extended JSON na saída textual e milissegundos na árvore. Mesmos tokens de código e layout. PNGs reais da modal de documento inspecionados em claro/escuro, 760 × 560, 100%.

## Enquadramento de entrega e editor atual — 13/09/2026

✅ Implementado: estrutura desktop, temas, navegação sem consulta automática, contexto fixo e cancelamento por aba. A v0.5.0 consolida o fluxo básico; v0.6.0 revisa produtividade; v1.0.0 exige acessibilidade e integração nativas. IA permanece opcional/experimental na v0.12.0. [Roadmap](09-plano-de-implementacao.md).

O checkout atual utiliza MongoTextEditor derivado de AvaloniaEdit.TextEditor e LongLineElementGenerator. Descrições datadas de TextBox/TextPresenter acima são históricas e não descrevem a base atual do editor. A presença de linhas visuais virtualizadas não encerra a homologação de arquivos extensos, folding ou acessibilidade. Esta revisão documental não altera medidas, temas, atalhos, navegação ou sessão e não gera nova evidência visual; testes/PNGs anteriores conservam sua data e limites.


## Polimento do MVP — 13/09/2026

A barra inferior passa a ter 36 unidades para comportar a ação Cancelar com alvo de 28. Apresenta estado textual, progresso de 100 unidades (indeterminado quando não há total), percentual separado quando conhecido, descrição truncável com dica completa e quantidade de operações adicionais. Rascunho local continua visível à direita. Sem overlay global: abas e Explorer permanecem navegáveis. Prioridade Alta para consulta manual, conexão, exportação e escrita; Normal para carga/metadados; Baixa para sugestões locais. Estados terminais duram seis segundos quando não há operação ativa.

Os botões do editor usam quebra de linha na largura mínima, evitando sobreposição com Opções/Histórico. Opções contém Formatar JSON/query/script: seleção ou conteúdo completo, sem execução e com Ctrl+Z. Exportar página oferece JSON e CSV; a dica explicita página e proteção de fórmulas. Árvores carregam campos em grupos de 256, com Próximos campos…, sem cortar o JSON/exportação. Textos enormes são apresentados por trecho visual próximo ao cursor, mantendo o documento e a seleção completos.

Evidência de controles reais: `MvpPolishUiTests`, 18 PNGs `mvp-status-<tema>-<largura>-<escala>.png`, nas três larguras/alturas e escalas do sistema. Inspeção revelou e corrigiu sobreposição da ProgressBar e da barra do editor. A fixture da barra usa operações sintéticas para tornar progresso e concorrência reproduzíveis; não é uma captura de produção. A matriz e a auditoria registram execução e limites.

## Atualização do aplicativo — 14/09/2026

A barra superior ganha a ação **Atualizar** entre Ambientes e Tema. Ela só aparece com versão nova ou pacote pronto; sem atualização, a composição anterior não muda. Botão `primary` (AccentBrush/OnAccentBrush), ícone de download 16, altura 32 e alvo mínimo 28. Rótulos curtos por estado: Atualizar, Baixando N%, Reiniciar. A dica e o HelpText trazem versão, motivo de falha e consequência (instalação ao fechar). Abaixo de 1100 de largura, a classe `compact` oculta o texto e mantém o ícone: em 960 o rótulo sobrepunha Ambientes a Ferramentas. Progresso e cancelamento reutilizam a barra inferior; Reiniciar pede confirmação (Reiniciar agora/Depois) e segue o fechamento normal.

Evidência: `AppUpdateUiTests`, 36 PNGs `update-<available|downloading>-<tema>-<largura>-<escala>.png`. Inspeção de 960 claro/escuro (escala 1 e 2), 1366 escuro e 1920 claro confirmou ausência de sobreposição e contraste do rótulo no tema escuro. A fixture usa um serviço de atualização sintético; não é captura de download real.

## Download de modelos — 14/09/2026

Na modal Autocomplete e IA local, abaixo de Modelo, a seção **Baixar modelo** repete o padrão seletor + ações: ComboBox `<variante> · <tamanho>` (sufixo "— instalado"), **Baixar** e **Atualizar lista**. Durante o download, **Cancelar** ocupa a coluna de Baixar, sem deslocar o seletor. Abaixo ficam ProgressBar de 6 com MinWidth 0 e percentual separado; metadata com pasta, tamanho, licença e destino completo; estado textual do download (sem depender de cor); e o aviso de fonte, licença e continuidade após fechar a janela. Nenhuma cor nova e nenhum botão primário adicional: Salvar continua sendo a única ação primária.

Evidência: `ModelDownloadUiTests`, 18 PNGs `model-download-<tema>-<largura>-<escala>.png` em 520 × 420, 660 × 680 e 900 × 760. Inspeção de 520 claro, 660 escuro e 900 claro a 150% sem sobreposição das ações; em 520 × 420 o progresso fica abaixo da dobra da área rolável. Fixture com fonte remota sintética.

**Revisão de nomes e pasta — 14/09/2026.** Os seletores Modelo e Baixar modelo usam o mesmo padrão de item: primeira linha "família — hardware precisão" (ex.: `SlopCoder-Mongo-1.5B-full — GPU DirectML FP16`); segunda linha `metadata` com tamanho e dica de uso no download, ou parâmetros, arquitetura e pasta na seleção. A caixa fechada mostra só a primeira linha (`SelectionBoxItemTemplate`) e, quando truncada na largura mínima, o nome completo fica na dica. A segunda linha do item escolhido para download aparece logo abaixo da linha de ações, com "GPU não detectada nesta máquina" quando aplicável — texto, não cor. **Ver detalhes do modelo no Hugging Face** é um `HyperlinkButton` sem padding, abaixo do destino. **Abrir pasta** entra como terceira ação do Diretório de modelos, depois de Procurar…, com o mesmo estilo neutro. Inspeção: 660 escuro sem sobreposição e com hierarquia título/metadata legível; em 520 claro o título da caixa trunca ("… GPU Di…"), motivo da dica.


## Revisão do plano de autocomplete — 15/09/2026

Contrato futuro (histórico, revisto pelo lote W0 abaixo): lista Ctrl+. (alias Ctrl+Espaço), IA explícita Ctrl+;, ghost tradicional e IA usando um presenter com arbitragem previsível; padrão híbrido não troca ghost tradicional visível. Tab/Esc/Enter/F6, foco/IME, recursos semânticos e evidência de 18 combinações mantidos. Correção antes do cursor fica na lista até prévia representável. Schema Learning não bloqueia resultados e informa falha persistente discretamente. Nada disso altera layout/atalhos atuais nesta revisão; não gerados novos PNGs. [Plano revisado](auto-complite/README.md), [tarefas por agente](auto-complite/execution-plan.md) e [schema learning](auto-complite/schema-learning.md).

## Política de atalhos do autocomplete (W0) — implementada em 18/09/2026

O produto decidiu remover `Ctrl+.` dos padrões: `Ctrl+Espaço` passou a ser o único gatilho padrão da lista básica explícita, revisando o contrato futuro acima. `Ctrl+;` é reconhecido para IA explícita, mas sem runtime ainda — a aba só informa indisponibilidade de forma discreta. A tabela de atalhos desta seção e a arbitragem de teclado foram atualizadas para refletir os doze comandos por escopo (`EditorCommandScope { Global, List, Snippet, Inline }`); nenhum layout novo ou PNG foi gerado, pois não há mudança visual. Build 0 avisos; suíte 1170 aprovados, 0 falhas. **Limitação registrada, não resolvida:** `Ctrl+Espaço` colide com troca de IME em Windows e Linux; sem tela de edição de atalhos, o contorno é um override manual em `EditorKeyBindings`. Homologação em layouts físicos reais, IME real e leitor de tela seguem pendentes. Detalhe em [auto-complite/editor-integration.md](auto-complite/editor-integration.md#arbitragem-de-teclado), [AC-08](auto-complite/decisions.md#ac-08--atalhos) e [estado da Fase 2](auto-complite/phases/phase-2-traditional-autocomplete.md#estado-da-implementação).

## Chat nativo de agentes (lote 6) — implementação parcial, 24/09/2026

Implementados `AgentChatPanel` (UserControl, mínimo 320 de largura), `AgentApprovalWindow` (modal proprietária 760 × 560, mínimo 600 × 420) e `AgentSettingsWindow` (660 × 560, mínimo 520 × 420), com `AgentChatViewModel`, `AgentApprovalViewModel` e `AgentSettingsViewModel`. Consomem só `IAgentRuntime`/`IAgentContextProvider` e portas de apresentação do Desktop (`Desktop/Agents/AgentChatPorts.cs`: catálogo de providers por capability, detalhes confiáveis de aprovação e gravação explícita de chave). Desde 25/09/2026 o painel está hospedado na janela principal (subseção abaixo); detalhes de aprovação seguem sem fonte confiável de produção e a aprovação permanece fail-closed.

- **Cabeçalho:** título, chip textual **Local/Externo**, Configurar…, seletores de provider (rótulo "Nome · Externo" ou "· Indisponível") e modelo, e linha metadata "Contexto fixo da aba: conexão › banco › coleção", derivada do snapshot da aba; o Explorer nunca a altera.
- **Status:** cartão com frase para cada estado (indisponível, sem provider, sem chave, credencial expirada, cofre indisponível, pronto, revisando, conectando, gerando, aguardando ferramenta/aprovação, cancelando, concluído, cancelado sem rollback, resultado incerto, tempo esgotado, falha com código seguro). ErrorBrush só acompanha texto; região `LiveSetting=Polite`, sem anunciar cada token.
- **Histórico:** ListBox virtualizada; mensagens com papel e "gerando…"; cartões de ferramenta com nome canônico, estado, duração e código; cartão de aprovação com borda WarningBrush, estado textual e **Revisar aprovação…**. A rolagem acompanha o stream só se o leitor já estava no fim; streaming não move foco. Convite vazio só aparece quando é possível conversar.
- **Envio:** escopo explícito (**Nenhum** padrão, **Somente metadados**, **Seleção do editor**); consentimento externo por sessão, desmarcado por padrão e zerado ao trocar provider — configurar conta/chave não consente. **Revisar envio** captura a aba antes de qualquer await e mostra a prévia (destino, escopo, namespace e contexto em fonte de código 14/21); mensagem, escopo, provider, modelo ou contexto da aba alterados descartam a prévia. **Enviar** só usa o pacote revisado. Trocar provider cria nova sessão, limpa a conversa visível e informa que nada foi transferido.
- **Teclado:** Enter quebra linha; Ctrl+Enter revisa/envia somente com foco no compositor; Escape no compositor descarta a prévia e nunca cancela turno; Tab chega a **Cancelar execução** (explícito, com dica de que não há rollback).
- **Aprovação:** ferramenta, destino, filtro/identidade, mudança, limite, risco textual (Destrutiva em ErrorBrush + texto), contagem regressiva e mensagem. **Rejeitar** recebe o foco inicial; Enter ativa o botão focado, Escape e fechar rejeitam; **Aprovar uma vez** (primário) fica desabilitado sem detalhes verificados, após expirar, ao encerrar pelo runtime ou sem digitar exatamente o nome da coleção em ação destrutiva. Texto do modelo nunca aprova.
- **Configurações:** providers com destino, métodos oficiais (só **Chave de API** ou **Sem conta (local)**; não há login de assinatura), estado da credencial, modelos e capabilities; chave em entrada mascarada, **Salvar chave no cofre**/**Remover chave** explícitos, status fixo sem ecoar a chave, campo limpo ao salvar/fechar. Abrir não autentica. **Ressalva de estado atual (25/09/2026):** esta descrição reflete o que está implementado hoje (lote 6). O login de assinatura pelo modo Claude Code foi implementado na UI em P7-CL5-02 (subseção "Conta Claude pelo Claude Code e modo em uso" abaixo), sem homologação com conta real.

Evidência: `AgentChatUiTests`, 42 PNGs `agent-chat-*`, `agent-approval-*` e `agent-settings-*` (claro/escuro; painel 400 × 720 em cinco estados, 360 × 620 e 480 × 768 em 100/150/200%; aprovação 760 × 560 e 600 × 420; configurações 660 × 560 e 520 × 420), com fixtures sintéticas. A inspeção corrigiu convite/compositor ativos no estado indisponível, "sem mudança (leitura)" exibido quando os detalhes não foram verificados e texto sob a barra de rolagem a 520. Limites: sem integração à janela principal nem medida do painel docked em 960/1366/1920; duração de ferramenta sintética (0 ms); alvo da ferramenta não aparece no cartão porque `AgentEvent` não o transporta; idiomas en/es/zh-CN traduzidos, sem PNG próprio. Leitor de tela, IME e diálogos nativos Windows/Linux permanecem pendentes.

### Hospedagem na janela principal (P7-L06-HOST) — 25/09/2026

- **Entrada:** botão **Agente IA** (ícone de mensagem + rótulo; só ícone abaixo de 1100, com dica e nome acessível) na barra superior e atalho **Ctrl+Shift+A**. Revisão de conflitos: não colide com F5, Ctrl+Enter, Ctrl+Espaço, Ctrl+;, Ctrl+T/O/S/W/Tab, Ctrl+Shift+S/O, F6 nem com os comandos de `EditorKeyBindings`; Ctrl+Shift sozinho (troca de layout no Windows) não é afetado. Painel fechado abre e foca o compositor; com foco no painel recolhe e devolve o foco ao editor; com o painel aberto e foco fora dele, move o foco para o chat.
- **Disposição:** recolhido ao iniciar (sem persistência de largura/estado nesta entrega). Encaixado à direita com divisor de 5, largura inicial 380 (mín. 320, máx. 560) enquanto a área de abas mantém ao menos 690 — a largura que tem na janela mínima —, para o editor nunca ficar mais estreito que na janela mínima. Abaixo disso (ex.: 960 × 620, 1366 com explorer largo) o painel vira **superfície própria** no lugar da área de abas, com o botão textual **Voltar ao editor**; o Explorer permanece. Encaixado, o botão é "×" com nome acessível "Recolher o painel Agente IA (Ctrl+Shift+A)".
- **Contexto e isolamento:** um `AgentChatViewModel` por aba, criado na primeira abertura do painel; o painel mostra sempre o chat da aba ativa e voltar à aba restaura sua conversa. O snapshot vem só da aba (perfil, banco, coleção fora do modo Console, revisão do texto e seleção do editor), capturado de forma síncrona antes de awaits; seleção no Explorer não altera o contexto e nada executa consulta. Mudança explícita do destino da própria aba atualiza o contexto fixo e descarta a prévia. Fechar a aba encerra o turno e a sessão daquele chat; as demais continuam.
- **Foco/teclado:** com foco no painel, F5, Ctrl+Enter e Escape nunca executam nem cancelam a operação da aba (Ctrl+Enter só revisa/envia a mensagem); F6 na superfície própria volta ao editor.
- **Inicialização e disponibilidade (P7-AUTH):** abrir o app verifica em segundo plano somente o provider salvo e elegível, inclusive com o painel recolhido; esse coordenador não compõe o runtime/chat nem bloqueia a janela. O estado ativo usa **Verificando**, **Disponível**, **Entrar**, **CLI ausente**, **Não verificado** (ação explícita necessária) ou **Tentar novamente** para falha/timeout. A seleção de outro provider segue a mesma política. Providers cuja checagem possa abrir diálogo de cofre (Codex e Copilot no Linux conforme configuração atual) não são tocados automaticamente; **Verificar disponibilidade**/**Testar conexão** é a ação humana explícita. Teste manual, retry e startup compartilham cache e operação por provider; após login/logout, a checagem é nova. Modelos salvos permanecem selecionados até a descoberta confirmar disponibilidade, sem reescrever conversa restaurada por fallback. Nenhum desses estados envia prompt ou abre MongoDB. O cartão de estado limita o texto a duas linhas e mantém o conteúdo integral na dica para que a ação continue visível em painéis estreitos.

Evidência P7-AUTH: `AgentChatHostUiTests.HostedPanelRendersDockedOrAsItsOwnSurfaceInBothThemesAndThreeSizes` passou 1/1; PNGs reais `agent-host-not-checked-{Light|Dark}-{960x620|1366x768|1920x1080}` foram gerados e inspecionados. Em 960×620, verificação adicional confirma que **Verificar disponibilidade** permanece dentro do cartão; 1366/1920 preservam layout encaixado. Não foi alterada a navegação nem o tema; leitor de tela e diálogos nativos continuam pendentes.
- **Status global:** na inicialização a contagem de recuperação pendente de credenciais aparece como operação de baixa prioridade na barra de status (aviso com a contagem, sucesso quando zero, erro com texto fixo), sem bloquear a inicialização.

Evidência: `AgentChatHostUiTests` gerou 22 PNGs `agent-host-{unavailable|not-checked|conversation}-{Light|Dark}-{960x620|1366x768|1920x1080}` e `agent-host-conversation-*-1366x768-{1.5|2}`. Inspeção: 1366/1920 encaixado com editor ≥ 400 e divisor visível; 960 como superfície própria com Explorer preservado; 200% sem corte do compositor ou de **Revisar envio**. A inspeção corrigiu o estado "não verificado" exibido em vermelho e o retorno ao editor apenas como "×" na superfície própria. Observação registrada: o **Assistente IA** local (fase 8, coluna de 240 na aba) coexistia com o painel Agente IA; o usuário decidiu removê-lo em 25/09/2026 (ver abaixo). Leitor de tela, IME e diálogos nativos continuam pendentes.

### Remoção do Assistente IA por aba (P7-L06-CLEANUP) — 25/09/2026

- **Layout:** `WorkspaceTabView` perde a coluna fixa de 240 (título "Assistente IA · por aba", cartões de prévia/proposta, compositor e aviso). O `SplitGrid` editor/resultados ocupa toda a largura da aba; divisor de 5, mínimos 180/140 e proporção persistida não mudam. Nenhum token, estilo ou atalho era exclusivo do painel (`section-title`, `card`, `quiet`, `muted`, `warning` e `metadata` seguem em uso); 67 chaves de localização exclusivas saíram dos quatro idiomas. "Usar o modelo local no Assistente IA" e a dica do modelo recomendado passam a citar o **Agente IA**.
- **Preferências e conexões (revisão CHANGES_REQUESTED):** saem os controles "Permitir que o Assistente IA use o contexto da aba", "Também incluir o JSON do Input" e "Permitir contexto da aba no Assistente IA local para esta conexão", com seus textos de escopo (6 chaves nos 4 idiomas). A nota do topo das preferências agora diz que o autocomplete usa só as opções de contexto listadas e que o Agente IA pede explicitamente o escopo de contexto de cada mensagem e o mostra antes do envio. PNGs inspecionados: `local-ai-context-settings-{Light|Dark}`, `autocomplete-settings-Dark-520-2` (nota longa quebra sem corte a 200%) e `local-ai-connection-context-{Light|Dark}` (o formulário termina em Favorita/Somente leitura); a barra de rolagem vertical do formulário de conexão encostava na borda direita dos campos da coluna direita (corrigido em P7-CL5-02 com calha direita de 16).
- **Efeito no Agente IA:** a área de abas e o limite de 690 para o modo encaixado não mudam; com o painel encaixado em 1366, o editor e os resultados da aba ganham as 240 unidades que a coluna antiga ocupava.
- **Evidência:** PNGs regenerados após a remoção e inspecionados: `workspace-Light-1366x768-1`, `workspace-Light-960x620-1`, `workspace-Dark-960x620-1` (editor e resultados até a borda direita, sem coluna, botões e metadados legíveis) e `agent-host-conversation-{Light|Dark}-{1366x768|960x620}` (1366 encaixado com editor da aba mais largo e divisor visível; 960 como superfície própria com **Voltar ao editor**; conversa, contexto, consentimento e **Revisar envio** legíveis nos dois temas). Observação (corrigida em P7-CL5-02, ver abaixo): no escuro o chip **Externo** do cabeçalho do Agente IA ficava sem fundo visível. As capturas antigas `ui-evidence/ai-chat-localization/` são restos de execuções anteriores e não são mais geradas. Leitor de tela, IME e diálogos nativos continuam pendentes.

### Conta Claude pelo Claude Code e modo em uso (P7-CL5-02) — 25/09/2026

> **Retificação vigente (02/10/2026):** o texto abaixo descreve o desenho de UI da revisão de 25/09 e seus chips “Claude · assinatura”/“Claude · API” são histórico do protótipo. No painel Agente IA, Claude segue pela CLI oficial com métodos nativos preservados, sem cobrança presumida e sem fallback à API. O acordo comercial Anthropic continua gate aberto; consulte o estado atual no início deste documento e na [Fase 5](phases/phase-05-v0.9.0/README.md).

- **Chips:** `Border.type-chip` do painel e das configurações passa a usar `HoverBrush` com contorno `DividerBrush` (antes `SecondaryBackground`, quase igual ao painel no escuro — o chip **Externo** ficava sem fundo). Linha própria abaixo do título com **Externo/Local** e o chip de modo textual **Claude · assinatura**/**Claude · API** (família do composition root + método de autenticação; local não tem chip). A lista do seletor mostra nome + chip; a caixa fechada mostra só o nome (`SelectionBoxItemTemplate`) para não truncar em 320–380.
- **Aviso de leitura:** borda `WarningBrush`, texto `PrimaryText` 12, até 4 linhas no cabeçalho e repetido na prévia: pasta do workspace ou "Sem pasta de workspace: toda leitura pedirá aprovação (negada nesta versão)", sempre com o envio à Anthropic pelo Claude Code e "Mudança de pasta vale só para sessões novas"; com sessão aberta em outra pasta, acrescenta "A sessão atual continua com: …". A pasta é capturada na thread de UI ao iniciar a sessão (`AgentSessionOptions.WorkingDirectory`). Pasta recusada pelo provider aparece com o motivo ("… foi recusada porque contém ~/.ssh; usando a pasta dedicada vazia do app — toda leitura pedirá aprovação"); sem pasta utilizável o status fica em ErrorBrush + texto, **Revisar envio** desabilitado e sem **Verificar disponibilidade**. O aviso aceita até 8 linhas (tooltip com o texto completo).
- **Configurações:** nome do produto sempre por `Branding.ProductName` (inclusive "Ferramentas do KapibaraStudio" em Capacidades); motivo de indisponibilidade exibido como frase própria (inicial maiúscula). Seção "Conta pela CLI oficial" (Status, Autenticação com explicação de bloqueio em borda `WarningBrush`, Tipo de conta), **Entrar pelo Claude Code…** (primário), **Parar de aguardar**, **Sair…**, textos de ajuda, comando manual com **Copiar comando** e avisos fixos; **Testar conexão** para todo provider; rodapé com `ProgressBar` indeterminada + frase de progresso (`LiveSetting=Polite`). A lista de providers fica desabilitada durante operações. Nenhum e-mail, organização ou token; só versão, nível da assinatura, nome da variável e caminho do executável.
- **Logout:** `AgentCliSignOutWindow` 480 de largura, título 16, mensagem em borda `WarningBrush` explicando o efeito global; **Cancelar** com foco inicial, Escape/fechar cancelam; confirmação em `Button.danger`.
- **Cartões de tool:** leitura nativa ("Leitura nativa Read · Executada pelo próprio Claude Code, fora das ferramentas do KapibaraStudio") separada de ferramenta do produto ("autorização, aprovação e auditoria valem"); códigos conhecidos descritos, desconhecidos como "código X".
- **Conexões:** formulário do cadastro com calha direita de 16 para a barra de rolagem.

Evidência: `AgentCliAccountUiTests` gerou 70 PNGs `agent-cli-*` (configurações em 9 estados × claro/escuro × topo/fim a 660 × 560; assinatura/bloqueado a 520 × 420 em 200%; diálogo de logout claro/escuro; host 960 × 620, 1366 × 768 e 1366 a 200% em assinatura com/sem pasta e API; chat com leitura nativa e cancelamento após envio a 400 e 480@200%). Inspeção corrigiu o seletor truncado ("Cla…" só com o chip) e o texto de tipo de conta ambíguo no estado bloqueado. Limites: fixtures sintéticas (gerenciador de conta roteirizado, sem processo), sem Claude Code real, Linux, leitor de tela nem diálogos nativos.

## Agente IA integrado (ADR-056) — implementação em validação, 27/09/2026

**Código presente no worktree, PNGs desta revisão ainda não inspecionados.** Decisão em [ADR-056](10-decisoes-arquiteturais.md#adr-056--agente-ia-integrado-conversas-persistidas-permissões-por-provider-modos-e-propostas-de-edição-26092026), tarefas P7-CL7-07 (painel, histórico, modos, chips, configurações/permissões) e P7-CL7-08 (diff no editor) em [23](phases/phase-05-v0.9.0/README.md). A UI CLP substitui o chat por aba, **Revisar envio**, o cartão de prévia e o consentimento por sessão descritos acima, que ficam como histórico. Tokens, tipografia (Inter 13, metadata 12, título 16, código 14), espaçamento múltiplo de 4, hospedagem (encaixado 380, mín. 320, máx. 560; superfície própria abaixo de 690 na área de abas) e **Ctrl+Shift+A** continuam como requisitos de inspeção.

### Painel global

- **Escopo:** um único painel ligado ao `WorkspaceViewModel`; trocar de aba não troca a conversa nem limpa o histórico. O arquivo e a aba ativos aparecem como chip e são capturados no envio. O resultado de um turno só atualiza a conversa que o originou, mesmo que outra esteja visível.
- **Cabeçalho:** título da conversa (16, truncado com dica), **Histórico** (flyout: busca, lista virtualizada por data, renomear inline, apagar com confirmação), **Nova conversa**, **Configurar…** e fechar ("×" com nome acessível). Ordem de Tab: título, Histórico, Nova conversa, Configurar, fechar.
- **Linha de disponibilidade:** texto 12 com o estado (Verificando…, Disponível, Não conectado, Claude Code ausente, Falha/tempo esgotado), o chip de modo (**Claude · assinatura**) e **Externo**; nos estados de falha, botão **Tentar novamente** na mesma linha. Verificando usa texto neutro com indicador pequeno, nunca vermelho; falha usa `ErrorBrush` + texto. `LiveSetting=Polite`. Não impede digitar no composer.
- **Resumo de permissões:** linha metadata ("Externo · leitura MongoDB · arquivos do workspace") com o link **Permissões**, que abre a `AgentPermissionsWindow` na seção correspondente. No Linux acrescenta "Tools do KapibaraStudio indisponíveis neste sistema".
- **Sem consentimento:** o composer é substituído por um cartão com texto curto e o botão primário **Configurar permissões**; não há envio parcial.
- **Chips de contexto** acima do composer: `Border.type-chip` com ícone de tipo, nome e qualificador (`clientes.json · ativo`, `developercluster › CakeShop`) e botão × com nome acessível "Remover <nome> do contexto"; chip acima do limite ou excluído aparece em `WarningBrush` + texto. **+ Anexar** abre menu com "Arquivo do workspace…" (picker filtrado por permissões e exclusões) e "Arquivo externo…" (diálogo nativo; item desabilitado com dica quando não permitido). Os chips mostram exatamente o que será enviado.
- **Composer:** caixa multilinha; abaixo, seletores de **modo** (Agente, Planejamento, Automático, Solicitar confirmações; Automático com a dica "aplica no editor sem salvar; reversível") e de **modelo**, e **Enviar**/**Cancelar execução**. Enter quebra linha; **Ctrl+Enter envia direto** com foco no composer (sem prévia); Escape no composer não cancela turno. Somem **Revisar envio**, o cartão de prévia e o checkbox de consentimento.
- **Cartões no histórico:** tool (existente); confirmação inline com alvo em código 14, **Rejeitar** com foco inicial e **Aprovar uma vez**; proposta de edição com arquivo, `+N −M`, **Revisar** (abre ou foca a aba com o diff), **Aplicar tudo** e **Descartar** — no Automático, **Manter**/**Reverter**. Estados textuais: registrada, aplicada, parcialmente aplicada, descartada, desatualizada.
- **Estados adicionais:** histórico ilegível (erro visível, conversas preservadas, sem sobrescrever), falha ao salvar histórico, resume falhou ("nova sessão iniciada com resumo local"), histórico desligado ("O histórico não está sendo guardado").

### Janela de permissões (`AgentPermissionsWindow`)

Modal proprietária aberta por **Configurações → Claude (assinatura) → Permissões…** ou pelo link do painel. A `AgentSettingsWindow` fica com o que é do provider (autenticação, disponibilidade, modelo padrão, modo padrão, avisos) e ganha o botão **Permissões…**. Navegação por seções à esquerda (lista com setas) e conteúdo à direita; **Salvar** e **Cancelar** no rodapé; Escape cancela; alterações só valem ao salvar. Medidas iniciais propostas: 760 × 600, mínimo 600 × 460, a confirmar com PNGs.

| Seção | Controles |
| --- | --- |
| Envio de dados | Consentimento de destino externo (checkbox, data e hora do consentimento, revogar); dados enviáveis: mensagem, arquivo ativo, arquivos do workspace, anexos externos, metadados da aba, schema inferido |
| Workspace e arquivos | Usar a pasta de Arquivos (caminho exibido); globs de exclusão (lista editável, padrão `.env`, `*.pem`, `*.key`, `**/secrets/**`); leitura de arquivos pelo agente (Read/Glob/Grep) com aviso de envio ao provider externo; propostas de edição no arquivo ativo, inclusive buffer sem caminho, / em outros arquivos do workspace |
| Anexos externos | Permitir "Arquivo externo…"; aviso de limite (256 KB por arquivo, 1 MB por mensagem) |
| Contexto automático | Arquivo ativo e metadados da aba como chips automáticos |
| Tools do KapibaraStudio | Conexões acessíveis (todas / selecionadas, lista com checkboxes); grupo **Somente leitura** com cada tool; grupo **Escrita** com as tools listadas e o rótulo "Não disponível nesta versão" |
| Confirmações | Operações que pedem confirmação; explicação do modo Solicitar confirmações |
| Histórico e privacidade | **Não guardar histórico** (opt-out) e **Apagar histórico** (`Button.danger` com confirmação); texto informando que anexos e credenciais nunca são guardados |

Nenhum controle depende só de cor; a escrita indisponível é indicada por texto, não só pelo estado desabilitado.

### Revisão por diff no editor

- `EditProposalReviewViewModel` por aba; proposta para arquivo não aberto abre-o numa aba e foca.
- Renderer de fundo pinta linhas removidas com o tom de erro e adicionadas com o tom de sucesso do tema (contraste verificado nos dois temas); as linhas propostas aparecem como bloco fantasma somente leitura, no padrão do overlay de sugestão inline.
- Cada hunk tem barra compacta **Apply**/**Revert**, acessível por teclado; a barra superior da aba tem **Aplicar tudo**/**Reverter tudo**, a alternância **Original ↔ Proposta** e a contagem de hunks pendentes.
- Apply entra no undo do editor; hunk cujo original mudou aparece como **desatualizado** (`WarningBrush` + texto) e não é aplicado. No Automático, os hunks já chegam aplicados com **Manter**/**Reverter**.
- Nada é salvo em disco: a aba fica suja e o usuário salva normalmente. Granularidade por hunk; linha a linha fica pendente "quando tecnicamente possível".

**Evidência exigida (futura):** PNGs reais claro/escuro do painel (vazio, sem consentimento, conversa com chips, confirmação inline, proposta), do flyout de histórico, da `AgentPermissionsWindow` (cada seção, 100% e 200%) e da revisão por diff (hunk pendente, aplicado, desatualizado), em 960/1366/1920; foco e teclado; homologação de leitor de tela e diálogos nativos separada.

## Contas próprias — planejamento prioritário em 25/09/2026

O [sublote 7B](phases/phase-05-v0.9.0/README.md) e o [bloco CL — Integração Claude](backlog/integracoes-agentes/historico-integracoes.md#plano-e-andamento) (substitui o antigo sublote 8B) priorizam conta Codex/ChatGPT e conta Claude pelo modo Claude Code. A configuração atual acima continua sem login de assinatura. Na implementação futura, o descritor habilita a ação de autenticação oficial somente após os gates do respectivo adapter; o Slop não apresenta formulário de senha/código/token nem WebView — para o Claude Code, a ação abre um terminal visível com o comando oficial (ver abaixo), nunca um formulário do Slop. Mostrar estados desconectado, autenticando, cancelado, expirado, cota atingida e indisponível com motivo textual e ação; abrir configurações não inicia login. API Key é alternativa explícita, sem troca automática de cobrança/contexto. Preservar foco ao retornar do fluxo oficial e nunca exibir segredo em status ou capturas. Exigir PNGs nos dois temas e homologação manual separada; esta alteração documental não entrega UI.

### Claude em dois modos — regra de UI planejada em 25/09/2026

Para o [bloco Integração Claude](backlog/integracoes-agentes/historico-integracoes.md#plano-e-andamento), o modo em uso aparece em texto, nunca só por cor. A configuração mantém autenticação, disponibilidade e modelo; a administração do acesso a dados e arquivos está na tela **Permissões**. O botão de entrada usa o fluxo oficial do Claude Code e não exibe segredos. **Logout** exige confirmação do efeito global. Ferramentas nativas de execução, escrita e rede ficam ausentes da allowlist ([ADR-054 revisada](10-decisoes-arquiteturais.md#revisão-de-25092026-mesma-data-decisão-posterior-do-usuário--riscos-residuais-do-threat-model-não-aceitos-escopo-revertido)). Pela [ADR-056](10-decisoes-arquiteturais.md#adr-056--agente-ia-integrado-conversas-persistidas-permissões-por-provider-modos-e-propostas-de-edição-26092026), a confirmação por chamada usa cartão inline **Aprovar uma vez**/**Rejeitar**, com **Rejeitar** como ação segura; não há opção "sempre". O código está em validação e requer PNGs reais claro/escuro e homologação nativa.

## Planejamento original v0.11.0 — chat nativo de agentes (atual Fase 5 / v0.9.0) (histórico do plano)

O [plano MCP/agentes](phases/phase-05-v0.9.0/README.md) prevê um painel Avalonia que consome o Agent Runtime. O protótipo local atual continua experimental e seu diff revisável deve ser preservado durante a adaptação. Não abrir ChatGPT/Claude em WebView nem acoplar views ao protocolo de um fornecedor.

## Atualização de permissões e aprovação de tools Claude — 28/09/2026

### Histórico de interface Copilot — snapshot de 28–29/09/2026

> Este trecho registra estados intermediários. As frases abaixo sobre `CopilotSessionNotHomologated`, provider indisponível e conta `NotLoggedIn` foram substituídas pelo status corrente no início deste documento e na [meta P7-COP](phases/phase-05-v0.9.0/README.md#meta-p7-cop--github-copilot-por-assinatura). Preserve-se o histórico de testes e UI, sem usá-lo como status atual.

A configuração distingue a assinatura Copilot da API OpenAI e informa que a conta pertence ao provider externo. O comando de login abre a CLI oficial instalada pelo usuário; o app não coleta credenciais nem exibe saída que possa contê-las. O teste de conexão consulta autenticação e modelos elegíveis via SDK, sem enviar prompt/contexto ou iniciar inferência. Mesmo com modelos descobertos, Copilot permanece indisponível no seletor (`CopilotSessionNotHomologated`) até os gates da [meta P7-COP](phases/phase-05-v0.9.0/README.md#meta-p7-cop--github-copilot-por-assinatura) passarem. Logout explica o efeito local e pede confirmação. Com histórico desativado, o adapter configura SessionFs/SQLite em memória; para histórico ativo, o SessionFs persistente usa diretório próprio do app, com limites e exclusão por sessão. Testes Windows oficiais confirmaram escrita/resume/erase básicos nesse SessionFs; falhas/concorrência do runtime e consulta Copilot com MongoDB de teste ainda estão pendentes. A exclusão do histórico Copilot remove a sessão nativa antes do registro local e apaga o store por sessão; falha ou sessão ilegível deve ser mostrada como erro sem indicar que a conversa foi apagada. PNGs Headless pt-BR claro/escuro foram inspecionados; leitor de tela e diálogos nativos permanecem pendentes.

**Ajuste operacional em 29/09/2026:** o cliente de sessão usa a mesma autenticação e o `COPILOT_HOME` oficial da CLI. A UI continua indicando destino externo e indisponibilidade até a sessão autenticada, retenção e permissões serem comprovadas; o último estado observado da conta é `NotLoggedIn`. Não mudar o texto para conectado ou disponível com base apenas na lista de modelos anterior.

**Atualização de apagar histórico em 29/09/2026:** a janela de Permissões agora aciona a operação coordenada pelo chat; o estado nativo é apagado antes de LiteDB. Qualquer falha fica visível e conserva as referências para retry. Histórico ativo ainda fica sob o `COPILOT_HOME` compartilhado; isolamento persistente continua bloqueando a homologação do erase.

A tela existente de permissões inclui controles separados para leitura de arquivos, execução de comandos, escrita nativa e rede. Comandos/escrita/rede começam desabilitados e informam que execução aprovada usa os privilégios do usuário, sem sandbox. O cartão inline de aprovação mostra categoria, nome da operação e argumentos; oferece **Permitir uma vez**, **Sempre permitir nesta sessão** (só leitura) e **Negar**. Em modo Automático, operações modificadoras continuam mostrando o cartão. O estado de sessão perdida aparece como aviso na conversa.

Na revisão de 28/09, rótulos do painel de agentes que apareciam como marcadores `[[chave]]` receberam traduções explícitas nos quatro idiomas suportados, incluindo chips de contexto, permissões, disponibilidade e histórico. O teste de catálogo verifica a resolução das chaves; a inspeção visual da versão recompilada ainda está pendente.

Evidência desta revisão: os testes renderizaram e foram inspecionados os PNGs `agent-permissions-{Light|Dark}-660x760.png` e `agent-cli-chat-native-tools-{Light|Dark}.png`; a tela estreita manteve o aviso, o cartão completo e o compositor acessíveis. Os PNGs são sintéticos e não representam uma sessão real Claude Pro/MongoDB. Consulte [Claude no KapibaraStudio](backlog/integracoes-agentes/historico-integracoes.md#plano-e-andamento) para os limites e a homologação pendente.

A futura UI deve mostrar provider/modelo e **Local** ou **Externo**, autenticação somente quando oficialmente suportada e capabilities disponíveis. Conexão do provider não autoriza envio de dados. Exibir contexto autorizado, destino fixo, estado de sessão, streaming, ferramentas com duração/resultado, aprovação, cancelamento e resultado incerto de escrita. A aprovação mostra alvo, risco e mudança concretos; negar/expirar não executa. Trocar provider não troca o destino silenciosamente nem compartilha cancelamento entre abas.

**Atualização P7-COP (29/09/2026):** a confirmação por chamada do registry é dinâmica e separada da janela de permissões persistentes. Para categorias optadas no turno, o cartão mostra provider, ferramenta e argumentos exatos, com ações **Permitir uma vez** e **Negar**; não oferece aprovação de sessão. Recusa e expiração usam estados próprios e não abrem a janela persistente. Ausência ou falha da confirmação/auditoria mantém a chamada bloqueada e identificada como confirmação indisponível. Salvar permissões persistentes só afeta novos turnos; não repete a chamada negada.

Preservar tokens, tipografia, atalhos e foco existentes; reservar ações de chat ao seu escopo de foco, com anúncio acessível de progresso sem narrar cada token. Estados obrigatórios: vazio, indisponível, desconectado, autenticando, cancelado, credencial expirada, aguardando aprovação, erro recuperável, cofre indisponível e dados não autorizados. Nenhum estado depende apenas de cor. Não instalar novo atalho global sem revisão de conflitos.

Evidência futura: PNGs reais nos dois temas, tamanhos 960×620, 1366×768 e 1920×1080, escalas 100/150/200%, textos longos/localização e uso completo por teclado. O plano não produz evidência de UI implementada; leitor de tela, login e diálogos/cofres nativos pertencem à homologação real. O workflow fechado continua na [Fase 7/v0.11.0](phases/phase-07-v0.11.0/README.md).
