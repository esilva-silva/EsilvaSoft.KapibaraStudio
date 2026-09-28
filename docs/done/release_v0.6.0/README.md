# Release v0.6.0 — organização dos projetos e autocomplete básico

**Arquivada em 22/09/2026 por escopo funcional.** A homologação manual, a validação multiplataforma e os gates externos permanecem abertos na [Fase 9 / v0.13.0](../../phases/phase-09-v0.13.0/README.md). Este arquivamento registra o recorte implementado e revisado; não afirma publicação remota nem encerramento dos requisitos amplos do catálogo.

Esta pasta corresponde à [Fase 2](../../phases/phase-02-v0.6.0/README.md) do [roadmap](../../09-plano-de-implementacao.md). A [Fase 1 / v0.5.0](../release_v0.5.0/README.md) permanece preservada.

## Objetivo entregue

Separação física dos núcleos de autocomplete e IA local e entrega do autocomplete determinístico contextual, sem depender de modelo ou de consulta ao servidor durante a digitação.

## Requisitos concluídos e aceitos no recorte

| Área | Recorte concluído | Evidência |
| --- | --- | --- |
| Separação de núcleos | `EsilvaSoft.KapibaraStudio.Autocomplete.Core`, `EsilvaSoft.KapibaraStudio.LocalAi.Core` e `EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi` isolados de `Core`, `Application`, `Infrastructure` e `Desktop`; composition root atualizado, sem ciclo nos nove projetos | [ADR-040](../../10-decisoes-arquiteturais.md); [inventário](../../24-inventario-roadmap.md); `AutocompleteArchitectureTests`; build da solução sem avisos/erros |
| Parser e contexto | Snapshot UTF-16, lexer compartilhado, parser tolerante, cache de tokens, contexto por cursor/escopo/dialeto/gatilho, pipeline, `$lookup`, `$facet`, tipos BSON e snippets | [estado da subfase 2](../../auto-complite/phases/phase-2-traditional-autocomplete.md); testes de parser/contexto; equivalência diferencial nas quatro sementes, 40 edições e oito carets |
| Provider e ranking | Catálogo determinístico, namespaces, fontes e quotas, filtros por shape/tipo, ranking top-K, snippets LSP e sinal de uso | `CompletionService`, `CompletionRanker`, `CompletionUsageTracker`; corpus de 675 fixtures mais gate agregado aprovados; MRR 1,000, top-1 44/44 e top-5 44/44 |
| Integração do editor | Lista contextual com `Ctrl+Espaço`, seleção, refiltro, `↑`/`↓`/`Enter`/`Tab`/`Esc`, snippets e descarte de respostas obsoletas | `CompletionWindowPresenter`, `EditorCommandDispatcher`; testes Headless/integração; W0 com 1.170 aprovados e 0 falhas |
| Isolamento operacional | Digitação não agenda carga, resultados são vinculados à aba/identidade de contexto e o caminho básico funciona sem modelo ou MongoDB | testes de integração, cancelamento, arquitetura e catálogo; regras registradas em [21 — autocomplete](../../21-autocomplete-local.md) |

O fechamento integrado da meta v0.8.0 confirmou a suíte completa com **2.674 aprovados, 0 falhas e 20 ignorados**, além de 43 benchmarks aprovados. A evidência específica desta fase permanece no gate W0 de 1.170 aprovados e no corpus de 675 fixtures mais um gate agregado.

## Limites e pendências aceitas

O aceite desta release cobre o recorte funcional automatizado. O requisito amplo `EDT-02` continua marcado como em desenvolvimento no [catálogo](../../03-catalogo-funcional.md), porque ainda faltam evidências ou cobertura para gates que não foram encerrados:

- alocação com campos de metadata acima do limite medido (174,72 KB a partir de aproximadamente 1.000 campos);
- job completo de latência p95/p99 da Fase 2;
- evidência independente para `TypeMismatchPenalty` e para a regra Elo `Stage → GroupBody`;
- highlighting de 64 KiB acima do orçamento registrado;
- matriz visual completa, layouts físicos/IME, Linux, leitor de tela, diálogos nativos e MongoDB real.

Essas lacunas não foram escondidas nem convertidas em aprovação textual. O registro técnico da revisão intermediária em [phase-2-review](../../auto-complite/phase-2-review.md) permanece histórico; o estado posterior de fechamento do recorte está em [phase-2-traditional-autocomplete](../../auto-complite/phases/phase-2-traditional-autocomplete.md).

## Documentos desta release

- [Pendências de homologação](pendencias-de-homologacao.md)
- [Fase 2 do autocomplete tradicional](../../auto-complite/phases/phase-2-traditional-autocomplete.md)
- [ADR-040 — separação dos núcleos](../../10-decisoes-arquiteturais.md)
- [Matriz de validação](../../15-matriz-de-validacao.md)
- [Acompanhamento da implementação](../../12-acompanhamento-da-implementacao.md)

## O que esta release não afirma

Não afirma conclusão do catálogo amplo, cumprimento dos gates de performance pendentes, homologação em Windows/Linux, operação com MongoDB real, acessibilidade nativa, nem aceite de IA explícita, ghost text preemptivo, administração ou agregação.
