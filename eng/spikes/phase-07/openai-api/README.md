# Spike isolado: OpenAI Chat Completions via SDK .NET

Este spike verifica se o SDK oficial `OpenAI` 2.14.0 oferece, por uma superfície estável, streaming de texto e function calling suficientes para o contrato inicial do SlopStudio. A API Responses foi removida deste spike porque seus tipos são anotados `[Experimental("OPENAI001")]` no SDK. O cliente `OpenAI.Chat.ChatClient`, `CompleteChatStreamingAsync`, `ChatTool.CreateFunctionTool` e os tipos de chamada incremental são usados sem suprimir diagnósticos; `TreatWarningsAsErrors=true` faz a compilação reprovar qualquer aviso.

O SDK documenta streaming assíncrono via `await foreach` e a coleta dos fragmentos de chamadas de função, seguida de validação dos argumentos antes da execução. O exemplo oficial de streaming/function calling demonstra os mesmos tipos do SDK usados aqui: [exemplo Chat, SDK 2.14.0](https://github.com/openai/openai-dotnet/blob/OpenAI_2.14.0/examples/Chat/Example04_FunctionCallingStreamingAsync.cs), [README oficial do SDK](https://github.com/openai/openai-dotnet/tree/OpenAI_2.14.0) e [changelog 2.14.0](https://github.com/openai/openai-dotnet/blob/OpenAI_2.14.0/CHANGELOG.md). Isso confirma adequação de contrato do SDK; não homologa conta, modelo, disponibilidade, cobrança ou compatibilidade de provider do produto.

## Escopo de execução

`Tests/fixtures/*.sse` fornece fluxos sintéticos de Chat Completions com texto UTF-8 fragmentado e argumentos de função enviados em múltiplos deltas. `OfflineHandler` responde apenas a `https://fixture.invalid/v1/chat/completions`, não delega a outro handler e falha para qualquer destino inesperado. Os testes não fazem chamadas de rede nem leem credenciais do ambiente. Para exercitar o construtor estável autenticado do SDK, o teste passa o token fictício `fixture-placeholder-not-a-credential`, usado apenas para compor o header Authorization requerido pelo construtor. O handler offline verifica o valor exato em memória: ele não sai do processo nem pode alcançar a rede.

O contrato permite apenas `fixture_echo`, usa schema fechado, acumula os deltas até o terminal `tool_calls`, rejeita função/argumentos fora da allowlist e nunca executa uma função real. O resultado sintético é devolvido em uma mensagem `tool` apenas para confirmar a associação do mesmo `tool_call_id` no turno seguinte. A fixture de cancelamento verifica cancelamento por chamada sem interferência em uma segunda sessão.

## Executar

Em PowerShell 7, a partir deste diretório:

```powershell
$isolatedAppData = Join-Path $PWD '.cache/isolated-appdata'
New-Item -ItemType Directory -Path $isolatedAppData -Force | Out-Null
$env:APPDATA = $isolatedAppData
$env:NUGET_CLI_HOME = Join-Path $PWD '.cache/nuget-cli-home'
dotnet restore OpenAiApiSpike.slnx --locked-mode --configfile NuGet.Config -p:NuGetAudit=false
dotnet build OpenAiApiSpike.slnx --no-restore
dotnet test OpenAiApiSpike.slnx --no-build --no-restore
pwsh -NoProfile -File Inventory-Packages.ps1
```

O restore usa lockfiles separados por projeto e um cache local `.cache/packages`. O redirecionamento de `APPDATA` serve para hosts em que o sandbox não permite ler o NuGet.Config global do usuário; ele não altera esse arquivo. `NuGetAudit=false` desativa somente a consulta de avisos de vulnerabilidade durante esse restore offline. Uma tentativa separada de `dotnet list package --vulnerable --include-transitive` com configuração/cache isolados não concluiu porque o feed NuGet falhou por TLS/credenciais. Portanto, não há auditoria de vulnerabilidades aprovada. O `NuGet.Config` fixa `https://data.nuget.org/v3/index.json` como origem exclusiva de audit, separada do feed de pacotes, para permitir auditoria em CI quando download de pacotes não for permitido pela rede. Com as dependências já no cache, a validação restaurou em locked mode sem chamadas externas. O inventário é gerado localmente por `Inventory-Packages.ps1`; inclui dependências diretas/transitivas dos projetos com target `net10.0`, a licença declarada no nuspec, `contentHash` NuGet e SHA-256 do arquivo `.nupkg`. O CSV versionado deve ser regenerado depois de atualizar os lockfiles. Os 24 pacotes atuais declaram MIT; isso não cobre termos dos serviços OpenAI nem autoriza distribuição de componentes que não estão nesses lockfiles.

Em runner conectado, repetir a auditoria sem desligá-la e incluir transitivas:

```powershell
dotnet restore eng/spikes/phase-07/openai-api/OpenAiApiSpike.slnx `
  --locked-mode `
  --configfile eng/spikes/phase-07/openai-api/NuGet.Config `
  -p:NuGetAudit=true `
  -p:NuGetAuditMode=all

dotnet list eng/spikes/phase-07/openai-api/OpenAiApiSpike.slnx package `
  --vulnerable --include-transitive --no-restore
```

O resultado cobre advisories publicados pelo feed na data consultada; não homologa provider, modelo, autenticação ou política de dados. Referências: [auditoria de pacotes NuGet](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages) e [comando dotnet package list](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-package-list).

## Resultado e limites

O Chat Completions no SDK 2.14.0 fornece as duas capacidades necessárias sem usar a API Responses experimental. A evidência deste spike é um contrato local com fixtures determinísticas. Ela não prova o comportamento de rede real, schema aceito por modelos, autenticação oficial no produto, políticas de retenção, limites/custo, suporte de recursos por modelo, nem autorização para enviar dados. Qualquer chamada paga e homologação do provider continuam gates separados da Fase 7.
