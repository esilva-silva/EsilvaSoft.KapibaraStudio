# Agentes de autocomplete

> [!NOTE]
> Esta pasta documenta perfis específicos da frente de autocomplete criados durante a revisão de 15/09/2026. O catálogo global de agentes citado na revisão não está mais no checkout; as instruções vigentes da solução estão em [`AGENTS.md`](../../../AGENTS.md).

Dez perfis especializados criados após a revisão de código/plano de 15/09/2026. São documentos reutilizáveis para acionar agentes posteriormente, não processos permanentes nem dez implementações disparadas nesta meta.

## Catálogo

| Perfil | Responsabilidade principal |
| --- | --- |
| [Autocomplete Architecture](architecture-agent.md) | Revisar arquitetura e integração, evitando quatro sistemas de completion. |
| [MongoDB Context](mongodb-context-agent.md) | Interpretar cursor e operação em documento incompleto com custo limitado. |
| [MongoDB Knowledge](mongodb-knowledge-agent.md) | Disponibilizar conhecimento contextual e schema observado, com origem e limites claros. |
| [Traditional Completion](traditional-completion-agent.md) | Entregar lista explícita por Ctrl+. e alias Ctrl+Espaço. |
| [AI Completion](ai-completion-agent.md) | Entregar IA explícita por Ctrl+; e pipeline de geração compartilhado. |
| [Traditional Preemptive](traditional-preemptive-agent.md) | Entregar sugestões contextuais automáticas de baixa latência, independentes de IA. |
| [AI Preemptive](ai-preemptive-agent.md) | Adicionar geração automática IA apenas quando elegível e útil. |
| [ONNX Runtime](onnx-runtime-agent.md) | Manter execução genérica de modelos, sem semântica MongoDB. |
| [Performance](performance-agent.md) | Medir custo e regressões durante cada fase. |
| [Testing](testing-agent.md) | Construir evidência independente de comportamento e integração das quatro modalidades. |

## Orquestração

Architecture fecha G00 → Knowledge consolida cache e aprendizado; Context constrói parser com contratos estáveis → integração → Traditional Completion → Traditional Preemptive. AI Completion prepara dados junto do tradicional preemptivo quando não disputam arquivos; ONNX Runtime entrega R41/R42 → IA explícita → AI Preemptive → híbrido → revisão Architecture. Performance e Testing acompanham desde G00.

[Plano executável](../execution-plan.md) define dependências reais, incluindo presenter T07 antes da IA explícita e schema learning L11–L16. Preservar essa ordem; desenho conceitual não autoriza todos escreverem ao mesmo tempo.

## Donos e arquivos compartilhados

| Área | Dono de implementação | Regra de integração |
| --- | --- | --- |
| Parser/contexto | MongoDB Context | Knowledge fornece shapes por contrato |
| Catálogo/cache/analyzer/LiteDB learned | MongoDB Knowledge | Um proprietário LiteDB; produtores de resultado reservados por lote |
| Ranker/snippets/lista/atalhos | Traditional Completion | Ranker reutilizado por automático |
| Coordinator e presenter inline | Traditional Preemptive | T07 cedo; AI Preemptive envia política/candidato |
| Fatos/prompt/output IA | AI Completion | ONNX é dono de tokenização e sessão |
| Runtime/metadata modelo/builders FIM | ONNX Runtime | Nenhuma regra MongoDB |
| DI/Core/preferences/WorkspaceTabView | Dono da tarefa ativa, coordenado por Architecture | Um escritor por arquivo por lote; revisar consumidores antes de merge |
| Benchmarks/testes | Performance / Testing | Implementadores podem adicionar testes locais; não disputar a mesma fixture |

## Handoff e aceite

1. Enviar ID, commit base, contratos aprovados, arquivos reservados, entradas e teste de aceite.
2. Agente verifica dependências; propõe alteração upstream se faltar contrato. Não inventa implementação paralela.
3. Entrega diff pequeno + evidência, limitações e próximo consumidor.
4. Testing/Performance revisam no lote; Architecture resolve incompatibilidade e libera integração.
5. Atualizar docs/índice e estado implementado apenas com verificação proporcional.

Sem branches/tarefas de app automáticas, agentes persistentes configurados ou modelo específico exigido. Esses perfis podem ser usados por subagentes ou por um implementador sequencial com a mesma disciplina.
