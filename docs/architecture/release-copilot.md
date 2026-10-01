# Release e autenticação Copilot — revisão de 01/10/2026

## Causa e correção

O publish single-file com extração nativa incorporava `copilot.exe`, `runtime.node` e DLLs do SDK ao executável principal. O adapter procura o runtime em `AppContext.BaseDirectory/runtimes/<rid>/native`, onde esses arquivos não existiam no pacote. O publish anterior foi reproduzido em `artifacts/release-review/before`; a ausência do executável foi confirmada.

Além disso, a checagem de conta e **Entrar** exigiam uma CLI no PATH. O SDK 1.0.14 distribui um wrapper headless compatível com a CLI 1.0.85, não o executável interativo de login. Uma máquina de desenvolvimento com CLI instalada escondia essa dependência.

O Desktop agora mantém todo o conteúdo `runtimes/` fora do single-file e recusa publicar sem wrapper, `runtime.node` e CLI interativa. `eng/CopilotAccountCli.targets` baixa a CLI standalone oficial da mesma versão fornecida pelo SDK, confere o SHA-256 do release oficial e a inclui em `runtimes/<rid>/copilot-cli/`. **Entrar/Sair** usam essa CLI; conta e sessões usam o runtime headless, com a mesma identidade oficial em `~/.copilot`. O produto não lê credenciais, recebe tokens ou implementa OAuth próprio. Tokens e overrides BYOK do processo continuam excluídos do ambiente filho; `COPILOT_HOME` é definido pelo produto para o diretório oficial, sem herdar um override externo.

O download dessas dependências acontece no build. Não há instalação nem download de CLI no startup. Falta de runtime impede a publicação; a checagem de conta conserva os estados de falha existentes. Cancelar a espera do terminal não encerra o login nem afirma logout.

## Origem de atualização

A única origem de releases do aplicativo é [esilva-silva/EsilvaSoft.KapibaraStudio/releases](https://github.com/esilva-silva/EsilvaSoft.KapibaraStudio/releases). A consulta usa a API pública desse mesmo repositório. O fallback para `EsilvaSoft.SlopStudio` foi removido, inclusive em falhas de rede, rate limit, JSON inválido, timeout ou falta de pacote compatível. O workflow publica somente quando o evento de tag pertence ao repositório canônico. Nomes legados de executável/pacote permanecem aceitos para preservar instalações e atalhos existentes; isso não consulta outro repositório.

Os downloads oficiais de dependências Copilot no build continuam vindo de `github/copilot-cli`; não são uma origem alternativa para atualização do KapibaraStudio. Verificação SHA-256, staging, cancelamento, restauração de arquivos na falha e aplicação no fechamento permanecem. A aplicação Linux preserva os modos Unix dos arquivos filhos; o tar local marca executáveis Copilot/MCP como executáveis também quando é produzido no Windows.

## Evidências e limites

- Restore locked e build Release: aprovados, zero avisos/erros.
- Publish Windows x64 real: wrapper e `runtime.node` externos, CLI interativa standalone presente; `--version` e `login --help` executados sem autenticação ou inferência.
- `CopilotPublishedRuntimeTests`: **2/2 aprovados**, executando o runtime da pasta publicada com PATH vazio. O modo Empty inicia sem credenciais; clientes de conta e sessão reconhecem `AuthType=user` e listam modelos elegíveis da conta já autenticada. Sem prompt, sessão de conversa ou leitura de tokens.
- Regressão focada de auto update/descoberta/configuração e persistência de sessão: **39 aprovados, 1 ignorado** (modo Unix exige Linux).
- Suíte integral Release: **4.465 aprovados, 2 falhas, 24 ignorados**. As falhas foram cleanup de staging em `VerifiedZipIsStagedAndAppliedByRenameWhileTheExecutableIsOpen` e arquivo temporário LiteDB em uso no teardown de `UnknownVersionOrInvalidEncodingCannotBeOverwritten(3,false)`; ambos passaram na reexecução focada. A suíte integral não foi verde e nenhuma asserção foi relaxada.
- Reexecução integral de **IntegrationTests**, após finalizar o empacotamento: **885 aprovados, 0 falhas, 4 ignorados** (889 total, 2m47s), em `TestResults/ReleaseCopilotFinalIntegration`. Somada às suítes já aprovadas na execução anterior (UnitTests 3.343/20 ignorados, Agents 198, Benchmarks 41), a evidência final cobre **4.467 aprovados e 24 ignorados**, em execuções separadas. As duas falhas de cleanup não se repetiram; isso não altera o resultado registrado da primeira execução.
- TRXs: `TestResults/ReleaseCopilotReview`, `TestResults/ReleaseCopilotFocused` e `TestResults/PublishedCopilot`.
- ZIP final Windows x64 extraído: verificação estrutural/CLI aprovada e os mesmos testes de conta/session/Empty passaram **2/2** em `TestResults/ExtractedReleaseCopilot`, com PATH vazio. Os quatro hashes foram recalculados; ZIPs contêm wrapper, `runtime.node` e CLI; tar.gz Linux x64/ARM64 contêm aplicativo, proxy MCP e duas CLIs com bit de execução.

O script `build-release.ps1 0.11.0 -SkipTests` gerou os quatro pacotes locais e `SHA256SUMS.txt` em `artifacts/release/0.11.0`; a suíte foi executada separadamente. Hashes e layouts dos arquivos finais são conferidos nesta revisão. Cross-compilação não comprova execução ARM64/Linux. Login OAuth novo com aprovação no navegador, clique em **Entrar/Sair**, janela nativa e leitor de tela ainda exigem homologação humana; reconhecer a conta existente não comprova esses passos. Nenhuma release foi publicada nesta revisão.

## Reproduzir

```powershell
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -c Release
dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore -c Release
./build-release.ps1 0.11.0
./eng/Test-ReleasePackage.ps1 -PublishDirectory artifacts/publish/win-x64 -Rid win-x64
$env:KAPIBARA_RELEASE_TEST_DIRECTORY = (Resolve-Path artifacts/publish/win-x64).Path
dotnet test tests/EsilvaSoft.KapibaraStudio.IntegrationTests/EsilvaSoft.KapibaraStudio.IntegrationTests.csproj --no-build --no-restore -c Release --filter FullyQualifiedName~CopilotPublishedRuntimeTests
```

Os testes de publish são explícitos: o segundo requer uma conta oficial já autenticada no Windows; não inicia login. A verificação estrutural e de ajuda/versão é compartilhada pelo script local e pelo CI. Para ambientes que bloqueiam telemetria de build Avalonia, a opção documentada `-p:UsedAvaloniaProducts=` continua disponível.

Fontes oficiais: [targets do SDK .NET](https://github.com/github/copilot-sdk/blob/main/dotnet/src/build/GitHub.Copilot.SDK.targets), [autenticação da CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/authenticate-copilot-cli). O contrato efetivo foi conferido também nos targets locais do pacote NuGet fixado, sem atualizar dependências.
