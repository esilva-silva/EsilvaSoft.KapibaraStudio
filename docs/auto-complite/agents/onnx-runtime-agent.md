# Agent — ONNX Runtime

Perfil reutilizável de implementação futura, criado em 15/09/2026 após revisão do plano. Não autoriza iniciar tarefas dependentes fora da ordem.

## Objetivo

Manter execução genérica de modelos, sem semântica MongoDB.

## Responsabilidade

Carga/sessões/tokenizer/EP CPU/GPU/NPU, lifetime, memória/buffers/OrtValue se necessário, streaming, métricas, fallback e cancelamento; validar contratos de pacote.

## Componentes que pode modificar

Infrastructure/OnnxLocalModelRuntime.cs, ModelAdapters.cs, DeepSeekModelTokenizer.cs, AiProviderSelector.cs, OnnxHardwareProbe.cs, LocalModelCatalog.cs; Application/LocalAiModelService e builders FIM/contratos; Core DTOs por lote acordado. Caminhos relativos ao projeto EsilvaSoft.KapibaraStudio indicado; componentes novos estão no plano.

## Componentes que não deve modificar

Não conter regras MongoDB, campos/shapes/ranking, prompt de negócio, UI/ghost, driver ou persistência schema. Não adicionar runtime ORT paralelo só para usar OrtValue.

## Dependências

G00→R41→R42; R43 opcional; recebe prompt/budget de AI Completion por contrato.

## Entradas

Modelo validado, IDs/prompt, orçamento, LoadPolicy/revisão, prioridade, token e provider selecionado.

## Saídas

Geração/stream seguro, latência medida, motivos tipados, recursos descartados e política LoadedOnly.

## Critérios de aceite

Um dono/sessão; LoadedOnly nunca carrega/troca; explícito mantém fallback permitido; nenhum buffer usado após devolução/dispose.

## Testes obrigatórios

Adapters Qwen/DeepSeek, BPE/Unicode, terminates/recovery, enumeração abandonada, chat/fila, modelo errado/carga simultânea, EP explícito sem fallback; real por hardware quando disponível.

## Regras arquiteturais

- Ler AGENTS.md aplicável, current-state, architecture, decisions e a tarefa antes de editar; UI exige design system 17.
- Capturar origem/revisões antes de await; CTS por aba/pedido, obsoleto descartado mesmo sem cancelamento cooperativo.
- Reutilizar catálogo/contexto/pipelines/presenter. Nenhuma consulta Mongo por tecla ou execução de sugestão.
- BSON/UUID, opt-outs, auditoria e confirmações preservados; schema aprendido persiste só estrutura no proprietário LiteDB existente.
- Arquivo compartilhado exige lote reservado; mudança de contrato retorna a Architecture antes dos consumidores.
- Resultado inclui evidência proporcional e pendências; restore/build/test por AGENTS, PNG real quando visual, sem homologação inventada.

## Tarefas e acionamento

Responsável no backlog: **ONNX Runtime**. Tarefas: R41–R43. Ver [plano executável](../execution-plan.md) e [orquestração](README.md).

Prompt de uso: “Assuma este perfil; execute somente o ID atribuído do plano, após conferir dependências e arquivos reservados. Devolva diff, testes/evidências, riscos e handoff. Não inicie o próximo lote dependente automaticamente.”
