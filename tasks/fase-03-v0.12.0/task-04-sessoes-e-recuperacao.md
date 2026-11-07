# F03-T04 — Validar sessões, persistência e recuperação

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.12.0.

**Requisitos relacionados:** MVP-011, MVP-014. **Entregas oficiais:** F03-E04.

[Índice da fase](README.md) · [Critérios da fase oficial](../../docs/MVP/fases/fase-03-v0.12.0.md)

## Objetivo e dependências

Validar sessões, persistência e recuperação, completando as lacunas descritas abaixo e reaproveitando implementação e aceites existentes. Não incluir funcionalidades de Melhorias, novos providers ou retomada de superfícies retiradas.

**Depende de:** [F03-T03](../fase-03-v0.12.0/task-03-conversas-e-streaming.md).

**Gate da fase:** IA local da fase 02 aceita nos critérios aplicáveis; reutilizar registry, sessões, permissões e adapters.

## Referências obrigatórias

- [Regras do repositório](../../AGENTS.md)
- [Regras comuns e comandos](../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../docs/MVP/02-requisitos-e-estado.md)
- [Arquitetura e invariantes](../../docs/MVP/03-arquitetura-e-invariantes.md)
- [Design system](../../docs/MVP/04-design-system.md)
- [Validação e evidências](../../docs/MVP/06-validacao-e-evidencias.md)
- [Baseline histórica](../../docs/MVP/09-baseline-de-validacao.md)
- [Tools de agentes](../../docs/MVP/contratos/tools-agentes.md)
- [Criação de arquivos](../../docs/MVP/contratos/arquivos-agentes.md)
- [Distribuição e suporte](../../docs/MVP/07-distribuicao-e-suporte.md)

## Execução

- [ ] Demonstrar reserva durável antes do envio e impedir envio quando a reserva falha.
- [ ] Provocar restart/crash, sessão perdida/ilegível, lock ocupado, gravação e exclusão falhas; preservar referências para retry.
- [ ] Validar opt-out, KeepHistory e retenção local sem prometer exclusão remota.
- [ ] Confirmar erro visível e recuperação sem segunda conexão LiteDB nem sessão vazia sobre dados ilegíveis.
- [ ] Corrigir defeitos comprovados no escopo, validar o impacto e atualizar os documentos oficiais afetados; não encerrar somente com outro plano quando houver implementação necessária.
- [ ] Registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Falhas de persistência e exclusão têm recuperação demonstrada; privacidade e histórico obedecem ao contrato sob concorrência.

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
