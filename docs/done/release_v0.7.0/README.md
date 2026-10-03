# Release v0.7.0 — autocomplete com IA explícita

**Arquivada em 22/09/2026 por escopo funcional.** A homologação de modelo, hardware, teclado físico, acessibilidade e interface nativa não está concluída; as pendências estão em [pendencias-de-homologacao.md](pendencias-de-homologacao.md) e continuam abertas na [Fase 10 / v0.14.0](../../phases/phase-10-v0.14.0/README.md).

Esta pasta corresponde à [Fase 3](../../phases/phase-03-v0.7.0/README.md) do [roadmap](../../09-plano-de-implementacao.md).

## Objetivo entregue

Uma sugestão de autocomplete por modelo local é solicitada explicitamente com `Ctrl+;`, apresentada como prévia revisável e só modifica o texto quando o usuário a aceita. O autocomplete tradicional continua disponível quando não há modelo utilizável.

## Requisitos concluídos e aceitos

| IDs | Recorte concluído | Evidência |
| --- | --- | --- |
| EDT-02 (extensão) | Pedido explícito captura texto, cursor, destino e contexto antes do `await`; transmite prévias e usa prioridade interativa no runtime compartilhado | `WorkspaceTabViewModel.RequestAiCompletionAsync`, `AiCompletionProvider`; `AiCompletionProviderTests.ExplicitRequestStreamsPreviewsAndEndsWithAProcessedCandidate` e `TheExplicitRequestIsAlwaysInteractive` |
| ADV-09 (recorte de autocomplete) | A proposta é visível, multilinha e revisável; `Tab` a insere como uma única operação de desfazer; não há inserção ou execução automática | `WorkspaceTabView.Autocomplete.Ai`, `AiCompletionPreviewPresenter`; `AiCompletionUiTests.TabAcceptsTheMultilinePreviewAsASingleUndo` |
| EDT-02 (fallback) | Sem provider, modelo, capacidade ou provider de hardware disponível, `Ctrl+;` explica o motivo e abre o autocomplete tradicional sem alterar o documento | `AiCompletionFallbackMessages`; `AiCompletionUiTests.ExplicitAiFallsBackToTheTraditionalListWithTheReason` e `WithoutAProviderTheShortcutStillExplainsItselfThroughTheList` |
| UX-01 (cancelamento e isolamento) | `Esc` cancela somente a geração da aba; nova edição invalida a solicitação; resposta tardia nunca aparece nem é inserida | `WorkspaceTabView.Autocomplete.Ai`; `AiCompletionUiTests.EscapeDuringGenerationCancelsAndRemovesTheIndicator` e `AStaleResultIsNeverShownAfterANewEdit` |
| Privacidade do recorte | Contexto ou texto gerado que parece segredo é recusado antes de chegar ao modelo ou ao candidato | `AiGenerationPipeline`; `AiCompletionProviderTests.SensitiveContextNeverReachesTheModel`, `SensitiveGeneratedTextNeverReachesTheCandidate` e `AiCompletionUiTests.ASensitiveContextFallsBackWithoutEverConsultingTheModel` |
| Robustez de geração | Timeout, estouro de contexto, preempção, carregamento e ausência de streaming produzem resultado tipado ou fallback sem aplicar texto | `AiGenerationPipeline`; `AiCompletionFallbackTests` e `AiCompletionProviderTests.ARuntimeWithoutStreamingStillProducesTheSameCandidate` |

Em 22/09/2026, os filtros automatizados para `AiCompletionUiTests`, `AiCompletionFallbackTests` e `AiCompletionProviderTests` executaram **39 testes aprovados, 0 falhas** em `net10.0`.

## Latência e limites da evidência

O perfil real de 19/09/2026 está em [performance — perfil da IA local](../../auto-complite/performance.md#perfil-da-ia-local--fase-4-ctrl-lote-a44--19092026). A numeração “Fase 4” desse subsistema é independente do roadmap do produto; a evidência mede o mesmo fluxo explícito `Ctrl+;` arquivado nesta release.

Na máquina documentada (Ryzen 9 7900, Radeon RX 7800 XT, Windows 11, .NET 10.0.12), o primeiro texto p95 foi 2.259,0 ms para SlopCoder 0.5B INT4 em CPU e 375,4 ms para SlopCoder 1.5B DML-FP16 em GPU. A medição usa modelos reais já carregados em harness de console; não comprova a latência de quadro da aplicação, a primeira carga nem outros equipamentos.

## Documentos desta release

- [Pendências de homologação](pendencias-de-homologacao.md)
- [Matriz de validação](../../15-matriz-de-validacao.md)
- [Decisões e evidências do subsistema](../../auto-complite/decisions.md)

## O que esta release não afirma

Não afirma homologação em Windows e Linux, execução de ONNX real dentro da interface, acessibilidade por leitor de tela, layout físico de teclado, qualidade linguística de domínio, nem cobertura de todos os modelos e provedores. Ghost text preemptivo, chat, catálogo multimodelo e seleção de CPU/GPU/NPU são escopo da [Fase 8 / v0.12.0](../../phases/phase-08-v0.12.0/README.md).
