# Autocomplete por IA

## Papel

Conclusão mais rica sob demanda (`Ctrl+;`), usando o modelo local. Compartilha AiGenerationPipeline e Output Processor com AiPreemptiveCompletionProvider; os providers são independentes. Falha explícita pode abrir lista tradicional se habilitada; falha automática apenas descarta.

## Cadeia

```text
AiCompletionProvider            (Application.Language.Ai)
        ↓
RelevantContextSelector + AiContextBuilder   (contrato do modelo)
        ↓
ICompletionPromptBuilder + ITokenizer        (adapter do modelo)
        ↓
ILocalAiModelService            (fila, prioridade, carga, cooldown — existente)
        ↓
ILocalModelRuntime / OnnxLocalModelRuntime   (genérico — existente, estendido)
        ↓
CompletionOutputProcessor
        ↓
Ranking / apresentação
```

Regras MongoDB ficam apenas nas duas primeiras etapas e no Output Processor. O runtime não conhece shapes, catálogo ou dialetos.

## Comportamento de `Ctrl+;`

1. Captura o contexto (mesma versão usada pela lista).
2. Mostra indicador de geração no cursor (camada de fundo, sem alterar texto).
3. `GenerateAsync` com prioridade `Interactive`, preemptando geração `Background` do preemptivo.
4. Tokens chegam por streaming; o Output Processor valida incrementalmente e a prévia inline cresce enquanto válida.
5. Tab aceita (inteiro ou incremental), Esc cancela/descarta. Alternativas e atalhos adicionais ficam adiados.
6. Se a IA não puder atender, abre a lista tradicional com uma linha de estado explicando o motivo.

A lista tradicional não espera a IA; `Ctrl+.` continua imediato mesmo com geração em andamento.

## Output Processor

| Etapa | Regra |
| --- | --- |
| Limpeza | Reaproveita `CleanGeneratedText`: eco do sufixo, cercas de código Markdown, nulos, marcadores reservados, tamanho |
| Parada estrutural | Corta quando a sugestão fecharia mais delimitadores do que os abertos desde o início do statement, ou ao completar o statement em modo inline |
| Sufixo | Remove sobreposição com o texto existente após o cursor |
| Estilo | Ajusta aspas ao estilo dominante detectado pelo contexto |
| Validação de catálogo | Identificadores em posição de campo/coleção/operador conferidos contra o catálogo; desconhecido com fontes frescas é apenas não observado; considerar cobertura/shape e permitir nomes de saída novos, sem tratar Complete como schema exaustivo |
| Validade sintática | Documento hipotético (prefixo + sugestão + sufixo) analisado pelo parser tolerante; diagnóstico novo reduz confiança |
| Privacidade | `CompletionPrivacy` na saída |
| Resultado | `AiCompletionCandidate(texto, confiança, faixa, diagnósticos)` |

## Múltiplos candidatos

Padrão: um candidato greedy. Alternativas via `num_return_sequences`/amostragem só entram se a avaliação mostrar ganho de aceite compatível com o custo (N× decode). Decisão na Fase 4 com medição.

## Fallback

| Condição | Tipo existente | Comportamento |
| --- | --- | --- |
| Modo Básico ou IA desabilitada | — | Lista tradicional; linha "IA desabilitada nas preferências" |
| Nenhum modelo selecionado | `LocalModelUnavailableException` | Lista + "Selecione um modelo em Preferências" |
| Arquivos ausentes, modelo inválido, tokenizer incompatível | `LocalModelUnavailableException` | Lista + mensagem do catálogo |
| Modelo sem capacidade `autocomplete`/`fim` | `LocalModelUnavailableException` | Lista + motivo |
| Provider explícito indisponível | `AiProviderUnavailableException` | Lista + motivo e alternativas |
| Carga em andamento | — | Indicador "Carregando modelo…"; `Esc` cancela a espera, não a carga |
| Cooldown após falha | `LocalModelUnavailableException` | Lista + tempo restante |
| Contexto excede a janela | `LocalModelContextException` | Reduz orçamento uma vez; persistindo, lista |
| Contexto sensível | — | Lista + "Contexto contém possível segredo" |
| Preempção por chat/teste | `LocalModelPreemptedException` | Descarte silencioso |
| Timeout | — | Mantém prévia parcial válida; sem prévia, lista |
| Erro nativo | `LocalModelUnavailableException` | Lista + mensagem segura; status do modelo atualizado |

Cada linha acima tem um valor de `LocalModelUnavailableReason` (`NoModelConfigured`, `ModelInvalid`,
`CapabilityMissing`, `ProviderUnavailable`, `Cooldown`, `NotLoaded`/`DifferentConfiguration`, `ContextOverflow`,
`RuntimeFailure`), decidido em `LocalAiModelService` e descrito em [DEC-R41-REASONS](decisions.md#dec-r41-reasons):
quem monta o fallback escolhe a mensagem pelo motivo e nunca por inspeção de texto. Durante a janela de recusa
([DEC-R41-COOLDOWN](decisions.md#dec-r41-cooldown)) a exceção traz `RetryAfter`, que é de onde sai o
tempo restante. Contexto sensível continua sendo decisão do provider (privacidade do editor) e não chega ao serviço.

## Tempos

| Limite | Valor inicial | Observação |
| --- | --- | --- |
| Indicador "ainda gerando" | 1 s | Feedback |
| Timeout rígido explícito | 10 s (configurável) | Nunca bloqueia a UI |
| Tokens gerados (explícito) | `generation.autocomplete.maxTokens` do metadata, senão 256 | Hoje: 32 para ambos |
| Tokens gerados (inline) | 24 (avaliar 16/32) | Latência |

Todos provisórios até os benchmarks de [performance.md](performance.md).

**Estado (lote A43, 19/09/2026).** O prazo rígido está implementado e é configurável por
`AutocompleteSettings.AiTimeoutMilliseconds` (1 000–60 000 ms, ausente = 10 000), medido ponta a ponta com
`TimeProvider` injetado; vencido, mantém a prévia parcial válida como candidato
([DEC-A43-TIMEOUT](decisions.md#dec-a43-timeout)). O atraso de 1 s vale para o painel inteiro, e não só para o texto
do indicador: uma geração mais rápida não desenha nada ([DEC-A43-INDICATOR](decisions.md#dec-a43-indicator)).
"Carga em andamento", "contexto excede a janela" (reduz uma vez) e "preempção" também estão implementadas
([DEC-A43-OVERFLOW](decisions.md#dec-a43-overflow), [DEC-A43-PREEMPTION](decisions.md#dec-a43-preemption)). Os tetos
de tokens gerados continuam vindo de `MaximumCompletionTokens`, limitados pelo valor declarado em
`generation.autocomplete.maxTokens` quando disponível. O orçamento editável usa somente dígitos sem separador e valida
contexto, overhead e saída contra a janela efetiva do modelo.

## Desempenho

- **Prompt pré-tokenizado:** o builder entrega IDs e o runtime não re-tokeniza (hoje prefixo/sufixo podem ser codificados duas vezes).
- **Marcadores FIM em cache** por tokenizer carregado.
- **Decodificação incremental** com `TokenizerStream` (Qwen nativo) ou decodificador incremental equivalente no tokenizer .NET do DeepSeek, eliminando a decodificação completa a cada token.
- **Prefix cache (experimento):** reuso do KV cache entre pedidos com `Generator.RewindTo` ([onnx-strategy.md](onnx-strategy.md#reuso-de-prefixo-do-kv-cache)). O layout do prompt coloca blocos estáveis (alvo, schema) no início para maximizar o prefixo comum.
- **Buffers:** `ArrayPool<int>` para IDs; nenhuma cópia de sequência por token.
- **Cache de respostas:** mantido para explícito (chave passa a ser o hash do prompt tokenizado + contrato + modelo, mais barato que serializar o request em JSON).

## Contratos

```csharp
public interface IAiCompletionProvider
{
    IAsyncEnumerable<AiCompletionUpdate> CompleteAsync(CompletionContext context, AiCompletionMode mode, CancellationToken cancellationToken);
}

public enum AiCompletionMode { Explicit, Inline }

public sealed record AiCompletionUpdate(AiCompletionCandidate? Candidate, AiCompletionState State, string? Message);
public enum AiCompletionState { Loading, Generating, Partial, Completed, Unavailable, Cancelled }
```

`AiAutocompleteProvider` atual é desmembrado nesse provider e no Output Processor. `LocalModelAiChatService` continuava usando `ILocalAiModelService` sem mudança de comportamento — **removido em 25/09/2026 pela [ADR-055](../10-decisoes-arquiteturais.md#adr-055--remoção-do-assistente-ia-por-aba-25092026)**.

## Decodificação restrita (pesquisa)

`GeneratorParams.SetGuidance` está presente no assembly 0.15.2 (JSON Schema, regex, Lark via llguidance). Possíveis usos: restringir o próximo nome de campo a uma alternância regex dos nomes do catálogo; garantir JSON/JS balanceado. Requer verificar suporte no build nativo distribuído e o custo por token. Não faz parte do aceite da Fase 4.

## Métricas

`ai_completion.requested/generated/accepted/cancelled`, `ai_completion.latency`, `ai_context_build.duration`, `tokenization.duration`, `inference.ttft`, `inference.duration`, `inference.tokens_per_second`, `inference.prompt_tokens`, `inference.generated_tokens`, `prefix_cache.reused_tokens` ([performance.md](performance.md#instrumentação)).


## Provider explícito e pipeline reutilizável

Esboço IA acima é contrato de geração compartilhada; os quatro adapters implementam ICompletionProvider em [architecture](architecture.md#providers-independentes). AiCompletionProvider usa Interactive/AllowLoad; AiPreemptiveCompletionProvider usa Background/LoadedOnly, flags/prazo próprios. Não duplicar seleção, prompts, limpeza ou tokenizer.

LocalAiModelService hoje converte LocalModelContextException em LocalModelUnavailableException. Antes de implementar retry por contexto, preservar motivo tipado no contrato e testar a fronteira; não detectar por texto de mensagem. Prioridade Interactive não implica preempção entre chat e IA explícita de mesma prioridade: fila FIFO; só Background é preemptado.

Cache de respostas precisa de revisão do modelo/tokenizer/contrato/política e parâmetros de geração, além do prompt final; validade de UI é verificada separadamente. Aceitar saída completa ou parcial exige edição válida contra sufixo/contexto. Sem modelo real, testes com fake não comprovam qualidade nem latência. [A41–A44](execution-plan.md).
