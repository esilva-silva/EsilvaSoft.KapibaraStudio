# Documentação oficial — EsilvaSoft.KapibaraStudio

**MongoDB Database Management & Development Tool.** Studio desktop em .NET 10/Avalonia, para Windows e Linux, sob licença MIT. Referência documental reorganizada em **06/11/2026**.

## Comece por aqui

| Divisão | Finalidade | Documento de trabalho |
| --- | --- | --- |
| [MVP](MVP/README.md) | Manter a gestão cotidiana de bancos e concluir o desenvolvimento assistido | [Requisitos e estado](MVP/02-requisitos-e-estado.md) · [Fases](MVP/fases/README.md) |
| [Remover](Remover/README.md) | Executar futuramente os cortes aprovados, preservando capacidades compartilhadas | [Inventário técnico](Remover/inventario-tecnico.md) · [TODOs](Remover/TODO.md) |
| [Melhorias](Melhorias/README.md) | Conceitos de evolução após o MVP, sem compromisso de versão | [Conceitos](Melhorias/conceitos.md) |

O recorte deriva dos três tópicos de [mvp-status.md](../docs/mvp-status.md). A decisão posterior preserva Claude, Codex, APIs externas e MCP externo em Melhorias, com código e disponibilidade atuais. Os demais cortes do tópico 2 são decisões para execução futura, não remoções já realizadas.

## Precedência e manutenção

1. `docs-new` é a referência oficial de escopo, fases e decisões para trabalho futuro. As [decisões vigentes](MVP/08-decisoes-vigentes.md) consolidam contratos obrigatórios; instruções em [AGENTS.md](../AGENTS.md) continuam aplicáveis.
2. [docs](../docs/README.md) preserva o histórico, ADRs de origem e evidências datadas. A numeração de fases antiga não orienta a nova sequência. Em caso de conflito de planejamento, prevalece `docs-new`; evidência antiga não muda de data nem de alcance.
3. Documentação normativa não comprova implementação. O catálogo novo separa implementação de validação e deve apontar ao código/registro correspondente. Código divergente vira lacuna explícita, não alegação de comportamento entregue.
4. Os cortes não autorizam apagar configurações, credenciais, rascunhos ou histórico. Compatibilidade, segurança e recuperação fazem parte da entrega futura.
5. Atualizar estado em um único lugar: [catálogo MVP](MVP/02-requisitos-e-estado.md). Fases descrevem entregas residuais e referenciam seus IDs; o inventário de remoção e os conceitos de melhoria têm IDs próprios.

Esta reorganização só cria documentação e encaminhamentos. Nenhuma funcionalidade, permissão, dependência, versão de binário, tag ou pacote foi alterada; nenhum teste da aplicação foi executado para comprovar capacidades novas.

## Entregas do novo plano

| Fase nova | Versão-alvo | Resultado |
| --- | --- | --- |
| 01 | v0.10.0 | Limpeza aprovada, compatibilidade e base do Studio |
| 02 | v0.11.0 | Autocomplete com IA e agente local, CPU como referência |
| 03 | v0.12.0 | GitHub Copilot e critérios residuais |
| 04 | v0.13.0 | Instalação, atualização e recuperação |
| 05 | v0.14.0 | Polimento e homologação do recorte |
| 06 | v1.0.0 | Aceite estável do MVP e preparação da distribuição |

Versões são alvos deste plano, não declarações de release. Não renumerar tags, pacotes, testes ou evidências históricas que já usam esses números. Identificar referências como **fase nova** ou **fase histórica**, acompanhadas de link. Nenhuma fase nova está concluída pela publicação destes documentos.

## Mapa de migração das fases

| Origem histórica | Destino oficial e tratamento |
| --- | --- |
| Fases 1–4 / v0.5.0–v0.8.0 | MVP: base existente, arquivos e autocomplete. Reutilizar aceites de [done](../docs/done/README.md), sem reimplementar entregas; validar lacunas nas fases novas pertinentes. |
| Fase 5 / v0.9.0 | Fase nova 03: Copilot. Demais providers permanecem em Melhorias; infraestrutura compartilhada continua no MVP. |
| Fase 6 / v0.10.0 | Base da fase nova 01: coleções, índices, views, validadores e transferência. Administração pesada vai para Remover; preservar o aceite histórico do Marco A. |
| Fase 7 / v0.11.0 | Remover: cancelar a meta de chat separado por workflow, sem excluir o painel Agente IA. |
| Fases 7A, 7B e 8 / v0.12.0 | Fase nova 02: contratos e avaliação necessários à IA local. Pesquisa adicional, hardware e expansão do KapiLab em Melhorias. |
| Fases 9 e 10 / v0.13.0–v0.14.0 | Fase nova 05: polimento e consolidação da homologação; critérios essenciais também acompanham cada entrega anterior. |
| Fase 11 / v1.0.0 | Fase nova 04 para instalação/update; fase nova 06 para aceite estável. |
| Subfases de `auto-complite` | Requisitos do autocomplete no MVP; sugestões automáticas com IA vão para Remover. Não confundir sua numeração histórica com as fases do produto. |
| Backlogs 01–07 | Distribuídos por capacidade entre Remover e Melhorias; itens básicos já usados permanecem no MVP. Não excluir serviços compartilhados por pertencerem a um documento de backlog. |

## Mapa da documentação de base

| Fontes históricas | Consolidação oficial |
| --- | --- |
| Visão, mercado/experiência, catálogo e inventário (01–03, 24) | [Produto e escopo](MVP/01-produto-e-escopo.md) e [requisitos e estado](MVP/02-requisitos-e-estado.md) |
| Compatibilidade, arquitetura, segurança/administração (04, 05, 07), `architecture` e ADRs (10) | [Arquitetura e invariantes](MVP/03-arquitetura-e-invariantes.md), [decisões](MVP/08-decisoes-vigentes.md) e inventário de remoção |
| Design system, identidade e paletas (17, 18, `ui`) | [Design system](MVP/04-design-system.md); assets e capturas históricos permanecem na origem |
| Editor/BSON/UUID, transferência, guia, Explorer, Console e highlighting (06, 13, 14, 19, 20, 22) | [Guia de uso](MVP/05-guia-de-uso.md), requisitos e invariantes |
| Autocomplete, ONNX, multimodelo (21, 23, 26), `auto-complite`, KapiCoder e KapiLab | MVP para capacidades essenciais e fase nova 02; Melhorias para extensões; Remover para IA automática legada |
| Qualidade, fontes, matriz, checklist e acompanhamento (08, 11, 12, 15, 16), `done` e aceites de fases | [Validação e evidências](MVP/06-validacao-e-evidencias.md); registros originais conservam data, ambiente e limites |
| Roadmap (09), índices de fases/backlog e metas antigas | Fases novas, inventário de remoção e conceitos de melhoria; cronologia fica em `docs` |
| Revisões de pacotes, updater e licenças em `architecture` | [Distribuição e suporte](MVP/07-distribuicao-e-suporte.md) |

## Como ler e atualizar

Os arquivos novos são Markdown, navegáveis por estes índices no repositório ou no editor. O [leitor HTML antigo](../docs/index.html) continua destinado ao histórico: não indexa `docs-new`. Ao alterar documentos históricos, regenerar seu snapshot com `node scripts/build-docs-index.cjs`.

Manter links relativos com caixa exata, IDs estáveis e TODOs verificáveis. Não duplicar cronologias, marcar tarefas de código como feitas por revisão documental ou ampliar o compromisso do MVP com conceitos de Melhorias. Registrar a evidência real e seus limites antes de mudar um estado para concluído.
