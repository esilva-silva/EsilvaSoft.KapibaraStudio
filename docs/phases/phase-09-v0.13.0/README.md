# Fase 9 — v0.13.0: polimento de código, arquitetura e UI/UX

**Situação:** Planejada. Escopo introduzido na reorganização de **03/10/2026**; nenhuma revisão ou homologação é declarada concluída por este documento.

## Objetivo

Consolidar as entregas funcionais com revisão de código, arquitetura e experiência desktop antes da homologação em ambientes reais.

## Escopo incluído

- Código: corrigir defeitos, remover duplicações e caminhos obsoletos comprovados, revisar tratamento de erros, cancelamento e concorrência.
- Arquitetura: revisar fronteiras entre projetos, contratos e adapters, composição DI, propriedade do LiteDB e persistência aditiva/versionada.
- UI/UX (UX-01/02): revisar consistência com o design system, navegação, foco, atalhos, estados vazios/erro/carregamento, temas e localização.
- Qualidade: revisar cobertura de regressões relevantes, documentação e rastreabilidade entre catálogo, implementação e critérios de aceite.

## Fora de escopo

Novos providers, autenticações paralelas, funcionalidades do backlog, transformação do desktop em site e reimplementação de escopos já aceitos. Homologação com MongoDB/mongosh, modelos, hardware, acessibilidade e diálogos nativos pertence à Fase 10.

## Antecipações técnicas presentes

Há revisões de [adapters do SO](../../architecture/system-adapters.md), [estabilidade dos testes](../../architecture/test-stability.md) e metas visuais do [Agente IA](../phase-05-v0.9.0/README.md). Reutilizar suas evidências e pendências; elas não comprovam uma revisão completa desta fase.

## Critério de aceite

Registrar os achados e as correções com escopo e evidência proporcionais. Os invariantes de navegação, contexto por aba/turno, cancelamento, BSON/UUID, confirmações, auditoria, limites mongosh e recuperação de sessão devem permanecer preservados. Alterações de sessão/contexto exigem testes de falha, concorrência e recuperação; alterações visuais exigem inspeção dos PNGs reais, sem mudar golden files para ocultar regressões.

Restore em modo locked, build e testes aplicáveis devem passar conforme as regras do projeto; benchmarks e medições de latência/alocação permanecem manuais, fora de CI/release. Atualizar ADRs, design system, guia, plano, inventário e acompanhamento quando decisões ou comportamento mudarem. Cada pendência real deve ter destino explícito na Fase 10; falhas automatizáveis permanecem nesta fase.

## Dependências

Consolidação dos escopos funcionais das Fases 1 a 8. A prioridade de entrega continua na Fase 8 e depois na Fase 5; a numeração não determina sozinha a ordem de implementação.

## Documentos relacionados

- [Design system](../../17-design-system-ui-ux.md) · [Arquitetura](../../05-arquitetura.md) · [ADRs](../../10-decisoes-arquiteturais.md)
- [Testes e qualidade](../../08-testes-e-qualidade.md) · [Matriz de validação](../../15-matriz-de-validacao.md) · [Acompanhamento](../../12-acompanhamento-da-implementacao.md)
- [Fase 10 — homologação](../phase-10-v0.14.0/README.md) · [Fase 11 — estabilidade](../phase-11-v1.0.0/README.md)
