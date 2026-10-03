# Fase 8 — v0.12.0: IA local e produtividade contextual

**Situação:** escopo automatizável implementado e validado em 22/09/2026; assistência com modelo ONNX e homologação nativa continuam experimentais até as evidências da Fase 10.

**Prioridade de entrega (03/10/2026):** esta fase é o foco atual. Execute as entregas pendentes da [meta de implementação](meta-de-implementacao.md) antes de retomar a Fase 5 / v0.9.0. O recorte automatizável foi aceito em 22/09/2026; a composição evoluiu depois (ADRs 055/056), sem restaurar o assistente por aba removido. Inferência ONNX real e homologação nativa continuam experimentais na Fase 10.

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

Existem `OnnxLocalModelRuntime`, catálogo multimodelo, ghost text e `LocalAgentProvider`. `LocalModelAiChatService` e o assistente por aba foram removidos em 25/09/2026 (ADR-055); a composição atual usa os contratos do Agente IA (ADR-056). O aceite automatizado original está na meta e deve ser conciliado com essa evolução antes de novas entregas. A presença desses caminhos não comprova fidelidade de modelo ou homologação nativa.

## Critério de aceite

Casos automatizados nos quatro idiomas para cada ação sem alterações extras não solicitadas; Tab/Escape/undo e descarte de resposta obsoleta; funcionamento sem modelo instalado; opt-out impedindo contexto indevido; cancelamento isolado entre chat e autocomplete.

## Dependências

Fase 4 aceita, modelo compatível externo, contexto determinístico, política de dados e avaliações reproduzíveis.

## Documentos relacionados

- [ONNX/chat](../../23-onnx-slopcoder.md) · [IA local multimodelo](../../26-ia-local-multimodelo.md) · [Autocomplete preemptivo](../../auto-complite/preemptive-autocomplete.md) · sub-fase [5](../../auto-complite/phases/phase-5-preemptive.md)

## Validação manual transferida

GPU/NPU, revisão linguística de domínio, fidelidade de todas as ações e medições de latência em hardware real são critérios da [Fase 10 / v0.14.0](../phase-10-v0.14.0/README.md).
