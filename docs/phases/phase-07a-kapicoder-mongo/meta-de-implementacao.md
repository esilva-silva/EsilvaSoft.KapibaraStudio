# Meta de implementação — Fase 7A / F7A-KAPI

**Título:** preparação do EsilvaSoft.KapibaraStudio para KapiCoder-Mongo.
**Data:** 09/10/2026.
**Estado:** implementação parcial em andamento no checkout Windows, dividida entre frentes Sol (contratos/provider local) e Luna (tools/registry). Não é aceite integral dos 78 itens.
**Vínculo:** ADV-09 e EDT-02, preparação da Fase 8 / v0.12.0; sem release própria.

## Resultado esperado

Integrar os contratos necessários aos modelos KapiCoder-Mongo sem regredir autocomplete determinístico, pacotes SlopCoder/DeepSeek, isolamento de contexto ou segurança das tools. Concluir o `LocalAgentProvider` existente para pacotes Qwen3 de agente, com geração limitada, retomada controlada, permissões e propostas revisáveis. Disponibilizar contratos reproduzíveis para treino e avaliação externos. Aceitar NPU somente com prova no dispositivo alvo.

O usuário confirmou a preparação documental e depois aprovou Release com Copilot + IA local, texto de raciocínio com retenção/orçamento configuráveis e todas as tools locais com permissões. A ADR-070 registra essas decisões. As demais recomendações externas continuam propostas; esta execução não publica novos binários/pacotes nem declara capacidade homologada.

### Escopo desta meta de chat

Implementar e validar os itens que possam ser exercitados neste checkout Windows. A validação manual em ambiente Linux foi excluída expressamente pelo usuário e deverá ser realizada manualmente fora desta meta. NPU/dispositivo, avaliação de hardware e smoke com pesos/pacotes externos não disponíveis também não recebem aceite. A UI de raciocínio é transitória por padrão; U7 agora tem retenção opt-in configurável local, subordinada a KeepHistory, com prazo de 1/7/30 dias e política persistida pelo proprietário LiteDB. O owner aplica essa política dentro do mesmo gate dos saves para filtrar autosaves já enfileirados após opt-out; se a gravação da política falhar, o owner falha fechado em memória. Expirações são preservadas e limitadas entre autosaves; limpeza/purge são executados pelo owner registrado. Conversas que solicitam resultados/diagnósticos capturados não retêm raciocínio. A tela de permissões agora oferece ao provider local consentimento de envio de resultados/diagnósticos, tools atuais do registry por seleção, criação de arquivo opt-in e limite de chamadas, ocultando consentimento de destino externo e opções de tool nativa. A migração v1→v2 é aditiva. As suítes UnitTests e IntegrationTests passaram nesta máquina; isso não equivale a homologação com modelo real ou hardware.

## Fonte e baseline

Fonte: [kapibara-studio-ajustes.md](C:/IA/models/KapibaraStudio.MongoIA/docs/kapibara-studio-ajustes.md), backlog externo consultado em 09/10/2026, que cita auditoria no commit `dba8546` de 08/10/2026. Os marcadores `[F]` e `[H]` pertencem à auditoria externa; não são comprovação nova no checkout. A [rastreabilidade](rastreabilidade-f7a.md) preserva IDs, prioridades, dependências e estimativas das linhas, com enquadramento nesta meta.

Verificações pontuais desta preparação confirmaram: três pacotes GenAI fixados em `0.15.2` em `Directory.Packages.props`; `LocalAgentProvider` ainda exige FIM/chat para propostas e rejeita `SubmitToolResultAsync`; `App.axaml.cs` originalmente removia providers no ramo Release; o estado atual registra Copilot + local. Não foi encontrado KapiLab em `tools/`. Esse recorte não substitui a auditoria completa F7A-00.

O checkout já contém alterações da Fase 6. A execução futura deve registrar o baseline sobre esse estado e preservar trabalho alheio. Restore/build/test não foram executados nesta entrega documental.

## Trilhos e prioridade

| Trilho | Resultado | Condição |
| --- | --- | --- |
| T0/T1 → T2 | Pacotes `qwen2`/FIM compatíveis e verificados; download e versionamento como conveniência | A fonte não identifica P0 de código para estes tiers; confirmar com os pacotes reais |
| T4 Agent | Qwen3 com plano, tools e geração limitada no provider existente | Contratos e política aprovados; smoke GenAI; testes funcionais e avaliação com pesos |
| Agent-Lite | Mesmo caminho T4, perfil reduzido e limites próprios | A15 e TE3; Lite na NPU também depende do trilho T3 |
| T3 NPU | Pipeline e execution provider compatíveis com o dispositivo | R0 aprovado; se reprovar, registrar retirada do trilho sem bloquear T0/T1/T2/T4 |
| Suporte ao treino | Catálogo, planos e prompts reproduzíveis para o workspace externo | Fonte única nos contratos reais; ferramenta manual e sem acesso novo ao MongoDB |

Fase 7A integra a preparação prioritária de IA local. O workflow fechado da Fase 7 permanece independente. Não criar outro painel por aba, provider paralelo ou fluxo de autenticação.

## Decisões antes dos lotes dependentes

D1/D2/D3/D6 estão aprovadas na ADR-070; as demais recomendações são propostas técnicas. A numeração ADR-070–076 da fonte não foi importada: ADR-070 registra agora as decisões desta meta; conferir próximos IDs ao documentar contratos.

| Decisão | Questão e padrão vigente desta meta | Bloqueia |
| --- | --- | --- |
| D1 — aprovada | Mostrar o texto do raciocínio local em componente recolhível, distinto de `ThinkingSummary` e tratado como dados inertes | A10/A16, U1/U2 e testes de apresentação |
| D2 — aprovada | Release com Copilot + IA local após implementação/validação; ADR-070 substitui a exclusividade da ADR-068 | Exposição Release em F7A-08, sem nova aprovação de escopo |
| D3 — aprovada | Retenção configurável, padrão desligado; definir opções/prazo/limites/limpeza e respeitar opt-outs. Não autoriza persistir resultados/credenciais ou enviar a outro provider | U7 obrigatório; TA7 cobre retenção ativa, expiração e exclusão |
| D4 | Conteúdo de raciocínio no contexto transitório entre passos, teto acumulado e política de esgotamento | A6/A11 e testes de janela/contexto |
| D5 | Papéis autocomplete/agente, carga única ou simultânea. Preservar um modelo por vez até evidência e decisão diferentes | S2/U5; testes de troca e memória |
| D6 — aprovada | Todas as tools vigentes do produto disponíveis ao local, filtradas por modos/permissões/grants/confirmações. Dataset coincide com o catálogo seguro; sem consultas/escritas MongoDB ou tools nativas | A13a–d, U4, K1/K2 e SFT externo |
| Q1–Q5 | Fechamento forçado: texto/tokens do treino; fórmula do teto; resposta a bloco sem fechamento; quota efetiva do pacote e registry | Parser, orçamento, C7/C8 e A5/A11 |
| Q6 | Resolver gates conflitantes de TTFA das fases externas 06/08 antes de escolher orçamento padrão | U2/U5 e aceite de desempenho real |
| Q7–Q10 | Repositório Agent-Lite; reuso de papel Chat; origem confiável da versão GenAI; local das preferências | Catálogo/download, compatibilidade e migração |
| C6 | Adotar `kapi-context-v2` somente se avaliação justificar; manter `editor-context-v1` como contrato legado | Pacotes que declarem v2 e seus goldens |
| R0/R1/R5 | Validar NPU e versão alvo GenAI; `0.17.1` é proposta da fonte, não upgrade aprovado. Backend Qnn dedicado só após falha demonstrada da rota principal e decisão | Trilho NPU e redistribuição |
| KapiLab | Console detalhado na [Fase 7B](../phase-07b-kapilab/meta-de-implementacao.md), com Rota A proposta em `tools/KapiLab` e ADR-071 proposta. Esta fase fornece as superfícies/contratos do produto; 7B implementa o consumidor e seus relatórios | Definição arquitetural no bootstrap 7B; não duplicar consoles ou políticas |

D1/D2/D3/D6 estão aprovadas e não exigem nova confirmação. Orçamento configurável também está aprovado; fórmula, limites e padrão devem ser definidos com metadata e Q6. D4/D5 e questões técnicas podem ser resolvidas por spike/teste/ADR no escopo autorizado, pedindo decisão somente para ampliação além dele.

## Lotes executáveis

Cada lote termina com diff revisável, testes proporcionais e registro dos itens aceitos. Os testes TA acompanham a implementação; TE com pesos e medições são opt-in e manuais. Lotes agrupam itens, sem impor um PR por linha.

| Lote | Entrega e itens | Dependências | Aceite específico |
| --- | --- | --- | --- |
| F7A-00 | Auditoria, baseline, decisões e revisão das hipóteses externas | Fonte, código e ADRs vigentes | Inventário por ID; baseline e falhas preexistentes; decisões pendentes identificadas; contratos do treino reconciliados |
| F7A-01 | Smoke GenAI R7; spike NPU R0 separado | Pacote Qwen3 de controle; hardware para R0 | Relatório por EP de template/guidance/AppendTokens/RewindTo; R0 prova dispositivo ou registra retirada NPU; sem alterar produto |
| F7A-02 | Contratos C4/A10/A16 | F7A-00; D1/D4 | Campos aditivos; capacidades opcionais; provider sem capacidade recusa opções/eventos; providers externos conservam comportamento |
| F7A-03 | Metadata/adapter C2/C3/C5/C10/C11; TA1 parcial | F7A-01/02 | Qwen3 sem FIM; hash/template/tokens/versionamento validados; pacotes antigos intactos; marcadores não confiáveis inertes |
| F7A-04 | Geração C8/C7/R8/A12; KV A6 e TA5 | R7; D4/D5; metadata | Sessão descartada ao terminar/cancelar/expirar; aprovação não prende fila; greedy legado preservado; falha no passo 2 não reinicia agente em CPU |
| F7A-05 | Tools A13a–d e superfícies de K1/K2/K4; consoles na 7B | D6; contratos neutros | Plano ∩ permissões ∩ grants; binding local; confirmação/auditoria; catálogo/dataset equivalentes; regressões Copilot/Claude; sem consulta direta/escrita MongoDB |
| F7A-06 | Provider A1/A2/A3/A5/A7/A9/A11/A14, PC2; TA2–TA5/TA7 | F7A-03/04/05; decisões de orçamento | Prompt do treino equivalente; parser estrito e incremental; ciclo tool→resultado→resposta; teto de tokens/chamadas; cancelamento isolado; ações somente pelo registry |
| F7A-07 | UI U1/U2/U3/U4/U6; TA6 | Provider; D1/D3/D6; design system | Estados ligados às capacidades; quatro idiomas; migração aditiva; PNGs reais inspecionados; foco/teclado; sem depender apenas de cor |
| F7A-08 | Composição A8/E2 | D2 aprovada (ADR-070); capacidades e testes | Release com Copilot + IA local; sem pacote, estado indisponível sem anunciar capacidades ausentes; validar Debug; guarda Claude Release intacta |
| F7A-09 | Legado PC1/TE5/TE6; downloads S3/DL1; papéis S1/S2/U5; Lite A15/TE3; TE1/TE2/TE7 | Pacotes; provider; D5; Q6/Q7 | Testes por pacote/hardware; download seguro/recuperação; duas versões sem sobrescrita; recomendação sem troca automática; Lite limitado ao perfil aprovado |
| F7A-10 | NPU R1/R9/R2/R3/R4/C1/C5b/R6/U8/DL2/TA8/TE4/R10; R5 condicional | R0 aprovado; decisão runtime/EP; R7 para contratos usados | Upgrade sem regressão; pipeline contido; EP por ação explícita; offline/cancelamento; NPU explícita sem fallback; geração real no dispositivo |
| F7A-11 | U7 parcialmente implementado nesta execução; C6/A4 condicionais, M1 opcional, K3/superfícies KapiLab e E4; console na 7B | Retenção aprovada; decisões técnicas; lotes aplicáveis | Retenção configurável com migração/limpeza/expiração/purge implementada; ainda falta validar execução, falhas/concorrência/recuperação e inspeção visual; orçamento limitado pelo pacote; superfícies estáveis; documentação e notices; aceite por item |

Sequência T4: F7A-00 → 01 → 02 → 03/04/05 → 06 → 07 → 08 → 09. Interseções de geração e metadata devem ser integradas antes do provider. K1/K2 exigem descritores reais; romper o ciclo A13d↔K2 definindo primeiro política e descritores, depois goldens, depois verificação de equivalência. Não fabricar catálogo do treino para justificar liberação.

Trilho NPU: R0 → R1/R9 → R2/U8 → R3 → C1/C5b/R4/R6 → TA8/TE4/DL2/R10. Resolver o ciclo R2↔U8 com porta testável e integração UI no mesmo lote. Testes TA1 são particionados: agente primeiro, pipeline NPU somente quando aplicável. U3 pode ser validado em Debug antes de qualquer liberação Release.

## Critérios obrigatórios de aceite

1. **Compatibilidade:** `qwen2`, SlopCoder, DeepSeek e contrato v1 mantêm resolução e comportamento; campos novos opcionais não mudam flags persistidas; desconhecidos/incompatíveis falham com estado claro. Golden só muda com decisão contratual justificada.
2. **Contexto:** capturar perfil/banco/coleção/texto/opções/consentimentos antes do await. Edição, fechamento, revogação e troca de contexto invalidam respostas; mudar explorer não redireciona conversa/aba ou executa consulta. CTS por operação/sessão.
3. **Segurança:** tools locais usam a autoridade existente. Documentos/erros/logs vêm exclusivamente de execuções humanas capturadas e autorizadas. Nunca executar consulta direta ou escrita MongoDB, mesmo aprovada. `create_workspace_file` conserva opt-in, confirmação pontual e ausência de sobrescrita; proposta de edição conserva diff/revisão/undo.
4. **Parser:** JSON inválido/truncado, chunks partidos, tool desconhecida/removida/fora do plano, schema inválido e marcadores em conteúdo não confiável são recusados ou tratados como dados. Tool dentro do bloco de raciocínio não executa; retry limitado e erro tipado.
5. **Limites:** verificar janela antes de cada passo; teto de orçamento e chamadas com fórmula documentada. Fechamento forçado equivale ao treino. Sem truncamento silencioso, repetição de chamada negada ou duplicação por fallback no meio do turno.
6. **Geração/concorrência:** fila e preempção preservadas; aprovação humana não bloqueia descarte; cancelamento encerra passo sem despachar fragmento de tool e sem afirmar rollback. Liberar memória nativa ao terminar/falhar/cancelar/expirar. Troca de modelo não publica resultados obsoletos nem derruba o processo.
7. **Privacidade/persistência:** raciocínio não vai para histórico por padrão e nunca para auditoria/logs/métricas/outro provider. Retenção ativa guarda apenas conteúdo autorizado, com prazo/limites configurados, remoção ao expirar e por Apagar histórico; redigir segredos e saídas de documentos vedadas antes de persistir. TA7 cobre modos desligado/ligado, expiração, exclusão, falha/concorrência/recuperação com canário e store real temporário. Respeitar opt-outs e opt-in de JSON; não persistir resultados/credenciais nos snapshots. Migração aditiva usa proprietário LiteDB único; falha visível; sessão ilegível preservada.
8. **Degradação:** sem modelo, com capacidade ausente ou falha de EP, informar motivo e conservar autocomplete determinístico. Sem download/carga no startup ou na digitação. Download iniciado pela pessoa preserva instalação anterior e se recupera após falha/cancelamento.
9. **Apresentação:** pt-BR/en/es/zh-CN, Light/Dark, estados de geração/cancelamento/erro/limite/permissão/proposta. Inspecionar PNGs reais dos testes na matriz do design system; testar foco/teclado e registrar leitores de tela/diálogos nativos separadamente.
10. **Release/licenças:** composição segue ADR vigente. Licença MIT e identidade EsilvaSoft.KapibaraStudio preservadas; notices dos modelos/dependências e redistribuição EP revisados antes de publicar. Não remover SlopCoder por recomendação externa sem gates equivalentes.

## Verificação e evidências

Executar na implementação, preservando lockfiles:

```powershell
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore
dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore
```

Quando a telemetria Avalonia estiver bloqueada, usar `-p:UsedAvaloniaProducts=` e registrar a limitação. Testes de ferramentas Benchmarks somente com opt-in `EnableBenchmarkTests=true`; medições de latência/alocação jamais no CI/release. Validar backends/lockfiles afetados, sem criar matriz para variantes que não foram adotadas.

| Evidência | Como aceitar | Limite |
| --- | --- | --- |
| TA1–TA8 | Testes de contrato, parser, modos, segurança, falhas, concorrência e recuperação; TRX e commit | Fakes não provam fidelidade do modelo/hardware |
| R7/R0 | Smoke manual no build da IDE; versão runtime/EP, pacote/hash, dispositivo e resultado por função | Sem sucesso não assumir guidance, KV ou NPU |
| TE1/TE2 | Pacote agente: 20 cenários × modos; orçamento/cancelamento/contexto/KV | Opt-in `KAPI_AGENT_MODEL`, `KAPI_AGENT_REASONING=1`; medições fora do CI |
| TE3/TE4 | Lite CPU e NPU real; registrar provider efetivo e memória | `KAPI_AGENT_LITE_MODEL`, `KAPI_TEST_NPU=1`; variável não comprova execução |
| TE5/TE6/R9 | Pacotes legados/novos e regressão greedy, 120 pedidos quando aplicável | `SLOP_QWEN_MODEL`, `SLOP_DEEPSEEK_MODEL`, `SLOP_TEST_GPU=1`; TRX por pacote/hardware/modo |
| TA6/U6/TE7 | PNGs gerados e inspecionados; roteiro nativo e matriz por pacote/hardware | Headless não equivale a leitor de tela, MongoDB real ou diálogo nativo |
| K1/K2/K4/PC2 | Catálogo, schemas, planos, renderização e paridade com fonte real | Registrar commit/hash; drift exige revisão do dataset externo |

Guardar evidência sem conteúdo sensível em `artifacts/f7a/`: decisões, baseline, TRX, inventário de PNGs inspecionados e relatórios manuais com pacote/hash/EP/hardware/comandos. Comparações externas com Claude/Copilot usam só fixtures autorizadas e não são dependência de conta para aceitar contratos locais.

## Estimativa e definição de pronto

A fonte declara 78 itens e 136–171 pessoa-dia, incluindo opcionais, condicionais e homologação manual. É estimativa externa, não calendário aprovado. A rastreabilidade distingue contagem das linhas e resumo declarado; recalibrar em F7A-00 e após R7/contratos. Não somar TE7 novamente à Fase 10 nem prometer o prazo otimista de 8–11 semanas.

**Marco A — preparação automatizável:** contratos aplicáveis integrados, critérios automatizáveis aprovados, nenhuma falha crítica aberta, documentação sincronizada e decisões de corte/adiamento registradas. Não permite declarar fidelidade ou desempenho de pesos reais.

**Marco B — homologação real:** R0/R7 e TE aplicáveis aceitos com pacote/hash/provider/dispositivo, gates de qualidade e desempenho definidos sem conflito, jornadas nativas e recuperação verificadas. A Fase 10 concentra a homologação; ausência de TE1/TE2/TE3 bloqueia anúncio de agente/Lite homologado, ausência de TE4 bloqueia NPU. Não transferir defeitos automatizáveis para homologação manual.

No fechamento atualizar catálogo, plano, guia de uso, IA multimodelo/ONNX, acompanhamento, matriz e índice offline; ADR/design system quando decisão ou comportamento mudar. O planejamento inicial partiu de todos os itens como **planejados**; a tabela e o registro atuais refletem a implementação parcial desta execução. Itens não adotados devem registrar motivo, decisão e impacto; não contar como implementados.

### Registro desta meta de chat — 09/10/2026

- Implementados parcialmente: F7A-02/03 (contratos/metadata e adapter Qwen3), F7A-05/06 (tools locais mediadas pelo registry e provider), F7A-07 (controles/apresentação de raciocínio), F7A-08 (composição Release Copilot + local) e seletor de modelo Agent em preferências (U5). U7 também entrou parcialmente: opt-in local, política de retenção no owner LiteDB, 1/7/30 dias, expiração e purge específicos, exclusão de raciocínio em conversas que solicitaram dados/diagnósticos capturados e migração aditiva v1→v2. A restauração sinaliza raciocínio descartado sem guardar seu conteúdo. A13d/U4 agora apresentam tools de documentos capturados e criação de arquivo para IA local sob consentimentos separados e limite por turno. A14 fecha um item de raciocínio aberto no cancelamento pelo runtime com conclusão `Cancelled`; TA3/TA4 receberam cenário determinístico sem pesos. O catálogo ganhou fixture Qwen3 de agente sem FIM e cobertura de rejeição de tokenizer incompleto; R6 agora reconhece variantes QNN/OpenVINO e W4A16/W8A16. A cobertura exata por ID está parcial; isto não fecha os respectivos lotes por completo.
- Build integral Windows da solução, com `-p:UsedAvaloniaProducts= -m:1`: **passou, 0 avisos/0 erros**. Build dos projetos IntegrationTests e UnitTests também passou, **0 avisos/0 erros**.
- UnitTests via VSTest fora do sandbox: **3.748 aprovados, 20 ignorados, 0 falhas**. IntegrationTests: **1.049 aprovados, 20 ignorados, 0 falhas**. O VSTest requer loopback; dentro do sandbox o testhost não conecta, então as execuções foram feitas fora dele. Os ignorados são dependentes de ambiente/configuração explicitamente skipados.
- Testes do parser cobrem envelope dividido em fragmentos, schema/nome estritos, truncamento, profundidade e tamanho; os casos foram executados dentro da suíte aprovada.
- A tela Preferências → Autocomplete e IA local, incluindo seletor do modelo Agent, foi renderizada e inspecionada em Light/Dark por teste headless; a inspeção não cobre todas as telas/estados da Fase 7A.
- Validação manual Linux: **excluída da meta por solicitação do usuário**. R7/TE com pesos reais, NPU, inspeção de PNGs reais, acessibilidade/leitores de tela e diálogos nativos permanecem pendentes e não são aceites por builds Windows.
- O fechamento do orçamento de raciocínio ainda usa nova chamada de geração em vez de `Generator.AppendTokens` no mesmo estado/KV; a equivalência ao treino depende de smoke R7 e permanece pendente (A11).
