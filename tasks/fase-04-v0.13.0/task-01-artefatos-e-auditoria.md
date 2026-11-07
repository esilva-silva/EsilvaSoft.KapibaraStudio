# F04-T01 — Preparar e auditar artefatos por RID

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.13.0.

**Requisitos relacionados:** MVP-011, MVP-015. **Entregas oficiais:** F04-E01.

[Índice da fase](README.md) · [Critérios da fase oficial](../../docs/MVP/fases/fase-04-v0.13.0.md)

## Objetivo e dependências

Preparar e auditar artefatos por RID, completando as lacunas descritas abaixo e reaproveitando implementação e aceites existentes. Não incluir funcionalidades de Melhorias, novos providers ou retomada de superfícies retiradas.

**Depende de:** [F03-T06](../fase-03-v0.12.0/task-06-experiencia-e-distribuicao.md).

**Gate da fase:** Contratos estabilizados e critérios aplicáveis das fases 01–03 aceitos; ambientes descartáveis disponíveis.

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

- [ ] Fixar matriz de sistema, arquitetura, instalação e versões de origem/destino antes dos ciclos; usar staging limpo.
- [ ] Gerar win-x64/win-arm64 e linux-x64/linux-arm64 conforme backend/lock e formatos do contrato de distribuição, sem misturar assets.
- [ ] Auditar payload real, inclusive single-file, hashes, SBOM oficial, LICENSE/MIT/notices/transitivas/nativos; confirmar ausência de CLI/runtime Copilot.
- [ ] Distinguir artefato gerado, publicado e homologado; não publicar assets nem assumir AUR disponível nesta tarefa.
- [ ] Corrigir defeitos comprovados no escopo, validar o impacto e atualizar os documentos oficiais afetados; não encerrar somente com outro plano quando houver implementação necessária.
- [ ] Registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

Artefatos de teste possuem origem, commit, RID, backend, hashes e inventário verificáveis; nenhum pacote ausente é anunciado como disponível.

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
