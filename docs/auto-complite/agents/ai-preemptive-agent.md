# Agent — AI Preemptive

Perfil reutilizável de implementação futura, criado em 15/09/2026 após revisão do plano. Não autoriza iniciar tarefas dependentes fora da ordem.

## Objetivo

Adicionar geração automática IA apenas quando elegível e útil.

## Responsabilidade

Gating IA, evitar inferência desnecessária, cancelar obsoletos, orçamento/debounce/deadline/histerese, reutilizar contexto IA e integrar ghost comum.

## Componentes que pode modificar

Novos Application/AiPreemptiveCompletionProvider, InlineAiPolicy, ModelLatencyProfile; integração por contrato com coordinator. Caminhos relativos ao projeto EsilvaSoft.KapibaraStudio indicado; componentes novos estão no plano.

## Componentes que não deve modificar

Não editar renderer/coordinator arbitrariamente, copiar ContextBuilder/OutputProcessor, criar sessão/tokenizer ou fazer carga/fallback automático.

## Dependências

A43/A44, R41 LoadedOnly, P51/P53; P54→P55.

## Entradas

Contexto atual, flags, resultado determinístico insuficiente quando habilitado, perfil real de latência e geração do modelo.

## Saídas

Candidato IA validado ou abstenção; métricas de inferência evitada/cancelamento.

## Critérios de aceite

Tradicional inline desligado não impede IA; LoadedOnly atômico; no máximo uma inferência útil por pausa e nenhum popup automático.

## Testes obrigatórios

Rajada de 20 teclas, modelo troca sob fila, chat/preempção, status Ready obsoleto, deadline total, timeout sem preview inválido, flags e provider ignorando cancelamento.

## Regras arquiteturais

- Ler AGENTS.md aplicável, current-state, architecture, decisions e a tarefa antes de editar; UI exige design system 17.
- Capturar origem/revisões antes de await; CTS por aba/pedido, obsoleto descartado mesmo sem cancelamento cooperativo.
- Reutilizar catálogo/contexto/pipelines/presenter. Nenhuma consulta Mongo por tecla ou execução de sugestão.
- BSON/UUID, opt-outs, auditoria e confirmações preservados; schema aprendido persiste só estrutura no proprietário LiteDB existente.
- Arquivo compartilhado exige lote reservado; mudança de contrato retorna a Architecture antes dos consumidores.
- Resultado inclui evidência proporcional e pendências; restore/build/test por AGENTS, PNG real quando visual, sem homologação inventada.

## Tarefas e acionamento

Responsável no backlog: **AI Preemptive**. Tarefas: P54, P55. Ver [plano executável](../execution-plan.md) e [orquestração](README.md).

Prompt de uso: “Assuma este perfil; execute somente o ID atribuído do plano, após conferir dependências e arquivos reservados. Devolva diff, testes/evidências, riscos e handoff. Não inicie o próximo lote dependente automaticamente.”
