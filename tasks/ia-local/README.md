# IA local — user stories e tarefas de implementação

[Índice geral de tarefas](../README.md) · [Fase oficial](../../docs/MVP/fases/fase-02-v0.11.0.md) · [Tarefas da fase 02](../fase-02-v0.11.0/README.md) · [Contrato de IA local](../../docs/MVP/contratos/ia-local.md)

Backlog criado em 12/11/2026 para detalhar, em user stories (US) com tarefas, o que falta para o **agente de IA local** e o **autocomplete com IA** do Studio. É uma árvore de execução separada: as tarefas F02-T01..T05 continuam válidas e inalteradas, e as entregas oficiais continuam sendo F02-E01..E05. Estado de requisitos permanece exclusivamente no [catálogo](../../docs/MVP/02-requisitos-e-estado.md); criar estes arquivos não executa nem homologa nada.

O treinamento dos modelos tem índice próprio em `tasks/README.md` do repositório de modelos `KapibaraStudio.MongoIA` (IDs `KT-USnn`). Dependências entre os dois repositórios são citadas pelo ID.

## Estado de partida (lido no checkout `8b32c35`, 12/11/2026)

Leitura de código, sem execução de testes nesta data.

- **Presente:** `LocalAgentProvider` com laço de vários passos, tools do plano, `SubmitToolResultAsync`, raciocínio por id de token, orçamento por passo e por turno e retry único de `InvalidToolCallFormat`; `LocalAgentPromptBuilder` (`local-agent-qwen3-v1`), `LocalAgentToolCallParser`, `Qwen3ModelAdapter`, capacidades `Agent/Tools/Reasoning`, grants de turno para o destino local, item de raciocínio e preferências no painel, GenAI 0.18.0.
- **Ausente ou parcial:** sessão de geração com KV vivo por turno (cada passo refaz o prefill completo); checagem prévia de janela; `Seed`; encerramento `Cancelled` do raciocínio; aplicação de `agentContract` e do hash do template na carga; decodificação restrita; repositório do agente no download; testes do ciclo tool → resultado → resposta; testes Explicit com modelo de agente; `--seeds`, `--thinking` e `agent bench` no KapiLab.
- **Modelo:** só existe pacote candidato não qualificado (Qwen3-4B-Thinking base, DML fp16). O pacote treinado de CPU depende do repositório de modelos.

## Regras desta árvore

- Valem as [regras comuns](../README.md#regras-comuns-de-validação) e o [AGENTS.md](../../AGENTS.md). Reaproveitar o que existe; não criar provider nem autenticação paralelos.
- Tools de agentes nunca consultam documentos nem escrevem no MongoDB, mesmo com aprovação. Os 13 nomes recusados retornam `UnknownTool`.
- Digitar não infere; `Ctrl+;` é o único gatilho do autocomplete com IA. Sem modelo, editor, consultas e sugestões determinísticas continuam funcionando; erro ou ausência de modelo não troca de provider em silêncio.
- O CI nunca baixa nem carrega modelos. Pesos reais e Hub real são `[Explicit]` com `Category("LocalModelIntegration")`; benchmarks são manuais.
- Cache de prefixo permanece `Disabled` sem aceite específico do pacote.
- Fixar pacote, hardware, corpus e limites antes de medir (KS-US01).

## User stories

| US | Requisitos e entregas | Tarefas | Depende de |
| --- | --- | --- | --- |
| [KS-US01 — Fixar a referência e fazer valer os contratos do pacote](us-01-referencia-e-contratos-do-pacote/README.md) | MVP-012, MVP-013 · F02-E01 | T01..T05 | — |
| [KS-US02 — Tornar o laço do agente local previsível em sucesso e falha](us-02-robustez-do-laco-do-agente/README.md) | MVP-013 · F02-E03 | T01..T06 | KS-US01 |
| [KS-US03 — Reutilizar o estado de geração entre os passos do turno](us-03-sessao-de-geracao-por-turno/README.md) | MVP-013 · F02-E03, F02-E05 | T01..T04 | KS-US02 |
| [KS-US04 — Restringir a saída de tool call quando o runtime permitir](us-04-decodificacao-restrita/README.md) | MVP-013 · F02-E03 | T01..T03 | KS-US02 |
| [KS-US05 — Comprovar tools, recusas e privacidade com modelo real](us-05-tools-permissoes-e-privacidade/README.md) | MVP-013 · F02-E03 | T01..T04 | KS-US02 |
| [KS-US06 — Encontrar, baixar e escolher o modelo de cada papel](us-06-modelos-descoberta-e-download/README.md) | MVP-012, MVP-013 · F02-E05 | T01..T05 | KS-US01 |
| [KS-US07 — Completar a experiência do agente local no painel](us-07-experiencia-no-painel-agente/README.md) | MVP-011, MVP-013 · F02-E03, F02-E05 | T01..T04 | KS-US02 |
| [KS-US08 — Concluir o autocomplete explícito com o pacote v4](us-08-autocomplete-explicito/README.md) | MVP-008, MVP-012 · F02-E02 | T01..T04 | KS-US01 |
| [KS-US09 — Garantir concorrência, liberação e recuperação](us-09-concorrencia-e-ciclo-de-vida/README.md) | MVP-011, MVP-012, MVP-013 · F02-E04 | T01..T04 | KS-US02, KS-US08 |
| [KS-US10 — Avaliar com modelo real e consolidar as evidências](us-10-avaliacao-real-e-evidencias/README.md) | MVP-008, MVP-011..013 · F02-E05 | T01..T05 | KS-US03, KS-US05, KS-US06, KS-US08, KS-US09 |

## Todas as tarefas

### KS-US01 — Fixar a referência e fazer valer os contratos do pacote

- [KS-US01-T01 — Registrar pacote de referência e limites](us-01-referencia-e-contratos-do-pacote/task-01-registrar-referencia-e-limites.md)
- [KS-US01-T02 — Revalidar paridade de prompt no runtime atual](us-01-referencia-e-contratos-do-pacote/task-02-revalidar-paridade-de-prompt.md)
- [KS-US01-T03 — Aplicar o contrato do agente na carga do pacote](us-01-referencia-e-contratos-do-pacote/task-03-aplicar-contrato-do-agente-na-carga.md)
- [KS-US01-T04 — Neutralizar marcadores reservados em texto não confiável](us-01-referencia-e-contratos-do-pacote/task-04-escapar-marcadores-reservados.md)
- [KS-US01-T05 — Registrar as decisões já tomadas no código](us-01-referencia-e-contratos-do-pacote/task-05-registrar-decisoes-vigentes.md)

### KS-US02 — Tornar o laço do agente local previsível em sucesso e falha

- [KS-US02-T01 — Cobrir o ciclo de tools com runtime falso](us-02-robustez-do-laco-do-agente/task-01-testes-do-ciclo-com-runtime-falso.md)
- [KS-US02-T02 — Checar a janela de contexto antes de gerar](us-02-robustez-do-laco-do-agente/task-02-checagem-previa-de-janela.md)
- [KS-US02-T03 — Preencher semente e amostragem do pacote](us-02-robustez-do-laco-do-agente/task-03-semente-e-amostragem.md)
- [KS-US02-T04 — Encerrar raciocínio e tool pendente no cancelamento](us-02-robustez-do-laco-do-agente/task-04-cancelamento-do-raciocinio.md)
- [KS-US02-T05 — Alinhar ao contrato tool fora do plano e envelope inválido](us-02-robustez-do-laco-do-agente/task-05-tool-fora-do-plano-e-envelope.md)
- [KS-US02-T06 — Manter o dispositivo durante o turno](us-02-robustez-do-laco-do-agente/task-06-dispositivo-estavel-no-turno.md)

### KS-US03 — Reutilizar o estado de geração entre os passos do turno

- [KS-US03-T01 — Definir a porta de sessão de geração](us-03-sessao-de-geracao-por-turno/task-01-porta-de-sessao.md)
- [KS-US03-T02 — Implementar a sessão no runtime ONNX](us-03-sessao-de-geracao-por-turno/task-02-implementacao-onnx.md)
- [KS-US03-T03 — Usar a sessão no laço do agente](us-03-sessao-de-geracao-por-turno/task-03-provider-usa-sessao.md)
- [KS-US03-T04 — Provar paridade e medir o ganho](us-03-sessao-de-geracao-por-turno/task-04-paridade-e-medicao.md)

### KS-US04 — Restringir a saída de tool call quando o runtime permitir

- [KS-US04-T01 — Executar o smoke de guidance e decidir](us-04-decodificacao-restrita/task-01-smoke-de-guidance.md)
- [KS-US04-T02 — Gerar a gramática e ativá-la por opt-in do pacote](us-04-decodificacao-restrita/task-02-gramatica-e-opt-in.md)
- [KS-US04-T03 — Validar com modelo real ou registrar a limitação](us-04-decodificacao-restrita/task-03-validar-ou-registrar-limitacao.md)

### KS-US05 — Comprovar tools, recusas e privacidade com modelo real

- [KS-US05-T01 — Exercitar o ciclo real das tools permitidas](us-05-tools-permissoes-e-privacidade/task-01-ciclo-real-de-tools.md)
- [KS-US05-T02 — Validar recusas, limites e revogação](us-05-tools-permissoes-e-privacidade/task-02-recusas-e-operacoes-proibidas.md)
- [KS-US05-T03 — Adicionar o canário de privacidade do raciocínio](us-05-tools-permissoes-e-privacidade/task-03-canario-de-privacidade-do-raciocinio.md)
- [KS-US05-T04 — Demonstrar propostas e criação de arquivo](us-05-tools-permissoes-e-privacidade/task-04-propostas-e-arquivos.md)

### KS-US06 — Encontrar, baixar e escolher o modelo de cada papel

- [KS-US06-T01 — Listar o repositório do agente no download](us-06-modelos-descoberta-e-download/task-01-repositorio-do-agente.md)
- [KS-US06-T02 — Separar a escolha de modelo por papel](us-06-modelos-descoberta-e-download/task-02-papeis-autocomplete-e-agente.md)
- [KS-US06-T03 — Avisar sobre adequação ao hardware](us-06-modelos-descoberta-e-download/task-03-recomendacao-por-hardware.md)
- [KS-US06-T04 — Preservar versões instaladas e permitir voltar](us-06-modelos-descoberta-e-download/task-04-versoes-e-rollback.md)
- [KS-US06-T05 — Garantir o uso sem modelo](us-06-modelos-descoberta-e-download/task-05-uso-sem-modelo.md)

### KS-US07 — Completar a experiência do agente local no painel

- [KS-US07-T01 — Completar as preferências de raciocínio](us-07-experiencia-no-painel-agente/task-01-preferencias-de-raciocinio.md)
- [KS-US07-T02 — Mostrar estados e espera do agente local](us-07-experiencia-no-painel-agente/task-02-estados-e-espera.md)
- [KS-US07-T03 — Gerar e inspecionar os PNGs das mudanças](us-07-experiencia-no-painel-agente/task-03-renderizacao-e-pngs.md)
- [KS-US07-T04 — Atualizar guia e design system](us-07-experiencia-no-painel-agente/task-04-guia-e-design-system.md)

### KS-US08 — Concluir o autocomplete explícito com o pacote v4

- [KS-US08-T01 — Validar o fluxo de prévia e aceite](us-08-autocomplete-explicito/task-01-fluxo-ctrl-ponto-e-virgula.md)
- [KS-US08-T02 — Validar contexto v2 e fechamento de delimitadores](us-08-autocomplete-explicito/task-02-contexto-v2-e-delimitadores.md)
- [KS-US08-T03 — Homologar ou manter desativado o cache de prefixo](us-08-autocomplete-explicito/task-03-cache-de-prefixo-por-pacote.md)
- [KS-US08-T04 — Guardar que digitar não infere](us-08-autocomplete-explicito/task-04-digitacao-nao-infere.md)

### KS-US09 — Garantir concorrência, liberação e recuperação

- [KS-US09-T01 — Validar cancelamento isolado e concorrência](us-09-concorrencia-e-ciclo-de-vida/task-01-cancelamento-e-concorrencia.md)
- [KS-US09-T02 — Validar a liberação do modelo](us-09-concorrencia-e-ciclo-de-vida/task-02-liberacao-do-modelo.md)
- [KS-US09-T03 — Validar retenção e opt-out](us-09-concorrencia-e-ciclo-de-vida/task-03-retencao-e-opt-out.md)
- [KS-US09-T04 — Validar recuperação e falha de persistência](us-09-concorrencia-e-ciclo-de-vida/task-04-recuperacao-de-sessao.md)

### KS-US10 — Avaliar com modelo real e consolidar as evidências

- [KS-US10-T01 — Criar os testes Explicit do agente](us-10-avaliacao-real-e-evidencias/task-01-testes-explicit-do-agente.md)
- [KS-US10-T02 — Estender o `kapilab agent run`](us-10-avaliacao-real-e-evidencias/task-02-extensoes-do-kapilab.md)
- [KS-US10-T03 — Executar as medições reais](us-10-avaliacao-real-e-evidencias/task-03-medicoes-reais.md)
- [KS-US10-T04 — Validar a experiência em desktop nativo](us-10-avaliacao-real-e-evidencias/task-04-experiencia-nativa.md)
- [KS-US10-T05 — Consolidar evidências e estado](us-10-avaliacao-real-e-evidencias/task-05-consolidacao-documental.md)

## Ordem e caminho crítico

KS-US01 abre a árvore. KS-US02, KS-US03, KS-US05, KS-US07, KS-US08 e KS-US09 podem avançar com o pacote candidato atual, desde que a evidência o identifique como não qualificado. KS-US04 é condicional ao smoke de guidance. KS-US06 e o fechamento de KS-US10 dependem do pacote treinado e publicado (KT-US09 do repositório de modelos).

## Correspondência com as tarefas da fase 02

| Tarefa F02 | User stories que a detalham |
| --- | --- |
| F02-T01 — Fixar referência e orçamentos CPU | KS-US01 |
| F02-T02 — Concluir autocomplete explícito | KS-US08 |
| F02-T03 — Concluir ciclo do agente local | KS-US02, KS-US03, KS-US04, KS-US05, KS-US07 |
| F02-T04 — Validar concorrência e ciclo de vida | KS-US09 |
| F02-T05 — Avaliar modelo real e experiência nativa | KS-US06, KS-US10 |

## Fora deste backlog

NPU/QNN e registro de execution providers do Windows ML, perfil reduzido do agente para NPU, tier Pro, novos providers e qualquer item de Melhorias.
