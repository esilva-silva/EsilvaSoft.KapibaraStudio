# Investigação do Copilot em Release — 03/10/2026

## Resultado

A etapa inicial da investigação foi dividida entre Sol (runtime, sessões e contratos do SDK), Luna (descoberta, empacotamento e histórico) e o chat principal (documentação oficial e verificações locais). Naquela etapa não houve alteração de código, dependências, permissões ou instalação, e a causa ainda não havia sido reproduzida. A reprodução posterior está registrada abaixo em “Reprodução da falha de sessão persistente”.

O contrato de distribuição mudou em `9b16058`: desde 02/10, o produto não inclui runtime/CLI Copilot. Login, conta e sessões usam `copilot.exe` instalado pelo usuário e encontrado no PATH. Isso explica por que o funcionamento antigo com runtime incluído não valida o pacote atual. Entretanto, os diagnósticos desta investigação confirmaram que a CLI WinGet do PATH padrão funciona fora do sandbox, autentica como usuário, lista modelos e aceita criação de sessão e restrição de agentes internos em testes compilados em Release.

## Evidências de código e histórico

- `Directory.Build.props`: `CopilotSkipCliDownload=true` para todo o build, não apenas Release.
- `CopilotRuntimeSettings.CommonClientOptions`: conta e sessão usam `RuntimeConnection.ForStdio(path: ...)`, `UseLoggedInUser=true`, ambiente filho filtrado e `BaseDirectory` no diretório oficial do usuário.
- `LocalCopilotAccountCommands.FindCliExecutable`: considera apenas diretórios absolutos do PATH do processo; no Windows procura `copilot.exe`, ignora shims npm `.cmd` e retorna o primeiro arquivo existente. A descoberta Windows não comprova que esse arquivo pode iniciar nem valida protocolo. Não há tentativa silenciosa de outro candidato após falha de startup.
- `LocalCopilotRuntimeResources`: clientes de conta e sessão compartilham a descoberta e configuração; sessão acrescenta SessionFs persistente ou volátil.
- `SdkCopilotRuntimeClient`: após criar/retomar a sessão, exige sucesso de `options.update(includedBuiltinAgents: [])` antes de enviar prompt.
- `App.axaml.cs` e projeto Desktop: não foi encontrado gate de composição Copilot específico de Debug/Release. O gate Release existente é do Claude. O publish exige ausência dos binários Copilot por decisão de distribuição, registrada no ADR-062.
- A documentação de 02/10 registrava comparação de autenticação sem prompt; esse teste pode passar com ambos os clientes desautenticados. Não comprovava conta autenticada, modelos ou conversa com a instalação externa.

## O que significa o erro da imagem

Em `CopilotSubscriptionAgentSession.ProduceAsync`, o catch genérico descarta a exceção e escolhe `CopilotSessionUnavailable` somente porque `_providerSessionId` está preenchido. Portanto o nome não demonstra que a sessão foi perdida. Pode representar startup, consulta de autenticação/metadados, retomada/criação, restrição de agentes ou envio, dependendo do estado anterior.

Uma falha de autenticação explicitamente reportada gera `CopilotNotLoggedIn` ou `CopilotNonSubscriptionAuth`; ferramentas ausentes ou sem schema têm códigos próprios. Eventos `SessionErrorEvent` são traduzidos para `CopilotProviderFailure`. Logo, a imagem sugere uma exceção no caminho de lifecycle/RPC, sem identificar seu estágio.

O aviso de fallback é emitido antes de a criação substituta terminar. Sua presença não comprova que uma nova sessão foi criada com sucesso. Alterar as permissões de ferramentas pela interface não corrige necessariamente esse caminho: o registro de ferramentas e os grants do produto são uma camada distinta da execução do processo Copilot.

## Verificações executadas

Na etapa inicial foram usados os testes explícitos existentes, sem modificar fontes ou compilar novamente a solução. O assembly de integração Release utilizado tinha data de 02/10/2026. Nenhum prompt, inferência, login/logout, consulta MongoDB ou ferramenta sobre dados do usuário foi executado. Nenhum arquivo de credenciais foi lido pelo investigador; a autenticação foi consultada pelo SDK/CLI oficial. Os testes posteriores da correção são evidência separada, não reexecução integral.

| Verificação | Resultado | Limite |
| --- | --- | --- |
| Descoberta local | Duas instalações nativas `copilot.exe`; também existe `gh.exe` | O adapter usa Copilot CLI, não o executável GitHub CLI `gh` |
| WinGet `--version` dentro do sandbox | Acesso negado | Bloqueio do ambiente de investigação |
| WinGet `--version` fora do sandbox | CLI 1.0.89 | Não indica falha de instalação |
| Segunda instalação nativa `--version` | CLI 1.0.89 | Não altera PATH persistente da máquina |
| Create/delete volátil e restrição de agentes, Release, segunda CLI | 2/2 aprovados | Sem prompt; não valida retomada persistente ou streaming |
| Mesmos testes e conta/modelos, Release, PATH padrão fora do sandbox | 3/3 aprovados | Conta oficial autenticada nos dois clientes, mesmo catálogo não vazio; sem inferência |
| Processo Desktop aberto durante a investigação | Executável em `bin/Debug/net10.0` | Não identifica o executável que produziu a captura enviada |

Resultados locais: `TestResults/CopilotReleaseInvestigation/copilot-no-prompt.trx` e `copilot-native-path.trx`. Os diretórios de resultados são artefatos locais e não precisam estar versionados. O PATH foi ajustado somente no processo de um comando para selecionar a segunda CLI; não houve alteração permanente de ambiente.

## Hipóteses da etapa inicial (antes da reprodução) e resultado

1. **Ambiente da instância que falhou:** PATH herdado pelo atalho/Explorer pode diferir do processo de teste; a primeira instalação pode estar ausente, bloqueada ou incompatível naquela instância. Não foi extraído o ambiente de outro processo nem alterado o atalho. A reprodução posterior isolou uma falha independente de PATH na configuração persistente.
2. **Retomada e persistência:** os testes iniciais cobriam sessão nova volátil. A reprodução posterior confirmou falha ao criar sessão persistente; os testes oficiais focados de persistência/retomada/erase passaram após a correção, mas IntegrationTests ainda está em execução.
3. **Envio e streaming:** a etapa inicial não reproduziu o erro. O teste manual oficial de persistência, stream, resume e erase posterior passou 1/1; isso não homologa conversa real pela UI do produto.
4. **Artefato efetivamente instalado:** ainda é necessário relacionar a reprodução ao caminho/versão do executável publicado e à CLI selecionada nessa mesma execução. Testar assembly Release não equivale a homologar um pacote single-file aberto por atalho.

A recomendação de observabilidade acima pertence à etapa inicial. A implementação atual passou a emitir códigos sanitizados por etapa; não envia prompt, anexos, argumentos de ferramentas, stdout/stderr bruto, credenciais ou exceções privadas à UI. Não há evidência que justifique abrir ferramentas nativas ou voltar a redistribuir o runtime.

## Referências

- [Revisão local da distribuição](release-copilot.md) e [ADR-062](../10-decisoes-arquiteturais.md).
- [SDK .NET fixado em v1.0.14](https://github.com/github/copilot-sdk/blob/v1.0.14/dotnet/README.md): caminho explícito de runtime stdio é suportado; BaseDirectory define COPILOT_HOME.
- [Implementação oficial Client.cs v1.0.14](https://github.com/github/copilot-sdk/blob/v1.0.14/dotnet/src/Client.cs): startup e validação do protocolo, configuração SessionFs e transporte de processos.
- [Instalação oficial do Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/install-copilot-cli): GitHub documenta WinGet e npm; a limitação a executável nativo no Windows é uma decisão deste produto, não uma proibição geral do GitHub.

As verificações da etapa inicial não declaravam homologação de conversa real, pacote instalado, OAuth novo, ferramentas MongoDB, Linux/ARM64 ou interface nativa; os limites após a correção estão registrados na seção de reprodução abaixo.

## Reprodução da falha de sessão persistente — 03/10/2026

A reprodução posterior isolou a falha no `session.create` do fluxo com retenção: RPC `-32603`, `Caminho SessionFs fora da raiz virtual.` O `SessionFsConfig` persistente usava `SessionStatePath = "session-state"` junto de convenções Windows e working directory do host. O runtime expandia o caminho relativo para uma forma com raiz de unidade Windows; o mapper persistente corretamente rejeitava `:` durante a normalização para impedir saída da raiz virtual. A configuração volátil usava outro mapper, sem a mesma rejeição de dois-pontos, razão pela qual os testes anteriores de create/delete volátil não cobriam essa falha.

A correção configura o namespace virtual como POSIX em qualquer host e usa `SessionStatePath = "/session-state"`, sem relaxar `Normalize`, a contenção da raiz, as verificações de reparse point ou as permissões. Como o SDK exige working directory POSIX quando as convenções são POSIX, `InitialWorkingDirectory` do SessionFs é `/workspace`; separadamente, `CopilotClient.WorkingDirectory` recebe o diretório real capturado pelo turno para o processo da CLI. O working directory real deixa de ser usado para derivar caminhos virtuais do estado da sessão.

A mesma revisão substitui a classificação genérica baseada na presença de um ID reservado por códigos sanitizados da etapa que falhou (`CopilotRuntimeStartFailed`, autenticação, configuração, lookup, resume, create, política, envio ou stream). ID reservado significa identidade pretendida, não prova de sessão nativa estabelecida. Exceções privadas não são encaminhadas à UI.

O relato de reprodução e a mudança de código foram fornecidos pela equipe durante esta investigação. Depois da correção, o teste manual oficial de persistência, retomada, streaming e erase passou **1/1** (`TestResults/CopilotSessionFailure/persistent-official-posix.trx`); o adapter de produção com reserva/hooks/permissões/contexto e restrição de agentes passou **1/1** (`TestResults/CopilotSessionFailure/reserved-production-posix.trx`). O filtro storage/runtime/discovery passou **25/25**. A suíte Release terminou sem falhas, em execuções por assembly: Agents **285/285**, UnitTests **3.374 aprovados e 20 ignorados**, IntegrationTests **909 aprovados e 5 ignorados** (`TestResults/CopilotSessionFix/suite`). O publish single-file self-contained win-x64 em `artifacts/copilot-session-fix/win-x64` passou `Test-ReleasePackage.ps1`. Não houve reexecução da UI nativa; esses resultados não são homologação de diálogo nativo, acessibilidade ou conversa pelo pacote aberto como usuário. Nenhum trust/permissão foi alterado e nenhum arquivo de credencial foi acessado. A causa observada não exigiu alteração de instalação da CLI ou PATH.

## Complemento — hipótese de folder trust

Após o usuário apresentar a confirmação de confiança da CLI interativa para `C:\Users\chuke`, foi conferido o contrato do SDK .NET 1.0.14: ele inicia a CLI com `--headless --no-auto-update --stdio`. A CLI interativa aberta pelos comandos de conta usa o perfil do usuário como diretório de trabalho; o chat usa a pasta de workspace capturada ou, na ausência dela, o diretório atual do processo. Assim, a janela interativa não comprova que o runtime do chat esteja aguardando aquela mesma confirmação.

A [documentação oficial de configuração](https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/configure-copilot-cli) distingue confiança da pasta e permissões de ferramentas. Não houve leitura de arquivos de credenciais/configuração, aprovação de pasta, chamada `AddTrustedAsync` ou uso de flags `--allow-all*` nesta investigação.

Foi executado explicitamente `OfficialRuntimeCreatesStreamsAndDeletesVolatileSession`, em Release, com a CLI local: **1/1 aprovado**, enviando apenas “Responda apenas com a palavra OK.”, sem ferramentas nem anexos. Houve delta de resposta e evento `idle`. O teste foi então ampliado para aplicar também `options.update(includedBuiltinAgents: [])` antes de enviar, como o produto exige, e a segunda execução passou **1/1**. Build da integração: zero avisos/erros. TRXs: `TestResults/CopilotFolderTrust/folder-trust-synthetic-turn.trx` e `folder-trust-restricted-turn.trx`. Diferentemente da etapa anterior sem inferência, este complemento realizou dois turnos sintéticos reais.

Esses resultados da etapa de folder trust não reproduziram a falha nem confirmaram trust como causa. A reprodução posterior está registrada na seção “Reprodução da falha de sessão persistente”. O processo Desktop observado durante este complemento executava o binário em `bin/Debug/net10.0`, enquanto os testes foram realizados em Release; isso não identifica por si só qual build originou as imagens.

Também foi identificado um aviso genérico de escopo na UI que diz que leituras sem workspace pedirão aprovação e serão negadas. Para Copilot, as ferramentas nativas são bloqueadas e as leituras permitidas passam pelo registry; esse texto não deve ser interpretado como diagnóstico de falha de trust. Nenhuma política de permissão foi relaxada. A causa da reprodução de `session.create` está identificada; correlacionar a execução original do usuário ao build e verificar eventual falha remanescente do pacote ainda dependem de captura sanitizada daquela instância.
