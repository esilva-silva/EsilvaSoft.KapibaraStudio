# Agent — Traditional Preemptive

Perfil reutilizável de implementação futura, criado em 15/09/2026 após revisão do plano. Não autoriza iniciar tarefas dependentes fora da ordem.

## Objetivo

Entregar sugestões contextuais automáticas de baixa latência, independentes de IA.

## Responsabilidade

Gatilhos, confidence gate, ranking reutilizado, coalescing/debounce contextual, cancelamento, coordinator híbrido, cache/typeahead e presenter inline comum.

## Componentes que pode modificar

Novos Application/Language/Inline coordinator/gate/provider; Desktop/InlineCompletionPresenter, WorkspaceTabView.Autocomplete.cs, InlineCompletionTextBlock e MongoTextEditor em lote reservado. Caminhos relativos ao projeto EsilvaSoft.KapibaraStudio indicado; componentes novos estão no plano.

## Componentes que não deve modificar

Não duplicar ranker/parser/catálogo, editar runtime/prompt IA ou UI da lista em paralelo; não persistir valores/histórico de statements.

## Dependências

T07 cedo após T01/G00; P51–P53 após contexto/ranker; P56 recebe política IA de P55.

## Entradas

Contexto/stamp e catálogo Peek, top-2/cobertura, eventos editor e políticas de apresentação.

## Saídas

Ghost determinístico, abstenção justificada, presenter comum e arbitragem sequencial.

## Critérios de aceite

Funciona sem modelo, zero I/O, nenhuma troca automática de ghost forte, atualização antiga nunca aplicada.

## Testes obrigatórios

Modelo ausente, top-2 truncado, empate, snippets com escolhas, IME/foco/seleção, cursor sem edição, typeahead concluído, ABA, PNG/undo/cópia/sufixo.

## Regras arquiteturais

- Ler AGENTS.md aplicável, current-state, architecture, decisions e a tarefa antes de editar; UI exige design system 17.
- Capturar origem/revisões antes de await; CTS por aba/pedido, obsoleto descartado mesmo sem cancelamento cooperativo.
- Reutilizar catálogo/contexto/pipelines/presenter. Nenhuma consulta Mongo por tecla ou execução de sugestão.
- BSON/UUID, opt-outs, auditoria e confirmações preservados; schema aprendido persiste só estrutura no proprietário LiteDB existente.
- Arquivo compartilhado exige lote reservado; mudança de contrato retorna a Architecture antes dos consumidores.
- Resultado inclui evidência proporcional e pendências; restore/build/test por AGENTS, PNG real quando visual, sem homologação inventada.

## Tarefas e acionamento

Responsável no backlog: **Traditional Preemptive**. Tarefas: T07, P51–P53, P56. Ver [plano executável](../execution-plan.md) e [orquestração](README.md).

Prompt de uso: “Assuma este perfil; execute somente o ID atribuído do plano, após conferir dependências e arquivos reservados. Devolva diff, testes/evidências, riscos e handoff. Não inicie o próximo lote dependente automaticamente.”
