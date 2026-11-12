# KS-US06-T01 — Listar o repositório do agente no download

**Estado de execução:** aberta; criação do arquivo não executa nem homologa a entrega. **Versão-alvo:** v0.11.0.

**User story:** [KS-US06 — Encontrar, baixar e escolher o modelo de cada papel](README.md). **Requisitos relacionados:** MVP-012, MVP-013. **Entregas oficiais:** F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## Objetivo e dependências

Acrescentar o pacote do agente publicado aos repositórios padrão do download.

**Depende de:** KT-US09-T02 — Publicar o pacote do agente (repositório de modelos `KapibaraStudio.MongoIA`, `tasks/us-09-publicacao-e-entrega/task-02-publicar-agente.md`)

## Referências obrigatórias

- [Regras do repositório](../../../AGENTS.md)
- [Regras comuns e comandos](../../README.md#regras-comuns-de-validação)
- [Catálogo e estado](../../../docs/MVP/02-requisitos-e-estado.md)
- [Validação e evidências](../../../docs/MVP/06-validacao-e-evidencias.md)
- [Contrato de IA local](../../../docs/MVP/contratos/ia-local.md)

## Execução

- [ ] Adicionar repositório e variantes do agente a `DefaultRepositories`, conforme a nota de entrega.
- [ ] Estender `HuggingFaceKapiCoderCatalogTests` com `HttpMessageHandler` falso.
- [ ] Conferir tamanho, licença e requisitos exibidos antes do download.
- [ ] Executar localmente a guarda `RealModelHubAccessIsConfinedToExplicitLocalModelTests`.
- [ ] Atualizar os documentos oficiais afetados quando o comportamento mudar e registrar evidências e limitações pelo procedimento oficial, sem transportar contagens antigas como resultado do checkout.

## Aceite e validação

O agente aparece no catálogo com dados corretos; nenhum teste do CI acessa o Hub real.

- **Automatizável:** testes de comportamento e de falha da alteração, mais restore, build e testes das regras comuns; usam runtime e `HttpMessageHandler` falsos e não carregam modelo.
- **Real/manual:** download real é manual e Explicit.
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
