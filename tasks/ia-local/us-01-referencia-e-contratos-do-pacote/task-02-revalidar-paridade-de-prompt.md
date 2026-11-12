# KS-US01-T02 — Revalidar paridade de prompt no runtime atual

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US01 — Fixar a referência e fazer valer os contratos do pacote](README.md). **Requisitos relacionados:** MVP-012, MVP-013. **Entregas oficiais:** F02-E01.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Repetir a prova de paridade do contrato `local-agent-qwen3-v1` e do contexto do editor no runtime fixado pelo checkout.

**Depende de:** [KS-US01-T01 — Registrar pacote de referência e limites](task-01-registrar-referencia-e-limites.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [KapiLab](../../../tools/KapiLab/README.md)

## Execução

- [ ] Rodar `scripts/lab/parity_local_agent_qwen3.py` contra as fixtures de `tools/KapiLab.Tests/Fixtures/local-agent-qwen3-v1`, sem `--generate-fixtures`.
- [ ] Rodar as provas nativas Explicit de `LocalAgentPromptContractTests` com `KAPI_QWEN3_TOKENIZER_PACKAGE` apontando para o pacote de referência.
- [ ] Repetir a paridade de `editor-context-v1` e `editor-context-v2` com `kapilab contract render` e `parity compare`.
- [ ] Se os bytes mudaram, versionar o contrato em vez de regravar fixtures; registrar a transformação de schemas observada no template nativo.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Zero divergências no runtime atual, ou contrato versionado com a diferença documentada; fixtures não são regravadas para passar.

- **Automatizável:** o script de paridade não acessa rede nem pesos e pode rodar sempre.
- **Real/manual:** as provas nativas exigem o tokenizer do pacote e são Explicit.
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
