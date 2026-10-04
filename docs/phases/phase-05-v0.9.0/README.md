# Fase 5 — v0.9.0: GitHub Copilot e integração com agentes

**Escopo reorganizado em 03/10/2026:** concluir a integração completa com GitHub Copilot no painel Agente IA, usando os contratos compartilhados existentes. “Integração com agentes” nesta fase significa o runtime, sessões, contexto, ferramentas, permissões, auditoria e propostas necessários ao Copilot; não é um compromisso de concluir todos os providers.

**Prioridade entre fases:** permanece a ordem vigente: entregas prioritárias da [Fase 8 / v0.12.0](../phase-08-v0.12.0/README.md), depois Fase 5 / v0.9.0. Esta revisão reorganiza o escopo, sem iniciar implementação ou declarar a integração concluída.

**Demais integrações:** Claude Code/Anthropic, Codex/ChatGPT, OpenAI API e outros providers externos, além da integração MCP com clientes externos, ficam no [bkl-06 — integrações de agentes](../../backlog/bkl-06-integracoes-agentes.md), sem fase ou versão comprometida. Suas implementações, composição, catálogo, disponibilidade, testes, permissões e gates continuam como estão. A infraestrutura compartilhada necessária ao Copilot permanece nesta fase. IA local e workflow conservam suas fases próprias.

**Rastreabilidade:** IDs históricos `P7-COP-01..07`, `P7-UX`, `P7-UX2`, `P7-AUTH` e `P7-LINUX`, versões de CLI/SDK, datas e resultados não são renumerados. Novos trabalhos referenciam Fase 5 / v0.9.0. O [histórico integral anterior](../../backlog/integracoes-agentes/historico-integracoes.md) conserva evidências e estados intermediários, inclusive os já substituídos.

## Meta P7-COP — GitHub Copilot por assinatura

**Objetivo:** finalizar instalação/configuração, conta oficial, modelos, chat com streaming, sessões, contexto autorizado, ferramentas do produto, propostas revisáveis, cancelamento e recuperação. Integração completa significa cumprir os contratos e aceites desse recorte; não amplia acesso a ferramentas nativas, escritas MongoDB, API/BYOK ou outros providers.

**Estado atual:** implementação parcial, com disponibilidade limitada no Windows após verificar conta e modelos. Há evidência oficial Windows de autenticação/modelos, streaming sintético, retomada/exclusão de SessionFs, cancelamento e chamadas ao registry com contexto, perfis e leituras sintéticas. Permanecem pendentes MongoDB de teste, falhas/concorrência/recuperação e negação/revogação no runtime oficial, aplicação/conflito na UI nativa, leitor de tela e revisão final de termos/licenças. Nenhum desses gates foi encerrado nesta reorganização.

**CLI e distribuição vigentes:** SDK .NET `GitHub.Copilot.SDK` 1.0.14 conectado à CLI oficial nativa instalada pelo usuário, sem redistribuição de CLI/runtime desde 02/10/2026. O caminho opcional salvo tem prioridade; caminho inválido falha sem fallback. Sem override, detecção no perfil local e depois no `PATH`. Conta, login e sessões compartilham configuração; sessões abertas retêm seu cliente. Os registros antigos sobre runtime/CLI empacotados são históricos. Consulte [ADR-059/062](../../10-decisoes-arquiteturais.md#adr-059--github-copilot-por-assinatura-via-cli-oficial-28092026), [release Copilot](../../architecture/release-copilot.md) e [termos GitHub Copilot](guia-termos-github-copilot.md).

### Escopo e contratos

- Concluir o adaptador existente `CopilotSubscriptionAgentProvider` sobre `IAgentProvider` e `IAgentSession`, usando `GitHub.Copilot.SDK` e o runtime oficial. Reutilizar chat, catálogo, registry, permissões, propostas de edição e auditoria existentes; não criar uma segunda infraestrutura de agentes.
- Exigir a CLI oficial instalada para login/logout: `copilot login` abre o fluxo OAuth oficial; logout abre a CLI interativa para o usuário executar `/logout`. Desde 02/10/2026, o SDK .NET conecta consultas/sessões à mesma instalação nativa do usuário, valida a compatibilidade do protocolo e não redistribui runtime/CLI. A API .NET fixada oferece consulta de estado, sem métodos próprios de login/logout. O aplicativo não lê arquivos de credenciais, recebe/copia tokens ou acessa endpoints privados. Logout apaga a sessão local compartilhada com outros usos da CLI/SDK, sem revogar o app OAuth no GitHub, e exige confirmação.
- Manter a modalidade exclusivamente Copilot: ausência de assinatura/acesso, expiração, limite, indisponibilidade ou bloqueio corporativo não acionam API/BYOK nem outro provider. Aceitar autenticação somente quando `GetAuthStatusAsync` informar `IsAuthenticated` e `AuthType == "user"`; `env`, `gh-cli`, `api-key`, `token` e estados desconhecidos ficam bloqueados. Validar a precedência de autenticação e configurar uma allowlist de ambiente para o subprocesso, sem ler ou registrar valores de credenciais.
- Descobrir modelos pelos mecanismos oficiais e apresentar somente os elegíveis para a conta/política. Informar que o consumo segue os limites e a cobrança do Copilot, sem promessa de uso ilimitado.
- Capturar o contexto antes do envio e vincular sessão, turno, workspace, perfil, banco e coleção à origem. Mudanças no Explorer ou em outras conversas não redirecionam respostas ou ferramentas. Isolar cancelamento por conversa e não prometer rollback.
- Expor somente ferramentas autorizadas pelo plano e pelo registry. Negar ferramentas desconhecidas e operações sem autorização antes da execução; não usar aprovação irrestrita. Nesta integração Copilot, `NativeTools` (incluindo comandos e rede) e escrita nativa estão desabilitados, e tools de escrita MongoDB não entram no plano. A única edição disponível é a proposta mediada `propose_file_edit`, com revisão e verificação de conflito com o buffer; ela não grava em disco.
- Preservar opt-outs e o proprietário LiteDB registrado em DI, com migração aditiva/versionada se necessária. Não persistir resultados ou credenciais nos snapshots. Verificar separadamente a retenção de histórico/contexto pelo runtime Copilot e sua compatibilidade com as preferências do usuário.

## Entregas e sequência de conclusão

| ID preservado | Entrega | Trabalho restante / evidência exigida |
| --- | --- | --- |
| P7-COP-01 | SDK, CLI e distribuição | Consolidar compatibilidade de protocolo, inventário/SBOM/notices e revisão de termos; validar pacotes sem CLI/runtime redistribuídos. |
| P7-COP-02 | Instalação e conta | Completar os cenários de CLI ausente/inválida/incompatível, login/logout, expiração, configuração conflitante e política corporativa, sem expor credenciais ou enviar inferência na checagem. |
| P7-COP-03 | Provider e conversa | Consolidar modelos elegíveis, streaming, erros tipados, limites e sessão perdida, sem replay ou fallback silencioso. |
| P7-COP-04 | Ferramentas e permissões | Validar leituras com fixture MongoDB descartável, negação/revogação/expiração e falha de auditoria/ponte; revisar/aplicar propostas e conflitos no editor. |
| P7-COP-05 | Configuração e experiência | Concluir estados, mensagens, permissões, contexto e métricas oficiais Copilot; verificar PNGs reais nos temas/idiomas e registrar pendências nativas. |
| P7-COP-06 | Sessões e recuperação | Cobrir duas conversas concorrentes, cancelamento independente, troca de seleção, reinício, sessão ausente/ilegível, falhas de gravação e exclusão, retenção/opt-out com runtime oficial. |
| P7-COP-07 | Validação e documentação | Restore locked, build e suítes previstas, evidências proporcionais; alinhar ADRs, design system, catálogo, matriz, guia e acompanhamento. Homologação manual real coordenada com a Fase 10. |

As linhas permanecem **parciais**; contratos locais e testes Headless não substituem os cenários oficiais restantes. Primeiro fechar configuração/conta e distribuição; depois conversa e tools; em seguida sessão/recuperação e experiência; por fim consolidar evidências e aceite.

## Infraestrutura compartilhada no escopo

- Manter `IAgentProvider`, `IAgentSession`, runtime, account manager, registry único, planos/grants por turno, consentimento, auditoria e persistência pelo proprietário LiteDB existente.
- Reutilizar [P7-AUTH](meta-inicializacao-autenticacao-agentes.md), [P7-UX](meta-ui-ux-painel-agente-ia.md) e [P7-UX2](meta-autoscroll-contexto-consumo-agente.md) para o aceite Copilot. Extensões específicas de Claude/Codex/API ficam no backlog; os contratos e regressões existentes são preservados.
- Expor somente tools incluídas no plano e autorizadas no registry. As sete leituras limitadas MongoDB exigem checkbox, consentimento separado, conexão e grants apropriados; diagnóstico exige seu grant. `get_collection_schema` continua fechado até consentimento local dedicado.
- Preservar `NativeTools` vazio, built-in agents desativados antes do envio, escritas MongoDB fora do plano e propostas `propose_file_edit` revisáveis somente no buffer, com Undo e proteção contra conflito.

## Fora do escopo

Concluir ou ampliar Claude Code/Anthropic, Codex/ChatGPT, OpenAI API/outros providers externos, autenticações alternativas e integração de clientes MCP externos. Esses itens estão no backlog. IA local pertence à Fase 8; chat simples por workflow, à Fase 7. Nenhuma integração existente é removida ou desativada por esta decisão.

## Aceite e limites de conclusão

- [ ] Instalação/configuração e conta oficial verificadas, modelos elegíveis e login/logout documentados; nenhum segredo em logs, snapshots ou artefatos.
- [ ] Streaming e erros de limite/expiração tratados; indisponibilidade não aciona API/BYOK ou outro provider.
- [ ] Ferramentas MongoDB validadas com fixture sintética descartável; permitir, negar, revogar, expirar e falhar auditoria/ponte bloqueiam ou executam conforme o plano antes de acessar dados.
- [ ] Ferramentas desconhecidas/nativas e agentes fora do plano bloqueados preventivamente em criação e retomada; selecionar/abrir coleção nunca executa consulta.
- [ ] Contexto e resultados vinculados à aba/sessão/turno de origem; duas conversas concorrentes e cancelamentos isolados; propostas obsoletas não sobrescrevem buffers.
- [ ] Reinício, sessão ausente/ilegível, gravação/exclusão com falha, opt-outs, retenção e recuperação validados sem perda silenciosa de referências.
- [ ] Estados de UI e PNGs reais inspecionados; homologação nativa de confirmação/aplicação, teclado/leitor de tela registrada separadamente.
- [ ] Restore locked, build e testes aplicáveis aprovados; documentação e revisão de termos/licenças concluídas, com limitações explícitas.

O encerramento funcional automatizável e a homologação real são registrados separadamente. A [Fase 10](../phase-10-v0.14.0/README.md) concentra a homologação manual; suporte completo só pode ser anunciado após seus gates aplicáveis. Linux mantém o limite decidido anteriormente: revisão estática/estrutural, build/cross-publish e testes unitários, sem conexão/login/CLI/cofre/MCP/MongoDB/UI nativos nesta meta. A [meta P7-LINUX](meta-linux-copilot-claude.md) conserva evidências históricas compartilhadas; o recorte Claude fica no backlog. Nenhuma prova Windows homologa Linux.

## Evidências e documentos

- [Histórico integral de implementação e validação](../../backlog/integracoes-agentes/historico-integracoes.md#meta-p7-cop--github-copilot-por-assinatura), com resultados originais e limitações.
- [Guia de uso](../../14-guia-de-uso.md), [matriz](../../15-matriz-de-validacao.md), [checklist](../../16-checklist-homologacao.md) e [acompanhamento](../../12-acompanhamento-da-implementacao.md).
- [Estabilidade dos testes](../../architecture/test-stability.md): benchmarks/latência são ferramentas manuais, fora do CI/release; Linux executa apenas as suítes unitárias previstas.
