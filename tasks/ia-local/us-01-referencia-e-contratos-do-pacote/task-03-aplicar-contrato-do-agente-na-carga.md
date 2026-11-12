# KS-US01-T03 — Aplicar o contrato do agente na carga do pacote

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

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

- [ ] Recusar o caminho do agente quando `agentContract` declarado não for conhecido, com código e mensagem visíveis; definir e documentar o tratamento de pacote sem o campo.
- [ ] Conferir na carga o SHA-256 de `chat_template.jinja` contra `reasoning.templateSha256` e recusar quando diferir.
- [ ] Preservar a regra do contrato: pacotes anteriores continuam carregáveis para autocomplete sem satisfazer o gate estrito.
- [ ] Cobrir com testes de metadata válido, contrato desconhecido, template alterado e pacote legado.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Pacote de agente com contrato desconhecido ou template alterado fica indisponível com motivo; pacote legado de autocomplete continua carregando.

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
