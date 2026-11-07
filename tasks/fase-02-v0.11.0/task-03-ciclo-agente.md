# F02-T03 — Concluir ciclo do agente local

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**Requisitos relacionados:** MVP-008, MVP-011..013. **Entregas oficiais:** F02-E03.

[Índice da fase](README.md) · [Critérios da fase oficial](../../docs/MVP/fases/fase-02-v0.11.0.md)

## Objetivo e dependências

Concluir ciclo do agente local, completando as lacunas descritas abaixo e reaproveitando implementação e aceites existentes. Não incluir funcionalidades de Melhorias, novos providers ou retomada de superfícies retiradas.

**Depende de:** [F02-T01](../fase-02-v0.11.0/task-01-referencia-cpu.md), [F02-T02](../fase-02-v0.11.0/task-02-autocomplete-explicito.md).

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

- [ ] Completar envio humano, streaming, chamada de tool, retorno e conclusão com runtime/registry/permissões existentes.
- [ ] Exercitar metadados/índices comuns e Atlas Search aplicáveis; documentos, erros e logs apenas de execuções humanas já capturadas.
- [ ] Validar tool inválida, envelope inválido, recusa, limites, auditoria, revogação e confirmação conforme contrato; não ampliar catálogo para satisfazer fixtures antigas.
- [ ] Demonstrar propostas e criação de arquivo somente pelos fluxos autorizados, preservando conflito/Undo e publicação exclusiva.
- [ ] Corrigir defeitos comprovados no escopo, validar o impacto e atualizar os documentos oficiais afetados; não encerrar somente com outro plano quando houver implementação necessária.
- [ ] Registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Ciclo permitido completa com modelo real e operações proibidas são recusadas mesmo com aprovação; evidência sintética e real permanece separada.

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
