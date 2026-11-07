# Fase 1 — v0.10.0: Limpeza aprovada e base cotidiana

**Situação:** em execução pela Task-R 1; nenhuma release ou homologação completa declarada. **Requisitos:** MVP-001..011.

## Objetivo e dependências

Inventariar caminhos a remover e dependências compartilhadas; implementar cortes aprovados com compatibilidade e correções da base, sem adiar IA CPU por revisão ilimitada.

Dependências: Nenhuma; baseline funcional existente.

## Entregas TODO

- [x] F01-E01 — Registrar mapa de código/UI/DI/configuração/testes dos cortes, distinguindo contrato compartilhado e provider preservado.
- [x] F01-E02 — Tratar abertura/migração de rascunhos e sessões legados, com falha visível, recuperação e sem execução automática.
- [x] F01-E03 — Retirar superfícies e caminhos aprovados de administração pesada, modos separados e IA automática obsoleta, preservando Console/aggregate e invariantes.
- [ ] F01-E04 — Revalidar jornadas MVP-001..011 afetadas e atualizar catálogo/guia com evidência e pendências nominais.

## Evidências existentes e pendências

[Aceite F6](../../../docs/phases/phase-06-v0.10.0/README.md), [Console](../../../docs/20-console.md), [base](../../../docs/done/README.md) e [inventário](../../Remover/inventario-tecnico.md)/[TODO de remoção](../../Remover/TODO.md). Compatibilidade e cortes foram implementados na Task-R 1, com testes por lote. F01-E04 conserva as pendências de homologação das jornadas reais/nativas; não equivale à conclusão da fase.

## Critério de saída

Rascunho legado abre por tratamento compatível ou erro recuperável, sem descarte/execução. Só após essa prova retirar caminhos: UI/DI não os expõem, Console/aggregate continuam funcionais, providers adiados/contratos compartilhados permanecem e abrir coleção apenas prepara texto. Jornadas afetadas mantêm BSON, destino e confirmações.

Cada entrega aponta procedimento, esperado/observado e evidência no snapshot final conforme [validação](../06-validacao-e-evidencias.md). Requisitos afetados permanecem coerentes com [arquitetura](../03-arquitetura-e-invariantes.md), [design system](../04-design-system.md) e [estado](../02-requisitos-e-estado.md).

Separar aceite automatizável de serviços/modelos reais e Desktop nativo; cenário obrigatório pendente mantém a entrega aberta. Não repetir trabalho funcional já aceito sem regressão; completar lacunas do recorte atual.

## Limites

Não incluir novos providers, funcionalidades de Melhorias, administração retirada ou pesquisa completa de modelos. Não enfraquecer segurança/privacidade, fidelidade BSON, ownership LiteDB e isolamento de origem para obter aprovação. A versão acima é alvo documental, sem renumerar artefatos históricos.

Execução corrente: [TODO e evidências por lote](../../Remover/TODO.md). F01-E01 concluída por inspeção; F01-E02 e F01-E03 implementadas e validadas nos lotes L1–L5. F01-E04 permanece aberta para a homologação real/nativa, sem antecipar seu aceite.
