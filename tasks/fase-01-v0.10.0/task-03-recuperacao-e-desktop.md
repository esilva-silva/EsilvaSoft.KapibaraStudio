# F01-T03 — Validar recuperação legada e jornadas nativas

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.10.0.

**Requisitos relacionados:** MVP-001..011. **Entregas oficiais:** F01-E04.

[Índice da fase](README.md) · [Critérios da fase oficial](../../docs/MVP/fases/fase-01-v0.10.0.md)

## Objetivo e dependências

Validar recuperação legada e jornadas nativas, completando as lacunas descritas abaixo e reaproveitando implementação e aceites existentes. Não incluir funcionalidades de Melhorias, novos providers ou retomada de superfícies retiradas.

**Depende de:** [F01-T01](../fase-01-v0.10.0/task-01-reconciliar-cortes.md), [F01-T02](../fase-01-v0.10.0/task-02-jornadas-mongodb.md).

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

- [ ] Reabrir rascunhos Script/Agregação como Texto, preservando texto/origem e sem execução; validar adaptação explícita ao Console e JSON opt-in.
- [ ] Exercitar abrir/salvar scripts, histórico, consultas salvas, workspace, opt-outs e reabertura; provocar arquivo ilegível, falha de gravação e concorrência entre abas.
- [ ] Verificar recuperação de capturas antigas do profiler pelo fluxo humano preservado, sem reintroduzir administração retirada.
- [ ] Executar jornadas afetadas em Windows e Linux gráficos com diálogos, clipboard, teclado e foco; separar prova Headless de prova nativa.
- [ ] Corrigir defeitos comprovados no escopo, validar o impacto e atualizar os documentos oficiais afetados; não encerrar somente com outro plano quando houver implementação necessária.
- [ ] Registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Sessão ilegível permanece recuperável, falhas são visíveis e o proprietário LiteDB continua único; cenários nativos ausentes ficam abertos e encaminhados nominalmente.

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
