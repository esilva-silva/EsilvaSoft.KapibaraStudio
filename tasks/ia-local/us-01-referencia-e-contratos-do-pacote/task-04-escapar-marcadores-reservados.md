# KS-US01-T04 — Neutralizar marcadores reservados em texto não confiável

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US01 — Fixar a referência e fazer valer os contratos do pacote](README.md). **Requisitos relacionados:** MVP-012, MVP-013. **Entregas oficiais:** F02-E01.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Garantir que mensagem do usuário, anexos e resultados de tool contendo marcadores do template não sejam interpretados como controle.

**Depende de:** nenhuma tarefa anterior desta árvore.

## Ponto de partida

- O formatter aplica `CompletionOutputProcessor.ContainsReservedOrSensitiveText` a usuário e tools; a cobertura de `<tool_call>` e `<think>` precisa ser verificada no código.

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [Tools de agentes](../../../docs/MVP/contratos/tools-agentes.md)

## Execução

- [ ] Verificar o comportamento atual com `<tool_call>`, `</tool_call>`, `<tool_response>`, `<think>`, `</think>` e marcadores de turno do ChatML em cada origem de texto.
- [ ] Corrigir onde o marcador atravessa, preservando o conteúdo legítimo para o usuário e o encoder JSON do contrato.
- [ ] Testar marcador partido entre chunks, em Unicode escapado e dentro de resultado truncado.
- [ ] Confirmar que só a saída do próprio modelo é analisada pelo `LocalAgentToolCallParser`.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Nenhuma origem externa produz `ToolRequested` ou evento de raciocínio; os bytes do contrato para texto comum não mudam.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** cenário com modelo real é manual, `[Explicit]` com `Category("LocalModelIntegration")`, em ambiente identificado; sem modelo configurado, não declarar o cenário executado.
- **Conclusão:** todos os critérios obrigatórios aprovados e vinculados à entrega oficial. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

## Registro de execução

Preencher ao executar; nenhum resultado novo é declarado neste arquivo.

| Campo | Registro |
| --- | --- |
| Snapshot e alterações locais | A registrar |
| Data, responsável, ambiente e versões | A registrar |
| Cenários, comandos, esperado e observado | A registrar |
| Resultado por critério | Pendente |
| Evidência oficial e artefatos sanitizados | A vincular em docs |
| Limitações e bloqueios | A registrar com impacto, recurso faltante e próximo passo |

Não armazenar credenciais, texto de raciocínio ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
