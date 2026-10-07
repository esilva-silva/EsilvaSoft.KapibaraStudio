# Fase 5 — v0.9.0: GitHub Copilot e integração com agentes

**F5-COP-PERM encerrada — 06/10/2026:** o usuário confirmou aprovação dos testes manuais de ferramentas e conexões no client. [Aceite funcional](meta-permissoes-tools-copilot.md#aceite-manual-do-usuário--06102026) substitui as pendências de acesso dos retornos anteriores. Os demais requisitos e gates da Fase 5 permanecem abertos conforme suas evidências; não se declara conclusão integral da integração.

**Cota configurável — 06/10/2026:** o campo nas permissões Copilot usa padrão 100 e vazio ilimitado por mensagem, com persistência versionada e efeito nos próximos turnos. [Implementação, regressões e inspeção visual](meta-permissoes-tools-copilot.md#limite-configurável-solicitado-pelo-usuário--06102026). Esse incremento não encerra o aceite manual das 14 tools ou os demais gates da fase.

**Novo retorno do client — 06/10/2026:** [captura e relato](meta-permissoes-tools-copilot.md#novo-retorno-do-client--06102026) identificam cota de chamadas nas duas leituras restantes; o cartão visível comprova `get_document`, e o agente relata o mesmo código para `mongo_explain`. Proposta não testada sem conteúdo autorizado. Leituras em novos turnos e proposta com anexo ativo permanecem no aceite manual do usuário; não há nova falha de permissão comprovada por esse retorno.

**Permissões reabertas — 06/10/2026:** o usuário informou 11/14 tools funcionando. [F5-COP-PERM](meta-permissoes-tools-copilot.md#retomada--06102026) corrige a divergência de proposta workspace-only e distingue cota de chamadas de permissão, com regressões e UI localizada. Os três bloqueios reais ainda precisam de argumentos/auditoria; aceite integral e validação na aplicação permanecem abertos.

**Correção de permissões no checkout — 05/10/2026:** [F5-COP-PERM](meta-permissoes-tools-copilot.md) reproduziu a omissão do cofre MongoDB nas fontes de oito leituras e corrigiu sua composição. Falha da fonte tem diagnóstico distinto de negação; workspace e `NotFound` em propostas têm orientação coerente. Foco **41/41** unitários e **13/13** integração/UI; PNGs reais inspecionados. Nenhum grant foi ampliado. O usuário fará a validação manual na aplicação; integração oficial completa segue com seus gates.

**Escopo reorganizado em 03/10/2026:** concluir a integração completa com GitHub Copilot no painel Agente IA, usando os contratos compartilhados existentes. “Integração com agentes” nesta fase significa o runtime, sessões, contexto, ferramentas, permissões, auditoria e propostas necessários ao Copilot; não é um compromisso de concluir todos os providers.

**Prioridade entre fases:** permanece a ordem vigente: entregas prioritárias da [Fase 8 / v0.12.0](../phase-08-v0.12.0/README.md), depois Fase 5 / v0.9.0. Esta revisão reorganiza o escopo, sem iniciar implementação ou declarar a integração concluída.

**Demais integrações:** Claude Code/Anthropic, Codex/ChatGPT, OpenAI API e outros providers externos, além da integração MCP com clientes externos, ficam no [bkl-06 — integrações de agentes](../../backlog/bkl-06-integracoes-agentes.md), sem fase ou versão comprometida. Suas implementações, composição, catálogo, disponibilidade, testes, permissões e gates continuam como estão. A infraestrutura compartilhada necessária ao Copilot permanece nesta fase. IA local e workflow conservam suas fases próprias.

**Rastreabilidade:** IDs históricos `P7-COP-01..07`, `P7-UX`, `P7-UX2`, `P7-AUTH` e `P7-LINUX`, versões de CLI/SDK, datas e resultados não são renumerados. Novos trabalhos referenciam Fase 5 / v0.9.0. O [histórico integral anterior](../../backlog/integracoes-agentes/historico-integracoes.md) conserva evidências e estados intermediários, inclusive os já substituídos.

**Meta complementada em 03/10/2026:** a [meta de implementação](meta-de-implementacao.md) detalha **30 requisitos GitHub/produto**, **14 ferramentas liberáveis**, parâmetros/permissões/limites, ferramentas bloqueadas e matriz de sucesso/recusa/falha/concorrência/homologação por ferramenta. Inclui fontes oficiais e destinação das capacidades opcionais SDK/CLI; mantém os gates atuais e a separação entre código, contrato local e aceite real.

**Primeiro lote de implementação — 03/10/2026:** avanços locais nos requisitos F5-GH-04/06/07 (invalidação e concorrência das ações de conta), F5-GH-13 (schema fechado e limitado antes de criar/retomar sessão) e F5-GH-10 (ignorar callbacks posteriores ao encerramento do turno). Testes dirigidos passaram: 25/25 de conta/lifecycle, 9/9 de schema e 22/22 no runtime fake STDIO. São evidências automatizadas locais; não encerram os critérios GH completos, as 14 tools V1..V9, CLI/conta oficial, MongoDB descartável ou homologação nativa.

**Continuação da implementação — 03/10/2026:** F5-GH-01/03 recebeu detecção de cabeçalho PE/ELF sem executar o candidato e testes fake de protocolo/capacidades; F5-GH-12 passou a congelar chips e exclusões antes da primeira leitura assíncrona. Para F5-GH-24/25 foi adicionada divulgação Copilot “Dados e uso” antes do envio, com link oficial, em pt-BR/en/es/zh-CN. Evidência local dirigida: 34/34 testes de conta/schema/compatibilidade (conjuntos focalizados), 9/9 de anexos e 1/1 teste UI que gera oito PNGs; os oito PNGs foram inspecionados em quatro idiomas e dois temas e não mostram corte do conteúdo. Esses testes não comprovam CLI oficial, fluxo de conta, MongoDB, acessibilidade nativa nem aceite das ferramentas. Termos, licença, métricas e outros gates seguem abertos.

**Segundo lote — 03/10/2026:** GH-11 agora mapeia categorias estruturadas de erro e timeouts para códigos localizados, sanitizados e sem replay automático; **44/44** testes focados de sessão/eventos/isolamento e **5/5** de localização passaram. GH-20/21 serializa a exclusão nativa/local do store volátil contra criação concorrente; **3/3** casos focalizados de exclusão/retry passaram. TOOL-04 ganhou verificação explícita de key directions, TTL e caminhos de filtros parciais sem valores; os testes de projeção e handler passaram **9/9**. A regressão visual de conversa e o aviso Copilot passaram **3/3**. São testes locais com fixtures/doubles; runtime oficial, crash/restart, MongoDB e matriz integral V1–V9 permanecem pendentes.

**Terceiro lote — 03/10/2026:** fake-STDIO cobre GH-14 em create/resume, rejeição de built-in agents por recusa e RPC ausente antes do prompt, e GH-19 para resume reservado ambíguo sem criar sessão substituta nem retransmitir prompt/histórico. Casos novos **5/5** e regressões fake **3/3** passaram. Não substituem runtime oficial, restart real, confirmação humana na UI nem cobertura de concorrência/cancelamento GH-22.

**Quarto lote — 03/10/2026:** GH-17/TOOL-14 agora revalida containment, links, exclusões e caminho canônico após a leitura assíncrona do arquivo do workspace; link inserido durante o `await` produz `OutsideWorkspace` e nenhuma proposta. Filtro de propostas passou **7/7** após rebuild correto do Application. O teste usa PathProbe/leitor sintéticos; symlink/junction nativo, aplicação/Undo no editor e runtime oficial continuam pendentes.

**Quinto lote — 03/10/2026:** GH-22 fortaleceu o teste fake-STDIO de duas conversas: ambas aguardam tools distintas; cancelar uma faz abort/detach somente da primeira e a segunda termina com o resultado correspondente. Ledger verifica IDs/prompts separados, processos com PIDs distintos e saída após Dispose. Teste fortalecido **1/1**, regressões fake **6/6**. Não substitui concorrência com CLI/runtime oficial, validação nativa nem crash/dispose sob sessão real.

**Sexto lote — 03/10/2026:** TOOL-06/GH-12 corrigiu a classificação de contexto: se a checagem detecta travessia de link, o metadado não afirma que o alvo está dentro da workspace só por seu caminho lexical. Um teste comprova `insideWorkspace=false`, ausência de `relativePath` e nenhuma leitura. Foco rebuilt com regressão de contexto/snapshot passou **3/3**; links OS nativos e runtime oficial seguem pendentes.

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

O aceite de cada entrega deve cobrir seus IDs `F5-GH-*`, as linhas `F5-TOOL-01..14` e os cenários V1..V9 aplicáveis da [meta detalhada](meta-de-implementacao.md). A liberação condicional de uma tool não equivale à homologação de seu contrato completo.

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
