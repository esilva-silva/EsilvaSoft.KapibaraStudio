# Fase 4 — v0.13.0: Instalação e atualização

**Situação:** planejada no novo plano; nenhuma execução ou release declarada por este documento. **Requisitos:** MVP-011, MVP-015.

## Execução por tarefas

[Índice e tarefas da fase](../../../tasks/fase-04-v0.13.0/README.md). Os checklists das tarefas acompanham execução; os requisitos e seu estado continuam no catálogo oficial. Evidências históricas não constituem nova execução.

## Objetivo e dependências

Fechar instalar → atualizar → reabrir e recuperação por modo de instalação.

Dependências: Contratos estabilizados das Fases 1–3; artefatos e ambientes descartáveis disponíveis.

## Entregas TODO

- [ ] F04-E01 — Gerar artefatos limpos por RID com hashes, notices/licenças e SBOM; auditar ausência de CLI/runtime Copilot redistribuídos.
- [ ] F04-E02 — Executar ciclo Windows e Linux portátil com seleção/integridade, falha de download/aplicação e recuperação.
- [ ] F04-E03 — Executar instalação/atualização/remoção gerenciada Linux e comprovar precedência do gerenciador, inclusive falha de marcador.
- [ ] F04-E04 — Comprovar preservação dos dados locais e recuperação de sessão/migração após atualização, sem sobrescrever arquivo ilegível.
- [ ] F04-E05 — Registrar diferenças entre pacote gerado, publicado e homologado; atualizar instruções somente para formatos disponíveis.

## Evidências existentes e pendências

Windows possui seleção/integridade/download parciais; Linux gerenciado possui ciclos de pacote testados em WSL/contêineres e runners, sem homologação desktop integral. O [contrato de distribuição](../07-distribuicao-e-suporte.md) define modos, arquivos, comandos e recuperação; a [baseline](../09-baseline-de-validacao.md) registra ambientes e resultados. Faltam comprovar atualizar/reabrir/recuperar no snapshot final por instalação suportada e auditar artefatos reais.

## Critério de saída

Artefato errado/corrompido é recusado antes de aplicar. Reabrir após update mostra versão efetiva esperada e dados preservados; falha tem recuperação demonstrada. Linux gerenciado nunca sobrescreve arquivos do gerenciador, inclusive root; marcador inválido fica manual. Remoção/purge preserva dados. Pacotes finais têm hashes/SBOM/notices e não contêm CLI/runtime Copilot residual.

Cada entrega aponta procedimento, esperado/observado e evidência no snapshot final conforme [validação](../06-validacao-e-evidencias.md). Requisitos afetados permanecem coerentes com [arquitetura](../03-arquitetura-e-invariantes.md), [design system](../04-design-system.md) e [estado](../02-requisitos-e-estado.md).

Separar aceite automatizável de serviços/modelos reais e Desktop nativo; cenário obrigatório pendente mantém a entrega aberta. Não repetir trabalho funcional já aceito sem regressão; completar lacunas do recorte atual.

## Limites

Não incluir novos providers, funcionalidades de Melhorias, administração retirada ou pesquisa completa de modelos. Não enfraquecer segurança/privacidade, fidelidade BSON, ownership LiteDB e isolamento de origem para obter aprovação. A versão acima é alvo documental, sem renumerar artefatos históricos.
