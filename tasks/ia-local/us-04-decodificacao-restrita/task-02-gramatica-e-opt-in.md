# KS-US04-T02 — Gerar a gramática e ativá-la por opt-in do pacote

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US04 — Restringir a saída de tool call quando o runtime permitir](README.md). **Requisitos relacionados:** MVP-013. **Entregas oficiais:** F02-E03.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Se o smoke aprovar, gerar a gramática de tool call a partir das tools do plano e ativá-la apenas quando o pacote declarar.

**Depende de:** [KS-US04-T01 — Executar o smoke de guidance e decidir](task-01-smoke-de-guidance.md); [KS-US02-T05 — Alinhar ao contrato tool fora do plano e envelope inválido](../us-02-robustez-do-laco-do-agente/task-05-tool-fora-do-plano-e-envelope.md)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)
- [Tools de agentes](../../../docs/MVP/contratos/tools-agentes.md)

## Execução

- [ ] Gerar a gramática dos schemas do registry para as tools do plano do turno, com no máximo uma chamada por mensagem.
- [ ] Ativar somente com `constrainedDecoding` declarado e runtime compatível; raciocínio e resposta em texto ficam livres.
- [ ] Manter o parser e o retry atuais como verificação independente.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Com opt-in, envelope inválido não ocorre nos testes; sem opt-in, o comportamento é o atual.

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
