# Remoções do recorte do Studio

Este diretório transforma os nove itens do tópico 2 de [MVP, cortes e próximas metas](../../docs/mvp-status.md) em decisões de escopo para execução futura. Esta documentação não remove nem desativa código, dados ou interface. O estado histórico das fases e as evidências existentes não devem ser reescritos como se a remoção já tivesse ocorrido.

| ID | Decisão | Lote |
|---|---|---|
| REM-001 | Retirar administração avançada indicada no tópico 2 | L3 |
| REM-002 | Retirar metas de administração do host, SSH administrativo, configuração e reinício de servidor e monitoramento externo | L5 |
| REM-003 | Retirar metas de operação de replica sets/sharding, provisionamento Atlas, nuvem, snapshots e recuperação operacional | L5 |
| REM-004 | Retirar meta de cofre criptográfico genérico e gestão de chaves/KMS | L5 |
| REM-005 | Cancelar meta de chat separado por workflow | L5 |
| REM-006 | Remover modos separados Script/mongosh e Agregação, com recuperação explícita dos rascunhos | L2 |
| REM-007 | Limpar infraestrutura histórica de IA automática durante a digitação | L4 |
| REM-008 | Retirar do MVP backup por Database Tools, sincronização automática e jobs agendados | L5 |
| REM-009 | Retirar do MVP SQL para MQL, gerador de dados, migrações versionadas, Git integrado e Federation/Archive/Charts/conectores | L5 |

A execução começa por L1, que fecha o inventário de consumidores e a política de compatibilidade. Cada lote depende dos critérios em [inventário técnico](inventario-tecnico.md) e das tarefas em [TODO](TODO.md). As metas não implementadas exigem revisão documental; não se presume ausência de qualquer código apenas por sua classificação funcional.

O recorte mantido está em [requisitos e estado](../MVP/02-requisitos-e-estado.md), IDs MVP-001–015, e [fase 01](../MVP/fases/fase-01-v0.10.0.md); avaliar melhorias em [conceitos](../Melhorias/conceitos.md). Preservar Console JavaScript e `aggregate`, operação cotidiana, importação/exportação lógica, agente local e Copilot. Claude, Codex, APIs externas e MCP externo permanecem disponíveis como estão, conforme decisão do usuário; sua conclusão foi adiada para Melhorias. Os contratos e infraestrutura compartilhados também permanecem.

As invariantes de `AGENTS.md` continuam obrigatórias, em especial segurança das ferramentas de agentes, isolamento de abas, BSON, confirmação, auditoria, opt-outs e proteção de credenciais. Mudanças futuras exigem atualização coordenada do design system, ADRs, fases, catálogo, guia e acompanhamento, sem apagar registros históricos.
