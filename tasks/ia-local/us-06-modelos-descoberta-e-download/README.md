# KS-US06 — Encontrar, baixar e escolher o modelo de cada papel

**Estado de execução:** aberta; criação desta pasta não executa nem homologa nenhuma entrega. **Versão-alvo:** v0.11.0.

**Requisitos relacionados:** MVP-012, MVP-013. **Entregas oficiais:** F02-E05.

[Índice IA local](../README.md) · [Critérios da fase oficial](../../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../../fase-02-v0.11.0/README.md)

## História

**Como** pessoa que quer usar IA local no Studio, **quero** baixar o modelo do agente e o do autocomplete e escolher qual faz cada papel, **para** começar a usar sem montar pastas à mão e sem perder o que já funciona.

## Contexto verificado

- `HuggingFaceModelSource.DefaultRepositories` lista Nano e Small; não há repositório do agente.
- `AutocompleteSettingsViewModel` já tem `AgentModels` e `ChatModel` com a opção de seguir o modelo do autocomplete.
- Não existe recomendador por hardware; rollback por versão não foi verificado.
- O orçamento do tier vem do pacote e vence a estimativa por hardware.

## Critérios de aceite

- [ ] O pacote do agente publicado aparece no download, com testes usando handler falso.
- [ ] Autocomplete e agente têm seleção própria, com um modelo carregado por vez.
- [ ] O Studio avisa quando o hardware não comporta o pacote, sem trocar de modelo ou provider em silêncio.
- [ ] Instalar nova versão não sobrescreve a anterior e permite voltar.
- [ ] Sem modelo, o Studio continua utilizável e explica o estado.

## Fora de escopo

- Pacotes NPU/QNN.
- Download automático sem ação do usuário.

## Dependências

- [KS-US01 — Fixar a referência e fazer valer os contratos do pacote](../us-01-referencia-e-contratos-do-pacote/README.md)

## Tarefas

| Tarefa | Resultado | Depende de |
| --- | --- | --- |
| [KS-US06-T01 — Listar o repositório do agente no download](task-01-repositorio-do-agente.md) | Agente disponível no catálogo de download | KT-US09-T02 (modelos) |
| [KS-US06-T02 — Separar a escolha de modelo por papel](task-02-papeis-autocomplete-e-agente.md) | Seleção por papel com um modelo carregado | — |
| [KS-US06-T03 — Avisar sobre adequação ao hardware](task-03-recomendacao-por-hardware.md) | Aviso de memória e dispositivo | KS-US06-T01 |
| [KS-US06-T04 — Preservar versões instaladas e permitir voltar](task-04-versoes-e-rollback.md) | Instalação por versão com verificação | KS-US06-T01 |
| [KS-US06-T05 — Garantir o uso sem modelo](task-05-uso-sem-modelo.md) | Estados sem modelo e de erro | — |

A história só fecha quando todos os critérios de aceite têm evidência proporcional. Evidência sintética e real permanecem separadas; preparação não equivale a aceite.
