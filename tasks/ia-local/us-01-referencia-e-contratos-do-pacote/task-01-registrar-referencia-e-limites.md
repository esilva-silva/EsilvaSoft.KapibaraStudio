# KS-US01-T01 — Registrar pacote de referência e limites

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US01 — Fixar a referência e fazer valer os contratos do pacote](README.md). **Requisitos relacionados:** MVP-012, MVP-013. **Entregas oficiais:** F02-E01.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Registrar a referência de CPU da fase 02 para autocomplete e agente, antes de qualquer avaliação.

**Depende de:** KT-US09-T03 — Entregar a nota de referência ao Studio (repositório de modelos `KapibaraStudio.MongoIA`, `tasks/us-09-publicacao-e-entrega/task-03-nota-de-entrega-ao-studio.md`)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [Baseline histórica](../../../docs/MVP/09-baseline-de-validacao.md)

## Execução

- [ ] Registrar, por papel (autocomplete e agente), pacote, variante, versão, SHA-256, licença, contrato de contexto e de agente, a partir da nota de entrega do repositório de modelos.
- [ ] Registrar runtime efetivo (versão em `Directory.Packages.props` e locks), hardware, memória e corpus versionado.
- [ ] Fixar os limites de qualidade, sucesso de tarefa, TTFT, tempo até a primeira resposta do agente, RAM e liberação, com amostra, aquecimento e método.
- [ ] Enquanto o pacote treinado não chega, registrar o pacote candidato como não qualificado e limitar seu uso a provas funcionais.
- [ ] Manter cache de prefixo `Disabled`.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Configuração, corpus e limites estão registrados com data anterior às medições; ausência de modelo ou hardware bloqueia os ensaios reais sem números inventados.

- **Automatizável:** trabalho documental: `node scripts/check-docs-reader.cjs` e `git diff --check`; não alegar execução das suítes do produto.
- **Real/manual:** não há execução real nesta tarefa; ela condiciona as medições de KS-US10.
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
