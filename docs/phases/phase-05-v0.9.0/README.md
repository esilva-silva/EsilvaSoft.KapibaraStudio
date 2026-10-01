# Fase 5 — v0.9.0: IA local e produtividade contextual

**Situação:** escopo automatizável implementado e validado em 22/09/2026; assistência com modelo ONNX e homologação nativa continuam experimentais até as evidências da Fase 9.

**Prioridade de entrega (01/10/2026):** esta fase assume o foco atual no lugar da Fase 7 / v0.11.0. Execute as entregas pendentes descritas na [meta de implementação](meta-de-implementacao.md) antes de retomar a Fase 7. Essa prioridade não altera o status experimental da inferência real nem transfere os gates de homologação da Fase 9.

**Meta de implementação:** [plano, tarefas executadas, evidências e critérios de conclusão](meta-de-implementacao.md).

A camada de IA local já participa do catálogo de localização nos quatro idiomas (`pt-BR`, `en`, `es`, `zh-CN`), com fallback em inglês. Isso encerra a meta transversal de tradução, sem alterar o status experimental.

## Objetivo

Assistência técnica local integrada à IDE, mantendo o autocomplete determinístico como base.

## Escopo incluído (IDs do catálogo)

- ADV-09 — modelos ONNX, ghost text aceito por Tab, propostas de chat revisáveis com diff, catálogo multimodelo e seleção de CPU/GPU/NPU.
- EDT-02 (extensão preemptiva) — sugestão inline sem solicitação explícita.

## Fora de escopo

Chatbot genérico, execução automática de sugestões, envio implícito a serviços externos, garantia de otimização sem explain/medição e obrigatoriedade de GPU.

## Antecipações técnicas presentes no código

Existem `LocalModelAiChatService`, `OnnxLocalModelRuntime`, ghost text e propostas com diff. A existência do painel não torna a assistência pronta.

## Critério de aceite

Casos automatizados nos quatro idiomas para cada ação sem alterações extras não solicitadas; Tab/Escape/undo e descarte de resposta obsoleta; funcionamento sem modelo instalado; opt-out impedindo contexto indevido; cancelamento isolado entre chat e autocomplete.

## Dependências

Fase 4 aceita, modelo compatível externo, contexto determinístico, política de dados e avaliações reproduzíveis.

## Documentos relacionados

- [ONNX/chat](../../23-onnx-slopcoder.md) · [IA local multimodelo](../../26-ia-local-multimodelo.md) · [Autocomplete preemptivo](../../auto-complite/preemptive-autocomplete.md) · sub-fase [5](../../auto-complite/phases/phase-5-preemptive.md)

## Validação manual transferida

GPU/NPU, revisão linguística de domínio, fidelidade de todas as ações e medições de latência em hardware real são critérios da [Fase 9 / v0.13.0](../phase-09-v0.13.0/README.md).
