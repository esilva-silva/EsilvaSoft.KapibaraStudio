# Roadmap de evolução do Kapibara Studio

**F5-COP-PERM — configuração solicitada em 06/10/2026:** implementado no checkout o campo Copilot de chamadas por mensagem, padrão 100 e vazio ilimitado, com persistência v2 compatível com leitura v1 e captura por turno no runtime/registry. O incremento atende ao pedido do usuário e substitui os tetos fixos 20/32 para esses turnos. [Aceite e evidências](phases/phase-05-v0.9.0/meta-permissoes-tools-copilot.md#limite-configurável-solicitado-pelo-usuário--06102026); validação na aplicação permanece com o usuário.

**F5-COP-PERM — novo retorno do client em 06/10/2026:** identificada recusa por cota, visível no cartão de `get_document` e relatada para `mongo_explain`; proposta não testada sem conteúdo autorizado. Próximo aceite manual: duas leituras em mensagens novas e proposta sobre o buffer anexado/autorizado. [Evidência e limites](phases/phase-05-v0.9.0/meta-permissoes-tools-copilot.md#novo-retorno-do-client--06102026); aceite integral da Fase 5 segue aberto.

**F5-COP-PERM reaberta — 06/10/2026:** retorno manual de 11/14 tools funcionando; `get_document`, `mongo_explain` e proposta ainda sem causa correlacionada na instalação. O checkout corrige o gate de proposta workspace-only e publica cota como `ToolCallLimitExceeded`, mantendo 20 chamadas por turno e as recusas existentes. [Retomada e evidências](phases/phase-05-v0.9.0/meta-permissoes-tools-copilot.md#retomada--06102026). Aceite real das 14 tools permanece aberto, com validação pelo usuário.

**Correção de permissões Copilot no checkout — 05/10/2026:** [meta F5-COP-PERM](phases/phase-05-v0.9.0/meta-permissoes-tools-copilot.md) corrige a ausência do cofre MongoDB nas fontes de leitura, separa falha de execução de negação e alinha workspace/propostas. Grants e prioridade entre fases preservados. Evidência automática no acompanhamento; validação manual na aplicação reservada ao usuário.

**Detalhamento da Fase 5 — 03/10/2026:** a [meta Copilot](phases/phase-05-v0.9.0/meta-de-implementacao.md) cobre 30 requisitos GitHub/produto e as 14 ferramentas liberáveis, com contratos, limites, permissões e aceite individual. Nenhuma nova capacidade foi habilitada; prioridade da Fase 8, backlog dos demais providers e gates existentes permanecem.

**Recorte da Fase 5 — 03/10/2026:** GitHub Copilot e infraestrutura compartilhada necessária à sua conclusão permanecem na Fase 5 / v0.9.0. Claude, Codex, APIs externas e integração de clientes MCP externos passam ao [bkl-06](backlog/bkl-06-integracoes-agentes.md), sem fase/versão comprometida. Código, disponibilidade, permissões e gates existentes são preservados; IA local e workflow mantêm suas fases. Esta alteração é documental e não comprova conclusão funcional ou nova homologação.

**P7-UX2 — implementação no checkout em 03/10/2026:** [meta de autoscroll, diferenças e métricas](phases/phase-05-v0.9.0/meta-autoscroll-contexto-consumo-agente.md). Autoscroll, cores, medição UTF-8 do snapshot e métricas tipadas oficiais para Claude Code, OpenAI e Copilot estão implementados. Gates automatizados Windows passaram; inspeção visual foi feita em capturas representativas. Homologação nativa, leitor de tela, conta oficial, amplitude de estados/capturas e tokenizer permanecem pendentes.

**Qualidade — 01/10/2026:** benchmarks são instrumentos manuais de desenvolvimento, fora dos gates de CI/release. A matriz automatizada mantém as duas suítes unitárias em Windows/Linux, guarda estática de fontes nos dois sistemas e integração Windows. [Revisão de estabilidade e limites](architecture/test-stability.md). A leitura estrutural de fontes no Linux não amplia a meta para testes de runtime/conexões reais.

**Registro histórico de release/Copilot — 01/10/2026 (política de distribuição substituída em 02/10):** naquele publish, o runtime SDK ficou externo ao single-file e os pacotes incluíam CLI oficial de login. Esses bundles não representam a política ou os artefatos atuais; a Fase 5 passou a exigir CLI/runtime nativos instalados pelo usuário e ausência de redistribuição. A revisão registra a transição e seus limites em [Release Copilot](architecture/release-copilot.md). Publicação remota, OAuth novo pela UI e execução Linux/ARM64 permanecem pendentes.

**P7-UX — entregas de implementação e validação automatizada concluídas em 30/09/2026:** [P7-UX — painel Agente IA](phases/phase-05-v0.9.0/meta-ui-ux-painel-agente-ia.md). Composição, conversa, compositor e estados reorganizados; build e suíte completa aprovados, PNGs Avalonia inspecionados. Homologação nativa de teclado/leitor de tela/IME e Windows/Linux permanece pendente; não fecha gates de providers.

**P7-AUTH — implementado em 30/09/2026:** [inicialização automática e contratos de conta](phases/phase-05-v0.9.0/meta-inicializacao-autenticacao-agentes.md) padroniza o reconhecimento da conta oficial já existente e o dispatch de integrações por contratos. Copilot é a primeira checagem automática elegível no Windows; políticas que podem exibir diálogo permanecem explícitas. Build e UnitTests passaram; homologação nativa de CLI/cofre continua pendente por SO. Não altera os gates da Fase 5.

**Claude no painel Agente IA — implementação no checkout:** o único caminho oferecido é a CLI oficial Claude Code, preservando seus métodos nativos de autenticação e cobrança conforme o método escolhido no CLI. Restrições de destino/transporte não alteram esses métodos. O provider HTTP Anthropic legado fica fora da composição e do catálogo do painel; nenhuma falha faz fallback. A chave API legada no vault não é lida nem apagada e conversas antigas não migram automaticamente. O gate do acordo comercial Anthropic segue aberto; homologação manual/runtime real permanece pendente. Consulte [meta Claude assinatura](backlog/integracoes-agentes/meta-claude-subscription-parity.md) e [backlog bkl-06](backlog/bkl-06-integracoes-agentes.md).

## Meta transversal ativa — adapters do SO e isolamento unitário (30/09/2026)

O pedido de arquitetura exige completar a migração de todos os acessos reais ao SO para adapters em projeto separado e substituir seu uso unitário por interfaces com doubles. Console STDIO MCP, segredos Windows/Linux, broker/socket, paths, launcher, diagnóstico, owner LiteDB, catálogo/cache de modelos, exportação e atualização passam por adapters em `Infrastructure.System`; ONNX está isolado em `Infrastructure.LocalAi.OnnxAdapter`. Na verificação integrada de 30/09, restore locked passou offline, build CPU teve 0 avisos/erros e a suíte completa aprovou Benchmarks 41/41, Infrastructure.Agents.Tests 198/198, UnitTests 3323 aprovados/20 ignorados e IntegrationTests 868 aprovados/14 ignorados. Builds cruzados `linux-x64` de Desktop/McpServer passaram. Runtime/testes nativos em Linux e homologações explícitas dos adapters continuam pendentes por falta de distribuição Linux neste host. Critérios completos, sequência e inventário em [system-adapters](architecture/system-adapters.md). Esta meta não redefine nem encerra os gates funcionais do roadmap.

Referência: **03/10/2026**. Este plano substitui o cronograma antigo F0–F7 e as numerações anteriores de seis e nove fases. As fases são compromissos de consolidação, não a ordem em que todo código foi escrito. Recursos antecipados continuam disponíveis com seus limites; sua existência não encerra uma fase.

**Prioridade de entregas — 03/10/2026:** Fase 8 / v0.12.0 (IA local e produtividade contextual) primeiro; Fase 5 / v0.9.0 (GitHub Copilot e agentes) depois. A numeração e as versões-alvo dos escopos foram reorganizadas; tags, pacotes históricos, IDs de requisitos e evidências de implementação não foram alterados. O [mapa de migração](phases/README.md#reorganização-de-03102026) registra origem e destino.

**Versões e planejamento:** a prioridade de desenvolvimento é a Fase 8 / v0.12.0; a Fase 5 / v0.9.0 fica em espera. Os registros de publicação e pacotes neste checkout são históricos: a reorganização documental não publica release, não altera tags nem muda a versão dos binários. Copilot conserva disponibilidade limitada no Windows e gates abertos; homologação manual ampla pertence à Fase 10.

## Status e evidências

- ✅ **Implementado:** caminho concreto no código para o recorte descrito; não significa homologação em todas as plataformas/topologias.
- 🚧 **Em desenvolvimento:** implementação parcial ou critérios essenciais pendentes, explicitados junto do status.
- 📋 **Planejado:** sem caminho integrado identificado para o recorte.
- 🧪 **Experimental:** caminho disponível, mas qualidade ou ambiente limita seu uso como compromisso estável.

O [catálogo](03-catalogo-funcional.md) mantém IDs e status do requisito completo; o [inventário de código](24-inventario-roadmap.md) discrimina recortes existentes e lacunas. A [matriz](15-matriz-de-validacao.md) registra testes executados e o [checklist](16-checklist-homologacao.md) mantém verificações externas. As metas que exigem ambiente, hardware ou operação humana foram concentradas na [Fase 10 / v0.14.0](phases/phase-10-v0.14.0/README.md); nenhuma validação textual equivale à publicação de uma release ou ao encerramento dos requisitos amplos do catálogo.

## Meta transversal — internacionalização da interface — concluída no recorte automatizado

A interface desktop suporta `pt-BR`, `en`, `es` e `zh-CN`. `pt-BR` permanece o idioma inicial para preservar a experiência existente; valores ausentes ou inválidos e chaves sem tradução resolvem para `en`, e uma chave também ausente no inglês aparece como `[[chave]]`. A preferência é persistida de forma aditiva na sessão, sem regravar rascunhos, resultados ou credenciais. Os arquivos `docs/**/*.md` continuam deliberadamente em português.

O incremento cobre catálogo, barra principal, Explorer, editor/resultados, ferramentas, conexões, ambientes, histórico, autocomplete/IA, acessibilidade, mensagens de operação e inicialização com restauração do idioma antes das operações do workspace. A matriz visual gera 64 PNGs reais nos quatro idiomas e nos dois temas. A meta de implementação e tradução está concluída: os testes unitários e a evidência visual estão verdes. Revisão linguística de domínio e validação integrada oficial com MongoDB permanecem como homologação externa das fases, não como bloqueio do catálogo de idiomas.

| Fase | Versão | Objetivo | Situação |
| --- | --- | --- | --- |
| 1 | v0.5.0 | MVP: conectar → navegar → consultar → visualizar → editar → exportar | Escopo funcional concluído e [arquivado](done/release_v0.5.0/README.md); homologação manual na Fase 10 / v0.14.0 |
| 2 | v0.6.0 | Organização dos projetos e autocomplete básico | Escopo funcional concluído e [arquivado](done/release_v0.6.0/README.md); homologação manual na Fase 10 |
| 3 | v0.7.0 | Autocomplete com IA | Escopo funcional concluído e [arquivado](done/release_v0.7.0/README.md); sugestões revisáveis, sem aplicação automática |
| 4 | v0.8.0 | Abertura e salvamento de arquivos de texto | Escopo funcional concluído e [arquivado](done/release_v0.8.0/README.md); inclui workspace local de uma pasta |
| 5 | v0.9.0 | GitHub Copilot e integração com agentes | Adiada até concluir as entregas prioritárias da Fase 8; concluir Copilot e infraestrutura necessária; demais integrações no [bkl-06](backlog/bkl-06-integracoes-agentes.md); gates abertos em [estado e limites](phases/phase-05-v0.9.0/README.md) |
| 6 | v0.10.0 | Administração e manutenção | [Em desenvolvimento](phases/phase-06-v0.10.0/README.md): coleções, views, validação, índices, estatísticas, usuários, papéis e exportação/importação lógica |
| 7 | v0.11.0 | Chat simples com IA baseado em workflow | [Planejada](phases/phase-07-v0.11.0/README.md): fluxo predefinido, escopo limitado e ações controladas |
| 8 | v0.12.0 | IA local e produtividade contextual | **Prioridade atual de entrega**; recorte automatizado implementado, entregas seguintes conforme [meta](phases/phase-08-v0.12.0/meta-de-implementacao.md); ONNX real permanece experimental |
| 9 | v0.13.0 | Polimento de código, arquitetura e UI/UX | [Planejada](phases/phase-09-v0.13.0/README.md): revisão transversal e correção de problemas, preservando os contratos existentes |
| 10 | v0.14.0 | Homologação manual e validação em ambientes reais | [Planejada](phases/phase-10-v0.14.0/README.md): plataformas, acessibilidade, MongoDB/mongosh, hardware, instalação e atualizações reais |
| 11 | v1.0.0 | Estabilidade, revisão completa, instalação e atualizações | [Planejada](phases/phase-11-v1.0.0/README.md): release estável após aceites funcionais, polimento da Fase 9 e homologação da Fase 10 |

O detalhamento de cada fase — escopo, fora de escopo, antecipações, aceite e dependências — fica em [`phases/`](phases/README.md). Este documento permanece como índice e regra geral.

A [reorganização de 03/10/2026](phases/README.md#reorganização-de-03102026) move agentes para v0.9.0, workflow para v0.11.0 e IA local para v0.12.0; acrescenta polimento em v0.13.0 e transfere homologação para v0.14.0. Estabilidade permanece v1.0.0, agora Fase 11. Nenhum requisito ou gate foi encerrado pela movimentação.

## Implementação antecipada, fase ativa, backlog e release arquivada

Quatro estados distintos, que não devem ser confundidos:

- **Prioridade atual** — Fase 8 / v0.12.0; as entregas pendentes estão detalhadas na meta de implementação. A Fase 5 / v0.9.0 foi adiada até concluir esse foco. A homologação real aplicável permanece na Fase 10.
- **Implementação antecipada** — código integrado que pertence a uma fase futura. Existe, é preservado e continua testado, mas **não** encerra a fase à qual pertence, **não** conta como escopo concluído da fase atual e **não** aparece na interface. Exemplos atuais: administração (v0.10.0), agregação e Script Engine (sem fase).
- **Backlog** — requisito sem fase definida, adiado ou retirado do escopo. Ver [`backlog/`](backlog/README.md). Entrar no backlog não apaga código. A regra de ocultar entradas visuais exige decisão de escopo; no bkl-06, implementações e pontos de entrada existentes continuam como estão por determinação do usuário.
- **Release arquivada** — versão cujo escopo funcional foi fechado e movido para [`done/`](done/README.md). Arquivar **não** significa homologar; a validação manual aplicável pertence à Fase 10.

Recursos fora da fase podem continuar presentes no código, desativados ou experimentais. Sua existência não é compromisso de suporte.

## Requisitos sem versão comprometida

O backlog de requisitos sem fase atribuída está em [`backlog/bkl-05-requisitos-sem-fase.md`](backlog/bkl-05-requisitos-sem-fase.md), preservando os IDs do catálogo. Nenhum item ali bloqueia automaticamente a v1.0.0.

## Ordem de trabalho e definição de pronto

1. Priorizar as entregas restantes da Fase 8 / v0.12.0 conforme sua meta de implementação; transferir validação de inferência/hardware real para a Fase 10.
2. Após concluir esse foco, retomar a Fase 5 / v0.9.0. A Fase 6 / v0.10.0 e a Fase 7 / v0.11.0 permanecem definidas em seus documentos; qualquer retomada deve respeitar dependências e o escopo priorizado vigente.
3. Executar o polimento da Fase 9 / v0.13.0 após consolidar os escopos funcionais.
4. Executar a Fase 10 / v0.14.0, que concentra a homologação manual e a validação em ambientes reais de todas as fases anteriores.
5. Fechar a v1.0.0 após os aceites funcionais e a Fase 10.

Cada issue deve indicar ID, versão, recorte, fora de escopo, dependências, cenário Given/When/Then, testes, documentação e status. As fases funcionais encerram seus critérios automatizáveis; MongoDB, modelo, cofre, leitor de tela, diálogo nativo e demais evidências manuais são aceitos exclusivamente na Fase 10. Não há estimativa de calendário aprovada.

Referências antigas F0–F7 em registros datados são históricas: F0 era fundação, F1 alpha, F2 MVP, F3 análise/recuperação, F4 antiga 1.0 e F5–F7 extensões. Não correspondem numericamente às onze fases atuais, nem à numeração de seis fases usada até 17/09/2026; usar versão e ID em novos trabalhos. Decisão em [ADR-035](10-decisoes-arquiteturais.md#adr-035--roadmap-por-versão-e-status-baseado-em-evidência-13092026).


## Revisão do plano de autocomplete — 15/09/2026

As fases 1 a 5 citadas nesta seção são sub-fases do subsistema de autocomplete, com numeração própria, e não as onze fases do produto. Plano revisado: dados existentes com aceite parcial; a sub-fase 1 amplia para aprendizado persistente de find; a sub-fase 2 fornece contexto/lista/presenter; 5.1 tradicional pode seguir a 2 sem IA; 3/4 preparam IA; 5.2 IA e 5.3 híbrido sequencial. Dez perfis e tarefas com dependências estão no plano executável. Não há implementação nova nesta revisão. [Plano revisado](auto-complite/README.md), [tarefas por agente](auto-complite/execution-plan.md) e [schema learning](auto-complite/schema-learning.md).
