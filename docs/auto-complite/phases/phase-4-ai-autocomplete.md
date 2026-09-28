# Fase 4 — Autocomplete por IA reformulado

Roadmap: v0.9.0 · Depende de: Fase 3 · Habilita: Fase 5.2 (IA preemptiva independente)

## Objetivo

Implementar a IA explícita (`Ctrl+;`) sobre a infraestrutura ONNX existente: provider, Output Processor, prévia inline com streaming, fallback gracioso para a lista tradicional, métricas de latência e o experimento de reuso de KV cache — sem acoplar regras MongoDB ao runtime.

## Situação atual

- `AiAutocompleteProvider` (dentro de `AutocompleteService.cs`) gera uma continuação curta para o ghost, usando o contrato v1.
- Runtime cria gerador novo a cada pedido, re-tokeniza e decodifica a saída inteira por token.
- Não existe `Ctrl+;`; a lista `Ctrl+Espaço` mostra uma sugestão IA/básica no topo (removida na Fase 2).
- Serviço central, fila com prioridade, carga desacoplada, cooldown e seleção de hardware já funcionam.

## Incrementos

| # | Entrega | Resultado verificável |
| --- | --- | --- |
| 4.1 | `AiCompletionProvider` + `CompletionOutputProcessor` (desmembrando `AiAutocompleteProvider`) | Testes com runtime falso |
| 4.2 | Runtime: `PromptTokens`, `StreamAsync` com `TokenizerStream`, marcadores em cache, sem decodificação completa por token | Benchmark linear no número de tokens |
| 4.3 | Comando `Ctrl+;`: indicador, prévia inline progressiva, alternativas, fallback para lista com motivo | Headless + PNG |
| 4.4 | Matriz de fallback e timeouts | Um teste por linha |
| 4.5 | Perfil de latência por modelo/provider; política por modalidade | Testes com perfil falso |
| 4.6 | Experimento de prefix cache (`RewindTo`) com teste de equivalência | Relatório e decisão por provider |
| 4.7 | Métricas de IA e harness de latência | Relatório JSON |

## Alterações

| Projeto | Arquivo | Alteração |
| --- | --- | --- |
| Core | `Autocomplete.cs` | `ModelGenerationRequest.PromptTokens`, `PrefixCache`, `StopSequences` (aditivos) |
| Application | `AutocompleteService.cs` | Remove `AiAutocompleteProvider` interno; mantém fachada de preferências, status e teste |
| Application | `IAutocompleteService.cs` (`ILocalModelRuntime`) | `StreamAsync` com implementação padrão |
| Application | `ILocalAiModelService.cs`, `LocalAiModelService.cs` | Geração em streaming respeitando `PriorityGate`; prompt de teste obtido do adapter |
| Application | `LocalModelAiChatService.cs` | Sem mudança de comportamento; ajusta dependência se o provider mudar de nome — **arquivo removido em 25/09/2026 pela [ADR-055](../../10-decisoes-arquiteturais.md#adr-055--remoção-do-assistente-ia-por-aba-25092026)** |
| Infrastructure | `OnnxLocalModelRuntime.cs` | Prompt por IDs, streaming, prefix cache opcional, invalidações |
| Infrastructure | `ModelAdapters.cs` (`OnnxModelTokenizer`), `DeepSeekModelTokenizer.cs` | Decodificação incremental |
| Infrastructure | `ServiceCollectionExtensions.cs` | Registro do provider e do processor |
| Desktop | `EditorCommandDispatcher`, `WorkspaceTabView.*` | Comando `editor.completion.ai`, indicador e prévia |
| Docs | [21](../../21-autocomplete-local.md), [23](../../23-onnx-slopcoder.md), [26](../../26-ia-local-multimodelo.md), [17](../../17-design-system-ui-ux.md) | Comportamento, atalhos, métricas e evidências |

## Novos componentes

`IAiCompletionProvider`/`AiCompletionProvider`, `AiCompletionCandidate`, `AiCompletionUpdate`, `CompletionOutputProcessor`, `StructuralStopDetector`, `GeneratedChunk`, `PrefixCacheState`, `ModelLatencyProfile`, `AiRuntimeHarness`, indicador de geração (renderizador de fundo).

## Fluxo

[architecture.md — IA explícita](../architecture.md#ia-explícita-ctrl) e [ai-autocomplete.md](../ai-autocomplete.md#comportamento-de-ctrl).

## Dependências

- Fase 3: seleção de fatos, contratos, orçamento.
- Fase 2: dispatcher de atalhos, arbitragem, lista para fallback.
- Modelos reais e GPU para evidências `Explicit`.

## Performance

- Prioridade `Interactive`; preempção de geração `Background`.
- Prompt pré-tokenizado; `TokenizerStream`; `ArrayPool` para IDs.
- Prefix cache somente se equivalente e vantajoso.
- Medir por modelo × hardware × contexto × geração × prefix cache: construção de contexto, tokenização, TTFT, total, tokens/s, working set, tokens reaproveitados; tempo de UI durante geração.

## Testes

- Provider e processor com runtime falso: limpeza, parada estrutural, estilo, validação de catálogo, privacidade.
- Fallback: cada linha da [matriz](../ai-autocomplete.md#fallback).
- Cancelamento (`Esc`, nova edição), timeout com prévia parcial, preempção por chat/teste, resultado obsoleto descartado.
- Streaming: texto final igual ao não streaming (falso e real).
- Prefix cache: sequência de chamadas com fake; equivalência greedy com modelo real.
- Chat: `AiChatTests` e `LocalAiModelServiceTests` aprovados.
- Headless: indicador, prévia multilinha, `Tab`/`Esc`/alternativas, lista de fallback com motivo; PNG nos dois temas.
- Métricas: tags da lista permitida, sem texto.
- `Explicit`: matriz de modelos/hardware com relatório.

## Critérios de aceite

1. `Ctrl+;` sem modelo, com modelo inválido, sem capacidade, com provider explícito indisponível, em cooldown ou com contexto sensível abre a lista tradicional com a mensagem correspondente (um teste por condição).
2. `Esc` durante a geração cancela e remove o indicador; o runtime falso comprova interrupção no passo seguinte; teste real confirma `terminate_session` e recuperação.
3. Resultado obsoleto nunca é exibido (provider lento que ignora cancelamento).
4. Geração de `Ctrl+;` preempta geração inline em andamento; chat continua funcionando sem regressão.
5. Texto final em streaming idêntico ao modo não streaming em fixtures e em modelo real.
6. Custo de decodificação por token deixa de crescer com o número de tokens gerados (benchmark).
7. Prefix cache: teste de equivalência greedy aprovado nos pares modelo/provider habilitados; decisão por provider registrada com TTFT com e sem reuso.
8. Relatório `Explicit` com TTFT p50/p95, total, tokens/s e working set para cada pacote/hardware disponível, anexado à PR.
9. Orçamento de `Ctrl+;` → primeiro texto (revisado) atendido na máquina de referência com o pacote recomendado, ou desvio justificado.
10. UI dentro do orçamento por quadro durante geração em CPU (sem travamento observável em Headless e nativo).
11. Métricas emitidas somente com tags permitidas.
12. Suíte regular e build das três variantes aprovados; documentação 17, 21, 23 e 26 e matriz atualizadas; AC-10, AC-15 e AC-18 promovidas ou revisadas.

## Riscos

| Risco | Mitigação |
| --- | --- |
| API GenAI em preview mudar | Versão fixada; adaptação isolada no runtime |
| `RewindTo` incompatível com DirectML/captura de grafo | Desligado por padrão; habilitação por provider após teste |
| Validação incremental rejeitar prévias corretas | Confiança em vez de descarte no modo explícito; métricas de reversão |
| Qualidade insuficiente de modelos pequenos | Contrato avaliado na Fase 3; recomendação de pacote por hardware |
| Memória do KV para a janela inteira | Medir; limitar `max_length` ao orçamento efetivo |
| Contenção de CPU com a UI | Medir quadros; avaliar reserva de núcleo |
| Mensagens de fallback excessivas | Uma linha de estado discreta, sem diálogos |

## Fora do escopo

Políticas de disparo preemptivo (LoadedOnly é contrato de runtime desta fase), decodificação restrita (`SetGuidance`), múltiplos modelos simultâneos, NPU sem pacote/hardware disponível.


## Revisão de tarefas

[R41–R43 e A41–A44](../execution-plan.md) são fonte da ordem por agente. R41/R42 estabilizam load policy sob gate, motivos de erro, tokenizer e streaming; A41–A43 implementam pipeline/provider e Ctrl+;. Presenter vem de T07, não esperar Fase 5. AiPreemptiveCompletionProvider reutiliza pipeline, não é o mesmo provider explícito com comportamento implícito.

R43/prefix cache é experimento desligado e não bloqueia entrega; critérios acima de equivalência valem somente para pares habilitados. Warmup, alternativas e formatos experimentais não são obrigatórios. Timeouts medem ponta a ponta; fonte learned respeita política/revisões.
