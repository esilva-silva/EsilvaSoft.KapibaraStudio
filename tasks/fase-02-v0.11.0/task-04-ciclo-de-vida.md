# F02-T04 — Validar concorrência e ciclo de vida

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**Requisitos relacionados:** MVP-008, MVP-011..013. **Entregas oficiais:** F02-E04.

[Índice da fase](README.md) · [Critérios da fase oficial](../../docs/MVP/fases/fase-02-v0.11.0.md)

## Objetivo e dependências

Validar concorrência e ciclo de vida, completando as lacunas descritas abaixo e reaproveitando implementação e aceites existentes. Não incluir funcionalidades de Melhorias, novos providers ou retomada de superfícies retiradas.

**Depende de:** [F02-T02](../fase-02-v0.11.0/task-02-autocomplete-explicito.md), [F02-T03](../fase-02-v0.11.0/task-03-ciclo-agente.md).

**Gate da fase:** Base e contexto seguros comprovados na fase 01; homologações não relacionadas podem continuar abertas.

## Referências obrigatórias

- [Regras do repositório](../../AGENTS.md)
- [Regras comuns e comandos](../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../docs/MVP/02-requisitos-e-estado.md)
- [Arquitetura e invariantes](../../docs/MVP/03-arquitetura-e-invariantes.md)
- [Design system](../../docs/MVP/04-design-system.md)
- [Validação e evidências](../../docs/MVP/06-validacao-e-evidencias.md)
- [Baseline histórica](../../docs/MVP/09-baseline-de-validacao.md)
- [Contrato de IA local](../../docs/MVP/contratos/ia-local.md)
- [Tools de agentes](../../docs/MVP/contratos/tools-agentes.md)
- [Criação de arquivos](../../docs/MVP/contratos/arquivos-agentes.md)

## Execução

- [ ] Exercitar duas abas/conversas, preempção prevista, cancelamento isolado e chunks tardios; não compartilhar CancellationTokenSource.
- [ ] Validar liberação por inatividade, fechamento e troca de modelo sem dispose concorrente; medir retenção real de recursos.
- [ ] Testar restart/crash, falha de persistência, sessão ilegível, opt-out geral/por conexão e retenção autorizada.
- [ ] Usar proprietário LiteDB único e migrações aditivas; snapshots não contêm resultados, credenciais nem estado de cache.
- [ ] Corrigir defeitos comprovados no escopo, validar o impacto e atualizar os documentos oficiais afetados; não encerrar somente com outro plano quando houver implementação necessária.
- [ ] Registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Operações preservam origem, cancelamento e privacidade; liberação e recuperação reais atendem aos limites fixados, sem encerrar com apenas mocks.

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
