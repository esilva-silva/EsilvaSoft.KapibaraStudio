# Fases do EsilvaSoft.KapibaraStudio

Referência: **28/09/2026**. Esta pasta contém o trabalho **planejado ou em execução** por fase. Requisitos concluídos e aceitos são arquivados em [`../done`](../done/README.md); requisitos sem fase definida, adiados ou retirados do escopo ficam em [`../backlog`](../backlog/README.md). Os documentos de planejamento e agentes antigos da Fase 7 foram removidos; o estado atual de Claude está em [`phase-07-v0.11.0/README.md`](phase-07-v0.11.0/README.md).

O [roadmap](../09-plano-de-implementacao.md) permanece na raiz da documentação como índice e regra geral; cada fase detalha o próprio escopo aqui.

O roadmap abaixo preserva a numeração histórica das fases posteriores; a Fase 7 foi retomada para concluir Claude, com documentação atual resumida em [`phase-07-v0.11.0/README.md`](phase-07-v0.11.0/README.md). Consulte o [acompanhamento](../12-acompanhamento-da-implementacao.md) para validação executada.

## Meta transversal concluída

A meta de internacionalização da interface foi concluída no recorte automatizado: `pt-BR`, `en`, `es` e `zh-CN`, com `pt-BR` inicial, fallback determinístico em `en`, persistência aditiva, troca em execução e textos de produto/acessibilidade localizados. Foram verificados 64 PNGs reais nos dois temas e os testes unitários passaram com 2.639 aprovados, 0 falhas e 20 ignorados. Os arquivos `docs/**/*.md` permanecem em português. Revisão linguística de domínio e homologações externas pertencem à Fase 9 / v0.13.0.

## Roadmap oficial

| Fase | Versão | Objetivo | Situação |
| --- | --- | --- | --- |
| 1 | v0.5.0 | MVP: conectar → navegar → consultar → visualizar → editar → exportar | [Arquivada por escopo funcional](phase-01-v0.5.0/README.md) |
| 2 | v0.6.0 | Organização dos projetos e autocomplete básico | [Arquivada](../done/release_v0.6.0/README.md) |
| 3 | v0.7.0 | Autocomplete com IA | [Arquivada](../done/release_v0.7.0/README.md) |
| 4 | v0.8.0 | Abertura e salvamento de arquivos de texto | [Arquivada](../done/release_v0.8.0/README.md) |
| 5 | v0.9.0 | IA local e produtividade contextual | [Experimental](phase-05-v0.9.0/README.md) |
| 6 | v0.10.0 | Administração e manutenção | [Em desenvolvimento](phase-06-v0.10.0/README.md) |
| 7 | v0.11.0 | Claude e integração com agentes | Implementada em código; em validação Windows — [estado e limites](phase-07-v0.11.0/README.md) |
| 8 | v0.12.0 | Chat simples com IA baseado em workflow | [Planejada](phase-08-v0.12.0/README.md) |
| 9 | v0.13.0 | Homologação manual e validação em ambientes reais | [Planejada](phase-09-v0.13.0/README.md) |
| 10 | v1.0.0 | Estabilidade, revisão completa, instalação e atualizações | [Planejada](phase-10-v1.0.0/README.md) |

## Situações

- **Em execução:** fase ativa. É o único escopo que justifica novas entradas na interface.
- **Em desenvolvimento:** existe código integrado antecipando a fase, mas a fase não está ativa. O código é preservado; a interface não o oferece.
- **Planejada:** sem caminho integrado comprometido para o recorte.
- **Experimental:** caminho disponível, com qualidade ou ambiente limitando o uso como compromisso estável.
- **Arquivada:** escopo funcional fechado e movido para `../done/release_vX.Y.Z`; a homologação manual correspondente fica na Fase 9.

## Implementação antecipada não encerra fase

Código existente para uma fase futura é **antecipação técnica**. Ele não transfere a fase para "concluída", não gera entrada na interface e não dispensa o aceite próprio da fase. Cada documento de fase lista suas antecipações em seção separada do escopo.

## Preservação de IDs

Os IDs do [catálogo funcional](../03-catalogo-funcional.md) (CON/DAT/EDT/AGG/IDX/TRF/ADM/ADV/UX) são preservados em qualquer movimentação. O [inventário](../24-inventario-roadmap.md) registra a evidência em código e a localização atual de cada requisito.
