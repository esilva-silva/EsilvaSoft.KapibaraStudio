# Task-R 1 — levantamento L1 do profiler e administração

O levantamento L1 abaixo registra a situação anterior ao corte, em 06/11/2026. O resultado observado de L3 está separado ao final; o levantamento isolado não comprova implementação ou homologação.

## Dados existentes e recuperação

`Infrastructure.System/LiteDbConnectionProfileRepository.ProfilerCapture.cs` usa a coleção LiteDB `profilerCaptureRecoveryV1`, por meio do proprietário já registrado em DI. Seus registros são **tickets de recuperação**, com conexão, banco, identidade do nó, período, configurações anterior e aplicada e estado; não são páginas de resultados capturados. `MongoServerAdministrator.ReadProfilerCaptureAsync` lê as entradas de `system.profile` sob demanda. Não presumir que a coleção local contenha essas páginas nem excluir seus registros como efeito da limpeza.

O repositório valida a versão e o conteúdo, recusa mais de 100 registros e comunica leitura inválida; não deve substituir dados ilegíveis por uma lista vazia. `ProfilerCaptureCoordinator` grava o ticket antes de alterar o servidor. Sua restauração exige ação humana, perfil correspondente e confirmação exata do banco; confere identidade do nó e estado atual antes de qualquer alteração, recusa conflito e só conclui o ticket quando a configuração anterior está confirmada. Falha ou cancelamento após envio podem deixar o estado incerto e precisam permanecer visíveis.

### Estratégia aprovada para L3

- Preservar a coleção e a leitura versionada no proprietário LiteDB único, sem migração destrutiva nem descarte silencioso de tickets; exibir falha de leitura e preservar registros não reconhecidos.
- Manter acesso explícito aos tickets pendentes e à restauração humana, com identificação suficiente da origem e confirmação do banco. Abrir sessão ou listar tickets não deve consultar ou alterar o servidor automaticamente.
- Manter temporariamente apenas a leitura de status e a configuração de profiler estritamente necessárias à restauração protegida de tickets existentes. Não oferecer início de captura, coleta ou configuração genérica como funcionalidades disponíveis. Remover os caminhos exclusivos correspondentes após separar os métodos de compatibilidade.
- Preservar as verificações de perfil, nó, configuração concorrente e releitura; não afirmar sucesso ou rollback quando o efeito for incerto. A conclusão de um ticket após restauração confirmada segue sendo uma ação explícita do fluxo de recuperação, não uma limpeza automática.
- Cobrir ticket pendente e restauração, falhas, corrupção/versão desconhecida e ausência de alteração implícita do servidor; manter os limites de evidência para MongoDB real e interface nativa.

## Mapa de consumidores para L3

| Camada | Caminhos e classificação inicial |
| --- | --- |
| UI | `Desktop/WorkspaceToolsWindow.axaml`, `ViewModels/MainWindowViewModel.ServerAdmin.cs`, `.SensitiveAdministration.cs`, `.RuntimeParameters.cs`, `.CustomRoles.cs`, `.ProfilerConfiguration.cs` e `.ProfilerCapture.cs`: separar recuperação explícita dos comandos, formulários e bindings removidos. `LocalizationViewModel.cs` contém textos a revisar por consumidor. |
| UI compartilhada | `MainWindowViewModel.DatabaseAdmin.cs`, `.CollectionAdmin.cs` e `.CollectionMaintenance.cs` incluem operações cotidianas; separar cada ação, preservando bancos, coleções, índices, views e validadores. |
| Aplicação | `WorkspaceService.cs`, `IMongoWorkspaceService.cs`, `IProfilerCaptureRepository.cs` e `ProfilerCaptureCoordinator.cs`: remover a cadeia exclusiva de início/coleta e administração pesada; preservar o recorte de recuperação até que tickets antigos possam ser tratados. |
| Infraestrutura | `MongoWorkspaceService.cs` encaminha métodos a `MongoServerAdministrator.cs`; separar os métodos antes de removê-los. `GetTopologyAsync` também é usado por `ExplorerMetadataService` e `MongoCollectionMaintenancePreflight`, portanto permanece. `ServiceCollectionExtensions.cs` registra o repositório no proprietário LiteDB. |
| Testes | Reexaminar `LiteDbProfilerCaptureRecoveryTests`, `LocalMongoProfilerCaptureCrashIntegrationTests`, `PhaseSixAdminHardeningTests`, `MongoServerAdministratorRuntimeParameterTests` e `MongoServerAdministratorKillOperationTests`; preservar segurança e recuperação aplicáveis e retirar apenas testes de comportamento efetivamente removido. |

As referências de testes, ferramentas, projetos, pacotes, auditoria e localização devem ser verificadas novamente na implementação. EnvironmentVault, secret stores, providers existentes e transferência lógica permanecem fora do corte. Diagnóstico e metadados necessários às jornadas mantidas também permanecem; tools de agentes continuam limitadas ao contrato de segurança vigente.

## Resultado de L3 — 06/11/2026

Snapshot de trabalho sobre `8361f73`, com alterações locais de L2 e L3, em Windows. Removidos os fluxos e contratos exclusivos de `killOp`, `validate`/`compact`, usuários e papéis e parâmetros runtime, além do início, coleta e configuração genérica do profiler. As implementações e DTOs exclusivos correspondentes foram retirados; os testes exclusivos desses comportamentos foram removidos ou adaptados ao comportamento preservado. A localização residual sem consumidor será revista em L5.

Permanecem diagnóstico de servidor, topologia e operações correntes usados pelo Explorer e por índices; bancos, coleções, índices, views, validadores, transferência lógica, confirmações e auditoria. O recorte de recuperação conserva tickets versionados no mesmo proprietário LiteDB e requer ação humana, conexão correspondente e confirmação do banco. O adaptador recebe o ticket e reconstrói somente a configuração anterior; verifica estado e identidade do processo, inclusive na releitura imediatamente anterior ao comando. Há ainda uma janela entre os comandos de rede: a verificação não é uma transação atômica.

A listagem local dos tickets não consulta nem altera o MongoDB ao abrir a sessão; a restauração exige ação explícita. Falhas de leitura, inclusive corrupção e versão incompatível, são visíveis e preservam os registros. Após envio, falha ou cancelamento mantém aviso de efeito incerto; o ticket só é concluído depois de confirmar o estado anterior. O teste de crash continua preparado por fixture descartável, sem reintroduzir início de captura no produto.

### Verificação registrada

- Build final `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -p:UsedAvaloniaProducts= -p:UseSharedCompilation=false -m:1 -nr:false`: aprovado, zero avisos e zero erros; analisadores permaneceram ativos.
- Testes focados: `dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore --filter 'FullyQualifiedName~Profiler|FullyQualifiedName~CollectionMaintenance|FullyQualifiedName~MainWindowViewModelActivity|FullyQualifiedName~Indexes|FullyQualifiedName~DatabaseExplorerUiTests|FullyQualifiedName~UnitSystemBoundaryIntegrationTests' --logger 'trx;LogFilePrefix=task-r1-l3-final'`. Foram 50 unitários e 16 de integração aprovados; dois cenários opt-in foram ignorados e permanecem pendentes. O filtro não executou testes da suíte de agentes.
- Renderização Headless: PNGs da recuperação inspecionados em claro e escuro, com ticket sintético e identificação de perfil, banco e estado legíveis. Artefatos no diretório `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/bin/Debug/net10.0/ui-evidence/render-27bdb5df3d284811a65176a9d4b4b401/`: `tools-admin-recovery-Light-scrolled.png` e `tools-admin-recovery-Dark.png`. O estado aparece como `Uncertain` em inglês; localizar a apresentação fica pendente em L5. A fixture não injeta repositório e mostra o aviso de indisponibilidade; isso não é evidência de uma leitura bem-sucedida do proprietário LiteDB na interface.
- O teste da guarda de identidade cobre a rejeição de nó alterado, mas não demonstra diretamente ausência de envio por um cliente Mongo falso. Os cenários reais de crash/restauração dependem de MongoDB descartável opt-in; Headless não homologa MongoDB real, diálogo nativo, teclado ou leitor de tela.
