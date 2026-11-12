# KS-US01-T03 — Aplicar o contrato do agente na carga do pacote

**Estado de execução:** em andamento; recusa de contrato desconhecido concluída em 12/11/2026, teste de template alterado no produto pendente. **Versão-alvo:** v0.11.0.

**User story:** [KS-US01 — Fixar a referência e fazer valer os contratos do pacote](README.md). **Requisitos relacionados:** MVP-012, MVP-013. **Entregas oficiais:** F02-E01.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Fazer a disponibilidade do agente depender do contrato declarado e da integridade do template, sem quebrar pacotes anteriores de autocomplete.

**Depende de:** [KS-US01-T02 — Revalidar paridade de prompt no runtime atual](task-02-revalidar-paridade-de-prompt.md)

## Ponto de partida

- `LocalAgentAvailability` exige capacidades `Agent`, `Tools`, `Chat`, metadata `agent` e formato `Qwen3ChatMlTools`.
- `LocalModelMetadataReader` valida `templateSha256` como hexadecimal de 64 caracteres; `Qwen3ModelAdapter` exige `chat_template.jinja` e ids de `<think>` iguais aos do metadata.

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)

## Execução

- [x] Recusar o caminho do agente quando `agentContract` declarado não for conhecido, com código e mensagem visíveis; definir e documentar o tratamento de pacote sem o campo.
- [x] Conferir na carga o SHA-256 de `chat_template.jinja` contra `reasoning.templateSha256` e recusar quando diferir.
- [x] Preservar a regra do contrato: pacotes anteriores continuam carregáveis para autocomplete sem satisfazer o gate estrito.
- [ ] Cobrir com testes de metadata válido, contrato desconhecido, template alterado e pacote legado.
- [x] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Pacote de agente com contrato desconhecido ou template alterado fica indisponível com motivo; pacote legado de autocomplete continua carregando.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Execução parcial registrada abaixo; itens não exercitados continuam abertos.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | `fcc4b72` em `master`, com as alterações deste lote ainda não commitadas |
| Data, responsável, ambiente e versões | 12/11/2026 · Claude Code a pedido do usuário · Windows 11 · .NET SDK 10.0.402 · sem modelo real, sem MongoDB |
| Cenários, comandos, esperado e observado | `agentContract` declarado e desconhecido torna o pacote indisponível como agente com motivo `AgentContractUnsupported` e mensagem própria; campo ausente mantém o pacote utilizável. A conferência do SHA-256 de `chat_template.jinja` já existia em `LocalModelMetadataReader.VerifyTemplate` (lida no código, não alterada). Testes `AnUnknownAgentContractIsNotServedAsAnAgentAndSaysWhy` e `AKnownOrUndeclaredAgentContractKeepsTheAgentAvailable`. Restore `--locked-mode`, build da solução com `-p:UsedAvaloniaProducts=` (0 avisos, 0 erros) e `dotnet test` da solução: Infrastructure.Agents.Tests 403/403; UnitTests 3.794 aprovados e 20 ignorados; IntegrationTests 1.126 aprovados, 20 ignorados e 1 falha (`AttachmentsCountTowardsTheInputLimitAndFailTypedWithoutAnyProcess`, bloqueado por variáveis de ambiente do Claude Code herdadas da sessão; não toca o código alterado). Foco: `--filter FullyQualifiedName~LocalAgentProvider` 38/38. |
| Resultado por critério | Parcial: contrato conhecido, desconhecido e ausente aprovados. |
| Evidência oficial e artefatos sanitizados | `tests/EsilvaSoft.KapibaraStudio.UnitTests/LocalAgentProviderToolLoopTests.cs`; contrato atualizado em [IA local](../../../docs/MVP/contratos/ia-local.md) |
| Limitações e bloqueios | Template alterado só tem teste no gate estrito do KapiLab; falta teste do leitor do produto. A mensagem do motivo novo ainda não aparece traduzida em nenhuma tela específica. |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
