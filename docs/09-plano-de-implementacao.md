# Roadmap de evolução do Kapibara Studio

**Qualidade — 01/10/2026:** benchmarks são instrumentos manuais de desenvolvimento, fora dos gates de CI/release. A matriz automatizada mantém as duas suítes unitárias em Windows/Linux, guarda estática de fontes nos dois sistemas e integração Windows. [Revisão de estabilidade e limites](architecture/test-stability.md). A leitura estrutural de fontes no Linux não amplia a meta para testes de runtime/conexões reais.

**Release/Copilot — 01/10/2026:** implementados runtime SDK externo ao single-file, CLI oficial de login incluída e origem única de auto update. Quatro pacotes locais gerados; conta oficial reconhecida no publish Windows sem CLI no PATH. Publicação remota, OAuth novo pela UI e execução Linux/ARM64 permanecem pendentes. [Revisão](architecture/release-copilot.md).

**P7-UX — entregas de implementação e validação automatizada concluídas em 30/09/2026:** [P7-UX — painel Agente IA](phases/phase-07-v0.11.0/meta-ui-ux-painel-agente-ia.md). Composição, conversa, compositor e estados reorganizados; build e suíte completa aprovados, PNGs Avalonia inspecionados. Homologação nativa de teclado/leitor de tela/IME e Windows/Linux permanece pendente; não fecha gates de providers.

**P7-AUTH — implementado em 30/09/2026:** [inicialização automática e contratos de conta](phases/phase-07-v0.11.0/meta-inicializacao-autenticacao-agentes.md) padroniza o reconhecimento da conta oficial já existente e o dispatch de integrações por contratos. Copilot é a primeira checagem automática elegível no Windows; políticas que podem exibir diálogo permanecem explícitas. Build e UnitTests passaram; homologação nativa de CLI/cofre continua pendente por SO. Não altera os gates da Fase 7.

## Meta transversal ativa — adapters do SO e isolamento unitário (30/09/2026)

O pedido de arquitetura exige completar a migração de todos os acessos reais ao SO para adapters em projeto separado e substituir seu uso unitário por interfaces com doubles. Console STDIO MCP, segredos Windows/Linux, broker/socket, paths, launcher, diagnóstico, owner LiteDB, catálogo/cache de modelos, exportação e atualização passam por adapters em `Infrastructure.System`; ONNX está isolado em `Infrastructure.LocalAi.OnnxAdapter`. Na verificação integrada de 30/09, restore locked passou offline, build CPU teve 0 avisos/erros e a suíte completa aprovou Benchmarks 41/41, Infrastructure.Agents.Tests 198/198, UnitTests 3323 aprovados/20 ignorados e IntegrationTests 868 aprovados/14 ignorados. Builds cruzados `linux-x64` de Desktop/McpServer passaram. Runtime/testes nativos em Linux e homologações explícitas dos adapters continuam pendentes por falta de distribuição Linux neste host. Critérios completos, sequência e inventário em [system-adapters](architecture/system-adapters.md). Esta meta não redefine nem encerra os gates funcionais do roadmap.

Referência: **29/09/2026**. Este plano substitui o cronograma antigo F0–F7 e as numerações anteriores de seis e nove fases. As fases são compromissos de consolidação, não a ordem em que todo código foi escrito. Recursos antecipados continuam disponíveis com seus limites; sua existência não encerra uma fase.

**Versão atual identificável:** a última release/tag publicada continua sendo v0.8.0. O estado de desenvolvimento está na meta v0.11.0, com Copilot por assinatura liberado de forma limitada no Windows; o próximo artefato deve ser pré-release até que os gates de homologação sejam fechados. A homologação manual ampla permanece na Fase 9; builds locais não implicam publicação remota. As tags históricas não foram alteradas.

## Status e evidências

- ✅ **Implementado:** caminho concreto no código para o recorte descrito; não significa homologação em todas as plataformas/topologias.
- 🚧 **Em desenvolvimento:** implementação parcial ou critérios essenciais pendentes, explicitados junto do status.
- 📋 **Planejado:** sem caminho integrado identificado para o recorte.
- 🧪 **Experimental:** caminho disponível, mas qualidade ou ambiente limita seu uso como compromisso estável.

O [catálogo](03-catalogo-funcional.md) mantém IDs e status do requisito completo; o [inventário de código](24-inventario-roadmap.md) discrimina recortes existentes e lacunas. A [matriz](15-matriz-de-validacao.md) registra testes executados e o [checklist](16-checklist-homologacao.md) mantém verificações externas. As metas que exigem ambiente, hardware ou operação humana foram concentradas na [Fase 9 / v0.13.0](phases/phase-09-v0.13.0/README.md); nenhuma validação textual equivale à publicação de uma release ou ao encerramento dos requisitos amplos do catálogo.

## Meta transversal — internacionalização da interface — concluída no recorte automatizado

A interface desktop suporta `pt-BR`, `en`, `es` e `zh-CN`. `pt-BR` permanece o idioma inicial para preservar a experiência existente; valores ausentes ou inválidos e chaves sem tradução resolvem para `en`, e uma chave também ausente no inglês aparece como `[[chave]]`. A preferência é persistida de forma aditiva na sessão, sem regravar rascunhos, resultados ou credenciais. Os arquivos `docs/**/*.md` continuam deliberadamente em português.

O incremento cobre catálogo, barra principal, Explorer, editor/resultados, ferramentas, conexões, ambientes, histórico, autocomplete/IA, acessibilidade, mensagens de operação e inicialização com restauração do idioma antes das operações do workspace. A matriz visual gera 64 PNGs reais nos quatro idiomas e nos dois temas. A meta de implementação e tradução está concluída: os testes unitários e a evidência visual estão verdes. Revisão linguística de domínio e validação integrada oficial com MongoDB permanecem como homologação externa das fases, não como bloqueio do catálogo de idiomas.

| Fase | Versão | Objetivo | Situação |
| --- | --- | --- | --- |
| 1 | v0.5.0 | MVP: conectar → navegar → consultar → visualizar → editar → exportar | [Escopo funcional implementado](phases/phase-01-v0.5.0/README.md); validação manual transferida para a [Fase 9 / v0.13.0](phases/phase-09-v0.13.0/README.md) |
| 2 | v0.6.0 | Organização dos projetos e autocomplete básico | [Arquivada](done/release_v0.6.0/README.md). Núcleos separados e autocomplete determinístico aceitos no recorte funcional |
| 3 | v0.7.0 | Autocomplete com IA | [Arquivada](done/release_v0.7.0/README.md). Sugestões assistidas por modelo local, sempre revisáveis e sem aplicação automática |
| 4 | v0.8.0 | Abertura e salvamento de arquivos de texto e workspace local de uma pasta | [Arquivada](done/release_v0.8.0/README.md). Inclui Abrir/Salvar/Salvar como, painel Arquivos com raiz única e operações de arquivo/pasta |
| 5 | v0.9.0 | IA local e produtividade contextual | Escopo automatizável implementado; [experimental](phases/phase-05-v0.9.0/README.md) até inferência real/homologação da Fase 9. Inclui prévia de contexto, propostas revisáveis, IME e gate Headless de latência |
| 6 | v0.10.0 | Administração e manutenção | [Em desenvolvimento](phases/phase-06-v0.10.0/README.md). Coleções, views, validação, índices, estatísticas, usuários, papéis e exportação/importação lógica |
| 7 | v0.11.0 | Claude e integração com agentes | Claude Code e GitHub Copilot por assinatura; Copilot com liberação limitada no Windows e homologação incompleta. [Estado, limites e roteiro](phases/phase-07-v0.11.0/README.md) |
| 8 | v0.12.0 | Chat simples com IA baseado em workflow | [Planejada](phases/phase-08-v0.12.0/README.md). Fluxo predefinido, escopo limitado e ações controladas |
| 9 | v0.13.0 | Homologação manual e validação em ambientes reais | [Planejada](phases/phase-09-v0.13.0/README.md). Consolida plataformas, acessibilidade, MongoDB/mongosh, hardware, instalação e atualização reais |
| 10 | v1.0.0 | Estabilidade, revisão completa, instalação e atualizações | [Planejada](phases/phase-10-v1.0.0/README.md). Release estável após os aceites funcionais e a Fase 9 |

O detalhamento de cada fase — escopo, fora de escopo, antecipações, aceite e dependências — fica em [`phases/`](phases/README.md). Este documento permanece como índice e regra geral.

A inserção da nova v0.11.0 preservou integralmente o escopo anterior de workflow na v0.12.0 e deslocou a homologação já existente para v0.13.0; a estabilidade continua v1.0.0, agora Fase 10. O [registro de migração](phases/phase-07-v0.11.0/README.md) mantém origem, destino, inventário e limites. Nenhum requisito ou gate foi encerrado pela renumeração.

## Implementação antecipada, fase ativa, backlog e release arquivada

Quatro estados distintos, que não devem ser confundidos:

- **Fase ativa** — o recorte automatizável da Fase 5 / v0.9.0 está concluído; a fase funcional em desenvolvimento é a [Fase 6 / v0.10.0](phases/phase-06-v0.10.0/README.md). A Fase 5 continua experimental até homologação real aplicável na Fase 9.
- **Implementação antecipada** — código integrado que pertence a uma fase futura. Existe, é preservado e continua testado, mas **não** encerra a fase à qual pertence, **não** conta como escopo concluído da fase atual e **não** aparece na interface. Exemplos atuais: administração (v0.10.0), agregação e Script Engine (sem fase), IA local (v0.9.0).
- **Backlog** — requisito sem fase definida, adiado ou retirado do escopo. Ver [`backlog/`](backlog/README.md). Entrar no backlog não apaga código: a implementação é preservada e isolada, apenas sem ponto de entrada visual.
- **Release arquivada** — versão cujo escopo funcional foi fechado e movido para [`done/`](done/README.md). Arquivar **não** significa homologar; a validação manual aplicável pertence à Fase 9.

Recursos fora da fase podem continuar presentes no código, desativados ou experimentais. Sua existência não é compromisso de suporte.

## Requisitos sem versão comprometida

O backlog de requisitos sem fase atribuída está em [`backlog/bkl-05-requisitos-sem-fase.md`](backlog/bkl-05-requisitos-sem-fase.md), preservando os IDs do catálogo. Nenhum item ali bloqueia automaticamente a v1.0.0.

## Ordem de trabalho e definição de pronto

1. Manter a Fase 5 / v0.9.0 com seu aceite automatizado registrado; transferir validação de inferência/hardware real para a Fase 9.
2. Avançar pelas fases 6, 7 e 8 na ordem do roadmap, reutilizando implementações antecipadas e preservando proteções. A meta atual da v0.11.0 entrega somente análise, contratos e plano; não ativa nova fase na interface.
3. Executar a Fase 9 / v0.13.0, que concentra a homologação manual e a validação em ambientes reais de todas as fases anteriores.
4. Fechar a v1.0.0 após os aceites funcionais e a Fase 9.

Cada issue deve indicar ID, versão, recorte, fora de escopo, dependências, cenário Given/When/Then, testes, documentação e status. As fases funcionais encerram seus critérios automatizáveis; MongoDB, modelo, cofre, leitor de tela, diálogo nativo e demais evidências manuais são aceitos exclusivamente na Fase 9. Não há estimativa de calendário aprovada.

Referências antigas F0–F7 em registros datados são históricas: F0 era fundação, F1 alpha, F2 MVP, F3 análise/recuperação, F4 antiga 1.0 e F5–F7 extensões. Não correspondem numericamente às dez fases atuais, nem à numeração de seis fases usada até 17/09/2026; usar versão e ID em novos trabalhos. Decisão em [ADR-035](10-decisoes-arquiteturais.md#adr-035--roadmap-por-versão-e-status-baseado-em-evidência-13092026).


## Revisão do plano de autocomplete — 15/09/2026

As fases 1 a 5 citadas nesta seção são sub-fases do subsistema de autocomplete, com numeração própria, e não as dez fases do produto. Plano revisado: dados existentes com aceite parcial; a sub-fase 1 amplia para aprendizado persistente de find; a sub-fase 2 fornece contexto/lista/presenter; 5.1 tradicional pode seguir a 2 sem IA; 3/4 preparam IA; 5.2 IA e 5.3 híbrido sequencial. Dez perfis e tarefas com dependências estão no plano executável. Não há implementação nova nesta revisão. [Plano revisado](auto-complite/README.md), [tarefas por agente](auto-complite/execution-plan.md) e [schema learning](auto-complite/schema-learning.md).
