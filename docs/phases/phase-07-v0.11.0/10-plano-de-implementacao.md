# Plano executável da v0.11.0

**Em desenvolvimento inicial.** O lote 0 tem spikes isolados; o lote 1 tem adapters de cofre compostos, ainda sem homologação nativa completa; e o lote 2 tem persistência de política, evaluator e registry interno parcial. Nenhum lote está concluído; MCP, runtime, chat, providers, protocolo externo, migração de credenciais, tools expostas e todos os ACs continuam pendentes. Prefixo de projetos omitido nas tabelas: `EsilvaSoft.SlopStudio`. Cada lote deve terminar com revisão de diff, documentação e evidência do seu gate antes de liberar o seguinte.

**Prioridade vigente — decisão do usuário em 25/09/2026 (posterior):** **finalizar a integração com Claude**, priorizando a assinatura Claude Pro pelo binário oficial do Claude Code, finalizando o que já está aberto e sem criar frentes desnecessárias. Ver [Bloco prioritário — Integração Claude](#bloco-prioritário--integração-claude-25092026) e [23](23-integracao-claude.md). Esse bloco substitui o sublote 8B; o 7B e os lotes 10/11/12 ficam subordinados a ele. Nada implementado ou homologado por esta decisão. **Atualização de 26/09/2026:** provider Claude Code e conta pela CLI já existem em código (commits `63c162d`/`6e07b41`, sem homologação); a frente ativa passa a ser o [polimento do Agente IA](#polimento-do-agente-ia--sublotes-clp-0clp-7-26092026) (ADR-056, CLP-0..CLP-7).

**Prioridade anterior (mesma data, parcialmente substituída):** viabilizar o uso das contas próprias **Codex/ChatGPT (7B)** e **Claude (8B)**, por integrações oficiais e sob os gates específicos abaixo. O 8B passa a integrar o escopo condicional da fase; a exclusão anterior de assinatura Claude é substituída por esta decisão. API Key continua baseline técnica disponível e alternativa escolhida explicitamente; não atende sozinha à prioridade de contas. Os dois acessos continuam **não implementados e não homologados**. Mantêm-se o chat completo (lote 6 ampliado) e a homologação de login somente manual com contas próprias e dados sintéticos, conforme [21](21-homologacao-manual-login.md). Importação de tokens/sessões e OAuth próprio permanecem fora do escopo.

## Agentes incorporados ao plano

A execução de cada lote usa os responsáveis, colaboradores, revisores e evidências da [matriz 20 — Agentes e execução](20-agentes-e-execucao.md). A sessão principal segue [goal-orchestrator](../../../agents/goal-orchestrator.md) e o [protocolo comum](../../../agents/phase-7-protocol.md); QA e revisão são gates de todo lote. [Claude Code](../../../agents/claude-code.md) usa os mesmos contratos por adapters locais. Esta preparação documental não fecha lote nem AC.

Os responsáveis são: arquitetura (0), persistência/segurança (1/11), Tool Registry (2), MCP (3/4), runtime (5), UI/UX (6, com persistence-security-agent em credenciais e consentimento), providers (7A/7B/8/CL, histórico 8B), ONNX (9), domínio MongoDB (10) e QA (12). O sublote 7B e o bloco CL têm `agent-provider-agent` como responsável, `persistence-security-agent` em confinamento/keyring e `ui-ux-agent` na entrada de UI. Cada despacho inclui lote, dependências, arquivos exclusivos, requisitos/ACs e cenários de falha; a matriz detalha as entregas e a revisão independente.

## Sequência e dependências

```text
0 Contratos/spikes -> 1 Cofre e identidade -> 2 Registry/políticas/auditoria
                                              -> 3 MCP read-only -> 4 Interoperabilidade
                                              -> 5 Runtime -> 6 Chat nativo
                                                           -> 7A OpenAI API / 8 Claude / 9 Local
                                                           -> 7B conta Codex/ChatGPT (condicional; gates próprios)
                                                           -> CL Integração Claude (prioritário; substitui 8B; passos 1–15 em 23)
4 + 6 + 7A + 8 + 9 -> 10 Escritas protegidas -> 11 Hardening -> 12 Aceite integrado
Ordem vigente: CL (reaproveitando partes do 3/6/10) -> 10 restante -> 11 -> 7B -> 12
```

Cofre, permissões e auditoria vêm antes de expor dados. **A ordem vigente é a do bloco CL abaixo**; o parágrafo seguinte descreve a priorização anterior de 7B/8B e vale apenas para o 7B, agora subordinado. Priorização anterior: (1) revalidar fontes, versões e modalidade de integração dos dois providers; (2) spikes isolados de autenticação, confinamento e armazenamento; (3) contratos comuns e adapters; (4) configuração na UI; (5) homologação manual. Spikes de 7B/8B podem avançar antes de concluir todos os lotes 7A/8, sem dados MongoDB nem entrada de produto. A habilitação depende dos gates de 0/1/2/5/6 e de 3 quando houver ponte MCP. Lotes 10/11 podem continuar nas partes independentes, mas não substituem essa prioridade. Se uma via for inviável, registrar evidência, impacto, alternativa e decisão explícita do usuário para adiar; entregar apenas API Key não conclui o objetivo de contas. Sem gate aprovado, o adapter permanece indisponível e a IDE continua operacional.

| Lote / objetivo | Dependências | Projetos, interfaces e classes principais | Testes e critério de conclusão |
| --- | --- | --- | --- |
| 0 — Fechar contratos e spikes de integração | Este plano | Core/Agents: DTOs; Application/Agents: portas; spike isolado de SDK/processo sem dados privados | Fixar binário Codex/schema, SDK Anthropic, pacote MCP e lock/transitivas; provar controle de ferramentas nativas, backend seguro e MCP dual-era. ADRs revisadas e licenças aprovadas; nenhuma dependência promovida por suposição |
| 1 — Credenciais e principal autenticado | 0 | Parcial: Application `ISecretStore` e `IAgentCredentialProvider`; Core `SecretReference`; adapters de cofre e provider de credenciais compostos no DI. Pendentes: identidade integrada, migração versionada e homologação nativa completa. | Evidências históricas e limites no relatório 17; não equivalem a gate aprovado. Ainda exigir disponibilidade/ausência/bloqueio reais, falhas da migração, reinício, exclusão e concorrência nos dois SOs; sem fallback plaintext e com perfis legados recuperáveis. |
| 2 — Registry, autorização e auditoria | 1 | Parcial: contratos, política persistida, evaluator, ledger versionado e facetas DI no owner único; registry interno com `list_connections` fora do DI. Pendentes: integrar auditoria ao pipeline, identidade, schemas das demais tools, controle de saída, executor BSON literal e demais handlers. | Evidências por incremento em 17/18/19; nova execução deve registrar seus próprios resultados. Ainda exigir entradas extras/ENV/JS negadas, namespace e read-only, negação de saída, auditoria indisponível, mesmo handler/política para todos os ingressos, schemas versionados e fixtures por tool. Nenhuma tool é exposta por MCP. |
| 3 — MCP somente leitura | 2 | Novo McpServer: `McpToolAdapter`, `AgentBrokerClient`; Infrastructure: `AgentBrokerHost`; primeiros handlers do catálogo | STDIO sem ruído, cliente sem grant, dois clientes, host fechado, versões IPC incompatíveis, BSON/limites/deadlines. Descobrir e executar conjunto inicial sem segredos nem escrita |
| 4 — Compatibilidade externa e recovery | 3 | McpServer e broker; fixtures separadas MCP 2026-07-28/2025-11-25 | Codex e Claude Code/Desktop por configuração explícita nas versões homologadas; cliente de referência dual-era. Crash/EOF/revogação/cancelamento e ausência de replay. Publicar matriz exata; Copilot só anunciado se efetivamente testado |
| 5 — Runtime normalizado | 2 | Application: `IAgentRuntime`, `IAgentProvider`, `IAgentSession`, `IAgentContextProvider`; `AgentRuntime`, `AgentContextProvider`, filas limitadas | Provider falso determinístico, deltas/duplicação/ordem, deadlock tool-result, isolamento e starvation. Eventos e estados terminais conforme contrato; nenhum DTO externo fora do adapter |
| 6 — Chat completo na UI: configuração, aprovação e consentimento nativos | 5 | Desktop: `AgentChatViewModel`, `AgentSettingsViewModel`, `AgentApprovalViewModel`, `AgentChatPanel` hospedado na `MainWindow` (dock recolhível) e Views; recursos/localização; portas de produção do Desktop | Escopo completo em [Lote 6 ampliado](#lote-6-ampliado--chat-completo-na-ui-da-aplicação). Fluxos teclado/foco, troca de provider sem transferência de dados, aprovação expirada, estados vazios/erro, PNGs nos dois temas em 960/1366/1920. UI usa apenas runtime/capabilities; controles não prometem auth inexistente. Leitor de tela e diálogos nativos: homologação pendente |
| 7A — Adapter OpenAI (API Key, baseline) | 0,1,5,6; 3 quando usa ponte MCP | `OpenAiAgentProvider` e sessão API direta em Infrastructure.Agents/OpenAI; composition root | API Key no cofre, credencial inválida/expirada, streaming, tools, orçamento e interrupção; homologação real com chave autorizada. Registrar capabilities e aceite efetivos; API Key não comprova login ChatGPT |
| 7B — Login por conta OpenAI via Codex App Server (**condicional e prioritário**) | Gates de 0/1/2/5/6; 3 se MCP; preservar 7A, sem exigir sua homologação API para iniciar spike | `CodexAppServerClient` e `CodexAgentSession` em Infrastructure.Agents/Codex; composição opt-in; entrada de UI por capability | Ver [Sublote 7B](#sublote-7b--login-por-conta-openai-condicional). Só é declarado entregue após confinamento provado e homologação manual do [roteiro 21](21-homologacao-manual-login.md); sem prova, permanece desabilitado e API Key é o caminho único |
| 8 — Adapter Claude | 0,1,5,6 | Infrastructure.Agents/Anthropic: `ClaudeAgentProvider`, `ClaudeAgentSession`; API C# e loop no runtime | Chave inválida, stream fragmentado, argumentos completos, tool-result, timeout e orçamento. Sessão lógica e seleção de modelo. É o modo **Anthropic API**, separado da assinatura; a assinatura é o modo Claude Code do bloco CL, sem fallback entre eles |
| 8B — Conta Claude pelo runtime oficial (**substituído pelo bloco CL em 25/09/2026**) | — | Ver [bloco CL](#bloco-prioritário--integração-claude-25092026): `ClaudeCodeAgentProvider` em Infrastructure.Agents/ClaudeCode, subprocesso do binário oficial; Agent SDK em sidecar rejeitado (ADR-053) | Gates GCL-1..8 em [23](23-integracao-claude.md#gates); casos C-01..C-34 do [roteiro 21](21-homologacao-manual-login.md#casos-do-modo-claude-code-assinatura) |
| 9 — Fachada local | 5,6 | Application: `LocalAgentProvider` sobre LocalAi.Core e serviços existentes; Infrastructure.LocalAi preservada | Offline, modelo ausente, FIM sem Chat/ToolCalling, preempção e autocomplete simultâneo. Nenhuma rede/fallback externo nem CTS global; assistente atual preservado |
| 10 — Escritas e índices controlados | 4,6,7A,8,9; gates de 2 | Application handlers; Infrastructure: mutações unitárias/precondições e auditoria; Desktop aprovação | insert/update/delete/create/drop index contra MongoDB descartável real; read-only, RBAC, `_id_`, concorrência, replay e crash pós-envio. Ticket único e pré-condição atômica; resultado incerto explícito |
| 11 — Hardening e distribuição | 10 | Infrastructure, Infrastructure.Agents, McpServer, Desktop, eng/CI | Threat model, payload/stream hostil, secret scans, ambientes mínimos de subprocessos, atualização proxy/IDE, rollback de pacote sem corromper sessão. SBOM/avisos/tag/hash revisados; IDE funciona sem agentes |
| 12 — Aceite completo | 11; resolução explícita da prioridade 7B/CL (histórico 8B) | UnitTests/Benchmarks e harness de integração a decidir conforme volume; docs | Evidências AC-01..20, registros da homologação manual dos dois acessos por conta, ou impedimentos e adiamento explicitamente decidido, plataformas nativas, versão/conta/modelo registrados com dados sintéticos. Nenhuma pendência de segurança tratada como homologada por mocks |

## Bloco prioritário — Integração Claude (25/09/2026)

Decisão do usuário: finalizar a integração com Claude, priorizando a **assinatura Claude Pro** pelo binário oficial do Claude Code executado como subprocesso ([ADR-053](../../10-decisoes-arquiteturais.md#adr-053--claude-via-assinatura-usando-o-binário-oficial-do-claude-code-como-subprocesso-25092026)), com o modo **Anthropic API** (lote 8) separado e **sem fallback silencioso** entre eles. Ferramentas nativas do Claude Code entram com aprovação por chamada ([ADR-054](../../10-decisoes-arquiteturais.md#adr-054--ferramentas-nativas-do-claude-code-com-aprovação-por-chamada-25092026)) — revisto no mesmo dia (ADR-054 revisada: só leitura pura) e em 26/09/2026 pela ADR-056. Arquitetura, fatos oficiais, tarefas `P7-CLx-nn`, gates GCL-1..8, riscos e limitações estão em [23 — Integração Claude](23-integracao-claude.md). Estado: **nada implementado, nenhum gate aprovado, nenhum AC aprovado**.

| Passo (ordem obrigatória) | Tarefas | Reaproveita |
| --- | --- | --- |
| 1 Detecção do Claude Code | P7-CL0-01 (spike), P7-CL1-01 | — |
| 2 Login oficial Claude Pro | P7-CL1-02 | — |
| 3 Validação do estado de auth | P7-CL1-03 | — |
| 4 ClaudeProvider (assinatura) | P7-CL2-01, P7-CL2-02 | Contratos do lote 5, catálogo do Desktop |
| 5 Chat com Claude | P7-CL3-01 | Runtime (5) e painel hospedado (6) |
| 6 Streaming | P7-CL3-02 | Filas limitadas do runtime |
| 7 Continuidade de sessão | P7-CL3-03 | — |
| 8 Permissões | P7-CL0-02 ([threat model — documento 22](22-threat-model.md), único local do documento, produzido no passo 0 e obrigatório antes do CL4), P7-CL4-01, P7-CL4-02 | Coordenador/bridge/`AgentApprovalWindow` do lote 10; broker do lote 3 |
| 9 Tool calls | P7-CL4-03 | Registry (2), McpServer (3), consentimento de schema |
| 10 Cancelamento | P7-CL3-04 | CTS por execução do runtime |
| 11 Integração com workspace | P7-CL5-01 | Workspace local (ADR-045) |
| 12 Ajustes de UI | P7-CL5-02 | `AgentSettingsWindow` e painel do lote 6 |
| 13 Tratamento de erros | P7-CL5-03 | Correção de chave recusada também no modo API/OpenAI |
| 14 Testes | P7-CL6-01, P7-CL6-02 | Varredura de canários do P7-L11-QA |
| 15 Documentação | P7-CL6-03 | — |

**O que continua do plano anterior, e em que ordem:**

1. **Bloco CL** (acima), absorvendo do lote 10 somente a ligação da infraestrutura de aprovação já implementada (coordenador, bridge, interaction authority, fonte de detalhes) e do lote 3 a composição do broker no Desktop — sem criar infraestrutura paralela.
2. **Lote 10 restante** (P7-L10-REG/MONGO: tools de escrita MongoDB com precondição atômica), redespachado após o CL4 estabilizar a aprovação.
3. **Lote 11** (P7-L11-QA e hardening), que **incorpora** o [threat model do modo Claude Code (documento 22)](22-threat-model.md) produzido no passo 0 do bloco CL — o documento vive só em 22, não é reescrito no lote 11.
4. **7B** (conta Codex/ChatGPT): gates e roteiro preservados, sem execução até concluir o bloco CL, salvo nova decisão.
5. **Lote 12**: aceite integrado. A prioridade de contas só se encerra com o modo Claude homologado manualmente e decisão explícita sobre o 7B.

Lotes 10, 11 e 12 ficam **subordinados** ao bloco CL: trabalho independente pode continuar, mas não disputa responsáveis nem arquivos do CL. Modo API Claude e OpenAI API continuam disponíveis como estão.

### Polimento do Agente IA — sublotes CLP-0..CLP-7 (26/09/2026)

Decisão do usuário registrada na [ADR-056](../../10-decisoes-arquiteturais.md#adr-056--agente-ia-integrado-conversas-persistidas-permissões-por-provider-modos-e-propostas-de-edição-26092026): transformar o Claude (assinatura) — já existente em código nos commits `081a5ab`, `63c162d` e `6e07b41` — num agente integrado, com conversas globais persistidas, permissões persistentes por provider, contexto em chips, modos, tools MongoDB de leitura via MCP local, prompt de sistema e propostas de edição revisadas por diff. Passos P7-CL7-01..11 e gates GCL-9..GCL-17 em [23](23-integracao-claude.md#polimento-do-agente-ia--adr-056-26092026). Estado: **planejado; CLP-0 (documentação) em execução; nenhum gate ou AC aprovado**.

| Sublote | Escopo | Responsável | Tarefas |
| --- | --- | --- | --- |
| CLP-0 | ADR-056 e documentação | documentation-agent + architecture-agent | P7-CL7-01 |
| CLP-1 | Contratos Core/Application, `AgentModePolicy`, prompt builder, anexos, `LineDiff` | architecture-agent + agent-runtime-agent | P7-CL7-02 |
| CLP-2 | Facetas LiteDB, preferências aditivas, redação | persistence-security-agent | P7-CL7-03 |
| CLP-3 | Tools novas, broker para o Claude Code, tool de permissão, proposal sink | tool-registry-agent + mcp-integration-agent | P7-CL7-04 |
| CLP-4 | Adapter (plano → argv, `init`, resume persistido), disponibilidade automática, FakeClaudeCode estendido | agent-provider-agent | P7-CL7-05, P7-CL7-06 |
| CLP-5 | Painel global, histórico, modos, chips, Configurações/Permissões | ui-ux-agent | P7-CL7-07 |
| CLP-6 | Revisão por diff no editor | ui-ux-agent + architecture-agent | P7-CL7-08 |
| CLP-7 | Testes, PNGs nos dois temas, revisão, documentação final | qa-testing-agent + code-review-agent + documentation-agent | P7-CL7-09..11 |

Ordem: CLP-0 → CLP-1 → (CLP-2 ∥ CLP-3) → CLP-4 → CLP-5 → CLP-6 → CLP-7. Escritas MongoDB continuam no lote 10 e aparecem como "não disponíveis nesta versão". Tools MCP no Linux ficam pendentes (proof de canal só no Windows).

## Lote 6 ampliado — chat completo na UI da aplicação

O lote 6 deixa de ser apenas painel, aprovação e configuração isolados: o chat precisa ficar **utilizável de ponta a ponta na janela principal**, ainda desabilitado por padrão até os gates. Estado em 25/09/2026: painel, aprovação e configuração existem no Desktop e a hospedagem na `MainWindow` está em andamento no checkout; nada disso é declarado entregue. Escopo exigido:

| Item | Exigência |
| --- | --- |
| Hospedagem | `AgentChatPanel` dentro da `MainWindow`, docado e recolhível; em janela mínima abre superfície própria com retorno ao editor; editor e resultados respeitam os mínimos do design system |
| Contexto por aba | Contexto fixo (conexão › banco › coleção) capturado da aba de origem; mudar o Explorer não redireciona; resposta tardia só atualiza a solicitação de origem; trocar de aba não vaza contexto |
| Provider e modelo | Seletor orientado ao catálogo/capabilities; modelo do catálogo permitido; trocar provider cria outra sessão sem transportar conversa nem consentimentos |
| Streaming | Deltas sem roubar foco ou rolagem; cancelamento explícito por execução, com resultado incerto sinalizado; anúncio acessível limitado |
| Cartões de tool call | Nome legível, destino, risco, estado (solicitado/executando/concluído/falhou/negado), duração e erro seguro; detalhe expansível sem segredo nem resultado completo automático |
| Aprovação e consentimento | Modal de aprovação vinculada à operação; consentimento de amostragem de schema e de envio de contexto; `Rejeitar` como ação segura; expiração e revogação tratadas |
| Configuração de providers e credenciais | Entrada protegida de API Key, persistência no cofre por escolha explícita, estado de disponibilidade do cofre; nenhuma parte da chave exibida; abrir a tela não inicia autenticação |
| MCP opt-in | Ativação explícita, desligada por padrão, com estado do broker visível e sem ser pré-requisito do chat |
| Acessibilidade e teclado | Ordem de Tab, Ctrl+Enter no compositor, Escape devolvendo foco, nomes acessíveis, contraste e alvos do design system |
| Estados vazio/erro | Todos os estados de [16](16-chat-nativo.md#aprovação-e-estados), com texto e ação; IDE operacional sem IA |
| Localização | Todas as cadeias pelo sistema de localização existente; textos longos/CJK com rolagem local |
| Evidência visual | PNGs reais inspecionados nos temas claro e escuro em 960×620, 1366×768 e 1920×1080 (e escalas do design system) |
| Homologação nativa | Leitor de tela, IME e diálogos nativos em Windows e Linux, feitos por pessoa; sem isso a linha permanece pendente |
| Restrições | Nenhum transcript persistido (**revisado pela ADR-056 em 26/09/2026**: histórico do produto persistido com opt-out, sem anexos nem credenciais); nenhuma condicional por marca no ViewModel; nenhuma tool fora do registry; providers reais só com credencial autorizada |

Propostas de código com **Aplicar ao editor** e histórico retomável ficam fora deste lote salvo novo requisito e permanecem registrados como não implementados em [16](16-chat-nativo.md). **Novo requisito em 26/09/2026:** ambos entram no polimento CLP (propostas revisadas por hunk e conversas persistidas), ver [acima](#polimento-do-agente-ia--sublotes-clp-0clp-7-26092026).

## Sublote 7B — login por conta OpenAI (condicional)

Objetivo prioritário: permitir conta própria ChatGPT/OpenAI pelo Codex App Server (`CodexAppServerClient`, `CodexAgentSession`, propostos). Usar autenticação gerenciada pelo Codex, sem `chatgptAuthTokens`, OAuth próprio nem extração de tokens. API Key permanece alternativa explícita, sem migração automática de cobrança ou contexto. Spikes isolados e implementação desabilitada podem avançar; habilitação e distribuição exigem os gates abaixo. A prioridade não constitui aceite do risco experimental.

| Gate | Prova exigida | Estado em 25/09/2026 |
| --- | --- | --- |
| G7B-1 Suporte | Documentação oficial com App Server suportado para produção **ou** decisão registrada de risco aceito pelo usuário, com data, versão e riscos. Sem uma das duas, o 7B não é habilitado | Pendente; fonte reconferida em 25/09/2026 ainda classifica como experimental |
| G7B-2 Confinamento | Ferramentas nativas de shell e edição de arquivos desligadas, sem shell livre, sem rede/plugins/skills/MCPs globais, diretório sem workspace MongoDB, ambiente mínimo, caminho absoluto validado; dados só via registry Slop | Pendente; o spike prova apenas handshake |
| G7B-3 Credenciais | Backend keyring/cofre verificado efetivamente; `auto`/`file` proibidos; nenhum `auth.json` ou token em plaintext em disco, LiteDB, logs ou temporários; falha do cofre visível | Pendente |
| G7B-4 Ciclo de vida | Login, logout, cancelamento, expiração, credencial revogada, offline e processo morto com estados tipados, sem replay de tool e sem afetar IDE/MongoDB | Pendente |
| G7B-5 Capabilities | Descritor efetivo = provado ∩ política ∩ sessão; FileEditing/CommandExecution/SubAgents desabilitadas; login não implica consentimento de dados | Pendente |
| G7B-6 UI honesta | **Entrar com conta** só aparece se o descritor anunciar; textos não prometem recursos Codex nem equivalência com a API | Pendente |
| G7B-7 Homologação manual | Casos aplicáveis do [roteiro 21](21-homologacao-manual-login.md) aprovados por SO, com registro datado | Pendente; nenhuma execução |

Regras: 7B é oculto por padrão e por composição opt-in; falha em gate de segurança o mantém desabilitado sem afetar o 7A; nunca declarar "login entregue" com base em API Key, mock ou teste headless; nenhuma credencial em CI. Afeta AGT-02 e AC-05/07/08, cuja aprovação depende do gate manual ([12](12-criterios-de-aceite.md)). O acesso Claude é tratado separadamente no [bloco CL](#bloco-prioritário--integração-claude-25092026) (substitui o antigo sublote 8B), com seus próprios gates GCL-1..8.

## Sublote 8B — conta Claude pelo runtime oficial (condicional)

> **Substituído em 25/09/2026** pelo [bloco prioritário Integração Claude](#bloco-prioritário--integração-claude-25092026). O texto abaixo é histórico: a modalidade foi fixada no subprocesso do binário oficial (Agent SDK em sidecar rejeitado), G8B-1..8 foram substituídos por GCL-1..8 e, no modo Claude Code, ferramentas nativas com aprovação e transcript do próprio Claude Code passam a ser aceitos por decisão do usuário.

Objetivo prioritário: usar a conta Claude do próprio usuário sem exigir API Key nessa modalidade. O provider API do lote 8 permanece disponível separadamente. Avaliar primeiro subprocesso do Claude Code oficial instalado pelo usuário, por interface CLI documentada (`claude -p` e saída estruturada); avaliar Agent SDK Python/TypeScript em sidecar se necessário. A escolha deve provar streaming, cancelamento, ferramentas e isolamento, sem portar protocolo interno. Classes acima são propostas, não implementadas. Fonte técnica: [Agent SDK](https://code.claude.com/docs/en/agent-sdk/overview).

O runtime oficial conduz login, refresh e estado de autenticação. A sessão do adapter traduz eventos para o runtime Slop; o loop do SDK/CLI não duplica o loop de tools de Application: chamadas MongoDB entram no mesmo registry por canal autenticado. Aprovações nativas não substituem autorização de domínio. Binário intacto e métodos oficiais de autenticação preservados; Slop não coleta senha/código/token nem importa sessão Desktop/Code. Não instalar runtimes ou dependências durante uma conversa.

| Gate | Prova exigida | Estado em 25/09/2026 |
| --- | --- | --- |
| G8B-1 Modalidade oficial | Registrar fontes datadas e enquadramento exato: executar binário oficial com autenticação própria do usuário ou integrar Agent SDK. Conciliar a permissão de execução do binário com a restrição de oferta de login no SDK; obter autorização específica se exigida. Aviso de cobrança não equivale a autorização geral; aceite de risco do usuário não substitui condições do fornecedor | Pendente; fontes em [15](15-fontes-e-licencas.md) |
| G8B-2 Processo e licença | Versão/origem/hash, caminho absoluto validado, binário intacto, argumentos estruturados, protocolo público e limites de frames/filas/stderr. Revisar termos e transitivas antes de distribuir sidecar; MIT do Slop preservada | Pendente |
| G8B-3 Credenciais | Comprovar armazenamento seguro do runtime em Windows e Linux, sem tokens plaintext, cópia no Slop ou leitura dos arquivos de autenticação pelo adapter. Se o runtime não oferecer backend seguro compatível, bloquear nesse SO; modo efêmero só se oficialmente suportado e escolhido. Logout não remove login de outra instalação | Pendente; não presumir que Claude usa o keyring do Codex |
| G8B-4 Confinamento e privacidade | Sem shell/file editing/subagents, tools nativas de leitura de arquivos ou rede arbitrária; sem configs, hooks, skills, plugins e MCPs globais herdados. Diretório/ambiente isolados, rede apenas para serviço autorizado, tools MongoDB pelo registry. Provar ausência de transcript persistido pelo processo oficial; limpeza posterior não satisfaz ausência de persistência | Pendente |
| G8B-5 Sessão e tools | Streaming limitado, contexto capturado antes de awaits, CTS por execução, concorrência, cancelamento, crash/EOF e eventos tardios; nenhum replay de escrita, nenhum acesso direto a MongoDB, nenhum segundo loop executando a mesma tool | Pendente |
| G8B-6 Conta e limites | Plano/acesso elegível verificado, login/logout/expiração/revogação/offline com falhas tipadas; cota/créditos/cobrança conforme condições vigentes, sem prometer gratuidade ou API incluída. Sem fallback automático para API Key ou outro provider | Pendente |
| G8B-7 UI por capability | Ação de autenticação delegada ao runtime oficial apenas após gates; navegador/fluxo oficial, sem WebView ou formulário de credenciais Slop. Mostrar disponibilidade e motivo, sem prometer plano/modelo não verificado; login não consente envio de dados | Pendente |
| G8B-8 Homologação | AU-04, H-01..H-17 aplicáveis, executados manualmente por provider/SO/versão/plano; evidências sanitizadas e revisão dos gates | Pendente; nenhuma execução |

Rastreio: AGT-03, AC-06/07/08/09/11/14/15/17/20. Prioridade compartilhada com 7B; cada um tem liberação independente. Falha de gate mantém a via bloqueada, sem reduzir proteção de dados para viabilizar demonstração. Só encerrar a prioridade após os dois acessos homologados ou adiamento explícito com impedimento documentado.


## Divisão em mudanças revisáveis

Cada lote pode gerar PRs menores por contrato, handler ou superfície. Primeiro adicionar DTO/interface e teste comportamental; depois adapter/caso de uso; por último entrada de UI ou registro da tool. Não publicar uma tool antes de existir autorização, saída limitada e teste de falha. Evitar introduzir projetos extras só para acomodar uma classe.

No lote 2, implementar primeiro `list_connections`/metadados, depois codec literal e find/count, depois schema/sample/distinct/explain/índices. Schema por amostragem exige consentimento apropriado. Aggregate e estatísticas adicionais entram somente após seus gaps; operações administrativas e script livre ficam fora da baseline. No lote 10, cada operação é liberada separadamente após testes de intenção/aprovação/desfecho.

## Decisões que exigem evidência futura

| Questão | Responsável e momento | Saída obrigatória / fallback seguro |
| --- | --- | --- |
| App Server tem suporte produtivo (ou o usuário registra risco aceito) e permite confinamento sem tools alternativas? | Arquitetura/segurança, lote 0 e gates do sublote 7B | Relatório reproduzível antes de habilitar a variante; manter API direta como baseline, com auth/capabilities e aceite explícitos, sem prometer equivalência com login ChatGPT |
| SDK MCP suporta as duas eras e clientes escolhidos? | Infra/QA, lotes 0/4 | Versões fixadas e fixtures; incompatibilidade impede anunciar cliente, não autoriza misturar protocolos |
| Secret Service indisponível no Linux alvo? | Segurança, lote 1 | Modo memória explícito ou provider indisponível; IDE operacional |
| Pré-condição update/delete é atômica nos documentos escolhidos? | Domínio MongoDB, lote 10 | Fixture concorrente real; negar quando não há estratégia segura |
| Licença de binário/SDK/transitivas permite distribuição? | Revisão de dependências, 0/11 | NOTICE e licença da versão; não incorporar componente sem verificação |

Streamable HTTP é incremento opcional posterior ao lote 4, dependente do mesmo broker e de revisão específica de autenticação/Origin/Host/TLS. Não condicionar o primeiro MCP à hospedagem remota. Persistência de transcript, shell, file editing e subagents requerem novos requisitos e não são ativados por um provider reportá-los. Única exceção decidida: ferramentas nativas do modo Claude Code com aprovação por chamada ([ADR-054](../../10-decisoes-arquiteturais.md#adr-054--ferramentas-nativas-do-claude-code-com-aprovação-por-chamada-25092026)), após threat model e gate de segurança; subagentes continuam negados. **Estado vigente (26/09/2026):** a ADR-054 revisada mantém só leitura pura (`Read`/`Glob`/`Grep`); a ADR-056 acrescenta histórico de conversas persistido com opt-out e edição de arquivos **somente como proposta** revisada no editor (`propose_file_edit`, nunca grava em disco).

## Validação por mudança

Executar restore locked, build e testes conforme AGENTS.md. Login por conta é validado somente pelo [roteiro manual](21-homologacao-manual-login.md). Atualizar catálogo, matriz, acompanhamento e ADR quando comportamento mudar. UI exige PNG real inspecionado, Windows/Linux e testes de foco; testes headless não substituem homologação nativa. Providers reais são testados somente com credenciais autorizadas, fora da CI comum e sem gravá-las em artefatos. [Matriz de testes](11-plano-de-testes.md).
