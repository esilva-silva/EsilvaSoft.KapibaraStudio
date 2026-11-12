# KS-US10-T02 — Estender o `kapilab agent run`

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US10 — Avaliar com modelo real e consolidar as evidências](README.md). **Requisitos relacionados:** MVP-008, MVP-011..013. **Entregas oficiais:** F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Dar ao KapiLab o que falta para medir o agente de forma repetível.

**Depende de:** [KS-US02-T03 — Preencher semente e amostragem do pacote](../us-02-robustez-do-laco-do-agente/task-03-semente-e-amostragem.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [KapiLab](../../../tools/KapiLab/README.md)

## Execução

- [ ] Adicionar execução com várias sementes e alternância e orçamento de raciocínio.
- [ ] Adicionar o benchmark do agente com tempo até a primeira resposta, tokens de raciocínio, estouro de orçamento e tool inventada.
- [ ] Manter o relatório versionado e apenas com hashes de conteúdo.
- [ ] Cobrir com testes do KapiLab; benchmark exige opt-in local e não roda em CI.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Comandos documentados no README do KapiLab e testados; o repositório de modelos consegue usá-los em KT-US06-T03.

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
