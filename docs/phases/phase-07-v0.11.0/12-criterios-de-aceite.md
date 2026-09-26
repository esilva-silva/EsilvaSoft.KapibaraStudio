# Critérios de aceite da versão

**Todos pendentes de implementação e evidência futura.** A conclusão desta meta de planejamento não marca nenhum aceite funcional abaixo como aprovado.

| ID | Comportamento exigido | Prova para aprovação |
| --- | --- | --- |
| AC-01 | Conjunto inicial read-only exposto via MCP | TOOL-01/02/05 + catálogo/schema e execução real |
| AC-02 | Cliente externo descobre e executa tools | MCP-01/03; matriz de clientes/versões e trace sanitizado |
| AC-03 | Nenhum segredo MongoDB enviado | PRIV-01; scan canário em todos os canais e DTO allowlist |
| AC-04 | Chat consome runtime independente | Tests de arquitetura e troca de adapter falso/real sem View específica |
| AC-05 | OpenAI/Codex opera pelo adapter | PR-01/02, AU-01/02; sessão/stream/tool/aprovação/cancelamento reais |
| AC-06 | Claude opera pelo adapter | Mesmos contratos e teste API real com credencial autorizada |
| AC-07 | UI oferece apenas autenticação oficial suportada | Revisão das fontes/versionamento e teste de capabilities; conta 7B e Claude pelo modo Claude Code (bloco CL) somente após seus gates específicos; sem importação de tokens |
| AC-08 | Chaves/tokens não persistidos plaintext | SK-01/02 e inspeção de cofre, workspace, arquivos do processo e logs |
| AC-09 | Trocar provider preserva UI e boundary | Novo provider de teste registrado sem branch de marca; PRIV-02 |
| AC-10 | Tool calls visíveis no chat | UI-01; estados solicitado/em execução/concluído/falhou/negado com origem |
| AC-11 | Usuário cancela execução isoladamente | RT-02/03 e prova real; sem promessa de rollback |
| AC-12 | Nenhuma ação sem aprovação requerida | APR-01/02, PER-01, AUD-01 e WR-01/02 |
| AC-13 | Conexões por IDs lógicos, contexto explícito | TOOL-01, PRIV-01/02; sem URI em tools/streams |
| AC-14 | MCP e chat usam um registry | Tests de composição/handler e equivalência de políticas/saída |
| AC-15 | Providers desabilitados/removidos não quebram IDE | DEG-01 e startup sem binários/configuração externa |
| AC-16 | IA local continua independente | Teste offline ONNX e fallback atual; nenhum serviço externo requerido |
| AC-17 | Windows e Linux suportados | IPC/cofre/processo/UI/instalação em SOs nativos, versões registradas |
| AC-18 | Roadmap/migração íntegros | Comparação integral dos escopos movidos e links/índice atualizados |
| AC-19 | Escritas unitárias e índices com integridade | MongoDB real descartável, precondição atômica, RBAC, `_id_`, resultado incerto |
| AC-20 | Extensão de providers e licenças verificáveis | Adapter de teste e matriz de SDK/binários/transitivas/NOTICE da release |

## Rastreio adicional de 25/09/2026 — login por conta e chat completo

Nenhum AC foi aprovado por esta atualização; ela só define que evidência adicional é exigida.

| AC | Acréscimo | Evidência manual/adicional exigida |
| --- | --- | --- |
| AC-05 | Se o 7B for ofertado, o adapter também opera por login de conta via Codex App Server; caso contrário, AC-05 é avaliado somente com API Key e a ausência de login é registrada como decisão | Roteiro [21](21-homologacao-manual-login.md) H-01..H-07, H-11 e H-13, com registro por SO/versão/conta-plano; gates G7B-1..7 do [plano](10-plano-de-implementacao.md#sublote-7b--login-por-conta-openai-condicional). API Key sozinha nunca comprova login |
| AC-07 | Contas Codex/ChatGPT (7B) e Claude (modo Claude Code, bloco CL, histórico 8B) só são ofertadas após seus gates; autenticação delegada ao runtime oficial, sem importação de tokens/sessões | Para 7B: H-11/H-14/H-17 + fontes datadas; risco experimental Codex não substitui condições da Anthropic. Para Claude, ver [rastreio do bloco Integração Claude](#rastreio-do-bloco-integração-claude--25092026) abaixo (H-11/H-14/H-17 não se aplicam a esse provider) |
| AC-06 | Conta Claude passa a prioridade condicional do 8B, além da API do lote 8 (**substituído**: ver rastreio do bloco Integração Claude abaixo) | AU-04, GCL-1..8 e C-01..C-36; API Key não prova assinatura |
| AC-08 | Nenhum token de conta Codex/Claude em plaintext; backend seguro de cada runtime verificado; falha do cofre visível sem fallback | H-10 e H-12 manuais (varredura de LiteDB, logs, temporários e diretório Codex isolado) somadas a SK-01/02 |
| AC-04/09/10/11/12 | O chat completo (lote 6 ampliado) é a superfície que os consome | UI-03 com PNGs nos dois temas em 960/1366/1920 e homologação nativa de leitor de tela/diálogos, pendente |
| AC-03 | Login e abertura do chat não enviam dados MongoDB sem consentimento | H-08 manual + PRIV-01/02 |

## Rastreio do bloco Integração Claude — 25/09/2026

Por decisão do usuário, a conta Claude passa a ser executada pelo modo Claude Code ([23](23-integracao-claude.md)). **Nenhum AC foi aprovado**; a tabela só define a evidência. "Manual" significa execução pelo usuário, com conta própria, no [roteiro 21](21-homologacao-manual-login.md#casos-do-modo-claude-code-assinatura).

| AC | Cobertura no modo Claude Code | Automatizado (CLI falso/fixtures) | Exige homologação manual |
| --- | --- | --- | --- |
| AC-06 | Claude opera pelos dois modos (assinatura via Claude Code e Anthropic API), separados | CC-01..05, PR-01/02 | C-01..C-07, C-13, C-17, C-18; API Key não prova assinatura e assinatura não prova API |
| AC-07 | UI oferece só autenticação oficial: login/logout delegados ao Claude Code, sem formulário, OAuth próprio ou importação | CC-03, teste de capabilities | C-03, C-04, C-07; GCL-1 com risco de distribuição registrado |
| AC-08 | Slop não persiste chaves/tokens; conta Claude sem credencial no Slop | CC-02/03, varredura de canários | C-14; mecanismo do runtime registrado (GCL-4) |
| AC-09 | Trocar entre assinatura e API cria outra sessão, sem transferência nem fallback | CC-05, PRIV-02 | C-07, C-17 |
| AC-11 | Cancelamento isolado do turno Claude Code | CC-07, RT-02 | C-12, C-21, C-32 (árvore de processos) |
| AC-12 | Nenhuma ação nativa sem aprovação exigida; tools do produto com suas regras | CC-06, APR-01/02 | C-08..C-11, C-15, C-25 (subagente/ferramenta desconhecida), C-29 (tools do produto via MCP); depende de GCL-3 |
| AC-03 | Login, abertura do chat e leitura sem grant não enviam dados MongoDB no modo Claude Code | CC-08 (deterministic `--tools`/`--disallowedTools`/`--settings`, ver [11](11-plano-de-testes.md#login-por-conta-homologação-manual)) | C-22 (nenhum dado sem consentimento); depende de GCL-3/GCL-5 |
| AC-10 | Tool calls nativas visíveis no chat com estado/risco | CC-06, UI-01 | C-08..C-11, C-25, C-29 |
| AC-14 | MCP e chat usam um registry único também no modo Claude Code | CC-06 | C-29; nenhuma tool do Slop/registry acessa MongoDB fora do registry (evidência CL4-03) |
| AC-15 | Modo Claude Code indisponível/desabilitado não quebra a IDE | CC-01, DEG-01 | C-01, C-02, C-31 (shim `.cmd` rejeitado) |
| AC-17 | Windows e Linux suportados no modo Claude Code | CC-01..08 por SO | C-01..C-36 por SO, registrados separadamente; C-24 registra o mecanismo de credencial por SO |

AC-10 (cartões de tool, inclusive nativas) segue UI-01/UI-03 com PNGs. Os testes automatizados nunca aprovam sozinhos AC-06/07/08.

Casos manuais que exigem evidência do usuário: para o 7B, H-01..H-17 aplicáveis do roteiro 21 (Windows e Linux separados); **para Claude, H-01..H-17 não se exige** — o roteiro usa C-01..C-36, que cobrem as mesmas intenções adaptadas ao modo Claude Code (ver [21](21-homologacao-manual-login.md#casos-do-modo-claude-code-assinatura)). Em ambos os casos, o chat também exige leitor de tela, IME e diálogos nativos. Enquanto o registro do roteiro estiver vazio, esses ACs continuam **pendentes**.

## Rastreio do polimento do Agente IA — 26/09/2026 (ADR-056)

Nenhum AC aprovado; a tabela só define a evidência adicional dos passos P7-CL7-01..11 ([23](23-integracao-claude.md#polimento-do-agente-ia--adr-056-26092026)).

| AC | Acréscimo | Automatizado | Manual (GCL-17) |
| --- | --- | --- | --- |
| AC-03 | Sem consentimento persistido nada é enviado; anexos respeitam exclusões/limites/redação; histórico persistido sem segredos, anexos ou credenciais | Resolver de anexos, canários no LiteDB, CTA sem consentimento | Anexar arquivo sintético excluído é recusado |
| AC-04/09 | Painel global consome o runtime; `AgentModePolicy` único ponto de decisão, sem branch por marca/modo na UI | Tabela modo × permissões → plano; testes de arquitetura | — |
| AC-10 | Cartões de tool MCP, confirmação inline e proposta de edição visíveis | UI headless e PNGs nos dois temas | Conversa real com tools do produto |
| AC-11 | Cancelamento preserva a conversa; turno pertence à conversa de origem | FakeClaudeCode, troca de conversa durante turno | Cancelar turno real |
| AC-12 | Solicitar confirmações pede toda tool; escrita indisponível; `propose_file_edit` nunca grava em disco | Adapter/registry com FakeClaudeCode | Apply/Revert por hunk em arquivo sintético |
| AC-14 | Tools do Claude Code pelo mesmo registry via MCP local | `init` validado contra o plano; paridade registry/MCP | Listar conexões, índices e schema em cache com MongoDB real (Windows) |
| AC-17 | Windows com tools MCP; Linux com estado "indisponível" | Por SO | Linux pendente para tools MCP |

## Portas de liberação

Marco MCP read-only exige AC-01/02/03/08/12/13/14 e testes relevantes de AC-17. Chat externo exige também AC-04..11/15/16; versão completa exige todos os critérios aplicáveis, incluindo escritas previstas no lote 10. Tools administrativas, shell, subagents, persistência de transcript e HTTP remoto não são implicitamente liberados por esse aceite. A única liberação de shell/arquivos é a das ferramentas nativas do modo Claude Code com aprovação por chamada (ADR-054), condicionada a GCL-3/GCL-5 e C-08..C-11. **Estado vigente (26/09/2026):** pela ADR-054 revisada não há shell, escrita de arquivo nem rede nativos (só `Read`/`Glob`/`Grep`); pela ADR-056 o histórico de conversas do produto passa a ser persistido com opt-out e a edição de arquivos existe só como proposta revisada no editor, condicionados a GCL-9..GCL-17.

Se o spike Codex obrigar API direta por impossibilidade de confinamento, registrar formalmente a mudança, capacidades e autenticação reduzidas. Não usar uma API Key para declarar entregue o login ChatGPT. O sublote 7B e o bloco CL (Claude pelo modo Claude Code, substitui o antigo 8B) são a prioridade atual: API Key sozinha não conclui esse objetivo. Aceite sem uma das contas exige impedimento documentado e decisão explícita de adiamento pelo usuário; a via pendente não é anunciada. Compatibilidade de um cliente ou plataforma não testada permanece pendente e não pode ser anunciada.

Evidência mínima por aceite: commit/versão, fixture/cenário, SO, SDK/binário/modelo quando aplicável, resultado, artefato sanitizado e limitações. Datas de planos e fontes não são datas de homologação. Testes ignorados e credenciais ausentes devem aparecer explicitamente no relatório.
