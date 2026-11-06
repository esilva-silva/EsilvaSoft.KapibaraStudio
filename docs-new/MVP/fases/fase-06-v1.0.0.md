# Fase 6 — v1.0.0: Aceite estável

**Situação:** planejada no novo plano; nenhuma execução ou release declarada por este documento. **Requisitos:** MVP-001..015.

## Objetivo e dependências

Preparar liberação estável para uso diário sem adicionar funcionalidades de backlog.

Dependências: Fases 1–5 com critérios aplicáveis aceitos e evidências vinculadas ao estado final.

## Entregas TODO

- [ ] F06-E01 — Reconciliar requisitos, critérios, artefatos e evidências no commit final, com pendências apenas fora do suporte/escopo declarado e justificadas.
- [ ] F06-E02 — Executar restore locked, build, testes aplicáveis e guardas manuais exigidas antes de push/release, registrando resultados e ignorados.
- [ ] F06-E03 — Auditar pacotes reais, integridade, notices/SBOM, termos aplicáveis, instalação/update e dados preservados.
- [ ] F06-E04 — Revisar documentação de uso, suporte, segurança, recuperação e notas de release; obter aceite explícito do recorte estável.
- [ ] F06-E05 — Registrar decisão de liberação e versão efetiva pelos procedimentos do projeto; criação desta fase não publica nem tagueia.

## Evidências existentes e pendências

[Estado](../02-requisitos-e-estado.md), [validação](../06-validacao-e-evidencias.md) e [distribuição](../07-distribuicao-e-suporte.md). Evidência histórica isolada não prova release estável: faltam estado final, gates, artefatos e decisão de liberação.

## Critério de saída

Todos MVP-001..015 têm critérios obrigatórios aplicáveis aprovados no suporte declarado, sem converter ignorados em sucesso. Checks e pacotes apontam mesmo snapshot; defeito bloqueante não vira melhoria por renomeação. Notas descrevem limites/recuperação/privacidade. Decisão registra versão efetiva e artefatos auditados; publicação/tag segue procedimento próprio.

Cada entrega aponta procedimento, esperado/observado e evidência no snapshot final conforme [validação](../06-validacao-e-evidencias.md). Requisitos afetados permanecem coerentes com [arquitetura](../03-arquitetura-e-invariantes.md), [design system](../04-design-system.md) e [estado](../02-requisitos-e-estado.md).

Separar aceite automatizável de serviços/modelos reais e Desktop nativo; cenário obrigatório pendente mantém a entrega aberta. Não repetir trabalho funcional já aceito sem regressão; completar lacunas do recorte atual.

## Limites

Não incluir novos providers, funcionalidades de Melhorias, administração retirada ou pesquisa completa de modelos. Não enfraquecer segurança/privacidade, fidelidade BSON, ownership LiteDB e isolamento de origem para obter aprovação. A versão acima é alvo documental, sem renumerar artefatos históricos.
