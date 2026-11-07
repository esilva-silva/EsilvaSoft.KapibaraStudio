# Task-R1 — evidências L4 / REM-007

Windows, 06/11/2026, checkout de origem `8361f73518b0ed2a0c931ea4348d557daf863786` com lotes L1–L3 locais. Este registro distingue mudança e validação; não representa homologação de modelos reais.

## Corte e preservações

`AutocompleteService` recebe diretamente `ILocalAiModelService` para configuração, status e teste explícito de modelo. Seu caminho de completar é determinístico: respeita `Enabled`, `UseDictionary`, a origem Dictionary e cancelamento; não pede geração nem carrega modelo. Foram retirados geração automática, fallback de IA, cache de respostas SHA256/TTL, revisão/relógio e registro DI exclusivo de `AiAutocompleteProvider`.

`CompletionSourcePolicy.Ai` e `MayLoadModel` continuam como compatibilidade de chamadores existentes, explicitamente ignorados pelo serviço do editor. Preferências versão 1, flags antigas, caminhos, orçamento, hardware, timeout, agente e IA explícita permanecem legíveis e serializáveis; nenhuma migração destrutiva ou segundo proprietário de modelo foi introduzido.

`AiAutocompleteProvider` permanece para avaliação explícita no KapiLab (`ModelRunCommand`, `AutocompleteBenchCommand`) e testes do contrato de prompt, limpeza, privacidade, FIM e fila compartilhada. Não está registrado na composição Desktop. `AiCompletionProvider`, `AiGenerationPipeline`, prioridade/preempção, cancelamento, configuração/model test e agente local permanecem. Comentários foram atualizados para não prometer ghost IA automático.

## Testes e substituições

Cache automático deixou de ter comportamento a testar. Novas provas fazem pedidos repetidos com `CompletionSourcePolicy.All` e modelo já carregado, além de política legada IA em modelo frio: nenhuma geração/carga adicional ocorre. Dicionário desativado não aciona fallback IA. Cancelamento continua observado.

Testes de privacidade, limites, suffix echo, serialização, cancelamento e preempção foram migrados para chamadas diretas ao provider/serviço compartilhado, preservando seu comportamento necessário ao laboratório e IA explícita. Composições de agente e configuração usam o serviço de modelo diretamente. O teste de relatório percorre `AutocompleteService.ConfigureAsync`/`RunModelTestAsync` e comprova status compartilhado e métricas.

O teste manual exclusivo `RealQwenCompletionTravelsThroughEditorAndAcceptsTab`, que esperava ghost IA ao digitar, foi retirado com o fluxo. Testes reais Qwen do runtime permanecem `Explicit`/`LocalModelIntegration`; não foram executados. Testes UI determinísticos e de IA explícita permanecem.

## Resultados

- Build solução: aprovado, zero avisos/erros, `dotnet build EsilvaSoft.KapibaraStudio.slnx --no-restore -p:UsedAvaloniaProducts= -p:UseSharedCompilation=false /nr:false /m:1`. Avalonia evita apenas a tarefa externa; analisadores permanecem ativos.
- UnitTests focados: 109/109 aprovados; `tests/EsilvaSoft.KapibaraStudio.UnitTests/TestResults/task-r1-l4-sol.trx`. Inclui autocomplete, reliability, predictive, DeepSeek, modelo/preempção, agente local, Inline UI e provider explícito.
- Relatório/configuração/status pelo serviço desacoplado: 1/1 aprovado após última mudança; `UnitTests/TestResults/task-r1-l4-model-sol.trx`.
- IntegrationTests focados: 28/28 aprovados; `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/TestResults/task-r1-l4-sol.trx`. Inclui ghost, Ctrl+Espaço, UI explícita, prévia/aceite/descarte/Undo, preferências, roundtrip e guarda `RealModelHubAccessIsConfinedToExplicitLocalModelTests`.
- KapiLab fora da solução: restore `--locked-mode` aprovado; build `--no-restore -p:UseSharedCompilation=false /nr:false /m:1` aprovado, zero avisos/erros. Somente compilação, sem comando de benchmark, geração ou modelo.

Nenhuma superfície visual foi alterada em L4. Homologação com modelo CPU real, Linux nativo, foco/IME/teclados físicos e acessibilidade continua pendente. Suite completa final pertence ao fechamento L5; os resultados focados não substituem essa execução.
