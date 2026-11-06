# Fase 5 — v0.14.0: Uso diário e homologação

**Situação:** planejada no novo plano; nenhuma execução ou release declarada por este documento. **Requisitos:** MVP-001..015.

## Objetivo e dependências

Consolidar evidência real Windows/Linux e corrigir defeitos do recorte escolhido.

Dependências: Entregas funcionais e atualização verificadas nas fases anteriores.

## Entregas TODO

- [ ] F05-E01 — Fixar matriz de SO/RID/instalação/MongoDB/autenticação/topologia e rastrear cenários aplicáveis por requisito.
- [ ] F05-E02 — Executar conectar → consultar → editar → salvar/exportar com MongoDB descartável e confirmar BSON/UUID/conflitos/permissões.
- [ ] F05-E03 — Executar falhas, concorrência, cancelamento e recuperação por aba/conversa, incluindo persistência ilegível.
- [ ] F05-E04 — Validar teclado/layouts/IME, foco, leitor de tela, clipboard e diálogos em desktops nativos, com PNGs reais quando houver mudança visual.
- [ ] F05-E05 — Consolidar modelo CPU, Copilot oficial e atualização por ambiente; corrigir defeitos e registrar limites de suporte sem inferências.

## Evidências existentes e pendências

[Matriz](../../../docs/15-matriz-de-validacao.md), [checklist](../../../docs/16-checklist-homologacao.md) e [F6 real](../../../docs/phases/phase-06-v0.10.0/meta-homologacao-finalizacao.md). Faltam consolidar snapshot final, ambientes gráficos, acessibilidade e permissões/topologias declarados.

## Critério de saída

Em cada ambiente declarado, conectar/consultar/editar/salvar/exportar atinge destino esperado. Conflito não sobrescreve silenciosamente; projeção não vira edição integral. Trocar aba/Explorer durante await não redireciona resposta. Sessão ilegível permanece recuperável. Teclado/IME/leitor de tela/diálogos completam jornadas. Toda alegação de suporte aponta cenário real aprovado; cenário aplicável falho bloqueia seu aceite.

Cada entrega aponta procedimento, esperado/observado e evidência no snapshot final conforme [validação](../06-validacao-e-evidencias.md). Requisitos afetados permanecem coerentes com [arquitetura](../03-arquitetura-e-invariantes.md), [design system](../04-design-system.md) e [estado](../02-requisitos-e-estado.md).

Separar aceite automatizável de serviços/modelos reais e Desktop nativo; cenário obrigatório pendente mantém a entrega aberta. Não repetir trabalho funcional já aceito sem regressão; completar lacunas do recorte atual.

## Limites

Não incluir novos providers, funcionalidades de Melhorias, administração retirada ou pesquisa completa de modelos. Não enfraquecer segurança/privacidade, fidelidade BSON, ownership LiteDB e isolamento de origem para obter aprovação. A versão acima é alvo documental, sem renumerar artefatos históricos.
