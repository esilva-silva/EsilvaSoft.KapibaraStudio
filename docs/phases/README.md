# Fases do EsilvaSoft.KapibaraStudio

Referência: **03/10/2026**. Esta pasta contém o trabalho **planejado ou em execução** por fase. Requisitos concluídos e aceitos são arquivados em [`../done`](../done/README.md); requisitos sem fase definida, adiados ou retirados do escopo ficam em [`../backlog`](../backlog/README.md). A Fase 5 concentra Copilot e infraestrutura necessária; o estado de Claude e das demais integrações externas está no [bkl-06](../backlog/bkl-06-integracoes-agentes.md).

O [roadmap](../09-plano-de-implementacao.md) permanece na raiz da documentação como índice e regra geral; cada fase detalha o próprio escopo aqui.

**Prioridade de entregas (03/10/2026):** Fase 8 / v0.12.0 primeiro; Fase 5 / v0.9.0 depois. IA local e produtividade contextual recebem o foco atual; a integração de agentes conserva a implementação e os gates abertos. Consulte o [acompanhamento](../12-acompanhamento-da-implementacao.md) para evidências datadas e o mapa de reorganização abaixo para origem e destino.

## Meta transversal concluída

A meta de internacionalização da interface foi concluída no recorte automatizado: `pt-BR`, `en`, `es` e `zh-CN`, com `pt-BR` inicial, fallback determinístico em `en`, persistência aditiva, troca em execução e textos de produto/acessibilidade localizados. Foram verificados 64 PNGs reais nos dois temas e os testes unitários passaram com 2.639 aprovados, 0 falhas e 20 ignorados. Os arquivos `docs/**/*.md` permanecem em português. Revisão linguística de domínio e homologações externas pertencem à Fase 10 / v0.14.0.

## Roadmap oficial

| Fase | Versão | Objetivo | Situação |
| --- | --- | --- | --- |
| 1 | v0.5.0 | MVP: conectar → navegar → consultar → visualizar → editar → exportar | Escopo funcional concluído e [arquivado](../done/release_v0.5.0/README.md); homologação manual na Fase 10 / v0.14.0 |
| 2 | v0.6.0 | Organização dos projetos e autocomplete básico | Escopo funcional concluído e [arquivado](../done/release_v0.6.0/README.md); homologação manual na Fase 10 |
| 3 | v0.7.0 | Autocomplete com IA | Escopo funcional concluído e [arquivado](../done/release_v0.7.0/README.md); sugestões revisáveis, sem aplicação automática |
| 4 | v0.8.0 | Abertura e salvamento de arquivos de texto | Escopo funcional concluído e [arquivado](../done/release_v0.8.0/README.md); inclui workspace local de uma pasta |
| 5 | v0.9.0 | GitHub Copilot e integração com agentes | Adiada até concluir as entregas prioritárias da Fase 8; concluir Copilot e infraestrutura necessária; demais integrações no [bkl-06](../backlog/bkl-06-integracoes-agentes.md); gates abertos em [estado e limites](phase-05-v0.9.0/README.md) |
| 6 | v0.10.0 | Administração e manutenção | [Marco A concluído](phase-06-v0.10.0/README.md): acesso ao servidor pela connection string; coleções, views, validação, índices, estatísticas, usuários/papéis e transferência lógica. RBAC restrito, topologias/nativos e multiplataforma no Marco B manual da Fase 10; SO/startup/API externa no [bkl-07](../backlog/bkl-07-administracao-fora-da-connection-string.md) |
| 7 | v0.11.0 | Chat simples com IA baseado em workflow | [Planejada](phase-07-v0.11.0/README.md): fluxo predefinido, escopo limitado e ações controladas |
| 7A | Vinculada à v0.12.0; sem release própria | Preparação KapiCoder-Mongo | [Planejada](phase-07a-kapicoder-mongo/README.md): contratos, agente local, suporte ao treino e NPU condicional; decisões aprovadas na ADR-070, sem implementação nesta meta |
| 7B | Vinculada à v0.12.0; ferramenta de desenvolvimento | KapiLab: contratos e avaliação pelo caminho da IDE | [Planejada](phase-07b-kapilab/README.md): marcos K0–K4, 25 tarefas, dependências da 7A e benchmarks manuais; proposta ADR-071 |
| 8 | v0.12.0 | IA local e produtividade contextual | **Prioridade atual de entrega**; recorte automatizado implementado, entregas seguintes conforme [meta](phase-08-v0.12.0/meta-de-implementacao.md); ONNX real permanece experimental |
| 9 | v0.13.0 | Polimento de código, arquitetura e UI/UX | [Planejada](phase-09-v0.13.0/README.md): revisão transversal e correção de problemas, preservando os contratos existentes |
| 10 | v0.14.0 | Homologação manual e validação em ambientes reais | [Planejada](phase-10-v0.14.0/README.md): plataformas, acessibilidade, MongoDB/mongosh, hardware, instalação e atualizações reais |
| 11 | v1.0.0 | Estabilidade, revisão completa, instalação e atualizações | [Planejada](phase-11-v1.0.0/README.md): release estável após aceites funcionais, polimento da Fase 9 e homologação da Fase 10 |

## Reorganização de 03/10/2026

| Escopo | Origem | Destino atual |
| --- | --- | --- |
| Copilot e infraestrutura de agentes necessária | Fase 7 / v0.11.0 → Fase 5 | [Fase 5 / v0.9.0](phase-05-v0.9.0/README.md) |
| Claude, Codex, APIs externas e integração MCP externa | Fase 7 / v0.11.0 → Fase 5 | [Backlog bkl-06](../backlog/bkl-06-integracoes-agentes.md), sem fase/versão; implementações intactas |
| Chat simples por workflow | Fase 8 / v0.12.0 | [Fase 7 / v0.11.0](phase-07-v0.11.0/README.md) |
| IA local e produtividade contextual | Fase 5 / v0.9.0 | [Fase 8 / v0.12.0](phase-08-v0.12.0/README.md) |
| Polimento de código, arquitetura e UI/UX | Novo escopo transversal | [Fase 9 / v0.13.0](phase-09-v0.13.0/README.md) |
| Homologação manual e ambientes reais | Fase 9 / v0.13.0 | [Fase 10 / v0.14.0](phase-10-v0.14.0/README.md) |
| Estabilidade, instalação e atualizações | Fase 10 / v1.0.0 | [Fase 11 / v1.0.0](phase-11-v1.0.0/README.md) |

Fases 1–4 e seus arquivos em `done/` permanecem arquivados por escopo funcional; administração continua na Fase 6. As pastas e os links acompanham os novos destinos. Referências de fase nos documentos operacionais foram alinhadas ao mapa atual; registros datados de decisões e testes conservam seu caráter histórico. IDs `P7-*` e `F5-*`, IDs funcionais, datas, contagens, versões de CLI, tags e pacotes já gerados permanecem como evidência original. A numeração das sub-fases de autocomplete é independente.

Esta mudança organiza o planejamento e não publica versões, altera binários, concede permissões ou encerra gates. A v1.0.0 depende dos aceites funcionais, do polimento da Fase 9 e da homologação da Fase 10. A disponibilidade já existente de cada provider segue seu documento; adiar sua fase não desativa a implementação.

## Situações

- **Marco A concluído:** implementação conectada e aceite automatizável aprovados; homologação manual/native explicitamente adiada segue na Fase 10, sem afirmar suporte não comprovado.
- **Em execução:** fase ativa. É o único escopo que justifica novas entradas na interface.
- **Em desenvolvimento:** existe código integrado antecipando a fase, mas a fase não está ativa. O código é preservado; a interface não o oferece.
- **Planejada:** sem caminho integrado comprometido para o recorte.
- **Experimental:** caminho disponível, com qualidade ou ambiente limitando o uso como compromisso estável.
- **Arquivada:** escopo funcional fechado e movido para `../done/release_vX.Y.Z`; a homologação manual correspondente fica na Fase 10.

## Implementação antecipada não encerra fase

Código existente para uma fase futura é **antecipação técnica**. Ele não transfere a fase para "concluída", não gera entrada na interface e não dispensa o aceite próprio da fase. Cada documento de fase lista suas antecipações em seção separada do escopo.

## Preservação de IDs

Os IDs do [catálogo funcional](../03-catalogo-funcional.md) (CON/DAT/EDT/AGG/IDX/TRF/ADM/ADV/UX) são preservados em qualquer movimentação. O [inventário](../24-inventario-roadmap.md) registra a evidência em código e a localização atual de cada requisito.
