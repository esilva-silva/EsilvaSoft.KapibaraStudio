# Criação de arquivos novos por agentes

Decisão solicitada pelo usuário em 07/10/2026 (ADR-067). `create_workspace_file` cria um arquivo novo e grava seu conteúdo; `propose_file_edit` mantém o contrato de edição revisável de arquivos existentes. A proibição de consultas diretas/documentos e escritas MongoDB do ADR-066 continua vigente.

## Uso

Abra uma pasta no painel Arquivos. Em **Permissões**, habilite **Permitir criar arquivos novos na workspace (cada chamada pede confirmação)** e salve. Esta opção começa desligada e usa a autorização de escrita de arquivos `NativeFileWrite` já persistida; não habilita as tools nativas Edit/Write. As permissões valem para novas mensagens. A criação está disponível nos canais integrados Copilot/Claude, quando a pasta e a porta de criação existem; não fica disponível no modo Planejamento nem para clientes MCP genéricos.

A ferramenta aceita somente `path` relativo à workspace e `content` textual:

```json
{"path":"resultado.json","content":"{\"valor\":42}\n"}
```

A pasta pai precisa existir. O arquivo é publicado completo em UTF-8 sem BOM. Resultado de sucesso: `{"status":"created","path":"resultado.json","bytes":13}` (a quantidade de bytes corresponde ao conteúdo informado). Não abre/executa o arquivo ou consulta MongoDB. A seleção do explorer e o buffer ativo não são modificados. Use **Atualizar** no painel Arquivos para atualizar a listagem. Gravar uma saída previamente autorizada em arquivo é uma ação explícita; isso não a inclui em snapshots de sessão.

## Proteções e erros

- Entrada JSON fechada: até 64 KiB UTF-8 no total; `path` de 1..1.024 caracteres; `content` até 65.536 caracteres, sem NUL/Unicode inválido, sujeito ao teto da entrada. Porta de I/O tem defesa adicional de 256 KiB.
- Caminho relativo estritamente dentro da pasta capturada. Exclusions, links/junctions, aliases ADS/8.3 e segmentos com ponto/espaço finais são recusados. Não cria pastas nem sobrescreve arquivos ou diretórios existentes.
- Confirmação obrigatória e pontual, inclusive em Automático. Exibe caminho e conteúdo completos, mantém hash dos argumentos e consome autorização uma vez. Aprovação por sessão ou para outro conteúdo não libera escrita.
- A porta local compartilha o gate de mutações de arquivos, escreve um temporário exclusivo e publica com move sem overwrite. Valida novamente os caminhos e a autorização antes da publicação. Duas criações do mesmo destino têm um único vencedor. Cancelamento/falha antes da publicação remove o temporário; não afirma rollback de efeito já concluído.
- Revalida principal, turno/canal, snapshot da pasta, opt-in, exclusões e geração/identidade do escopo antes do commit. Revogação/rebinding exige nova mensagem/chamada e nova aprovação; não há retry automático.
- Auditoria de confirmação e Intent duráveis antes da gravação. Evento de execução declara risco Write e `CreateWorkspaceFiles`; nenhuma permissão de escrita MongoDB é concedida. Auditoria contém IDs/status/contagens, não conteúdo/caminho bruto. Confirmação inline mantém os argumentos completos durante a decisão.
- `FileAlreadyExists`: destino preservado; `ParentDirectoryNotFound`: pasta pai ausente; `InvalidPath`/`Excluded`/`OutsideWorkspace`: caminho recusado; `PermissionDenied`: autorização indisponível/revogada; `FileCreationCancelled`: cancelada antes da publicação; `FileCreationFailed`: falha de I/O sanitizada.
- `FileCreatedAuditIncomplete` significa **arquivo criado e auditoria final incompleta**. Runtime publica OutcomeUnknown, sem repetir a operação nem afirmar ausência de efeito. Inspecione o destino antes de qualquer nova tentativa. Perda de resposta/cancelamento no runtime depois de dispatch também conserva efeito possível.

A checagem de caminhos reduz o risco de links alterados durante awaits; o processo não controla alterações externas hostis no sistema de arquivos. Homologação nativa por SO permanece separada dos testes offline.

## Validação

Os testes exercitam criação UTF-8, não sobrescrita, concorrência, cancelamento/limpeza/recuperação, recusa de caminhos, autorização e revogação antes do commit, aprovação pontual/exata, falha preventiva/final de auditoria e classificação de efeito possível no runtime. O roundtrip sintético utiliza SDK/STDIO/runtime/registry e a porta local real, com escrita em diretório temporário próprio. CLI/conta oficiais não são exercitados.

O aplicativo está aberto na saída Debug padrão. A compilação usa `-p:OutputPath=bin/AgentFileCreate/net10.0/`, isolada por projeto; essa versão só será usada após iniciar o executável atualizado. Não fecha/reinicia automaticamente a sessão em uso.

### Evidências Windows — 07/10/2026

Restore locked passou, inclusive na saída inicialmente isolada por artifacts. Build final da solução em saída isolada por projeto passou com **0 avisos/0 erros**, sem fechar o aplicativo em uso:

```powershell
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -p:OutputPath=bin/AgentFileCreate/net10.0/ -p:UsedAvaloniaProducts=
dotnet test tests/EsilvaSoft.KapibaraStudio.UnitTests --no-build --no-restore -p:OutputPath=bin/AgentFileCreate/net10.0/ --logger trx --results-directory artifacts/create-workspace-file/final-unit-v2
dotnet test tests/EsilvaSoft.KapibaraStudio.IntegrationTests --no-build --no-restore -p:OutputPath=bin/AgentFileCreate/net10.0/ --filter "FullyQualifiedName~AgentWorkspaceFileCreationIntegrationTests|FullyQualifiedName~CopilotRegistryRoundtripTests|FullyQualifiedName~AgentPermissionsResidualUiTests" --logger trx --results-directory artifacts/create-workspace-file/final-feature-integration
```

- Unidade final: **3.460 aprovados/20 ignorados/0 falhas**, incluindo 17 casos de criação/autorizações, troca de escopo durante Intent, efeitos de auditoria e classificação `FileCreatedAuditIncomplete` no runtime.
- Integração focal final: **26/26**, incluindo seis casos de I/O real, três roundtrips SDK/STDIO (um cria `created.json` pelo runtime com a ponte histórica de aprovação composta) e regressões de permissões/renderização. O writer recebe autorização na fronteira de publicação, e a instância de turno precisa continuar sendo a capturada antes da auditoria.
- O orçamento do prompt foi ampliado na cobertura para o catálogo completo com criação habilitada; reproduziu 2.107/2.093/2.108 bytes antes da redução do texto explicativo. O limite continua **2.048 bytes**, sem relaxar a asserção ou remover as instruções de autorização/recusa.
- A suíte integral anterior: Agents **403/403**, UnitTests **3.456 aprovados/20 ignorados**, IntegrationTests **1.023 aprovados/5 ignorados/2 falhas**. As falhas foram exclusivamente cleanup: `FailedReplacementRestoresOriginalsAndKeepsTheUpdateWithItsError(False)` (`PENDING.JSON.tmp`) e `UnknownPersistedIdentifierModeIsVisibleAndNeverOverwritten("7")` (`WORKSPACE-LOG.DB.tmp`), ambos com arquivo em uso. O foco posterior com essas fixtures e o recurso passou **30/30**, sem mudar cleanup/asserções. Não se declara aquela suíte integral verde nem se atribui o proprietário do handle; a investigação de estabilidade permanece aberta.
- O primeiro build padrão não pôde copiar DLLs porque o app estava aberto; a saída por artifacts também não preservou a localização esperada de algumas fixtures STDIO. Essas tentativas não são aprovação e foram substituídas pela saída isolada dentro de cada projeto, preservando as fixtures e a aplicação em uso.

Logs em `artifacts/create-workspace-file-final-build.log`, `artifacts/create-workspace-file-final-unit-v2.log`, `artifacts/create-workspace-file-final-feature-integration.log`; TRX nos diretórios acima. Execução integral anterior em `artifacts/create-workspace-file/complete-project-output` e `artifacts/create-workspace-file-complete-project-output.log`; foco das falhas de limpeza em `final-integration-focus` e `artifacts/create-workspace-file-final-integration-focus.log`. Nenhum benchmark foi executado.

PNGs reais pt-BR inspecionados: `agent-permissions-Light-660x760.png` (Copilot) e `agent-permissions-Claude-Dark-660x760.png`, em `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/bin/AgentFileCreate/net10.0/ui-evidence/render-39f7fb7d954f4938ba68975229e951b5/`. A opção create-only está legível/desligada, com quebra de linha e nome acessível explícito; texto Claude distingue Edit/Write nativos da criação mediada. A matriz existente de 216 PNGs foi gerada, não inspecionada integralmente. Leitor de tela/diálogo nativo, conta/CLI oficiais e filesystem Linux nativo continuam gates separados.

**Homologação manual — 07/10/2026:** o usuário autorizou registrar como homologados os testes funcionais das tools vigentes e a conexão MongoDB. [Aceite da Fase 5 e alcance](../phases/phase-05-v0.9.0/meta-permissoes-tools-copilot.md#homologação-manual-do-usuário--07102026). Este aceite é declarado pelo usuário; as execuções automatizadas acima não usaram conta/CLI ou MongoDB/Atlas reais. Cenários técnicos específicos, topologias Atlas, acessibilidade, Linux e revisão de release permanecem com suas evidências próprias.
