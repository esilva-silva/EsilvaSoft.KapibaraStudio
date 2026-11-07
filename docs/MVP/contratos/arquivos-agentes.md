# Criação de arquivos por agentes

`create_workspace_file` grava somente arquivo novo; `propose_file_edit` propõe edição revisável de buffer existente, sem salvar. Nenhuma delas executa MongoDB.

## Uso e formato

Abra uma pasta em Arquivos. Em Permissões, habilite a criação de arquivos novos e salve. O opt-in começa desligado e usa `NativeFileWrite`; não habilita Edit/Write nativos. Vale para mensagens novas, nos canais integrados Copilot/Claude com pasta e porta de criação disponíveis; não no modo Planejamento nem em clientes MCP genéricos.

Entrada fechada: `{"path":"resultado.json","content":"{\"valor\":42}\n"}`. Caminho relativo de 1..1.024 caracteres; conteúdo textual sem NUL/Unicode inválido, até 65.536 caracteres e teto total de entrada de 64 KiB UTF-8. A porta I/O tem defesa adicional de 256 KiB. A pasta pai deve existir. Publicar UTF-8 sem BOM, sem abrir/executar o arquivo ou mudar seleção/buffer. O retorno contém `status=created`, caminho relativo e quantidade de bytes; Atualizar no explorer relê a listagem.

## Autorização e publicação

Cada chamada exige confirmação pontual, inclusive em Automático. Exibir caminho/conteúdo completos; vincular autorização ao hash exato dos argumentos e consumir uma vez. Aprovação por sessão ou outro conteúdo não permite gravar.

Recusar caminhos fora da pasta capturada, exclusões, links/junctions, aliases ADS/8.3 e segmentos com ponto/espaço finais. Não criar pastas nem sobrescrever arquivo/diretório. Compartilhar o gate de mutações, escrever temporário exclusivo e publicar por move sem overwrite. Revalidar caminho, principal, turno/canal, snapshot da pasta, opt-in, exclusões e geração imediatamente antes da publicação. Duas criações concorrentes do mesmo destino têm um vencedor.

Confirmação e Intent devem ser duráveis antes da escrita. Auditar IDs/status/contagens, sem conteúdo ou caminho bruto. Risco é Write/`CreateWorkspaceFiles`, sem permissão de escrita MongoDB. Revogação/rebinding exige nova mensagem/chamada e nova aprovação; sem retry automático.

## Falhas e recuperação

| Código | Efeito e recuperação |
| --- | --- |
| `FileAlreadyExists` | Preservar destino; escolher outro nome. |
| `ParentDirectoryNotFound` | Pai ausente; ferramenta não cria diretórios. |
| `InvalidPath`, `Excluded`, `OutsideWorkspace` | Recusar destino sem gravar. |
| `PermissionDenied` | Autorização indisponível/revogada; nova decisão humana necessária. |
| `FileCreationCancelled` | Cancelamento antes da publicação; remover temporário. |
| `FileCreationFailed` | Falha I/O sanitizada. |
| `FileCreatedAuditIncomplete` | Arquivo criado e auditoria final incompleta; publicar OutcomeUnknown, não repetir automaticamente. Inspecionar destino antes de nova tentativa. |

Perda de resposta ou cancelamento após dispatch conserva efeito possível; não prometer rollback. As verificações reduzem corridas de links, mas não controlam alterações externas hostis no filesystem. Testar criação UTF-8, exclusividade, concorrência, limpeza/recuperação, revogação antes do commit, confirmação exata e falhas de auditoria. Linux nativo, leitor de tela e conta/CLI oficiais exigem evidências próprias.
