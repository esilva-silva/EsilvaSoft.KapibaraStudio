# Segurança, contexto e privacidade

**Proposta — ADR-049/051; ADR-051 substituída pela [ADR-056](../../10-decisoes-arquiteturais.md#adr-056--agente-ia-integrado-conversas-persistidas-permissões-por-provider-modos-e-propostas-de-edição-26092026) em 26/09/2026 (planejado, ver seção final).** Nenhum dado MongoDB sai da máquina apenas porque um provider está conectado. Uma saída precisa de ação explícita, contexto autorizado ou chamada de tool permitida. O mesmo limite se aplica ao cliente MCP externo: ele é outro destinatário, mesmo quando seu processo está local.

## Contexto não é uma escala de acesso irrestrito

| Opção visível | Payload permitido após autorização | Exclusões |
| --- | --- | --- |
| Nenhum | Mensagem digitada pelo usuário e identificação técnica da sessão | Nenhuma coleta automática; texto digitado também passa por inspeção de segredos |
| Somente metadados | IDs lógicos e nomes de namespaces explicitamente escolhidos | URI, senha, variáveis, documentos, amostras e topologia bruta |
| Schema | Campos/tipos derivados e proveniência da evidência | Valores exemplo; nomes sensíveis devem poder ser omitidos |
| Amostras | Projeção e quantidade autorizadas na coleção escolhida | Amostrar outras coleções ou ampliar projeção silenciosamente |
| Resultados | Página/trecho explicitamente escolhido, com limite | Resultado completo e páginas ainda não carregadas |
| Seleção explícita | Trecho de editor/resultado confirmado, com origem | Arquivo completo, workspace ou histórico por inferência |

Esses escopos são independentes. Selecionar schema não concede documentos; uma inferência remota de schema por amostra exige consentimento para leitura local e envia somente a derivação autorizada. O nome do banco, valores de `distinct`, números de count e explain também podem ser sensíveis. A política descreve dados expostos, não apenas qual botão foi acionado.

`IAgentContextProvider` cria snapshot com origem, conexão/revisão, namespace, aba/versão de texto, data da evidência, classificação, orçamento, redaction e destinatário. Interface mostra prévia do pacote e escopo ativo antes do envio. **Revisado por ADR-056 (planejado):** no Agente IA a prévia por mensagem é substituída por consentimento persistente por provider e chips de contexto sempre visíveis e removíveis antes do envio. Tool responses passam pelo mesmo filtro e orçamento; não usar a permissão da primeira mensagem como autorização indefinida para novas coleções.

Trocar provider inicia sessão independente; histórico/contexto anterior não é retransmitido sem confirmação explícita. Trocar Explorer nunca muda conversa ou operação existente. Redução de permissão bloqueia envios futuros e elimina buffers locais que já não devem ser usados; não promete apagar dados já recebidos pelo serviço. Não prometer retenção zero dos serviços externos; apontar termos e controles de conta aplicáveis nas [fontes](15-fontes-e-licencas.md).

## Segredos e fronteiras

Resolver connection ID dentro de Infrastructure. Construir DTO sanitizado por allowlist; jamais serializar `ConnectionProfile` integralmente. Remover senha, URI, certificados privados, tokens, env, caminhos de segredos, usuário/host quando não necessários e mensagens de exceção com credenciais. Nomes de perfil são texto livre: para qualquer destino externo substituir por alias estável derivado do ID (`Conexão <ID>`), não tentar inferir todos os segredos por regex; no destino local o nome real pode ser preservado. Guardar chaves/tokens somente no cofre aprovado; logs de SDK e traces de protocolo não podem capturar autenticação.

O estado local atual não oferece cofre seguro completo. A entrega futura exige migração controlada das credenciais MongoDB que forem persistidas e usadas por agentes, além das novas chaves de IA. Um segredo já gravado não fica seguro só porque deixou de ser retornado por MCP. [Plano de migração e limites de remoção](07-autenticacao-e-segredos.md).

Resposta de banco, schema, nome de coleção, arquivo e mensagem do provider são dados não confiáveis. Nunca reinterpretá-los como política, shell, instrução de aprovação ou conteúdo privilegiado. Redação de padrões é defesa adicional, não substitui seleção de campos e autorização determinística. Se não puder sanitizar com confiança, bloquear com diagnóstico seguro.

## Orçamentos propostos para o primeiro incremento

Configurar limites locais com teto rígido: 100 documentos por find; amostra de documentos padrão 5/teto 20; schema derivado padrão 20/teto 100; 256 KiB por resultado de tool, 64 KiB por chamada, execução padrão 5 s/teto 30 s, até 2 leituras simultâneas por sessão, 4 por conexão e 8 globais, uma escrita por sessão, até 20 tool calls por turno. O catálogo pode impor teto menor; elevar esses valores requer revisão de ameaça e desempenho. Incluir `truncated`, motivo e contagem; nunca cortar dentro de um valor BSON ou apresentar JSON inválido. Documento individual maior que orçamento gera erro sem retornar fragmento enganoso.

O catálogo inicial usa paginação explícita por skip e não promete snapshot consistente nem cursor durável. Se a implementação adicionar cursores, eles deverão ser handles opacos emitidos pelo host, vinculados a principal, destino, consulta, revisão e expiração de até 5 min; sem credenciais/filtros em texto. Revogação invalida handles. Máximo de tokens/custo depende de modelo/conta, com limite configurado e parada por orçamento; sem fallback automático para outro serviço pago. Estes números são decisões iniciais de produto a validar por teste, não limites do MongoDB ou dos fornecedores.

## Auditoria sem conteúdo sensível

Evento versionado contém timestamp UTC, operation/request ID, provider/client, sessão/turno, tool/version, connection ID, namespace autorizado (ou identificador pseudônimo conforme política), nível de risco, decisão e motivo, aprovação local/horário, política/revisão, início/fim, duração, resultado (`Succeeded`, `Denied`, `Cancelled`, `Failed`, `Uncertain`) e contagens. Não contém chave/token, URI, prompt, filtro, diff, resultados, stdout bruto ou stack trace. Hashes de conteúdo sensível também ficam fora do registro persistente.

Persistir via proprietário LiteDB em coleção aditiva/versionada; `agentAuditEvents` schema v1 já armazena DTO fechado com 30 dias/10.000 eventos de retenção, preservando intents sem desfecho. Essa camada foi testada isoladamente, mas ainda não está ligada ao registry: o requisito de fechar acesso em falha só fica operacional quando o pipeline aguardar o append e negar antes de execução/publicação. Exportação de auditoria também exige ação explícita. Auditoria local é rastreabilidade operacional, não prova inviolável contra administrador da máquina. Falha de leitura/escrita deve ser visível e fechar o acesso de agentes; a IDE manual continua disponível conforme suas próprias políticas.

Conversas e resultados de tools são efêmeros na v0.11.0; metadados de sessão podem ser retomados sem conteúdo sensível. **Revisado por ADR-056 (26/09/2026, planejado):** o histórico do Agente IA passa a ser persistido no owner LiteDB único, com redação antes de gravar, opt-out **Não guardar histórico** e **Apagar histórico**; conteúdo de anexos, resultados completos de tools e credenciais continuam fora da persistência (ver seção final). Não inserir transcript em rascunhos nem histórico de consultas. Retenção própria do provider/App Server é uma fronteira separada a verificar/desativar quando possível; se o provider exige persistência incompatível com a política, a capability de retomada fica indisponível, com explicação.

## Ameaças e mitigação verificável

| Ameaça | Controle | Evidência exigida |
| --- | --- | --- |
| Prompt injection em documento/schema | Dados não elevam política; registry valida toda chamada | Fixture instrui a vazar ENV e nada sai |
| Confused deputy e spoof de conexão | Principal fora de argumentos; allowlist de IDs/namespaces | Cliente A não usa concessão/cursor de B |
| Aprovação reutilizada / TOCTOU | Ticket único + revisão/hash/cancelamento + precondição | Alterar alvo/diff após aprovação nega |
| Exfiltração por consulta e erro | JSON literal, DTO allowlist, erro sanitizado | Canários de secrets ausentes no tráfego/logs |
| Esgotamento de recursos | Limites de tamanho/concorrência/deadline/custo | Payload enorme e stream sem fim terminam |
| Escrita oculta em aggregate | Validação estrutural recursiva e lista fechada | `$out`/`$merge` aninhados e namespaces não concedidos negados |
| Processos nativos do provider | Shell/files/network tools desabilitadas; ambiente mínimo | Nenhum acesso alternativo ao arquivo de workspace/cofre |
| Replay após crash | Sem retry de escrita, intenção auditada e estado incerto | Falha após commit não duplica documento |

Windows/Linux, cofre bloqueado, ausência de D-Bus/Secret Service e ambiente sem internet fazem parte do aceite. Degradação não deve paralisar navegação, consultas manuais, arquivos nem autocomplete determinístico.

## Modo Claude Code — threat model obrigatório (25/09/2026)

A liberação de ferramentas nativas com aprovação ([ADR-054](../../10-decisoes-arquiteturais.md#adr-054--ferramentas-nativas-do-claude-code-com-aprovação-por-chamada-25092026)) muda a linha "Processos nativos do provider" da tabela acima **somente no modo Claude Code**. O threat model completo está no [documento 22](22-threat-model.md) (tarefa P7-CL0-02; rascunho anterior ao spike P7-CL0-01, ainda sem revisão independente) e é gate (GCL-3, pendente) antes de habilitar essas ferramentas. Resumo das ameaças que ele deve cobrir:

| Ameaça | Controle previsto | Evidência exigida |
| --- | --- | --- |
| Prompt injection (documento, CLAUDE.md do cwd, página web) levando a comando ou escrita | Aprovação por chamada com alvo visível; destrutivas sem "sempre"; registry inalterado para MongoDB | Fixture hostil pede `rm`/exfiltração e nada executa sem aprovação |
| Exfiltração por rede (WebFetch, comando) | Categoria `Network` aprovada por domínio; cwd sem segredos | Canário não sai sem aprovação explícita |
| Acesso a LiteDB, cofre ou dados do app pelo shell | cwd escolhido fora do diretório de dados; aviso de que o shell roda com o usuário do SO | Tentativa de ler o diretório de dados exige aprovação e é destacada |
| Hooks e MCPs de usuário/projeto executados em `-p` sem diálogo de confiança | `--setting-sources` restrito, `--strict-mcp-config`, cwd controlado | Canários de hook/MCP sintéticos não disparam (C-15) |
| Regras `permissions.allow` e configurações gerenciadas aprovando fora do diálogo | Excluir fontes quando possível; exibir exceções restantes | Regra sintética não auto-aprova ou aparece como exceção visível |
| Cobrança por API em vez da assinatura | Detecção do método efetivo e bloqueio | `ANTHROPIC_API_KEY` sintética bloqueia envio (C-06) |
| Tokens da conta expostos pelo Slop | Slop não lê stdout de login, arquivos de `~/.claude` nem guarda credenciais | Varredura sem tokens em LiteDB/logs/temporários/PNGs (C-14) |
| Transcript persistido pelo Claude Code em `~/.claude/projects` | Limitação aceita pelo usuário e visível nas configurações; Slop não persiste transcript | Aviso presente (C-16) |
| Spoof da tool de permissão ou do canal MCP | Canal autenticado por sessão; tool não chamável pelo modelo | Chamada direta da tool é negada |
| Processo órfão ou turno inacabado | Interrupt/SIGINT; kill com `OutcomeUnknown`; limites de fila/stderr | Cancelamento e crash sem processo residual (C-12) |

## Agente IA integrado — privacidade e ameaças (ADR-056, 26/09/2026)

**Implementação CLP presente no checkout em 27/09/2026, sem gate de segurança aprovado.** A [ADR-056](../../10-decisoes-arquiteturais.md#adr-056--agente-ia-integrado-conversas-persistidas-permissões-por-provider-modos-e-propostas-de-edição-26092026) substitui a ADR-051 mantendo seus princípios (nenhum envio só por conectar, saída de tool pela política, DTO allowlist, alias externo, redação, erros sanitizados, auditoria sem conteúdo). O codec de conversas rejeita conteúdo de tool em campos persistidos e o prompt usa o nome da pasta, sem caminho absoluto. Testes de canários, revisão do diff final e prova de saída real ainda são necessários. Detalhes de ameaças em [22](22-threat-model.md#polimento-do-agente-ia-adr-056--ameaças-novas-26092026).

| Dado | Pode sair para o provider | Persistido no Slop |
| --- | --- | --- |
| Mensagem digitada | Sim, com consentimento persistente | Sim, após redação (salvo opt-out) |
| Arquivo ativo (buffer, inclusive não salvo) | Sim, se a permissão "arquivo ativo" estiver ligada; chip visível e removível | Só metadados do chip (nome, tipo, tamanho, hash) |
| Arquivos do workspace / externos | Sim, se permitidos; exclusões `.env`, `*.pem`, `*.key`, `**/secrets/**`; 256 KB/arquivo, 1 MB/mensagem | Só metadados |
| Leitura nativa `Read`/`Glob`/`Grep` | Sim, se ligada; aviso de envio à Anthropic | Não |
| Resultado de tool do produto | Sim, pela política/grants do registry | Só cartão resumido |
| Proposta de edição | Texto proposto vem do provider | Referência da proposta; o texto vive no buffer da aba, nunca em disco pelo agente |
| Credenciais, URI, segredos | Nunca | Nunca |

| Ameaça | Controle previsto | Evidência exigida |
| --- | --- | --- |
| Prompt injection em anexo/arquivo lido | Conteúdo é dado não confiável; não eleva política; edições só como proposta revisada; sem ferramentas de execução/rede | Fixture hostil em arquivo anexado pede para gravar/vazar e nada é executado nem salvo |
| Segredo em arquivo anexado | Globs de exclusão, redação existente, limites | Canário em `.env`/arquivo excluído não sai |
| Histórico persistido vazando segredo | Redação antes de gravar; sem anexos/credenciais; opt-out e apagar | Varredura de canários no LiteDB após conversas com segredos sintéticos |
| Canal MCP forjado | Proof por sessão via `IAgentPrincipalAuthority`; `--strict-mcp-config`; `init` validado contra o plano | `init` com servidor extra ou tool inesperada bloqueia o turno |
| Modo Automático alterando buffer sem revisão | Só buffer, reversível, nunca disco; hunk desatualizado não aplicado | Teste de que nenhum arquivo muda em disco |
