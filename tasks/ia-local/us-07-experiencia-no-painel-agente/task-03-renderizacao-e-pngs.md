# KS-US07-T03 — Gerar e inspecionar os PNGs das mudanças

**Estado de execução:** em andamento; PNGs do estado novo gerados e inspecionados em 13/11/2026. **Versão-alvo:** v0.11.0.

**User story:** [KS-US07 — Completar a experiência do agente local no painel](README.md). **Requisitos relacionados:** MVP-011, MVP-013. **Entregas oficiais:** F02-E03, F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Validar visualmente as mudanças do painel pelos testes de renderização.

**Depende de:** [KS-US07-T01 — Completar as preferências de raciocínio](task-01-preferencias-de-raciocinio.md); [KS-US07-T02 — Mostrar estados e espera do agente local](task-02-estados-e-espera.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Design system](../../../docs/MVP/04-design-system.md)

## Execução

- [x] Gerar os PNGs pt-BR claro e escuro dos estados novos pelos testes de renderização.
- [x] Inspecionar os arquivos reais e registrar quais foram vistos.
- [x] Não alterar golden nem asserção para esconder regressão.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

PNGs inspecionados e listados; diferença de golden tem causa visual explicada.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** Headless não homologa desktop nativo nem leitor de tela; isso fica em KS-US10-T04.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `c41de38` no branch `ia-local-us06`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 13/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · Headless, sem modelo real |
| Cenários, comandos, esperado e observado | O teste de UI gera `agent-reasoning-always-on-Light-380.png` e `agent-reasoning-always-on-Dark-380.png` em `ui-evidence` da saída de IntegrationTests (pt-BR, largura 380, escala 1). Os dois arquivos foram abertos e inspecionados: Raciocínio aparece marcado e esmaecido, o seletor de orçamento continua ativo, e o restante do compositor não mudou de posição. Nenhum golden ou asserção existente foi alterado. Build da solução com `-p:UsedAvaloniaProducts=`: 0 avisos, 0 erros. `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.805 aprovados e 20 ignorados; IntegrationTests 1.143 aprovados, 20 ignorados e 2 falhas: `AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess` (bloqueado por variáveis de ambiente do Claude Code herdadas da sessão) e `CancelledNativeDeletionPreservesLocalStateAndAllowsIdempotentRetry(True)`, teste de exclusão de sessão Copilot que falhou uma vez na suíte integral e passou três vezes isolado; causa não diagnosticada. |
| Resultado por critério | Aprovado para o único estado visual novo deste lote, nos dois temas. |
| Evidência oficial e artefatos sanitizados | `tests/EsilvaSoft.KapibaraStudio.IntegrationTests/AgentChatReasoningToggleUiTests.cs`, `LocalAgentProviderToolLoopTests.cs`; [guia](../../../docs/MVP/05-guia-de-uso.md) e [design system](../../../docs/MVP/04-design-system.md) atualizados |
| Limitações e bloqueios | Só a largura 380 em escala 1 foi renderizada; os três tamanhos e escalas do design system não foram percorridos. A dica não aparece em imagem estática. Os PNGs ficam na pasta de saída do teste e não são versionados. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
