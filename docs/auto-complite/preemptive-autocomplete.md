# Autocomplete preemptivo (inline)

Revisão de 22/09/2026. **5.1 (Traditional Preemptive), 5.2 (AI Preemptive) e 5.3 (estratégia híbrida) implementados no escopo automatizado**. Preemptivo descreve o disparo automático, não a tecnologia de geração. O gate de edição→ghost p95 ≤ 20 ms passou em Headless; modelo ONNX real e homologação nativa permanecem na Fase 10.

**Ordem de fonte efetiva do ghost, implementada em 5.1:** determinístico (`TraditionalPreemptiveCompletionProvider`) → dicionário lexical local → IA. A IA automática só entra com `InlineUseAi = true` **e** modelo já em `Ready` (LoadedOnly); nenhum caminho de digitação carrega, troca ou inicializa modelo. Detalhe e evidência em [phase-5-preemptive](phases/phase-5-preemptive.md#51-traditional-preemptive-completion--concluído-em-18092026-com-pendências-abertas).

```text
Preemptive Completion
├── Traditional / Contextual — sem modelo
└── AI — ONNX local opcional
```

## Providers e infraestrutura compartilhada

TraditionalPreemptiveCompletionProvider usa CompletionService e CompletionRanker da lista explícita. AiPreemptiveCompletionProvider usa o pipeline de fatos/prompt/output de AiCompletionProvider. Só políticas de geração/disparo/apresentação variam. [Contratos](architecture.md#providers-independentes).

Um InlineCompletionCoordinator por editor possui a sugestão automática. Um presenter atende todos os ghosts, inclusive IA explícita; não criar parser/ranker/renderizador por provider.

## 5.1 Traditional Preemptive Completion — implementado em 18/09/2026

```text
Edição → snapshot/cursor → Context Engine → Knowledge Catalog (Peek)
      → CompletionService → Ranking → confiança → Inline Suggestion
```

Implementado por `InlineCompletionCoordinator`, `TraditionalPreemptiveCompletionProvider`, `InlineCompletionConfidence`, `InlineCompletionPolicy` e `InlineCompletionEditorState`. Computação p95 ≤ 5 ms medida e atendida; edição → ghost p95 ≤ 20 ms medida e atendida em quatro execuções Headless (3,85–8,42 ms). Máximos isolados de 22–25 ms não alteram o gate p95. A medição não comprova latência nativa; ver [performance](performance.md) e [phase-5-preemptive](phases/phase-5-preemptive.md).

Fontes determinísticas: campos/tipos, operadores, métodos, collections, databases, stages, snippets, estruturas de filtros/updates. Sem modelo, rede, amostragem ou contexto IA. Cache ausente não dispara carga pelo automático: usar o disponível; enriquecimento vem do Explorer, comando explícito ou refresh autorizado.

Exemplo (barra é cursor): `db.Projects.find({ "Customer.I|": 1 })`. Com Id forte, mostrar `d`; aceitar produz `"Customer.Id"`. Já `db.Projects.find({ Customer.| })` exige substituir texto anterior para obter `"Customer.Id"`. Primeiro ghost é só inserção no cursor: abster-se nesse caso; Ctrl+. oferece a correção completa. Prévia de substituição é evolução visual com teste próprio.

Snippets podem gerar continuação literal inequívoca; placeholders que exigem escolha pertencem à lista. Não inventar valores/UUIDs nem reproduzir literais do histórico automaticamente. Histórico de aceites de símbolos, em memória, pode contribuir para ranking; modelos de statements com valores ficam adiados.

### Confiança e custo

- Mesmo ranking do explícito; score ponderado **não é probabilidade**.
- Filtrar dialeto/shape/escopo/faixas antes de pontuar; exigir prefixo estrito e alvo resolvido para campos.
- Hipótese inicial: score normalizado ≥ 0,85 e margem top-1/top-2 ≥ 0,20, a calibrar por categoria com validação separada. Sem top-2 por truncamento não significa candidato único.
- Complete no catálogo significa frescor, não schema exaustivo. Exigir também SearchExhausted, evidência não truncada e confiança de contexto. Índice não prova presença em todo documento.
- Até 200 candidatos, sem substring/fuzzy no automático; top-2 no conjunto elegível e cancelamento entre lotes.
- Cache curto por editor com chave completa do contexto, não só prefixo; invalidar por revisão de schema/configuração/alvo.
- Coalescer eventos da mesma edição; um worker e uma pendência substituível por editor. Sem debounce artificial se contexto já está pronto. Metas provisórias: computação p95 ≤ 5 ms; edição → ghost p95 ≤ 20 ms.
- Ao exceder orçamento ou haver ambiguidade, não exibir; nunca esperar na UI.

## 5.2 AI Preemptive Completion

```text
Context Engine → Relevant Context Selector → AI Context Builder
              → ILocalAiModelService (LoadedOnly, Background)
              → ONNX → Output Processor → Inline Suggestion
```

Usar principalmente sem continuação determinística forte, por exemplo expressão após `$match:`. Contexto: código próximo ao cursor, operação, coleção, campos/schema e metadata relevante. Statements anteriores só com opções de contexto/histórico habilitadas e privacidade aplicada. Valores de resultados/amostras nunca entram.

- Flags independentes; funciona com tradicional preemptivo desligado, sem chamar esse gerador.
- Debounce inicial: DelayMilliseconds (150 ms). Adaptativo só após medir: clamp(1,5 × mediana dos últimos 20 intervalos, 50, DelayMilliseconds), respeitando toda a faixa salva 50–2000.
- Modelo correto carregado, FIM/contrato compatível, sem ação interativa pendente e perfil de latência elegível. Verificar **dentro da fila**, não por status antes de await.
- LoadedOnly não inicia carga, troca, download, warmup ou fallback que inicialize outra sessão. Falha/troca descarta; ação explícita pode recuperar pelo caminho normal.
- Sem perfil medido, automático inibido até uso explícito fornecer pelo menos 20 medições válidas (limiar inicial). Suspender após duas janelas fora do orçamento; reabilitar só por medidas explícitas dentro dele, sem inferência periódica de sondagem.
- Saída: min(MaximumCompletionTokens, limite do pacote, 24). Contexto respeita janela do modelo e reserva de saída. Prazo total provisório: 600 ms da última edição ao candidato validado, incluindo debounce, fila, tokenização, prefill, decode e despacho.
- Streaming interno possível; automático publica candidato validado estável, não cada token. Timeout cancela e descarta.

## 5.3 Hybrid Preemptive Strategy

### Comparação e decisão

| Estratégia | Latência / custo | Complexidade / estabilidade | Decisão |
| --- | --- | --- | --- |
| Tradicional, depois IA se insuficiente | Resposta rápida; evita inferência; IA recebe custo limitado da tentativa tradicional | Um dono, uma publicação por pedido | **Padrão** |
| Tradicional visível enquanto IA trabalha | Pode estender cedo, mas gasta recursos mesmo com candidato bom | Disputa de âncora, flicker, confiança não comparável | Experimento desligado |
| IA sempre primeiro | Prefill/decode em toda oportunidade | Dependência de modelo e energia | Rejeitada |

Decisão de engenharia baseada em APIs/custos, não experimento de UX já executado. [Pesquisa](research.md#revisão-técnica-de-15092026).

### Fluxo

```text
Edição elegível
  → tradicional habilitado? gerar/rankear em worker
      → alta confiança? publicar e encerrar pedido
      → sem confiança? aguardar debounce restante
  → IA habilitada e LoadedOnly elegível? gerar/validar
      → pedido ainda atual? publicar
      → senão descartar
```

Padrão não troca ghost tradicional visível por IA, nem abre lista como fallback automático. Ctrl+; é outra ação explícita e pode substituí-lo. Ambos preemptivos desligados não desabilitam os comandos explícitos.

Experimento futuro de extensão: só após 5.1/5.2, no máximo uma extensão, mesmo prefixo/faixa, sem aceite parcial em curso, sem diagnóstico novo e dentro do prazo. Comparar aceite/reversão, trocas visuais, CPU/energia, inferências evitáveis e latência; não comparar numericamente score IA com tradicional. Promoção exige relatório de uso real.

## Gatilhos e inibições

Só edição elegível: ponto em receptor, prefixo de campo, abertura de objeto, dois-pontos, vírgula, parêntese e nova linha são categorias a avaliar. Cursor sem edição, seleção, perda de foco, IME, lista/snippet ativos, comentário/regex/número, sufixo conflitante e contexto desconhecido suspendem. Lookup dinâmico não herda última coleção.

**IME — integração automatizada concluída; homologação nativa pendente.** O editor envolve o cliente de texto do Avalonia, publica início/atualização/fim de preedit e limpa o estado ao perder foco/desanexar; testes Headless verificam que a composição suprime o ghost. A digitação IME em Windows/Linux nativos permanece pendente da Fase 10.

Esc suprime a âncora atual; refresh/resposta tardia não ressuscita ghost. Nova edição pode criar pedido. Trocar aba/destino/modo/preferências invalida tudo.

## Concorrência e typeahead

Providers automáticos usam geração comum do coordinator, com token filho por execução; CTS nunca atravessa abas. Publicação e aceite conferem documento/cursor/seleção/alvo/modo, revisões de catálogo/configuração/modelo e sessão anexada. Request A termina após B: descartar A mesmo que ignore cancelamento.

Typeahead só reancora candidato **concluído** se a única edição inserir exatamente seu prefixo na âncora e demais revisões coincidirem. Inferência antiga em voo cancela/descarta mesmo com typeahead compatível. Backspace invalida inicialmente; restauração fica adiada até haver histórico de âncoras validado.

## Renderização

Prototipar presenter comum na Fase 2, evitando dependência circular com IA explícita. Integrar tradicional no 5.1 e IA no 5.2. Remover overlay só após paridade de caret, rolagem, quebra, zoom, seleção, cópia, undo, IME e acessibilidade. [Editor](editor-integration.md#ghost-text).

## Aceitação

Tab via IncrementalCompletion; Esc descarta; Enter preserva nova linha e invalida. Uma aceitação é uma unidade de undo, nunca execução. Syntax.GhostText e recursos existentes; sem novos atalhos opcionais nesta entrega.

## Configuração, métricas e migração

[Defaults e migração](configuration.md). Métricas locais por origem: pedidos, exibições, aceites, obsoletos, timeout, inferências evitadas, latência total, reversão; sem texto/nomes de usuário. [Testes](testing.md), [tarefas](execution-plan.md).

Manter fluxo antigo até paridade de cada modo. 5.1 deve passar sem modelo/IA; 5.2 deve funcionar com tradicional preemptivo desligado quando elegível. No máximo um fluxo automático ligado por editor durante migração.
