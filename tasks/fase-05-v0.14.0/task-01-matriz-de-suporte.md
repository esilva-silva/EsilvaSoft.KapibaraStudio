# F05-T01 — Consolidar matriz de suporte e lacunas

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.14.0.

**Requisitos relacionados:** MVP-001..015. **Entregas oficiais:** F05-E01.

[Índice da fase](README.md) · [Critérios da fase oficial](../../docs/MVP/fases/fase-05-v0.14.0.md)

## Objetivo e dependências

Consolidar matriz de suporte e lacunas, completando as lacunas descritas abaixo e reaproveitando implementação e aceites existentes. Não incluir funcionalidades de Melhorias, novos providers ou retomada de superfícies retiradas.

**Depende de:** [F04-T05](../fase-04-v0.13.0/task-05-dados-e-instrucoes.md).

**Gate da fase:** Entregas funcionais e atualização das fases anteriores verificadas nos critérios aplicáveis.

## Referências obrigatórias

- [Regras do repositório](../../AGENTS.md)
- [Regras comuns e comandos](../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../docs/MVP/02-requisitos-e-estado.md)
- [Arquitetura e invariantes](../../docs/MVP/03-arquitetura-e-invariantes.md)
- [Design system](../../docs/MVP/04-design-system.md)
- [Validação e evidências](../../docs/MVP/06-validacao-e-evidencias.md)
- [Baseline histórica](../../docs/MVP/09-baseline-de-validacao.md)
- [Distribuição e suporte](../../docs/MVP/07-distribuicao-e-suporte.md)

## Execução

- [ ] Reunir matrizes já fixadas nas fases anteriores, evitando adiar para esta fase a definição de seus ambientes.
- [ ] Mapear todos MVP-001..015 a SO/RID/instalação/MongoDB/autenticação/topologia/modelo/runtime e cenários aplicáveis.
- [ ] Vincular evidência existente com snapshot/alcance, identificar o que exige reexecução no estado final e registrar ambientes faltantes.
- [ ] Fixar a matriz gráfica Windows/Linux e de acessibilidade; exclusões de suporte exigem justificativa, sem renomear defeito obrigatório como melhoria.
- [ ] Corrigir defeitos comprovados no escopo, validar o impacto e atualizar os documentos oficiais afetados; não encerrar somente com outro plano quando houver implementação necessária.
- [ ] Registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Todos os requisitos têm cobertura e lacunas nominais; cada alegação de suporte aponta combinação que será efetivamente exercitada.

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
