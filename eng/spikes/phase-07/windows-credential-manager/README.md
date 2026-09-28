# Spike Credential Manager Windows — lote 0 / Fase 7

**Resultado parcial em 23/09/2026:** três cenários passaram contra a API nativa Windows em uma sessão interativa de logon, usando apenas alvos novos e bytes sintéticos. Gravação, readback idêntico, metadados, exclusão e ausência após exclusão foram observados; a limpeza passou após falhas induzidas. Em uma repetição posterior pelo processo sandbox sem sessão de logon, os três `CredWriteW` falharam com `1312 / NoLogonSession` antes de gravar qualquer valor. Portanto [`evidence.json`](evidence.json) registra a repetição reprovada mais recente, enquanto os resultados da execução bem-sucedida estão registrados no transcript da execução anterior do spike, não mais em arquivo após a repetição sobrescrever o relatório.

Este spike é independente do produto. Não implementa `ISecretStore`, não modifica DI, LiteDB, sessão ou providers e não lê API keys, tokens, variáveis de credenciais ou credenciais preexistentes.

## Reprodução

Requisitos: Windows e PowerShell 7 com uma sessão de logon que tenha acesso ao próprio Credential Manager. Executar nesta pasta:

```powershell
./Test-WindowsCredentialManager.ps1
```

O script compila [CredentialManagerProbe.cs](CredentialManagerProbe.cs) em memória com `Add-Type`, executa os três cenários e grava somente resultados saneados em `evidence.json`. Não exige NuGet, restore, rede ou permissões administrativas. Falha no teste produz código de saída não zero; falha de limpeza não é escondida. Rode em sessão de logon que tenha Credential Manager disponível: execução de serviço/sandbox sem sessão pode falhar `1312`, o que é resultado negativo de disponibilidade, não teste de cofre bloqueado.

Cada cenário cria um target `EsilvaSoft.SlopStudio.Spike.Phase07.Synthetic/<GUID novo>` e 32 bytes gerados por `RandomNumberGenerator`. O alvo não pode ser fornecido externamente, e o valor não sai do método. A API só lê um alvo depois que o próprio teste o gravou. Não há `CredEnumerate`, wildcard, listagem de credenciais ou importação de dados reais. O marcador aleatório é comparado em memória e apagado dos buffers controlados pelo spike, sem conversão para string ou logging.

## Escopo do usuário

`CredWriteW`, `CredReadW` e `CredDeleteW` operam no conjunto de credenciais associado ao token/logon corrente. O teste utiliza `CRED_TYPE_GENERIC` e `CRED_PERSIST_LOCAL_MACHINE` (2): esta persistência significa **o mesmo usuário nesta máquina**, inclusive em sessões futuras, e não compartilhamento entre todos os usuários. O campo `UserName` contém apenas `synthetic-spike-only`; ele não escolhe o dono do cofre. [CredWriteW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew), [CREDENTIALW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/ns-wincred-credentialw).

O tipo/persistência retornados foram conferidos na leitura. **Outro usuário, outra sessão, reinício, roaming e backup não foram exercitados**. O escopo correto decorre do contrato nativo e da chamada sob o token atual, sem impersonation ou elevação de administrador; esta execução isolada não prova a negação de acesso por uma segunda conta. Processos com acesso ao mesmo contexto do usuário também não são isolados por aplicação pelo Credential Manager.

## Casos e resultado observado

| Cenário | Verificação nativa | Limpeza |
| --- | --- | --- |
| `Success` | `CredWriteW=true`; `CredReadW=true`; bytes e metadados idênticos | `CredDeleteW=true`; leitura retorna `ERROR_NOT_FOUND` (1168) |
| `FailureAfterWrite` | Gravação real e exceção sintética antes da leitura | `finally` exclui; leitura retorna 1168 |
| `FailureAfterRead` | Gravação/leitura reais e exceção sintética após comparação | `finally` exclui; leitura retorna 1168 |

Ambiente: Windows **10.0.26200.0**, processo **x64**, PowerShell **7.6.6**. Evidência registrada às **03:06:54 UTC / 00:06:54 America/Sao_Paulo de 23/09/2026**. Todos os três alvos criados na execução foram excluídos e sua ausência confirmada. `ReadBackMatches=false` em `FailureAfterWrite` é esperado: a exceção acontece antes da leitura; o teste exige comparação verdadeira nos outros dois cenários.

A primeira execução não interativa e a repetição independente atual retornaram **1312 / `NoLogonSession`** em `CredWriteW`, sem gravação e sem tentativa de leitura de segredo. Entre essas duas execuções, uma repetição autorizada em sessão Windows interativa conseguiu acessar o conjunto do usuário e passou os três cenários; seus contadores e resultados foram reportados na saída direta do processo, mas o arquivo então gerado foi sobrescrito pelo relatório negativo atual. Esta alternância confirma que a disponibilidade depende do contexto de logon. Nenhuma dessas falhas foi interpretada como cofre bloqueado.

## Limpeza e erros

A exclusão fica em `finally`, inclusive quando uma etapa retorna falha ou lança exceção gerenciada. O teste só exclui um target após confirmar sua própria gravação. Buffers de `CredReadW` são liberados com `CredFree`; o buffer de escrita é zerado e liberado. Após excluir, uma nova leitura do mesmo alvo verifica `ERROR_NOT_FOUND`. [CredReadW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credreadw), [CredDeleteW](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-creddeletew).

Erros nativos são capturados imediatamente com `GetLastWin32Error` e expostos apenas como códigos numéricos e categorias fixas (`Denied`, `NotFound`, `NoLogonSession`, `InvalidParameter`, `InvalidFlags`, `NativeFailure`). Exceções gerenciadas não expõem mensagem/stack. O relatório contém apenas cenário, alvo sintético, indicadores e códigos; não contém bytes, hash/fragmentos do valor ou identidade real de usuário.

`finally` dá limpeza determinística nos caminhos normais e falhas gerenciadas testados. **Não garante limpeza após encerramento forçado do processo, queda do SO ou recusa do próprio cofre ao excluir.** Nesses casos, o resultado não pode ser considerado aprovado; o prefixo sintético identifica eventual resíduo para inspeção manual. A prova não simula falha do serviço durante a exclusão e não promete eliminar cópias internas/backups do SO.

Permanecem pendentes: isolamento por segunda conta, acesso com cofre bloqueado, falhas de persistência/exclusão do SO, reinício/migração, consentimento/modo memória, integração de produto, Linux/Secret Service e todos os critérios de aceite globais da Fase 7. Nenhuma conexão MongoDB, API, provider ou teste Linux foi executado.
