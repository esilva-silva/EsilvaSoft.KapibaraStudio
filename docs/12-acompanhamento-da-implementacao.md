# Acompanhamento da implementação

**Revisão de licenças Windows — 03/10/2026:** [inventário dos dois ZIPs locais v0.11.0](architecture/windows-license-review-2026-10-03.md), incluindo bundles Desktop/MCP, runtimes e fontes. 1.014 registros, 60 identidades/versões NuGet/runtime packs, sem origem não identificada. Encontrados payload Copilot antigo, Windows ML proprietário e licenças/avisos ausentes nos ZIPs. Ferramenta manual e evidências versionadas; sem alteração de backend ou republicação. Reconstrução, avisos completos por RID, SBOM formal e avaliação da exceção de bibliotecas de sistema pelo SignPath permanecem pendentes.

**Corrida de descarte de metadados — 03/10/2026:** o log `logs_100615514147.zip` revelou uma carga na fila global chegando à origem após descarte do cache no Ubuntu. A carga agora verifica cancelamento e identidade da conexão após adquirir os semáforos. Um novo teste determinístico reproduziu a falha antes da correção e passou depois; foco de metadados **27/27**, restore locked e build Release sem avisos/erros. Solução Windows: **4.569 aprovados, zero falhas, 36 ignorados reportados**; benchmarks não executados. Reexecução nativa Ubuntu pendente. [Diagnóstico e evidências](architecture/test-stability.md#continuação-descarte-do-cache-no-ubuntu--03102026).

**P7-UX2 — código e gates automatizados concluídos em 03/10/2026:** [meta e limites](phases/phase-07-v0.11.0/meta-autoscroll-contexto-consumo-agente.md). Restore locked e build da solução passaram sem avisos/erros; Windows: UnitTests **3.380 aprovados/20 ignorados**, Infrastructure.Agents.Tests **288/288**, IntegrationTests **903 aprovados/16 ignorados**, zero falhas. Capturas Headless em claro/escuro, idiomas e escalas foram geradas; amostras pt-BR e flyouts foram inspecionados. Amplitude de inspeção visual, clique/teclado fora do Headless, leitor de tela, Windows nativo, Linux e CLI/contas oficiais seguem pendentes. Não mede tokenizer, ocupação de janela ou cota.

**Configuração do executável Copilot — implementada em 03/10/2026:** preferência aditiva e singleton Application unificam conta, login e sessões; override inválido falha sem fallback e detecção automática percorre perfil local antes de PATH. Restore/build Release, focos corrigidos e teste manual sem prompt passaram. Duas falhas na execução integral inicial foram corrigidas/cobertas por filtros focalizados, sem nova execução integral. [Resultados e limites](architecture/release-copilot.md#configuração-do-executável--revisão-de-03102026).

## Repriorização de entregas — Fase 5 antes da Fase 7 — 01/10/2026

A prioridade de execução foi trocada entre a Fase 5 / v0.9.0 (IA local e produtividade contextual) e a Fase 7 / v0.11.0 (Claude e agentes). As entregas pendentes da Fase 5 passam à frente; a retomada da Fase 7 fica para depois. IDs, versões e evidências já registradas não foram renumerados nem reclassificados. O detalhamento executável continua na [meta da Fase 5](phases/phase-05-v0.9.0/meta-de-implementacao.md); inferência com modelos/hardware reais e homologação nativa permanecem nos gates da Fase 9. Consulte também o [roadmap](09-plano-de-implementacao.md) e o [índice das fases](phases/README.md).

**Continuação da estabilidade — 01/10/2026:** os logs do CI 36931654539 identificaram duas corridas no Ubuntu: classificação de cancelamento no coordenador de aprovação e ordem presumida dos despachos no teste de `Busy`. Correções concluídas, mantendo asserções de auditoria/nenhuma escrita no cancelamento e uma escrita por sessão. Dois casos novos reproduzem deterministicamente a falha anterior. Restore locked e build Release passaram; solução Windows **4.509 aprovados, zero falhas, 25 ignorados reportados**. Foco **36/36** e três execuções com 1/2/4 processadores também **36/36**. Benchmarks continuam manuais e não foram executados; reexecução CI Ubuntu ainda pendente. [Detalhes](architecture/test-stability.md#continuação-ci-36931654539).

**Estabilidade de testes — 01/10/2026:** [revisão e evidências](architecture/test-stability.md). Caminhos sintéticos corrigem a falha de arquitetura dos logs; CI usa a mesma lista unitária nos dois SOs e guarda de fontes também no Linux. Medições saíram dos testes automáticos; Benchmarks exige opt-in manual, fora de CI/release. Aguardar cargas revelou e corrigiu uma corrida de descarte de semáforos. Autosave usa relógio controlado e ghost aguarda sua tarefa; cleanup da migração só repete compartilhamento/bloqueio Windows 32/33 em pasta sintética, com limite. Restore locked passou; build Release final zero avisos/erros. Suíte Windows final: **4.507 aprovados, zero falhas, 25 ignorados** (Unit 3.360/20, Agents 254/0, Integration 893/5). UnitTests passou em três execuções adicionais com as mesmas contagens. Nenhum benchmark foi executado; PNGs workspace claro/escuro 960×620 da execução foram inspecionados. Alterações ainda não executadas no CI Linux; este host não tem distro WSL. Homologações reais permanecem pendentes.

**Validação final desta revisão:** reexecução integral de IntegrationTests: **885 aprovados, 0 falhas, 4 ignorados**; as duas falhas de cleanup anteriores não se repetiram. UnitTests 3.343/20 ignorados, Agents 198 e Benchmarks 41 já haviam passado. ZIP final Windows extraído: 2/2 testes de runtime/conta oficial com PATH vazio. Quatro SHA-256 e modos de execução Linux conferidos. [Resultados e limites](architecture/release-copilot.md).

**Release/Copilot — 01/10/2026:** causa reproduzida no publish, correção implementada e quatro pacotes locais gerados. Auto update sem fallback ao repositório antigo. Build Release 0 avisos/erros; testes do publish com conta oficial e PATH vazio 2/2; regressão focada 39 aprovados/1 ignorado. Integral com duas falhas de cleanup, aprovadas na reexecução focada; não declarada verde. [Resultados e gates](architecture/release-copilot.md).

**P7-LINUX — revisão estrutural e testes unitários, 01/10/2026:** [meta Copilot/Claude Linux](phases/phase-07-v0.11.0/meta-linux-copilot-claude.md) registra o limite escolhido pelo usuário: não testar conexões ou fluxos reais em máquinas Linux. Implementado leitor da prova MCP pelo Secret Service existente, sem unlock/prompt/fallback; gate produtivo Claude Linux preservado. Corrigidos payload Copilot externo no single-file, licença original e modos 0755 de launchers/helpers nos TARs; verificador confere cabeçalhos ELF. TARs x64/ARM64 foram substituídos pelas versões auditadas, hashes/licença conferidos. A CI Linux compila a solução, inspeciona estruturalmente pacotes e roda somente UnitTests e Infrastructure.Agents.Tests; a CI Windows mantém a suíte completa. Foi removida a trilha GNOME Keyring/D-Bus de integração e o smoke de CLI não executará binários Linux. Localmente, UnitTests: **3.354 aprovados, 20 ignorados**; Infrastructure.Agents.Tests: **200 aprovados**, ambos sem falhas. Esta evidência não significa homologação funcional Linux.

**P7-LINUX — incremento de cobertura unitária, 01/10/2026:** UnitTests passou **3.357/3.357, 20 ignorados** e Infrastructure.Agents.Tests passou **214/214**, zero falhas. Foram incluídos contratos de falha/retry e catálogo Copilot, defaults de sessão, launcher/permissão POSIX com filesystem sintético, encerramento de stream e exit code Claude, e registry MCP em memória. Dois testes dependentes de permissões/symlink POSIX não executam no host Windows; serão exercitados apenas pelo job unitário Linux. Execução aprovada com `CopilotSkipCliDownload=true` após falha TLS ao tentar baixar a CLI, e `UsedAvaloniaProducts=` para evitar a tarefa externa Avalonia. Nenhuma conexão, CLI ou binário Linux foi iniciado; contagens são evidência Windows de unit tests, não homologação Linux.

**P7-LINUX — classificação segura de conta Claude, 01/10/2026:** acrescentei matriz unitária para só aceitar conta `claude.ai` first-party com plano explícito e sem fonte de chave; chave direta, helper, OAuth de ambiente, cloud, estado deslogado ou assinatura não identificada permanecem categorias distintas. Um teste verifica que campos imprevistos com e-mail, organização, token e caminho não vazam no DTO. Infrastructure.Agents.Tests passou **222/222**, zero falhas; o filtro Claude passou **32/32**. Os cenários são sintéticos e não acessam estado de usuário nem iniciam CLI. A meta agora rotula os registros anteriores de runtime como históricos, fora dos gates Linux atuais. Permanecem lacunas unitárias de maior porte na orquestração Claude→MCP e no lifecycle completo do provisionador MCP; a auditoria mapeou esses alvos, sem ainda convertê-los em novos testes.

**P7-LINUX — reparo da evidência dos TARs, 01/10/2026:** a validação profunda encontrou que os TARs de nome raiz não continham a CLI, embora uma rodada anterior os tivesse registrado como corrigidos. Substituí-os pelos pacotes pós-merge que passaram `Assert-KapibaraLinuxArchive` em x64/ARM64; `Test-ReleasePackage.ps1 -SkipRuntimeExecution` também passou nos dois publishes. `SHA256SUMS.txt` foi atualizado para `220c11aa…f8247e` (x64) e `c05835ed…d4452d6e` (ARM64). Esta checagem só leu estruturas, modos e ELF; nenhum payload foi executado.

**P7-LINUX — regressão unitária atualizada em 01/10/2026:** `ClaudeCodeMcpSessionTests` passou **9/9** e `AgentMcpChannelProvisionerLifecycleTests` **3/3**, incluindo negação Linux antes de iniciar broker. `CopilotSessionIsolationTests` passou **17/17**, cobrindo descarte/falhas/concorrência, aquisição pendente e cancelamento antes do envio; os filtros Copilot combinados de lifecycle, stream tardio e cleanup passaram **18/18**, e a regressão Copilot **67/67**. Os contratos Claude de conta/sessão e launcher passaram **54/54**. Suítes integrais: UnitTests **3.360 aprovados, 20 ignorados**; Infrastructure.Agents.Tests **254/254**, zero falhas/ignorados. A seleção de executáveis usa probes e metadados injetados, sem filesystem real (`LinuxAgentAdapterContractTests` 8/8). A matriz Linux executa somente essas duas assemblies em `ubuntu-24.04` x64 e `ubuntu-24.04-arm`; CI remota ainda não rodou sobre as alterações locais e não houve execução de runtime Linux.

**Verificação de build/restore P7-LINUX (01/10/2026):** build Release da solução passou com **0 avisos/erros** (`UsedAvaloniaProducts=` e `CopilotSkipCliDownload=true`). Restore `--locked-mode` também passou em cache local com `NuGetAudit=false`, após isolar `%APPDATA%` porque o sandbox não permite ler o `NuGet.Config` global; a consulta online de vulnerabilidades ficou indisponível por bloqueio de rede. O arquivo de lock foi respeitado, mas esta execução não é evidência de auditoria NuGet online.
**Revisão P7-UX após uso — 30/09/2026:** seletor direto de provider com lista única limitada, nomes sem repetição e chips menores; contornos removidos dos detalhes comuns, preservando destaque de bloqueios. Build em saída isolada (aplicativo aberto): 0 avisos/erros; 22/22 testes UI/hosting/conta aprovados e PNGs da lista aberta inspecionados. [Evidências e limites](phases/phase-07-v0.11.0/meta-ui-ux-painel-agente-ia.md#revisão-após-captura-do-usuário--30092026).

**Pacotes Linux 0.11.0 — verificação de modo executável (01/10/2026):** auditoria dos TARs locais encontrou o proxy MCP em `0644`; os publishes Release foram refeitos para `linux-x64` e `linux-arm64` com versão explícita 0.11.0, os archives regenerados e `SHA256SUMS.txt` atualizado. ELF, permissões e licença passaram na validação do empacotador. Isso não comprova execução nativa Linux.

**Merge de `origin/master` e CLI Copilot empacotada (01/10/2026):** fast-forward do commit `2ebc08b` aplicado e alterações locais reaplicadas após resolver conflitos; master agora inclui a aquisição checksum-verificada da CLI interativa 1.0.85. As publicações x64/ARM64 foram reconstruídas e os hashes dos dois assets oficiais conferidos pelo manifesto. Atualizei o empacotador para validar ELF e preservar `0755` também na CLI; ambos os TARs passaram e os SHA-256 foram regenerados. Restore e build completo continuam dependentes do download TLS do alvo MSBuild; com `CopilotSkipCliDownload=true`, o build completo passou sem avisos/erros. A CI remota e execução Linux nativa continuam pendentes.

**P7-UX — 30/09/2026:** [P7-UX — painel Agente IA](phases/phase-07-v0.11.0/meta-ui-ux-painel-agente-ia.md) implementado diretamente em `F:\source\esilva-silva\EsilvaSoft.KapibaraStudio`. Restore locked e build aprovados (0 avisos/erros); suíte completa: **4.466 aprovados, 23 ignorados, 0 falhas**. Histórico de referência medido em 61,07% (provider genérico) e 55,35% (CLI oficial simulado). Matriz de 144 PNGs mais estados de overflow/streaming/persistência; amostras renderizadas inspecionadas. Homologação nativa e runtimes reais continuam pendentes.

**P7-AUTH — implementado em 30/09/2026:** a análise identificou que startup atualizava apenas o catálogo passivo enquanto “Testar conexão” consultava conta/modelos oficiais. O contrato `IAgentAccountManager` e `IAgentAccountHandler` agora vive em Application; handlers registrados por provider são despachados sem switch de marca. Startup do provider salvo, retry, Testar conexão e refresh pós-entrar/sair usam `AgentProviderAvailabilityService`, com política explícita, single-flight/cache, timeout, descarte de snapshots obsoletos, preservação do modelo selecionado e cancelamento coordenado no encerramento. Restore locked e build passaram; build sem avisos/erros; UnitTests **3.340 aprovados, 20 ignorados**; integração focada de conta/UI/arquitetura **10/10**. Na integração completa, 866 passaram e dois fixtures não relacionados falharam ao excluir diretórios temporários ainda em uso; passaram 3/3 quando isolados. A execução testou contratos simulados; autenticação Copilot real nesta sessão não foi repetida. Política Linux/Codex ainda requer ação explícita quando a consulta pode tocar keyring/diálogo; validação nativa por SO segue pendente. [Meta e limites](phases/phase-07-v0.11.0/meta-inicializacao-autenticacao-agentes.md).

## Meta transversal — isolamento do SO — 30/09/2026

**Orquestração solicitada:** Sol 6.1 extraiu Claude Code e runner mongosh; Luna auditou dependências transitivas e separou MCP/IPC/processos/composição LiteDB. A conversa principal integra DI, referências, snapshot de ambiente, documentação e validação. Build CPU aprovado sem avisos/erros; filtro de 125 casos unitários passou (inclui os 84 anteriores e orquestração mongosh/console/ambiente/admissão/conta). Providers: 101/101 passaram (21 Claude em memória e os 80 Codex/Copilot). As falhas iniciais Claude eram do fake: o init usava alias em vez de identificador canônico; o teste confundia cleanup posterior com cancelamento de outra sessão. O teste agora observa duas sessões simultâneas ainda ativas, cancela uma e confirma que a outra segue ativa antes de liberá-la. Políticas de produção e asserções de isolamento foram preservadas. TRX em TestResults/ClaudeMemoryBoundary e TestResults/MongoshMcpMemory. Integração nativa não reexecutada devido ao limite da revisão automática; Linux pendente. Driver console, DNS, broker de produção, LiteDB privado e renderização continuam na meta ativa.

**Incremento catálogo/privacidade:** enumeração limitada extraída para `IAgentWorkspaceFileCatalog`/adapter System; abertura do editor usa probe. Picker limpa cache de escopo anterior, captura exclusões e rejeita respostas antigas/canceladas. Sete casos novos do picker e três de privacidade usam doubles; o scanner de fonte passou à integração, mantendo três verificações de assemblies na unidade. Build CPU zero avisos/erros e filtro unitário 84/84 passaram, incluindo os 71 anteriores. A última integração teve 175 aprovados, uma falha e dois ignorados de 178 automatizados: a asserção de cancelamento exigia tipo exato e recebeu `TaskCanceledException`, derivada de `OperationCanceledException`. Corrigida para aceitar a hierarquia e conferir o token, ainda sem reexecução. Revisão automática impediu nova execução fora do sandbox por limite de uso, sem executar a ação; integração permanece pendente. Claude/mongosh seguem com Sol 6.1 e auditoria semântica com Luna sob orquestração da conversa principal.

**Incremento probe/store:** `IAgentWorkspacePathProbe` leva consultas de existência/links ao adapter System. Registry, resolver e chat recebem a porta; falta/falha recusa acesso. O helper de ferramentas de sessão e seus 35 casos usam diretórios/bytes em memória e callback de eventos simulado. O store Desktop recebe reader síncrono limitado para validar base de arquivo fechado; a preferência pelo buffer/tab capturado permanece. Filtro 71/71 passou (35 sessão, 14 arquivos simulados, cinco buffer, onze políticas, seis ponte Desktop); integração 171 aprovados/dois symlinks ignorados de 173 automatizados. Quinze homologações explícitas não executadas. Picker ainda enumera disco e outras fixtures/processos persistem; meta completa ativa.

**Incremento leitura limitada:** `IAgentBoundedFileReader` separa bytes de anexos/propostas; reader real está em Infrastructure.System e a composição registry/chat recebe a porta. Decodificação/redação/limites permanecem em Application; ausência recusa alvos em disco e buffer capturado não lê arquivos. A fixture real de anexos foi movida para integração: 168 aprovados e dois casos de symlink ignorados de 170 automatizados; quinze homologações oficiais explícitas não executadas. Filtro unitário 22/22 (cinco novos de buffer em memória, onze política, seis ponte Desktop). Build CPU zero avisos/erros. Paths, links, fixtures de ferramentas de sessão e demais frentes continuam pendentes.

**Incremento Copilot:** sessão/provider consomem portas de cliente, sessão, store, recursos e comandos. Wrapper SDK, SessionFs/SQLite, configuração nativa e terminal/descoberta CLI passaram para `Infrastructure.System`; fakes não iniciam SDK/processo/armazenamento. A composição Desktop aceita substituição dos adapters e o cleanup respeita a factory injetada. A fixture PowerShell, stores reais, descoberta do CLI e testes oficiais explícitos estão na integração. Passaram 80/80 casos focalizados Codex/Copilot (incluem os 69 anteriores e 11 novos de provider/composição) e 6/6 da ponte automatizada Desktop em memória; integração reexecutada 121/121 automatizados Windows, com nove testes oficiais explícitos não executados. Build CPU zero avisos/erros. Os seis testes manuais Desktop/tools foram movidos para integração e continuam explícitos; a reexecução automática passou 121/121 e listou quinze homologações oficiais sem executá-las. Políticas/binding nativos usam doubles de repositório/autoridade: 11/11 casos passaram, incluindo oito migrados de LiteDB e três de falhas/cancelamento; a ponte Desktop permaneceu 6/6. Demais acessos transitivos e Linux permanecem pendentes; meta completa permanece ativa.

**Incremento Codex:** descoberta do CLI/ambiente, preparação de home privado, lançamento e árvore de processos passaram para `Infrastructure.System`. Provider/transporte recebem portas; pasta de workspace é simulada nos testes. 38/38 testes focalizados com doubles passaram; a fixture real foi movida para integração e passou 11/11 Windows fora do sandbox (ACL bloqueada no sandbox). Codex oficial/Linux não homologados por esse servidor falso. Restam Claude/mongosh/broker, Copilot e outras fixtures com I/O.

Em validação multiplataforma: `Infrastructure.System` concentra adapters de arquivos/workspace/exportação, console STDIO MCP, LiteDB, segredos Windows/Linux, broker/IPC, credenciais MCP, paths, launchers, catálogo/cache de modelos, diretório de modelos, atualização e processos Claude/Codex/mongosh/Copilot. O runtime ONNX está em `Infrastructure.LocalAi.OnnxAdapter`; a medição de memória usa porta. A composição preserva o owner LiteDB único e os contratos de autenticação, permissões e cancelamento. Restore `--locked-mode` offline e build CPU passaram; a execução completa aprovou Benchmarks 41/41, UnitTests 3323/20 ignorados, Infrastructure.Agents.Tests 198/198 e IntegrationTests 868/14 ignorados. Builds cruzados `linux-x64` de Desktop e McpServer passaram. Runtime/testes Linux e homologações nativas `Explicit` seguem pendentes porque não há distribuição Linux neste host. A auditoria unitária expandida mantém três ocorrências seguras (stream de assembly, stream em memória do fake de console e handler HTTP fake); o teste de opções Copilot, a leitura/escrita de relatórios reais e o runner ONNX real estão em integração. [Plano completo](architecture/system-adapters.md), [auditoria](architecture/unit-resource-audit.md), [ADR-060](10-decisoes-arquiteturais.md#adr-060--adapters-do-sistema-operacional-e-isolamento-dos-testes-30092026).

## Fase 7 — retomada focada em Claude — 28/09/2026

A documentação anterior de planejamento e os agentes da fase foram removidos para reduzir o escopo. A implementação atual está sendo finalizada sobre os providers, chat e MCP existentes; não se declara aceite de homologação real nesta revisão. Foram vinculados ao turno os snapshots de workspace e canal MCP, adicionados pedidos visíveis de aprovação para ferramentas e concessões em memória restritas a leituras e argumentos exatos. Comandos, escrita e rede continuam exigindo aprovação individual. Ver [Claude no KapibaraStudio](phases/phase-07-v0.11.0/README.md) para o estado verificável, limites e roteiro real. A validação executada nesta retomada será registrada após terminar os testes da solução.

## Fase 5 / v0.9.0 — escopo automatizável concluído — 22/09/2026

Build da solução: **0 avisos, 0 erros**. Suíte completa: **2.742 aprovados, 0 falhas, 20 ignorados**. Implementados consentimento local de contexto global/por conexão, opt-in separado de Input JSON e prévia obrigatória antes da inferência, com invalidação por edição/política/destino; respostas são vinculadas à revisão de origem e propostas continuam revisáveis, undoáveis e sem execução Mongo. Runtime agora invalida chamadas obsoletas que aguardam fila/carga sem interromper contexto válido; autocomplete imediato determinístico e fallback/IA debounced; editor publica ciclo de composição IME. Quatro execuções Headless do gate edição→ghost deram p95 3,85–8,42 ms. Imagens de chat e prévia nos quatro idiomas/temas foram geradas e inspecionadas.

Restore locked-mode passou via cache local e `NuGet.Config` temporário; a leitura do config global e TLS para NuGet.org são bloqueados pelo sandbox, portanto `NuGetAudit` online não foi executado. Modelo ONNX real, fidelidade da inferência, GPU/NPU, Linux gráfico, IME/leitor de tela nativos e latência/memória reais continuam experimentais na Fase 9. A matriz e a [meta de implementação](phases/phase-05-v0.9.0/meta-de-implementacao.md) registram as evidências e limites.

## Meta v0.8.0 — fases 1–4 arquivadas — 22/09/2026

Os escopos funcionais das fases 1–4 foram fechados e arquivados em `docs/done/release_v0.5.0` até `release_v0.8.0`; a homologação manual continua transferida para a Fase 9. Restore locked-mode, build Release e testes concluíram sem falhas: 2.674 testes unitários aprovados, 20 ignorados, 43 benchmarks aprovados e zero avisos/erros. `build-release.ps1 -Version 0.8.0` gerou os quatro pacotes locais (Windows/Linux, x64/ARM64) e `SHA256SUMS.txt`; checksums, arquiteturas, executáveis single-file, versão `0.8.0.0` e modo Linux `755` foram verificados. Modelos/hardware reais, MongoDB real, leitor de tela, diálogos nativos e demais homologações externas permanecem pendentes na Fase 9.

## Atualização da meta de autocomplete — 21/09/2026

A meta foi retomada com escopo funcional e de testes automatizados, sem validar múltiplas plataformas, MongoDB/modelos reais ou metas de performance. O fluxo foi dividido por risco: tarefas de corpus e regressão ficam no perfil Luna; integração de catálogo, ranking e snippets no Terra; parser, contexto, recuperação tolerante, concorrência e cancelamento no Sol; decisões arquiteturais, persistência LiteDB e revisão sistêmica sobem ao Astra apenas quando necessário.

O novo runner de casos `.case` cobre parsing, papéis de contexto, shapes, kinds, aspas, `ReplaceSpan`, edições aplicadas, expansão de snippets e um primeiro corpus de ranking tipado: **675 casos aprovados**. A cobertura já inclui stages de pipeline, `$lookup`, busca, arrays de operadores, modificadores de update, construtores BSON por tipo e prioridade de métodos de leitura. O `CompletionContextEngine`, o `ShapeWalker` e o catálogo de linguagem foram ajustados para recuperação tolerante, contexto após whitespace, referências de campo/variável, strings contextuais, `$facet` por ramo e shapes específicos por símbolo. O ranking passou a transportar tipos BSON conhecidos do campo pai até `CompletionItem`, aplicar `TypeMismatchPenalty` somente para restrições declaradas e priorizar construtores compatíveis.

Os provedores explícito e preemptivo, cancelamento, concorrência, namespaces de metadata, schema learning e `$lookup` estrangeiro possuem cobertura unitária. O gate funcional de ranking registra **MRR 1,000, top-1 44/44 e top-5 44/44** sobre o corpus local; extensões futuras de corpus não reabrem este aceite. A suíte unitária completa mais recente teve **2.639 aprovados, 0 falhas e 20 ignorados**. Performance, validação multiplataforma, leitor de tela, MongoDB real e modelos reais permanecem explicitamente fora da meta.

## Fase 1 (K11–K16, K16-b) e Fase 5.1 (Traditional Preemptive) — 18/09/2026

**Registro histórico desta data.** Os limites de IME, latência e controles abaixo foram reaudidados e atualizados em 22/09 na seção [Fase 5 / v0.9.0](#fase-5--v090--escopo-automatizável-concluído--22092026); não representam o estado atual.

Build `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore`: **0 avisos, 0 erros**. Suíte
`dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore`: **1273 aprovados, 0 falhas** (baseline de entrada: 1154).

### Fase 1 — K11–K16 e K16-b

Trabalho real entregue, sem reconstruir catálogo/benchmarks já existentes: cancelamento no scan de substring de
`NameTable.Collect` (K12); limite de cargas simultâneas no `MetadataCache` — 2 por conexão, 4 globais (K13); a
memoização de mescla em `MetadataCatalogSource` virou LRU de 8 entradas (K14); `CollectionSchema.Merge` deixou de usar
`int.MaxValue` como teto implícito e passou a respeitar `SchemaMaximumDepth`/`SchemaMaximumNodes` (K15); cota por fonte
em `KnowledgeCatalog.Query` que impede uma fonte de ocultar totalmente outra quando ambas produzem candidatos do mesmo
`kind` (K16-b), pré-requisito de L15.

**K11 teve o escopo reduzido**: a mudança de contrato de `ConnectionIdentity` proposta originalmente foi rejeitada.
Gerações por chave, single-flight, write-through com guarda de geração, `SampleSchemaAsync` protegido contra
desconexão/invalidação, `Peek` propagado e `Changed` terminal por chave **já existiam com teste antes desta meta** —
não são entrega deste lote. K11 ganhou duas travas de regressão novas: cache esvaziado após `InvalidateEnvironment` e
hosts distintos com o mesmo nome de namespace sem reuso cruzado entre conexões.

**Registro histórico desta seção:** K17 (relatório de performance do catálogo) não iniciado; a implementação de L11–L16/schema
learning persistido em LiteDB foi posteriormente incorporada ao repositório, mas ainda precisa de cobertura integrada no corpus desta meta. Detalhe em
[phase-1-data-traditional](auto-complite/phases/phase-1-data-traditional.md#k11k16-e-k16-b--concluídos-em-18092026).

### Fase 5.1 — Traditional Preemptive Completion

Entregue `TraditionalPreemptiveCompletionProvider` (determinístico, `MetadataAccess.Peek`, sem rede/ONNX/I-O),
`InlineCompletionConfidence` (abstenção por prefixo vazio, catálogo truncado, palavra completa, snippet, mais de uma
continuação estrita, continuação fora do top-1 ou margem top-1/top-2 abaixo de 0,20), `InlineCompletionCoordinator`
com debounce por `TimeProvider` e uma pendência substituível por editor, `InlineCompletionPolicy` e
`InlineCompletionEditorState`. 47 testes novos cobrem relógio falso (20 teclas abaixo do debounce → zero computação;
a pausa gera exatamente uma), os sete gatilhos de cancelamento, provider que ignora cancelamento tendo o resultado
descartado, isolamento entre abas, funcionamento sem modelo/MongoDB, supressão pelo automático quando a lista
explícita está aberta, combinações das flags, LoadedOnly em três fases, migração, abstenção por
ambiguidade/truncamento e aceite como operação única de undo sem executar consulta.

**A fonte padrão do ghost automático mudou**: a ordem passou a ser determinístico → dicionário lexical local → IA. A
IA automática só é alcançada com `InlineUseAi = true` **e** modelo já em `Ready` (LoadedOnly); nenhum caminho de
digitação carrega, troca ou inicializa modelo. `AiAutocompleteProvider` foi preservado, apenas deixou de ser o padrão.

Flags aditivas em `AutocompleteSettings`, persistidas como anuláveis para distinguir **ausente** de **`false`
explícito**: `InlineEnabled` (efetivo `?? true`), `InlineUseTraditional` (efetivo `?? UseDictionary`), `InlineUseAi`
(efetivo `?? false`, inclusive na migração de documento v1 antigo — decisão de 18/09/2026, W3, registrada em
[configuration](auto-complite/configuration.md#migração): a redação anterior da especificação mandava derivar
`InlineUseAi` de `Mode != Basic` na migração, o que ligaria inferência automática em praticamente toda instalação
existente; a implementação usa `false` também na migração, mantendo o opt-in exigido).

**Pendências que não podem ser declaradas resolvidas:**

- **IME.** `InlineCompletionEditorState.Composing` existe e é respeitado pelo coordinator (testado), mas o editor
  **não publica** o estado de composição — `ImeComposing` é sempre falso em produção. A regra "não sugerir durante
  composição de IME" é pendência de implementação, não de homologação.
- **Sem UI para as três flags inline** em `AutocompleteSettingsWindow`; só editáveis pelo JSON persistido.
- **Edição → ghost, meta de 20 ms: NÃO atendida.** Medido em Headless com debounce de 50 ms: excedente de
  17,6–27,1 ms sobre o debounce (primeira sugestão do processo 92/112/108 ms; aquecida 77/68/77 ms). O trabalho de
  geração é ~0,07 ms; o excedente é dominado pela resolução do temporizador do Windows (~15,6 ms) mais dois saltos de
  despachante. A meta não foi ajustada. Computação p95 ≤ 5 ms **é atendida** (catálogo de linguagem: p95 0,009 ms;
  200 campos de schema: p95 0,068 ms). Ver [performance](auto-complite/performance.md).
- Matriz de 18 PNGs de homologação visual não produzida nesta entrega.
- Fases 3 e 4 têm o caminho de IA local implementado e coberto por fakes; 5.2/5.3 também têm o fluxo automático integrado via `CompletionSession`/`AutocompleteService`, com política `LoadedOnly`. Modelo ONNX real e performance permanecem fora desta meta.
- Homologação real (layouts físicos ABNT2/US em Windows e Linux X11/Wayland, IME real, leitor de tela, MongoDB real,
  modelos ONNX reais) não executada.

Também registrado nesta rodada: ganho medido do cache de tokens da Fase 2 (BenchmarkDotNet, Ryzen 9 7900, Windows 11
25H2, .NET 10.0.12) — análise por tecla voltou ao patamar da baseline (1 MiB/meio: 7,78 → 7,38 ms) e o refiltro com a
lista aberta ficou muito mais barato (64 KiB/fim 0,37×; 1 MiB/meio 0,14×; 1 MiB/início 71 ns). Detalhe completo em
[performance](auto-complite/performance.md), [preemptive-autocomplete](auto-complite/preemptive-autocomplete.md) e
[phase-5-preemptive](auto-complite/phases/phase-5-preemptive.md).

## W0 — Política de atalhos do autocomplete (18/09/2026) — CONCLUÍDO

Lote reservado à política de teclado do autocomplete determinístico, sem tela de edição de atalhos. Build 0 avisos;
suíte completa **1170 aprovados, 0 falhas**.

- `EditorCommandScope { Global, List, Snippet, Inline }` (novo) e doze `EditorCommandIds` substituem a arbitragem
  descritiva por resolução por escopo: `EditorCommandDispatcher.Match(keyEvent, scope)` responde só dentro do escopo
  consultado pelo chamador (lista aberta › sessão de snippet › ghost visível › global). O mesmo gesto pode ser padrão
  de comandos diferentes em escopos diferentes (`Tab`/`Esc` servem lista, snippet e ghost); duplicidade **no mesmo
  escopo** torna a sessão ilegível.
- **`Ctrl+.` removido dos padrões** por decisão explícita do produto. `Ctrl+Espaço` é agora o único gatilho padrão do
  básico explícito; `Ctrl+;` continua reconhecido para IA explícita, mas sem runtime — nunca insere `;`, nunca abre a
  lista tradicional no lugar, nunca dispara consulta; a aba apenas informa indisponibilidade. Como os padrões nunca são
  gravados na sessão, um override de `Ctrl+.` já salvo por um usuário continua legível e funcional, e nunca é
  removido, adicionado ou reescrito. `EditorKeyBindings.CurrentVersion` permanece `1`; não há migração de dado.
- **Três defeitos corrigidos** em `EditorCommandDispatcher.Matches`: (1) `Ctrl` sozinho abria a lista e uma tecla
  desconhecida sem modificador aceitava o ghost, porque a decisão terminava em
  `gesture.Key == keyEvent.PhysicalKey || gesture.Symbol == keyEvent.PhysicalSymbol` e um evento só com modificador
  produz `Symbol`/`PhysicalKey`/`PhysicalSymbol` todos `null`, casando por `null == null`; a correção decide pela
  espécie do gesto (tecla nomeada por identidade de tecla; pontuação pelo símbolo produzido pelo layout, com o físico
  como alternativa) e um evento sem `HasTrigger` nunca casa. (2) Gestos ligados a `Enter` físico nunca casavam, porque
  o enum `Key` do Avalonia nomeia o valor físico `"Return"` e `EditorKey` o chama de `Enter`. (3) A navegação por
  `↑`/`↓` era um no-op silencioso: trocar o `ItemsSource` por um array com a mesma instância selecionada fazia o
  `ListBox` reemitir `SelectionChanged` e desfazer o movimento recém-aplicado.
- Mudança de cursor e de seleção agora apenas invalidam/cancelam a sugestão; só alteração de texto agenda uma nova.
- Textos de UI passaram a derivar dos gestos efetivos (`WorkspaceTabViewModel.GestureText`), com projeção de exibição
  em pt-BR (`↑`, `↓`, `Esc`, `Ctrl+Espaço`); o formato canônico persistido (`EditorKeyGesture.ToString()`) não mudou.
- **Limitação conhecida, registrada e não resolvida:** sem `Ctrl+.`, `Ctrl+Espaço` é o único disparo padrão do básico
  e colide com a troca de método de entrada (IME) em Windows e Linux. Esta meta não entrega tela de edição de
  atalhos; o único contorno para quem é afetado é um override manual em `EditorKeyBindings`.
- **Pendências que não podem ser declaradas concluídas:** homologação em layouts físicos reais (ABNT2 e US em
  Windows; X11 e Wayland em Linux), IME real, leitor de tela e MongoDB real — teste Headless não substitui nenhuma
  delas. As Fases 1, 3, 4 e 5 do plano de autocomplete seguem em aberto; W0 cobriu somente a política de teclado.

Detalhe: [editor-integration.md](auto-complite/editor-integration.md#arbitragem-de-teclado),
[AC-08](auto-complite/decisions.md#ac-08--atalhos) e [estado da Fase 2](auto-complite/phases/phase-2-traditional-autocomplete.md#estado-da-implementação).

## Autocomplete tradicional — fechamento de lacunas (17/09/2026) — CONCLUÍDO

Entrega focada em fechar defeitos que violavam critérios de aceite do autocomplete determinístico, mais o backlog já
especificado na documentação. **Nenhuma funcionalidade de IA foi usada ou adicionada**: o caminho continua offline,
sem ONNX, sem rede e sem consulta MongoDB durante a digitação.

### O que foi corrigido

1. **Lex único por requisição.** `CompletionContextEngine` tokenizava o documento no modo do dialeto e o `ShapeWalker`
   tokenizava tudo de novo no modo padrão `Script`, ignorando o dialeto. Agora há sobrecarga
   `ShapeWalker.Walk(text, tokens, caret, role, definition, rootShape, ct)` que recebe os tokens já produzidos; a
   sobrecarga antiga preserva assinatura e delega.
2. **Agregação passou a estreitar.** Em `EditorDialects.AggregationJson` o documento é um pipeline nu, sem chamada
   envolvente, então `FindCall` falhava e nada estreitava: a posição de nome de estágio oferecia
   `Field | QueryOperator | AggregationStage` indiscriminadamente. O motor agora semeia o shape raiz `Pipeline`
   — **apenas como fallback**, quando não há chamada catalogada.
3. **Vírgula devolve o quadro da chave.** Defeito encontrado durante a entrega: `ShapeWalker.Descend` empilhava um
   quadro ao casar `chave:` e nunca o desempilhava na vírgula que encerra aquele valor. Consequência real: depois de
   `find({ status: 1, ` a forma ficava presa em `FieldCondition`, cujo conjunto de chaves é vazio, o estreitamento era
   descartado e **estágios de agregação eram oferecidos dentro de um filtro `find`** — exatamente o que o critério de
   aceite proíbe.
4. **Acesso a metadados guiado pelo gatilho.** `MetadataAccess.Peek` era fixo no código. Agora
   `CompletionTrigger.Invoked` mapeia para `LoadIfNeeded` e digitação (`TriggerCharacter`/`Automatic`) continua em
   `Peek`, nunca agendando trabalho remoto. `CompletionAutoOpenOnTrigger`, que era inerte, passou a valer.
5. **Fluxo de campos do pipeline.** `PipelineInfo` existia e era testado, mas `PipelineStage` não tinha produtor algum.
   Foi escrito `Context/PipelineStageReader`, e os campos inferidos entram por `LocalSchemas` com o sinalizador
   `RestrictFieldsToLocalSchemas` — não por `LocalSymbols`, que o `CompletionService` emite incondicionalmente e
   vazaria campos para posições de operador e de nome de estágio (ver ADR-041).
6. **Descarte de resposta obsoleta e cancelamento.** `CompletionResponse.IsFor` existia e nunca era chamado. A aba
   passou a usar `EditorRequestScope` próprio, com `IsFor` como guarda explícita; **um CTS por aba, nunca compartilhado**.
7. **Sinal de uso ligado ponta a ponta.** `RankingProfile.UsageWeight` foi aplicado no ranqueamento e, como o
   `CompletionUsageTracker` não tinha **nenhum produtor em produção**, o aceite e o desfazer do editor passaram a
   alimentá-lo. É memória de sessão, só nomes, nunca persistida em LiteDB nem na sessão de workspace.
8. **Alocação do ranqueamento.** Os realces deixaram de ser materializados para todo candidato analisado e passaram a
   ser construídos só para os sobreviventes do top-K.

### Evidência

- `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore` → **0 avisos, 0 erros**.
- `dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore` → **1 156 aprovados, 0 falhas** (baseline de entrada: 1 127).
- Alocação remedida por componente em [performance](auto-complite/performance.md); orçamento de 64 KB por tecla
  cumprido com folga em todos os componentes determinísticos medidos.

### Decisão de encerramento — 17/09/2026

A meta foi **marcada como concluída por decisão do responsável**, com as pendências abaixo aceitas em aberto. Registro
explícito para quem ler depois: o critério de aceite original exigia que a latência atendesse ao limite documentado, e
**essa evidência não foi produzida**. O job completo do BenchmarkDotNet foi desbloqueado (os worktrees de
`.claude/worktrees` foram removidos) mas não foi executado, por decisão de não gastar mais tempo nesta sessão.
Portanto os gates de p95/p99 estão **aceitos como pendentes, não cumpridos**. Nenhuma medição foi estimada,
extrapolada ou inferida para preencher essa lacuna.

### Pendências aceitas em aberto — NÃO declarar cumpridas

- **Orçamento de alocação por tecla ESTOURADO em coleção grande.** Remedido em 18/09/2026: 43,39 KB com 100 campos,
  **174,72 KB com 1 000 e com 10 000 campos**, contra o limite de 64 KB. A causa é `MetadataCatalogSource.Describe`,
  que monta o texto de apresentação de cada campo por candidato, inclusive para os descartados pelo top-K. Não
  corrigido nesta entrega. A afirmação anterior de que o orçamento estava cumprido valia só para o catálogo de
  linguagem embarcado, não para campos vindos de metadados.
- **Gates de latência p95/p99 sem evidência.** As medições desta entrega são de alocação, não de latência. O job
  completo do BenchmarkDotNet não concluiu nesta sessão. Nenhum gate de p95 da Fase 2 pode ser declarado cumprido.
- **`TypeMismatchPenalty` continua dado morto.** `CompletionContext.ValueType` passou a ser populado, mas descobriu-se
  descasamento semântico: `ValueType` é a forma primitiva da posição de **valor**, enquanto `CatalogSymbol.ApplicableTypes`
  descreve o tipo BSON do **campo** a que o operador se aplica, lido em posição de **chave** — onde `ValueType` é nulo.
  Aplicar a penalidade exige propagar `ApplicableTypes` até o `CompletionItem` e resolver o tipo do campo em
  `ParentPath`. Não foi feito, e nenhum atalho por texto de apresentação foi usado.
- **Elo `Stage → GroupBody` não resolvido.** `ShapeWalker.Match` trata `Fixed`/`Operator`/`FieldPath`/`Dynamic` e ignora
  `Exclusive` e `rule.Names`; o elo vive em `CatalogSymbol.ValueShape`. Logo `[{ $group: {` reporta `Stage`, não `GroupBody`.
- **Matriz de 18 PNGs de homologação visual** não produzida; fora do escopo acordado desta entrega.
- **Estouro do highlighting** (3,5 ms em 64 KiB contra 2 ms por tecla) não corrigido.
- **Homologação real pendente**: nada aqui foi verificado contra MongoDB real, leitor de tela ou diálogos nativos.
  Toda a evidência é de teste headless e de medição local.

## Consultas avançadas — incremento de 14/09/2026

**Meta textual validada:** joins e evolução de campos concluídos para os stages do escopo; inferência local com limites e sem execução. Campo derivado inserido via Ctrl+Espaço e desfeito no editor real. Suíte completa **650 aprovados, 0 falhas**, `phase2-acceptance.trx`; auditoria requisito a requisito em [27](backlog/27-consultas-avancadas.md). Os checkpoints abaixo são históricos e não representam pendências atuais da meta. Release, catálogo amplo e homologação nativa permanecem separados.

Revisão de histórico/contexto/diagnósticos: **643 aprovados, 0 falhas**, `phase2-final-audit.trx`, build sem avisos/erros. Histórico de Agregação com snapshot e opt-out; erro de gravação visível; origens de campos filtradas e wrappers BSON excluídos; erros seguros de comando; seleção real no MongoDB 8.0.30 sem fallback. 18 novas imagens da janela Histórico, além das 18 de validação; 600 claro/720 escuro inspecionados. Pendentes: joins/evolução de schema nas sugestões e revisão final das jornadas.

Checkpoint final deste incremento: suíte regular **623 aprovados, 0 falhas, 0 ignorados**, `phase2-current.trx`. Testes Explicit de IA não integram esse total.

Implementados catálogo dos 12 stages com distinção lexical, validação offline com seleção do erro, proteção compartilhada de `$out`/`$merge` e explain bruto do modo Agregação. Snapshots e cancelamento testados entre abas; resultados anteriores conservados durante explain. Fixture MongoDB 8.0.30 de joins/arrays/facet e plano real aprovada. Build sem avisos/erros; 29 testes focados aprovados. O primeiro checkpoint da suíte completa teve 617 aprovados antes da inclusão de validação/explain. [Escopo, evidência e trabalho restante](backlog/27-consultas-avancadas.md); o aceite completo da fase permanece aberto.

## Situação vigente e revisão documental — 13/09/2026

🚧 Próxima entrega: **v0.5.0 MVP**. Última tag alpha identificada localmente: **v0.1.1-alpha**, seguida pelo checkout inspecionado `e806ae4`; publicação remota não verificada. [Roadmap](09-plano-de-implementacao.md) substitui F0–F7 por v0.5.0–v1.0.0, com exclusões/dependências/aceite; [catálogo](03-catalogo-funcional.md) e [inventário](24-inventario-roadmap.md) distinguem implementado, parcial, planejado e experimental.

Conferidos serviços concretos MongoDB/Console/mongosh, persistência, UI, exportador JSON, formatador de apresentação, editor AvaloniaEdit, autocomplete/IA, testes e workflow de versão. CSV e formatação geral de query/script não têm caminho integrado identificado e permanecem bloqueadores do MVP. Administração/scripts/IA já presentes são antecipações, sem remoção ou duplicação.

A evidência de testes datada mais recente registra **530 aprovados e 1 falha** na revisão de datas. Isso não determina o resultado do checkout atual: a suíte não foi reexecutada nesta tarefa documental. Históricos anteriores de TextBox, ausência de servidor e contagens menores permanecem registros das respectivas datas; o editor atual usa AvaloniaEdit e existe evidência posterior de Console com dois servidores locais. Não transformar esses registros em homologação Linux, GPU, mongosh ou diálogos nativos.

Alterados somente documentos: README/índice, visão, catálogo, roadmap, inventário, ADR-035, guia e documentos associados. Referências comparativas proprietárias retiradas; MIT, avisos de terceiros, dependências, binários e comportamento preservados. Verificação documental registrada na matriz.

## Registros históricos de implementação

Reorganizado em 13/09/2026; registros abaixo preservam suas datas. Este documento é o registro operacional do desenvolvimento; o plano continua sendo a referência de escopo em [09-plano-de-implementacao.md](09-plano-de-implementacao.md).

## Chat IA revisável do Console — 12/09/2026

Implementado painel fixo à direita por aba, separado do autocomplete preditivo. O contexto estruturado é bounded e transitório; histórico e cancelamento são próprios da aba; respostas obsoletas não atravessam editor/destino. A resposta exibe explicação, código e diff sem aplicação automática. Escritas e destruições exigem confirmação adicional; aplicação segue o undo/foco do editor e nunca executa a consulta nem altera o Explorer.

Evidência automatizada: `AiChatTests` cobre o exemplo de filtro/limite, limites do contrato, classificação de risco e rejeição de proposta obsoleta. Ainda pendentes homologação com um provedor conversacional real, leitura de tela e inspeção dos PNGs nas 18 combinações previstas.

## Revisão UI/UX implementada — 10/09/2026

- Modal de conexões pesquisável com cadastro, URI rápida, teste, abertura, duplicação e remoção; ações de salvar/voltar ficam no rodapé fixo. Senhas de formulário são limpas entre edições.
- Explorer de bancos/coleções carregados sob demanda. Abrir coleção cria/ativa consulta, sem executar. Perfil alterado invalida a conexão aberta e aplica a política atual.
- Abas independentes com editor textual de consulta/script/agregação e resultados/mensagens/erros abaixo; cancelamento e contexto por aba. Banco explícito no runner mongosh com literal serializado, sem campos duplicados no formulário de consulta.
- Paleta azul clara/escura, preferência Sistema, tipografia compacta configurável, foco/atalhos e splitters persistidos. Placeholders habilitados não herdam a opacidade 0,5 do template Fluent.
- Recuperação de rascunhos, debounce 750 ms, opt-out geral/por conexão, entrada JSON opt-in e migração aditiva; falhas preservam o conteúdo e ficam visíveis.
- Ferramentas administrativas continuam disponíveis em janela contextual: documentos, lote, índices, análise/schema, coleções, transferências e administração. A consulta permanece no editor textual com autocomplete, paginação, histórico e arquivos; não há construtor de consulta com múltiplos campos.
- Design system, ADRs, plano, guia, fontes e matriz atualizados; AGENTS.md na raiz e no Desktop criados.

### Evidência desta revisão

| Gate | Windows | Ubuntu/WSL2 |
| --- | --- | --- |
| Dependências travadas | Restore --locked-mode aprovado | RestoreLockedMode aprovado |
| Build .NET 10.0.400 | 0 avisos, 0 erros | Compilação aprovada |
| NUnit | **244/244**, 0 ignorados | **244/244**, 0 ignorados |
| Renderização Avalonia/Skia | 18 combinações tema/tamanho/DPI + modal/formulário/fonte ampliada | Mesma matriz aprovada |
| Concorrência e persistência | Contexto, cancelamento, retorno fora de ordem, política e falhas aprovados | Mesmos testes aprovados |

Logs finais: `chuke_ESILVA-PC_2026-09-10_07_43_02_net10.0.trx` e `_Esilva-Pc_2026-09-10_07_42_42_net10.0.trx`, no diretório TestResults do projeto de testes. Imagens completas em ui-evidence no diretório de execução; prévias estáveis em [claro](ui/preview-claro.png) e [escuro](ui/preview-escuro.png).

A primeira verificação Linux de retorno de foco encontrou dependência temporal do recarregamento de perfis; o foco agora retorna antes desse I/O e o teste aguarda a tarefa real da modal. A rodada final passou nos dois sistemas. O build isolado usou `-p:UsedAvaloniaProducts=` para não depender da escrita externa de telemetria; analisadores e warnings-as-errors permaneceram ativos.

Limites explícitos: renderização Headless usa os controles reais e dados simulados, sem MongoDB/mongosh. Servidor, autenticação real, leitor de tela, seletores nativos e integração com gerenciadores de janelas permanecem na homologação funcional. Não foram encontradas instalações de mongosh/mongod nos ambientes inspecionados. CI publicada ainda não foi executada.

## Situação do produto completo

| Área | Estado | Evidência |
| --- | --- | --- |
| Fundação .NET 10 | Em implementação | `global.json`, solução `.slnx`, versões centralizadas e cinco arquivos `packages.lock.json` criados; arquitetura atual documentada |
| CI Windows/Linux | Em implementação | Workflow GitHub Actions executa restore travado, build e NUnit em `windows-latest` e `ubuntu-latest`; falta execução publicada |
| Release Windows/Linux | Em implementação | `release.yml` dispara em tags `v*`, usa runners self-hosted Windows/Linux, executa restore travado, build e NUnit e publica executável único self-contained `win-x64`/`linux-x64` com versão da tag no GitHub Releases; `RuntimeIdentifiers` adicionados ao Desktop para manter o lock file válido; falta execução publicada |
| Workspace LiteDB | Em implementação | Repositório de perfis com arquivo local, índice de nome e execução serializada |
| Conexões MongoDB | Em implementação | Perfis múltiplos no LiteDB, pastas, favoritos, ambiente, cor e tags persistidos, criação/edição/duplicação/remoção, teste `buildInfo` e lista/criação capped/renomeação/remoção protegida de coleções pelo driver oficial |
| Consulta BSON | Em implementação | Find textual limitado com filtro, projeção/ordenação/paginação (`skip`), `hint` e `maxTimeMS` expressos no editor, Explain `executionStats`, contagem exata/estimada, valores distintos limitados no servidor e resultados Extended JSON na UI |
| Agregações | Em implementação | Pipeline JSON validado e limitado pelo driver oficial; aba de edição e resultados criada |
| Autocomplete MQL | Em implementação | Catálogo local de operadores e campos inferidos dos resultados carregados ou de uma amostra explícita, limitada a 200 documentos e mantida apenas na memória da coleção |
| CRUD de documentos | Em implementação | Insert, replace, update parcial com operadores/upsert, insertMany limitado, delete-one e delete-many; filtros de alteração não vazios, prévia somente leitura do primeiro documento e interfaces de documento/lote |
| Índices | Em implementação | Listagem, criação e remoção por nome (com `_id_` protegido), chaves BSON, nome, único, esparso, oculto, TTL, filtro parcial e collation; interface na aba Índices |
| Modo script JavaScript + JSON | Em implementação | Runner `mongosh --norc`, entrada EJSON, API `slop.results`, salvar/abrir `.js`, histórico local de caminhos e separação entre console e resultados estruturados |
| Interface Avalonia | Revisão UI/UX implementada; homologação nativa pendente | Explorer de bancos, modal de conexões, abas independentes e painéis verticais; ferramentas existentes preservadas; renderização Headless/Skia aprovada no Windows e Ubuntu em 10/09/2026 |
| Testes NUnit | 244/244 aprovados nos dois sistemas | Em 10/09/2026, cobertura anterior de BSON, perfis, administração e transferências acrescida de sessão, isolamento por aba, falhas, concorrência, temas, foco e atalhos; integração real permanece separada |
| Histórico de consultas | Em implementação | Texto de consultas bem-sucedidas pode ser persistido no LiteDB sem URI ou credenciais; operador pode desativar a persistência, e a lista é limitada por perfil para reabrir o texto no editor |
| Consultas salvas | Em implementação | Consultas nomeadas guardam o texto completo, favorito e contexto do perfil; podem ser recarregadas, atualizadas ou removidas sem reconstrução por campos auxiliares |
| Histórico de scripts | Em implementação | Caminhos absolutos de arquivos `.js` salvos ou abertos ficam no LiteDB, sem conteúdo ou credenciais; a lista é limitada a 50 itens e pode ser desativada |
| Dependências | Em implementação | Avalonia 12.1.2 e MongoDB.Driver 3.11.1 restaurados com lockfiles; documentação oficial da linha atual 3.x revisada em 07/09/2026 |
| Credenciais | Estratégia revisada em 10/09/2026 | URI direta ou `${ENV.get("chave")}` opcional; ambientes locais com valores próprios. `${NOME_DA_VARIAVEL}` legado preservado. Cofre nativo pendente; runner usa ambiente do processo filho, sem senha nos argumentos |
| Segurança de escrita | Em implementação | Perfil pode ser criado como somente leitura; camada de serviço bloqueia CRUD, índices, importação, criação de coleção e scripts |
| Transferência lógica | Em implementação | Exportação limitada com manifesto/Extended JSON e importação por upsert de `_id`, sem apagar o destino |
| Administração de leitura | Em implementação | Status do servidor, topologia via `hello`, operações correntes, usuários/papéis em modo leitura, `dbStats` do banco e `collStats` da coleção selecionada, exibidos em Extended JSON e sujeitos aos privilégios MongoDB |
| Administração de alteração | Em implementação | Remoção de banco de usuário com confirmação textual exata; bancos internos `admin`, `config` e `local` são bloqueados; `killOp` exige ID numérico repetido exatamente e respeita modo somente leitura; criação de usuário exige confirmação e não persiste senha |
| Auditoria local | Em implementação | Criação/remoção de coleções e bancos, criação/remoção de usuários, alterações de papéis e `killOp` ficam registradas no LiteDB como metadados; URI, senha, papéis JSON e conteúdo de documentos não são aceitos; retenção e exportação JSON limitadas a 500 entradas |
| Administração avançada | Pendente | Ações de alteração, topologia e Atlas seguem as fases do plano |
| Validação com mongosh | Pendente por ambiente | `mongosh` não está instalado neste host; parser e runner foram cobertos sem processo externo, mas falta teste contra MongoDB real |

## Incrementos concluídos neste ciclo

1. Estrutura de solução .NET 10 com versões centralizadas de pacotes.
2. Modelos e contratos para perfis, teste de conexão e consulta MongoDB.
3. LiteDB para perfis de conexão, com modo `Direct`, diretório local por sistema e separação explícita entre BSON LiteDB e BSON MongoDB.
4. Implementação inicial do driver MongoDB para `buildInfo`, bancos, coleções e `find` limitado.
5. Janela Avalonia inicial e testes NUnit de domínio/LiteDB.
6. CRUD inicial, índices simples e modo script JavaScript + JSON adicionados à camada de aplicação e à interface.
7. Dependências restauradas sem vulnerabilidades transitivas detectadas; cinco arquivos de lock verificados, build sem avisos e a suíte NUnit inicial aprovada.
8. Bloqueio de senha em URI persistida adicionado enquanto o cofre por sistema não está implementado.
9. Autocomplete local para campos e operadores MQL adicionado, sem transmissão de dados a serviços externos.
10. Execução de pipelines de agregação JSON adicionada ao driver, serviço, interface e testes de validação.
11. Exportação lógica de banco em Extended JSON, com manifesto, limite explícito por coleção e resultado rastreável na interface.
12. Painel administrativo de leitura para `serverStatus` e `dbStats` adicionado ao driver, serviço e interface.
13. Remoção explícita de perfil de conexão adicionada ao gerenciador local LiteDB.
14. Importação do formato lógico adicionada com validação de manifesto/caminhos e upsert em lotes de 500 documentos.
15. Criação de coleção pelo explorer adicionada, com bloqueio de namespaces internos do MongoDB.
16. Perfis autenticados por referência a variável de ambiente adicionados sem armazenar segredo no LiteDB.
17. Modo somente leitura aplicado de forma defensiva à camada de serviço e exposto na criação de perfil.
18. Criação de índices passou a suportar opções único, esparso e TTL.
19. Matriz de CI para Windows e Linux adicionada com restore travado, build e testes.
20. Projeção e ordenação opcionais passaram a ser editáveis no filtro de consulta e foram adicionadas à cobertura de domínio.
21. Update parcial com operadores MongoDB e opção de upsert adicionado ao CRUD e à aba Documentos.
22. Erro de parsing XAML no placeholder de update corrigido; build Avalonia voltou a passar sem avisos.
23. Edição de perfil passou a preservar o ID LiteDB, evitando duplicação ao alterar uma conexão.
24. Explain `executionStats` adicionado ao driver, serviço e aba de diagnóstico somente leitura.
25. Salvar e abrir scripts `.js` foi adicionado com caminho informado pelo operador e sem execução automática ao carregar.
26. Inserção em lote limitada a 10000 documentos foi adicionada com opção ordenada e proteção de modo somente leitura.
27. Cancelamento cooperativo foi adicionado ao shell Avalonia: operações recebem `CancellationToken`, o rodapé mostra o estado e o operador pode solicitar cancelamento; efeitos já enviados ao MongoDB não são revertidos.
28. Resultados de consultas, agregações, índices, estatísticas e identificadores `_id` passaram a usar Canonical Extended JSON para preservar tipos BSON, incluindo UUIDs.
29. Exclusão em lote foi adicionada ao serviço e à aba Documentos, com filtro obrigatório não vazio e confirmação visual explícita por checkbox.
30. Consultas passaram a aceitar `skip` e `hint` BSON opcionais; as opções são aplicadas ao `find` e ao comando Explain, com limites de paginação validados.
31. `collStats` foi adicionado ao painel de Administração para diagnóstico de uma coleção selecionada, com validação local e suporte a cancelamento.
32. Remoção de índices por nome foi adicionada com proteção explícita do índice `_id_`, validação de request e atualização da lista após a operação.
33. Consultas passaram a aceitar `maxTimeMS` entre 1 ms e 10 minutos; o limite é enviado ao servidor no `find` e no Explain.
34. Histórico de consultas foi persistido no LiteDB, com limite de 50 itens na UI, filtro por conexão e reaplicação de filtro, projeção, ordenação, `skip`, `hint` e `maxTimeMS`.
35. A persistência do histórico ganhou um controle explícito na UI; ao desativá-la, a lista é limpa e nenhuma nova consulta é gravada no LiteDB.
36. Build completo e suíte NUnit foram executados em 08/09/2026: 0 avisos, 0 erros e 57 de 57 testes aprovados.
37. Histórico de scripts `.js` adicionado ao LiteDB: mantém apenas o caminho absoluto e data de acesso, elimina repetição do mesmo caminho e preenche o campo ao selecionar um item, sem abrir ou executar automaticamente.
38. Persistência temporal do histórico de scripts ajustada para ticks UTC, evitando deslocamento causado pela normalização de `DateTime` do LiteDB; build passou sem avisos e 61 de 61 testes NUnit foram aprovados em 08/09/2026.
39. Prévia somente leitura do primeiro documento correspondente adicionada à aba Documentos para revisão antes de replace ou update; build passou sem avisos e 61 de 61 testes NUnit foram aprovados em 08/09/2026.
40. Consultas salvas adicionadas: nome, favorito, vínculo com perfil e recuperação de todas as opções do `find`; build passou sem avisos e 65 de 65 testes NUnit foram aprovados em 08/09/2026.
41. Gerador de UUID padrão adicionado à aba Documentos: produz `Canonical Extended JSON` com `$binary`, base64 em ordem RFC 4122 e `subType` `04`; build passou sem avisos e 66 de 66 testes NUnit foram aprovados em 08/09/2026.
42. Contagem exata por filtro e estimativa da coleção adicionadas à consulta; a estimativa recusa filtro e a interface declara que ela considera toda a coleção. Build passou sem avisos e 70 de 70 testes NUnit foram aprovados em 08/09/2026.
43. Valores distintos adicionados em aba própria: o filtro atual é aplicado com `$match`, os valores são agrupados por campo e limitados no servidor antes da transferência. Build passou sem avisos e 75 de 75 testes NUnit foram aprovados em 08/09/2026.
44. Perfis favoritos adicionados ao workspace LiteDB: são persistidos, exibem estrela na lista e aparecem antes dos demais perfis. Build passou sem avisos e 77 de 77 testes NUnit foram aprovados em 08/09/2026.
45. Editor de perfil passou a registrar ambiente e cor hexadecimal `#RRGGBB`; os valores são validados, normalizados e persistidos no LiteDB. Build passou sem avisos e 79 de 79 testes NUnit foram aprovados em 08/09/2026.
46. Editor de perfil passou a registrar tags separadas por vírgula; as tags são normalizadas, deduplicadas e persistidas no LiteDB. Build passou sem avisos e 81 de 81 testes NUnit foram aprovados em 08/09/2026.
47. Duplicação guiada de perfil adicionada: abre uma cópia editável com novo identificador e preserva URI, banco, ambiente, cor, tags, favorito e modo somente leitura. Build passou sem avisos e 82 de 82 testes NUnit foram aprovados em 08/09/2026.
48. Renomeação de coleção adicionada: valida origem/destino, bloqueia namespaces de sistema, permite substituição do destino somente por escolha explícita e atualiza o explorer após êxito. Build passou sem avisos e 87 de 87 testes NUnit foram aprovados em 08/09/2026.
49. Remoção protegida de coleção adicionada: exige que o operador digite o nome exato da coleção, bloqueia namespaces de sistema e respeita modo somente leitura. Build passou sem avisos e 93 de 93 testes NUnit foram aprovados em 08/09/2026.
50. Criação de coleção passou a suportar modo capped, tamanho máximo em bytes e máximo de documentos; limites exigem modo capped e tamanho positivo. Build passou sem avisos e 97 de 97 testes NUnit foram aprovados em 08/09/2026.
51. Remoção protegida de banco adicionada ao painel Administração: exige confirmação textual exata, bloqueia bancos internos e atualiza o explorer após êxito. Build passou sem avisos e 103 de 103 testes NUnit foram aprovados em 08/09/2026.
52. Criação de índice passou a aceitar filtro parcial BSON/Extended JSON, entregue à opção tipada `PartialFilterExpression` do driver. Build passou sem avisos e 105 de 105 testes NUnit foram aprovados em 08/09/2026.
53. Consulta de operações correntes adicionada ao painel Administração usando o comando somente leitura `currentOp`; a disponibilidade continua dependente de privilégios MongoDB. Build passou sem avisos e 105 de 105 testes NUnit foram aprovados em 08/09/2026.
54. Auditoria local adicionada ao LiteDB para alterações administrativas selecionadas, com lista no painel Administração e rejeição de URI em resumos. Build passou sem avisos e 109 de 109 testes NUnit foram aprovados em 08/09/2026.
55. Autocomplete passou a carregar campos de uma amostra explícita de até 200 documentos da coleção, sem persistir documentos ou schema localmente; o cache de campos é limpo ao trocar a coleção. Build passou sem avisos e 109 de 109 testes NUnit foram aprovados em 08/09/2026.
56. Perfis passaram a registrar no LiteDB a data da última conexão bem-sucedida, sem persistir URI resolvida, senha ou outro segredo. Os testes de domínio e round-trip no repositório LiteDB foram acrescentados; a contagem estática dos atributos NUnit confirma 111 casos declarados. A execução de build/NUnit está pendente porque o executor atingiu o limite de uso antes de iniciar o comando.
57. O explorer passou a aceitar criação de views MongoDB com coleção de origem e pipeline JSON, mantendo opções capped incompatíveis bloqueadas; dois testes de validação foram acrescentados. A contagem estática esperada passa a 113 casos, aguardando build/NUnit executáveis.
58. Administração de leitura passou a consultar usuários (`usersInfo`) e papéis (`rolesInfo`) no banco `admin`, com resultados Extended JSON e dependência explícita de privilégios. Validação executável continua pendente.
59. A aba Script passou a oferecer seleção nativa Avalonia para abrir e salvar arquivos `.js`; o carregamento continua sem execução automática e o histórico mantém apenas o caminho. Validação executável continua pendente.
60. A auditoria local passou a diferenciar criação de view (`view.create`) de criação de coleção (`collection.create`), mantendo o namespace e o resumo sem conteúdo BSON. Validação executável continua pendente.
61. Views passaram a aceitar atualização protegida do pipeline via `collMod`, com nome exato de confirmação e auditoria `view.update`; materialização permanece fora do escopo. A contagem estática dos atributos NUnit passa a 119 casos declarados após cobertura de origem protegida e pipeline não-array. Validação executável continua pendente.
62. Pipelines de views agora exigem array de documentos tanto na validação local quanto no comando enviado ao servidor; estágios escalares são rejeitados antes da rede. A contagem estática de casos passa a 121 com cobertura equivalente em criação e edição, e a validação executável continua pendente.
63. Após autorização de crédito de reset, a solução compilou sem avisos ou erros e a suíte NUnit aprovou 121 de 121 casos em Windows, em 08/09/2026. Essa execução cobre as regras de perfil, LiteDB, views e pipeline; não substitui a homologação com MongoDB, `mongosh`, Linux ou interface aberta.
64. O histórico de scripts passou a guardar a entrada Extended JSON somente quando o operador marca `Persistir entrada JSON`. O valor fica limitado a 64 KiB, precisa ser um objeto JSON e permanece ausente por padrão; build sem avisos e 125 de 125 testes NUnit aprovados em Windows, em 08/09/2026.
65. A configuração de validação de coleção foi adicionada com `collMod`: aceita documento BSON/Extended JSON, níveis `off`/`strict`/`moderate`, ações `error`/`warn`, confirmação pelo nome exato, bloqueio de namespaces de sistema e auditoria sem conteúdo BSON. Build sem avisos e 132 de 132 testes NUnit aprovados em Windows, em 08/09/2026.
66. A tela passou a carregar o validador, nível e ação existentes via `listCollections` antes de editar a coleção selecionada; ausência da coleção e valores de servidor não reconhecidos retornam erro explícito. Build sem avisos e 132 de 132 testes NUnit aprovados em Windows, em 08/09/2026.
67. Um `$jsonSchema` inicial passou a ser inferido de amostra limitada a 200 documentos, sem persistir documentos, sem marcar campos obrigatórios e sem aplicar mudanças automaticamente. Build sem avisos e 133 de 133 testes NUnit aprovados em Windows, em 08/09/2026.
68. A inferência de schema passou a reconhecer ObjectId, data e Binary/UUID no Canonical Extended JSON como tipos BSON, em vez de tratá-los como objetos aninhados. Build sem avisos e 134 de 134 testes NUnit aprovados em Windows, em 08/09/2026.
69. Consultas passaram a aceitar comentário operacional de até 512 caracteres, enviado ao `find` e Explain e persistido em histórico e consultas salvas no LiteDB para reprodução do contexto. Build sem avisos e 134 de 134 testes NUnit aprovados em Windows, em 08/09/2026.
70. A validação de comentário ganhou cobertura de preservação e de limite excedido; build sem avisos e 136 de 136 testes NUnit aprovados em Windows, em 08/09/2026.
71. Consultas passaram a aceitar `batchSize` entre 1 e 10000 para controlar lotes do cursor e Explain; o valor é persistido e restaurado por histórico e consultas salvas no LiteDB. Build sem avisos e 136 de 136 testes NUnit aprovados em Windows, em 08/09/2026.
72. `BatchSize` ganhou cobertura de preservação e dos limites mínimo e máximo; build sem avisos e 139 de 139 testes NUnit aprovados em Windows, em 08/09/2026.
73. Criação de coleção e view passou a aceitar collation BSON/Extended JSON opcional, validada localmente e adaptada para a opção tipada do driver. Build sem avisos e 139 de 139 testes NUnit aprovados em Windows, em 08/09/2026.
74. Collation ganhou cobertura para documento válido, array inválido e JSON malformado; build sem avisos e 142 de 142 testes NUnit aprovados em Windows, em 08/09/2026.
75. Find e Explain passaram a aceitar collation BSON/Extended JSON opcional no editor de consultas; o documento é convertido para a opção tipada do cursor e para o comando Explain. Build sem avisos e 142 de 142 testes NUnit aprovados em Windows, em 08/09/2026.
76. Collation de consulta ganhou cobertura para documento válido, array inválido e JSON malformado; build sem avisos e 145 de 145 testes NUnit aprovados em Windows, em 08/09/2026.
77. Collation de consulta passou a ser persistida e restaurada no histórico e nas consultas salvas LiteDB, completando a reprodução das opções disponíveis no editor. Build sem avisos e 145 de 145 testes NUnit aprovados em Windows, em 08/09/2026.
78. O teste de ida e volta do histórico passou a cobrir comentário, `batchSize` e collation juntamente com filtro, projeção, ordenação, hint e `maxTimeMS`. Build sem avisos e 145 de 145 testes NUnit aprovados em Windows, em 08/09/2026.
79. O teste de consulta salva passou a cobrir comentário, `batchSize` e collation, além das opções anteriores e do estado favorito. Build sem avisos e 145 de 145 testes NUnit aprovados em Windows, em 08/09/2026.
80. A criação de view deixou de enviar o campo `collation` quando ele não foi informado, evitando um valor BSON nulo inválido no comando `create`. Build sem avisos e 145 de 145 testes NUnit aprovados em Windows, em 08/09/2026.
81. A criação de índices passou a aceitar collation BSON/Extended JSON opcional, validada localmente e convertida para a opção tipada do driver; a aba Índices passou a expor o campo. Build sem avisos e 149 de 149 testes NUnit aprovados em Windows, em 08/09/2026.
82. Perfis de conexão passaram a aceitar uma pasta hierárquica local, normalizada, persistida no LiteDB, preservada em edição/duplicação e usada na ordenação após favoritos. Build sem avisos e 152 de 152 testes NUnit aprovados em Windows, em 08/09/2026.
83. A Administração passou a permitir `killOp` protegido: ID numérico positivo repetido para confirmação, bloqueio de modo somente leitura e auditoria local sem dados de conexão. Build sem avisos e 157 de 157 testes NUnit aprovados em Windows, em 08/09/2026.
84. O painel Administração passou a consultar `hello`, exibindo em Extended JSON a topologia e o papel informado pelo nó MongoDB selecionado. Build sem avisos e 157 de 157 testes NUnit aprovados em Windows, em 08/09/2026.
85. O fluxo de criação de usuário foi implementado no núcleo, serviço, driver e interface: banco selecionado, papéis como array de documentos, confirmação textual e senha não persistida. A validação executável ficou pendente porque o executor rejeitou o `dotnet build` por limite de uso; a suíte permanece registrada em 157 de 157 até nova execução.
86. O cadastro de usuários ganhou limites locais de 1024 caracteres para senha e 32 KiB para papéis JSON. A cobertura NUnit foi ampliada, mas permanece sem execução por causa do limite do executor; a contagem oficial continua em 157 de 157.
87. A Administração passou a remover usuários pelo comando `dropUser`, exigindo nome repetido para confirmação, respeitando modo somente leitura e registrando apenas o alvo na auditoria. A validação executável do incremento aguarda o executor; a contagem oficial continua em 157 de 157.
88. A Administração passou a conceder ou revogar papéis com `grantRolesToUser` e `revokeRolesFromUser`, usando JSON validado, confirmação do usuário, modo somente leitura e auditoria local. A validação executável aguarda o executor; a contagem oficial continua em 157 de 157.
89. O checklist de homologação foi detalhado em [16-checklist-homologacao.md](16-checklist-homologacao.md), cobrindo Windows/Linux, MongoDB real, `mongosh`, CRUD, índices, transferências, scripts, administração e evidências sem segredos.
90. A auditoria estática deste ciclo confirmou que não há `TODO`, `NotImplemented` ou `NotSupportedException` no código; os itens restantes estão concentrados em cofre de credenciais, materialização de views, opções avançadas de índices, métricas históricas e homologação real. O executor informou limite primário de uso de 100%, portanto nenhuma nova execução de build/teste foi declarada.
91. A auditoria local passou a ser exportável para JSON, limitada a 500 metadados e sem URI, senha, papéis ou documentos. A validação executável aguarda o executor; a contagem oficial continua em 157 de 157.
92. A serialização da auditoria foi extraída para `AuditJsonSerializer`, com limite de 500 entradas e testes de array formatado/limite; a execução continua pendente por limite de uso do executor.
93. O executor continua com limite primário de uso esgotado. O usuário determinou que créditos de reset não sejam consumidos; a validação dos incrementos administrativos aguardará a próxima janela normal de uso e, até lá, nenhuma contagem de testes é alterada.
94. A exportação da auditoria passou a exigir extensão `.json` antes de criar diretórios ou escrever o arquivo, reduzindo o risco de formato incorreto; validação executável permanece pendente.
95. O repositório LiteDB passou a reter somente as 500 auditorias mais recentes, removendo itens antigos no mesmo ciclo de gravação; foi acrescentado teste de retenção, ainda aguardando execução.
96. A exportação de auditoria passou a criar exclusivamente arquivos novos, recusando sobrescrever uma evidência existente; validação executável permanece pendente sem uso de crédito de reset.
97. A importação lógica passou a rejeitar manifestos com coleções ou arquivos duplicados, limites/metadados inválidos e contagens que não correspondem ao arquivo Extended JSON antes de escrever a coleção afetada. A validação executável continua aguardando a janela normal do executor.
98. A Administração passou a executar `validate` com `full: true` para coleções de usuário, exigindo confirmação textual, bloqueando perfis somente leitura e registrando o alvo na auditoria LiteDB. Foram incluídos testes NUnit da regra de confirmação; a execução da suíte continua aguardando a janela normal do executor.
99. A criação de índices passou a expor a opção `hidden` do driver MongoDB, permitindo criar um índice mantido pelo servidor mas ignorado pelo otimizador. Foi acrescentado teste NUnit de validação; a compatibilidade por versão do servidor permanece para homologação real.
100. A Administração passou a expor `compact` para coleções de usuário, com confirmação textual, opção explícita `force`, bloqueio de perfil somente leitura, auditoria LiteDB e testes NUnit das regras de alvo. A operação depende de versão, permissões e topologia, que permanecem pendentes de homologação real.
101. A criação explícita de banco foi adicionada por meio de uma coleção inicial: a interface exige banco, coleção e confirmação, bloqueia bancos internos/perfis somente leitura e atualiza o explorer após sucesso. O núcleo recebeu testes NUnit para as regras de criação; a execução continua pendente na janela normal do executor.
102. A janela normal do executor foi restabelecida sem consumo de crédito de reset. `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -v:minimal` concluiu sem avisos ou erros e `dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore -v:minimal` aprovou 192 de 192 testes NUnit no Windows. Esta evidência valida os incrementos locais 85 a 101, mas não substitui a homologação com MongoDB, `mongosh`, Linux ou interface aberta.
103. O autocomplete local ganhou sugestões exclusivas de estágios de agregação no editor de pipeline, reutilizando apenas os campos mantidos em memória da coleção atual. Um teste NUnit cobre a exclusão de operadores que não são estágios; a execução desta alteração aguarda a próxima rodada de validação local.
104. Após o incremento de autocomplete, a solução compilou novamente sem avisos ou erros e a suíte NUnit aprovou 193 de 193 casos no Windows, em 08/09/2026. A homologação externa continua pendente porque este host não possui `mongod`, `mongosh` ou Docker instalados.
105. A importação lógica passou a oferecer seleção nativa de pasta via `StorageProvider`, preservando a possibilidade de informar o caminho manualmente em Windows e Linux. A próxima validação local deve conferir o handler Avalonia junto da suíte existente.
106. Após incluir o seletor de pasta, build sem avisos/erros e 193 de 193 testes NUnit aprovados foram repetidos no Windows, em 08/09/2026. A interação visual do seletor continua pendente de uma sessão com automação nativa disponível.
107. Updates parciais passaram a aceitar `arrayFilters` como array JSON de documentos, validado antes da rede e convertido para `ArrayFilterDefinition` do driver. A interface recebeu o campo opcional e os testes NUnit cobrem JSON válido e inválido; a execução desta alteração aguarda a próxima rodada de validação local.
108. Após incluir `arrayFilters`, build sem avisos/erros e 197 de 197 testes NUnit aprovados foram executados no Windows, em 08/09/2026. A semântica contra arrays reais e permissões do servidor permanece para homologação MongoDB.
109. O update parcial passou a distinguir documentos de operadores e pipelines JSON, convertendo arrays de estágios para `PipelineUpdateDefinition` no driver. A validação e os testes NUnit cobrem formatos válidos e inválidos; a execução do novo incremento aguarda a próxima rodada local.
110. Após corrigir a sobrecarga `StartsWith` indicada pelo analisador, a solução compilou sem avisos/erros e a suíte NUnit aprovou 201 de 201 casos no Windows, em 08/09/2026. Pipelines de update ainda requerem homologação com coleção MongoDB real.
111. Índices existentes passaram a ter visibilidade alterável por `collMod`, com confirmação textual, proteção de `_id_`, auditoria e teste NUnit de domínio. A execução da suíte para esse incremento permanece pendente.
112. Após compilar a alteração de visibilidade de índice, a solução concluiu sem avisos ou erros e a suíte NUnit aprovou 204 de 204 casos no Windows, em 08/09/2026. A alteração por `collMod` ainda requer homologação em MongoDB real.
113. O cadastro rápido de conexão foi adicionado: a URI `mongodb://` ou `mongodb+srv://` é analisada localmente, abre o editor seguinte com URI, banco e nome sugerido preenchidos e continua exigindo revisão/salvamento explícito. Build sem avisos/erros e 210 de 210 testes NUnit aprovados no Windows, em 08/09/2026.
114. A duplicação de documento foi adicionada à aba Documentos: a prévia Extended JSON gera um rascunho de inserção sem o `_id` de nível superior e o operador ainda precisa revisar e escolher Inserir. A URI fornecida para o teste foi analisada localmente sem ser registrada ou exibida; ela preencheu o nome sugerido do perfil e não informou banco padrão. A automação nativa não disponibilizou a janela nesta sessão, portanto a interação visual continua pendente. Build sem avisos/erros e 214 de 214 testes NUnit aprovados no Windows, em 08/09/2026.
115. A criação de índices passou a aceitar `wildcardProjection` BSON quando as chaves incluem `$**`. A validação local exige documento JSON, valores 0/1 e um único modo de inclusão ou exclusão, permitindo a exceção de `_id`; o adaptador a entrega na opção tipada do driver. Build sem avisos/erros e 218 de 218 testes NUnit aprovados no Windows, em 08/09/2026. A compatibilidade por versão do MongoDB permanece para homologação real.
116. `Find-and-modify` foi adicionado à aba Documentos: usa o mesmo filtro, update, upsert e `arrayFilters` da atualização parcial, mas executa uma única chamada atômica e exibe o documento depois da alteração. Build sem avisos/erros e 218 de 218 testes NUnit aprovados no Windows, em 08/09/2026; a semântica em servidor real permanece pendente de homologação.
117. A criação de coleções passou a expor índice clustered: a UI aceita chave BSON de um campo, a validação bloqueia view/capped e o adaptador usa `CreateCollectionOptions<BsonDocument>.ClusteredIndex` do driver. Build sem avisos/erros e 222 de 222 testes NUnit aprovados no Windows, em 08/09/2026. A disponibilidade por versão/topologia do MongoDB permanece para homologação real.
118. A aba Índices passou a carregar estatísticas de uso por `$indexStats`, em operação somente leitura. O ajuste também separou visualmente os controles de criação/listagem dos controles de remoção/visibilidade. Build sem avisos/erros e 222 de 222 testes NUnit aprovados no Windows, em 08/09/2026; permissões e conteúdo da resposta exigem homologação MongoDB real.
119. A Administração passou a consultar a configuração corrente do profiler do banco selecionado por `profile: -1`, sem habilitar, alterar filtro ou registrar coleta. Build sem avisos/erros e 222 de 222 testes NUnit aprovados no Windows, em 08/09/2026; privilégios e resposta de servidor permanecem para homologação real.
120. A aba Resultados passou a exportar a página de consulta carregada como array Extended JSON para arquivo `.json` novo, recusando sobrescrever um destino existente. O serializador valida cada documento antes da escrita. Build sem avisos/erros e 223 de 223 testes NUnit aprovados no Windows, em 08/09/2026.
121. A exportação de resultados ganhou cobertura para documento JSON truncado, que interrompe a serialização antes de gerar um array inválido. Build sem avisos/erros e 224 de 224 testes NUnit aprovados no Windows, em 08/09/2026.
122. A exportação de resultados ganhou cobertura para página vazia, serializada como `[]` válido. Build sem avisos/erros e 225 de 225 testes NUnit aprovados no Windows, em 08/09/2026.
123. A UI do MVP foi reorganizada para priorizar conexão, seleção de banco/coleção, filtro e execução de consulta: painéis receberam maior respiro, ações de manutenção foram recolhidas, gestão de coleções e opções avançadas de consulta começam ocultas e ações principais ganharam dicas contextuais. Build sem avisos/erros no Windows, em 08/09/2026.
124. Histórico e consultas salvas passaram a ficar em uma seção recolhida, mantendo a área de consulta curta no primeiro uso. A exportação dos resultados agora ocupa uma linha própria, explica que exporta apenas a página visível em Extended JSON e nomeia a ação de forma consistente. Build sem avisos/erros e 225 de 225 testes NUnit aprovados no Windows, em 08/09/2026.
125. O formulário aberto pela conexão rápida passou a se apresentar como uma etapa de configuração, informando que a URI já preencheu seus campos para revisão. A mensagem de status do cabeçalho também foi limitada e truncada para preservar ações e título em telas estreitas. Build sem avisos/erros e 225 de 225 testes NUnit aprovados no Windows, em 08/09/2026.
126. O primeiro acesso sem perfil selecionado agora mostra uma orientação central com o próximo passo e um atalho para cadastrar a conexão. A área de banco, coleção e consulta só aparece após selecionar um perfil, evitando comandos desabilitados sem explicação. A propriedade de visibilidade é atualizada junto com a seleção do perfil. Build sem avisos/erros e 225 de 225 testes NUnit aprovados no Windows, em 08/09/2026.
127. A área de banco e coleção agora informa o passo pendente — carregar bancos, escolher coleção ou consultar o contexto pronto — ao lado dos controles correspondentes. A mensagem reage à conexão, banco e coleção selecionados, eliminando a ambiguidade de botões de consulta indisponíveis. Build sem avisos/erros e 225 de 225 testes NUnit aprovados no Windows, em 08/09/2026.
128. A consulta principal ganhou a ação “Ver primeiros documentos”, que define o filtro como `{}` e retorna a paginação ao início. Ela torna explícito o caminho seguro para explorar uma coleção sem exigir que a pessoa edite JSON apenas para remover um filtro anterior. Build sem avisos/erros e 225 de 225 testes NUnit aprovados no Windows, em 08/09/2026.
129. O fluxo de URI passou a informar no próprio formulário que senhas precisam ser trocadas por `${MONGODB_PASSWORD}` antes de salvar e que a variável é resolvida somente durante a conexão. A orientação reduz o erro de tentar persistir uma URI com segredo embutido, preservando a política de credenciais do MVP. Build sem avisos/erros e 225 de 225 testes NUnit aprovados no Windows, em 08/09/2026.
130. A remoção de coleção saiu da área de consulta cotidiana e foi agrupada em “Gerenciar coleções e validação (avançado)”, com descrição e confirmação preservadas. A escolha diminui a exposição de uma ação irreversível durante a navegação normal do MVP. Build sem avisos/erros e 225 de 225 testes NUnit aprovados no Windows, em 08/09/2026.
131. Perfis configurados como somente leitura agora exibem um aviso na área de banco e coleção, explicando antecipadamente por que CRUD, índices, scripts e ações administrativas de escrita estão bloqueados. Build sem avisos/erros e 225 de 225 testes NUnit aprovados no Windows, em 08/09/2026.
132. As ações que movem o fluxo principal — continuar URI, salvar perfil, carregar bancos e executar consulta — receberam ênfase tipográfica consistente. A hierarquia ajuda a identificar o próximo passo sem introduzir cores ou estilos que possam conflitar com o tema nativo do Windows ou Linux. Build sem avisos/erros e 225 de 225 testes NUnit aprovados no Windows, em 08/09/2026.
133. A execução de consulta ganhou atalhos F5 e Ctrl+Enter, também documentados na dica do botão. Os gestos usam o mesmo comando e respeitam as condições já existentes de perfil, banco e coleção selecionados. Build sem avisos/erros e 225 de 225 testes NUnit aprovados no Windows, em 08/09/2026.
134. Para caber melhor em janelas menores, as dez abas de resultado foram agrupadas em quatro níveis principais: Resultados, Análise, Operações e dados, e Script JavaScript + JSON. As abas internas preservam todas as ferramentas existentes, reduzindo a sobrecarga horizontal da navegação principal. Build sem avisos/erros e 225 de 225 testes NUnit aprovados no Windows, em 08/09/2026.
135. O autocomplete de MQL deixou de ser apenas informativo: sugestões de campo e operador podem ser selecionadas e aplicadas ao token atual do filtro. Com `{}` como ponto de partida, um campo cria um esqueleto JSON válido para revisão. Cobertura unitária verifica substituição de token e criação do esqueleto; build sem avisos/erros e 227 de 227 testes NUnit aprovados no Windows, em 08/09/2026.
136. A lista de autocomplete é invalidada quando o filtro é editado manualmente ou quando a coleção muda. Isso impede aplicar uma sugestão baseada em campos de outro contexto e pede uma nova solicitação explícita de sugestões. Build sem avisos/erros e 227 de 227 testes NUnit aprovados no Windows, em 08/09/2026.
137. O atalho Esc passou a disparar o mesmo cancelamento cooperativo já disponível no rodapé, com dica visível no botão. O comando só fica ativo durante uma operação em andamento. Build sem avisos/erros e 227 de 227 testes NUnit aprovados no Windows, em 08/09/2026.
138. A barra de script foi dividida entre ações de arquivo e entrada Extended JSON/execução, reduzindo a largura de uma única linha de controles e destacando a ação de executar no `mongosh`. Build sem avisos/erros e 227 de 227 testes NUnit aprovados no Windows, em 08/09/2026.
139. O perfil passou a aceitar usuário e senha em campos separados. A URI persistida recebe somente o marcador `${MONGODB_PASSWORD}`; a senha fica em cofre de sessão por perfil, fora do LiteDB e removida ao fechar a IDE. Testes verificam isolamento e remoção da credencial da sessão; build sem avisos/erros e 229 de 229 testes NUnit aprovados no Windows, em 08/09/2026.

## Validação pendente deste ciclo

- Validar CRUD e índices contra MongoDB real em Windows e Linux.
- Validar modo script com `mongosh` e MongoDB real em Windows e Linux.
- Validar exportação lógica contra fixture MongoDB, incluindo UUIDs, datas e uma coleção acima do limite.
- Validar o fluxo nativo no Windows com uma instância MongoDB local; renderização automatizada aprovada nesta revisão.
- Homologar MongoDB/mongosh e integrações nativas no Linux; build/NUnit/renderização automatizada aprovados nesta revisão.

## Próximos incrementos

1. Adicionar armazenamento seguro de credenciais.
2. Validar CRUD, índices, views, validadores, usuários/papéis e script contra MongoDB/mongosh reais.
3. Adicionar restauração por Database Tools depois que o cofre de credenciais estiver disponível.
4. Adicionar outras operações administrativas de alteração com confirmação e auditoria.

## Critério de atualização

Cada mudança deve atualizar esta tabela com estado, evidência e resultado de validação. “Concluído” só é permitido quando o requisito correspondente tiver código, teste adequado e evidência de execução registrada. Falhas de ambiente, dependências ou infraestrutura devem permanecer registradas aqui até resolução.


## Identidade visual — 10/09/2026

- Referências locais docs/ui preservadas. Símbolo próprio vetorial do recipiente inclinado, duas assinaturas SVG, oito PNGs (16–512), ICO de sete resoluções e 15 ícones de comandos. Fonte das geometrias: Brand.axaml; gerador reproduzível em tools/BrandAssets.
- Aplicação no título/ícone da janela, cabeçalho, estado sem abas, explorer, comandos de arquivo/execução e abas de saídas. Tokens frios azul/violeta e ajustes de contraste; sessões, contexto, comandos e invariantes mantidos.
- Documentação atualizada: design system, ADR-022, plano, guia, catálogo, matriz e manual de identidade. Prévias reais em docs/ui/preview-*.png.
- Windows: restore travado; build sem avisos/erros; 244/244 testes. Inspeção dos PNGs nas 18 combinações, formulário/modal, fonte ampliada e estado vazio. Testes ampliados para contraste violeta/superfícies secundárias, limites da barra e ícone/estado vazio, sem relaxar asserções.
- Ubuntu/WSL: tentativa de revalidação encontrou ausência do SDK solicitado em global.json (10.0.400). A validação Linux anterior não é reapresentada como prova desta mudança. Shell nativo, leitor de tela e integração MongoDB real permanecem pendentes.


TRX final desta identidade: Windows, 244/244 (artefato histórico não disponível neste checkout: `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-10_13_34_57_net10.0.trx`). Build do gerador também aprovado sem avisos/erros, com analisadores habilitados.


## Estratégia de credenciais — revisão de 10/09/2026

Esta revisão substitui as restrições históricas descritas nos itens 8, 16, 56, 129 e 139: agora é permitido persistir credenciais diretas na URI por escolha do usuário. Referências já cadastradas permanecem referências; nenhum secret é convertido automaticamente.

Adicionados módulo Ambientes / Key Vault, ambientes customizados e seleção ativa persistida, `ENV.get()` no editor/URI, compatibilidade `${NOME}`, snapshot por operação e transporte de credenciais ao runner pelo ambiente do processo filho. UI informa ausência de criptografia nativa. Salvar ambientes exige reabrir conexões, preservando textos e execuções iniciadas. Validação desta revisão registrada na matriz; as contagens históricas acima não representam automaticamente o novo código.

Validação final desta revisão: restore travado e build Windows com 0 avisos/erros; **259/259 testes NUnit aprovados**, incluindo renderização. TRX: `tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-10_14_34_49_net10.0.trx`. Bootstrap JS também exercitado com Node/stubs, sem servidor real. PNGs de Ambientes (18 combinações) e formulários de conexão foram inspecionados; uma sobreposição foi corrigida e recebeu verificação geométrica. Linux, MongoDB/mongosh reais, leitor de tela e cofre nativo permanecem pendentes.

## Database Explorer — revisão de 10/09/2026

Implementação entregue: raízes de todas as conexões, carga progressiva, desconexão e refresh granular preservando expansão; menus por nível; detalhes de banco, coleção, índice e topologia; seleção explícita de membro suportado compartilhada pelo driver e mongosh. Abas existentes mantêm seu destino, e a recuperação de sessão não conecta automaticamente.

Documentos possuem página em JSON e árvore estruturada, copiar/abrir no editor, inserir/editar/excluir com confirmação e contexto fixo. Edição relê o documento completo antes de confirmar e detecta conflito; resultados abertos no editor ficam fora dos rascunhos automáticos. Scripts CRUD, índices e administração são gerados sem executar. Serviços de metadados e modelos de apresentação mantêm o driver fora da UI.

Restore travado e build Windows aprovados, 0 avisos/erros; **276/276 testes aprovados**, nenhum ignorado. TRX final (artefato histórico não disponível neste checkout: `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-10_23_11_35_net10.0.trx`). Inspecionados 18 PNGs do explorer e editor de documento nos dois temas. Design system, ADR-025, catálogo, plano, guia e matriz atualizados. [Guia e prévias](19-database-explorer.md); [rastreabilidade dos 13 critérios](15-matriz-de-validacao.md#database-explorer--10092026).

Homologação externa permanece pendente: MongoDB/mongosh reais, autenticação/permissões, DNS SRV/TXT e topologias reais, Linux nativo e leitor de tela. A evidência automatizada não equivale a esses cenários.

## Console JavaScript — revisão de 11/09/2026

Console substitui Consulta JSON como modo principal. Entregues runtime Jint/proxies/driver, db por conexão/banco, getConnection e ConnectionPool/indexadores, ENV capturado, cursores limitados, CRUD/índices, múltiplos resultados, mensagens, histórico, autocomplete assíncrono e Ctrl+Enter por seleção/statement. Explorer gera os comandos sem executar; consultas e rascunhos JSON antigos são convertidos preservando opções.

Proteções permanecem por destino efetivo: somente leitura, filtro não vazio, destinos/índices protegidos, confirmação e auditoria. Origem do resultado acompanha edição de documento. O modo Script/mongosh permanece separado. Dependências permissivas Jint 4.16.0 (BSD-2-Clause) e Acornima 1.7.0 (BSD-3-Clause); nome e MIT do produto preservados.

Restore travado e build aprovados, sem avisos/erros. **294/294 testes aprovados**, nenhum ignorado. TRX final (artefato histórico não disponível neste checkout: `../tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-11_07_55_19_net10.0.trx`). Fixture real com dois MongoDB Community 8.0.30 locais validou API/CRUD/índices e integridade BSON; 18 PNGs Console inspecionados. [Guia](20-console.md) e [matriz](15-matriz-de-validacao.md).

Linux nativo, leitor de tela, autenticação/TLS e topologias distribuídas permanecem pendentes. Evidência real desta fixture Console não substitui homologações anteriores do runner mongosh.


## UUID/GUID configurável — revisão de 11/09/2026

Entregue `UuidCodec` como codec único (bytes, subtype, construtores e saída), substituindo `UuidExtendedJson`; a asserção do fixture canônico anterior foi mantida em `UuidCodecTests`. Construtores `UUID`, `CGUUID`, `JUUID` e `GUUID` no Console (inclusive `EJSON.parse`), campos BSON do adaptador, importação e helpers do runner mongosh. Preferência global e sobrescrita por conexão em Preferências e no editor de conexão, com prévia das quatro formas; persistência aditiva e textual em `WorkspacePreferences`. Resultados, árvore, área de transferência, editor de documento, “Abrir no editor” e **Gerar UUID** usam a representação efetiva; `_id`, precondição e exportação continuam canônicos. Métricas identificam a representação e UUIDs legados de origem desconhecida, com texto truncável e dica.

Build Windows `--no-restore -p:UsedAvaloniaProducts=` com **0 avisos e 0 erros**. **369 testes aprovados, 0 falhas, 1 ignorado** (integração MongoDB real sem binário local). TRX: `tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-11_14_50_37_net10.0.trx`. 54 PNGs `uuid-*` gerados; amostras dos dois temas, tamanhos mínimo/máximo e escalas 1, 1,5 e 2 inspecionadas. A inspeção levou a duas correções: métrica cortada em 960 px e literal GUUID quebrado a 460 px.

Pendências: executar `ConsoleMongoIntegrationTests` com `SLOP_CONSOLE_MONGOD` (bytes gravados e consultas por representação já codificados no teste), helpers com mongosh instalado, Linux nativo, leitor de tela, Python legacy, UUID v7 e migração assistida.

## Resultados em JSON e árvore — revisão de 11/09/2026

Entregues `StructuredResultSet` e `StructuredResultDocument` no Core, com origem, posição, `_id`, truncamento e completude, além de `ExtendedJsonFormatter` (altera só espaços), `ExtendedJsonValue` (tipos BSON sem conversão) e `ExtendedJsonComparer`. O painel Resultados ganhou seletor **JSON | Árvore** por aba, **Copiar JSON**, JSON formatado com cursor ligado à seleção, árvore sob demanda com avisos textuais, menu por documento (botão direito, Shift+F10 e tecla Menu), `DocumentJsonWindow` somente leitura e edição a partir do resultado pela modal existente, que relê por `_id` antes de gravar e informa documento alterado ou removido. O runtime do Console passou a conservar a resposta equivalente do driver (ordem de campos) e a informar método e projeção. **Documentos → Editar…** mantém o fluxo anterior.

Build Windows `--no-restore -p:UsedAvaloniaProducts=` com **0 avisos e 0 erros**. **414 testes aprovados, 0 falhas, 1 ignorado** (integração MongoDB real sem binário local); baseline anterior de 369 aprovados. TRX: `tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-11_16_08_39_net10.0.trx`. Foram gerados 72 PNGs `results-*` e `result-document-*`; amostras claro/escuro, 960 e 1366, 100% e 200% foram inspecionadas. Observado e não alterado, por ser comportamento anterior: a barra de rolagem sobreposta do Fluent cruza a última linha em áreas pequenas e o TextBox focado usa o fundo de foco do Fluent no tema escuro.

Pendências: edição e conflito contra MongoDB real, leitor de tela, Linux nativo, área de transferência nativa, virtualização de árvores muito grandes e desempenho do TextBox com páginas próximas do limite de 8 MB.
## Autocomplete local Qwen — validação final em 11/09/2026

Entregues contratos em Core/Application, fallback básico, runtime ONNX GenAI 0.15.2/ONNX 1.28.0 isolado em Infrastructure, tokenizer nativo, FIM limitado, cache, serialização de inferências, cancelamento por editor e descarte por versão. Preferências aditivas no proprietário LiteDB, catálogo externo, teste do modelo, prévia com fonte de código e Tab/Esc; Ctrl+Espaço mantém metadados Console/MQL. README, guia, ADR-027, design system e documentação técnica atualizados. [Instalação reproduzível e limites](21-autocomplete-local.md).

Checkout final validado sobre `8fac5c1` (inclui os incrementos de UUID e resultados). Restore `--locked-mode` e build `--no-restore -p:UsedAvaloniaProducts=` aprovados, **0 avisos/erros**. **415 testes regulares aprovados**, 0 falhas; os **2 testes Explicit de Qwen** foram executados separadamente e também passaram. TRX: `autocomplete-current-final.trx` e `qwen-current-final.trx` em `tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults`.

Pesos reais Qwen2.5-Coder-0.5B Q4, revisão e SHA-256 registrados no guia, instalados somente como fixture temporária externa. Geração FIM produziu `a + b`, avaliada com a função sintética `add(2, 3) = 5`; sessão reutilizada, cancelamento nativo e geração posterior aprovados. Caminho completo de preferências → serviço → modelo → editor → Tab testado. Inspecionados 18 PNGs do editor, 18 de preferências e 2 com geração Qwen real, nos dois temas; a prévia usa o token CodeFont e entrelinha configurável.

Publicações self-contained `win-x64`, `win-arm64`, `linux-x64` e `linux-arm64` geradas. DLLs ONNX/GenAI de Windows conferidas: máquina 8664 para x64 e AA64 para ARM64; sem pesos nos pacotes. GPU/DirectML/CUDA não são distribuídos nesta versão CPU; OpenVINO/QNN/NPU são extensões futuras. Execução nativa Linux/ARM64, desempenho de modelos maiores e leitor de tela continuam homologações externas, sem serem anunciados como comprovados pelos builds cruzados.

## Autocomplete preditivo — entrega de 11/09/2026

Implementado o objetivo do modelo já preparado: ghost text no cursor, Tab incremental, dicionário prioritário, Context Builder limitado com Input/campos de resultados/histórico/nomes conhecidos, preferências independentes e isolamento por aba. Preservados serviço/runtime/DI/persistência existentes. Catálogo sugere a pasta preparada se não houver caminho salvo; nenhum peso foi copiado para o repositório ou pacote.

Restore travado e build aprovados, 0 avisos/erros. **431 testes regulares e 2 Explicit reais aprovados**; `predictive-final.trx` e `predictive-real-final.trx`. Modelo CPU de `F:\models\Qwen2.5-Coder-0.5B-onnx-int4-cpu` gerou `a + b` no editor, aceito em duas etapas; cancelamento e reutilização nativos aprovados. 38 PNGs de editor/preferências/Qwen inspecionados nos dois temas. [Auditoria requisito por requisito e limites](15-matriz-de-validacao.md#autocomplete-preditivo--auditoria-de-aceite-11092026). [Arquitetura e configuração](21-autocomplete-local.md).

## Falha intermitente de CI no Linux — 11/09/2026

Os runs `fix` (34625741076) e `v0.1.0.alpha` (34638550609) falharam só em `ubuntu-latest`, e `uuid` (34632561387) falhou também em `windows-latest`, sempre no mesmo teste: `ContextIsBoundedBeforeItReachesRuntime` (413/415 aprovados no alpha). O stack aponta `RegexMatchTimeoutException` em `CompletionPrivacy.ContainsSensitiveText`, com `MatchTimeout` de 50 ms sobre os 65 536 caracteres do contexto limitado. Não é diferença de sistema operacional: o limite era de tempo de relógio, e pausas de GC e preempção de um runner ocupado contam nele (suíte de 58 s no CI contra 19 s local). A reprodução local com 48 threads concorrentes e alocações grandes causou 21 timeouts em 200 verificações (pior caso 147 ms) com a configuração anterior e nenhum com a nova (pior caso 2,8 ms).

Correção: padrão de privacidade no motor `NonBacktracking` (tempo linear), limite de 1 s como proteção e falha fechada — contexto não verificado a tempo é tratado como sensível e não segue para a IA. A extração de palavras do fallback volta às palavras-chave se atingir o limite, sem propagar exceção ao editor. Testes novos: pior caso sob pressão de CPU/GC, incluindo `-----BEGIN ` repetido (quadrático no motor anterior), e fallback básico sobre o contexto inteiro; o de pior caso falha na implementação anterior. Windows: build com 0 avisos/erros, **416 testes aprovados, 0 falhas, 1 ignorado**, e a classe de autocomplete repetida 5 vezes sem falhas. TRX: `tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/chuke_ESILVA-PC_2026-09-11_16_35_02_net10.0.trx`. Pendente: nova execução do CI em `ubuntu-latest`; o WSL local não tem SDK .NET instalado.


## 12/09/2026 — Syntax highlighting central

Implementado lexer semântico com vocabulário MongoDB único, snapshots de contexto por presenter, cache de linhas e worker cancelável. Template opt-in mantém TextBox e integra editores, Resultados, Input, modais, árvores e ferramentas BSON/índices. Recursos Syntax.* nos temas existentes, suporte visual a Extended JSON/UUID, Query API, pipelines, Atlas Search, DSL e namespaces carregados. Ghost text e pares de delimitadores usam o mesmo esquema. Sem alteração de execução/persistência, lockfiles ou dependências.

A asserção antiga do autocomplete que exigia um único Run no sufixo passou a conferir a concatenação integral dos Runs após a sugestão: o sufixo agora é segmentado por tokens. Não foram alterados golden files. Resultados finais e pendências na [matriz](15-matriz-de-validacao.md); [arquitetura e limites](22-syntax-highlighting.md).

## 12/09/2026 — Modos de identificador (ObjectId, UUID v4, Standard)

Entregue `IdentifierRepresentationMode` (`Standard`, `ObjectId`, `UuidV4`) acima da `UuidRepresentation`, que segue separada e por conexão. `IdentifierRepresentationService` (Core) centraliza detecção por tipo BSON, parsing de `ObjectId("…")`, 24 hexadecimais, construtores UUID, UUID com 32 dígitos e wrappers canônicos, formatação, geração (ObjectId e UUID v4), placeholders de script e a conversão ObjectId → UUID alternativa (12 bytes + 4 zeros, reversível). Não havia conversão ObjectId → UUID anterior no código; a regra foi definida e registrada na ADR-032. `UuidCodec` delega a varredura compartilhada e continua restrito a UUID.

Integração: Preferências com **Representação padrão de identificadores**, explicação dinâmica e prévia por modo, acima de **Representação UUID · Binary BSON** (comparação das quatro formas oculta em ObjectId); saída humana com `ObjectId("…")` em Resultados, visualização, Documentos, editor e cópias, revertida com bytes idênticos em campos BSON, importação e `EJSON.parse`; árvore e identidade com “· UUID …” somente em UUID v4; menu **Copiar _id**, **Copiar consulta por _id** e **Copiar UUID equivalente do _id**; **Gerar identificador** e **Interpretar** nas Ferramentas; `_id` de exemplo dos scripts CRUD por modo e representação da conexão; métrica “IDs <modo> · UUID <representação>”. Persistência aditiva `WorkspacePreferences.IdentifierMode`: sessões anteriores recebem Standard e mantêm a `UuidRepresentation`; valor desconhecido fica visível e não é sobrescrito. As expectativas de menu de `ResultPanelUiTests` e o teste do snippet de ferramentas foram atualizados porque a interface mudou intencionalmente (novos itens de cópia e comando renomeado).

Build Windows `--no-restore -p:UsedAvaloniaProducts=` com **0 avisos e 0 erros**. Suíte completa: **504 aprovados, 1 falha, 1 ignorado** (integração MongoDB real sem binário). TRX: `tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/identifier-mode-final.trx`. A falha é `HugeSingleLineKeepsFullTextAndMakesEveryRangeReachableWithoutShapingItAll` (syntax highlighting, `VisualLength` 2000017 para limite 4106); ela falha igualmente em um worktree limpo do commit `f8e7a16`, sem as alterações deste incremento, e não foi alterada. Gerados 72 PNGs (`identifier-results-*` e `identifier-preferences-<modo>-*`); amostras dos dois temas, tamanhos mínimo/máximo e escalas inspecionadas. A inspeção levou a encurtar a anotação da árvore (“· UUID equivalente …” → “· UUID …”), que era cortada pela coluna de valor de 720 px mesmo em 1920 px.

Pendências: corrigir a regressão do teste de linha longa do syntax highlighting (fora deste escopo), homologação com MongoDB/mongosh reais, Linux nativo, leitor de tela, área de transferência nativa e UUID v7.

## ONNX SlopCoder e chat — 13/09/2026

Revisão implementada: [contrato, uso, distribuições CPU/WinML/CUDA e limites](23-onnx-slopcoder.md). DeepSeek-Coder FIM com tokenizer .NET e manifesto validado; chat e autocomplete compartilham sessão, preservando cancelamento e revisão das propostas. A exportação CPU fornecida foi executada em CPU; a tentativa DirectML falhou na geração e o fallback CPU foi validado. O modelo FIM pode acrescentar alterações não solicitadas no chat; fidelidade conversacional não está homologada.


## Datas BSON — 13/09/2026

Restore locked aprovado; build sem restore com -p:UsedAvaloniaProducts= aprovado, 0 avisos/erros. Suíte final: **530 aprovados, 1 falha preexistente** em HugeSingleLineKeepsFullTextAndMakesEveryRangeReachableWithoutShapingItAll (já registrada na entrega ONNX). TRX: tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/date-format-final.trx.

Novos testes verificam bytes BSON com fixture do driver, UTC e offset -03:00, milissegundos negativos/zero, datas inválidas, limite Int64, strings preservadas, argumentos BSON enviados pelo Console e helper mongosh executado em Jint. Resultado visual confirmado na modal de documento: Date legível nos PNGs reais result-document-json-Light-760-1.png e result-document-json-Dark-760-1.png em ui-evidence do diretório de testes. Os testes existentes de resultados/cópia e representações também passaram.

Não homologado nesta alteração: MongoDB/mongosh real, Linux, leitor de tela e área de transferência nativa. Exportação permanece canônica.

## Polimento MVP — 13/09/2026

Restore locked e build sem restore com `-p:UsedAvaloniaProducts=` aprovados, zero avisos/erros. Suíte `mvp-polish.trx`: **549 testes executados/aprovados, zero falhas**; 555 descobertos, seis casos explícitos de IA fora desta execução/MVP. Arquivo local: `tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/mvp-polish.trx`.

Inclui dois testes de integração com MongoDB portátil 8.0.30 em Windows, paginação/CRUD protegido/conflito/exportação, 18 PNGs da barra concorrente nos temas/tamanhos/escalas declarados, undo real de formatação e correção do teste de linha com dois milhões de caracteres antes falho. Nenhuma asserção de limite visual foi enfraquecida. Startup Headless quente: 9 ms na última execução, 464 ms em execução anterior isolada; cenário de duas páginas de 100 em 5.000 documentos com edição/conflito/exportação: 55 ms (anterior 84 ms). Esses números são observações locais, não benchmark de produção.

Não encerrados: Linux gráfico, diálogos nativos/clipboard, leitor de tela, cold start/CPU/RAM nativos e matriz ampliada de autenticação/topologia. WSL sem distribuição disponível; acesso Linux solicitado. Veja a [auditoria completa e o inventário anterior às alterações](done/release_v0.5.0/25-auditoria-mvp-performance.md). Não declarar v0.5.0 pronta por esses testes.

## SlopCoder-Mongo-1.5B-full INT8 como modelo sugerido — 13/09/2026

`F:\models\SlopCoder-Mongo-1.5B-full-ONNX-INT8` passou a ser a primeira entrada de `LocalModelCatalog.KnownWindowsModels`: é sugerido quando a
preferência de caminho está vazia e aparece na descoberta; um caminho já salvo continua prevalecendo. Pesos fora do Git e da distribuição.
Motivo, métricas do pipeline externo e limites em [23](23-onnx-slopcoder.md#slopcoder-mongo-15b-full-qwen2--13092026); guia e autocomplete local
atualizados.

O contrato usado no treino foi conferido contra este checkout: `LocalModelAiChatService`, `AutocompleteContextBuilder`, `QwenFimPromptBuilder`,
`OnnxLocalModelRuntime`, `LocalModelCatalog`, `AutocompleteService`, `Core/Autocomplete.cs` e `Core/AiAssistant.cs` diferem da versão validada
apenas em fim de linha.

Restore locked aprovado; build sem restore com -p:UsedAvaloniaProducts= aprovado, 0 avisos/erros. Suíte regular: **546 aprovados, 0 falhas,
2 ignorados**. Teste Explicit `RealQwenGeneratesWithCpuAndReusesNativeSession` aprovado com `SLOP_QWEN_MODEL` apontando para o pacote novo
(validação pelo catálogo, geração CPU repetida na mesma sessão nativa e cancelamento). TRX: `slopcoder-1.5b-full-regular.trx` e
`slopcoder-1.5b-full-real.trx` em TestResults do projeto de testes.

Não executado: `RealQwenCompletionTravelsThroughEditorAndAcceptsTab`, que exige a sugestão exata `a + b` num prompt JavaScript genérico —
critério do Qwen genérico, não de um modelo especializado em MongoDB. Não homologado nesta alteração: uso interativo na IDE com MongoDB real,
GPU/DirectML, Linux e latência no editor desta máquina (a do pipeline externo está em 23). Sem alteração visual. Pendências registradas em 23:
`Decode` com saída vazia e contexto excedente no chat descarregam o modelo com cooldown de 30 s.

## IA local multimodelo — 13/09/2026

Supersede a entrada anterior quanto à descoberta: `LocalModelCatalog.KnownWindowsModels` e `PreferredModelPath` foram removidos. Modelos passam a
ser subpastas de um diretório configurável, com `kapibarastudio-model.json` opcional, validade `Valid`/`Invalid`/`Unsupported`/`MissingFiles` e
adapters por arquitetura. `ILocalAiModelService` centraliza descoberta, carga sob demanda desacoplada do editor, troca com descarga prévia, fila com
prioridade, capacidades, barra de atividades e teste completo; autocomplete e chat o reutilizam. `OnnxHardwareProbe` usa `GetEpDevices`; Automático
faz NPU → GPU → CPU com fallback, escolha explícita não faz fallback. Preferências aditivas (`ModelDirectory`, `SelectedModel`, `ChatModel`,
`ChatEnabled`) na sessão versão 1. As duas pendências acima (`Decode` vazio e contexto excedente) foram corrigidas. [Especificação](26-ia-local-multimodelo.md),
[ADR-037](10-decisoes-arquiteturais.md).

Build 0 avisos/erros; suíte regular **564 aprovados, 0 falhas, 2 ignorados**. Reais nesta máquina com o SlopCoder-Mongo-1.5B: sonda, teste de modelo em
CPU e Automático e `RealQwenGeneratesWithCpuAndReusesNativeSession` aprovados; teste em GPU explícita falhou na geração DirectML e reportou o motivo
sem fallback — incompatibilidade do pacote, registrada como não homologada. Evidência visual: PNGs Headless da modal nos dois temas e três tamanhos.
Não homologado: NPU, CUDA, Linux, diálogo nativo de pasta e uso interativo com MongoDB real.

## Homologação do MVP no Windows — 14/09/2026

Após a correção da seleção de formato por extensão, o build passou com 0 avisos/erros e a suíte completa passou com **594 aprovados, 0 falhas** usando o fixture MongoDB portátil 8.0.30. Os dois testes `MongoReal` confirmaram paginação, CRUD, edição protegida com conflito, tipos BSON, UUIDs, índices, somente leitura, exportação e cancelamento.

Na aplicação nativa, o perfil `MvpNative` conectou em `127.0.0.1:55440`, a árvore carregou `admin/config/local`, a consulta de `system.version` retornou 1 documento e a barra inferior exibiu execução, cancelamento disponível e conclusão. O cancelamento foi acionado durante outra consulta e a IDE voltou ao estado pronto com a mensagem de efeitos não revertidos automaticamente. **Copiar JSON** foi validado no clipboard do Windows e retornou o documento completo sem credenciais. O seletor do Windows produziu JSON válido e CSV real (`"_id","version"` / `"featureCompatibilityVersion","8.0"`). Os testes de streaming confirmaram cancelamento de exportação após escrita parcial e limpeza do arquivo incompleto. Uma medição sem inspeção visual abriu a janela em 1.396 ms; após 2 s o processo usava 220,3 MiB e acumulava 3.734,4 ms de CPU. A validação visual Linux está dispensada; leitor de tela permanece fora da homologação disponível.

## Atualização automática — 14/09/2026

Implementada a [ADR-038](10-decisoes-arquiteturais.md): `AppVersion`/`AppUpdateSelector` (Core), `IAppUpdateService` (Application), `GitHubAppUpdateService` (Infrastructure), `AppUpdateViewModel`, botão **Atualizar** na barra superior e troca de arquivos em `Program.Main` após o fechamento, com opção Reiniciar agora. Canal estável; pré-release só para instalação em pré-release. SHA-256 obrigatório, rollback por renomeação e erro persistido no pendente.

Build 0 avisos/erros; suíte regular **577 aprovados, 0 falhas, 2 ignorados**. Novos: `AppUpdateSelectorTests` (SemVer, canal, drafts, RID, checksums), `AppUpdateServiceTests` (zip e tar.gz sintéticos via `HttpMessageHandler`, hash divergente, cancelamento, rate limit, troca com executável aberto com compartilhamento de exclusão, rollback com `lastError` e nova tentativa, pendente obsoleto, detecção de build local) e `AppUpdateUiTests` (estados, barra de operações, cancelamento, 36 PNGs). A inspeção dos PNGs mostrou sobreposição Atualizar/Ambientes/Ferramentas em 960, corrigida com rótulo compacto abaixo de 1100.

Não homologado: download real do GitHub seguido de troca e reinício em Windows e Linux (roteiro no plano: pacote local 0.4.9 → release v0.5.0), pasta sem escrita em instalação real e comportamento com antivírus bloqueando a renomeação.

## Download de modelos SlopCoder — 14/09/2026

Implementada a [ADR-039](10-decisoes-arquiteturais.md): `RemoteModelVariant` (Core), `IRemoteModelSource` (Application), `HuggingFaceModelSource` (Infrastructure) e seção **Baixar modelo** em Preferências → Autocomplete, com seletor de variantes, Baixar/Cancelar, Atualizar lista, progresso na modal e na barra inferior, verificação por hash, retomada e seleção do modelo instalado.

Build 0 avisos/erros; suíte regular **582 aprovados, 0 falhas, 2 ignorados**. Novos: `HuggingFaceModelSourceTests` (agrupamento por `genai_config.json`, revisão fixada, caminho inseguro ignorado, vetores independentes SHA-1 de blob git e SHA-256, pasta instalada não sobrescrita, arquivo corrompido sem instalação e retomada sem novo download do verificado, cancelamento oculto do catálogo) e `ModelDownloadUiTests` (lista, pré-seleção, progresso, operação na barra, seleção após instalar, cancelamento pela barra, 18 PNGs). Verificação real contra o Hugging Face nesta máquina: listagem `int4` 415 MB e `int8` 1124 MB (10 arquivos, 3 LFS, revisão `9e2775a`) e download parcial de `genai_config.json`, `tokenizer_config.json` e `tokenizer.json` (LFS via CDN), com hashes conferidos e sem pasta temporária restante.

Não homologado: download completo de 415 MB/1,1 GB seguido de Testar modelo, retomada após queda real de rede, Linux e espaço em disco insuficiente em volume real.

## Modelos SlopCoder 0.5B e 1.5B-full, nomes e pasta — 14/09/2026

Revisão da ADR-039: download de `esilva/SlopCoder-Mongo-0.5B-ONNX` e `esilva/SlopCoder-Mongo-1.5B-full-ONNX` (CPU INT4/INT8 e GPU DirectML INT4/FP16). Os repositórios safetensors `SlopCoder-Mongo-0.5B` e `-1.5B-full` viram família e link do card, sem download, porque o runtime não os carrega. `ModelDisplayNames` aplica "família — hardware precisão" ao download e à seleção; itens em duas linhas, dica com nome completo, aviso de GPU não detectada, link para o card e botão **Abrir pasta**.

Build 0 avisos/erros; suíte regular **592 aprovados, 0 falhas, 2 ignorados**. Novos ou revistos: `ModelDisplayNamesTests` (convenção de pasta, metadata, hardware declarado, nomes fora da convenção preservados), `HuggingFaceModelSourceTests` (família do repositório base, ordem e dica do publicador, repositório inacessível sem esconder os demais, repositório não configurado recusado) e `ModelDownloadUiTests` (quatro títulos, dica, link do card, pasta criada pelo Abrir pasta, mesmo nome na seleção após instalar, 18 PNGs). Os `Display` de `LocalAiModelServiceTests` continuam iguais para pastas fora da convenção. Listagem real nesta máquina: 8 variantes com nomes, dicas, tamanhos e cards corretos (0.5B `f706662`, 1.5B-full `e683025`).

Não homologado: abertura real da pasta pelo gerenciador de arquivos (Windows/Linux), download completo das variantes 1.5B e DirectML, e aviso de GPU com detecção real sem GPU.

## Pacotes SlopCoder-Mongo DirectML homologados em GPU — 14/09/2026

Sem alteração de código. O pipeline externo produziu pacotes `-ONNX-DML-FP16` e `-ONNX-DML-INT4` (1.5B-full e 0.5B) com `kapibarastudio-model.json`
(`hardware` `["gpu"]`); os pacotes CPU passaram a declarar `["cpu"]`. Com o build Debug WinML existente (sem rebuild) e `SLOP_QWEN_MODEL`
apontando para cada pacote: `RealModelTestRunsOnTheRequestedHardware(Gpu)` **aprovado** em DirectML para 1.5B-full DML-FP16, 1.5B-full DML-INT4
e 0.5B DML-FP16; `(Auto)` aprovado em DirectML com o 1.5B-full DML-FP16 e direto em CPU com o 1.5B-full INT8. Isso resolve, para esses pacotes,
a falha de geração em GPU explícita registrada na IA local multimodelo (a exportação anterior era para CPU). Métricas, TRX e limites em
[23](23-onnx-slopcoder.md#pacotes-slopcoder-mongo-directml-gpu--14092026). Não homologados: uso interativo da janela, troca entre dois pacotes
DirectML no mesmo processo, download pelo **Baixar modelo**, NPU e CUDA.


## Revisão do plano de autocomplete — 15/09/2026

**Registro histórico de 15/09/2026:** concluída revisão documental do autocomplete contra b082d4a, quando Schema Discovery/Learning ainda era descrito como pendente. A implementação subsequente está registrada no bloco corrente desta página e em [schema learning](auto-complite/schema-learning.md); nenhuma homologação real nova é declarada neste registro histórico. [Plano revisado](auto-complite/README.md), [tarefas por agente](auto-complite/execution-plan.md).

## Núcleos isolados de autocomplete e IA local — 17/09/2026

Reestruturação puramente física, sem mudança de regra de negócio: extração de `EsilvaSoft.KapibaraStudio.Autocomplete.Core`, `EsilvaSoft.KapibaraStudio.LocalAi.Core` e `EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi` a partir de `Core`/`Application`/`Infrastructure`, isolando fisicamente o núcleo de autocomplete determinístico e o de IA/ML local do domínio MongoDB/LiteDB/UI. `Infrastructure` mantém MongoDB, LiteDB, console Jint e atualização de aplicativo; `Infrastructure.LocalAi` leva os adaptadores ONNX e os pacotes `Microsoft.ML.OnnxRuntime*`. O composition root do Desktop passa a chamar `AddKapibaraStudioInfrastructure` e `AddKapibaraStudioLocalAiInfrastructure`. Namespaces dos tipos movidos acompanham o novo assembly. Detalhes, grafo de dependências final (nove projetos sem ciclo) e os seis desvios aceitos em relação ao desenho original: [ADR-040](10-decisoes-arquiteturais.md).

Evidência: build e suíte completa verdes, **1127/1127 testes aprovados**, sem regressão de performance observada. Nenhum requisito muda de status por esta entrada: é reorganização estrutural de projetos existentes, não nova funcionalidade — os status ✅/🚧/📋/🧪 do catálogo funcional permanecem os mesmos de antes da extração.

## Internacionalização da interface — 21/09/2026

Meta iniciada para pt-BR, inglês, espanhol e chinês simplificado. Os arquivos `docs/**/*.md` continuam em português por decisão de escopo; a tradução será aplicada à interface desktop, mensagens e recursos do produto.

Primeiro incremento implementado: `ApplicationLanguages` (Core) define os quatro códigos, mantém `pt-BR` como idioma inicial e usa `en` como fallback para código inválido/ausente; `WorkspacePreferences.Language` é aditivo e a fronteira LiteDB normaliza o valor sem rejeitar sessões legadas. `LocalizationViewModel` introduz resolução por chave com fallback inglês e marcador observável `[[chave]]` quando não há tradução nem em inglês. A extensão global `Loc` permite troca em execução em janelas com DataContexts distintos. A barra principal e as janelas de conexões, documentos, ambientes e histórico usam o catálogo em rótulos, dicas e nomes de acessibilidade. `ILocalAiModelService` e `IMetadataCache` agora aceitam um localizador opcional: estados/operações dinâmicos da IA local e do carregamento de metadados também acompanham o idioma da interface, sem acoplar o Application à camada Desktop.

Evidência final do recorte: testes direcionados do catálogo, normalização, fallback, JSON legado, persistência, resolução nos quatro idiomas e `LocalizationUiTests` aprovados; build da solução sem avisos/erros. Foram migrados os textos estáticos da barra principal, Explorer, editor/resultados, ferramentas do banco e preferências de autocomplete/IA, além de mensagens dinâmicas de operações, administração, documentos, ambientes, atualizações, seletores nativos, diagnósticos de orçamento/operação, contexto enviado à IA e opções de modelos. `ILocalAiModelService`, `ILocalModelCatalog`, `IMetadataCache`, `IAiChatService` e o serviço de topologia aceitam o localizador opcional para estados, falhas, validações, operações e mensagens de segurança sem acoplar a camada Application/Infrastructure à UI. `LocalizationUiTests` gera e verifica 64 PNGs reais de oito janelas (conexões, ambientes, histórico, JSON de documento, mutação, autocomplete, ferramentas e janela principal; pt-BR, en, es e zh-CN em Light/Dark); a inspeção visual corrigiu um conflito de chave que expunha placeholders `{0}`/`{1}` no botão de contagem, corrigiu textos iniciais criados antes da restauração do idioma e não encontrou clipping evidente nem perda de legibilidade CJK nas amostras revisadas. O runtime do Console, as fontes remotas e o runtime ONNX recebem o localizador opcional; a troca de idioma invalida estados derivados das abas e preferências de IA; histórico e seletores de resultado do Console usam uma camada de apresentação localizada sem alterar os registros persistidos. O fallback de idioma é inglês e `docs/**/*.md` permanece em português. A meta de implementação/tradução está concluída com build sem avisos/erros e **2.639 testes unitários aprovados, 0 falhas e 20 ignorados**. Benchmarks não fazem parte do critério desta meta. Revisão linguística de domínio, MongoDB/mongosh real, leitor de tela, diálogos nativos e Linux gráfico permanecem como homologação externa das fases.


## Reorganização de fases e documentação — 18/09/2026

Meta documental e de interface. O roadmap passou de seis para **oito fases** e a documentação ganhou separação explícita entre planejado, em execução, concluído e adiado. **Nenhum documento, requisito, ADR ou código funcional foi apagado.**

### Movimentações de arquivo

| Origem | Destino | Motivo |
| --- | --- | --- |
| `docs/25-auditoria-mvp-performance.md` | `docs/done/release_v0.5.0/25-auditoria-mvp-performance.md` | Evidência da v0.5.0, arquivada junto da release. Links internos reescritos para `../../`. |
| `docs/27-consultas-avancadas.md` | `docs/backlog/27-consultas-avancadas.md` | A v0.6.0 deixou de ser "consultas avançadas". O documento recebeu cabeçalho de reclassificação e é preservado como evidência histórica. |

Os demais documentos numerados permanecem na raiz de `docs/` por serem transversais a várias fases (governança, arquitetura, design system, segurança, qualidade e validação), conforme a regra da meta. `docs/auto-complite/phases/phase-1..5-*.md` **não** foram movidos: são sub-fases do subsistema de autocomplete, com numeração própria, referenciadas pelas fases 2 e 3 do produto.

### Documentos criados

`docs/phases/README.md` e os oito `docs/phases/phase-NN-vX.Y.Z/README.md`; `docs/backlog/README.md` e `bkl-01` a `bkl-05`; `docs/done/README.md`, `docs/done/release_v0.5.0/README.md` e `docs/done/release_v0.5.0/pendencias-de-homologacao.md`.

### Reclassificações de requisito

| Requisito | Antes | Depois | Decisão |
| --- | --- | --- | --- |
| Administração, índices, coleções, views, validação, transferência lógica | v0.7.0 | v0.10.0 (Fase 6) | Reordenação do roadmap oficial. Código preservado e integrado; pontos de entrada removidos por não ser a fase atual. |
| AGG-01/02/03/04 (modo Agregação) | v0.6.0 | Backlog (`bkl-04`) | A v0.6.0 passou a ser organização dos projetos e autocomplete básico. Implementação parcial documentada como **antecipação técnica**, não como escopo concluído. |
| EDT-06, TRF-05 (Script Engine entre conexões) | Sem versão comprometida | Backlog (`bkl-03`) | A v0.8.0 passou a ser abertura e salvamento de arquivos de texto. Console Jint continua ativo; modo Script/mongosh desativado no seletor. |
| EDT-07 (exportar query para C#) | v0.6.0 | Backlog (`bkl-05`) | Sem caminho integrado e sem fase atribuída. |
| Cofre criptográfico (recorte de CON-07) | v0.5.0 | Backlog (`bkl-01`) | O cofre **não existe**: o LiteDB é aberto sem senha e os valores de ambiente são gravados em JSON puro. O armazenamento local permanece, por ser necessário à resolução de `${ENV.get(...)}`. |

### Alterações de interface

- Removidos da barra superior: botão **Ferramentas** e botão **Ambientes** (rotulado "Ambientes / Key Vault"). O grid passou de dez para oito colunas.
- Removidas do menu de contexto do Explorer as quatro entradas que abriam a janela de Ferramentas. Preservadas as demais, inclusive geradores de script, "Ver índices" e "Detalhes e estatísticas".
- Removido o `ComboBox` "Modo do editor". **Console** é o único modo oferecido.
- `WorkspaceTabViewModel` passou a distinguir `Modes` (modos que o editor entende, incluindo Script e Agregação) de `SelectableModes` (o que a interface oferece). Essa separação foi necessária para que rascunhos e histórico salvos em Script ou Agregação reabram como foram gravados — reduzir `Modes` diretamente causava conversão silenciosa para Console em `Restore`.
- `WorkspaceToolsWindow`, `EnvironmentsWindow`, `EnvironmentsViewModel`, `ConsoleRuntime`, `MongoshScriptExecutionService`, o parser e o validador de agregação permanecem íntegros nos seus projetos, sem mudança de camada.

### Testes

`AppUpdateUiTests.RenderTopBar` e `DatabaseExplorerUiTests` foram ajustados aos pontos de entrada removidos e agora **asseram a ausência** dos botões e das entradas de menu; o segundo passou a exercitar `WorkspaceToolsWindow` diretamente, provando que a janela continua funcional sem entrada visual. Novo arquivo `DisabledFeatureEntryPointsTests` cobre: `SelectableModes` com apenas Console, restauração fiel de rascunhos em modos desativados, ausência do seletor de modos e ausência dos botões na barra.

Os testes que atribuem `Mode = "Script"` ou `"Agregação"` diretamente **não** foram alterados: são a evidência de que o código preservado continua íntegro.

### Validação executada nesta meta

`dotnet build` sem avisos e sem erros; `dotnet test` com **1156 aprovados, 0 falhas, 4 ignorados** (Explicit). `node scripts/build-docs-index.cjs` regenerou o índice com 84 documentos.

**O que esta meta não fez:** nenhum gate de homologação real foi fechado. A v0.5.0 foi arquivada por escopo funcional, com as pendências de Windows/Linux, leitor de tela, diálogos nativos e matriz de autenticação registradas em [`done/release_v0.5.0/pendencias-de-homologacao.md`](done/release_v0.5.0/pendencias-de-homologacao.md). A reorganização não foi usada para ocultar teste falho ou pendência.

## Planejamento da v0.11.0 — MCP e agentes externos — 22/09/2026

Meta em desenvolvimento inicial: [plano técnico](phases/phase-07-v0.11.0/README.md), análise da solução e contratos para Agent Runtime, providers, ferramentas compartilhadas, MCP e chat Avalonia. Há spikes do lote 0, adapters isolados de cofre no lote 1 e política/registry interno parcial no lote 2; **não há integração MCP, OpenAI ou Claude, runtime, chat, DI ou Tool Registry exposto entregue.** O roadmap passa a dez fases: novo MCP/agentes em 7/v0.11.0; workflow anterior preservado em 8/v0.12.0; homologação anterior preservada em 9/v0.13.0; estabilidade em 10/v1.0.0. Origem/destino e inventário no [registro de migração](phases/phase-07-v0.11.0/README.md).

Escopos arquivados, requisitos amplos, backlog, evidências e pendências existentes foram conservados. Nenhuma release, tag, pacote, projeto de produção ou dependência foi criado pela migração documental. A proteção nativa de segredos de agentes é planejada para v0.11.0; não corrige retroativamente os ambientes locais em texto puro. Build/testes do checkout e verificação do leitor devem ser registrados como regressão/documentação, nunca como homologação dos providers futuros.

Validação da meta documental em 22/09/2026: restore locked e build (0 avisos/0 erros, telemetria Avalonia desativada conforme instrução de ambiente); UnitTests 2.674 aprovados/20 ignorados/0 falhas e Benchmarks 43 aprovados/0 falhas. Revisão independente de contratos e preservação dos escopos concluídas. [Auditoria dos 34 itens e limites](phases/phase-07-v0.11.0/README.md). Nenhum aceite funcional de MCP/providers é encerrado por esses testes da base existente.

## Lote 0 da v0.11.0 — spike Codex confirmado parcialmente — 22/09/2026

**Estado: em andamento; disponibilidade, versão e geração de schema aprovadas somente para avançar dentro do lote 0. Nenhuma feature ou critério AC aprovado.** Em execução revisada no Windows, `codex.exe` reportou `codex-cli 0.155.0-alpha.16`, SHA-256 `97d4d67419d0ac2f71342f9a5e850f9468aa622618de8ea823223edb9a91926a`. O spike gerou 310 schemas JSON (3.512.403 bytes); o diretório de saída e o manifesto somaram 3.559.959 bytes. `credentialsCaptured=false` e não houve divergência entre os hashes manifestados e os 310 arquivos. A evidência está em `%TEMP%\slop-phase07-codex-review-edc3773c90604717a023886faca3cc52`, fora do repositório e local a este host Windows.

| Gate do lote 0 | Evidência atual | Pendente para fechar somente o gate do spike |
| --- | --- | --- |
| Codex App Server — disponibilidade/versão/schema | Harness no Windows: 310 arquivos JSON, 3.512.403 bytes; zero divergências; probe recebeu `initialize` válido (332 bytes), enviou `initialized` e encerrou após EOF com exit 0. Testes sintéticos: 5 transporte + 6 manifesto aprovados, 1 skip (symlink no arquivo sem privilégio). Registro em [validação da meta](phases/phase-07-v0.11.0/README.md). | Disponibilidade/schema e handshake STDIO Windows aprovados **somente para avançar dentro do lote 0**. A fonte oficial classifica App Server como experimental e sem suporte para produção, então adoção fica bloqueada. Saídas locais, fora do repositório. Baseline API direta definida; capacidades e provider real ainda pendentes, assim como confinamento, autenticação/keyring, Linux e sessões/tools. |
| OpenAI API direta — contrato SDK | Spike isolado `OpenAI` 2.14.0, restore locked local, build e 5/5 testes offline; lock, inventário MIT/sha256 em [`eng/spikes/phase-07/openai-api`](../eng/spikes/phase-07/openai-api/README.md). | Contrato SSE/tool-call/cancelamento candidato validado. Sem rede, credencial real ou prova de suporte por modelo. A tentativa de auditoria de vulnerabilidade não concluiu por falha TLS/credenciais do feed; pacote não foi promovido ao produto. |
| MCP dual-era | Spike isolado `ModelContextProtocol` 2.2.0, restore locked/build e **10/10 testes Windows**: quatro SDK↔SDK e seis com cliente independente BCL, sem pacote/referência/serializer MCP compartilhado. Pipes STDIO reais cobrem `2025-11-25` e `2026-07-28`, descoberta/listagem sintética vazia e rejeição de incompatibilidade. Inventário e locks em [`eng/spikes/phase-07/mcp-sdk`](../eng/spikes/phase-07/mcp-sdk/README.md). | Aprova somente interoperabilidade mínima de listagem sintética com servidor C# no Windows. Linux, clientes comerciais (Claude/Codex), suíte oficial de conformidade, concorrência, broker e tools reais seguem pendentes; inventário ainda não é SBOM de distribuição. |
| Anthropic | Spike isolado `Anthropic` 12.50.0: restore locked, build (0 avisos/erros), inventário reproduzível, assinatura NuGet verificada, auditoria sem vulnerabilidade reportada e 2 pacotes/licenças/hashes registrados. | Aprova somente versão, dependência e contrato compilado. Sem chamada API, credencial, streaming observado, tool calling ou teste Linux; inventário `net10.0` sem RID não é SBOM final. Nenhum pacote promovido ao produto. |
| Cofre/keyring Windows/Linux | O spike mantém três falhas `CredWriteW` 1312 (`NoLogonSession`) sem gravação reproduzível. `WindowsCredentialSecretStore` isolado passou 30 testes determinísticos e o round-trip nativo foi ignorado; `LinuxSecretServiceSecretStore` isolado passou 84 testes focados. | Revisão estática independente permite avançar, sem comprovar APIs nativas. DI, provider, migração, bloqueio, reinício, outro usuário e recuperação pendentes; Linux/Secret Service não foi exercitado em runtime D-Bus. |
| Secret Service Linux — binding | Candidato MIT `Tmds.DBus.Protocol` 0.94.1 fundamenta `LinuxSecretServiceSecretStore` isolado, com 84 testes focados aprovados em execução anterior; prompts/cancelamento são representados pelo contrato tipado. O adapter está selecionado/componível no DI por SO. | Runtime indisponível no host (WSL negado); D-Bus/Secret Service real, bloqueio/ausência/headless e ciclo de sessão permanecem pendentes. Nenhum teste Linux foi executado nesta continuação. |
| Dependências e licenças | Pesquisa documental de candidatos em [fontes e licenças](phases/phase-07-v0.11.0/README.md). | Selecionar versões publicadas, verificar lockfiles, transitivas e componentes nativos por RID, vulnerabilidades, SBOM, termos e NOTICE. A pesquisa atual não certifica a árvore final nem autoriza distribuição. |

Os spikes validam candidatos isolados nesta máquina Windows; `CODEX_HOME` isolado não prova uso de keyring ou confinamento/sandbox. A revisão dos caminhos não dispõe de proteção atômica contra TOCTOU. Nesta continuação, restore locked local e build passaram; após limpeza dos fixtures Mongo acumulados, a suíte repetida passou com 2.699 testes unitários e 43 benchmarks, 0 falhas e 20 testes ignorados (ver [revalidação](phases/phase-07-v0.11.0/README.md)). Essa suíte é anterior aos contratos e ao adapter atuais; ainda não houve suíte integral posterior. Permanecem pendentes confinamento, autenticação/provider real, cofre integrado por SO, Linux, clientes externos, tools e análise final de dependências/licenças. **Nenhum código funcional de produto da Fase 7 foi homologado; AC-01..AC-20 continuam pendentes.** Ver também a [matriz de validação](15-matriz-de-validacao.md#lote-0-da-v0110-spike-codex-confirmado-parcialmente--22092026).

Após a revisão adicional do cliente BCL, os testes `MongoReal` foram executados novamente: 4/4 passaram com MongoDB 8.0.30 e os diretórios dbpath temporários foram limpos pelo fixture. Isso valida a limpeza nas quatro execuções observadas; não altera os limites de homologação da Fase 7.

## Lotes 1–2 da v0.11.0 — cofres, política e registry internos

Criados em `Application` os contratos `ISecretStore` e `IAgentCredentialProvider`; `SecretReference` opaca e versionada está em Core. Os resultados não carregam texto de exceção/backend e usam `Unavailable`, `Locked`, `Denied`, `Cancelled`, `NotFound` e `Corrupt`. Cancelamento pelo chamador continua via `CancellationToken`/`OperationCanceledException`; `Cancelled` representa o prompt de desbloqueio dispensado. `WindowsCredentialSecretStore` foi implementado de forma isolada em Infrastructure: **30 testes determinísticos aprovados** e **1 teste de round-trip nativo ignorado** por `1312 / NoLogonSession`, reconfirmado nesta continuação. `LinuxSecretServiceSecretStore` isolado passou **84 testes focados em execução anterior**, sem D-Bus/Secret Service Linux nativo; não houve execução Linux nesta continuação. Também há persistência de políticas/grants, evaluator, registry interno de `list_connections` e ledger `agentAuditEvents` versionados no único proprietário LiteDB. Provider/repository de política, repository de auditoria e evaluator estão registrados no DI. A composição, evaluator, registry, persistência de política e ledger passaram **77 testes focados Windows, 0 ignorados**. O registry revalida o snapshot e usa alias derivado do ID para nomes enviados a providers externos. O ledger ainda não está integrado ao registry; revisão independente identificou falha no correlacionamento de intents e campos ainda faltantes frente ao contrato de auditoria completo. Ainda faltam migração de credenciais, provider, protocolo MCP, integração de auditoria fail-closed, controles completos de saída, UI e ferramenta exposta. A revisão estática não é homologação nativa; AC-01..AC-20 seguem pendentes.

## Retomada da Fase 7 — Claude no Windows — 28/09/2026

**Atualização Claude assinatura — implementação no checkout:** o painel Agente IA oferece somente a CLI oficial Claude Code e preserva os métodos nativos de autenticação e cobrança definidos pelo CLI. Restrições de destino/transporte são independentes. O provider HTTP Anthropic legado fica fora da composição e do catálogo, sem fallback; a chave API legada no vault não é lida nem apagada, e conversas antigas não migram automaticamente. O gate do acordo comercial Anthropic continua aberto. Homologação manual/runtime real permanece pendente.

O estado atual, plano em ordem de execução, limitações e roteiro de aceite estão consolidados em [Fase 7 — Claude](phases/phase-07-v0.11.0/README.md). Foram finalizados o vínculo do snapshot do workspace ao turno, aviso de sessão perdida, permissões/auditoria das ferramentas, cancelamento isolado e UI de confirmação; concessões pendentes agora são descartadas ao atualizar/fechar o escopo e tickets MCP ficam vinculados à geração do turno. Escrita nativa `Edit`/`Write` permanece desabilitada porque a CLI poderia escrever após a aprovação sem verificar atomicamente buffers sujos; alterações seguem por proposta mediada.

Validação atual: restore `--locked-mode`; build sem avisos/erros; Infrastructure.Agents.Tests **246/246**; Benchmarks **43/43**; testes focados de política, permissões, sessão, localização e persistência **61/61**; testes de processos órfãos **2/2**. A suíte UnitTests integral obteve **3.774 aprovados, 12 falhos e 23 ignorados**. As falhas observadas envolvem `CredentialStoreFailed` no cofre/registro de canais, coerente com a limitação Windows `NoLogonSession` já documentada; não são tratadas como aprovação nem corrigidas por mock nesta retomada. PNGs reais do cartão de aprovação foram revisados em tema claro/escuro. Não houve login com Claude Pro real, ferramenta MCP contra MongoDB real nem aprovação comercial da Anthropic para a modalidade integrada; os gates continuam abertos.

## Meta P7-COP — GitHub Copilot por assinatura — 29/09/2026

Implementação inicial integrada aos contratos de provider/runtime e às telas existentes de conta. A autenticação continua delegada à Copilot CLI oficial (`copilot login`); o SDK fornece runtime RPC headless, não login interativo. “Testar conexão” consulta estado e catálogo oficial de modelos para conta `user` sem inferência; `GetStatusAsync` só lê snapshot. O provider mantém chat indisponível. Em 29/09, o build Windows passou sem avisos/erros; os filtros automatizados Copilot e chat/reserva passaram anteriormente, assim como publish Release Windows x64. O host reserva o ID nativo e confirma sua gravação/releitura no repositório LiteDB antes de iniciar sessão; falha bloqueia o envio. O SDK recebe o ID reservado e pode retomar/recriar a sessão somente sob o mesmo ID; erros ambíguos ficam fechados. O opt-out configura SessionFs/SQLite volátil em memória por ID; roteamento total e limpeza ainda precisam de comprovação pelo runtime real. Após o login concluído pelo usuário, teste explícito Windows-only confirmou assinatura (`AuthType=user`) e o modelo elegível `auto`, sem prompt/contexto nessa consulta. Outro teste criou uma sessão autenticada volátil com zero ferramentas; a query falhou antes de emitir delta ou idle, com diagnóstico sanitizado relacionado ao modelo. A sessão volátil foi limpa; com `EnableSessionStore=false`, não havia arquivo nativo a apagar. Portanto login e catálogo estão confirmados, mas a conversa real, streaming, tools MongoDB, retenção/erase e cancelamento continuam pendentes. Restore `--locked-mode` passou anteriormente em CPU, WinML e CUDA. Linux está fora do escopo de homologação e aceite P7-COP. A suíte integral anterior teve Benchmarks 43/43, UnitTests 3.766 aprovados, 11 falhas e 42 ignorados; Agents.Tests teve 322 aprovados e 11 falhas em fixtures Codex. Essas falhas são alheias ao Copilot e não fecham os gates da meta.

A inspeção do SDK 1.0.14 confirma persistência local do conteúdo de sessões e artefatos; `EnableSessionStore=false` desativa busca cross-session, não essa gravação. `KeepHistory=false` passa pelo SessionFs e pelo SQLite em memória; o runtime oficial aceitou a negociação inicial e uma sessão volátil real foi removida no cleanup, mas o roteamento total ainda não está comprovado. O contrato exige `DeleteSessionAsync` nativo antes de remover conversa persistente do LiteDB; falha de leitura, sessão ilegível/sem ID ou erro/cancelamento da exclusão nativa bloqueia a remoção LiteDB. Fake STDIO verifica chamadas do adapter e continuidade por ID, mas não comprova retenção/erase do runtime. O teste autenticado Windows confirmou assinatura e modelo `auto`; a query real terminou em erro sanitizado relacionado ao modelo antes de streaming. Cancelamento/retomada real, ferramentas MongoDB, limpeza de dados persistidos, keyring e diálogos nativos seguem pendentes. Linux está fora do escopo de homologação desta meta. O chat fica bloqueado até esses gates serem demonstrados. Ver [plano e critérios P7-COP](phases/phase-07-v0.11.0/README.md#meta-p7-cop--github-copilot-por-assinatura) e [ADR-059](10-decisoes-arquiteturais.md#adr-059--github-copilot-por-assinatura-via-cli-oficial-28092026).

**Revisão de autenticação de sessão em 29/09:** verificamos no SDK 1.0.14 que `CopilotClientMode.Empty` desabilita keytar e não reconhece o login da CLI; o cliente foi alinhado ao modo `CopilotCli`, com mesmo `COPILOT_HOME`, allowlist e recursos ambientais restritos. O fake STDIO também passou a usar esse modo e verifica o payload efetivo de create/resume. O teste concorrente confirma que cancelar uma conversa não aborta outra. O filtro Copilot passou **56/56**, reserva do chat **2/2** e build completo da solução foi revalidado com 0 avisos/erros. O teste comparativo de autenticação não envia prompt, mas a conta aparece atualmente `NotLoggedIn`; novo login pelo fluxo oficial é necessário antes de executar inferência. Runtime autenticado, allowlist real e retenção/erase ponta a ponta continuam pendentes; o provider segue indisponível.

**Rechecagem após login informado pelo usuário (29/09):** o teste manual Windows-only, sem prompt, consultou conta e sessão pelo runtime oficial; ambos retornaram `IsAuthenticated=false` e autenticação ausente. O login ainda não está visível para o SDK/CLI no ambiente da aplicação. Não houve inferência, e autenticação/modelos elegíveis continuam pendentes. O filtro Copilot passou **56/56**, o build completo passou com 0 avisos/erros e `git diff --check` não apontou erros.

**Correção do erase Copilot (29/09):** a camada de permissões agora chama um coordenador de apagamento no chat. Ele preserva IDs legíveis, apaga sessões nativas antes do `DeleteAllAsync` LiteDB e somente depois atualiza a UI; falha em qualquer etapa fica visível e permite retry. Novo teste cobre falha nativa sem exclusão LiteDB, falha LiteDB mantendo a referência e retry idempotente até concluir. `AgentChatCopilotReservationTests` + `AgentCliAccountUiTests`: **9/9**. Risco ainda aberto: com histórico ativo, o runtime grava em `%USERPROFILE%\\.copilot\\session-state`; falta separar o SessionFs persistente da CLI oficial antes de homologar retenção/erase. O estado de login mais recente continua `NotLoggedIn`; nenhum prompt foi enviado.

**SessionFs persistente e rechecagem Windows (29/09):** o adapter grava sessões ativas em `%LOCALAPPDATA%\\EsilvaSoft\\KapibaraStudio\\Copilot\\sessions`, com diretório derivado por hash do ID, arquivos atômicos, cotas e SQLite isolado. A revisão posterior adicionou lock cross-process exclusivo por ID e erase coordenado: só remove os arquivos depois do delete nativo bem-sucedido. Teste Windows com processo PowerShell confirmou o lock; suíte persistente **11/11**, Copilot **67/67** e chat/reserva **9/9**. A consulta oficial, sem prompt, ainda reporta `NotLoggedIn` para conta e sessão, com 0 modelos. Ainda falta provar roteamento completo pelo runtime e remoção ponta a ponta de artefatos reais. Provider e chat seguem bloqueados; homologação exclusivamente Windows.

**Lock cross-process e ordem de erase (29/09):** locks de arquivo exclusivos por sessão agora impedem que stores concorrentes abram o mesmo ID. A limpeza adquire o lock antes do delete nativo pelo SDK e remove arquivos somente depois do sucesso; lock ocupado ou falha nativa conserva dados e permite retry. Um teste Windows usa processo PowerShell separado para verificar exclusividade. Resultados atuais: store persistente **11/11**, regressão Copilot **67/67**, chat/reserva **9/9**, build da solução 0 avisos/erros. A conta oficial ainda retorna `NotLoggedIn`/0 modelos; conversa autenticada e roteamento SessionFs real pendem homologação.

**Sessão vazia no runtime oficial (29/09):** a nova verificação manual Windows-only criou e limpou uma sessão sem prompt usando `AvailableTools=[]`; passou **1/1**. Uma tentativa de retomada antes de qualquer mensagem falhou porque o runtime não encontrou eventos da sessão; esse cenário não prova retomada após conteúdo real e permanece fora do aceite. A rechecagem de autenticação após o login informado retornou `IsAuthenticated=false` nos dois clientes e nenhum modelo foi consultado. Retomada com histórico, ferramentas reais, streaming, erase ponta a ponta e login ainda estão pendentes; Linux não integra a homologação.

**Diagnóstico adicional de login (29/09):** `GetAuthStatusAsync` retornou `false` nos modos SDK `CopilotCli` e `Empty` e no cliente Copilot de sessão. O processo oficial `copilot login` foi aberto após falha de acesso no sandbox, mas terminou antes de alterar o estado detectado; não capturamos saída nem lemos credenciais. O usuário precisa concluir o fluxo interativo oficial em terminal visível; a causa da divergência segue indeterminada. Nenhum modelo ou prompt foi consultado nesta rodada.

**Homologação oficial Windows — autenticação e streaming (29/09):** a comparação foi refeita fora do sandbox porque o teste restrito não tinha acesso ao Credential Manager. O runtime oficial reportou autenticação de usuário nos clientes de conta e sessão `CopilotCli` e na CLI instalada; `Empty` retornou não autenticado. As consultas de modelos dos clientes de conta e sessão retornaram o mesmo catálogo elegível (1 modelo), sem inferência. Depois, um teste enviou uma única mensagem sintética sem contexto e sem ferramentas numa sessão volátil; o runtime produziu delta e `idle`, e o cleanup removeu a sessão. Resultados dos testes explícitos manuais: status/modos **1/1**, catálogo conta/sessão **1/1**, streaming/cleanup volátil **1/1**. Isso ainda não homologa histórico persistente, retomada após restart, ferramentas do produto, cancelamento ou erase com conteúdo; chat permanece fechado.

**SessionFs, retomada e erase com runtime oficial (29/09):** teste manual autenticado enviou uma mensagem sintética sem contexto/tools para uma sessão com SessionFs persistente temporária; o runtime escreveu três arquivos sem que o teste lesse seu conteúdo. Depois de encerrar o primeiro cliente SDK, um segundo cliente retomou a mesma ID pelo SessionFs com `ResumeSessionAsync`. O cleanup coordenado removeu os arquivos e passou a asserção de diretório ausente. Teste explícito Windows-only passou **1/1**. Isso comprova o caminho básico de persistência/retomada/erase após reinício de cliente, mas não falhas/crash/concorrência reais, tools do produto ou cancelamento; provider/chat continuam bloqueados até fechar esses gates.

**Cancelamento real e correção de autenticação (29/09):** rechecagem Windows fora do sandbox confirmou assinatura (`AuthType=user`) nos clientes SDK produtivos e na CLI instalada; o catálogo elegível coincide (1 modelo, `auto`). Streaming real sem tools, persistência/retomada SessionFs após restart do cliente, erase temporário e cancelamento após delta passaram, cada teste manual explícito **1/1**. No cancelamento, o adapter registrou `MayHaveTakenEffect`, sem alegar rollback, e limpou a sessão volátil. As tentativas `NotLoggedIn` anteriores decorreram da falta de acesso do sandbox ao Credential Manager e são evidência histórica, não o estado atual. Faltam passagem de ferramentas do produto pelo runtime e gates de falha/concorrência/recuperação com runtime oficial; provider/chat seguem indisponíveis. Homologação só Windows.

**Gate de agentes internos do SDK (29/09):** auditoria do XML local do SDK 1.0.14 não encontrou configuração inicial para lista permitida vazia; existe apenas denylist por nomes e atualização posterior ainda sem prova de ordem/efeito. Não usar wildcard sem contrato. Mantemos o provider Copilot bloqueado até comprovar isolamento contra ferramentas/agentes fora do registry. A licença e assinatura estão autenticadas, porém isso não basta para habilitar o chat.

**Isolamento de agentes internos confirmado (Windows, 29/09):** o SDK oferece `Options.UpdateAsync(includedBuiltinAgents: [])`. O adapter aguarda `Success` após create/resume e antes do primeiro envio; sem sucesso encerra o turno. O fake verifica payload vazio e ordem pré-send, e um teste Windows sem inferência verificou `Success=true` e lista oficial de agentes vazia **1/1**. A correção fecha o gate de agentes built-in. Restam o fluxo de ferramenta de produto através do runtime real e os cenários de falha/concorrência/recuperação de retenção com runtime oficial; chat segue bloqueado.

**Escopo de ferramenta nativa por turno (Windows, 29/09/2026):** login Copilot realizado, conforme confirmação do usuário; a homologação real de conta, catálogo, streaming, SessionFs, retomada, erase e cancelamento já está registrada na Fase 7. Foi adicionado escopo interno efêmero indexado por sessão e turno com o plano, permissões e snapshot capturados pelo runtime. O registry real permite somente `get_workspace_context` para principal interno e exige ferramenta no plano, provider/sessão/turno coincidentes e `TabMetadata`; a entrada MCP mantém o fluxo anterior. Testes: conjunto UnitTests de registry/chat/runtime **51/51**; filtro Copilot adapters **67/67**; build completo **0 avisos/erros**; `git diff --check` passou (avisos apenas de conversão LF/CRLF). Homologação manual Windows com modelo oficial chamou essa ferramenta no registry real e registrou auditoria (**1/1**), usando somente diretório temporário sintético, sem MongoDB ou arquivos do usuário. Restam ferramentas MongoDB com permissões e testes oficiais de falha/concorrência/recuperação; chat permanece bloqueado até fechar esses gates. Linux não será homologado.

**P7-COP — listagem MongoDB pelo runtime Copilot (Windows, 29/09/2026):** homologação manual com modelo oficial chamou `list_connections` pelo `AgentRuntime` e `AgentToolRegistry` reais (**1/1**). A política em memória concedeu apenas `ReadMetadata` para um perfil sintético e a chamada produziu evento de ferramenta concluída e auditoria; não abriu conexão MongoDB. O registry anonimiza nomes de conexão para destinos externos e lista apenas perfis autorizados. O teste confirma o caminho de autorização de metadados, não uma consulta ao servidor MongoDB. Consulta real/sintética com servidor de teste, negações ponta a ponta do modelo e gates oficiais de falha/concorrência/recuperação permanecem pendentes; provider/chat seguem indisponíveis. Linux não será homologado.

**P7-COP — tentativa da fixture MongoDB (Windows, 29/09/2026):** o teste de integração `AgentReadToolsUseLiteralCodecPoliciesAndDurableLedgerAgainstRealServer` foi selecionado, mas ficou **Ignorado** porque não existe `mongod.exe` na cache portátil do projeto nem `SLOP_CONSOLE_MONGOD` configurado. Não tentamos usar perfil/URI real do usuário nem instalar servidor automaticamente. A consulta Copilot a uma ferramenta dependente de MongoDB permanece pendente até haver uma fixture Windows de teste; Linux está fora do escopo.

**Rechecagem final Windows (29/09/2026):** o build completo da solução havia passado com **0 avisos/erros**; os filtros Copilot passaram novamente (**67/67** em Infrastructure.Agents) e a homologação manual oficial `list_connections` (**1/1**) permanece registrada acima. Corrigi o setup do fake Codex para não iniciar restore implícito dentro do teste: o restore locked pertence ao comando da solução e o subprocesso agora usa `--no-restore`, além de anexar stdout/stderr em caso de falha. Com isso, o conjunto Agents reduziu de 142 para **10 falhas**; a execução atual aprova **340**, e as 10 restantes falham em `CodexAppServerHome.PrepareWindows` ao tentar alterar ACL no ambiente restrito (`UnauthorizedAccessException`). A suíte UnitTests segue em **3.772 aprovados, 11 falhas e 42 ignorados** (3.825), incluindo Credential Manager nativo; Benchmarks **43/43**. A execução conjunta sem rebuild terminou com os mesmos resultados por projeto. `git diff --check` passou (avisos somente de normalização LF/CRLF). A fixture MongoDB segue ausente, portanto não houve consulta Copilot a servidor real. Permanecem pendentes runtime oficial em falha/concorrência/recuperação, negação ponta a ponta por modelo real, consulta MongoDB, acessibilidade/leitor de tela e diálogos nativos; provider/chat continuam indisponíveis. Linux não será homologado.

**Buffer ativo e fixture CI (29/09/2026):** cada turno registra se o anexo Arquivo ativo realmente foi incluído. propose_file_edit para `ctive_buffer é recusado tipadamente quando o chip foi removido, sem abrir permissão persistente; o alvo workspace mantém a própria autorização. mongo_explain permanece sob consentimento conservador de consulta/documentos por poder revelar filtros. O filtro MongoDB da CI inclui os testes de leitura derivada e preserva o teste de codec/ledger existente, usando bancos aleatórios `gentdb_<guid> e limpeza somente do banco criado pelo teste. Build da solução passou com 0 avisos/erros; verificação focada de runtime/anexo/registro e UI passou 4/4. A suíte UnitTests mais recente antes destas mudanças teve 3.802 aprovados, 11 falhas de Credential Manager/Windows Credential Manager no ambiente restrito e 42 ignorados. A fixture Mongo não está disponível localmente; a extensão da CI ainda não foi executada, portanto não há homologação de consulta MongoDB real.

**Vínculo íntegro do anexo com o snapshot (29/09/2026):** a atestação do chip agora valida que existe exatamente um anexo `ActiveFile`, que seu conteúdo é idêntico ao texto redigido do `WorkspaceContext` e que `TabId` e `DocumentVersion` coincidem com os da solicitação. Conteúdo, aba ou revisão divergentes (ou revisão ausente) fazem `active_buffer` retornar `ActiveFileSnapshotMismatch` sem registrar proposta; propostas de arquivo do workspace continuam independentes. Auditoria delegada detectou o gap bool-only e os testes focados passaram 8/8, incluindo correspondência, divergências e ausência da revisão.

**Reexecução integral após hardening do preflight de cofre (29/09/2026):** UnitTests terminou com **3.810 aprovados, 0 falhas e 53 ignorados** (3.863 total, 2m14s). Onze testes de integração nativa foram ignorados com motivo explícito porque o Credential Manager desta sessão não permite escrita; quatro `AgentDerived*` seguem ignorados sem MongoDB local. Build da solução passou com 0 avisos/erros e a regressão focada do agente/editor passou 142/142. Isto é evidência local Windows; CI do commit de release ainda precisa ser executada.

**Estabilização local do pré-release v0.11.0 (29/09/2026):** identifiquei que os lockfiles do backend CPU não incluíam `Microsoft.Data.Sqlite` e impediam restore locked dos pacotes Linux. Atualizei os lockfiles de Agents/Desktop para CPU; restores locked CPU e WinML passaram. `build-release.ps1 0.11.0 -SkipTests` gerou `artifacts/release/0.11.0` para win-x64, win-arm64, linux-x64 e linux-arm64, com `SHA256SUMS.txt`; verifiquei os quatro SHA-256. O workflow agora marca toda versão 0.x como pré-release, conforme a política do README. Na configuração Release, a solução teve 4.197 aprovados, zero falhas e 63 ignorados (Unit 3.810/53, Infrastructure.Agents 344/10, Benchmarks 43/0). Os 10 skips ACL e 11 skips do cofre são limitações verificadas deste host; quatro testes Mongo seguem sem fixture local. A CI remota do commit, runtime oficial Copilot com MongoDB de teste e gates nativos permanecem pendentes; não criar tag/publicação a partir desta árvore ainda não commitada.

**Meta transversal adapters do SO — validação final Windows (30/09/2026):** após extrair o runtime ONNX para `Infrastructure.LocalAi.OnnxAdapter`, fixtures passaram a usar caminhos sintéticos com base explícita e `MongoWorkspaceService` passou a receber `IMongoClientPool`. Os testes Mongo de recusa usam pool e acesso a arquivos falsos, ambos verificando zero chamadas. Restore locked CPU, build CPU da solução (0 avisos/erros) e suíte completa passaram: Benchmarks 43/43, Infrastructure.Agents.Tests 196/196, UnitTests 3318 aprovados/20 ignorados e IntegrationTests 860 aprovados/14 ignorados. Builds cruzados linux-x64 de Desktop e McpServer passaram. A auditoria de UnitTests/TestSupport encontrou apenas leitura de stream de assembly e escrita em stream de memória; Infrastructure.Agents.Tests usa handler HTTP falso. WSL e Docker não estão disponíveis, portanto runtime e testes nativos Linux continuam pendentes. Evidência e inventário em `docs/architecture/system-adapters.md` e `docs/architecture/unit-resource-audit.md`.
