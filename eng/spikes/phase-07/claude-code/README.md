# Spike P7-CL0-01 — Claude Code instalado (Windows)

Spike reproduzível do binário oficial do Claude Code usado como subprocesso pelo modo "Claude (assinatura)"; consulte o [estado e os limites atuais da Fase 5](../../../../docs/phases/phase-05-v0.9.0/README.md). **Não implementa o provider**, não altera `src/` e fica fora da solução principal (sem dependências novas). Este registro de spike permanece como evidência técnica específica da antiga Fase 7; a pasta histórica `eng/spikes/phase-07/` não foi renumerada.

Execução registrada: **25/09/2026, Windows 11 Pro 10.0.26200, Claude Code 2.1.268**, conta própria do usuário (Pro, login feito por ele). Nenhuma execução em Linux.

## Regras seguidas

- Nenhum arquivo de credencial aberto, lido, listado ou hasheado; nada sob `~/.claude` foi lido pelo harness. Nenhum `login`/`logout`. Nenhum endpoint privado; binário não modificado; `--bare` não usado.
- Valores falsos (`sk-ant-invalid-spike`) só em `auth status`, nunca com `-p`.
- Argv estruturado (`ProcessStartInfo.ArgumentList`), sem shell; executável aceito somente se `.exe` com caminho absoluto.
- O spike roda dentro de outra sessão Claude Code (Claude Desktop). As variáveis herdadas com prefixo `CLAUDE`/`ANTHROPIC` (inclusive `ANTHROPIC_BASE_URL` e tokens de mensageria do host) **são removidas somente do processo filho**, para reproduzir o ambiente de um usuário comum. O produto não pode fazer isso (regra 6 do plano 23); ver risco R-CL-02.
- Transcripts sanitizados: e-mail, org, ids de conta, perfil (`<HOME>`, `<USER>`), pasta de trabalho (`<WORK>`), UUIDs truncados (`8a87f55b-…`), assinaturas de *thinking* truncadas. A varredura final procurou e-mail, orgId, orgName, usuário do SO e valor de token nos 53 arquivos: **0 ocorrências**.
- Consumo: **25 execuções `-p` contabilizadas** (23 turnos completos ou cancelados + 2 abortados no início por erro do harness, contados de forma conservadora), modelo `haiku` (`claude-haiku-4-5-20251001`), `--max-turns` 1–12, prompts sintéticos. Custo estimado pelo próprio CLI (preço de lista, não cobrança real da assinatura): US$ 0,56 somado nos 21 `result`. Registro em [`transcripts/model-calls.txt`](transcripts/model-calls.txt).

## Arquivos

| Arquivo | Papel |
| --- | --- |
| [`Invoke-ClaudeCodeSpike.ps1`](Invoke-ClaudeCodeSpike.ps1) | Cenários nomeados (`-Scenario list`). Cenários com chamada ao modelo exigem `-AllowModelCalls` e incrementam o contador |
| [`ClaudeSpike.Common.ps1`](ClaudeSpike.Common.ps1) | Resolução do executável, `auth status` com allowlist de campos, sanitizador, gravação de transcripts |
| [`SpikeProcess.cs`](SpikeProcess.cs) | Carregado por `Add-Type`: processo sem shell, stdout/stderr por linha com tempo relativo, `Kill(false)`, `Kill(true)` e Job Object `KILL_ON_JOB_CLOSE` |
| [`PermissionMcpServer.ps1`](PermissionMcpServer.ps1) | Servidor MCP STDIO mínimo com a tool `approve` usada em `--permission-prompt-tool`; políticas `allow`, `allow-bare`, `deny`, `rewrite`, `persist`, `delay:<s>`, `deny-webfetch`; registra só dados sintéticos e **nomes** de variáveis |
| [`transcripts/`](transcripts/) | Saída sanitizada de cada cenário (`<cenário>.jsonl`, `<cenário>.mcp.jsonl`, `auth-status.json`). Servem de base para as fixtures do CLI falso (P7-CL6-01) |

Execução (PowerShell 7):

```powershell
$work = Join-Path $env:TEMP 'slop-cl0'   # pasta vazia, fora do repositório
pwsh -NoProfile -File eng/spikes/phase-07/claude-code/Invoke-ClaudeCodeSpike.ps1 -Scenario auth -WorkRoot $work -OutDir "$work\transcripts"
pwsh -NoProfile -File eng/spikes/phase-07/claude-code/Invoke-ClaudeCodeSpike.ps1 -Scenario perm-tools -AllowModelCalls -WorkRoot $work -OutDir "$work\transcripts"
```

`-ClaudePath` aceita o caminho absoluto de `claude.exe`; sem ele, o script procura no `PATH` e no pacote WinGet `Anthropic.ClaudeCode`. Revise os transcripts antes de versioná-los.

## Fatos observados (2.1.268, Windows)

### Instalação e binário

- WinGet instala `claude.exe` nativo (221.637.792 bytes) em `%LOCALAPPDATA%\Microsoft\WinGet\Packages\Anthropic.ClaudeCode_…\`; sessões abertas antes da instalação não o têm no `PATH`. Não havia shim `.cmd`.
- `Get-AuthenticodeSignature`: **Valid**, assinante `CN="Anthropic, PBC"`, emissor DigiCert Trusted G4 Code Signing, com carimbo de tempo. A Anthropic não documenta esse procedimento; é verificação do SO.
- `--help` lista `--permission-mode` com `acceptEdits, auto, bypassPermissions, manual, dontAsk, plan`, **sem `default`**; a [CLI reference](https://code.claude.com/docs/en/cli-reference) diz que `default` e `manual` são equivalentes, e `--permission-mode default` foi aceito (exit 0). O `init` reporta `permissionMode: "default"`.
- Flags novas relevantes, não previstas no plano: `--permission-prompts host|none` (≥ 2.1.259), `--restricted`, `--safe-mode`, `--include-hook-events`, `--replay-user-messages`, `--max-budget-usd`, `--fallback-model`.

### `auth status` ([auth-status.json](transcripts/auth-status.json))

JSON por padrão (`--json`), `--text` com três linhas (`Login method`, `Organization`, `Email`). Exit 0 em todos os casos logados; `--setting-sources` inválido dá exit 1 com `Invalid setting source: … Valid options are: user, project, local`.

| Caso (sem `-p`) | `authMethod` | `apiKeySource` | `subscriptionType` | Campos de conta |
| --- | --- | --- | --- | --- |
| Ambiente limpo | `claude.ai` | ausente | `pro` | email, orgId, orgName |
| `ANTHROPIC_API_KEY` falsa | **`claude.ai`** | `ANTHROPIC_API_KEY` | **`null`** | presentes |
| `ANTHROPIC_AUTH_TOKEN` falso | `oauth_token` | ausente | ausente | ausentes |
| `CLAUDE_CODE_OAUTH_TOKEN` falso | `oauth_token` | ausente | ausente | ausentes |
| `--settings '{"apiKeyHelper":…}'` antes de `auth status` | `api_key_helper` | `apiKeyHelper` | ausente | ausentes |
| `--settings '{"env":{"ANTHROPIC_API_KEY":…}}'` | `claude.ai` | `ANTHROPIC_API_KEY` | `null` | presentes |
| `apiKeyHelper` em `.claude/settings.json` do cwd, fontes padrão ou `project` | `api_key_helper` | `apiKeyHelper` | — | — |
| Mesmo projeto com `--setting-sources user` ou `""` | `claude.ai` | ausente | `pro` | presentes |

Conclusões: (1) `authMethod` sozinho **não** distingue assinatura de API Key; o discriminador é `apiKeySource` presente ou `subscriptionType` nulo. (2) `ANTHROPIC_AUTH_TOKEN` (cobrança por gateway/API) e `CLAUDE_CODE_OAUTH_TOKEN` (assinatura) produzem a **mesma** saída `oauth_token`; só os nomes das variáveis os separam. (3) `auth status` **reflete** `--settings`, `--setting-sources`, env e cwd do comando quando recebe as mesmas flags globais antes do subcomando. (4) Nenhuma variável de nuvem foi testada.

### stream-json ([basic-turn1](transcripts/basic-turn1.jsonl))

- **Sem mensagem no stdin, nada é emitido**: nem `system/init` em 20–40 s ([init-default](transcripts/init-default.jsonl)); fechar o stdin encerra com exit 0 sem `result`. O `init` só chega **depois** de a CLI ler a primeira mensagem (≈0,8–2,3 s), e `system/status: requesting` sai 2–6 ms depois dele. Não há janela útil para validar o `init` antes da requisição ao modelo.
- Ordem típica: `rate_limit_event` (pode vir antes do `init`) → `system/init` → `system/status` → `stream_event` (`message_start`, `content_block_start/delta/stop`, `message_delta`, `message_stop`) intercalado com `system/thinking_tokens` e `assistant` (uma mensagem por bloco) → `user` com `tool_result` e `tool_use_result` → `result`.
- `system/init` (campos): `type, subtype, cwd, session_id, tools, mcp_servers[{name,status}], model, permissionMode, slash_commands, terminal_slash_commands, apiKeySource ("none" na assinatura), claude_code_version, output_style, agents, skills, plugins, capabilities, analytics_disabled, product_feedback_disabled, uuid, memory_paths, messaging_socket_path, fast_mode_state, fast_mode_disabled_reason, powershell_path`. `capabilities = ["interrupt_receipt_v1","interrupt_cancel_queued_v1","msg_lifecycle_v1"]`. **Não há campo de fontes de configuração, hooks ou managed settings.**
- `result`: `subtype` (`success`, `error_during_execution`), `is_error`, `num_turns`, `session_id`, `total_cost_usd`, `usage`, `modelUsage` (com `costBasis`, `provider`), `permission_denials[{tool_name,tool_use_id,tool_input}]`, `terminal_reason` (`completed`, `aborted_tools`), `stop_reason`, `subagent_stats`, `api_error_status`, `errors`.
- Blocos de *thinking* chegam com texto vazio e `signature` opaca: não exibir nem persistir.
- Não observados: `system/api_retry` (nenhum erro de API) e `system/permission_denied` (negações por host ou por regra aparecem só como `tool_result` com `is_error` e em `result.permission_denials`; negações de `Read` por regra `deny` **não** entram em `permission_denials`).
- Com `--include-hook-events`: `system/hook_started` e `system/hook_response` antes do `init`. Observados também `system/task_started`/`task_notification` para Bash longo.

### Sessão

- `--session-id <uuid>` no primeiro processo e `--resume <uuid>` no segundo mantiveram o contexto (resposta `ABACAXI-7`).
- `--no-session-persistence` existe; `--resume` dessa sessão falha **antes** do modelo: stderr `No conversation found with session ID`, `result` `error_during_execution`, custo 0, exit 1. O mesmo caminho serve para "sessão inválida/expirada" sem gasto.
- `memory_paths.auto` aponta para `~/.claude/projects/<cwd codificado>/memory/` (auto memória ativa mesmo com `--setting-sources ""`).

### `--permission-prompt-tool` ([perm-*](transcripts/))

- O servidor MCP é iniciado ≈0,6 s após o processo, **antes** de qualquer mensagem (initialize, `notifications/initialized`, `tools/list`), e recebe o ambiente do `claude` (canário presente) **mais** variáveis injetadas: `CLAUDECODE`, `CLAUDE_CODE_ENTRYPOINT`, `CLAUDE_CODE_SESSION_ID`, `CLAUDE_PROJECT_DIR`, `CLAUDE_CODE_MESSAGING_SOCKET` e `CLAUDE_CODE_MESSAGING_TOKEN`.
- Pedido: `tools/call` com `arguments = { tool_name, input, tool_use_id }` e `_meta = { "claudecode/toolUseId", progressToken }`.
- Resposta como texto JSON em `content[0].text`: `{"behavior":"allow","updatedInput":{…}}` executa **exatamente** `updatedInput` (troca de `mkdir` por `echo REWRITTEN-BY-HOST` executou o `echo`); `{"behavior":"deny","message":…}` vira `tool_result` com `is_error` e a mensagem, e entra em `permission_denials`. `allow` sem `updatedInput` não foi exercitado (cenário `perm-allow-bare` existe, não executado).
- `updatedPermissions` com `destination: "session"` fez a segunda chamada Bash **não** consultar o host; nada foi gravado no cwd. Sem `updatedPermissions`, nenhuma regra foi criada (nenhum `.claude/` no cwd em todos os cenários).
- A tool de permissão **não aparece** em `init.tools` e o modelo não a enxerga (listou suas tools sem `mcp__…`); `--disallowedTools mcp__slopperm__approve` não impede o uso pela CLI.
- Correlação: o `tool_use` completo sai no stdout 0,2–1,4 ms **depois** do pedido MCP; o `content_block_start` parcial (com `id` e `name`, input vazio) sai centenas de ms antes. O Slop precisa de janela curta de espera para correlacionar por `tool_use_id`.
- Prazo: com `MCP_TOOL_TIMEOUT=8000`, host lento vira `tool_result` de erro "`tool "approve" timed out after 8s`" e **nada executa** (fecha falho), sem entrada em `permission_denials`. Sem a variável, a CLI esperou 75 s e aplicou a decisão tardia.

### Quais ferramentas pedem permissão (modo default, `--setting-sources ""`)

| Pediu ao host | Não pediu |
| --- | --- |
| `Write`, `Edit`, `Bash mkdir …`, `Bash echo "$0 $BASH_VERSION"`, `Bash alias \| head -3`, `WebFetch` (localhost), `Read`/`Glob` quando há regra `ask` | `Read`, `Glob`, `Grep` no cwd; `Bash echo spike-bash` (comando somente leitura); `ToolSearch`; `TaskCreate` |

`TodoWrite` não existe nesta versão (substituído por `TaskCreate`…). A [tabela oficial de tools](https://code.claude.com/docs/en/tools-reference) marca **sem permissão** também `Agent`, `SendMessage`, `CronCreate/Delete/List`, `RemoteTrigger`, `PushNotification`, `ScheduleWakeup`, `ListAgents`, `ReportFindings`, `AskUserQuestion`, `EnterPlanMode`, `ExitWorktree`; com permissão `PowerShell`, `Monitor`, `NotebookEdit`, `WebSearch`, `Skill`, `EnterWorktree`, `ExitPlanMode`. Essas não foram exercitadas uma a uma.

### Configuração e isolamento

- `--settings` inline com `permissions.ask` (`Read`, `Glob`, `Grep`) forçou pedido para `Read` e `Glob`; `deny` `Read(./secret.txt)` bloqueou a leitura e **filtrou** o arquivo do `Glob`; `deny` `Bash(mkdir proibido*)` negou sem consultar o host. `disableAllHooks` aceito.
- `--setting-sources`: valores `user`, `project`, `local` (vírgula); `""` aceito; `managed` rejeitado. Mesmo com `""`, o `init` trouxe 16 skills, 5 agentes, auto memória e — sem `--strict-mcp-config` — o conector remoto da conta claude.ai (`claude.ai Claude Docs`, 8 tools).
- Projeto sintético com hook `SessionStart`, `permissions.allow` e `.mcp.json`: com fontes padrão, o **hook rodou** em `-p` (arquivo criado), o `.mcp.json` foi conectado sem pergunta e a regra `allow` foi **ignorada** com aviso no stderr (workspace não confiável). Com `--setting-sources user --strict-mcp-config --settings '{"disableAllHooks":true}'`: nenhum hook, nenhum MCP de projeto, conector claude.ai ausente.
- `--tools "Bash,Read,Edit,Write,Glob,Grep,WebFetch"` reduziu `init.tools` exatamente a essa lista (sem `Agent`/`Task`/`Skill`/`ToolSearch`); o modelo não conseguiu delegar.
- `--restricted` removeu Bash, PowerShell, WebFetch, Monitor, CronCreate e RemoteTrigger, mas **manteve** `Task` (Agent), `Write`, `Edit`, `SendMessage` etc.
- Bash no Windows: Git Bash (`/usr/bin/bash 5.3.15`); também existe a tool `PowerShell` (`powershell_path` no `init`). Nenhum alias apareceu (o usuário pode não ter); a documentação diz que `~/.bashrc`/`~/.profile` são carregados.
- Um Bash aprovado enxerga `CLAUDECODE`, `CLAUDE_CODE_CHILD_SESSION`, `CLAUDE_CODE_ENTRYPOINT`, `CLAUDE_CODE_EXECPATH`, `CLAUDE_CODE_MESSAGING_SOCKET`, `CLAUDE_CODE_MESSAGING_TOKEN`, `CLAUDE_CODE_SESSION_ID`, `CLAUDE_PID` e o canário do pai (apenas nomes foram coletados).
- Sandbox da CLI: segundo a documentação, só macOS/Linux/WSL2; não vale no Windows nativo.
- Managed settings: `C:\Program Files\ClaudeCode\managed-settings.json` e `HKLM\SOFTWARE\Policies\ClaudeCode` ausentes nesta máquina; o `init` não informa managed settings.

### Cancelamento no Windows (`ping -n 45 127.0.0.1` via Bash aprovado)

| Método | claude.exe | Filho `PING.EXE` | Turno |
| --- | --- | --- | --- |
| `Process.Kill(false)` (só o claude) | encerra (-1) | **sobrevive (órfão)** | sem `result` |
| `Process.Kill(true)` (árvore) | encerra | encerrado | sem `result` |
| Job Object `KILL_ON_JOB_CLOSE` + `CloseHandle` | encerra | encerrado | sem `result` |
| Fechar stdin | continua | continua | termina normalmente depois |
| `{"type":"control_request","request_id":…,"request":{"subtype":"interrupt"}}` no stdin | continua até fechar stdin (exit 1) | encerrado | `control_response` `success` em 8 ms, `tool_result` "rejected", `result` `error_during_execution`, `terminal_reason: aborted_tools` |

O formato `control_request`/`interrupt` é o protocolo SDK↔CLI; a documentação pública cita `interrupt()` do SDK e as capabilities, mas **não** publica esse formato para uso direto. SIGINT/CTRL_BREAK não foi testado no Windows. O processo filho em si pode ficar fora do Job se nascer antes do `AssignProcessToJobObject` (o produto deve criar o processo suspenso ou usar `PROC_THREAD_ATTRIBUTE_JOB_LIST`).

## Hipóteses do threat model 22

| ID | Resultado | Evidência / observação |
| --- | --- | --- |
| H-01 | **Parcial** | `--setting-sources user` ou `""` exclui `apiKeyHelper` e hooks de **projeto** sem afetar a assinatura; `permissions.allow` de projeto já é ignorado em `-p`. Excluir `user` não foi testado sobre as configurações reais do usuário (proibido tocar `~/.claude`). Skills, agentes, auto memória e conectores claude.ai continuam (esses últimos saem com `--strict-mcp-config`) |
| H-02 | **Parcial** | Tipo de conta e método por `authMethod` + `apiKeySource` + `subscriptionType`; `authMethod` fica `claude.ai` com API Key no ambiente; `ANTHROPIC_AUTH_TOKEN` e `CLAUDE_CODE_OAUTH_TOKEN` indistinguíveis (`oauth_token`). Nuvem não testada |
| H-03 | **Confirmada** (com ressalva) | `init` traz `apiKeySource`, `model`, `permissionMode`, `tools`, `mcp_servers`, `plugins`, `claude_code_version`; não traz fontes de configuração nem hooks |
| H-04 | **Parcial** | `init` lista `skills`, `agents`, `plugins` e tools de MCP (inclusive conector claude.ai); não havia plugin instalado para testar |
| H-05 | **Confirmada** | A tool de permissão fica fora de `init.tools` e invisível ao modelo; `--disallowedTools` nela não quebra o fluxo |
| H-06 | **Confirmada** | `updatedInput` alterado foi o executado |
| H-07 | **Confirmada** (com ajuste) | Pedido traz `tool_use_id` (e `_meta.claudecode/toolUseId`); o `tool_use` completo chega ~1 ms **depois** do pedido, o parcial antes |
| H-08 | **Confirmada** (no cwd) | Sem `updatedPermissions` nada é criado; com `destination: session` a regra vale só no processo. `~/.claude` não foi inspecionado |
| H-09 | **Confirmada** | Read/Glob/Grep no cwd sem pedido; `ask` via `--settings` força pedido; `deny` nega e filtra |
| H-10 | **Confirmada** | `MCP_TOOL_TIMEOUT` controla; estouro = erro de tool, nada executa; sem a variável esperou ≥ 75 s. Padrão exato não documentado |
| H-11 | **Confirmada** | MCP iniciado no startup, antes da primeira mensagem; herda o ambiente do `claude` e recebe `CLAUDE_CODE_MESSAGING_*`/`CLAUDE_PROJECT_DIR` |
| H-12 | **Refutada** (para o `init`) | Nenhum campo de managed settings; detecção só por caminhos/registro documentados (existência, sem ler) ou `/status` interativo |
| H-13 | **Não testada** | Exigiria rede externa (proibida no spike). Documentação não descreve redirecionamento do WebFetch |
| H-14 | **Refutada no Windows** | Sandbox da CLI documentado só para macOS/Linux/WSL2; Linux pendente |
| H-15 | **Parcial** | Authenticode válido (Anthropic, PBC) verificável pelo SO; sem procedimento da Anthropic; Linux pendente |
| H-16 | **Confirmada por documentação** | Windows e Linux: `~/.claude/.credentials.json` (Windows herda ACL do perfil; Linux 0600); macOS Keychain. Arquivo não aberto nem verificado |
| H-17 | **Confirmada** | Git Bash no Windows; docs: perfil do shell carregado; também há tool `PowerShell` |
| H-18 | **Parcial** | Interrupt por `control_request` funciona e mata o filho, mas formato não documentado publicamente; fechar stdin não cancela; Windows sem SIGINT testado; kill da árvore/Job Object é o caminho confiável |
| H-19 | **Parcial** | Versões mínimas documentadas: `--permission-prompt-tool` (espera de MCP) 2.1.199, `capabilities` 2.1.205, `--forward-subagent-text` 2.1.211, stdin no Windows 2.1.211, `mcp_server_errors` 2.1.219, espera `--mcp-config` 2.1.221, `--resume` entre diretórios 2.1.223, checagem de redirecionamento de entrada 2.1.257, `--permission-prompts` 2.1.259. As demais sem versão; só 2.1.268 testada |
| H-20 | **Confirmada** | `--tools` restringe exatamente; lista de isentas de pedido acima (observada + documentação) |
| H-21 | **Confirmada: `init` vem depois** | Nada é emitido antes da primeira mensagem; `init` e requisição ao modelo ficam a milissegundos |
| H-22 | **Confirmada** | Flag existe, só `-p`; `--resume` falha sem custo |
| H-23 | **Não executada** (decisão pendente) | Documentação: `CLAUDE_CONFIG_DIR` muda o local de `.credentials.json` (login separado). Não testado para não exigir novo login; conflito possível com GCL-1 |
| H-24 | **Confirmada** | `--settings` com `ask`/`deny`/`disableAllHooks` funciona; deny vence allow em qualquer fonte (docs); não altera a autenticação observada |
| H-25 | **Confirmada** | WinGet entrega `claude.exe` nativo |

## Riscos novos

| ID | Risco | Tratamento proposto |
| --- | --- | --- |
| R-CL-01 | `authMethod` continua `claude.ai` com `ANTHROPIC_API_KEY` no ambiente | Classificar por `apiKeySource`/`subscriptionType` e nomes de variáveis, nunca só por `authMethod` |
| R-CL-02 | Slop iniciado de dentro de um terminal/app Claude Code herda `CLAUDECODE`, `CLAUDE_CODE_*` e `ANTHROPIC_BASE_URL` do host | Detectar por nome e bloquear/avisar ("ambiente de outra sessão Claude Code"); não remover (regra 6) sem decisão |
| R-CL-03 | Comando aprovado e servidor MCP recebem `CLAUDE_CODE_MESSAGING_TOKEN`/`SOCKET` (canal com a sessão) | Registrar como residual do item 1; avaliar com mcp-integration-agent o que o canal permite |
| R-CL-04 | Tools sem pedido de permissão incluem `Agent`, `SendMessage`, `CronCreate`, `RemoteTrigger`, `PushNotification`, `ScheduleWakeup` | Usar `--tools` com allowlist explícita (não `--disallowedTools`), validada contra `init.tools` |
| R-CL-05 | `--setting-sources ""` não remove skills, agentes, auto memória nem conectores claude.ai | `--strict-mcp-config` obrigatório; `--tools` sem `Skill`/`Agent`; auto memória como limitação visível (grava em `~/.claude/projects`) |
| R-CL-06 | Sem `MCP_TOOL_TIMEOUT`, a CLI espera o diálogo por tempo indeterminado (≥ 75 s) | O Slop define o próprio prazo do diálogo e nega; avaliar passar `MCP_TOOL_TIMEOUT` ao filho (variável não relacionada à autenticação) |
| R-CL-07 | Interrupt limpo depende de protocolo não documentado; kill só do `claude.exe` deixa órfãos | Job Object obrigatório (Windows) / grupo de processos (Linux); interrupt só com decisão explícita |
| R-CL-08 | Hooks de projeto rodam em `-p` com fontes padrão | Argv com `--setting-sources user` (ou `""`) + `--settings '{"disableAllHooks":true}'` |

## Recomendações para CL1..CL4

- **CL1-01:** aceitar só `.exe` absoluto (Windows); registrar versão, tamanho e resultado Authenticode; versão mínima inicial **2.1.268** (única testada) até haver matriz; recusar se `--permission-prompts`/`capabilities` ausentes.
- **CL1-03:** executar `auth status` com o **mesmo** argv global do turno (`--settings`, `--setting-sources`), env e cwd, imediatamente antes de escrever o prompt. Assinatura somente se `loggedIn && authMethod == "claude.ai" && apiKeySource ausente && subscriptionType não nulo`; `oauth_token` → método "token no ambiente", bloqueado por padrão com nome da variável; `api_key_helper`/`apiKeySource` → bloqueio. Allowlist de campos (sem e-mail/org).
- **CL3-01:** argv proposto: `-p --output-format stream-json --input-format stream-json --verbose --include-partial-messages --permission-mode default --max-turns N --strict-mcp-config --mcp-config <arquivo privado> --permission-prompt-tool mcp__<slop>__<tool> --tools <allowlist> --setting-sources <decisão GCL-1> --settings <arquivo do Slop com deny/ask/disableAllHooks>`; opcional `--no-session-persistence`. Validar `init` (versão, `apiKeySource=="none"`, `permissionMode=="default"`, `tools` == allowlist, `mcp_servers` == só o Slop `connected`) e abortar/avisar em divergência — sabendo que a requisição já saiu. Mensagem só por stdin; fechar stdin após `result`.
- **CL3-03:** `--resume` com o `session_id` do `init`; tratar `No conversation found` como sessão expirada sem custo.
- **CL3-04:** Job Object `KILL_ON_JOB_CLOSE` criado antes de qualquer filho; kill da árvore como caminho normal de cancelamento com `OutcomeUnknown`; interrupt por `control_request` só após decisão sobre documentação.
- **CL4-01/02:** pedido `{tool_name,input,tool_use_id}`; resposta sempre com `updatedInput` igual ao exibido; nunca `updatedPermissions`; correlação por `tool_use_id` com espera curta pelo `assistant` completo; prazo do diálogo abaixo de `MCP_TOOL_TIMEOUT` definido pelo Slop.

## Limitações

- Somente Windows 11 e versão 2.1.268; Linux, variáveis de nuvem, plugins instalados, `CLAUDE_CONFIG_DIR`, redirecionamento do WebFetch, SIGINT/CTRL_BREAK, `allow` sem `updatedInput`, `--safe-mode` e `--permission-prompts none` com `-p` não foram exercitados.
- Configuração real do usuário (`~/.claude/settings.json`) não foi lida; efeitos de `--setting-sources` sobre regras/hooks do usuário foram inferidos por documentação e por settings de projeto sintéticos.
- O próprio spike roda sob outra sessão Claude Code; o ambiente limpo é uma aproximação do ambiente do usuário final.
- O custo em US$ é estimativa do CLI a preço de lista; o consumo real é da cota da assinatura (`rate_limit_event` mostra utilização da janela).
