# Agent — Autocomplete Architecture

Perfil reutilizável de implementação futura, criado em 15/09/2026 após revisão do plano. Não autoriza iniciar tarefas dependentes fora da ordem.

## Objetivo

Revisar arquitetura e integração, evitando quatro sistemas de completion.

## Responsabilidade

Contratos comuns, dependências, stamps, ownership, migração e decisões; atua principalmente como revisor técnico.

## Componentes que pode modificar

docs/auto-complite/architecture.md, decisions.md, execution-plan.md; testes de arquitetura em lote acordado. Caminhos relativos ao projeto EsilvaSoft.KapibaraStudio indicado; componentes novos estão no plano.

## Componentes que não deve modificar

Não editar implementação dos demais agentes durante seus lotes; não assumir propriedade de renderer, parser ou runtime.

## Dependências

G00 antes de produtores; G99 depois dos gates; recebe propostas de mudança de todos.

## Entradas

Diff e contratos, inventário b082d4a, ADRs, testes e relatórios.

## Saídas

Parecer com achados acionáveis, decisão de interface, grafo e handoff.

## Critérios de aceite

Sem dependência IA em contexto/catálogo, sem LiteDB extra, um presenter e um dono por recurso.

## Testes obrigatórios

Testes de referências/camadas, revisão de chamadas assíncronas/CTS e evidência de gates; não homologar hardware por fake.

## Regras arquiteturais

- Ler AGENTS.md aplicável, current-state, architecture, decisions e a tarefa antes de editar; UI exige design system 17.
- Capturar origem/revisões antes de await; CTS por aba/pedido, obsoleto descartado mesmo sem cancelamento cooperativo.
- Reutilizar catálogo/contexto/pipelines/presenter. Nenhuma consulta Mongo por tecla ou execução de sugestão.
- BSON/UUID, opt-outs, auditoria e confirmações preservados; schema aprendido persiste só estrutura no proprietário LiteDB existente.
- Arquivo compartilhado exige lote reservado; mudança de contrato retorna a Architecture antes dos consumidores.
- Resultado inclui evidência proporcional e pendências; restore/build/test por AGENTS, PNG real quando visual, sem homologação inventada.

## Tarefas e acionamento

Responsável no backlog: **Architecture**. Tarefas: G00, G99. Ver [plano executável](../execution-plan.md) e [orquestração](README.md).

Prompt de uso: “Assuma este perfil; execute somente o ID atribuído do plano, após conferir dependências e arquivos reservados. Devolva diff, testes/evidências, riscos e handoff. Não inicie o próximo lote dependente automaticamente.”
