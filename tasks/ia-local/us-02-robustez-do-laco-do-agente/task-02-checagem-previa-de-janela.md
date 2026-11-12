# KS-US02-T02 — Checar a janela de contexto antes de gerar

**Estado de execução:** em andamento; correção e teste do provider concluídos em 12/11/2026, testes de limite exato pendentes. **Versão-alvo:** v0.11.0.

**User story:** [KS-US02 — Tornar o laço do agente local previsível em sucesso e falha](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Detectar antes de cada passo que prompt, orçamento de raciocínio e resposta não cabem na janela do pacote.

**Depende de:** [KS-US02-T01 — Cobrir o ciclo de tools com runtime falso](task-01-testes-do-ciclo-com-runtime-falso.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)

## Execução

- [x] Calcular tokens do prompt renderizado mais orçamento do passo e `maxAnswerTokensPerStep` contra a janela declarada e a recomendada.
- [x] Emitir `LocalContextExhausted` com mensagem acionável antes de iniciar a geração.
- [x] Decidir e documentar o que acontece com o histórico: nada é descartado em silêncio.
- [ ] Testar no limite exato, um token acima e com resultado de tool grande.
- [x] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Nenhuma geração começa quando a soma excede a janela; o erro é visível e o histórico do turno é preservado ou revertido conforme documentado.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `fcc4b72` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 12/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · sem modelo real, sem MongoDB |
| Cenários, comandos, esperado e observado | A checagem de prompt renderizado + `MaximumTokens` (resposta + orçamento do passo) contra a janela já existia em `OnnxLocalModelRuntime.BuildPrompt`, antes do primeiro token. Defeito encontrado e corrigido: o serviço de modelos embrulha a exceção como indisponibilidade `ContextOverflow` e o provider a publicava como `LocalGenerationFailure`; agora publica `LocalContextExhausted`. Teste `ContextThatDoesNotFitIsReportedBeforeAnyTokenAndRollsTheTurnBack`. Restore `--locked-mode`, build da solução com `-p:UsedAvaloniaProducts=` (0 avisos, 0 erros) e `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.794 aprovados e 20 ignorados; IntegrationTests 1.126 aprovados, 20 ignorados e 1 falha (`AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess`, bloqueado por variáveis de ambiente do Claude Code herdadas da sessão; não toca o código alterado). Foco: `--filter FullyQualifiedName~LocalAgentProvider` 38/38. |
| Resultado por critério | Parcial: mapeamento do erro e reversão do histórico aprovados; limite exato, um token acima e resultado de tool grande não exercitados. |
| Evidência oficial e artefatos sanitizados | `tests/EsilvaSoft.KapibaraStudio.UnitTests/LocalAgentProviderToolLoopTests.cs`; contrato atualizado em [IA local](../../../docs/MVP/contratos/ia-local.md) |
| Limitações e bloqueios | Os casos de limite dependem do tokenizer real do pacote e ficam para teste Explicit com modelo (KS-US10-T01). O histórico do turno é revertido por inteiro; nada é descartado em silêncio. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
