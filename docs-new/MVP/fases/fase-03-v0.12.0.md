# Fase 3 — v0.12.0: Conclusão do GitHub Copilot

**Situação:** planejada no novo plano; nenhuma execução ou release declarada por este documento. **Requisitos:** MVP-011, MVP-014.

## Objetivo e dependências

Concluir lacunas de conta, conversa, contexto, sessões, tools e experiência com runtime oficial, preservando aceite funcional histórico.

Dependências: IA CPU da Fase 2; reutilizar registry, sessões, permissões e adapters existentes.

## Entregas TODO

- [ ] F03-E01 — Consolidar baseline GH/tool vigente e diagnosticar intermitência LiteDB sem mascarar falha por reexecução isolada.
- [ ] F03-E02 — Validar CLI oficial ausente/inválida/incompatível, override sem fallback, login/logout/expiração, modelos e políticas aplicáveis.
- [ ] F03-E03 — Demonstrar streaming/falha sem replay, duas conversas, snapshot de origem, cancelamento isolado e restart/retomada.
- [ ] F03-E04 — Demonstrar persistência/opt-out, exclusão com retry, revogação/expiração/auditoria e propostas com conflito/Undo nas lacunas existentes.
- [ ] F03-E05 — Inspecionar PNGs e registrar jornadas oficiais/nativas, termos/licenças e lacunas de ambiente sem ampliar tools.

## Evidências existentes e pendências

[Meta Copilot](../../../docs/phases/phase-05-v0.9.0/meta-de-encerramento.md), [matriz residual](../../../docs/phases/phase-05-v0.9.0/l00-matriz-residual.md) e [tools](../../../docs/architecture/agent-tool-safety.md). Aceite funcional existente não encerra conta/runtime, sessões/recovery, LiteDB, UI nativa ou termos.

## Critério de saída

Override inválido não faz fallback. Morte do runtime encerra turno sem replay e evento tardio não reabre conclusão. Duas conversas isolam destino/cancelamento. Falha de reserva impede envio durável; exclusão falha mantém referências para retry. Revogação durante await impede entrega indevida. Proposta respeita conflito/Undo; catálogo exclui tools removidas. Cada GH/tool aplicável tem evidência individual ou pendência bloqueante.

Cada entrega aponta procedimento, esperado/observado e evidência no snapshot final conforme [validação](../06-validacao-e-evidencias.md). Requisitos afetados permanecem coerentes com [arquitetura](../03-arquitetura-e-invariantes.md), [design system](../04-design-system.md) e [estado](../02-requisitos-e-estado.md).

Separar aceite automatizável de serviços/modelos reais e Desktop nativo; cenário obrigatório pendente mantém a entrega aberta. Não repetir trabalho funcional já aceito sem regressão; completar lacunas do recorte atual.

## Limites

Não incluir novos providers, funcionalidades de Melhorias, administração retirada ou pesquisa completa de modelos. Não enfraquecer segurança/privacidade, fidelidade BSON, ownership LiteDB e isolamento de origem para obter aprovação. A versão acima é alvo documental, sem renumerar artefatos históricos.
