# F06-T04 — Concluir documentação e obter aceite

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.15.0.

**Requisitos relacionados:** MVP-001..015. **Entregas oficiais:** F06-E04.

[Índice da fase](README.md) · [Critérios da fase oficial](../../docs/MVP/fases/fase-06-v0.15.0.md)

## Objetivo e dependências

Concluir documentação e obter aceite, completando as lacunas descritas abaixo e reaproveitando implementação e aceites existentes. Não incluir funcionalidades de Melhorias, novos providers ou retomada de superfícies retiradas.

**Depende de:** [F06-T03](../fase-06-v0.15.0/task-03-auditar-pacotes-finais.md).

**Gate da fase:** Fases 01–05 com critérios obrigatórios aplicáveis aceitos e evidências reconciliadas ao candidato final.

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

- [ ] Revisar guia, suporte, segurança, privacidade, recuperação e limitações com base nas evidências finais.
- [ ] Preparar notas de release descrevendo escopo MVP e limites, sem acrescentar funcionalidades de Melhorias.
- [ ] Apresentar recorte e artefatos concretos para aceite explícito do responsável; registrar identidade do responsável, data, decisão e referência.
- [ ] Manter a tarefa aberta enquanto faltar aceite ou houver critério obrigatório pendente; não confundir aprovação deste plano com aprovação do produto.
- [ ] Corrigir defeitos comprovados no escopo, validar o impacto e atualizar os documentos oficiais afetados; não encerrar somente com outro plano quando houver implementação necessária.
- [ ] Registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Documentação corresponde ao comportamento homologado e o aceite explícito do MVP está registrado para os artefatos revisados.

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
