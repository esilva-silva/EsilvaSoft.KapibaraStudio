# Spikes Codex App Server: schema e handshake

`Collect-CodexAppServerSchema.ps1` coleta somente a disponibilidade local do CLI Codex (`codex.exe` no Windows ou `codex` no Linux/macOS), sua versão e SHA-256, e gera o bundle JSON Schema correspondente àquela instalação. O coletor executa apenas `--version` e `app-server generate-json-schema --out`; ele não inicia o App Server. O probe separado descrito abaixo inicia somente o handshake STDIO.

O processo recebe um `CODEX_HOME` temporário e privado: ACL sem herança, limitada ao usuário atual, no Windows; modo POSIX `0700` no Linux/macOS. Também recebe um ambiente reduzido sem variáveis de API key, tokens ou autenticação e com os diretórios de perfil redirecionados ao scratch. Isso reduz acesso acidental à configuração do usuário, mas **não prova** que o backend de credenciais seja `keyring`, nem fornece confinamento de filesystem, processos, ferramentas nativas ou rede.

O manifesto local registra o nome do executável, versão, SHA-256 e hashes dos arquivos gerados; não registra credenciais. A saída deve ficar em diretório absoluto já existente, local e sem links/reparse points em seus ancestrais; o script também rejeita UNC e drives de rede mapeados no Windows. O script não substitui saídas existentes. A validação de caminhos não elimina TOCTOU: outro processo com acesso de escrita pode trocar diretórios entre a verificação e o uso. No Linux, o caminho local não prova que o filesystem montado é local; não use mounts remotos para esse spike.

## Execução

No PowerShell 7 no Windows ou Linux, a partir da raiz do repositório. O executável é descoberto como `codex.exe` ou `codex`, conforme o sistema:

```powershell
$output = Join-Path ([IO.Path]::GetTempPath()) ('slop-phase-07-codex-schema-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
pwsh -NoProfile -File eng/spikes/phase-07/codex-app-server/Collect-CodexAppServerSchema.ps1 -OutputDirectory $output
```

Quando não estiver no `PATH`, informe `-CodexPath` como caminho absoluto de `codex.exe` no Windows ou `codex` no Linux/macOS:

```powershell
pwsh -NoProfile -File eng/spikes/phase-07/codex-app-server/Collect-CodexAppServerSchema.ps1 `
  -CodexPath 'C:\caminho\codex.exe' `
  -OutputDirectory 'C:\evidencias\codex-app-server'
```

No Linux, o formato é `/usr/local/bin/codex` e um diretório como `/tmp/codex-app-server`. Se faltar o executável, o comando falhar, o hash mudar ou nenhum schema for gerado, a coleta falha sem produzir manifesto de sucesso. O scratch é removido apenas após validar caminho absoluto, prefixo e pai esperado; reparse points no alvo ou na árvore impedem a remoção recursiva.

## Limites da evidência

Um resultado bem-sucedido do coletor valida apenas disponibilidade, versão/hash e geração do schema específico da instalação. Ainda não valida confinamento de ferramentas nativas, backend `keyring`, handshake, login, sessão/turno, streaming, interrupção ou chamadas reais. O bundle pode descrever ferramentas poderosas; sua presença não concede autorização para expô-las.

O resultado não conclui o lote 0: continuam necessários os spikes de providers e do SDK MCP nas eras planejadas, a revisão de dependências transitivas/licenças e os gates de segurança do plano técnico. A documentação oficial descreve a geração versionada com `codex app-server generate-json-schema --out <diretório>` e classifica o App Server como experimental, sem suporte para workloads de produção: [Codex App Server](https://learn.chatgpt.com/docs/app-server).

## Execução observada

A execução Windows registrada e os seus limites estão no [acompanhamento do lote 0](../../../../docs/12-acompanhamento-da-implementacao.md#lote-0-da-v0110--spike-codex-confirmado-parcialmente--22092026). Os schemas permanecem apenas em diretório temporário local, fora do repositório.

## Probe separado de handshake STDIO

`Probe-CodexAppServer.ps1` exige PowerShell 7.4+, caminho absoluto do binário e manifesto já coletado. Compara o SHA-256 antes/depois e executa exclusivamente `app-server --listen stdio://`, com cwd privado fora do workspace, `CODEX_HOME` novo e ambiente por allowlist. Não herda PATH, proxies, tokens ou flags Codex do usuário. Configura `cli_auth_credentials_store = "ephemeral"` somente no scratch para este probe sem autenticação; essa escolha **não homologa keyring** nem altera a exigência de armazenamento seguro da integração futura.

O manifesto deve ser um arquivo regular UTF-8 local. A leitura rejeita link/reparse point no arquivo ou ancestral, verifica o tamanho pelo handle antes de ler e também limita a leitura a **1 MiB**, inclusive se o arquivo crescer. O JSON tem profundidade máxima 8; propriedades raiz duplicadas e metadados inválidos são recusados. Apenas versão/hash validados retornam ao PowerShell; falhas não reproduzem tokens do JSON. `FileShare.Read` impede escrita/exclusão concorrente compatível com compartilhamento de arquivos no Windows enquanto o handle está aberto. Isso não torna atômica a verificação de links anterior ao open, não exclui hard links nem homologa arquivos especiais/filesystems remotos no Linux.

```powershell
pwsh -NoProfile -File eng/spikes/phase-07/codex-app-server/Probe-CodexAppServer.ps1 `
  -CodexPath 'C:\caminho\codex.exe' `
  -SchemaManifestPath 'C:\evidencias\schema\codex-app-server-schema-manifest.json' `
  -OutputDirectory 'C:\evidencias\handshake'
```

O diretório de saída precisa existir e não conter `codex-app-server-handshake.json`. O helper `BoundedHandshake.cs` envia `initialize` com identidade sintética, exige resposta de ID 1 com os campos obrigatórios do schema observado, compara `codexHome` ao scratch em memória, envia `initialized` e fecha stdin. A notificação `initialized` não recebe confirmação própria; o relatório registra o envio e a saída normal após EOF. Nenhum outro método é enviado: não há login/logout, thread, turno, prompt, chamada de tool ou comando daemon.

O frame da resposta tem teto de 16 KiB e profundidade JSON de 16; stdout posterior e stderr são drenados separadamente, com teto de 64 KiB por stream, sem preservar seu conteúdo. O prazo total padrão é 15 segundos (configurável entre 1 e 60); falhas/timeout encerram a árvore do processo e não geram evidência de sucesso. A limpeza recursiva valida o pai/nome esperado e rejeita reparse points. O relatório só é escrito após saída normal, hash estável, zero `auth.json` no scratch e limpeza concluída. Não registra caminhos retornados pelo servidor, user-agent bruto, diagnósticos ou credenciais; guarda contagens, plataforma por allowlist e resultados booleanos.

Verificação do transporte com cinco subprocessos sintéticos independentes, sem Codex ou credenciais:

```powershell
pwsh -NoProfile -File eng/spikes/phase-07/codex-app-server/Test-BoundedHandshake.ps1
pwsh -NoProfile -File eng/spikes/phase-07/codex-app-server/Test-SchemaManifest.ps1
```

Os cenários cobrem resposta/EOF válidos, ID incorreto, frame acima do teto, stderr acima do teto e timeout. Não substituem homologação Linux nem teste hostil completo. O processo real pode inicializar componentes internos; o probe não observa toda atividade de filesystem, processos ou rede e **não oferece sandbox/confinamento**, isolamento comprovado de keyring ou garantia contra TOCTOU. Também não prova que uma configuração global/gerenciada nunca foi consultada. A ausência de `auth.json` limita-se ao scratch inspecionado, sem leitura do auth do usuário.

O teste do manifesto cobre entrada válida, tamanho excessivo, JSON inválido sem eco de conteúdo, profundidade excessiva, propriedade duplicada e reparse point ancestral. Inclui symlink no arquivo quando o SO permite criá-lo; no Windows sem esse privilégio, imprime `SKIP` e esse cenário permanece não homologado. Na revisão Windows de 23/09/2026 UTC, os seis cenários disponíveis e os cinco de transporte passaram; symlink no arquivo ficou pendente. Caminhos e limpeza continuam sujeitos a troca por outro processo com escrita; o scratch privado reduz acesso acidental, sem isolar processos do mesmo usuário.

O handshake Windows confirmado e suas contagens estão no [acompanhamento do lote 0](../../../../docs/12-acompanhamento-da-implementacao.md#lote-0-da-v0110--spike-codex-confirmado-parcialmente--22092026). AC-01..AC-20 e o lote 0 continuam pendentes. Protocolo consultado: [documentação oficial OpenAI — App Server](https://learn.chatgpt.com/docs/app-server#initialization).
