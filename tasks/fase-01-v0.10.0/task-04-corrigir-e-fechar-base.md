# F01-T04 — Corrigir regressões e fechar a base

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.10.0.

**Requisitos relacionados:** MVP-001..011. **Entregas oficiais:** F01-E04.

[Índice da fase](README.md) · [Critérios da fase oficial](../../docs/MVP/fases/fase-01-v0.10.0.md)

## Objetivo e dependências

Corrigir regressões e fechar a base, completando as lacunas descritas abaixo e reaproveitando implementação e aceites existentes. Não incluir funcionalidades de Melhorias, novos providers ou retomada de superfícies retiradas.

**Depende de:** [F01-T01](../fase-01-v0.10.0/task-01-reconciliar-cortes.md), [F01-T02](../fase-01-v0.10.0/task-02-jornadas-mongodb.md), [F01-T03](../fase-01-v0.10.0/task-03-recuperacao-e-desktop.md).

**Gate da fase:** Base funcional existente e cortes registrados pela Task-R 1; não repetir a limpeza.

## Referências obrigatórias

- [Regras do repositório](../../AGENTS.md)
- [Regras comuns e comandos](../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../docs/MVP/02-requisitos-e-estado.md)
- [Arquitetura e invariantes](../../docs/MVP/03-arquitetura-e-invariantes.md)
- [Design system](../../docs/MVP/04-design-system.md)
- [Validação e evidências](../../docs/MVP/06-validacao-e-evidencias.md)
- [Baseline histórica](../../docs/MVP/09-baseline-de-validacao.md)
- [Inventário de cortes](../../docs/Remover/inventario-tecnico.md)
- [Evidências da Task-R 1](../../docs/Remover/TODO.md)

## Execução

- [ ] Corrigir apenas defeitos comprovados pelas tarefas da fase, incluindo polimento do estado técnico de recuperação quando aplicável; preservar providers e contratos compartilhados.
- [ ] Adicionar regressões significativas e repetir cenários afetados; alterações visuais exigem PNGs reais inspecionados conforme design system.
- [ ] Atualizar catálogo, guia e documentos afetados com evidência final; associar cada critério de F01-E04 ao resultado, sem encerrar pendência real por transferência para F05.
- [ ] Corrigir defeitos comprovados no escopo, validar o impacto e atualizar os documentos oficiais afetados; não encerrar somente com outro plano quando houver implementação necessária.
- [ ] Registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

F01-E04 só fecha com os critérios obrigatórios afetados aprovados; a preparação de IA pode avançar com base/contexto seguros comprovados, sem declarar toda a fase concluída.

- **Automatizável:** executar testes relevantes de comportamento/falha quando houver alteração de código e os checks das regras comuns. Para trabalho somente documental, verificar links e diff; não alegar testes do produto.
- **Real/nativo:** executar os cenários reais especificados nesta tarefa com ambiente identificado; separar MongoDB/modelo/runtime oficial de mocks e desktop nativo de Headless. Quando a tarefa apenas prepara uma matriz ou reconcilia evidências, não apresentar essa preparação como homologação.
- **Conclusão:** todos os critérios obrigatórios desta tarefa devem estar aprovados e vinculados às entregas oficiais. Critério inaplicável exige justificativa; falho, ignorado ou não exercitado permanece aberto.

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

Não armazenar credenciais ou payloads sensíveis. Ao concluir, atualizar o estado de execução acima e os checklists com links para a evidência oficial; o estado dos requisitos continua no catálogo MVP.
