# Baseline de evidências e limitações

Consolidação em 06/11/2026 dos resultados necessários aos requisitos atuais. Não é nova execução, soma de snapshots diferentes nem homologação adicional. Contagens abaixo conservam data, ambiente e alcance; registros de artefatos são identificadores de reprodução, sem pressupor que arquivos locais de teste acompanhem todo checkout.

## Base cotidiana e cortes

Gestão de coleções, índices, views, validadores e transferência lógica possui aceite funcional anterior do Marco A. Isso não homologa todas as permissões, topologias, desktop Linux ou recuperação. Console/aggregate permanecem; o modo Script/mongosh e o editor Agregação separado foram retirados. Rascunhos recuperam texto/origem em Texto, sem execução. A execução e resultados de 06/11/2026 estão completos em [TODO de remoção](../Remover/TODO.md) e nas evidências por lote ali listadas: solução final com 5.268 aprovados, zero falhas e 40 ignorados, sem modelos/MongoDB reais ou release. Essa rodada não fecha os cenários nativos pendentes.

## Tools e arquivos — 07/10/2026, Windows

Aceite funcional das 11 tools vigentes e conexão MongoDB declarado pelo usuário em 07/10/2026. Preservar esse aceite; conta/CLI, topologias Atlas, Linux, acessibilidade e termos têm gates próprios.

A rodada de segurança teve Agents 403/403, UnitTests 3.442 aprovados/20 ignorados e IntegrationTests inicialmente 1.018 aprovados/uma falha/5 ignorados. Falha: teardown de `PersistedSecretScanTests.EncodedCanaryIsDetected` com `WORKSPACE-LOG.DB.tmp` em uso. O caso isolado passou 1/1 e a repetição da integração passou 1.019/5 ignorados/zero falhas; isso não diagnostica ownership do handle. Focos de captura/auditoria: `AgentQuerySnapshotToolTests` 20/20 e `AgentQueryExecutionCaptureTests` 2/2. Artefatos registrados sob `artifacts/agent-safe-tools/`.

Criação de arquivo teve build em saída isolada `bin/AgentFileCreate/net10.0/`, UnitTests finais 3.460 aprovados/20 ignorados/zero falhas e integração focal 26/26, incluindo I/O real em pasta temporária e roundtrip sintético SDK/STDIO. Uma rodada integral anterior teve duas falhas de cleanup (`PENDING.JSON.tmp` e `WORKSPACE-LOG.DB.tmp`); foco posterior 30/30 não torna a rodada anterior verde. Artefatos registrados sob `artifacts/create-workspace-file/`. PNGs reais pt-BR claro/escuro foram inspecionados de forma representativa; a matriz gerada de 216 frames não foi inspecionada integralmente. Não houve conta/CLI oficiais, leitor de tela ou filesystem Linux nativo nessa prova.

## Agente local e KapiLab — 11/10/2026

Prova de paridade editor/contexto: 2.000 registros (1.300 reais + 700 sintéticos), zero divergências; não equivale a 2.000 registros exclusivamente reais nem homologa chat/treino. Prompt do agente tem 200 fixtures de bytes/tokens. A paridade depende de reproduzir a transformação nativa dos schemas, não de aplicar diretamente o template HF ao envelope original.

Fixture kapi-sim: 315/315 casos de referência, 500/500 chamadas adicionais e replay 44/44, runtime/registry/política/auditoria reais e MongoDB Community 8.0.32 no WSL; grants/confirmações sintéticos, workspace/LiteDB temporários. Pelo menos 20 saídas de sucesso por tool aplicável; Atlas teve 20 falhas reais de serviço, sem prova de output schema de sucesso. Sem consulta direta de documentos por tools: resultados eram preparados por execução humana/serviço de query da fixture.

Rodada PR 3: 707 testes KapiLab por backend Cpu/WinML, 26 Python e 5.219 testes de produto aprovados, zero falhas. Artefatos registrados em `reports/lab/pr-3/reference-test.json`, `real-500.json`, `replay.json`, `summary.json` e `tests-real/`. Comparador externo tem gate ≥99% e zero permissões extras; comparação com o simulador externo e 20 sucessos Atlas permanecem pendentes. Catálogo completo não concede permissões de sessão, e esses resultados não medem inferência/task success.

Cancelamento real CPU é opt-in por `KAPILAB_REAL_CPU_MODEL`. As provas exercitam interrupção após primeiro token nativo, processo CLI filho e matriz local; confirmam saída do PID líder, não terminação universal de descendentes. Sem modelo configurado, não declarar esses testes executados. Procedimento do [laboratório](../../tools/KapiLab/process-cancellation-evidence.md).

## EOS/cache — 26/10/2026

Com a mesma DLL ORT CPU 1.28.0 por hash, 500 prompts sintéticos de cada tier T0/T1 passaram na comparação Python × IDE e duas repetições com cache/cauda mínima 32 tokens. Isso prova paridade de geração, sem avaliação humana/task success. TTFT e pico RAM com quatro sessões continuaram pendentes; nenhum pacote recebeu homologação de cache.

Wrapper CPU passou 714 testes; WinML passou 708 e ignorou seis provas opcionais. Não homologa GPU. Após correção da expectativa de `runtime` escalar, build e 24 testes focais passaram; a suíte integral não foi repetida após essa última correção. Atualização posterior para GenAI 0.18.0 exige revalidação; as provas 0.15.2 não demonstram equivalência dos modelos no runtime novo.

## Pacotes Linux — 27/10/2026

WSL2 Ubuntu 26.04/Fedora 44/Arch e contêineres mínimos exercitaram instalar, atualizar, downgrade e remover, preservando dados e arquivos gerenciados. ARM64 local usou emulação; Actions também exercitou deb/rpm em runner ARM64 nativo sob Xvfb, sem desktop interativo. Run registrado: 40591333319, commit `25e9a9b`, versão de teste `0.9.6-ci.1`, 16 jobs aprovados, release pulado em workflow_dispatch; nada publicado por essa rodada.

Build Linux: SDK 10.0.400, UnitTests 3.845 aprovados/20 ignorados, Agents 403/403 e 68 integrações focais. Build Windows final e UnitTests 3.845/20 ignorados passaram; integração updater 47 aprovados/um Linux-only ignorado. A rodada integral anterior de integração teve três teardowns LiteDB e um teste Claude bloqueado corretamente por variáveis herdadas. Arch aarch64 teve apenas análise estática. Não foram homologados Wayland nativo, clipboard, diálogos, leitor de tela, menus/ícones em desktop real ou AUR publicado.

## Próximas validações

Fase 2: pacote/corpus/hardware CPU e qualidade/latência/memória previamente fixados; ciclo do agente, cancelamento e retenção reais. Fase 3: conta/runtime oficiais, duas conversas, streaming sem replay, revogação/expiração e diagnóstico causal LiteDB. Fase 4: atualizar/reabrir/recuperar por modo. Fase 5: matriz gráfica/acessibilidade/MongoDB. Fase 6: artefatos finais auditados e decisão de termos. Usar o [procedimento de evidências](06-validacao-e-evidencias.md); nenhum desses gates é encerrado pela consolidação documental.
