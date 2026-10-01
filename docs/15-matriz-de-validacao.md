# Matriz de validação

**Revisão P7-UX após uso — 30/09/2026:** seletor direto de provider com lista única limitada, nomes sem repetição e chips menores; contornos removidos dos detalhes comuns, preservando destaque de bloqueios. Build em saída isolada (aplicativo aberto): 0 avisos/erros; 22/22 testes UI/hosting/conta aprovados e PNGs da lista aberta inspecionados. [Evidências e limites](phases/phase-07-v0.11.0/meta-ui-ux-painel-agente-ia.md#revisão-após-captura-do-usuário--30092026).

**P7-UX — evidência automatizada em 30/09/2026:** [P7-UX — painel Agente IA](phases/phase-07-v0.11.0/meta-ui-ux-painel-agente-ia.md). Restore locked, build 0 avisos/erros, 4.466 testes aprovados e 23 ignorados; TRXs por assembly em `TestResults/P7UX/Final`. Matriz Avalonia de 144 PNGs cobre dois tipos de provider, quatro idiomas, dois temas, três larguras e três escalas; fixtures adicionais cobrem foco/rolagem, contexto expandido, falha de persistência durante streaming e eventos na origem. Amostras reais inspecionadas. Não comprova leitor de tela, IME, diálogos nativos ou serviços reais em Windows/Linux.

**P7-AUTH — implementada com doubles em 30/09/2026:** [diagnóstico e aceite](phases/phase-07-v0.11.0/meta-inicializacao-autenticacao-agentes.md). Restore locked e build solução passaram; build teve 0 avisos/erros. UnitTests: **3.340 aprovados, 20 ignorados**; dispatcher/composição **19/19** focados; filtro de conta, painel e guarda de arquitetura **10/10**. A suíte completa de IntegrationTests teve 866 aprovados, 2 falhas de remoção de diretório temporário em fixtures de migração/LiteDB e 14 ignorados; os dois testes de cleanup e a guarda passaram isoladamente **3/3**. Ainda pendem CLI/cofre reais em cada SO e confirmação do startup Copilot em ambiente de usuário. A evidência de “Testar conexão” anterior não é tratada como prova manual do novo gatilho.

| Verificação P7-AUTH | Evidência | Limite |
| --- | --- | --- |
| Contratos e roteamento por provider | `IAgentAccountManager`/`IAgentAccountHandler` em Application; handlers DI e testes de dispatcher/composição 19/19 | Não valida CLI/cofre oficial; arquitetura impede referência de adapters em ViewModels |
| Startup/retry/Testar conexão/login/logout | UnitTests completos 3340/20 ignorados; 19 focados em arquitetura/dispatcher/composição | Doubles em memória; login continua explícito e logout global exige confirmação |
| Conta inelegível para checagem sem diálogo | Teste garante ausência de chamada de conta e refresh de catálogo automáticos; retry explícito executa | Dialog/keyring real depende de plataforma |
| Concorrência, timeout, resposta tardia e recuperação | Testes focados cobrem single-flight, refresh pós-comando, timeout, clearing e publicação protegida por geração | Operações de runtime real ainda precisam ser homologadas |
| Modelo e conversa restaurados | Testes confirmam preservação do modelo pendente e sem fallback destrutivo | Catálogo/modelos reais dependem de autenticação oficial |
| Encerramento | Teste valida cancelamento e espera bounded antes de descartar providers | Providers que ignorem cancelamento além do limite ainda não têm teste nativo separado |
| Erro tipado de autenticação em turno | Revalidação é acionada sem replay do turno/tools | Não foi validado com stream oficial autenticado |

## Adapters do SO — incremento de 30/09/2026

| Verificação | Evidência | Limite |
| --- | --- | --- |
| Restore travado | CPU Windows: passou em `--locked-mode` usando cache local e `NuGet.Config` temporário | Restore offline; auditoria online do NuGet e execução Linux pendentes |
| Build solução | CPU Windows: zero avisos/erros | `UsedAvaloniaProducts=` evita telemetria externa |
| UnitTests após migrações | 3323 aprovados, 20 ignorados | Execução Windows; recursos reais movidos para integração |
| Infrastructure.Agents.Tests | 198/198 aprovados | Doubles/providers; não homologa SDKs ou CLIs oficiais |
| Benchmarks unitários | 41/41 aprovados com agregação e ambiente sintético | BenchmarkDotNet não é varrido como fonte de teste; runner ONNX real foi movido para integração explícita |
| IntegrationTests completa | 868 aprovados, 14 ignorados, 882 total | Testes `Explicit` não são executados por padrão; WSL sem distribuição Linux |
| `dotnet test` da solução completa | Benchmarks 41/41; UnitTests 3323/20 ignorados; Agents.Tests 198/198; IntegrationTests 868/14 ignorados | Windows; homologações reais externas/ONNX explícitas não executadas |
| Build cruzado Linux | Desktop e McpServer com `-r linux-x64`: zero avisos/erros | Compilação cruzada Windows; runtime e testes Linux não executados |
| Guarda e inventário de fronteiras | Guarda passou em UnitTests, Agents.Tests, TestSupport e fontes `*Tests.cs` de Benchmarks; três ocorrências em memória/handler falso revisadas | Regex e execução local não provam ausência de I/O por toda dependência transitiva |
| Regras de workspace e sessão com doubles | 118/118 + 13/13 casos distintos | Seleção focalizada; não prova isolamento da suíte inteira |
| Adapters de arquivos e armazenamento de sessão | Integração 57/57 | Recursos sintéticos locais; não homologa cofre/CLI/MongoDB real |
| Protocolo/conta/provider Codex com doubles | 38/38 Windows; STDIO em memória, pasta simulada | Não homologa a conta ou o CLI oficial |
| Processo Codex, ambiente, home privado e descendentes | Integração 11/11 Windows com servidor falso local | Requer ACL fora do sandbox; Linux pendente |
| Suíte separada de integração após extração Codex | 68/68 Windows, sem skips | Inclui os 57 casos de armazenamento e os 11 de Codex; não somar novamente |
| Copilot com doubles + regressão Codex | 80/80 Windows | Inclui os 69 anteriores e 11 novos de provider/composição; SDK não iniciado |
| Ponte Copilot→runtime→registry→Desktop | 6/6 Windows em memória | Proposta e resultados de confirmação; serialização SDK validada na integração |
| Política/binding dos turnos nativos | 11/11 Windows em memória | Oito casos migrados de LiteDB e três de falhas/cancelamento; não valida armazenamento da identidade |
| Buffer de anexos com reader simulado | 5/5 Windows | Texto não salvo, consentimento/permissão, tamanho e cancelamento; zero chamadas ao reader |
| Ferramentas de sessão e anexos com probe/reader em memória | 71/71 Windows | 35 sessão, 14 arquivos simulados, 5 buffer, 11 políticas e 6 ponte Desktop; inclui casos anteriores |
| Integração após probe e store Desktop | 171 aprovados, 2 ignorados, 173 automatizados Windows | Três novos casos de validação de base de arquivos fechados; duas criações de symlink recusadas; quinze homologações explícitas não executadas |
| Integração após leitura limitada/anexos | 168 aprovados, 2 ignorados, 170 automatizados Windows | Inclui 121 anteriores, 44 anexos movidos e cinco contratos do reader; symlinks recusados pelo Windows local; quinze homologações explícitas não executadas |
| Integração após extração Copilot e separação manual | 121/121 casos automatizados Windows | Quinze testes oficiais explícitos não executados, incluindo seis movidos de UnitTests; inclui os 68 anteriores, não somar novamente |
| Isolamento completo da produção | Em validação final | Recursos ONNX isolados em `Infrastructure.LocalAi.OnnxAdapter`; restantes adapters do SO em `Infrastructure.System`; repetição da integração após injeção Mongo e execução Linux pendentes |
| Mongosh/console/ambiente/admissão MCP/conta com doubles | Filtro 125/125 Windows; build CPU zero avisos/erros | Inclui os 84 anteriores; driver console/DNS/broker de produção ainda pendentes; nenhuma integração nativa reexecutada |
| Claude com doubles e regressão Codex/Copilot | 101/101 Windows | 21 novos Claude e 80 anteriores; stream/resume, recusas e cancelamento entre sessões em memória. Integração CLI real e Linux pendentes |
| Picker, privacidade e arquitetura em memória | 84/84 Windows; build CPU zero avisos/erros | Inclui os 71 anteriores, sete picker, três privacidade e três metadados de assemblies; scanner de fonte separado na integração |
| Integração após catálogo | 175 aprovados, uma falha, dois ignorados de 178 automatizados | Asserção de cancelamento corrigida após falha de tipo exato; reexecução fora do sandbox impedida por limite da revisão automática. Não declarar verde |

Detalhes e critérios de conclusão em [system-adapters](architecture/system-adapters.md). Nenhuma mudança visual foi realizada neste incremento; homologação nativa não é inferida dos testes de ViewModels.

## Meta P7-COP — GitHub Copilot por assinatura — 29/09/2026

**Estado corrente (29/09/2026):** o chat Copilot está habilitado de forma limitada após checagem explícita de conta/modelos. O provider informa `Chat`, `Streaming`, `Sessions`, `ModelSelection`, `TurnPlan` e `ToolCalling`; `NativeTools` permanece falso. Os registros intermediários desta matriz que diziam “provider indisponível” ou “chat não habilitado” preservam o estado observado naquela data/etapa e foram superados pela correção de permissões descrita na Fase 7.

| Escopo atual de tools Copilot | Estado verificável |
| --- | --- |
| Registry geral | 12 leituras MongoDB, 4 ferramentas por sessão e 5 ferramentas de escrita existem no registry comum; isso não significa que todas sejam declaradas ao Copilot. |
| Expostas pelo plano | Leituras de metadados/cache/contexto e `mongo_find`, `mongo_count`, `sample_documents`, `mongo_find_one`, `get_document`, `mongo_distinct` e `mongo_explain` podem ser declaradas conforme permissões; leituras com valores exigem checkbox individual, consentimento separado para enviar documentos MongoDB, conexão permitida e grants transitórios do turno. `mongo_explain` também exige grant de diagnóstico. `propose_file_edit` pode ser incluída conforme modo e permissões. |
| Exercitadas com modelo/runtime oficial | `get_workspace_context` com contexto sintético; `list_connections` com perfil sintético e grant limitado; `get_document` com prompt `ApprovedOnce` e origem fake; `propose_file_edit` em `active_buffer` com consulta sintética, tanto com sink fake quanto com `WorkspaceViewModel` Headless e `AgentEditProposalStore` Desktop de produção. Proposta ficou Registered e não foi aplicada/salva. Nenhum MongoDB foi aberto nesses ensaios. |
| Implementadas, mas não homologadas pelo runtime oficial | As sete leituras com valores MongoDB estão sujeitas a opt-in individual, consentimento e grants transitórios; ainda falta exercício com MongoDB de teste pelo runtime oficial. As leituras de metadados/cache não foram revalidadas nesta rodada. Testes Desktop Headless cobrem o cartão de confirmação (Permitir uma vez/Negar), captura da aba ativa e aplicação segura no `MongoTextEditor`, marcando stale sem sobrescrever texto divergente. Um teste explícito com modelo Copilot oficial agora compõe `WorkspaceViewModel` Headless, runtime, registry e `AgentEditProposalStore` de produção e registra a proposta do buffer não salvo na aba correta, sem aplicá-la; a apresentação e aplicação manual pela UI real ainda não foram homologadas. |
| Não expostas ao Copilot | `get_collection_schema` permanece fora até composição de consentimento local de amostragem; as cinco tools de escrita MongoDB; `approve`; e todas as `NativeTools` (shell, leitura/escrita nativa de arquivos e rede). |

`propose_file_edit` em `target: "active_buffer"` usa o snapshot do editor capturado no início do turno e entrega uma proposta revisável por hunks; a aplicação é no buffer e undoável, sem salvar em disco. `EditNotApplicable` é falha de aplicação da proposta (como texto-base incompatível, diff vazio ou proteção de redação), não prova ausência de editor/workspace ou de grant. A mensagem do modelo vista na captura de tela não é evidência de que o editor estava inacessível.

**Gates reais remanescentes:** consulta Copilot a MongoDB de teste pelo runtime oficial (o teste do registry real foi conectado a um job CI com MongoDB 8, ainda sem resultado); negação, revogação e falhas de auditoria/ponte de tools MongoDB no runtime oficial; falhas/concorrência/recuperação do runtime com tools; aplicação manual da proposta no Desktop e inspeção do diálogo nativo, leitor de tela e revisão final de termos. A chamada oficial de `propose_file_edit` até o store Desktop na aba Headless sintética foi aprovada; não demonstra interação da UI real ou aplicação manual. Testes Headless cobrem a resposta pelo cartão UI, captura de buffer não salvo e conflito/aplicação protegida no `MongoTextEditor`; não substituem homologação nativa. Homologação P7-COP é somente Windows. Não declarar a meta homologada.

**Revalidação automatizada desta revisão (29/09/2026):** build Debug da solução passou sem avisos/erros com `--no-restore -p:UsedAvaloniaProducts=`; filtro combinado atualizado de política, permissões, registry, runtime, chat Desktop, captura de aba e propostas **136/136**. Inclui `FakeCopilotRuntimeRegistersActiveBufferProposalInProductionDesktopStore` (**1/1**), que compõe `WorkspaceViewModel` Headless e captura seu buffer não salvo antes de atravessar adapter Copilot fake→runtime→registry→store real, deixando o buffer intacto; `CopilotWorkspaceContextRequiresConsentCheckboxTabMetadataAndExactTurnScope`, que valida chamada liberada e recusas por checkbox, `TabMetadata`, consentimento externo, turno ou provider divergente; e `CopilotGrantsRequireMatchingProviderPlanAndPermissionSnapshots`, que confirma zero grants sob divergência de provider, plano ou permissões. O filtro `NativeChatTurnPolicyProviderTests` passou **8/8**. Os filtros focados anteriores também passaram: política/permissões/composição/UI **80/80**, fake STDIO Copilot **21/21**, contratos de permissão/runtime simulado Copilot **31/31**, proposta/composição DI **5/5**, testes Desktop de confirmação e captura **2/2**, buffer/propostas/registry/runtime **69/69** e âncoras/eventos Desktop **4/4**. A suíte completa, executada antes dos testes mais recentes, ficou parcial: Benchmarks **43/43**; UnitTests **3.795 aprovados, 12 falhas e 42 ignorados**. As falhas incluem `CredentialStoreFailed` em testes de credenciais/MCP e `UnauthorizedAccessException` ao aplicar ACL em `CodexAppServerHome.PrepareWindows`; não foram atribuídas à mudança Copilot. Restore locked local foi impedido pelo sandbox ao ler `C:\Users\chuke\AppData\Roaming\NuGet\NuGet.Config`. A tentativa do teste de modelo oficial `WindowsOfficialModelProposesEditToSyntheticActiveBuffer` foi encerrada antes de enviar prompt: o SDK não reportou `AuthType=user` neste sandbox; nenhum segredo foi lido. Nesta continuação, o teste explícito sem inferência `WindowsSessionRuntimeReportsOfficialAuthenticationAcrossModes` não conseguiu iniciar `copilot.exe` por `Win32Exception`/“Acesso negado”, antes da consulta de autenticação. O [run CI 36630480325](https://github.com/esilva-silva/EsilvaSoft.KapibaraStudio/actions/runs/36630480325), no mesmo `HEAD` anterior ao diff atual, falhou no restore Ubuntu e no build Release Windows; logs detalhados retornaram HTTP 403, portanto a causa não foi confirmada. Esse run não continha o novo job Mongo, ainda sem resultado. PNGs reais da janela de permissões, cartão de confirmação e revisão de proposta em claro/escuro foram inspecionados. O teste real do registry Mongo permaneceu ignorado localmente.

**Autorização de ferramentas do Copilot (29/09/2026):** corrigida a ausência de grants efetivos para o principal interno: política vazia persistida como âncora e grants somente em memória para cada turno ativo, restritos por consentimento, plano, conexão, geração e destino. `get_cached_schema` usa o mesmo escopo exato do turno. Cinco testes específicos e 51 testes focados de regressão passaram; build Desktop sem avisos. A verificação no aplicativo reiniciado, com MongoDB real e diálogo nativo, continua pendente.

**Propostas, grants e ordem de notificações (29/09/2026):** o teste `FakeCopilotRuntimeRegistersActiveBufferProposalInProductionDesktopStore` agora percorre `WorkspaceViewModel` Headless com uma consulta não salva, adapter Copilot fake, `AgentRuntime`, registry e store Desktop; a proposta fica vinculada à aba de origem como `Registered` e não modifica o buffer (**1/1**). `CopilotGrantsRequireMatchingProviderPlanAndPermissionSnapshots` adiciona casos fail-closed de provider/plano/permissões divergentes; o filtro `NativeChatTurnPolicyProviderTests` passou **8/8**. Para validar callbacks concorrentes sem depender da afinidade incorreta de `Dispatcher.UIThread.CheckAccess()` no Headless, o teste de ordem injeta uma fila de despacho controlada; 32 mutações concorrentes e um estado terminal `Applied` produzem 33 notificações, todas com o snapshot final. Passou **30/30** repetições, e o filtro combinado de regressão passou novamente **136/136**. Isso comprova a ponte local Headless e a ordenação de callbacks sob a fila simulada, não a janela nativa ou runtime oficial.

**Modelo Copilot oficial→store Desktop (Windows, 29/09/2026):** o teste explícito `WindowsOfficialModelProposesEditIntoActiveWorkspaceViewModelStore` passou **1/1** após autenticação `AuthType=user` confirmada nos clientes oficiais de conta/sessão. Com uma única inferência sintética, o modelo chamou `propose_file_edit`; o `AgentRuntime` e registry de produção registraram no `AgentEditProposalStore` associado ao `WorkspaceViewModel` a alteração `.limit(10)`→`.limit(5)`, vinculada ao `TabId` correto. O buffer permaneceu intacto e nenhum MongoDB/arquivo real foi acessado. Isso fecha a ponte do runtime oficial ao store Headless; não valida a apresentação/aplicação visual no app nem diálogo nativo/leitor de tela.

**Confirmação pontual pelo runtime oficial (Windows, 29/09/2026):** `WindowsOfficialModelCallsSyntheticDocumentReadWithOneCallApproval` passou **1/1**. O modelo oficial pediu `get_document`; o registry apresentou a chamada ao prompt de confirmação pontual e só depois de `ApprovedOnce` despachou exatamente uma leitura à origem sintética, com grants restritos ao turno. A origem não conecta a MongoDB e retorna apenas um documento sintético. Isso valida o percurso de confirmação do SDK oficial com prompt controlado, mas o cartão Avalonia real continua coberto por Headless; interação manual/nativa, recusa/revogação oficial e MongoDB de teste seguem pendentes.

| Gate | Evidência disponível | Status e limite |
| --- | --- | --- |
| SDK/runtime oficial | `GitHub.Copilot.SDK` 1.0.14; runtime Windows x64 1.0.85 com SHA-256 verificado; restores locked CPU/WinML/CUDA | Parcial: autenticado, runtime transmitiu resposta sintética sem tools, retomou sessão persistida após criar outro cliente SDK e apagou os arquivos SessionFs temporários. Publish Release Windows x64 passou. Linux fora do escopo de homologação P7-COP |
| Conta e segurança | Testes de classificação `AuthType=user`, allowlist do ambiente, descoberta de CLI e decisão pre-tool; sem leitura de credenciais nem fallback para API | Fora do sandbox, conta oficial autenticada (`AuthType=user`) e catálogo de modelos coincidente nos clientes de conta e sessão (1 modelo), sem inferência. O sandbox isolado retornava falso por não acessar o Credential Manager; homologação manual usa o contexto Windows do usuário |
| Provider, sessão e permissões | SDK consulta autenticação/modelos sem inferência; filtros Copilot e reserva/erase cobertos por testes; consultas de conta e sessões usam `CopilotCli`, com allowlist explícita e recursos ambientais restritos por sessão; SessionFs volátil e persistente. Runtime STDIO falso verifica create/resume, rejeição de `bash`, retorno de tool planejada e cancelamento isolado entre conversas. Runtime oficial Windows já validou streaming, retomada/erase, cancelamento e chamadas restritas de contexto/metadados | Chat liberado de forma limitada após checagem explícita. A disponibilidade não homologa todas as ferramentas: falhas/concorrência e recuperação ainda não foram homologadas completamente; consulta MongoDB real aguarda fixture Windows |
| Retenção e exclusão | `IAgentProviderSessionCleanup` chama exclusão nativa antes da linha LiteDB; limpeza global valida as conversas antes da exclusão. `KeepHistory=false` configura SessionFs/SQLite em memória por ID, com limites de 16 MiB por arquivos e SQLite, 4.096 arquivos, 4.096 diretórios e caminhos de 4.096 caracteres; isolamento, exclusão e operações concorrentes têm testes focados | Parcial: runtime oficial escreveu 3 arquivos de conteúdo na SessionFs persistente temporária; um novo cliente retomou a sessão e o cleanup removeu a árvore. Ainda faltam crash recovery e falhas/concorrência da limpeza com runtime real; linha ilegível ou erro nativo continua bloqueando a remoção LiteDB |
| Interface | Teste de renderização Copilot passou; PNGs pt-BR claro/escuro inspecionados | Parcial: leitor de tela e diálogos nativos pendentes |

**Atualização de evidências (29/09/2026):** store SessionFs persistente implementado e coberto por **11/11** testes; regressão Copilot **67/67**; limpeza/reserva do chat **9/9**. O caminho persistente é separado do home da CLI e usa diretório por hash, SQLite por sessão, limites, escrita atômica e lock exclusivo entre processos. Teste Windows confirmou que um processo separado não abre lock de sessão ocupado; o erase espera o lock e chama a exclusão nativa antes de apagar a árvore local. As execuções sandboxadas retornavam `NotLoggedIn` por falta de acesso ao Credential Manager; com acesso Windows ampliado, os clientes oficiais `CopilotCli` de conta e sessão retornaram `AuthType=user` e catálogo coincidente (1 modelo), sem inferência. Uma resposta sintética sem tools passou com SessionFs volátil e persistente; o teste persistente verificou 3 arquivos, retomada por um novo cliente SDK e erase da árvore temporária. Permanecem pendentes falhas/crash/concorrência de limpeza com runtime real, cancelamento real e tools do produto. Provider permanece indisponível; a homologação da meta é somente Windows.

**Revisão de composição (29/09/2026):** o teste de integração do adapter agora também confirma que o cliente de sessão recebe a configuração persistente SessionFs, mas mantém o mesmo `CopilotCli`/home oficial usado para autenticação. Resultado atual: store persistente **7/7**, filtro Copilot **63/63**, limpeza/reserva de chat **9/9** e build de solução 0 avisos/erros. Estado oficial da conta não mudou: `NotLoggedIn`; runtime autenticado continua gate impeditivo. Nenhuma validação Linux foi executada para homologação.

**Revisão de exclusão concorrente (29/09/2026):** o SessionFs persistente usa lock exclusivo por ID em `.locks`, mantido enquanto a sessão estiver aberta. Apagar adquire o lock antes de `DeleteSessionAsync` no SDK; contenção entre processos/store e erro do delete nativo preservam os dados para retry. Teste Windows abre outro processo PowerShell para confirmar a negação de lock concorrente. Store **11/11**, filtro Copilot **67/67**, limpeza/reserva **9/9**, build 0 avisos/erros. Conta continua `NotLoggedIn`, e o roteamento do runtime real permanece por verificar.

“Testar conexão” atualiza autenticação e modelos elegíveis por uma consulta explícita de metadados (sem prompt/contexto/inferência); `GetStatusAsync` segue passivo. A checagem, isoladamente, não inicia conversa; após status de conta elegível, o catálogo atual disponibiliza as capabilities limitadas acima. O aceite de P7-COP será feito no Windows; Linux está fora desta homologação e não deve ser apresentado como suportado/homologado por esta meta. Para exclusão de conversas, a sessão persistida pelo runtime deve ser removida antes da linha LiteDB; falha ou histórico ilegível impede a limpeza e precisa ser reportada. Consulte o [estado, roteiro e limitações da meta](phases/phase-07-v0.11.0/README.md#meta-p7-cop--github-copilot-por-assinatura).

## Lote 0 da v0.11.0 — spike Codex confirmado parcialmente — 22/09/2026

**Estado: em andamento; apenas disponibilidade, versão e geração do schema estão aprovadas para avançar dentro do lote 0.** No Windows, `codex.exe` reportou `codex-cli 0.155.0-alpha.16`, SHA-256 `97d4d67419d0ac2f71342f9a5e850f9468aa622618de8ea823223edb9a91926a`. Foram gerados 310 schemas JSON, com 3.512.403 bytes; diretório mais manifesto somaram 3.559.959 bytes. O manifesto registra `credentialsCaptured=false`; os hashes dos 310 arquivos conferiram, com zero divergências. A saída está em `%TEMP%\slop-phase07-codex-review-edc3773c90604717a023886faca3cc52`, fora do repositório e local a este host.

| Gate | Evidência disponível | Status e limite |
| --- | --- | --- |
| Codex App Server — disponibilidade/versão/schema/handshake | [Spikes Codex](../eng/spikes/phase-07/codex-app-server/README.md): 310 schemas conferidos e `initialize` STDIO válido no Windows; envio de `initialized`, EOF e exit 0. Detalhes no [relatório](phases/phase-07-v0.11.0/README.md). | **Aprovado somente para avançar internamente no lote 0.** Fonte oficial ainda classifica App Server experimental e sem suporte de produção; adoção produtiva bloqueada. Saídas locais não versionadas/portáteis. Confinamento, auth/keyring, sessão e compatibilidade pendentes. API direta agora tem spike contratual offline, ainda sem provider real/capabilities homologadas. |
| OpenAI API direta | [Spike SDK .NET 2.14.0](../eng/spikes/phase-07/openai-api/README.md): build e 5/5 testes offline aprovados para stream SSE fragmentado, tool allowlist/schema, associação de chamada e cancelamento isolado. | **Aprovado somente como contrato local candidato.** Sem conta/chave real, chamada de rede ou suporte por modelo; auditoria de vulnerabilidade não executada por indisponibilidade do feed. Sem dependência no produto. |
| MCP dual-era | [Spike isolado](../eng/spikes/phase-07/mcp-sdk/README.md) fixou SDK 2.2.0; **10/10 testes Windows**, incluindo cliente independente BCL em processo separado e seis cenários adicionais, sem biblioteca/serializer MCP compartilhado pelo cliente. Pipes STDIO reais e fixtures autorais cobrem `2025-11-25`/`2026-07-28`, descoberta/listagem vazia e rejeição explícita de incompatibilidade. | **Aprovado somente como prova mínima de descoberta/listagem sintética contra servidor C# no Windows.** Linux, clientes comerciais como Claude/Codex, conformidade integral, concorrência, broker e tools reais pendentes. Inventário não é SBOM aprovado. |
| Anthropic SDK/provider | Spike isolado oficial `Anthropic` 12.50.0: restore locked/build, inventário de 2 pacotes, licenças e hashes reproduzível; assinatura NuGet verificada e audit sem vulnerabilidades reportadas. Detalhes em [`eng/spikes/phase-07/anthropic-sdk`](../eng/spikes/phase-07/anthropic-sdk/README.md). | **Aprovado somente o candidato/tag/licença e contrato de streaming compilado.** Nenhum stream observado, chamada API, credencial ou tool calling real; sem validação Linux. Inventário `net10.0` sem RID não é SBOM de produto. |
| Cofre/keyring Windows/Linux | O spike Windows mantém três falhas `CredWriteW` 1312 (`NoLogonSession`) sem gravação reproduzível. `WindowsCredentialSecretStore` isolado passou **30 testes determinísticos**; o round-trip nativo foi ignorado por 1312. `LinuxSecretServiceSecretStore` isolado passou **84 testes focados**. | **Adapters parciais, sem prova nativa:** revisão estática independente permite avançar, mas não homologa Credential Manager ou Secret Service. DI, provider, migração, reinício, bloqueio, isolamento por conta e recuperação seguem pendentes. D-Bus/Secret Service Linux não foi exercitado; WSL retornou `E_ACCESSDENIED`. |
| Secret Service Linux — binding e adapter | `Tmds.DBus.Protocol` 0.94.1 (MIT), commit, assinatura e hashes registrados no relatório; `LinuxSecretServiceSecretStore` e seu protocolo/criptografia associados passaram 84 testes focados. | **Implementação isolada, sem runtime Linux:** nenhum serviço D-Bus/Secret Service foi acessado. Prompts, cancelamento/desbloqueio real, headless, ciclo de sessão e interoperabilidade continuam pendentes; nenhuma dependência ou cofre foi integrado ao produto. |
| Licenças e transitivas | Pesquisa inicial registrada em [fontes e licenças da fase](phases/phase-07-v0.11.0/README.md). | **Pendente:** fixar artefatos/tag/hash e lockfiles; auditar dependências transitivas e nativas por RID, vulnerabilidades, SBOM, NOTICE e termos antes de distribuição. |

Os resultados não provam confinamento de ferramentas nativas, autenticação, keyring, sandbox, atividade de processo ou compatibilidade ampla. A revisão de path não protege contra TOCTOU atômico. Schemas/manifesto estão em diretório temporário local fora do repo; não são evidência portátil. **Validações futuras planejadas — não executadas para o produto por este registro:** `dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode`; `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore` (em ambiente com telemetria Avalonia bloqueada, acrescentar `-p:UsedAvaloniaProducts=`); `dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore`. Os resultados de build/test citados acima pertencem somente aos spikes isolados.

Revalidação da solução existente em 23/09/2026: restore locked local pelo cache, build com 0 avisos/erros, 2.699 UnitTests aprovados/0 falhas/20 ignorados e 43 Benchmarks aprovados. Isso confirma a base anterior, não features novas da Fase 7. **AC-01 a AC-20 permanecem pendentes**, conforme [critérios de aceite](phases/phase-07-v0.11.0/README.md); evidência detalhada, incluindo o primeiro teste bloqueado por falta de espaço, está na [validação da meta](phases/phase-07-v0.11.0/README.md). O acompanhamento correspondente está em [12-acompanhamento](12-acompanhamento-da-implementacao.md#lote-0-da-v0110--spike-codex-confirmado-parcialmente--22092026).

## Lotes 1–2 — cofres, políticas e registry internos

`ISecretStore` e `IAgentCredentialProvider` foram adicionados em Application; `SecretReference` opaca/versionada fica em Core. `WindowsCredentialSecretStore` isolado em Infrastructure: **30 testes determinísticos aprovados** e **1 round-trip nativo ignorado** por `1312 / NoLogonSession` (reconfirmado no Windows em 23/09/2026). `LinuxSecretServiceSecretStore` isolado: **84 testes focados aprovados em execução anterior**, sem D-Bus/Secret Service nativo; nenhuma execução Linux foi feita nesta continuação. A persistência de política/grants via proprietário LiteDB, evaluator e registry interno de `list_connections` estão implementados; provider e repository de política, auditoria e evaluator estão registrados no DI, compartilhando o único owner. A suíte focada Windows de composição, evaluator, registry, política e ledger passou com **77 aprovados, 0 ignorados** em 23/09/2026. Inclui regressões de snapshot de perfil e alias externo para nomes de conexão, além dos testes isolados do ledger. Estado: **implementação parcial; sem ingress MCP/runtime, integração de auditoria fail-closed, controle completo de saída, ferramenta exposta ou aprovação de qualquer AC**. A revisão encontrou uma falha na correlação de intents ainda em correção; não comprova provider real, protocolo MCP ou homologação nativa completa.

## Fase 5 / v0.9.0 — IA local e produtividade contextual — 22/09/2026

Escopo automatizável implementado. Restore locked-mode passou com `NuGet.Config` temporário apontando ao cache local e `NuGetAudit=false`; sandbox bloqueia o config global e TLS com NuGet.org, então a auditoria online de pacotes ficou indisponível. Build integrado (`dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -p:UsedAvaloniaProducts=`): **0 avisos, 0 erros**. Suíte completa (`dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore -p:UsedAvaloniaProducts=`): **2.742 aprovados, 0 falhas, 20 ignorados** (2.699 UnitTests + 43 Benchmarks). Os 20 ignorados incluem verificações condicionadas a pesos ONNX/ambiente real.

| Frente | Evidência automatizada | Limite registrado |
| --- | --- | --- |
| F5-02/05 — privacidade e chat | 73 testes focados; consentimento global e por conexão; Input JSON em opt-in separado; snapshot de contexto revisto antes da inferência e invalidado se instrução, editor, destino ou política mudar; diff recalculado, proposta inválida/obsoleta recusada, confirmação adicional e undo; nenhuma execução Mongo ao aplicar | Fakes e serviço baseline não comprovam fidelidade do modelo local real |
| F5-03 — runtime multimodelo | 4 testes focados, inclusive chamada já registrada/aguardando durante troca e override ChatModel distinto permitido apenas na seleção base atual | Pacotes ONNX, providers GPU/NPU e falhas de hardware reais não exercitados |
| F5-04 — preemptivo e IME | Rajada de 20 teclas: 20 lookups determinísticos locais, 0 inferências de IA antes do debounce e uma única execução de fallback depois; lifecycle de preedit IME publicado e suprime ghost; quatro execuções de edição→ghost p95 **3,85–8,42 ms** | Headless não homologa IME nativo, acessibilidade ou temporização em Windows/Linux gráfico |
| F5-06/07 — UI e idiomas | 73 casos focados nos quatro idiomas; PNGs Headless claros/escuros de chat/proposta e prévia nos quatro idiomas; amostras pt-BR claro, zh-CN escuro e en claro inspecionadas; undo após aplicar | Revisão linguística de domínio, leitor de tela e layouts nativos continuam na Fase 9 |

PNGs do chat e prévia: `tests/EsilvaSoft.KapibaraStudio.UnitTests/bin/Debug/net10.0/ui-evidence/ai-chat-localization/` (quatro locales × claro/escuro). Benchmarks condicionais não executados estão contados como ignorados; não se declara inferência real concluída. A Fase 5 permanece experimental até a homologação aplicável; ver [meta de implementação](phases/phase-05-v0.9.0/meta-de-implementacao.md).

**Atualização de 25/09/2026 (P7-L06-CLEANUP, [ADR-055](10-decisoes-arquiteturais.md#adr-055--remoção-do-assistente-ia-por-aba-25092026)):** o painel Assistente IA por aba foi removido; as linhas F5-02/05 (parte de chat) e F5-06/07 deixam de ter código e testes. Removidos junto com o código: `AiChatTests` (15 casos), `WorkspaceTabAiChatLocalizationUiTests` (4 casos, PNGs `ai-chat-localization`), e em `DeepSeekIntegrationTests` `ChatSharesModelAndRejectsTruncatedProposals`, `ChatPrivacyPreventsModelLoading` (2 casos) e o explícito `RealChatProducesReviewableCode`. Ajustados para chamar o papel `Chat` de `ILocalAiModelService` diretamente, sem afrouxar asserções: `LocalAiModelServiceTests.ChatIsUnavailableForAModelThatDoesNotDeclareIt`, `ExplicitChatPreemptsRunningAutocompleteWithoutReloading` e `DeepSeekIntegrationTests.CancelingChatDoesNotCancelQueuedAutocomplete`; privacidade, preempção e cancelamento isolado do provider local seguem em `LocalAgentProviderTests`. Suíte (SDK 10.0.401, Windows): UnitTests de 3.584/0/20 (3.604) para **3.562 aprovados, 0 falhas, 20 ignorados (3.582)**; Infrastructure.Agents.Tests 95/0; Benchmarks 43/0. Os opt-ins de contexto local persistidos ficaram sem consumidor; na revisão seguinte seus controles saíram das preferências e do cadastro de conexão (valores preservados, sem migração). `AiPrivacySettingsUiTests` passou a provar a ausência dos controles e a preservação dos valores ao salvar, e o novo `AgentChatEditorRevisionTests` (2 casos) prova, com o `WorkspaceTabViewModel` real, que editar o texto avança `EditorRevision`/`DocumentVersion` e descarta o pacote revisado do Agente IA. Suíte em worktree isolado com só estas alterações: UnitTests **3.545 aprovados, 0 falhas, 39 ignorados (3.584)** — os 19 ignorados a mais são testes de MongoDB real sem o binário local no worktree; na árvore principal eles passaram na rodada anterior —, Infrastructure.Agents.Tests 95/0, Benchmarks 43/0 (pendência no [doc 16](phases/phase-07-v0.11.0/README.md)).

## Estado corrente da meta de autocomplete — 21/09/2026

Evidência automatizada atual: `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -p:UsedAvaloniaProducts=` passou com **0 avisos e 0 erros**; a suíte `EsilvaSoft.KapibaraStudio.UnitTests` passou com **2.639 aprovados, 0 falhas e 20 ignorados**; o corpus `LanguageCaseCorpusTests` passou com **676 testes verdes** (675 fixtures e o gate agregado de ranking). Os testes novos cobrem namespaces de metadata, schema aprendido no catálogo/completion, edições/snippets, `$lookup` estrangeiro e propagação de campos por ramos de `$facet`.

Fora desta meta: gates de performance, validação em múltiplas plataformas, MongoDB real, modelos reais, leitor de tela e homologação nativa. O gate funcional publicado de ranking cobre 44 casos locais com **MRR 1,000, top-1 44/44 e top-5 44/44**; namespaces de metadata, schema learning e `$lookup` estrangeiro têm suites determinísticas próprias.

## Meta transversal — internacionalização — concluída no recorte automatizado — 21/09/2026

`ApplicationLanguageTests`, `LocalizationViewModelTests` e `LocalizationUiTests` cobrem os quatro códigos (`pt-BR`, `en`, `es`, `zh-CN`), normalização com fallback em inglês, persistência aditiva, troca em execução, chaves ausentes, placeholders, diagnósticos e status da IA. A matriz UI gera **64 PNGs reais** (oito janelas × quatro idiomas × claro/escuro); amostras CJK, espanhol, claro e escuro foram inspecionadas. Build da solução passou sem avisos/erros; os testes unitários passaram com **2.639 aprovados, 0 falhas e 20 ignorados**. Benchmarks não fazem parte do critério desta meta. `docs/**/*.md` permanece em português, e fontes remotas/runtime ONNX também usam o localizador com fallback em inglês.

O resultado Headless não encerra a homologação: continuam pendentes revisão linguística de domínio, MongoDB/mongosh real, leitor de tela, diálogos nativos e execução Linux gráfica. Os arquivos `docs/**/*.md` permanecem em português por decisão de escopo.

## Fase 1 (K11–K16, K16-b) e Fase 5.1 (Traditional Preemptive) — 18/09/2026

Build `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore`: **0 avisos, 0 erros**. Suíte
`dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore`: **1273 aprovados, 0 falhas** (baseline de entrada:
1154). Esta seção separa o que tem **validação automatizada** (build + teste + benchmark local) do que exige
**homologação manual** (hardware/SO/entrada reais), concentrada na [Fase 9 / v0.13.0](phases/phase-09-v0.13.0/README.md), como exigido pelo `AGENTS.md`.

### Validação automatizada (evidência local, esta máquina)

| Item | Evidência |
| --- | --- |
| K12 — cancelamento no scan de substring | Teste dedicado de `NameTable.Collect` com cancelamento |
| K13 — limite de cargas simultâneas do `MetadataCache` | Teste de concorrência com 2/conexão e 4 globais |
| K14 — LRU de 8 entradas na memoização de mescla | Teste de `MetadataCatalogSource` com estouro do LRU |
| K15 — teto de profundidade/nós em `CollectionSchema.Merge` | Teste com schema que excede `SchemaMaximumDepth`/`SchemaMaximumNodes` |
| K16-b — cota por fonte em `KnowledgeCatalog.Query` | Teste com duas fontes do mesmo `kind` disputando `MaximumCandidates` |
| K11 — travas de regressão | Cache esvaziado após `InvalidateEnvironment`; hosts distintos com mesmo nome de namespace sem reuso cruzado |
| P51 — coordinator, debounce, cancelamento | 20 teclas abaixo do debounce → 20 lookups determinísticos locais, zero inferências IA; pausa gera exatamente um fallback; sete gatilhos de cancelamento cobertos |
| P52 — provider determinístico e confiança | Abstenção por ambiguidade/truncamento/palavra completa/snippet; funcionamento sem modelo/MongoDB |
| P53 — presenter, typeahead, undo | Aceite como operação única de undo sem executar consulta; isolamento entre abas |
| Flags inline e migração | Quatro combinações de `InlineUseTraditional`/`InlineUseAi`; ausente ≠ `false` explícito; migração v1 com `InlineUseAi = false` |
| Camada 0 do inline (computação) | p95 0,009 ms (catálogo de linguagem) e p95 0,068 ms (200 campos de schema); orçamento de 5 ms atendido |
| Cache de tokens da Fase 2 | Refiltro com lista aberta: 64 KiB/fim 0,37×, 1 MiB/meio 0,14×, 1 MiB/início 71 ns; análise por tecla de volta a 7,38 ms (baseline 7,78 ms) |

### Homologação manual — NÃO executada, não declarar cumprida

| Item | Situação |
| --- | --- |
| Edição → ghost, p95 ≤ 20 ms | Meta atendida em Headless após otimização: p95 de 3,85–8,42 ms em quatro execuções. Não homologa temporização nativa |
| IME real | O editor publica início/atualização/fim de preedit; ciclo coberto em Headless. IME nativo aguarda homologação |
| Layouts físicos (ABNT2/US Windows; X11/Wayland Linux) | Não executado |
| Leitor de tela | Não executado |
| MongoDB real | Não exercitado neste lote |
| Modelos ONNX reais no caminho inline com IA | Não exercitado neste lote |
| Matriz de 18 PNGs (claro/escuro) do ghost 5.1 | Não produzida |

### Pendências de escopo, não de evidência

- K17 (relatório de performance do catálogo) permanece fora desta meta; L11–L15 estão implementados com testes, enquanto L16 (benchmark/gate de performance) permanece fora desta meta.
- `InlineEnabled`/`InlineUseTraditional`/`InlineUseAi` têm bindings e controles em `AutocompleteSettingsWindow`; a anotação anterior que dizia serem editáveis só via JSON estava desatualizada.
- Registro histórico: os arquivos `.case` em `tests/.../Language/Cases/` estavam sem runner. Desde 21/09/2026, o runner automatizado cobre parsing, papéis, shapes, kinds, aspas, `ReplaceSpan`, edições/snippets e ranking tipado (**675 fixtures aprovados**); o gate agregado publica MRR/top-1/top-5 e as integrações de namespace metadata, schema learning e `$lookup` têm cobertura determinística complementar.
- Fases 5.2 e 5.3 estão implementadas no fluxo automático com fakes: `LoadedOnly`, debounce, cancelamento, descarte por geração e arbitragem tradicional → IA são cobertos. A Fase 4 também tem `Ctrl+;`, indicador, prévia inline multilinha e matriz de fallback; o caminho com modelo ONNX real continua fora da validação desta meta.

Detalhe: [acompanhamento](12-acompanhamento-da-implementacao.md#fase-1-k11k16-k16-b-e-fase-51-traditional-preemptive--18092026),
[phase-1-data-traditional](auto-complite/phases/phase-1-data-traditional.md),
[phase-5-preemptive](auto-complite/phases/phase-5-preemptive.md) e [performance](auto-complite/performance.md).

## Fase 2 — autocomplete tradicional (estado em 16/09/2026)

Build focalizado e suíte UnitTests foram executados em Windows x64: **1126 aprovados, 0 falhas**. Esta evidência cobre testes automatizados; não substitui MongoDB real, leitor de tela, layouts nativos ou métricas p95. `AvaloniaTextSnapshot` e o caminho contextual estão cobertos por testes focados. Permanecem pendentes o corpus MRR/top-K, a matriz de 18 PNGs e os gates de desempenho da UI.

### W0 — Política de atalhos do autocomplete (18/09/2026)

Build Windows x64: **0 avisos, 0 erros**. Suíte completa: **1170 aprovados, 0 falhas**. Evidência cobre `EditorCommandScope`/`EditorCommandDispatcher`/`EditorKeyBindings` (Core), o roteamento por escopo em `WorkspaceTabView.Autocomplete.cs` e a projeção de exibição pt-BR em `WorkspaceTabViewModel.GestureText` (Desktop), incluindo os três defeitos de casamento de tecla corrigidos (modificador solitário, `Enter` físico, no-op de `↑`/`↓`). Não homologado por esta evidência: layouts físicos reais (ABNT2/US em Windows; X11/Wayland em Linux), IME real e leitor de tela — teste Headless não os substitui. Detalhe em [política de atalhos](auto-complite/editor-integration.md#arbitragem-de-teclado), [AC-08](auto-complite/decisions.md#ac-08--atalhos) e [acompanhamento](12-acompanhamento-da-implementacao.md#w0--política-de-atalhos-do-autocomplete-18092026--concluído).

## Autocomplete — Fase 1: catálogo de conhecimento — 14/09/2026

Restore `--locked-mode` da solução aprovado, com lockfiles novos do projeto de benchmarks para Cpu, WinML e Cuda. Build Debug WinML: 0 avisos, 0 erros. Suíte regular: **681 aprovados, 0 falhas, 4 ignorados**, em `tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/fase1-regular.trx`. Os ignorados exigem MongoDB portátil ausente nesta máquina, inclusive o novo `MetadataSourceListsKindsValidatorsIndexesAndSamplesWithoutValues`. Testes Explicit de modelos seguem fora da seleção.

| Área | Evidência local | Validação pendente |
| --- | --- | --- |
| Linguagem MongoDB como dados | Integridade do arquivo; projeção do highlighting como superconjunto do vocabulário anterior; contrato com as globais e proxies reais do bootstrap do Console (Jint) | Revisão por versão de servidor |
| Metadata Cache | Leitura sem bloqueio, single-flight com 50 leituras concorrentes, stale-while-revalidate, backoff, LRU, desconexão descartando resultado tardio, invalidação por chave, amostragem só por ação explícita ou opt-in; 29,9 MB retidos no cenário 1 000 × 1 000 com validator em todas | Carga real com permissões restritas |
| Schema | Validator `$jsonSchema`, índices, resultados (sem valores), amostra com ocorrência, limites de nós e profundidade | Amostragem no servidor real, views e time series |
| Catálogo | Consulta apenas às fontes pedidas, dialeto, prefixo sem `$`, campos mesclados por caminho, completude | — |
| Invalidação | DDL via WorkspaceService e Console, falha sem publicação, write-through do Explorer | MongoDB real |
| Métricas | Tags restritas à lista permitida, sem texto de usuário | — |
| Benchmarks | Projeto BenchmarkDotNet e cenário de memória; números em [performance](auto-complite/performance.md#baseline-medida--fase-1) | Segunda máquina de referência |

UI: nenhum comportamento visual novo; os testes Headless existentes de highlighting e autocomplete continuam aprovados. Não houve homologação nativa nem Linux nesta entrega.

## Incremento de consultas avançadas — 14/09/2026

**Aceite da meta textual:** **650 aprovados, 0 falhas**, `phase2-acceptance.trx`; build sem avisos/erros. `AggregationFieldInferenceTests` acrescenta projeções, remoções, renomeações, joins, `let` e facets. `DerivedFieldSuggestionInsertsAtTheCursorAndPreservesUndoOffline` valida a jornada real do editor sem rede (90,4 ms no ensaio focado, sem generalizar para desempenho nativo). [Auditoria e limites](backlog/27-consultas-avancadas.md). Checkpoints abaixo preservam suas contagens históricas.

Revisão posterior: **643 aprovados, 0 falhas**, `phase2-final-audit.trx`. `AggregationHistoryTests`, `MongoCompletionTargetTests`, fixture real de seleção/erro e 18 PNGs adicionais de Histórico. Não confundir esse total com os checkpoints anteriores abaixo; testes Explicit de modelos permanecem fora da execução regular. Detalhes e pendências em [27](backlog/27-consultas-avancadas.md).

Suíte completa após o incremento: **623 aprovados, 0 falhas, 0 ignorados** em `tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/phase2-current.trx`. Testes Explicit de modelos IA continuam fora da seleção regular.

Restore locked e build com `-p:UsedAvaloniaProducts=` aprovados. `phase2-analysis.trx`: 29 testes aprovados, incluindo validação offline, proteção de escrita, autocomplete básico contextual, localização no editor e isolamento/cancelamento de explain. MongoDB portátil **8.0.30**: fixture independente dos 12 stages, join, unwind, facet, resultado Bia/5, contagem 2 e plano real aprovados; nenhuma coleção de saída criada. 18 PNGs `aggregation-diagnostic-*` gerados; inspeção de claro 960 e escuro 1366 confirmou seleção do erro e painel de diagnóstico. Validação Headless não comprova leitor de tela ou sessão nativa. [Pendências de aceite](backlog/27-consultas-avancadas.md).

Este documento separa o que já possui evidência local do que exige um ambiente MongoDB/mongosh real. Nenhuma linha marcada como pendente deve ser apresentada como homologada.

| Área | Windows | Linux | Evidência local | Validação externa necessária |
| --- | --- | --- | --- | --- |
| Compilação .NET 10 | Aprovada em 10/09/2026, 0 avisos/erros | Aprovada em Ubuntu/WSL2, SDK 10.0.400 | Restore travado e solução compilada nos dois sistemas | Validar distribuição nativa em máquina limpa |
| Testes NUnit | 244/244 aprovados em 10/09/2026 | 244/244 aprovados em 10/09/2026 | Logs TRX e testes de domínio, sessão, concorrência e UI | CI publicada e integração MongoDB/mongosh |
| LiteDB | Código e testes de repositório | Código e testes de repositório | Perfis, histórico, consultas salvas e auditoria usam LiteDB | Reiniciar a aplicação e verificar o arquivo de workspace em cada sistema |
| CRUD BSON | Código implementado | Código implementado | Validações e adaptador do driver oficial | Fixture MongoDB com UUID, datas, arrays, duplicidade e filtros concorrentes |
| Criação e remoção de banco | Código implementado | Código implementado | Criação por coleção inicial e confirmação, remoção protegida por nome | MongoDB real com permissões mínimas, nomes inválidos e atualização do explorer |
| Índices | Código implementado | Código implementado | Chaves BSON, único, esparso, TTL e filtro parcial | Índices reais, erro de permissão, tipos text/hashed/wildcard e impacto de build |
| Views | Código implementado | Código implementado | `create` com `viewOn`, edição protegida via `collMod` e pipeline JSON; opções capped são rejeitadas | MongoDB real, leitura somente, dependências, edição e permissões |
| Validação de coleção | Código implementado | Código implementado | `listCollections` para leitura, `collMod` para alteração e schema inicial inferido de até 200 documentos em memória | Validadores válidos/inválidos, inferência de tipos BSON, `warn`, `off`, permissões e versão do servidor |
| Script JavaScript + JSON | Código implementado | Código implementado | Runner `mongosh --norc`, entrada EJSON e `slop.results`; persistência da entrada somente por opção explícita | `mongosh` instalado, autenticação, cancelamento e script com múltiplas consultas |
| Administração e manutenção | Código implementado | Código implementado | `serverStatus`, `hello`, `currentOp`, usuários, papéis, estatísticas, `validate` completo e `compact` de coleção com confirmação e auditoria | Privilégios mínimos, respostas por versão, impacto de `validate`/`compact`, mensagens de autorização e topologias reais |
| Exportação/importação lógica | Código implementado | Código implementado | Manifesto, Extended JSON e upsert por `_id` | UUID, datas, coleção grande, falha parcial e retomada revisada |
| Interface Avalonia | Headless/Skia aprovado | Headless/Skia aprovado em Ubuntu/WSL2 | Dois temas × três tamanhos × três escalas; modal, editor textual, fonte 20, foco e atalhos | Leitor de tela, seletores nativos e gerenciadores de janelas reais |
| Rascunhos e preferências | Aprovados | Aprovados | Debounce, migração aditiva, reinício sem conexão/resultados, opt-out, descarte e falhas | Avaliar jornadas de uso prolongado com usuários |
| Abas e contexto | Aprovados com executor controlado | Aprovados com executor controlado | Destino capturado, respostas fora de ordem, seleção sem fallback, cancelamento isolado, perfil alterado | Executar os mesmos fluxos contra MongoDB/mongosh reais |

| Consulta editor-first | Documentada | Documentada | Consulta, explain, histórico e autocomplete usam o texto do editor; banco/coleção são contexto da aba; não há formulário paralelo de filtro/ordenação | Homologar ergonomia, acessibilidade e autocomplete com MongoDB/mongosh reais |

## Procedimento mínimo

1. Instalar .NET 10 SDK, MongoDB compatível e `mongosh` no sistema alvo.
2. Criar uma fixture descartável com um usuário de leitura e um usuário de escrita.
3. Executar build e NUnit, guardando o log e a contagem aprovada.
4. Abrir a aplicação, cadastrar duas conexões e exercitar CRUD, índices, view, exportação, administração e script.
5. Repetir no segundo sistema operacional e registrar versão do servidor, versão do shell, topologia e privilégios.
6. Atualizar a coluna de evidência deste documento e o [acompanhamento da implementação](12-acompanhamento-da-implementacao.md) somente após a execução real.

O modo script não deve receber senha resolvida em argumentos. O runner aceita URI direta ou interpolada pelo ambiente do processo filho; integração com cofre nativo permanece pendente.

## Baseline e evidência atual

A baseline anterior à revisão era de 229 testes aprovados no Windows. Em 10/09/2026, a revisão passou com 244 testes no Windows e no Ubuntu/WSL2, incluindo renderização Headless/Skia. Os logs estão relacionados no acompanhamento. Homologação com MongoDB/mongosh, diálogos nativos, leitor de tela e gerenciadores de janelas reais continua pendente.

Após a alteração de visibilidade de índice, build sem avisos nem erros e 204 testes NUnit aprovados foram confirmados no Windows, sem consumir crédito de reset. Após incluir o cadastro rápido de URI, a duplicação de prévia de documento e a `wildcardProjection`, a solução continuou sem avisos nem erros e 218 testes NUnit foram aprovados, também sem consumir crédito de reset. A URI fornecida foi exercitada somente na análise local; a automação nativa desta sessão não expôs a janela Avalonia para validação visual.

## Reprodução da revisão UI/UX

Windows: restore travado, `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -p:UsedAvaloniaProducts=` e `dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore --logger trx`.

Linux: SDK oficial 10.0.400 instalado temporariamente no Ubuntu; `dotnet test EsilvaSoft.KapibaraStudio.slnx --artifacts-path <pasta-isolada> -p:UsedAvaloniaProducts= -p:RestoreLockedMode=true --logger trx`. Artefatos locais desta execução: `.cache/linux-validation`, isolados de bin/obj do Windows.

A opção UsedAvaloniaProducts vazia evita somente a tarefa de telemetria de build no ambiente restrito. Não relaxa testes ou analisadores. Os últimos TRX são identificados no [acompanhamento](12-acompanhamento-da-implementacao.md).


## Identidade visual — revisão de 10/09/2026

| Verificação | Windows nesta revisão | Linux nesta revisão |
| --- | --- | --- |
| Restore travado / build | Aprovados, 0 avisos e 0 erros | Não reexecutados: Ubuntu sem SDK .NET 10 compatível |
| NUnit e Headless/Skia | 244/244; contraste, barra nos limites da janela, fonte 20, modal, estado vazio e ícone | Pendente; o 244/244 Linux acima é da baseline anterior |
| Inspeção visual | PNGs reais dos dois temas, 3 tamanhos × 3 escalas; folhas de contato e imagens detalhadas | Pendente |
| Shell / ícone nativo / leitor de tela | Pendente | Pendente |

Assets, reprodução e prévias: [identidade visual](18-identidade-visual.md). Não houve alteração de golden files nem redução dos limites de contraste. O teste detectou 4,497:1 no primeiro azul claro escolhido; a cor foi corrigida para #1B6EDC.



## Credenciais e Ambientes / Key Vault — 10/09/2026

| Verificação | Evidência desta revisão | Limite |
| --- | --- | --- |
| Restore / build Windows | `dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode`; build `--no-restore -p:UsedAvaloniaProducts=`; 0 avisos/erros | Restore precisou de acesso ao NuGet.Config do usuário; nenhuma dependência adicionada |
| NUnit Windows | **259/259 aprovados**, 0 ignorados | TRX: `tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-10_14_34_49_net10.0.trx` |
| Credenciais diretas e opcionais | Duas URIs válidas via parser MongoDB, round-trip LiteDB, Development/Production, caracteres reservados; campos de usuário/senha persistem URI direta | Autenticação contra servidor real pendente |
| Compatibilidade e recuperação | URI legada preservada após reinício, nenhuma migração de secrets; cofre inválido/versão futura não sobrescrito; falha de gravação visível | Criptografia/cofre nativo não implementados |
| Contexto e concorrência | Snapshot mantém valores anteriores; alteração invalida explorer, rejeita abertura antiga e preserva execução em andamento; entradas administrativas capturam ambiente | Retornos usam executores controlados |
| Editor | Valor ENV inserido como texto escapado, sem injeção de campos; ObjectId e Int64 máximo preservados; script com ENV e chave ausente | JSON estrito administrativo mantém suas validações; entrada `slop.input` é literal |
| Bootstrap JavaScript | Node executou o script gerado com stubs em dois ambientes; ordem conectar/banco/emitir, erro de chave ausente e remoção das variáveis de transporte conferidos | Não equivale a mongosh ou MongoDB real |
| UI | 18 PNGs de Ambientes: 2 temas × 3 tamanhos × 3 escalas, inspecionados; sobreposição corrigida e geometria testada; formulário de conexão claro/escuro inspecionado | PNGs em `tests/EsilvaSoft.KapibaraStudio.UnitTests/bin/Debug/net10.0/ui-evidence`; teclado Escape testado, leitor de tela pendente |
| Linux | Não reexecutado nesta revisão | Números Linux anteriores são baseline; homologação desta alteração pendente |

Cofre de ambientes usa armazenamento local sem criptografia nativa, separado dos snapshots. O aceite automatizado prova configuração simultânea e resolução por ambiente; homologação ponta a ponta exige MongoDB/mongosh instalados e uma fixture descartável, conforme o checklist. Nenhuma credencial real foi usada nesta validação.

## Database Explorer — 10/09/2026

Restore travado aprovado; build Windows com `--no-restore -p:UsedAvaloniaProducts=` aprovado, **0 avisos e 0 erros**. Suíte final: **276/276 testes aprovados**, nenhum ignorado. TRX desta revisão (artefato histórico não disponível neste checkout: `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-10_23_11_35_net10.0.trx`). Os totais anteriores são históricos.

| Critério de aceite | Implementação e evidência automatizada | Homologação externa |
| --- | --- | --- |
| 1. Cadastrar/conectar | Formulário/repositório existentes, raízes salvas desconectadas; carga explícita testada | Autenticação MongoDB real pendente |
| 2. Expandir conexão | Bancos sob demanda; teste `SavedConnectionsAppearWithoutNetworkAndChildrenLoadProgressively` | Privilégios reais pendentes |
| 3. Expandir banco | Coleções sob demanda no mesmo teste; nenhuma consulta de documentos ao navegar | Catálogos reais pendentes |
| 4. Consultar documentos | Editor com filtros, sort, projection, limit e páginas; JSON e árvore estruturada renderizados | Paginação contra coleção real pendente |
| 5. Índices/detalhes | Índices compostos, direção, unique, sparse, TTL, partial e definição preservados em `IndexMetadataRetainsCompoundDirectionsAndAdditionalOptions` | Variações de versão MongoDB pendentes |
| 6. Criar/remover índices | Ferramenta gráfica acessada pelo menu real no teste de UI; remoção com contexto e proteção `_id_` testadas | Criação/remoção em servidor pendentes |
| 7. CRUD de documentos | Serviços existentes integrados; releitura completa, precondição e conflito em `EditLoadsFullDocumentAndUsesSnapshotPrecondition`; confirmação explícita | CRUD real e permissões pendentes |
| 8. Scripts CRUD | Namespace escapado; menu real gera sem executar; opções de índices em Extended JSON | Execução mongosh real pendente |
| 9. Menus contextuais | `ContextMenusGenerateScriptsWithoutExecutingAndRenderDocumentTree` abre o menu e aciona scripts/ferramentas | Teclado e leitor de tela nativos pendentes |
| 10. Instância/topologia | Adaptador de hello distingue primary, membros, arbiter e restrições; detalhes cancelam seleção antiga | Replica set, mongos e load balancer reais pendentes |
| 11. Selecionar instância | URI mantém autenticação/TLS; resolução SRV/TXT controlada testada; abas anteriores mantêm destino; recuperação sem conexão automática | DNS, failover e roteamento reais pendentes |
| 12. Múltiplas conexões | Todas as raízes salvas, contexto por aba; alteração de ambiente invalida conexões sem redirecionar execução | Sessão real com múltiplos servidores pendente |
| 13. Refresh granular | `RefreshRetainsExpandedNodesAndDoesNotReloadSiblingConnection`; resposta atrasada após desconectar rejeitada | Latência real pendente |

PNGs reais Headless/Skia inspecionados em **18 combinações** (claro/escuro × 960×620, 1366×768, 1920×1080 × escalas 1, 1,5, 2), além do editor de documento nos dois temas. Evidência em `tests/EsilvaSoft.KapibaraStudio.UnitTests/bin/Debug/net10.0/ui-evidence/explorer-*.png` e `document-editor-*.png`; prévias no [guia](19-database-explorer.md). Testes usam dados sintéticos e executores controlados. Linux, diálogos nativos, leitor de tela e MongoDB/mongosh reais não foram homologados nesta revisão.

## Console JavaScript — 11/09/2026

Restore com `--locked-mode` aprovado; build `--no-restore -p:UsedAvaloniaProducts=` com **0 avisos/erros**; **294/294 testes aprovados**, nenhum ignorado. TRX final (artefato histórico não disponível neste checkout: `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-11_07_55_19_net10.0.trx`).

| Aceite do Console | Evidência |
| --- | --- |
| 1. Conexão principal selecionável | Destino usa os nós conectados por identidade; CanExecute/contexto por aba. ConsoleWorkspaceTests verifica isolamento entre perfis e retorno fora de ordem. |
| 2. Banco principal selecionável | Seletor de banco da conexão; ConsoleWorkspaceTests valida captura antes de alteração do banco e recuperação. |
| 3. db aponta ao banco selecionado | ConsoleMongoIntegrationTests executa db.Customers no CompanyDb e compara os três caminhos de consulta. |
| 4. getConnection acessa conexões cadastradas | Runtime real usa snapshots dos perfis; testes cobrem duas conexões, nome inexistente/ambíguo tratado pelo proxy e URI não utilizada inválida. |
| 5. ConnectionPool navega conexão/banco/coleção | Runtime tests exercitam propriedades e indexadores com espaços/hífens; integração real compara os documentos retornados. |
| 6. ENV.get funciona | Snapshot permanece estável durante alteração; integração real insere Name via ENV e consulta o documento correspondente. |
| 7. Autocomplete contextual | ConsoleWorkspaceTests cobre sugestões globais, conexões, bancos e coleções, indexadores e requisição assíncrona suspensa; view rejeita texto/destino antigos. |
| 8. Queries no painel | ConsoleUiTests executa JavaScript pelo editor e verifica dois resultados; PNGs reais inspecionados nos dois temas. |
| 9. console.log em Mensagens | Verificado no runtime, interface e fixture real; warn/error usam o mesmo canal com identificação. |
| 10. Cancelamento | Token atinge operação de banco controlada, abas permanecem independentes; loop limitado; confirmação negada por Escape não envia escrita. |
| 11. Histórico | Persistência LiteDB verificada para sucesso/erro/cancelamento, destino capturado e conexões usadas; opt-out não grava; metadados não incluem resultados/URI. |
| 12. Explorer gera nova sintaxe | Menu real gera Console sem execução; abrir coleção produz find limitado; scripts CRUD/índices usam o db da aba. |

Cobertura adicional: migração de rascunho JSON preservando opções e destino; proteção somente leitura aplicada à conexão adicional; rejeição de filtro vazio, _id_ e $out/$merge; seleção por parser sem fallback para script completo; objetos host não expostos. Testes anteriores do runner mongosh mantidos explicitamente nesse modo, sem relaxar sua política de somente leitura.

**MongoDB real:** ConsoleMongoIntegrationTests inicia dois processos Community **8.0.30**, apenas em 127.0.0.1, com diretórios próprios e dados sintéticos. Exercitados insertOne/Many, find/One, count/estimated, distinct, aggregate, sort/skip/limit/project, updateOne/Many, replaceOne, deleteOne/Many, create/dropIndex, múltiplas conexões e confirmação. Tipos ObjectId, UUID legado, Int64 máximo, Decimal128 e data conferidos no caminho real. Processos encerrados ao final; nenhuma conexão do usuário acessada. Binário portátil em .cache, fora da distribuição.

**Visual:** 18 PNGs Console (2 temas × 960×620/1366×768/1920×1080 × 100/150/200%) inspecionados, além das evidências regeneradas do Explorer/documentos. [Guia e prévias](20-console.md). Esta revisão não homologa Linux nativo, leitor de tela, TLS/autenticação, topologias distribuídas ou mongosh real.


## UUID/GUID configurável — 11/09/2026

Build Windows `--no-restore -p:UsedAvaloniaProducts=` com **0 avisos/erros**; **369/369 testes executados aprovados**, 1 ignorado (MongoDB real ausente). TRX final — `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-11_14_50_37_net10.0.trx` (artefato histórico ausente neste checkout; resultado preservado como registro, não revalidado). Linux não reexecutado nesta revisão.

| Aceite | Evidência automatizada | Validação externa |
| --- | --- | --- |
| Bytes por representação | `UuidCodecTests`: fixtures hexadecimais independentes para Standard, C# legacy, Java legacy e Go/Standard; cruzamento com `GuidRepresentation` do driver | Pendente: `ConsoleMongoIntegrationTests` grava os quatro construtores e confere bytes/subtype (ignorado sem `SLOP_CONSOLE_MONGOD`) |
| Ida e volta, zero, caixa, inválido | Construtor → BSON → saída → construtor, UUID zero, maiúsculas/minúsculas, sem hífens; inválidos com linha/coluna no codec, Console, adaptador e importação | — |
| UUID em string e subtype 3/4 | Strings, regex, comentários e acesso a membro intactos; subtype 4 nunca vira legado; subtype 3 sem perfil = origem desconhecida | — |
| Coleções mistas e consulta explícita | Documento misto em C#/Java; Console envia filtros com representação escolhida; integração real conta `CGUUID` 1 e `JUUID` 0 | Pendente com servidor real |
| Conexões simultâneas | `ConnectionsWithDifferentPoliciesRunConcurrentlyWithoutInterference`: duas abas, conclusão fora de ordem, snapshot capturado, mudança durante execução, global sem sobrepor conexão | — |
| Console, Documentos, clipboard, snippets | Metrics/Resultados/árvore, área de transferência Headless, editor de documento, “Abrir no editor” executado no Console com bytes idênticos, **Gerar UUID**, exportação canônica | Área de transferência nativa e leitor de tela pendentes |
| Migração e falha | Sessão sem campos → Standard; valor desconhecido visível e não sobrescrito; falha de gravação visível, aplicada em memória e repetível | — |
| mongosh | Helpers executados com stubs no Jint e comparados byte a byte com o codec | mongosh instalado pendente |
| UI | 54 PNGs `uuid-*`: Documentos, Preferências e editor de conexão × 2 temas × 3 tamanhos × escalas 1/1,5/2; geometria do seletor e do botão Salvar verificada; amostras inspecionadas | Gerenciadores de janela reais pendentes |

## Resultados JSON e árvore — 11/09/2026

Build Windows `--no-restore -p:UsedAvaloniaProducts=` com **0 avisos/erros**; **414 testes aprovados, 0 falhas, 1 ignorado** (MongoDB real ausente), total 415. Baseline imediatamente anterior: 369 aprovados e 1 ignorado. TRX final — `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-11_16_08_39_net10.0.trx` (artefato histórico ausente neste checkout; resultado preservado como registro, não revalidado). Linux não reexecutado nesta revisão.

| Aceite | Evidência automatizada | Validação externa |
| --- | --- | --- |
| Alternar JSON/árvore sem perder documentos, origem ou seleção | `SwitchingViewsKeepsDocumentsOriginAndSelectionAcrossResultSets`; teste de UI troca pelo seletor real e mantém o documento selecionado | — |
| Expandir/recolher documentos, objetos e arrays | `TreeExpandsObjectsAndArraysOnDemandWithNamesIndexesTypesAndValues`: carga sob demanda, 40 níveis, índices `[i]`, recolher sem recarregar | Árvores muito grandes sem virtualização |
| Campos ausentes, `null`, nomes com ponto, arrays vazios, estruturas profundas | Mesmo teste; formatador com 100 níveis; documento sem `_id` | — |
| Preservação de tipos | `ExtendedJsonPresentationTests`: fixture escrita à mão conferida pelo parser do driver; ObjectId, Int32, Int64 máximo, Double `-0.0`, Decimal128, data, binário, regex, timestamp e MinKey; UUID Standard, C# legacy, Java legacy, Go e Python legacy (subtype 3 de origem desconhecida) com bytes idênticos após reescrever os construtores | Fixture MongoDB real pendente |
| Documento único, múltiplos documentos, múltiplos conjuntos, truncado | Testes do painel e de UI com documento único aberto por padrão, três documentos, dois conjuntos, `limitado` e projeção parcial | — |
| Menu por mouse e teclado | UI: botão direito sobre o item, Shift+F10 e tecla Menu na árvore e no JSON (documento no cursor); motivo textual quando a edição está indisponível | Leitor de tela pendente |
| JSON somente leitura e cópia formatada | UI abre a modal pelo menu, confere origem, destino e texto, copia e fecha por Escape | Área de transferência nativa pendente |
| Edição: cancelar, alterado, removido, sem `_id` | `EditorOpensTheCopyWithoutIoAndRereadsTheDocumentBeforeWriting` (inalterado, alterado, removido), `ReadOnlyOrClosedConnectionOpensTheCopyButBlocksSaving`, `EditAvailabilityRequiresIdentityAndCompleteCollectionDocuments`; Escape na modal de UI | Gravação e conflito contra servidor real pendentes |
| Nada executado implicitamente | Chamadas ao serviço Mongo e ao runner contadas antes e depois de menus, visualização, edição e cancelamento | — |
| Isolamento entre abas e respostas fora de ordem | `TabsKeepTheirOwnResultsViewAndSelectionWhenResponsesArriveOutOfOrder`; troca de destino limpa só a aba; snapshot sem resultados | — |
| Ordem de campos do driver no Console | `ResultsKeepDriverFieldOrderAndReportProjectionUnlessTheScriptChangesThem` | — |
| Renderização | 72 PNGs: JSON e árvore (960 × 620, 1366 × 768, 1920 × 1080), modal JSON e edição (600 × 420, 760 × 560, 900 × 650) × claro/escuro × 100/150/200%; geometria do seletor, de Copiar JSON e das ações das modais; amostras inspecionadas | Gerenciadores de janela reais pendentes |

Asserções alteradas: `WorkspaceUiTests` e `UuidPreferencesUiTests` passaram a esperar `"campo": valor`, porque Resultados agora é indentado. A asserção negativa de vazamento entre abas usa o mesmo formato, para não passar trivialmente. Nenhum golden file foi alterado.
## Autocomplete local Qwen — 11/09/2026

| Gate | Evidência atual | Limite |
| --- | --- | --- |
| Restore/build | Restore travado e build Windows com `-p:UsedAvaloniaProducts=`, 0 avisos/erros | Opção evita somente a tarefa externa de telemetria Avalonia |
| Suíte regular | **415 aprovados**, 0 falhas; TRX (artefato histórico não disponível neste checkout: `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/autocomplete-current-final.trx`) | Dois testes Qwen são Explicit e executados à parte |
| IA real CPU | **2 aprovados**; TRX (artefato histórico não disponível neste checkout: `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/qwen-current-final.trx`) | Qwen2.5-Coder-0.5B Q4, CPU Windows x64; não generaliza qualidade/performance a modelos maiores |
| FIM/tokenizer | Tokenizer nativo + FIM gerou `a + b`; código sintético avaliado com resultado 5 | Teste fixo, não benchmark de qualidade |
| Cancelamento nativo | Cancelar geração longa, recuperar sessão e gerar novamente | Inicialização nativa não é imediatamente interrompível; worker mantém UI livre |
| Contexto/concorrência | AutocompleteReliabilityTests: 1 inferência ativa, fila cancelada não executa, duas sessões independentes; limite antes do runtime | Fakes determinísticos, complementados pelo teste nativo de cancelamento |
| Fallback/cache | LocalAutocompleteTests: AI/Auto → básico, cooldown, sem carga desabilitado, cache 64/expiração, ausência/invalidez, privacidade | Não promete reconhecer segredos arbitrários |
| Persistência/recuperação | Preferências antigas/defaults, round-trip, falha visível sem aplicar configuração não salva, sessão corrompida/futura não sobrescrita | Mesmo proprietário LiteDB, nenhuma segunda conexão |
| UI | Tab/Escape, preview multiline, 18 PNGs de editor + 18 de preferências + 2 Qwen reais inspecionados | Headless/Skia; leitor de tela e diálogos nativos não homologados |
| Publicação | Self-contained win-x64/win-arm64/linux-x64/linux-arm64; DLLs Windows x64/ARM64 corretas e sem pesos | Cross-publish não comprova execução em Linux ou hardware ARM64 |
| Aceleração | CPU distribuído; detecção/seleção DML/CUDA preparada, CPU fallback | DML/CUDA sem binários/homologação; OpenVINO/QNN/NPU não implementados |

Pesos de teste externos ao repositório; nenhuma dependência Python/Ollama/servidor. [Modelo, revisão, hash e receita de instalação](21-autocomplete-local.md#receita-reproduzível-validada-para-05b-q4).

## Autocomplete preditivo — auditoria de aceite (11/09/2026)

Esta revisão substitui as limitações anteriores de preview abaixo do editor e contexto somente textual. Restore `--locked-mode` aprovado; build `--no-restore -p:UsedAvaloniaProducts=` com 0 avisos/erros; **431 testes regulares aprovados**, 0 falhas (TRX (artefato histórico não disponível neste checkout: `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/predictive-final.trx`)). **2 testes Explicit reais aprovados**, separados da suíte comum (TRX (artefato histórico não disponível neste checkout: `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/predictive-real-final.trx`)).

| Requisito | Implementação e evidência conferida |
| --- | --- |
| Modelo preparado e reutilização | LocalModelCatalog + registro singleton de serviço/provider; modelo em `F:\models\Qwen2.5-Coder-0.5B-onnx-int4-cpu`; RealQwenGeneratesWithCpuAndReusesNativeSession gera, cancela e volta a gerar na sessão nativa |
| Carregamento desacoplado e UI responsiva | OnnxLocalModelRuntime executa carga/geração em Task.Run, provider serializa por SemaphoreSlim; real Qwen pelo editor com dispatcher ativo |
| Geração automática | EditorCompletionChanged observa texto/cursor/seleção; RealQwenCompletionTravelsThroughEditorAndAcceptsTab obtém `a + b` sem botão de sugestão |
| Ghost text sem editar documento | InlineCompletionTextBlock projeta prefixo/inserção/sufixo no layout real; PendingContextChangesAreRejectedAndGhostTracksTheCaretWithoutEditingTheDocument verifica documento intacto, sufixo, posição e rolagem |
| Tab incremental e Escape | IncrementalCompletion, testes por caminho/expressão/aspas/CRLF; teclado real Headless aceita partes, conserva resto e descarta por Escape; sem preview permanece o handler normal |
| Digitação, Backspace e contexto obsoleto | CompletionSession por editor com versão/CTS; testes de debounce, ABA, resposta tardia após mudar Input, Backspace, desabilitação, concorrência e cancelamento nativo |
| DSL e linguagem atuais | Context Builder usa ConnectionPool e getDatabase no Console, comandos mongosh em Script e operadores/stages em Agregação; CommandContextRespectsTheEditorLanguage |
| Dicionário antes de IA | DictionaryPrecedesModelAndDoesNotWaitForDebounce comprova resposta síncrona e zero carga ONNX; UI completa Conn para ConnectionPool |
| Input e Resultados temporários | ContextIsBoundedKeepsCursorAndHonorsIndependentOptions e ResultFieldsAndInputStayInsideTheirOriginatingTab; campos de documento real da fixture, sem valores, sem atravessar abas/destinos |
| Nomes e histórico | Snapshot copia nomes dos perfis, nós já carregados da conexão da aba, destino e até três comandos da mesma conexão/banco; não consulta MongoDB a cada tecla |
| Contexto limitado | Janelas de caracteres e limites auxiliares em AutocompleteContextBuilder/Bounded; orçamento final em tokens nativos no QwenFimPromptBuilder; testes de limite e preservação junto ao cursor |
| Fallback e cache | Ausência/invalidade/falha, cooldown e ausência de carga desabilitado; cache 64/30s com chave do request completo; CacheIncludesAuxiliaryContextAndDictionaryCanBeDisabled |
| Preferências | Modal existente ampliada; campos novos aditivos, round-trip dos cinco flags desligados, defaults de sessão antiga e erro de persistência sem aplicar configuração não salva |
| Inferência curta | 32 tokens padrão; sufixo existente interrompe geração e é removido da inserção; SuffixEchoIsNotInsertedTwice e integração real |
| Arquitetura/extensibilidade | Contratos e DI existentes preservados; nenhuma dependência ONNX em editor/Core; runtime substituível por fábrica, catálogo/tokenizer/prompt separados |
| Documentação | Guia 21, guia de uso, design system, ADR-030, plano, catálogo e acompanhamento atualizados |

Inspecionados PNGs reais de 18 combinações do editor e 18 das preferências (claro/escuro, três tamanhos, escalas 100/150/200%) e duas imagens de inferência Qwen. A projeção respeita o fundo do template focado e recorte do viewport. Evidência em `tests/EsilvaSoft.KapibaraStudio.UnitTests/bin/Debug/net10.0/ui-evidence/autocomplete-*.png`. Não se alteraram golden files. Asserções antigas de Tab integral foram substituídas por verificação de inserção parcial, resto disponível e reconstrução integral; testes de falha agora iniciam a IA com prefixo sem resposta óbvia do dicionário, preservando a verificação de cooldown/fallback.

Limites: execução comprovada em Windows x64 CPU. Headless/Skia não homologa leitor de tela, IME, gerenciador de janela nativo ou Linux/ARM64. 150 ms é debounce, não promessa de latência total; qualidade e duração variam conforme contexto/hardware. Resultados contribuem com campos de amostra limitada, não treinamento. Publicações descritas na seção anterior correspondem ao incremento anterior, não foram refeitas nesta revisão preditiva.


## Syntax highlighting MongoDB — 12/09/2026

Windows x64 / .NET 10.0.401 / Avalonia 12.1.2. Restore com --locked-mode aprovado, sem alterar dependências/lockfiles. Build com --no-restore -p:UsedAvaloniaProducts= aprovado, zero avisos/erros. Suíte completa com --no-build --no-restore: **453 aprovados, zero falhas**, em 34 s. O runner informou duas integrações Qwen opt-in não executadas; elas não integram a contagem de testes aprovados. TRX local: tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/syntax-validation.trx. Após o ajuste visual final do fundo focado, novo build aprovado e os **4 testes de UI de highlighting/autocomplete** passaram em 15 s.

| Cenário | Evidência |
| --- | --- |
| JSON, números, booleanos, null, propriedades/strings e Extended JSON | SyntaxHighlightingTests preserva texto e dígitos Int64, reconhece wrappers/construtores |
| Query API, pipelines e Atlas Search | Casos de find, $gte, $group/$sum, distinção de $set em update/pipeline, operadores Search apenas no contexto |
| DSL e namespaces | Caminhos ConnectionPool/ConnectionPull e getConnection; nomes conhecidos por caminho, fallback sem metadados e índice conhecido |
| Delimitadores e texto não executável | Pares excluem strings/comentários/regex e identificam delimitador sem par |
| Incremental e cancelamento | 5.000 linhas com edição local reutilizam sufixo; abertura/fechamento de comentário propaga estado; snapshots independentes |
| UI e documento extenso | Trabalho obsoleto cancelado; 4.000 linhas com spans de região distante limitados; não comprova virtualização do TextBox |
| Temas, seleção e undo | Troca Dark/Light reutiliza snapshot, contraste >= 4,5:1 para todos os tokens, seleção preservada, edição e Ctrl+Z reais Headless |
| Visual | **36 PNGs reais inspecionados**, workspace 960/1366/1920 e modal 600/760/900, escalas 100/150/200%, ambos os temas; fundo focado final revalidado |
| Regressões | Suíte existente de autocomplete/resultados e demais jornadas aprovada; sem alteração de golden files |

Prévias duráveis: [claro](ui/syntax-claro.png), [escuro](ui/syntax-escuro.png). Arquitetura, tokens, extensão e limitações em [22](22-syntax-highlighting.md). A classificação não executa MongoDB nem altera BSON/sessão. Homologação Linux, leitor de tela, IME nativo e MongoDB/mongosh real não foi realizada nesta entrega. O TextBox continua medindo todo o documento; não há promessa de responsividade constante para arquivos ilimitados.

## Modos de identificador — 12/09/2026

Build Windows `--no-restore -p:UsedAvaloniaProducts=` com **0 avisos/erros**; **504 aprovados, 1 falha, 1 ignorado**. TRX — `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/identifier-mode-final.trx` (artefato histórico ausente neste checkout; resultado preservado como registro, não revalidado). A única falha (`HugeSingleLineKeepsFullTextAndMakesEveryRangeReachableWithoutShapingItAll`, syntax highlighting) reproduz no commit `f8e7a16` sem estas alterações. Linux não reexecutado.

| Aceite | Evidência automatizada | Validação externa |
| --- | --- | --- |
| Opções ObjectId, UUID v4 e Standard; Standard ≠ UUID v4 | `ModesAreThreeNamedValuesAndStandardMeansObjectIdPlusUuid`: três valores serializados por nome, sem `StandardUuid`, rótulos e descrições por modo | — |
| Standard aceita e preserva ObjectId e UUID | `BsonTypeHasPriorityAndLookalikeStringsStayStrings`, `HumanJsonUsesConstructorsForBothTypesAndRestoresIdenticalBson` (três modos, BSON idêntico após reescrita), strings com cara de identificador intactas | — |
| ObjectId → UUID determinística, sem alterar BSON | Fixture independente `66e3bd5b-3bc3-f54c-840d-73ac00000000`, reversão e caixa; `ModeChangeRerendersResultsButEditorCopiesAndExportKeepTheStoredTypes`: texto do editor, gravação (`BsonType.ObjectId`), precondição e exportação canônicas | Pendente com servidor real |
| UUID subtype 4, CGUUID, JUUID, GUUID | Suítes UUID existentes aprovadas; parsing comparado a `BsonBinaryData` do driver; placeholders `CGUUID`/`JUUID` | — |
| Configuração separada e migração sem perda | Sessão sem `IdentifierMode` com `UuidRepresentation` Standard e JavaLegacy → modo Standard e representação preservada; valor desconhecido visível e não sobrescrito; falha de gravação visível | — |
| Parsing e formatação centralizados | `ParseIdentifierAcceptsRequiredFormsInEveryModeWithDriverIdenticalBson`, inválidos com motivo e linha/coluna; UI, adaptador, Console e importação chamam o serviço | — |
| Visualizadores, cópias e scripts | Árvore, identidade, Documentos, JSON, editor, `EJSON.parse` do Console, importação, `_id` de exemplo do Explorer, **Gerar identificador**, **Interpretar**; menu com cópia de `_id`, consulta e UUID equivalente via área de transferência Headless | Área de transferência nativa pendente |
| Preferências e prévia | `IdentifierModesDriveResultsMenuAndPreferencesAndRenderInEveryRequiredVariant`: descrição, seções ObjectId/UUID e comparação das quatro formas por modo; 18 PNGs `identifier-results-*` e 54 `identifier-preferences-<modo>-*` × claro/escuro × 3 tamanhos × 100/150/200%; geometria do seletor e da descrição verificada | Leitor de tela e gerenciadores de janela reais pendentes |

## ONNX SlopCoder, hardware e chat — 13/09/2026

Windows x64, .NET 10.0.401: restore `--locked-mode` e build `--no-restore -p:UsedAvaloniaProducts=` aprovados nas variantes Cpu, WinML e Cuda (0 avisos/erros). Suíte final padrão WinML: **520 aprovados, 1 falha** em 37 s; TRX (artefato histórico não disponível neste checkout: `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/onnx-expanded-final.trx`). A falha é `HugeSingleLineKeepsFullTextAndMakesEveryRangeReachableWithoutShapingItAll`, já registrada na entrega anterior de identificadores; asserção e golden files não foram alterados. Seis testes nativos Explicit ficam fora da suíte regular.

| Verificação | Resultado e evidência |
| --- | --- |
| Tokenizer e prompt DeepSeek | 547 strings e 40 prompts completos com IDs idênticos; decode também confere os três casos sem round-trip do HF. TRX CPU (artefato histórico não disponível neste checkout: `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/deepseek-cpu-final.trx`) |
| SlopCoder real CPU | Geração repetida, cancelamento e recuperação; 12 tokens em 1168/1146 ms em prompt curto sintético. Mesmo TRX; não é TTFT nem benchmark do editor |
| Chat e GPU → CPU | Proposta ONNX real e recuperação depois de erro DirectML aprovadas. TRX (artefato histórico não disponível neste checkout: `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/deepseek-chat-fallback.trx`) |
| GPU estrita | Falhou na execução do pacote CPU (`DmlFusedNode_0_4`, `80070057`). TRX (artefato histórico não disponível neste checkout: `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/deepseek-gpu.trx`). Não há homologação GPU deste pacote; CUDA só restaurado/compilado |
| Contratos/falhas | Manifesto com ID incorreto e pesos externos ausentes retornam Invalid; propostas truncadas e marcadores são rejeitados; chat cancelado não cancela autocomplete em fila; sessão inicializada uma vez; filtros de privacidade impedem carga |
| Regressão visual | Suíte Headless produziu PNGs existentes de autocomplete; inspecionados claro/escuro 1366×768 100%, ghost text e painel IA. Não houve mudança de layout/XAML |

O chat FIM gerou filtro de data adicional não solicitado: integração funcional não representa aprovação de fidelidade conversacional. Revisão e confirmação permanecem obrigatórias. Linux, GPU com exportação compatível, leitor de tela, diálogos nativos e MongoDB real não foram homologados nesta entrega. [Guia e contratos](23-onnx-slopcoder.md).

Regressão Qwen real no build WinML: **1 aprovado**, geração CPU repetida e recuperação de cancelamento; TRX (artefato histórico não disponível neste checkout: `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/onnx-qwen-regression.trx`).


## Datas BSON — 13/09/2026

Restore locked aprovado; build sem restore com -p:UsedAvaloniaProducts= aprovado, 0 avisos/erros. Suíte final: **530 aprovados, 1 falha preexistente** em HugeSingleLineKeepsFullTextAndMakesEveryRangeReachableWithoutShapingItAll (já registrada na entrega ONNX). TRX: tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/date-format-final.trx.

Novos testes verificam bytes BSON com fixture do driver, UTC e offset -03:00, milissegundos negativos/zero, datas inválidas, limite Int64, strings preservadas, argumentos BSON enviados pelo Console e helper mongosh executado em Jint. Resultado visual confirmado na modal de documento: Date legível nos PNGs reais result-document-json-Light-760-1.png e result-document-json-Dark-760-1.png em ui-evidence do diretório de testes. Os testes existentes de resultados/cópia e representações também passaram.

Não homologado nesta alteração: MongoDB/mongosh real, Linux, leitor de tela e área de transferência nativa. Exportação permanece canônica.

## Revisão documental do roadmap — 13/09/2026

Inspeção estática histórica do checkout `e806ae4`, serviços concretos, chamadas de UI, testes, dependências e tags locais. [Inventário](24-inventario-roadmap.md) associa status a código e lacunas; o [roadmap](09-plano-de-implementacao.md) corrente organiza oito fases. Não houve execução de restore/build/NUnit, MongoDB, mongosh, modelos ou homologação visual naquela tarefa somente de Markdown. As contagens daquele registro permanecem históricas, inclusive a falha registrada; não são o resultado do checkout atual.

Verificação documental: 68 requisitos originais preservados e EDT-08 acrescentado; status e versão nas 69 definições; oito fases com objetivo, escopo, exclusões, aceite, dependências e documentação. Links locais, tabelas e blocos de código conferidos. Três links antigos para TRX ausentes foram convertidos em referências textuais explícitas, preservando o registro sem oferecer download inexistente. Referências comparativas removidas conforme o escopo; não houve auditoria jurídica de terceiros nem alteração de avisos/licenças.

## Homologação nativa Windows — 14/09/2026

| Cenário | Evidência | Estado |
| --- | --- | --- |
| Conectar e navegar | Perfil `MvpNative` em MongoDB 8.0.30 local; databases `admin`, `config` e `local` carregados após expansão | ✅ Homologado |
| Consulta e barra inferior | `system.version` retornou 1 documento; barra mostrou execução, cancelamento disponível e conclusão | ✅ Homologado |
| Exportação JSON | Seletor nativo salvou Extended JSON válido | ✅ Homologado |
| Exportação CSV | Seletor nativo salvou cabeçalho `_id,version` e linha `featureCompatibilityVersion,8.0`; extensão digitada prevalece sobre filtro inicial | ✅ Homologado |
| Cancelamento de consulta | Ação **Cancelar** da barra inferior acionada durante execução; IDE retornou a `Pronto` e informou que efeitos já enviados não são revertidos | ✅ Homologado |
| Cancelamento de exportação | Testes de streaming cancelam após escrita parcial, propagam `CancellationToken` e removem o arquivo incompleto | ✅ Homologado |
| Linux visual | Validação visual dispensada por decisão de escopo | ⏭️ Fora do aceite |
| Clipboard Windows | Botão **Copiar JSON** retornou o documento completo no clipboard nativo, sem credenciais | ✅ Homologado |
| Leitor de tela/cold start CPU-RAM | Leitor de tela não executado; startup/CPU-RAM possui uma amostra nativa | ⚠️ Parcial |
| Medição de inicialização Windows | 1.396 ms até janela; 220,3 MiB e 3.734,4 ms de CPU após 2 s, uma amostra sem inspeção visual | ⚠️ Medição inicial |

## Polimento MVP — 13/09/2026

Restore locked e build sem restore com `-p:UsedAvaloniaProducts=` aprovados, zero avisos/erros. Suíte `mvp-polish.trx`: **549 testes executados/aprovados, zero falhas**; 555 descobertos, seis casos explícitos de IA fora desta execução/MVP. Arquivo local: `tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/mvp-polish.trx`.

Inclui dois testes de integração com MongoDB portátil 8.0.30 em Windows, paginação/CRUD protegido/conflito/exportação, 18 PNGs da barra concorrente nos temas/tamanhos/escalas declarados, undo real de formatação e correção do teste de linha com dois milhões de caracteres antes falho. Nenhuma asserção de limite visual foi enfraquecida. Startup Headless quente: 9 ms na última execução, 464 ms em execução anterior isolada; cenário de duas páginas de 100 em 5.000 documentos com edição/conflito/exportação: 55 ms (anterior 84 ms). Esses números são observações locais, não benchmark de produção.

Não encerrados: Linux gráfico, diálogos nativos/clipboard, leitor de tela, cold start/CPU/RAM nativos e matriz ampliada de autenticação/topologia. WSL sem distribuição disponível; acesso Linux solicitado. Veja a [auditoria completa e o inventário anterior às alterações](done/release_v0.5.0/25-auditoria-mvp-performance.md). Não declarar v0.5.0 pronta por esses testes.

## IA local multimodelo — 13/09/2026

| Critério | Evidência |
| --- | --- |
| Suíte regular | **564 aprovados, 0 falhas, 2 ignorados**; `multimodel-regular.trx` em TestResults do projeto de testes |
| Diretório, descoberta e validação | `DiscoveryUsesFolderNamesOptionalMetadataAndIsolatesInvalidFolders`: nomes de pasta, metadata opcional, `MissingFiles`, `Invalid` e `Unsupported` sem afetar os válidos |
| Seleção e persistência | `SelectedModelIsAFolderNameThatCannotEscapeTheDirectory`, `SelectionResolvesInsideTheDirectoryAndChatCanUseItsOwnModel`, `PreferencesStoreFolderNamesAndRefreshKeepsTheSelection`, round-trip em LiteDB |
| Hardware e fallback | `AutomaticTriesNpuGpuThenCpuAmongAvailableAndCompatibleDevices`, `ExplicitHardwareUsesOnlyThatBackendAndExplainsUnavailability`, `ProbeOffersOnlyProvidersTheRuntimeReportsAndPrefersTheFirstAdapter` |
| Serviço central | Troca com descarga prévia, capacidade de chat, preempção do autocomplete, carga não abortada pelo editor, barra de atividades, teste de modelo e falha de provider |
| Real nesta máquina (WinML, Ryzen 9 7900, RX 7800 XT) | Sonda, teste de modelo CPU e Automático e geração CPU aprovados com SlopCoder-Mongo-1.5B; GPU explícita falhou na geração DirectML e reportou sem fallback. `multimodel-real.trx` |
| Não homologado | NPU, CUDA, Linux, diálogo nativo de pasta, uso interativo com MongoDB real; GPU para este pacote |

[Especificação e evidência](26-ia-local-multimodelo.md#evidência--13092026).


## Revisão do plano de autocomplete — 15/09/2026

Revisão documental, sem alteração de código de produto: inspeção estática e pesquisa técnica; validação de links/índice offline registrada ao concluir. Nenhum build/teste de produto, MongoDB real, modelo/CPU/GPU/NPU, teclado/IME ou PNG novo é alegado. Fase 1 continua com aceite parcial; schema learning, quatro providers, flags/atalhos e novos gates de concorrência/persistência/UI estão planejados, com casos em execution-plan/testing. [Plano revisado](auto-complite/README.md), [tarefas por agente](auto-complite/execution-plan.md) e [schema learning](auto-complite/schema-learning.md).

## Autocomplete tradicional — fechamento de lacunas (17/09/2026) — CONCLUÍDO

> Encerrada por decisão do responsável com pendências aceitas em aberto. As linhas marcadas como pendentes abaixo
> **não foram verificadas** e não devem ser lidas como aprovadas.

Build `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore`: **0 avisos, 0 erros**.
Testes `dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore`: **1 156 aprovados, 0 falhas** (entrada: 1 127).

| Área | Evidência local | Validação pendente |
| --- | --- | --- |
| Lex único por requisição | `ShapeWalkerTokenSourceTests.WalkUsesTheTokensItReceivesInsteadOfLexingAgain` | — |
| Estreitamento em pipeline nu | `ShapeWalkerTokenSourceTests.RootShapeAppliesOnlyWhenThereIsNoEnclosingCall` | — |
| Vírgula devolve o quadro da chave | `ShapeWalkerTokenSourceTests.CommaReturnsToTheEnclosingObjectShape` | — |
| Acesso por gatilho (`Invoked` carrega, digitar não) | `TraditionalCompletionIntegrationTests`, testes do motor | — |
| Campos do pipeline por `LocalSchemas` | `PipelineStageReader` + testes de contexto | Homologação com coleção real |
| Descarte de resposta obsoleta e CTS por aba | `TraditionalCompletionIntegrationTests` | — |
| Sinal de uso ponta a ponta | `TraditionalCompletionUsageSignalTests` (6 testes) | — |
| Isolamento do núcleo (sem IA, sem rede, sem UI) | `AutocompleteArchitectureTests` (6 testes, agora varrendo todos os subespaços) | — |
| Alocação ≤ 64 KB por tecla, catálogo embarcado | `NameTableAllocationTests`; ver [performance](auto-complite/performance.md) | — |
| **Alocação ≤ 64 KB por tecla, campos de metadados** | **estourado: 174,72 KB a partir de ~1 000 campos** | **`MetadataCatalogSource.Describe` por candidato; não corrigido** |
| **Latência p95/p99 dos gates da Fase 2** | **nenhuma** | **Job completo do BenchmarkDotNet; não concluído nesta sessão** |
| `TypeMismatchPenalty` | nenhuma | Exige propagar `ApplicableTypes` ao `CompletionItem` |
| Elo `Stage → GroupBody` | nenhuma | `ShapeWalker.Match` ignora `Exclusive`/`rule.Names` |
| Matriz de 18 PNGs (claro/escuro, 4 estados) | nenhuma | Fora do escopo desta entrega |
| Highlighting em 64 KiB | 3,5 ms medidos contra orçamento de 2 ms | Não corrigido |
| MongoDB real, leitor de tela, diálogos nativos | nenhuma | Homologação manual pendente |

## Orçamento de tokens por hardware e modelo — 20/09/2026

| Área | Evidência automatizada | Validação pendente |
|---|---|---|
| Tiers de VRAM | `AiHardwareTierTests`: limites 2/4/8/12/16/24 GiB e RTX 2060 6/12 GiB | GPUs físicas adicionais |
| Limites do modelo | `LocalModelCatalogTests.ModelExposesContextWindowAndAutocompleteGenerationLimit`, `ModelMetadataAcceptsDeclaredBudgetsAboveLegacyLimits`; fallback 8192/256, origem/confiança e limite inclusivo | Exportações de cada família de modelo |
| Perfis manuais e persistência | `AutocompleteSettingsViewModelTests.ManualHardwareProfileDrivesSuggestionsAndPersistsAsAdditiveJson` | Uso interativo da modal em Windows/Linux |
| Orçamento combinado | Validação do ViewModel com contexto + saída + overhead, valores inclusivos e clamp no provider | Memória livre real do provider/driver |
| UI | `AutocompleteUiTests.TokenBudgetComboBoxesKeepSelectionsAndFreeTypedValuesWhenSaved`; seleção exata de 32, somente dígitos e PNGs Dark/Light existentes | Inspeção manual adicional da composição em Windows/Linux |
| ONNX real | `LocalAiModelServiceTests.RealModelTestRunsOnTheRequestedHardware`: Automático e GPU aprovados para SlopCoder DML FP16; CPU recusada corretamente porque o metadata declara GPU-only | Outras exportações/providers |
## Fase 4 — arquivos de texto e workspace local (implementada; homologação nativa pendente)

| Área | Critério observável | Evidência automatizada esperada | Homologação ainda necessária |
| --- | --- | --- | --- |
| Leitura e codificação | Arquivos vazios e extensões desconhecidas abrem como texto; UTF-8/16/32 e BOM preservados; binário/codificação inválida falham visivelmente | Round-trip de bytes, BOM, Unicode e quebras de linha; limite de 16 milhões de caracteres | Arquivos reais em Windows/Linux e permissões do sistema |
| Salvamento | Salvar e Salvar como preservam a aba, detectam alteração externa e não truncam destino em falha | Temporário no mesmo diretório, sobrescrita, cancelamento, erro e edição durante salvamento | Diálogo nativo e comportamento com antivírus/arquivos bloqueados |
| Painel Arquivos | Barra lateral acessível com **Conexões** e **Arquivos**; raiz única, pastas antes de arquivos, carregamento sob demanda e erro recuperável | Trocas rápidas de raiz, descarte de respostas antigas, caminhos equivalentes e ausência de execução automática | Navegação nativa, foco, teclado e leitor de tela |
| Operações de workspace | Criar, renomear e excluir com colisão/raiz protegidas; exclusão usa lixeira | Fixtures independentes para arquivo/pasta, atualização após sucesso e buffers sem caminho após exclusão | Lixeira real disponível/indisponível em Windows e Linux |
| Sessão e privacidade | Migração aditiva v1→v2; raiz e documentos recuperáveis; resultados/credenciais fora do snapshot | Migração, sessão ilegível/proteção, opt-out e recuperação de buffer alterado | Reinício real, mudança de perfil e homologação prolongada |

Os testes automatizados cobrem os critérios de implementação. Teste Headless não substitui diálogos nativos, lixeira, acessibilidade ou leitor de tela; esses itens continuam como homologação externa.

## v0.11.0 — matriz futura de MCP e agentes externos

**Status: 🚧 Em desenvolvimento inicial.** Há spikes do lote 0, adapters de cofre isolados do lote 1 e política/registry interno parcial do lote 2; nenhum teste de integração externa foi aprovado, e MCP, runtime, providers, chat e tools expostas permanecem não entregues. O [plano técnico](phases/phase-07-v0.11.0/README.md) especifica os casos e critérios de aceite.

| Camada | Evidência futura | Ambiente / limite |
| --- | --- | --- |
| Contratos e registro único | Eventos normalizados, ordem, isolamento de sessões, schema inválido, ferramenta ausente, limites BSON, timeout/cancelamento, negação de permissão/aprovação e auditoria sem secrets | NUnit com fixtures independentes; não prova provider real |
| MCP | Descoberta/execução por cliente externo, serialização Extended JSON, transportes, desconexão, tool calls concorrentes e recusa de escrita no lote somente leitura | Processo MCP real e MongoDB de teste; contratos simulados identificados |
| Adaptadores externos | Sessão, streaming, expiração, login cancelado, chave inválida, indisponibilidade e capabilities honestas | Integração explícita dependente de credenciais autorizadas, sem API keys em CI |
| Cofre e privacidade | Persistência apenas de referências, bloqueio/ausência do cofre, exclusão/revogação, zero egress não autorizado, isolamento de URI/segredos | Windows e Linux reais; teste unitário não comprova proteção nativa |
| UI e regressões | Chat/provider selector, tool calls, aprovações e cancelamento; ONNX/autocomplete e IDE continuam sem rede/provedor/modelo/MCP | PNGs reais claro/escuro e homologação nativa na Fase 9/v0.13.0 |

**Acréscimo de 25/09/2026, atualizado em 27/09/2026 (sem homologação manual).** Por decisão do usuário, a matriz inclui: (1) **contas Codex/ChatGPT (sublote 7B) e Claude pelo modo Claude Code** (bloco CL, condicionais e prioritários; substitui o antigo sublote 8B), validadas manualmente pelo [roteiro de homologação](phases/phase-07-v0.11.0/README.md) (H-01..H-17 para o 7B; C-01..C-36 para Claude inicial e C-37..C-45 para CLP — contas próprias, dados sintéticos, nunca automatizado com credenciais nem em CI); (2) **chat completo na UI**, com PNGs reais nos dois temas em 960/1366/1920 e homologação nativa de leitor de tela e diálogos. Código de Claude e CLP existe, com gates ainda pendentes.

| Linha | Evidência exigida | Ambiente / estado |
| --- | --- | --- |
| Login por conta OpenAI (7B) | Registro datado por caso: versão do binário Slop e Codex, SO, conta/plano sem e-mail, resultado e evidência sanitizada; confinamento e keyring provados; suporte oficial ou risco aceito registrado | Windows e Linux nativos, execução manual do usuário. **Não executado; 7B não implementado; AC-05/07/08 pendentes** |
| Conta Claude — modo Claude Code (bloco CL; substitui 8B em 25/09/2026; escopo de ferramentas revisado no mesmo dia — [ADR-054 revisada](10-decisoes-arquiteturais.md#revisão-de-25092026-mesma-data-decisão-posterior-do-usuário--riscos-residuais-do-threat-model-não-aceitos-escopo-revertido)) | Spike P7-CL0-01 **executado no Windows** ([relatório](../eng/spikes/phase-07/claude-code/README.md)); CC-01..08 automatizados só com CLI falso/fixtures; GCL-1..8; casos manuais C-01..C-36 por SO/versão do Claude Code/plano: detecção, login/logout delegados, auth expirada, API Key no ambiente bloqueando, modo em uso, **ferramenta ausente por construção** para execução/escrita/rede (C-08..C-11, C-26, C-32, C-33), leitura com/sem pasta de workspace (C-35/C-36), cancelamento só por Job Object/grupo de processos, resume, zero tokens nos artefatos do Slop. [Plano](phases/phase-07-v0.11.0/README.md) | Manual pelo usuário com conta própria; **provider implementado em código, casos manuais não executados; AC-03/06/07/08/09/10/11/12/14/15/17 pendentes**; ferramentas nativas de execução/escrita/rede **não liberadas** (riscos residuais não aceitos pelo usuário); leitura pura e tools do produto via MCP condicionadas ao [threat model — documento 22](phases/phase-07-v0.11.0/README.md) (GCL-3 pendente) |
| Polimento do Agente IA (ADR-056, sublotes CLP-0..CLP-7; [plano 23](phases/phase-07-v0.11.0/README.md)) | GCL-9..GCL-17: `AgentModePolicy` (modo × permissões → plano); prompt < 2 KB sem segredos; `LineDiff` apply/revert por hunk e hunk desatualizado; resolver de anexos (limites, exclusões, redação); facetas LiteDB (CAS, ilegível não sobrescrito, versão futura recusada, falha visível); migração aditiva das preferências; FakeClaudeCode (argv por modo, `init` com MCP válido/inválido, resume após reinício e fallback, cancelamento preservando a conversa); registry sem URI/credencial, `get_cached_schema` sem tocar o banco, `propose_file_edit` sem gravar em disco e proposta para buffer sem caminho/workspace com binding da conversa; UI headless (troca de aba não troca conversa, turno em conversa não ativa, disponibilidade com timeout e **Tentar novamente**); PNGs claro/escuro do painel, histórico, Permissões e diff | Código CLP integrado; build passou; testes focados nesta revisão: 37 aprovados, incluindo proposta para buffer sem caminho via escopo nativo; suíte completa, inspeção visual atualizada, leitor de tela, IME, diálogos nativos, Linux e manual GCL-17/C-37..C-45 no Windows com conta própria e MongoDB real **pendentes**. Nenhum gate ou AC aprovado |
| Chat completo na UI | UI-03: hospedagem na janela principal, contexto por aba, streaming, cartões de tool, aprovação/consentimento, MCP opt-in; PNGs 960/1366/1920 claro/escuro; leitor de tela, IME e diálogos nativos | Headless e PNGs para regressão; homologação nativa manual. **Em andamento, não entregue** |

**Evidência adicional de 28/09/2026:** uma tentativa somente leitura com URI MongoDB fornecida pelo usuário conectou e `ListDatabaseNames` concluiu com retorno, sem extrair nomes ou contagem e sem registrar URI, nomes ou conteúdo. O [relatório CLP](phases/phase-07-v0.11.0/README.md) limita a conclusão à conectividade daquele caminho isolado. C-41/GCL-17 ainda exigem Claude Code e tools do aplicativo via MCP, índices, schema em cache, negações, edição revisada e retomada; nenhum gate é fechado por essa tentativa.

**Validação automatizada CLP na mesma rodada:** restore locked e build passaram; a suíte oficial terminou com **exit 1**: UnitTests 3.782 aprovados/22 ignorados, Benchmarks 43 aprovados, Infrastructure.Agents.Tests 244 aprovados/**2 falhas** em testes da árvore de processos do CLI falso. Total 4.069 aprovados, 2 falhas, 22 ignorados. Filtros de propostas/anchors/eventos (8/8), opt-out/LiteDB/eventos (20/20) e screenshots/foco (3/3) passaram. PNGs reais do painel, Permissões e diff foram inspecionados nos dois temas; ver tamanhos e falhas no [relatório CLP](phases/phase-07-v0.11.0/README.md). Leitor de tela, IME, diálogos nativos, Linux e GCL-17 permanecem pendentes.

A validação documental desta meta deve registrar separadamente índice/leitor, links, preservação das fases e eventuais regressões da solução; não converte esta matriz futura em resultados executados.

Os gates reais necessários para aceitar v0.11.0 (providers, MCP, segredos, Windows/Linux e UI) devem ser satisfeitos antes de anunciar essa entrega. A Fase 9 amplia a homologação transversal; não é justificativa para liberar a Fase 7 sem as provas exigidas em seus [critérios de aceite](phases/phase-07-v0.11.0/README.md).

**P7-COP — rechecagem e erase (29/09/2026):** login segue sem confirmação (`IsAuthenticated=false` nos clientes SDK de conta e sessão; nenhum prompt). O teste `CopilotEraseDeletesNativeSessionsBeforeDurableHistoryAndCanRetryAfterCleanupFailure` prova que falha da exclusão nativa impede `DeleteAllAsync`, falha LiteDB mantém a referência e retry conclui a remoção. Testes correlatos passaram **9/9**. Homologação apenas Windows, conforme escopo do usuário; Linux não será homologado nesta meta. A persistência com histórico ativo ainda compartilha `%USERPROFILE%\\.copilot\\session-state` com a CLI, gate pendente para retenção/erase.

**P7-COP — atualização após login real (29/09/2026):** rechecagem Windows confirmou autenticação oficial de usuário e catálogo coincidente entre cliente de conta e sessão (1 modelo elegível). Streaming sintético sem ferramentas, SessionFs persistente com retomada após reinício do cliente, erase da árvore temporária e cancelamento após delta passaram em testes manuais explícitos individuais **1/1**. O cancelamento informa possível efeito, sem rollback. Permanecem pendentes o caminho real de ferramenta do produto com permissões, falhas/concorrência/recuperação usando runtime oficial e conferência visual/manual Windows. Linux está explicitamente fora da homologação. O provider segue bloqueado até esses critérios serem atendidos.

**Gate de segurança SDK (29/09/2026):** o SDK 1.0.14 não documenta desativação de todos os agentes internos na criação de sessão: só exclusão nominal e atualização posterior. Não presumir wildcard nem janela segura. Copilot autenticação, catálogo, streaming, resume/erase e cancelamento Windows têm evidência manual; tools reais pelo registry e isolamento dos agentes internos ainda pendem. Provider indisponível; Linux fora do escopo.

**Atualização de isolamento Copilot (Windows, 29/09/2026):** `Options.UpdateAsync` agora aplica lista vazia de agentes internos antes de qualquer envio e falha fechada se não houver confirmação. Fakes validam create/resume/ordem antes de send; runtime oficial confirmou lista vazia **1/1**, sem prompt. Os gates manuais reais de SessionFs básico e cancelamento continuam aprovados. A execução de ferramenta em modelo Copilot real pela registry do app e testes reais de falha/concorrência/recuperação permanecem pendentes; Linux está fora do escopo.

**P7-COP — escopo interno de ferramenta por turno (Windows, 29/09/2026):** a autorização do `get_workspace_context` no `AgentToolRegistry` real passou com escopo exato `(session, turn)`, plano e `TabMetadata`; ausências de plano, permissão ou turno foram recusadas, dois snapshots permaneceram isolados e o runtime registrou/removeu o escopo no ciclo de vida do turno (**51/51 testes UnitTests focalizados**). O adapter Copilot passou **67/67**, build da solução passou sem avisos/erros e `git diff --check` passou. Homologação manual Windows com modelo oficial chamou a ferramenta pelo registry real (**1/1**) com somente pasta temporária sintética e auditoria, sem acessar MongoDB ou arquivos do usuário. Login e autenticação/streaming/SessionFs foram homologados no Windows conforme registro acima. Restam ferramentas MongoDB com permissões e recuperação/falhas concorrentes no runtime oficial. Linux fora do escopo; provider ainda não homologado.

**P7-COP — list_connections no Copilot oficial (Windows, 29/09/2026):** teste manual explícito passou **1/1** chamando `list_connections` pelo `AgentRuntime` e `AgentToolRegistry` reais, com grant de sessão limitado a um perfil sintético e saída auditada. Nenhum servidor MongoDB foi consultado; a saída externa é anonimizada pelo registry. Esse resultado confirma autorização na enumeração de perfis, não ferramentas de consulta MongoDB nem falhas/concorrência/recuperação pelo runtime oficial. Linux fora do escopo; provider ainda não homologado.

**P7-COP — fixture MongoDB do Copilot (29/09/2026):** o teste `AgentReadToolsUseLiteralCodecPoliciesAndDurableLedgerAgainstRealServer` aceita `SLOP_CONSOLE_MONGODB_URI` e cria um banco aleatório por execução, removido ao final. Foi adicionado ao CI um job Ubuntu isolado com o serviço oficial `mongo:8.0`, que executa somente esse teste. A execução local Windows continuou ignorada (1/1) porque não há Docker, `mongod.exe` nem URI de teste configurada; não usamos URI real do usuário. O resultado do novo job ainda depende da execução no CI. Esse teste cobre registry/fontes Mongo reais, codecs, autorização e ledger, não uma chamada de tool pelo modelo Copilot oficial. Runtime oficial com MongoDB e gates de falha/concorrência/recuperação permanecem pendentes; Linux segue fora do aceite P7-COP.

**P7-COP — permissão de ferramentas na interface (29/09/2026):** o painel agora assina `PermissionsRequested`, abre a janela de permissões pelo botão **Permissões…** e abre essa janela quando uma chamada do registry retorna negação de permissão. A janela altera opt-ins persistentes, não concede aprovação humana pontual ao turno; a chamada não é repetida após salvar, e o usuário envia uma nova mensagem. Cada turno congela o snapshot de permissões antes da execução; alterações e revogações posteriores passam a valer em novos turnos e não interrompem uma operação já autorizada. Tool calling e turn plans ficam habilitados apenas para o registry do produto, ainda sob as permissões persistidas; ferramentas nativas do provider ficam desativadas.

**P7-COP — confirmação humana por chamada (29/09/2026):** categorias configuradas para confirmação solicitam aprovação antes do dispatch das tools Copilot de metadados MongoDB, contexto do workspace e documentos. A confirmação mostra argumentos da chamada, grava intent/terminal próprios e libera somente uma execução com `ApprovedOnce`; resposta de sessão, recusa, timeout, ausência/falha de prompt ou ledger e escopo alterado falham fechados sem acesso à fonte. Essa decisão pontual não cria grant de sessão. Alterar a janela de permissões persistentes ainda exige novo turno e não dispara retry. Propostas de edição preservam revisão própria. Build: 0 avisos/erros; focos registry/política **51/51** e registry/UI **36/36**. Não há validação pelo runtime oficial nem MongoDB de teste.

**P7-COP — rechecagem sem inferência do runtime oficial (Windows, 29/09/2026):** testes explícitos passaram **3/3**: autenticação reportada nos modos SDK, conta/sessão com autenticação de usuário e catálogo elegível coincidente, e criação/remoção de sessão vazia. Nenhum prompt foi enviado. Essa checagem não comprova chamada de tool pelo runtime nem aprovação humana dinâmica; a fixture MongoDB local permanece ausente.

**P7-COP — proposta no buffer ativo pelo Copilot oficial (Windows, 29/09/2026):** o teste explícito passou **1/1** com consulta sintética e sink fake. Confirmou chamada de `propose_file_edit`, `target: "active_buffer"`, alteração `.limit(10)`→`.limit(5)`, binding da aba e auditoria, sem gravação em disco. O filtro local de proposta em buffer passou **2/2**. Não valida apresentação/aplicação no Desktop real; a consulta MongoDB via Copilot permanece pendente porque falta `mongod.exe`/`SLOP_CONSOLE_MONGOD`.

**P7-COP — runtime fake, registry e store Desktop (29/09/2026):** o novo teste determinístico passa pelo adapter Copilot/SDK com fake stdio, `AgentRuntime`, `AgentToolRegistry` real e `AgentEditProposalStore` real (**1/1**). O plano contém só `propose_file_edit`; runtime abre um escopo Copilot exato por sessão/turno, e a chamada `active_buffer` termina em proposta Registered para aba sintética, validada pelo resolver do store. Nenhum modelo, rede, MongoDB ou arquivo real participa, e a proposta não é aplicada/salva. A homologação com runtime/modelo Copilot oficial ainda usa sink fake; falhas/concorrência do runtime oficial seguem pendentes.

**P7-COP — separar recusa de confirmação da falta de permissão (Windows, 29/09/2026):** a rejeição/expiração da confirmação pontual agora retorna códigos próprios; só `PermissionDenied`/`PermissionMissing` abre a janela de permissões persistentes. Focos de registry, janela de chat e conta passaram **43/43**, incluindo confirmação aceita, negada, expirada e grants ausentes; os PNGs de permissões e conversa claro/escuro foram regenerados e inspecionados. Falhas/concorrência/recuperação no runtime oficial permanecem gates.

**P7-COP — resultado de confirmação através do runtime (Windows, 29/09/2026):** casos do SDK simulado verificaram `ConfirmationRejected`/`ConfirmationExpired` como `Denied` e `ConfirmationUnavailable` como `Failed`; runtime, registry e UI passaram **42/42**. Confirmação humana no runtime oficial e cenários de falha/concorrência/recuperação continuam pendentes.

**Triagem da suíte completa (29/09/2026):** após corrigir o teste de canal MCP que escolhia `AskConfirmations` sem invocar `approve`, repetimos toda a suíte UnitTests: **3.802 aprovados, 11 falhas e 42 ignorados** (2m11s). O caso MCP corrigido passou e não aparece mais entre as falhas. As 11 falhas restantes foram todas tentativas de persistir credenciais Windows/MCP (`CredentialStoreFailed` e `WindowsCredentialManager`), limitadas pelo ambiente de execução; não foram consideradas aprovadas. Os testes direcionados do provider e da fila de eventos passaram **2/2** e o build completo passou com **0 avisos e 0 erros**.

**Revalidação focada após ajustes de UI Copilot (29/09/2026):** o filtro combinado de política, janela de permissões, registry, runtime, chat Desktop, propostas e tools de sessão passou **136/136**. O campo de confirmação preserva JSON de entrada longo e a janela omite controles nativos sem perder as opções válidas de workspace; PNGs claros/escuros foram inspecionados.

**Gate do buffer por mensagem e fixture DerivedReads (29/09/2026):** `propose_file_edit target=active_buffer` agora exige atestação de que o anexo `ActiveFile` foi resolvido naquele turno; sem o chip, retorna recusa de proposta sem abrir permissões persistentes e sem registrar alteração. Reanexar permite a proposta, e o alvo independente de arquivo do workspace continua governado por suas próprias permissões. O filtro focado Application/runtime passou **37/37**; novo assertion verifica que escopo incompleto captura ausência do anexo. O job Mongo CI foi ampliado para ferramentas DerivedReads e preserva a cobertura anterior; cada teste usa database com UUID, confirma ausência antes de escrever e remove somente seu próprio database. O filtro local ainda é ignorado sem servidor Mongo; execução real aguarda CI.
