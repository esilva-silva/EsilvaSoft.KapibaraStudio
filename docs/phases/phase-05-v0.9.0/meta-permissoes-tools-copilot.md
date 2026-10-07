# Meta F5-COP-PERM — corrigir acesso às tools do GitHub Copilot

**Registro:** 05/10/2026; retomada e encerramento em 06/10/2026. **Estado:** concluída para o problema de acesso às tools e conexões. Correções e validação automática registradas; o usuário confirmou que realizou os testes manuais no client e que as ferramentas e conexões passaram. Essa confirmação substitui as pendências dos retornos anteriores. [Aceite manual](#aceite-manual-do-usuário--06102026). Esta conclusão não encerra os demais gates da Fase 5.

**Vínculo:** Fase 5 / v0.9.0, P7-COP-04/05/06/07 e [meta de implementação](meta-de-implementacao.md), especialmente autorização, contexto do turno, confirmação e propostas. Esta meta não encerra os aceites existentes nem altera a prioridade da Fase 8.

## Objetivo e resultado esperado

**Pedido adicional de 06/10/2026:** o usuário solicitou tornar a cota configurável nessa janela, com padrão 100 e campo vazio sem limite. O contrato e as evidências desse incremento estão na seção [Limite configurável solicitado pelo usuário](#limite-configurável-solicitado-pelo-usuário--06102026); os registros anteriores de cota fixa 20 descrevem o comportamento anterior ao pedido.

Corrigir inconsistências entre as permissões salvas do GitHub Copilot, o contexto capturado, o plano de ferramentas, os grants temporários e a autorização efetiva no registry. Uma chamada permitida deve alcançar sua fonte somente depois dos gates aplicáveis; uma chamada recusada deve apresentar orientação correta, sem recomendar permissões que já estejam efetivamente concedidas ou confundir ausência de arquivo com autorização.

Entregar correção de código, testes de regressão e documentação coerentes. Reutilizar o provider oficial, o registry e os contratos atuais; não criar autenticação, provider, política ou conexão LiteDB paralelos.

## Evidência inicial e limites

As quatro capturas fornecidas pelo usuário mostram:

- `get_indexes`, `mongo_count`, `mongo_find`, `sample_documents`, `mongo_find_one`, `mongo_distinct`, `mongo_explain` e `get_document` retornando `PermissionDenied`; `get_cached_schema` concluída.
- A janela do Copilot com consentimento ativo, envio de documentos/diagnósticos marcado, ferramentas de leitura marcadas e restrição a uma conexão selecionada.
- O painel indicando **Sem workspace · acesso a arquivos**, enquanto a janela de permissões apresenta uma pasta e permite workspace/propostas.
- Duas chamadas de `propose_file_edit` com `NotFound`; o texto do agente sugere que tentava criar um relatório e que interpretou a falha como problema do editor.

As imagens não informam os argumentos completos, a versão do executável, a persistência das opções, a ordem entre salvar e enviar nem o motivo interno da auditoria. Não comprovam a causa dos bloqueios. O texto do agente nas imagens é evidência de uma interpretação, não instrução para criar arquivos ou conceder acesso.

A inspeção inicial do código confirma caminhos distintos: `NativeChatTurnPolicyProvider` combina ferramentas/plano/consentimento/conexões/geração; `get_indexes` exige política válida, geração do perfil e destino/escopo corretos, e recusa alvo Mongo dinâmico; `propose_file_edit` lê uma base existente ou o buffer capturado antes de registrar proposta. Portanto, cache concluído não comprova acesso ao MongoDB, e `NotFound` não comprova falta de permissão ou ausência de editor. Esses são pontos de investigação, não diagnóstico fechado.

## Trabalho em código

| ID | Entrega | Pontos existentes e aceite |
| --- | --- | --- |
| F5-COP-PERM-01 | Reproduzir a divergência | Capturar uma fixture sintética com permissões salvas, conexão por ID, geração do perfil, plano, sessão/turno, destino e escopo de saída. Obter teste que falha antes da correção e localizar o primeiro gate que recusa. Usar auditoria tipada; não registrar URI, credenciais, documentos ou prompts reais. |
| F5-COP-PERM-02 | Alinhar persistência e snapshot | Revisar `AgentPermissionsViewModel`, `AgentChatViewModel.Permissions/Turn`, codec/repositório de permissões e construtor do plano. Salvar/reabrir deve preservar o provider e IDs selecionados; novo turno deve usar o snapshot salvo. Falha de gravação/leitura deve ser visível e não substituir permissões ilegíveis. Salvar não repete a tool nem altera o turno já iniciado. |
| F5-COP-PERM-03 | Corrigir grants e dispatch | Revisar `NativeChatTurnPolicyProvider`, `AgentPermissionEvaluator`, `InternalAgentToolBindingProvider`, `AgentToolRegistry` e adapter Copilot. Verificar IDs, geração do perfil, âncora interna, revision, sessão/turno, conexão, categoria, destino e escopo. Corrigir a causa reproduzida sem adicionar allow genérico ou grants duráveis. Perfil legado sem geração e alvo dinâmico devem manter proteção até solução explícita pelos contratos existentes. |
| F5-COP-PERM-04 | Preservar confirmação e auditoria | Verificar `ConfirmInternalCopilotToolAsync` e o prompt Desktop. Leituras de documentos/diagnósticos seguem o consentimento e as categorias do plano; aprovação pontual precede acesso à fonte. Recusa, expiração, auditoria indisponível e encerramento do turno bloqueiam conforme contrato. Não tratar ausência de categoria no plano como uma nova recusa implícita sem decisão arquitetural. |
| F5-COP-PERM-05 | Diagnóstico correto no cartão | Distinguir permissão persistente ausente, contexto inválido, conexão não autorizada/obsoleta, confirmação recusada/expirada, falha da fonte e falha de auditoria. Aproveitar motivos tipados existentes, com orientação sanitizada e localizada. `Permissões…` deve ser útil para a causa; não oferecer essa ação como solução para toda falha. |
| F5-COP-PERM-06 | Coerência de workspace/proposta | Revisar captura do workspace e chips, `AgentToolRegistry.ProposeFileEdit`, `AgentWorkspacePaths`, store/applier e descrição/schema enviado ao modelo. Reproduzir a divergência entre pasta exibida e snapshot do turno. Buffer ativo autorizado usa `target=active_buffer`; arquivo existente autorizado usa caminho dentro da raiz. Arquivo inexistente mantém recusa explicável; a tool atual não cria arquivos. Tornar explícito esse limite para evitar tentativas de criação e diagnóstico falso de editor indisponível. |

A criação de arquivos novos não faz parte desta correção. Se o diagnóstico exigir mudar contratos públicos, permissões, migração ou semântica de revogação, registrar a decisão e seu aceite antes de incorporar a alteração. Correções de carregamento/persistência devem usar o proprietário LiteDB registrado em DI, com migração aditiva/versionada quando necessária.

## Matriz de regressão e aceite

| Cenário | Evidência exigida |
| --- | --- |
| Permissões salvas, conexão permitida, geração atual e novo turno | `get_indexes` e as sete leituras MongoDB das capturas são autorizadas; fonte chamada uma vez após os gates. `get_cached_schema` preserva seu contrato sem disparar amostragem ou consulta. Estender regressões às demais tools do catálogo de 14. |
| Tool desligada, consentimento de documentos desligado ou conexão fora da seleção | Negação antes de ler/enviar dados; motivo coerente; cache/metadados não herdam indevidamente autorização para documentos. |
| Salvar, fechar/reabrir permissões e reiniciar o app | IDs/opções preservados e aplicados ao próximo turno; teste cobre repositório real e perfil legado sem abrir segundo owner. Falha ou documento ilegível continua visível e protegido. |
| Trocar provider, Explorer, conexão, aba ou workspace durante awaits | Operação mantém origem capturada; grants não passam entre providers/conversas/turnos; perfil/geração alterados são revalidados antes de acesso/publicação. |
| Revogar/alterar opções enquanto há turno em execução | Preservar a semântica vigente: opções salvas afetam próximos turnos; fechamento/cancelamento do escopo impede callbacks tardios. Não prometer interrupção retroativa de operação já autorizada. |
| Duas conversas, cancelamento e resposta tardia | Cancelar uma não cancela a outra; nenhuma chamada tardia usa grants de turno substituto; resultado permanece correlacionado ao request de origem. |
| Confirmação permitida, negada, expirada ou cancelada | Executar somente conforme plano/decisão; recusa não chama fonte; não repetir automaticamente a operação. Argumentos confirmados são os executados. |
| Fonte ou auditoria falha, seguida de novo turno saudável | Resultado e diagnóstico sanitizados; auditoria obrigatória falhando impede acesso/liberação conforme gate; recuperação explícita sem replay e sem conteúdo sensível em logs/snapshots. |
| Buffer ativo sem caminho, arquivo existente e arquivo inexistente | Proposta revisável no alvo capturado; `NotFound` explica base ausente sem sugerir falta de editor. Não criar/salvar arquivo no disco. |
| Chip removido, fora da raiz, exclusão, link e buffer/base alterados | Manter recusas e revalidação após await; nenhuma proposta escapa do workspace ou sobrescreve buffer alterado. Revisão/aplicação e Undo respeitam o modo capturado. |
| UI de permissão/diagnóstico alterada | Testes Headless e inspeção dos PNGs reais em temas claro/escuro, idiomas suportados e painel estreito/escala alta. Registrar separadamente teclado, foco, leitor de tela e janela nativa pendentes. |
| Runtime oficial e MongoDB descartável | Repetir o caso relatado com dados sintéticos, conexão permitida, aprovação/recusa e novas mensagens após salvar; registrar SO, versão CLI/SDK, argumentos sanitizados e resultado. Fake STDIO ou Headless não encerram esse gate. |

## Sequência e verificação

1. Levantar motivo tipado e reprodução mínima dos bloqueios de leitura, preservando o estado preexistente do checkout.
2. Reproduzir separadamente workspace/proposta; confirmar se `NotFound` decorre de tentativa de criar arquivo ou de captura/resolução incorreta.
3. Corrigir a causa no fluxo existente e executar testes dirigidos de permissões, registry, binding Copilot, persistência, confirmação, propostas e snapshots.
4. Executar restore/build/testes previstos no `AGENTS.md`; não habilitar benchmarks nem iniciar CLI oficial/conta/servidor real nos testes automáticos.
5. Gerar e inspecionar PNGs se houver alteração visual; executar homologação oficial Windows com fixture MongoDB descartável e registrar o que depende de ambiente disponível. Linux conserva o recorte vigente da Fase 5; evidência Windows não homologa Linux.
6. Atualizar documentação, evidências e estado de cada entrega. Encerrar esta meta somente com correção e validação proporcionais; pendências reais permanecem explícitas.

```powershell
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore
dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore
node scripts/build-docs-index.cjs
```

Se a telemetria externa do Avalonia for bloqueada no ambiente isolado, usar `-p:UsedAvaloniaProducts=` para validação, mantendo analisadores e testes. Testes oficiais são manuais/opt-in; resultados ignorados não contam como aceite.

## Documentação e definição de concluído

- [Guia](../../14-guia-de-uso.md): salvar versus próximo turno, conexão selecionada, envio de documentos, confirmação e recuperação; esclarecer buffer/arquivo existente e o limite de criação de arquivos.
- [Design system](../../17-design-system-ui-ux.md): estados/ações de diagnóstico e coerência do resumo do workspace se a UI mudar.
- [ADRs](../../10-decisoes-arquiteturais.md): registrar decisão se contratos de autorização, geração, migração ou propostas mudarem; preservar ADR-059/062 e a autenticação oficial.
- [Catálogo](../../03-catalogo-funcional.md), [plano](../../09-plano-de-implementacao.md), [acompanhamento](../../12-acompanhamento-da-implementacao.md), [matriz](../../15-matriz-de-validacao.md), README da fase e meta principal: separar planejado, implementado, teste sintético e homologação real por entrega. Regenerar o índice offline.

- [x] Causas reproduzidas em código para leitura com credencial no cofre, resumo de workspace, recusa de arquivo base ausente e diagnóstico de cota; resultado funcional na aplicação aprovado pelo usuário em 06/10/2026.
- [x] Correção de código e regressões positivas/negativas implementadas, preservando isolamento, opt-outs, limites BSON/EJSON, confirmações e auditoria.
- [x] Restore, build e testes pertinentes aprovados e registrados; PNGs reais inspecionados.
- [x] Caso relatado validado manualmente na aplicação pelo usuário em 06/10/2026: ferramentas e conexões aprovadas. A confirmação não acrescenta homologação de acessibilidade ou Linux.
- [x] Documentação e índice offline alinhados ao comportamento comprovado; nenhuma alegação de suporte integral baseada só em doubles.

**Registro inicial:** a montagem desta meta usou capturas e inspeção estática, sem alterar o produto. A execução posterior está registrada abaixo; testes sintéticos não comprovam a causa exata na instalação das capturas.

## Execução — 05/10/2026

O usuário confirmou que salvou as permissões antes da nova mensagem. Mais tarde reservou para si a validação manual ao rodar a aplicação. Não foi solicitado nem lido qualquer URI, senha, token ou arquivo de credenciais real.

**Causa reproduzida nas leituras:** a composição de `MongoAgentFindSource`, `MongoAgentIndexSource` e `MongoAgentExplainSource` omitia `ISecretStore`. `OperationEnvironment` rejeita um perfil com `SecretReference` quando esse adapter está ausente; o registry classificava a falha da fonte como `PermissionDenied`. Isso explica um caminho em que as permissões estão corretas, o cache funciona e a leitura Mongo falha antes de pedir um cliente. Não prova que o perfil das capturas usa essa modalidade de credencial.

A correção injeta o mesmo cofre MongoDB registrado no container e usa `IMongoClientPool`, sem mudar consentimentos, grants, exposição, limites ou autenticação Copilot. O URI retornado pelo cofre continua validado contra a versão redigida do perfil antes de preparar o cliente. Credencial ausente, negada, ilegível ou divergente continua bloqueando o acesso. Não há fallback para URI sem senha, retries automáticos nem segredo em resultados/auditoria.

**Diagnóstico:** `AgentToolInvocationResult` preserva o motivo interno `ExecutionFailed` e agora publica esse código seguro quando a falha era mascarada como `PermissionDenied`. O runtime mostra falha de execução e o cartão não recomenda abrir permissões. Negações de policy/grant continuam negadas; validação rejeitada não é promovida a sucesso. Os cartões atualizam os rótulos ao trocar de idioma.

**Workspace e proposta:** o handler da conta Copilot retorna `AgentCliReadScope.None`, pois não oferece ferramentas nativas. O resumo agora usa a pasta permitida para as tools do produto em vez de interpretar esse `None` como ausência de workspace. O prompt esclarece que `propose_file_edit` não cria arquivos e que `NotFound` significa ausência do arquivo base; o cartão apresenta a orientação localizada. O contrato de proposta permanece edição revisável de buffer/arquivo existente, com limites e revalidação próprios.

**Evidências dirigidas:** `CopilotReadCredentialCompositionTests` reproduziu **8 falhas antes** da correção, todas sem chamada ao cofre, e os mesmos **8 casos passaram depois**. `MongoAgentReadCredentialTests` passou **18/18**: oito paths de leitura com credenciais sintéticas, quatro categorias de falha com recuperação e seis casos de cancelamento concorrente, com adapter que respeita ou ignora o token. `MongoOperationContext` revalida o cancelamento após a resposta do cofre antes de permitir pedir cliente. Nenhum cliente real é criado. Quatro casos adicionais exercitam registry/binding/policy/LiteDB de produção e verificam que conexão fora da seleção não lê o cofre. Foco combinado **41/41** unitários e **13/13** integração/UI. A matriz gera 72 PNGs em quatro idiomas, dois temas, três larguras e três escalas; oito amostras finais foram inspecionadas. Manifest com arquivos/hashes em `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/TestResults/copilot-permissions-ui-manifest.json` (artefato local ignorado pelo Git).

**Verificação final:** restore locked aprovado; build da solução com `UsedAvaloniaProducts=` e `-m:1` aprovado, **0 avisos/erros**. Suíte com `NUnit.NumberOfTestWorkers=0`: Infrastructure.Agents.Tests **398**, IntegrationTests **1.013** e UnitTests **3.588** aprovados, **4.999** no total, sem falhas. Testes oficiais/OS/modelos permanecem explícitos ou ignorados; benchmarks não foram habilitados. A execução anterior com workers padrão apresentou duas falhas de limpeza de `WORKSPACE-LOG.DB.tmp` em fixtures LiteDB preexistentes; a execução serial verde não comprova a solução dessa intermitência. Não foram modificadas essas fixtures para ocultar falhas. Após o último ajuste visual de conversa retomada, rebuild da solução passou e regressões de chat/conta passaram **67/67** unitários e **56/56** integração/UI. Relatórios locais `copilot-permissions-verified.trx`, `copilot-permissions-chat-final.trx` e `copilot-permissions-ui-final.trx` nas pastas `TestResults` dos projetos.

**Validação manual reservada ao usuário:** após compilar e abrir o app, salvar permissões, enviar nova mensagem para a conexão permitida e revisar a confirmação aplicável. Conferir `get_indexes` e leituras Mongo; repetir com conexão fora da seleção para verificar recusa. Conferir pasta no painel, proposta em buffer/arquivo existente e orientação de arquivo inexistente. CLI oficial, MongoDB real, janela nativa, teclado e leitor de tela permanecem sem nova evidência desta execução. Não há promessa de homologação Linux.

## Retomada — 06/10/2026

**Resultado informado pelo usuário:** 11 das 14 tools funcionaram; `get_document` e `mongo_explain` responderam `PermissionDenied`, e `propose_file_edit` foi bloqueada. As consultas foram limitadas a um documento ou `_id`, sem escrita MongoDB. O arquivo ativo era `teste.md`, nenhuma proposta foi aplicada e não foi criado relatório. Faltam argumentos sanitizados, código exato da proposta e motivo tipado da auditoria para localizar as recusas na instalação. Esse relato não comprova que faltou permissão persistente.

**Divergência reproduzida de proposta:** o plano admite edição de outros arquivos existentes quando `OtherWorkspaceFiles`, envio de arquivos e uso da pasta estão permitidos, independentemente da permissão do arquivo ativo. O gate nativo exigia também envio/edição do ativo e retornava `UnknownTool` antes do handler. Agora admite qualquer um dos dois pares autorizados; o handler continua conferindo o alvo concreto. A regressão aceita um arquivo existente do workspace, recusa o buffer e o caminho do ativo sem sua permissão e preserva `NotFound` para arquivo ausente. Não há criação, gravação automática ou ampliação de grants.

**Diagnóstico reproduzido de cota:** a admissão mantém 20 chamadas por turno, incluindo descobertas e repetições que chegam à admissão; testar 14 nomes não garante que houve apenas 14 chamadas. A 21ª chamada era publicada como `PermissionDenied` com auditoria `Denied/LimitExceeded`. Agora publica `ToolCallLimitExceeded`, mantém a mesma recusa/auditoria e orienta uma nova mensagem, sem ação de revisão de permissões. `Busy` continua separado para concorrência; a tabela de turnos também permanece limitada e, se o erro persistir em novo turno, a orientação é aguardar. Não se alterou a cota nem se adicionou replay.

O código novo é aplicado somente à recusa de admissão, não a estouro de saída ou a falha de policy, consentimento, namespace, geração, confirmação, validação de resultado ou auditoria. Testes dos paths Copilot `get_document`, `mongo_explain` e proposta conferem recusa sem acesso adicional à fonte/sink; uma proposta volta a funcionar em novo turno com as mesmas permissões. **Não há evidência de que a cota ou a divergência de alvo tenham causado os três bloqueios do usuário.**

**Reprodução antes/depois:** os dois casos iniciais falharam antes da correção: proposta workspace-only com `UnknownTool` e 21ª chamada com `PermissionDenied`. Após a correção, o foco de registry, gates, sessão, quota e runtime passou **253/253**; três regressões adicionais verificam os caminhos específicos. A matriz Headless gerou 72 PNGs com os três diagnósticos; amostras reais em quatro idiomas e ambos os temas foram inspecionadas. Evidência automática final registrada após a suíte abaixo; validação na aplicação permanece com o usuário.

**Próxima evidência manual:** após recompilar e reiniciar em momento escolhido pelo usuário, enviar mensagens separadas para `get_document`, `mongo_explain` e edição de `teste.md` com `target=active_buffer`, anexo ativo e consentimentos correspondentes. Se falhar, registrar argumentos sanitizados, código e motivo tipado, sem URI, segredo ou conteúdo real. Uma nova mensagem isola a hipótese de cota; não substitui a investigação dos demais gates. A meta permanece aberta quanto ao aceite real das 14 tools.

**Verificação automática desta retomada:** restore locked e build da solução aprovados, **0 avisos/erros**, usando `--artifacts-path .codex-build/copilot-followup`, `UsedAvaloniaProducts=` e `-m:1`. O build normal inicial encontrou o executável aberto bloqueado; a aplicação não foi fechada. UnitTests **3.593** e Infrastructure.Agents.Tests **398** passaram na suíte serial. IntegrationTests passou **974** casos na execução ampla e **39/39** na reexecução dos protocolos Copilot depois de disponibilizar na saída isolada os mesmos dois scripts sintéticos de runtime/compatibilidade: **1.013 casos distintos aprovados**, **5.004** somando os três projetos. Esse total combina execuções, não uma execução única verde da solução. CLI oficial, MongoDB real, modelos/hardware/native permanecem explícitos ou ignorados; benchmarks não executados.

Na primeira pasta isolada, a substituição textual usada pelas fixtures Claude para localizar seu executável falso também alterava o nome da pasta ancestral; 131 casos falharam pela localização do fake. Houve ainda uma falha preexistente de limpeza `WORKSPACE-LOG.DB.tmp` em `LiteDbSchemaLearningRepositoryTests`, não repetida na segunda execução ampla. Ao mudar a saída, arquivos gerados deixados na árvore de fontes causaram atributos duplicados; esses artefatos foram movidos para `.codex-build/copilot-followup-first`, preservando os relatórios/PNGs. Não foram alteradas fixtures ou asserções para esconder essas falhas. TRXs locais: `copilot-followup-red.trx`, `copilot-followup-gates.trx`, `copilot-followup-verified.trx`, `copilot-followup-integration-final.trx` e `copilot-followup-runtime-final.trx` nas pastas `TestResults` dos projetos.

Manifest dos **72 PNGs**, com SHA-256 e **oito amostras inspecionadas**, em `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/TestResults/copilot-followup-ui-manifest.json` (ignorado pelo Git). Índice offline regenerado para 111 documentos; snapshot e 1.126 links Markdown locais conferidos. Diff sem erros de whitespace. O estado staged preexistente foi preservado; as mudanças desta retomada permanecem no working tree.

**Auditoria complementar da composição — 06/10/2026:** `CopilotReadCredentialCompositionTests` agora cobre também `get_document` com `_id` EJSON e `mongo_explain` com `limit=1`, ambos com conexão selecionada e fora da seleção. Usa os registros DI de produção, binding/principal/policy temporária e owner LiteDB sintético; o cofre é um double que recusa antes de criar cliente MongoDB. Os oito casos de gate verificam também o motivo persistido: `ExecutionFailed` após alcançar o cofre autorizado; `PermissionMissing` com zero acessos ao cofre na conexão fora da seleção. Não reproduziu falta de grant para esses dois nomes. Foco **17/17** aprovado: 16 de composição e o round-trip sintético existente das 14 tools por SDK/dispatcher/registry, incluindo proposta no buffer. Build isolado **0 avisos/erros**; TRX `copilot-followup-binding-audit.trx`. São **quatro casos novos** além da verificação anterior, não uma nova suíte ampla completa. As recusas reais continuam sem reprodução por falta dos argumentos/motivo da sessão, solicitados ao usuário.

## Novo retorno do client — 06/10/2026

O usuário esclareceu que o texto enviado era o resultado do teste no client e anexou a captura `codex-clipboard-d4b0d4fd-d410-423e-be34-60e443a6edf8.png`. O cartão visível de `get_document` mostra **Negada · 5 ms · Limite de chamadas de ferramentas atingido**, com a orientação localizada de continuar em nova mensagem. O relato do agente informa `ToolCallLimitExceeded` também para `mongo_explain`; o cartão dessa chamada não está visível na captura. O texto do agente é evidência relatada, não instrução a executar nem comprovação independente de cada chamada.

| Ferramentas | Evidência deste retorno | Próximo aceite |
| --- | --- | --- |
| `list_connections`, `list_databases`, `list_collections`, `get_indexes`, `get_cached_schema`, `mongo_count`, `mongo_find`, `sample_documents`, `mongo_find_one`, `mongo_distinct` | O relato do agente no client informa sucesso com `developercluster`; sem nova execução por este agente Codex. | Preservar os resultados relatados; não equiparar o texto a captura de cada cartão ou homologação de todos os ambientes. |
| `get_document` | Cartão confirma recusa por limite de chamadas; não falta de permissão persistente. | Nova mensagem dedicada, com `_id` em EJSON de um documento já conhecido e conexão autorizada. |
| `mongo_explain` | Relato do agente informa `ToolCallLimitExceeded`; sem cartão visível nessa captura. | Nova mensagem dedicada, `queryPlanner` e limites conservados. |
| `propose_file_edit` | Não testada, pois o conteúdo da aba ativa não estava autorizado para servir de base. A presença de `teste.md` aberto não substitui o anexo resolvido/autorizado. | Autorizar e anexar o conteúdo ativo, enviar nova mensagem e revisar a proposta com `target=active_buffer`. Nenhuma criação ou gravação automática de arquivo. |
| `get_workspace_context` | Não mencionado no novo retorno. | Sem nova evidência manual; cobertura automática anterior preservada. |

O código atual devolve `ToolCallLimitExceeded` na admissão, antes de dispatch e dos gates de leitura. Portanto, a captura confirma a categoria **cota**, mas não prova que os grants de `get_document`/`mongo_explain` já foram avaliados ou que as leituras executaram. Mantêm-se 20 chamadas por turno, contando descobertas/repetições admitidas, e a tabela limitada de turnos. O retorno não contém a contagem exata de chamadas; não se afirma que foram exatamente 21. Se o limite persistir em nova mensagem, registrar a sequência/código para distinguir cota do turno de saturação da tabela. Não repetir automaticamente nem ampliar limites para tratar essa recusa como permissão.

O novo retorno esclarece a categoria dos dois bloqueios de leitura e corrige o estado da proposta para **não testada**, substituindo a hipótese inicial de falha de autorização nesses casos. Não é necessário enviar documentos ou credenciais reais para registrar esse diagnóstico. O aceite manual das duas leituras em turnos novos e da proposta com conteúdo autorizado permanece com o usuário; nenhum novo código ou teste de produto foi executado nesta atualização documental.

## Limite configurável solicitado pelo usuário — 06/10/2026

Na seção **Tools do KapibaraStudio** das permissões Copilot, o campo **Máximo de chamadas de ferramentas por mensagem** inicia em **100**. Vazio (inclusive só espaços) representa **sem limite de quantidade por mensagem**; valores preenchidos devem ser inteiros positivos. Zero, negativos, decimais, texto ou overflow impedem salvar e mostram orientação localizada. Salvar usa a revisão CAS existente e vale para novas mensagens; alterar o campo não muda uma mensagem já em execução nem repete uma chamada recusada.

O valor é persistido em `AgentProviderPermissions.MaximumToolCallsPerTurn`, na faceta do owner LiteDB já registrado. O formato v2 grava a propriedade explícita, incluindo null para ilimitado. A leitura de v1 sem propriedade assume 100 em memória e não reescreve o documento; o próximo save bem-sucedido grava v2. Documento v2 com propriedade ausente, tipo inválido ou valor não positivo permanece ilegível e protegido contra sobrescrita. Testes fecham o owner antes de abrir/reabrir fixtures offline; não existe segundo owner do banco real.

O registry obtém a cota do escopo nativo Copilot de sessão/turno/provider corretos; a primeira admissão mantém o valor para esse turno. O runtime também captura o valor da requisição e deixa de interromper Copilot no antigo teto de 32. Assim, padrão 100 e ilimitado funcionam nos dois níveis. Os caminhos sem esse escopo e as demais integrações conservam seu comportamento anterior. Vazio não desativa concorrência, limite de turnos rastreados, deadlines, leases de operações tardias, limites de dados, consentimentos, grants, confirmações ou auditoria.

O campo tem rótulo/ajuda acessíveis e tradução pt-BR/en/es/zh-CN. A ajuda e o erro ficam junto à entrada na rolagem central. O footer passa a status em linha própria acima das ações para impedir sobreposição em 520×500 com texto longo. A matriz cobre quatro idiomas, dois temas, três tamanhos (520×500, 700×720, 960×760), três escalas (100/150/200%) e três estados (100, vazio, inválido): **216 PNGs**. A inspeção e os resultados finais de testes são registrados abaixo; runtime oficial, MongoDB real, teclado e leitor de tela nativos permanecem na validação manual do usuário.

**Verificação do incremento:** restore locked e build da solução em `.codex-build/copilot-followup`, com `UsedAvaloniaProducts=` e `-m:1`, aprovados, **0 avisos/erros**. Foco inicial **53/53** unitários e **52/52** integração; este último antecedeu os ajustes de footer e cinco casos adicionais de documento ilegível. A suíte ampla serial passou **3.603** unitários e **398** de adapters; IntegrationTests passou **1.036** e apresentou duas falhas: a asserção da UI consultava o `IsEnabled` local em vez de `IsEffectivelyEnabled`, controlado pelo comando, e `UnreadableChannelRowIsReportedAndNeverRewritten` teve cleanup `WORKSPACE-LOG.DB.tmp` intermitente na fixture existente. A asserção passou a verificar habilitação efetiva e a matriz também passa a digitar pelo binding real do TextBox. Nenhuma fixture LiteDB foi modificada para esconder a falha. Build final aprovado e reexecução de UI/persistência/caso de cleanup passou **42/42**. Assim, **1.038 casos distintos de integração** passaram em execuções complementares, **5.039** somando os três projetos; não se trata de uma execução única verde da solução, nem de prova de resolução da intermitência LiteDB. CLI/MongoDB reais e testes explícitos permanecem fora desta execução; benchmarks não habilitados.

**Evidência visual final:** foram gerados 216 PNGs; oito amostras reais foram inspecionadas nos quatro idiomas, ambos os temas, três tamanhos/escalas e estados padrão, vazio e inválido. Campo/ajuda/erro ficam legíveis, salvar desabilita efetivamente para valor inválido e o footer não sobrepõe os botões. Manifest com SHA-256 e amostras em `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/TestResults/copilot-configurable-budget-ui-manifest.json` (local ignorado). TRXs locais: `copilot-configurable-budget-unit.trx`, `copilot-configurable-budget-integration.trx`, `copilot-configurable-budget-verified.trx` e `copilot-configurable-budget-ui-final.trx`. Documentação e índice offline atualizados; alterações staged preexistentes preservadas. Após recompilar e reabrir a aplicação, o usuário pode conferir 100, salvar vazio, iniciar nova mensagem e reabrir as permissões para verificar a escolha. O aceite manual integral das tools continua separado.

## Aceite manual do usuário — 06/10/2026

O usuário confirmou: **as ferramentas funcionaram e os testes manuais de ferramentas e conexões passaram**. A validação foi realizada por ele no client, conforme combinado. Esse retorno encerra a pendência funcional do caso de acesso e substitui os estados anteriores de leituras ainda não confirmadas e proposta não testada. Os registros anteriores permanecem como histórico da investigação.

- [x] Ferramentas e conexões: teste manual aprovado pelo usuário.
- [x] Resultado manual registrado separadamente dos testes automáticos, com encerramento desta meta de correção de acesso.

O usuário não forneceu inventário por chamada, versões de CLI/SDK, parâmetros ou uma nova matriz por sistema operacional; esses detalhes não foram inventados. A confirmação não acrescenta prova manual específica de cada valor da cota configurável, leitor de tela, teclado, Linux, outras integrações ou dos demais gates da Fase 5. A cobertura automática do campo de cota permanece registrada na seção anterior. Esta atualização altera somente a documentação; não executa novamente as ferramentas nem os testes do produto.
