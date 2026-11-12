# KS-US01 — Fixar a referência e fazer valer os contratos do pacote

**Estado de execução:** aberta; criação desta pasta não executa nem homologa nenhuma entrega. **Versão-alvo:** v0.11.0.

**Requisitos relacionados:** MVP-012, MVP-013. **Entregas oficiais:** F02-E01.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## História

**Como** pessoa que usa o Studio com IA local, **quero** que o Studio só execute pacotes cujo contrato ele reconhece, com referência e limites fixados antes de qualquer medição, **para** confiar que o modelo carregado é o avaliado e que conteúdo não confiável não se passa por chamada de tool.

## Contexto verificado

- `LocalModelMetadataReader` lê `agentContract`, mas a disponibilidade do agente não o exige; só `kapilab model validate --strict-kapi` exige contrato conhecido.
- A verificação do SHA-256 de `chat_template.jinja` contra o metadata em tempo de carga não foi confirmada.
- O filtro `CompletionOutputProcessor.ContainsReservedOrSensitiveText` é aplicado a usuário e tools; não foi confirmado se barra `<tool_call>` e `<think>`.
- As provas de paridade de prompt são da GenAI 0.15.2; o checkout usa 0.18.0, o que exige revalidação.
- O código expõe o agente local em Release e permite retenção opcional de raciocínio, sem registro correspondente nas decisões vigentes.

## Critérios de aceite

- [ ] Pacote, hash, licença, runtime, hardware, corpus e limites de qualidade, tempo e memória estão registrados antes das medições.
- [ ] Paridade de prompt, template e tokenização revalidada no runtime atual, ou contrato versionado se os bytes mudaram.
- [ ] Pacote com contrato de agente desconhecido ou template adulterado não entra no caminho do agente, com motivo visível.
- [ ] Marcadores reservados em texto do usuário, anexos e resultados de tool não produzem chamada nem raciocínio forjados.
- [ ] Decisões já tomadas no código estão registradas nas decisões vigentes.

## Fora de escopo

- Mudar autenticação ou permissões de providers externos.
- Habilitar cache de prefixo.

## Dependências

Nenhuma user story anterior desta árvore.

## Tarefas

| Tarefa | Resultado | Depende de |
| --- | --- | --- |
| [KS-US01-T01 — Registrar pacote de referência e limites](task-01-registrar-referencia-e-limites.md) | Referência CPU e limites fixados | KT-US09-T03 (modelos) |
| [KS-US01-T02 — Revalidar paridade de prompt no runtime atual](task-02-revalidar-paridade-de-prompt.md) | Paridade de bytes e tokens no GenAI vigente | KS-US01-T01 |
| [KS-US01-T03 — Aplicar o contrato do agente na carga do pacote](task-03-aplicar-contrato-do-agente-na-carga.md) | Recusa visível de contrato ou template inválido | KS-US01-T02 |
| [KS-US01-T04 — Neutralizar marcadores reservados em texto não confiável](task-04-escapar-marcadores-reservados.md) | Texto externo não forja chamada nem raciocínio | — |
| [KS-US01-T05 — Registrar as decisões já tomadas no código](task-05-registrar-decisoes-vigentes.md) | Decisões vigentes atualizadas | — |

A história só fecha quando todos os critérios de aceite têm evidência proporcional. Evidência sintética e real permanecem separadas; preparação não equivale a aceite.
