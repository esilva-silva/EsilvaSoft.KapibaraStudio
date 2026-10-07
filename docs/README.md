# Documentação do EsilvaSoft.KapibaraStudio

**Recorte da Fase 5 — 03/10/2026:** GitHub Copilot e infraestrutura compartilhada necessária à sua conclusão permanecem na Fase 5 / v0.9.0. Claude, Codex, APIs externas e integração de clientes MCP externos passam ao [bkl-06](backlog/bkl-06-integracoes-agentes.md), sem fase/versão comprometida. Código, disponibilidade, permissões e gates existentes são preservados; IA local e workflow mantêm suas fases. Esta alteração é documental e não comprova conclusão funcional ou nova homologação.

Referência: **03/10/2026**. Produto desktop .NET 10/Avalonia, Windows/Linux, LiteDB e MIT. A integração Claude já está no código e segue em validação; a [documentação do Claude no backlog](backlog/integracoes-agentes/historico-integracoes.md#plano-e-andamento) registra modalidades, segurança, limites e homologação pendente. A homologação real com Claude Pro não foi executada nesta revisão.

## Comece por aqui

Abra [o índice HTML](index.html) para navegar pelo índice lateral e ler documentos, tabelas e exemplos no painel principal. O leitor acompanha o tema do sistema e funciona sem internet. Links entre Markdown permanecem no próprio painel.

Ao editar, adicionar ou mover arquivos `.md`, execute `node scripts/build-docs-index.cjs` na raiz do repositório para atualizar a cópia de leitura local. Quando servido por HTTP, o leitor busca o conteúdo atual dos arquivos; a lista de documentos continua sendo gerada por esse comando. O renderizador Marked e sua licença MIT estão em `docs/assets/`.

1. [Fases](phases/README.md): as onze fases oficiais, com escopo, aceite e situação de cada uma.
2. [Roadmap por versão](09-plano-de-implementacao.md): índice das fases e as regras que separam antecipação, fase ativa, backlog e release arquivada.
3. [Backlog](backlog/README.md): requisitos sem fase definida, adiados ou retirados do escopo atual.
4. [Releases arquivadas](done/README.md): escopo concluído por versão, com as pendências de homologação ainda abertas.
5. [Inventário de implementação](24-inventario-roadmap.md) e [catálogo funcional](03-catalogo-funcional.md): o que existe em código e os IDs de cada requisito.
6. [Guia de uso](14-guia-de-uso.md): operações realmente disponíveis na interface.
7. [Matriz de validação](15-matriz-de-validacao.md) e [checklist de homologação](16-checklist-homologacao.md): separar teste automatizado de servidor, SO, hardware e UI nativos.

✅ Implementado indica o recorte descrito no código, não uma release homologada. 🚧 Em desenvolvimento indica implementação parcial; 📋 Planejado, ausência de caminho integrado; 🧪 Experimental, limites de qualidade/ambiente. Itens amplos do catálogo só fecham quando todo seu aceite aplicável tem evidência.

## Estado consolidado do checkout

A [reorganização de 03/10/2026](phases/README.md#reorganização-de-03102026) define onze fases, com prioridade na Fase 8 / v0.12.0 e retomada da Fase 5 / v0.9.0 depois. Polimento pertence à Fase 9, homologação à Fase 10 e estabilidade à Fase 11. As versões-alvo documentais não alteram tags ou pacotes já produzidos.

Os recortes funcionais v0.5.0–v0.8.0 estão arquivados, mas seus gates de plataforma, acessibilidade, diálogos nativos, topologias MongoDB e edição real continuam abertos. A IA local da v0.12.0 é experimental; a administração/manutenção da v0.10.0 segue em desenvolvimento. O Claude Code, o provider de chat, streaming, retomada e a ponte MCP já estão implementados em Windows; a homologação real com conta e servidor continua pendente. Consulte [Claude no KapibaraStudio — backlog](backlog/integracoes-agentes/historico-integracoes.md#plano-e-andamento).

A internacionalização da interface está concluída no recorte automatizado para `pt-BR`, `en`, `es` e `zh-CN`, com `pt-BR` inicial e fallback em `en`; os arquivos `docs/**/*.md` permanecem em português. A evidência histórica de fechamento da v0.8.0 é build sem avisos/erros, **2.674 testes unitários aprovados, 0 falhas e 20 ignorados**, além de **43 benchmarks aprovados**. Essa evidência não valida recursos MCP ou providers ainda planejados.

## Estrutura da documentação

| Pasta | Conteúdo |
| --- | --- |
| Raiz de `docs/` | Documentos transversais a várias fases: governança, arquitetura, design system, segurança, qualidade e validação. |
| [`phases/`](phases/README.md) | Trabalho planejado ou em execução, uma pasta por fase e versão. |
| [`backlog/`](backlog/README.md) | Requisitos sem fase, adiados ou retirados do escopo. Código preservado; o bkl-06 mantém composição e disponibilidade existentes. |
| [`done/`](done/README.md) | Requisitos concluídos, arquivados por versão, com as pendências de homologação explícitas. |
| [`auto-complite/`](auto-complite/README.md) | Subsistema de autocomplete. Suas "fases" têm numeração própria e não correspondem às fases do produto. |
| `ui/`, `models/`, `legado/`, `assets/` | Paletas e capturas, exemplos de manifesto de modelo, imagens legadas e o runtime do leitor HTML. |

## Documentos por assunto

| Assunto | Documentação |
| --- | --- |
| Produto e UX | [01 — Visão/escopo](01-visao-e-escopo.md), [02 — Experiência](02-mercado-e-experiencia.md), [14 — Guia de uso](14-guia-de-uso.md), [17 — Design system](17-design-system-ui-ux.md), [18 — Identidade visual](18-identidade-visual.md) |
| Contratos e arquitetura | [04 — Compatibilidade](04-compatibilidade-e-capacidades.md), [05 — Arquitetura](05-arquitetura.md), [10 — ADRs](10-decisoes-arquiteturais.md) |
| Dados e operação | [06 — Editor/BSON/UUID](06-editor-bson-e-uuid.md), [07 — Segurança/administração](07-dados-seguranca-e-administracao.md), [13 — Transferência lógica](13-exportacao-logica.md) |
| Editor e exploração | [19 — Explorer](19-database-explorer.md), [20 — Console](20-console.md), [22 — Highlighting](22-syntax-highlighting.md) |
| Inteligência opcional | [21 — Autocomplete local](21-autocomplete-local.md), [23 — ONNX/chat](23-onnx-slopcoder.md), [26 — IA local multimodelo](26-ia-local-multimodelo.md), [Autocomplete MongoDB — padrão e plano](auto-complite/README.md) |
| Claude, autenticação e ferramentas | [Claude no KapibaraStudio](backlog/integracoes-agentes/historico-integracoes.md#plano-e-andamento) |
| Segurança das ferramentas de agentes | [Metadados, índices e saídas já executadas — ADR-066](architecture/agent-tool-safety.md) |
| Planejamento e histórico | [03 — Catálogo funcional](03-catalogo-funcional.md), [09 — Roadmap](09-plano-de-implementacao.md), [12 — Acompanhamento](12-acompanhamento-da-implementacao.md), [24 — Inventário](24-inventario-roadmap.md) |
| Qualidade e validação | [08 — Testes](08-testes-e-qualidade.md), [11 — Fontes/evidências](11-fontes-e-evidencias.md), [15 — Matriz de validação](15-matriz-de-validacao.md), [16 — Checklist de homologação](16-checklist-homologacao.md) |
| Licenças dos pacotes Windows | [Revisão dos ZIPs e runtimes — 03/10/2026](architecture/windows-license-review-2026-10-03.md) |

## Rastreabilidade e manutenção

Usar os IDs CON/DAT/EDT/AGG/IDX/TRF/ADM/ADV/UX e a versão alvo em issues, PRs e critérios de teste. Os 68 IDs originais permanecem; EDT-08 torna explícito o requisito de formatação do MVP. Para mudança de comportamento, atualizar catálogo, inventário, roadmap, fase correspondente, guia e decisões afetadas.

Cada requisito tem **uma** localização principal: fase, backlog ou release arquivada. Movimentações são registradas no [acompanhamento](12-acompanhamento-da-implementacao.md), com origem e destino.

O roadmap substitui F0–F7 e também a numeração anterior de seis fases: referências antigas em registros datados são históricas. Recursos avançados já presentes são antecipações mantidas, não trabalho a repetir, e não aparecem na interface enquanto sua fase não estiver ativa. Datas e contagens antigas são evidência da revisão indicada, não validação automática do checkout atual.
