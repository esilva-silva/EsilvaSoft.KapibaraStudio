# KS-US05-T02 — Validar recusas, limites e revogação

**Estado de execução:** em andamento; recusas do destino local cobertas em 12/11/2026 na composição real, sem modelo. **Versão-alvo:** v0.11.0.

**User story:** [KS-US05 — Comprovar tools, recusas e privacidade com modelo real](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Comprovar que o agente local não executa operação proibida em nenhuma condição.

**Depende de:** [KS-US05-T01 — Exercitar o ciclo real das tools permitidas](task-01-ciclo-real-de-tools.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Tools de agentes](../../../docs/MVP/contratos/tools-agentes.md)

## Execução

- [x] Pedir consulta e escrita diretas e conferir a recusa; tentar os 13 nomes recusados e conferir `UnknownTool` antes de policy e auditoria.
- [ ] Validar limites de chamadas, de bytes por resultado e de paginação, e a revogação antes e depois da auditoria.
- [ ] Estender `AgentRuntimeDenyBoundaryTests` e os testes de registry para o destino local onde faltar.
- [x] Conferir que aprovação não amplia a fronteira.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Todas as tentativas proibidas são recusadas e auditadas; revogação não permite retry nem publicação tardia.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | Testes em `403061a` (`master`); este registro é alteração posterior |
| Data, responsável, ambiente e versões | 12/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · sem modelo real, sem MongoDB |
| Cenários, comandos, esperado e observado | O `LocalAgentProvider` real roda sobre a composição de produção (runtime, registry, política, auditoria e LiteDB reais), com o modelo substituído por um roteiro e a fonte de metadados por um espião. Os 13 nomes permanentemente recusados, pedidos pelo "modelo" como consulta ou escrita direta: nenhum `ToolRequested`, turno falho, nome ausente do catálogo anunciado, sem descritor nem schema no registry, auditoria vazia e zero leituras de metadados. Chamada de `list_databases` sem grant para a conexão: negada com `PermissionDenied` antes de qualquer leitura, auditada como `PermissionMissing`, e a recusa volta ao modelo sem revelar a conexão. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.802 aprovados e 20 ignorados; IntegrationTests 1.142 aprovados, 20 ignorados e 1 falha (`AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess`, bloqueado por variáveis de ambiente do Claude Code herdadas da sessão). Na rodada anterior da mesma data, `StartupCompositionResumesCrashJournalsInBackgroundOnTheSingleOwner` falhou uma vez na suíte integral, passou três vezes isolado e não se repetiu; a causa não foi diagnosticada. |
| Resultado por critério | Parcial: nomes recusados e ausência de grant aprovados; a aprovação humana nem chega a ser pedida para nome recusado. |
| Evidência oficial e artefatos sanitizados | `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/LocalAgentComposedRuntimeTests.cs` e `tests/EsilvaSoft.KapibaraStudio.TestSupport/LocalAgentScriptedDoubles.cs` |
| Limitações e bloqueios | Não exercitados aqui: limites de bytes e de paginação do registry, revogação antes e depois da auditoria no destino local e a extensão de `AgentRuntimeDenyBoundaryTests` (os testes novos ficaram em IntegrationTests, que faz parte da solução). O pedido de consulta direta em linguagem natural depende do comportamento do modelo real e fica em KS-US05-T01. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
