# Release e autenticação Copilot — revisão de 01/10/2026

**Auditoria dos arquivos locais — 03/10/2026:** os ZIPs em `artifacts/release/0.11.0/` ainda são do empacotamento anterior: incluem runtime Copilot e proxy MCP, inclusive binários dentro do single-file, e não contêm `THIRD-PARTY-NOTICES.md`. As correções do checkout descritas abaixo não atualizam esses arquivos. A [revisão de licenças](windows-license-review-2026-10-03.md) cobre ambos os RIDs e registra também termos proprietários Windows ML, fontes OFL e avisos faltantes. Reconstrução e reauditoria dos pacotes permanecem pendentes.

## Correção anterior (histórico de 01/10/2026)

O publish single-file com extração nativa incorporava `copilot.exe`, `runtime.node` e DLLs do SDK ao executável principal. O adapter procura o runtime em `AppContext.BaseDirectory/runtimes/<rid>/native`, onde esses arquivos não existiam no pacote. O publish anterior foi reproduzido em `artifacts/release-review/before`; a ausência do executável foi confirmada.

Além disso, a checagem de conta e **Entrar** exigiam uma CLI no PATH. O SDK 1.0.14 distribui um wrapper headless compatível com a CLI 1.0.85, não o executável interativo de login. Uma máquina de desenvolvimento com CLI instalada escondia essa dependência.

O Desktop agora mantém todo o conteúdo `runtimes/` fora do single-file e recusa publicar sem wrapper, `runtime.node` e CLI interativa. `eng/CopilotAccountCli.targets` baixa a CLI standalone oficial da mesma versão fornecida pelo SDK, confere o SHA-256 do release oficial e a inclui em `runtimes/<rid>/copilot-cli/`. **Entrar/Sair** usam essa CLI; conta e sessões usam o runtime headless, com a mesma identidade oficial em `~/.copilot`. O produto não lê credenciais, recebe tokens ou implementa OAuth próprio. Tokens e overrides BYOK do processo continuam excluídos do ambiente filho; `COPILOT_HOME` é definido pelo produto para o diretório oficial, sem herdar um override externo.

O download dessas dependências acontece no build. Não há instalação nem download de CLI no startup. Falta de runtime impede a publicação; a checagem de conta conserva os estados de falha existentes. Cancelar a espera do terminal não encerra o login nem afirma logout.

## Origem de atualização

A única origem de releases do aplicativo é [esilva-silva/EsilvaSoft.KapibaraStudio/releases](https://github.com/esilva-silva/EsilvaSoft.KapibaraStudio/releases). A consulta usa a API pública desse mesmo repositório. O fallback para `EsilvaSoft.SlopStudio` foi removido, inclusive em falhas de rede, rate limit, JSON inválido, timeout ou falta de pacote compatível. O workflow publica somente quando o evento de tag pertence ao repositório canônico. Nomes legados de executável/pacote permanecem aceitos para preservar instalações e atalhos existentes; isso não consulta outro repositório.

Naquela revisão, downloads oficiais de dependências Copilot vinham de `github/copilot-cli`; não eram uma origem alternativa para atualização do KapibaraStudio. A verificação SHA-256, staging, cancelamento, restauração e aplicação no fechamento pertencem ao fluxo de atualização do app e permanecem. A política atual de distribuição foi substituída pela atualização de 02/10 abaixo.

Se a substituição dos arquivos falhar ao fechar, o processo inicia novamente a cópia antiga para manter o app acessível e grava a falha junto ao update pendente. Na próxima abertura, a barra identifica o estado como **Tentar instalar** e o tooltip informa o erro salvo; o próximo reinício tenta aplicar o pacote novamente. Isso distingue claramente a tentativa de instalação malsucedida de uma atualização concluída.

## Evidências históricas e limites — pacote de 01/10/2026

- Restore locked e build Release: aprovados, zero avisos/erros.
- Publish Windows x64 real: wrapper e `runtime.node` externos, CLI interativa standalone presente; `--version` e `login --help` executados sem autenticação ou inferência.
- `CopilotPublishedRuntimeTests`: **2/2 aprovados**, executando o runtime da pasta publicada com PATH vazio. O modo Empty inicia sem credenciais; clientes de conta e sessão reconhecem `AuthType=user` e listam modelos elegíveis da conta já autenticada. Sem prompt, sessão de conversa ou leitura de tokens.
- Regressão focada de auto update/descoberta/configuração e persistência de sessão: **39 aprovados, 1 ignorado** (modo Unix exige Linux).
- Suíte integral Release: **4.465 aprovados, 2 falhas, 24 ignorados**. As falhas foram cleanup de staging em `VerifiedZipIsStagedAndAppliedByRenameWhileTheExecutableIsOpen` e arquivo temporário LiteDB em uso no teardown de `UnknownVersionOrInvalidEncodingCannotBeOverwritten(3,false)`; ambos passaram na reexecução focada. A suíte integral não foi verde e nenhuma asserção foi relaxada.
- Reexecução integral de **IntegrationTests**, após finalizar o empacotamento: **885 aprovados, 0 falhas, 4 ignorados** (889 total, 2m47s), em `TestResults/ReleaseCopilotFinalIntegration`. Somada às suítes já aprovadas na execução anterior (UnitTests 3.343/20 ignorados, Agents 198, Benchmarks 41), a evidência final cobre **4.467 aprovados e 24 ignorados**, em execuções separadas. As duas falhas de cleanup não se repetiram; isso não altera o resultado registrado da primeira execução.
- TRXs: `TestResults/ReleaseCopilotReview`, `TestResults/ReleaseCopilotFocused` e `TestResults/PublishedCopilot`.
- ZIP final Windows x64 extraído: verificação estrutural/CLI aprovada e os mesmos testes de conta/session/Empty passaram **2/2** em `TestResults/ExtractedReleaseCopilot`, com PATH vazio. Os quatro hashes foram recalculados; ZIPs contêm wrapper, `runtime.node` e CLI; tar.gz Linux x64/ARM64 contêm aplicativo, proxy MCP e duas CLIs com bit de execução.

O script `build-release.ps1 0.11.0 -SkipTests` gerou os quatro pacotes locais e `SHA256SUMS.txt` em `artifacts/release/0.11.0`; a suíte foi executada separadamente. Hashes e layouts dos arquivos finais são conferidos nesta revisão. Cross-compilação não comprova execução ARM64/Linux. Login OAuth novo com aprovação no navegador, clique em **Entrar/Sair**, janela nativa e leitor de tela ainda exigem homologação humana; reconhecer a conta existente não comprova esses passos. Nenhuma release foi publicada nesta revisão.

## Reproduzir a revisão histórica de 01/10

```powershell
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -c Release
dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore -c Release
./build-release.ps1 0.11.0
./eng/Test-ReleasePackage.ps1 -PublishDirectory artifacts/publish/win-x64 -Rid win-x64
$env:KAPIBARA_RELEASE_TEST_DIRECTORY = (Resolve-Path artifacts/publish/win-x64).Path
```

Os testes de publish acima pertenciam ao bundle de 01/10 e foram removidos junto ao runtime redistribuído. Não use esses resultados para validar o pacote atual. Para ambientes que bloqueiam telemetria de build Avalonia, `-p:UsedAvaloniaProducts=` continua disponível.

Fontes oficiais: [targets do SDK .NET](https://github.com/github/copilot-sdk/blob/main/dotnet/src/build/GitHub.Copilot.SDK.targets), [autenticação da CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/authenticate-copilot-cli). O contrato efetivo foi conferido também nos targets locais do pacote NuGet fixado, sem atualizar dependências.

## Atualização de distribuição — 02/10/2026

A partir desta revisão, o KapibaraStudio **não baixa nem redistribui a CLI/runtime do GitHub Copilot**. Login, consulta de conta/modelos e sessões do SDK usam a instalação nativa oficial do usuário, descoberta como caminho absoluto no `PATH`. Isso remove do pacote o binário standalone e seus termos de redistribuição. Em Windows, instalar pelo WinGet; em Linux, usar o método/binário nativo oficial. Shims `.cmd` de instalações npm no Windows não são aceitos.

O SDK .NET 1.0.14 permite `RuntimeConnection.ForStdio(path: ...)` apontando à CLI instalada e faz handshake/validação de protocolo em `StartAsync`; instalação incompatível falha visivelmente e não troca silenciosamente para outra CLI/runtime. `CopilotSkipCliDownload=true` impede aquisição pelo build. O adapter mantém autenticação da conta oficial, `COPILOT_HOME` explícito, ambiente filho filtrado e não lê tokens/credenciais. O baseline que o SDK 1.0.14 usava para download era 1.0.85; isso não declara a versão mínima da instalação externa.

O release agora inclui `THIRD-PARTY-NOTICES.md` com a licença MIT do SDK; o validador exige o arquivo e confirma a atribuição. O publish Windows x64 desta revisão passou e não criou diretório `runtimes/`; o smoke do pacote confirmou aviso presente e ausência de executáveis Copilot.

Build Debug/Release passaram sem avisos/erros. Os testes direcionados passaram: agentes Copilot **23/23**, descoberta/sessões/auto update **36 aprovados, 1 ignorado**, localização **30/30** e contas **22/22**. O teste manual explicitamente selecionado `SessionRuntimeReportsOfficialAuthenticationAcrossModes` passou **1/1** com a CLI oficial local 1.0.89 no `PATH`; ele iniciou clientes de conta e sessão e comparou estado de autenticação sem prompt/modelos. A primeira entrada WinGet do `PATH` não iniciou neste sandbox; o teste usou a outra instalação nativa acessível. A autenticação efetiva desta conta, os modelos e inferência não foram validados. Linux/ARM64 também ficam pendentes. Consulte [a lista de ações jurídicas e de dados](../phases/phase-07-v0.11.0/guia-termos-github-copilot.md).

## Configuração do executável — revisão de 03/10/2026

`WorkspacePreferences.CopilotCliExecutablePath` agora guarda opcionalmente o executável absoluto oficial. O singleton Application `ICopilotCliConfiguration` aplica a mesma resolução a checagem de conta, login e sessões. O caminho explicitamente salvo tem prioridade; se estiver ausente ou inválido, o usuário recebe falha sem fallback. Sem caminho salvo, a descoberta consulta a instalação no perfil local e depois o `PATH`; **Usar detecção automática** limpa o override.

As configurações mostram o caminho detectado sem iniciar a CLI e oferecem **Caminho da CLI**, **Salvar caminho** e **Usar detecção automática**. Mudanças se aplicam às próximas operações; sessões existentes conservam o cliente já criado. A configuração não altera a política de distribuição: a CLI segue instalada pelo usuário, sem download, redistribuição ou leitura de credenciais pelo produto.

Restore locked e build Release passaram sem avisos ou erros. Os focos de descoberta, persistência e UI passaram **18/18**. Na execução inicial da suíte Release, UnitTests teve **3.374 aprovados e 20 ignorados**, Infrastructure.Agents.Tests **284 aprovados, 0 falhas**, e IntegrationTests **906 aprovados, 2 falhas e 5 ignorados**. Uma falha era a nova guarda de fonte no teste unitário, que usava `Path.GetTempPath`; foi corrigida para caminhos sintéticos. A outra ocorreu no teardown `LiteDbSchemaLearning ACorruptDocument...` porque `workspace-log.db.tmp` ainda estava em uso; o caso passou na reexecução sem alteração de teste de produção.

A reexecução focada final em `TestResults/CopilotCliPathCorrected` passou **5/5 UnitTests** e **28/28 IntegrationTests** (33 testes focados no total), cobrindo as duas falhas iniciais e os cenários host/UI/descoberta/sessão. Isso não foi uma nova execução integral da suíte. O teste manual explícito `TestResults/CopilotCliPath/official-local-cli.trx` passou **3/3** com a CLI local detectada e autenticada: conta, modelos e sessão com as restrições verificadas, sem prompts. Isso não valida inferência ou UI nativa.

O publish single-file self-contained Windows x64 foi gerado em `artifacts/copilot-cli-path/win-x64` e aprovado pelo `Test-ReleasePackage.ps1`. PNGs reais claro/escuro de 660×560 e 520×420 foram inspecionados; campo e ações permanecem acessíveis por rolagem. Não houve execução da UI nativa. Linux/ARM64, inferência e homologação nativa seguem fora desta evidência.

### Falha do SessionFs persistente — revisão de 03/10/2026

A reprodução reportada pela equipe falhou em `session.create` com RPC `-32603` e `Caminho SessionFs fora da raiz virtual.` A configuração anterior combinava `SessionStatePath = "session-state"`, convenções Windows e um working directory do host; a expansão do caminho relativo produzia uma raiz de unidade que o mapper persistente rejeita. O contrato corrigido usa convenções POSIX e `/session-state` absoluto no namespace virtual. O SDK também exige working directory absoluto POSIX nessa convenção: a raiz virtual será `/workspace`, enquanto `CopilotClient.WorkingDirectory` continua sendo o diretório real capturado para o processo da CLI. Assim, caminhos virtuais do SessionFs não dependem da unidade Windows e não alteram o diretório de trabalho autorizado do processo.

O mapper mantém a normalização restritiva, contenção na raiz e permissões existentes. O diagnóstico agora identifica a etapa sanitizada que falhou; o ID reservado expressa a identidade pretendida e não prova que a sessão nativa foi criada. A mensagem/exceção privada do SDK não é exposta. Em execuções Release separadas: o teste oficial persist/stream/resume/erase passou **1/1** (`TestResults/CopilotSessionFailure/persistent-official-posix.trx`), o adapter reservado de produção passou **1/1** (`reserved-production-posix.trx`), filtro storage/runtime/discovery **25/25**, Agents **285/285**, UnitTests **3.374 aprovados/20 ignorados** e IntegrationTests **909 aprovados/5 ignorados**, sem falhas. Publish single-file self-contained win-x64 em `artifacts/copilot-session-fix/win-x64` passou `Test-ReleasePackage.ps1`. A UI nativa não foi reexecutada nem homologada; os resultados das suítes são execuções separadas, não uma execução única. Veja a [investigação Copilot](copilot-release-investigation-2026-10-03.md#reprodução-da-falha-de-sessão-persistente--03102026).
