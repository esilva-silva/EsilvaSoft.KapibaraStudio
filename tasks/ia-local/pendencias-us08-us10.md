# Pendências de KS-US08, KS-US09 e KS-US10 — instruções de continuação

**Estado:** levantamento de 15/11/2026 sobre `d8be8dc` em `master` com alterações locais não commitadas (testes Explicit do agente e opções novas do `kapilab agent run`). Este arquivo **não** executa nem homologa entrega; o estado de cada tarefa continua no próprio arquivo dela.

[Índice IA local](README.md) · [KS-US08](us-08-autocomplete-explicito/README.md) · [KS-US09](us-09-concorrencia-e-ciclo-de-vida/README.md) · [KS-US10](us-10-avaliacao-real-e-evidencias/README.md)

## Antes de começar

1. Ler [AGENTS.md](../../AGENTS.md), [CLAUDE.md](../../CLAUDE.md) e os `AGENTS.md` locais dos diretórios que for editar.
2. Ler o [contrato de IA local](../../docs/MVP/contratos/ia-local.md), [arquitetura e invariantes](../../docs/MVP/03-arquitetura-e-invariantes.md) e [validação e evidências](../../docs/MVP/06-validacao-e-evidencias.md).
3. Regras que não podem ser quebradas: tools de agente nunca consultam documentos nem escrevem no MongoDB; não compartilhar `CancellationTokenSource` entre abas; capturar origem antes de `await`; um único proprietário LiteDB (DI); nunca sobrescrever sessão ilegível; CI nunca baixa nem carrega modelo; testes com modelo real são `[Explicit]` + `Category("LocalModelIntegration")`; benchmarks nunca rodam no CI.
4. Ao terminar cada item, atualizar a tabela "Registro de execução" e o checklist da tarefa correspondente com data, ambiente, comandos e resultado **observado**. Não copiar contagens antigas. Evidência sintética, real e nativa ficam separadas.

## Modelos disponíveis

Publicados no Hugging Face (namespace `esilva`). Apenas os repositórios `-ONNX` são instaláveis pelo Studio; os demais são os pesos-fonte (Safetensors), informativos.

| Papel | Repositório ONNX (instalável) | Fonte | Variantes no catálogo do Studio |
| --- | --- | --- | --- |
| Autocomplete Nano (Qwen2.5-Coder-0.5B) | [esilva/KapiCoder-Mongo-Nano-ONNX](https://huggingface.co/esilva/KapiCoder-Mongo-Nano-ONNX) | [esilva/KapiCoder-Mongo-Nano](https://huggingface.co/esilva/KapiCoder-Mongo-Nano) | `cpu-int4` (padrão), `cpu-int8`, `dml-fp16`, `htp-fim-exact-v1` (NPU, runtime não homologado) |
| Autocomplete Small (Qwen2.5-Coder-1.5B) | [esilva/KapiCoder-Mongo-Small-ONNX](https://huggingface.co/esilva/KapiCoder-Mongo-Small-ONNX) | [esilva/KapiCoder-Mongo-Small](https://huggingface.co/esilva/KapiCoder-Mongo-Small) | `cpu-int8`, `dml-fp16`, `htp-fim-exact-v1` |
| Legado (FIM, linha SlopCoder) | [esilva/SlopCoder-Mongo-0.5B-ONNX](https://huggingface.co/esilva/SlopCoder-Mongo-0.5B-ONNX), [esilva/SlopCoder-Mongo-1.5B-full-ONNX](https://huggingface.co/esilva/SlopCoder-Mongo-1.5B-full-ONNX) | [0.5B](https://huggingface.co/esilva/SlopCoder-Mongo-0.5B), [1.5B-full](https://huggingface.co/esilva/SlopCoder-Mongo-1.5B-full) | Usado por `SLOP_QWEN_MODEL` em `RealModelStreamsATurnOffline` |
| **Agente** | **Nenhum publicado** | — | Só existe o candidato local `KapiCoder-Mongo-Agent-Qwen3-4B-Thinking-ONNX-dml-fp16` (Qwen3-4B-Thinking-2507 base, sem ajuste, **não qualificado**, só GPU DirectML) |

Observações:

- O catálogo padrão está em `src/EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi.OnnxAdapter/HuggingFaceModelSource.cs` (`DefaultRepositories`). A revisão fixa do Snapdragon está em `src/EsilvaSoft.KapibaraStudio.LocalAi.Core/ISnapdragonModelProvisioner.cs`.
- Confirmar no card do repositório (ou na nota de entrega KT-US09-T03 do repositório `KapibaraStudio.MongoIA`) se a revisão publicada é o pacote **v4** com `editor-context-v2` antes de declarar qualquer resultado de KS-US08-T02/T03.
- Nenhum pacote declara `runtime.prefixCache="reuse"`: o cache de prefixo **continua `Disabled`**.
- Sem pacote de agente treinado e para CPU, qualquer rodada do agente é **prova funcional**, nunca medição de aceite (KS-US01-T01).
- Variáveis dos testes Explicit: `KAPI_AGENT_MODEL` (pasta do pacote de agente), `KAPI_AGENT_DEVICE` (`cpu`, `gpu` ou `auto`), `SLOP_QWEN_MODEL` (FIM). Não executar com modelo real sem autorização explícita do usuário.

## A — Implementável agora (sem modelo real, entra no CI)

Fazer nesta ordem; cada item é uma mudança pequena com teste próprio.

### A1. KS-US08-T01 — resposta obsoleta por troca de conexão, banco ou coleção

- **Situação:** o produto já invalida: `src/EsilvaSoft.KapibaraStudio.Desktop/WorkspaceTabView.Autocomplete.cs` (`CompletionContextChanged`, linhas ~154-157) chama `InvalidateCompletion` quando mudam `Profile`, `Database`, `Collection`, `Mode` ou `InputJson`. Falta teste.
- **Fazer:** em `tests/EsilvaSoft.KapibaraStudio.IntegrationTests` (classe `AiCompletionUiTests`), um caso parametrizado por `Profile`/`Database`/`Collection`: `Ctrl+;` com runtime falso que segura o resultado, alterar a propriedade da aba, liberar o resultado; afirmar que não há prévia, nada foi inserido e o runtime recebeu cancelamento. Basear-se em `AStaleResultIsNeverShownAfterANewEdit`.
- **Cuidado:** alterar a propriedade sem tirar o foco do editor, senão o teste passa por `LostFocus` e não pelo caminho que se quer provar.

### A2. KS-US08-T04 — colar e navegar não chamam o runtime

- **Fazer:** dois casos novos ao lado de `TypingNeverReachesTheAiEvenWithTheLegacyOptInAndAModelReady`: (a) colar um bloco (comando de colar do editor ou `Document.Insert` multilinha) e (b) navegar com setas/Home/End/clique. Runtime falso que falha se for chamado e serviço de modelo que falha se carregar.
- Medição manual do orçamento da busca de nomes por tecla fica no grupo B (não promover ao CI).

### A3. KS-US08-T02 e KS-US09-T03 — mapear opt-in JSON, fontes, rascunhos e orçamento

- **Fazer:** localizar e reexecutar os testes já existentes em `AiContextPipelineTests`, `WorkspaceSessionPersistencePolicyTests`, `LearnedSchemaCatalogSourceTests` e `ConnectionProfileTests`. Registrar nos arquivos das tarefas quais cobrem: opt-in de entrada JSON no contexto da IA, fontes habilitadas, opt-out geral e por conexão de rascunhos, e orçamento (Nano 4096/64, Small 8192/128) vindo do pacote ao escolher o modelo.
- Escrever teste **apenas** para o que não tiver cobertura. Não duplicar a implementação no teste.

### A4. KS-US09-T02 — liberação ao fechar o aplicativo

- **Fazer:** em `LocalAiModelServiceTests`, descartar o serviço (caminho de shutdown do app) com uma geração em andamento: a geração é cancelada, o runtime é liberado uma única vez, depois do retorno do trabalhador, sem dispose concorrente. Verificar também o caminho real de encerramento no `App`/host do Desktop (quem chama o dispose) e cobrir se houver lacuna.

### A5. KS-US09-T03 — canário de vazamento em sessão, contexto e log

- **Fazer:** um turno do agente local com runtime falso cujo resultado de tool e diagnóstico contêm marcadores únicos (e uma string em formato de credencial). Afirmar que os marcadores não aparecem no snapshot de sessão do workspace, no contexto serializado do agente nem no log capturado. Reusar o padrão do canário de raciocínio de KS-US05-T03.

### A6. KS-US09-T04 — recuperar a conversa local sem reexecutar nada

- **Situação:** `src/EsilvaSoft.KapibaraStudio.Desktop/ViewModels/AgentChatViewModel.Conversations.cs` restaura com `OpenConversationAsync(id, restoring: true)`. Só existe teste equivalente para Copilot: `ReservedIdIsDurableBeforeCreateAndSendAndRestoredWithoutAutomaticReplay` (`AgentChatCopilotDurableReservationTests`).
- **Fazer:** dois testes para o provider local: (a) conversa local persistida com chamada de tool já concluída; reabrir o view model; afirmar histórico íntegro, nenhum turno iniciado no provider, nenhuma tool executada, nenhum modelo carregado; (b) conversa gravada durante uma geração (encerramento no meio do turno); reabrir e afirmar estado interrompido visível, sem replay automático e sem perder as mensagens anteriores.
- Se (b) revelar que o estado intermediário não é persistido de forma recuperável, corrigir com migração aditiva e versionada no repositório LiteDB existente, nunca abrindo segunda conexão.

### A7. KS-US10-T01 — casos Explicit que faltam e conferência do CI

- **Fazer** em `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/LocalAgentProviderModelIntegrationTests.cs`: (a) várias chamadas de tool no mesmo turno (registry com `list_databases` e `list_collections`, resultados sintéticos); (b) recusa: pedido que só seria atendido por tool não permitida, afirmar que nenhuma tool fora do plano é executada e o turno termina tipado. Mesmos atributos `[Explicit]` + `Category("LocalModelIntegration")` e mesmas variáveis.
- **Conferir** em `.github/workflows/` que a categoria `LocalModelIntegration` é excluída e que só `UnitTests` e `Infrastructure.Agents.Tests` rodam. Rodar a guarda `RealModelHubAccessIsConfinedToExplicitLocalModelTests`.

### A8. KS-US10-T02 — `kapilab agent bench`

- **Situação:** `kapilab agent run` já tem `--seeds`, `--thinking default|on|off`, `--think-budget` e relatório `kapilab-agent-run-report-v1` (`tools/KapiLab/Core/AgentModelRunCommand.cs`, opções em `tools/KapiLab/Cli/KapiLabCommandLine.cs`). Não existe benchmark do agente.
- **Fazer:** subcomando `agent bench` reutilizando o executor de `agent run` e o padrão de `tools/KapiLab/Cli/BenchMatrixCommand.cs`: repetições com aquecimento descartado; tempo até a primeira resposta (média, p95), tokens de raciocínio, turnos com orçamento estourado, tool inventada, tokens por segundo, pico de RAM do processo; condição de cache e dispositivo registrados. Relatório versionado novo, só com hashes de conteúdo (sem prompts, respostas nem raciocínio).
- Separar no relatório os motivos hoje agrupados em `invalidToolCallTurns` (envelope inválido, tool fora do plano, limite de chamadas). Se o provider reporta o mesmo código para os três, acrescentar o detalhe tipado no provider sem mudar o código público existente.
- Testes em `tools/KapiLab.Tests` com provider falso (parsing, validação, resumo). O comando real exige opt-in local e nunca entra no CI. Documentar em `tools/KapiLab/README.md`.

## B — Exige modelo real, hardware ou desktop nativo (manual, com autorização)

| Item | Tarefa | O que fazer |
| --- | --- | --- |
| B1 | KS-US10-T01 | Diagnosticar o travamento de `CancellingARealAgentTurnEndsItWithoutAnErrorAndTheSessionAnswersAgain` (DirectML, 13/11): repetir com `dotnet test --blame-hang --blame-hang-timeout 5m` e depois com `KAPI_AGENT_DEVICE=cpu`, para separar provider de driver; comparar com a prova de cancelamento do KapiLab. Corrigir se for defeito do Studio |
| B2 | KS-US09-T01 | Cancelamento após o primeiro token com modelo real (o mesmo teste de B1) |
| B3 | KS-US08-T01 | Fluxo `Ctrl+;` → prévia → Tab/Escape com `KapiCoder-Mongo-Nano-ONNX` `cpu-int4` real |
| B4 | KS-US08-T03 | `kapilab bench autocomplete --session-mode --device cpu` nos pacotes Nano/Small: equivalência greedy ≥ 99%, acerto real ≥ 95%, T0 ≤ 150/250 ms, T1 ≤ 500/650 ms, TTFT e RAM com quatro sessões. Só habilitar `PrefixCacheMode=Reuse` se o pacote declarar `runtime.prefixCache="reuse"` **e** passar nos gates; senão registrar `Disabled` e o impacto |
| B5 | KS-US08-T04 | Medição manual do orçamento da busca de nomes por tecla |
| B6 | KS-US09-T02 | Memória antes/depois de carga e liberação com modelo real |
| B7 | KS-US10-T04 | Jornada em Windows nativo (`Ctrl+;`, Tab, Escape, envio ao agente, aprovação de tool, cancelamento, uso sem modelo); Linux nativo quando houver; registrar o que Headless não cobre (leitor de tela, IME, diálogos nativos) |

## C — Bloqueado por dependência externa

- **KS-US01-T01** precisa da nota de entrega KT-US09-T03 (repositório `KapibaraStudio.MongoIA`) para fixar pacote, SHA-256, hardware, corpus e **limites**. Sem ela, B6 e KS-US10-T03 não têm contra o que comparar.
- **KS-US10-T03** (medições de aceite) precisa do pacote de agente treinado e de variante CPU (KT-US07/KT-US09) e dos limites acima. Até lá: não declarar números de aceite.
- **KS-US10-T05** (consolidação em `docs/`, MVP-012/013, F02-E01..E05) depende de KS-US10-T03 e T04. Pode-se adiantar só a atualização de documentos para o que mudar em A1–A8.

## Verificação ao final de cada item

```bash
dotnet restore EsilvaSoft.KapibaraStudio.slnx --locked-mode
```

```bash
dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -p:UsedAvaloniaProducts=
```

```bash
dotnet test EsilvaSoft.KapibaraStudio.slnx --no-build --no-restore
```

```bash
dotnet test tools/KapiLab.Tests
```

```bash
node scripts/check-docs-reader.cjs
```

Falhas conhecidas em rodadas integrais (não mascarar, registrar se reaparecerem): `AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess` (variáveis de ambiente do Claude Code herdadas; deve passar fora dessa sessão), `PreviousSessionKeepsItsUuidRepresentationAndGetsStandardIdentifierMode("JavaLegacy")` (intermitente) e, em `KapiLab.Tests`, `RealWindowsCtrlCPreservesThePriorSampleAndPublishesAnIncompleteCancelledReport` (depende de console real).
