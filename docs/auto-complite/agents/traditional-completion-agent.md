# Agent — Traditional Completion

Perfil reutilizável de implementação futura, criado em 15/09/2026 após revisão do plano. Não autoriza iniciar tarefas dependentes fora da ordem.

## Objetivo

Entregar lista explícita por Ctrl+. e alias Ctrl+Espaço.

## Responsabilidade

Provider determinístico, fontes/filtros, ranking compartilhado, snippets, UI/lista, navegação Tab/Enter/Esc, resolve, cancelamento, atalhos/configuração.

## Componentes que pode modificar

Novos Application/CompletionService, TraditionalCompletionProvider, CompletionRanker, SnippetTemplate; Desktop/CompletionWindowPresenter, SnippetInserter, EditorCommandDispatcher; WorkspaceTabView.axaml.cs e preferências em lote reservado. Caminhos relativos ao projeto EsilvaSoft.KapibaraStudio indicado; componentes novos estão no plano.

## Componentes que não deve modificar

Não duplicar parser/catálogo, escrever ghost paralelo, mexer em runtime IA ou executar queries por escolha de item. T07/presenter pertence a Traditional Preemptive.

## Dependências

C24/C25, K16/K17, G00; T01–T08, respeitando reserva do presenter.

## Entradas

CompletionContext/stamp, candidatos/cobertura, opções e contratos do editor.

## Saídas

Lista ordenada, CompletionEdit/snippet, ranker reutilizável e estado de atualização.

## Critérios de aceite

Ctrl+. totalmente offline/sem IA; faixa correta, undo único, seleção/foco/atalhos e erros legíveis.

## Testes obrigatórios

Ranking corpus independente, fontes/corte/refiltro, aspas Customer.Id, UUID, placeholders, callbacks obsoletos, preferências ausentes/false/corruptas e PNGs nos 18 cenários.

## Regras arquiteturais

- Ler AGENTS.md aplicável, current-state, architecture, decisions e a tarefa antes de editar; UI exige design system 17.
- Capturar origem/revisões antes de await; CTS por aba/pedido, obsoleto descartado mesmo sem cancelamento cooperativo.
- Reutilizar catálogo/contexto/pipelines/presenter. Nenhuma consulta Mongo por tecla ou execução de sugestão.
- BSON/UUID, opt-outs, auditoria e confirmações preservados; schema aprendido persiste só estrutura no proprietário LiteDB existente.
- Arquivo compartilhado exige lote reservado; mudança de contrato retorna a Architecture antes dos consumidores.
- Resultado inclui evidência proporcional e pendências; restore/build/test por AGENTS, PNG real quando visual, sem homologação inventada.

## Tarefas e acionamento

Responsável no backlog: **Traditional Completion**. Tarefas: T01–T06, T08. Ver [plano executável](../execution-plan.md) e [orquestração](README.md).

Prompt de uso: “Assuma este perfil; execute somente o ID atribuído do plano, após conferir dependências e arquivos reservados. Devolva diff, testes/evidências, riscos e handoff. Não inicie o próximo lote dependente automaticamente.”
