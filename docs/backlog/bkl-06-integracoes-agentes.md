# Backlog — demais integrações de agentes

**Disponibilidade em Release — 08/10/2026 (ADR-068):** por pedido do usuário, somente GitHub Copilot é composto como agente no Desktop Release. Os demais providers e seus fluxos continuam no código e em Debug conforme os gates existentes. A preservação de disponibilidade descrita abaixo é histórica para Release; fases e prioridade de IA local permanecem. [Decisão e evidências](../architecture/release-copilot.md).

**Política de tools vigente — 07/10/2026:** o backlog não altera a proibição compartilhada de query/escrita direta de agentes. Preservar providers/autenticação/canais existentes e limitar tools a metadados/índices e resultados/diagnósticos de execução humana capturados. [ADR-066 e contrato](../architecture/agent-tool-safety.md). As evidências de consultas/escritas anteriores são históricas.

**Decisão de 03/10/2026:** a [Fase 5 / v0.9.0](../phases/phase-05-v0.9.0/README.md) passa a concentrar a conclusão completa de GitHub Copilot e da infraestrutura de agentes necessária a ele. As demais integrações externas antes agrupadas nessa fase ficam neste backlog, **sem fase ou versão comprometida**.

## Recortes adiados

| Integração | Estado preservado / pendências |
| --- | --- |
| Claude Code oficial / Anthropic | Código existente preservado; homologação funcional/comercial pendente; gate Release e autenticação oficial continuam como estão. [Meta de paridade](integracoes-agentes/meta-claude-subscription-parity.md), [guia de termos](integracoes-agentes/guia-termos-anthropic-claude.md) e [estado/evidências](integracoes-agentes/historico-integracoes.md#plano-e-andamento). |
| Codex via App Server / assinatura ChatGPT | Experimental; isolamento preventivo e homologação de runtime/conta/tools pendentes. [Estado preservado](integracoes-agentes/historico-integracoes.md#codex--assinatura-chatgpt-experimental) e ADR-058 continuam válidos. |
| OpenAI API, Anthropic API e outros providers externos | Preservar os adapters e a disponibilidade já existentes; não criar novos providers, modalidades de autenticação ou fallback. Anthropic HTTP legado continua fora da composição Desktop. |
| MCP para clientes/agentes externos | Consolidação de ingressos externos, descoberta, contas/grants e homologação própria adiadas. Registry, contratos, broker/proxy, portas e testes existentes permanecem; infraestrutura exigida pelo Copilot fica na Fase 5. |

O ID funcional **ADV-09** é preservado. IA local/autocomplete e workflow mantêm os destinos próprios, Fases 8 e 7; não são movidos por esta decisão. O [histórico integral](integracoes-agentes/historico-integracoes.md) conserva IDs `P7-*`, datas, resultados, limitações e estados intermediários anteriores à separação.

## Código e comportamento preservados

Esta movimentação é documental. Não remover, desativar, refatorar ou alterar adapters, registros DI, catálogo, UI, autenticação, permissões, armazenamento, testes ou empacotamento destas integrações. A regra geral de ocultar entradas fora de fase não se aplica a esta reorganização: o usuário determinou manter as implementações como estão. Os bloqueios já existentes permanecem, inclusive o gate comercial Claude no Release e o caráter experimental do Codex.

Não ler credenciais, capturar/reutilizar tokens ou usar endpoints privados. Login permanece oficial; nenhuma falha faz fallback silencioso. Preservar owner LiteDB único, opt-outs, auditoria, grants, isolamento de sessão/turno e os contratos compartilhados necessários ao Copilot. Manter regressões das integrações adiadas não significa retomar suas entregas.

## Retomada e aceite

Retomar somente após repriorização explícita no roadmap, com fase/versão, escopo, dependências e critérios próprios. O trabalho restante deve partir do código e dos ADRs vigentes, sem infraestrutura ou autenticação paralela. Testes automatizados, gates oficiais/nativos e termos comerciais continuam distintos; nenhum gate é encerrado pelo adiamento. As pendências destes providers não bloqueiam o aceite exclusivo Copilot da Fase 5, exceto regressões na infraestrutura compartilhada ou invariantes de segurança/persistência.

## Referências

- [ADRs de agentes](../10-decisoes-arquiteturais.md#adr-046--runtime-e-adaptadores-de-agentes-22092026) e [decisão desta reorganização](../10-decisoes-arquiteturais.md#adr-065--fase-5-concentrada-em-copilot-e-demais-integrações-no-backlog-03102026).
- [Catálogo ADV-09](../03-catalogo-funcional.md), [inventário](../24-inventario-roadmap.md), [matriz](../15-matriz-de-validacao.md) e [acompanhamento](../12-acompanhamento-da-implementacao.md).
