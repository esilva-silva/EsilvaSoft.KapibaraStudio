# Fase 6 — v0.10.0: administração e manutenção

**Situação:** Marco A (implementação e aceite automatizável da Fase 6) concluído em 09/10/2026. Marco B fica para a homologação manual da Fase 10, conforme escopo. Restore `--locked-mode` e build integral passaram; o gate serial com `NUnit.NumberOfTestWorkers=0` passou sem falhas: Agents 403/403, IntegrationTests 1.041 aprovados/20 ignorados, UnitTests 3.732 aprovados/20 ignorados (5.176 aprovados/40 ignorados). Smokes reais com fixtures sintéticas passaram: F6-09 `$out`, F6-13 build via `currentOp`, F6-18 crash após mutação de profiler, F6-24 crash após arquivo da coleção antes do manifesto, F6-27 Upsert e F6-28 crash após BulkWrite antes do journal e recuperação via ViewModel com owner LiteDB reaberto. As fixtures foram limpas e os estados anteriores verificados. F6-19 negação RBAC não foi demonstrável porque autorização está desabilitada em localhost; o smoke parou antes de escrever e esse cenário fica na Fase 10. O caso de symlink da integração de arquivos continua ignorado por limitação ambiental. PNGs Light/Dark da aba Transferir foram inspecionados. Não foi feita homologação multiplataforma.

Revalidação adicional: IntegrationTests passaram também com agendamento padrão (1.041/19 ignorados). Duas execuções padrão anteriores haviam falhado intermitentemente no teardown de fixtures LiteDB; o diagnóstico opt-in de locks ficou habilitado, mas a execução que o acompanhou passou e não identificou processo proprietário. A suíte integral aceita continua sendo o gate serial aprovado; a corrida anterior fica registrada sem atribuir causa não comprovada.

**Revisão de 08/10/2026:** [inventário de implementação, lacunas e ordem proposta](revisao-2026-10-08.md). Restore/build aprovados (0 avisos/erros) e 148 testes focados aprovados; não encerra o aceite da fase nem homologação real.

**Meta de implementação — 08/10/2026:** [F6-IMPL](meta-de-implementacao.md) registra implementação, aceite e limites por F6-01..32. Estados “parcial” nas tabelas descrevem restrições assumidas ou cenários encaminhados à homologação manual da Fase 10; não reabrem o Marco A.

**Meta de homologação/finalização — 08/10/2026:** [F6-HOM](meta-homologacao-finalizacao.md) mantém a linha do tempo e o aceite final. Smokes em `sample_mflix` usaram fixtures sintéticas e namespace exclusivo; nenhum documento preexistente foi lido ou alterado. Os crashes controlados de export/import F6-24/28 foram homologados depois; topologias/RBAC com autorização ativa e UI nativa/acessibilidade ficam no Marco B/manual da Fase 10.

As atualizações datadas abaixo são snapshots históricos; qualquer registro antigo de fase aberta, crash pendente ou gate falho foi superado pelo aceite vigente acima e na [F6-HOM](meta-homologacao-finalizacao.md).

**Registro anterior à conclusão Marco A — 09/10/2026:** build integral passou com 0 avisos/erros. F6-27 passou filtro unitário 66/66 e smoke opt-in Mongo Upsert 1/1; F6-24 limpeza/recuperação de export passou 8/9 (symlink ignorado); UI Headless passou 1/1 com PNGs Light/Dark inspecionados. Naquela tentativa, IntegrationTests teve 2 falhas de teardown LiteDB por arquivo `WORKSPACE-LOG.DB.tmp` ocupado; os testes afetados passaram isolados. Gates e smokes finais posteriores estão resumidos no início desta página e nas metas de implementação/homologação. Homologação multiplataforma será manual e não foi executada.

**Rastreabilidade F6-01..32:** [código, testes, evidências e limites por requisito](rastreabilidade-f6.md). Na retomada de 08/10, o teste Mongo opt-in não obteve resultado porque `localhost:27017` recusou conexões; não houve acesso ou escrita em `sample_mflix` nessa sessão. O resultado anterior permanece registrado como evidência histórica, sem ser tratado como smoke repetido.

## Objetivo

Cobrir tarefas rotineiras de manutenção diretamente na aplicação, com acesso ao servidor exclusivamente pela connection string MongoDB: coleções, views, validação, índices, estatísticas, usuários, papéis e exportação/importação lógica.

**Recorte decidido em 08/10/2026 (ADR-069):** operações que exigem SO/SSH, arquivos/configuração do servidor, inicialização/reinício ou API administrativa externa passam ao [bkl-07](../../backlog/bkl-07-administracao-fora-da-connection-string.md), sem fase/versão. Permissões elevadas e limitações por servidor/topologia não equivalem a outro canal de acesso; comandos conectados permanecem condicionais às capacidades do alvo. A decisão não altera código nem reativa entradas visuais.

## Escopo incluído (IDs do catálogo)

- DAT-02/10/11 — criar banco, criar/renomear coleção, views e validação de coleção.
- IDX-01/02/03/04 — listar, criar, remover índices, opções suportadas, visibilidade e scripts.
- ADM-01/02/03/04/09 — métricas expostas por comandos MongoDB, `serverStatus`, `hello`, `currentOp`, `killOp`, profiler, usuários/papéis por comandos suportados, `validate`, `compact`, `collMod` e parâmetros runtime acessíveis pela conexão. O Marco A cobre configuração/coleta/restauração limitada do profiler, administração conectada e manutenção; negação RBAC exige um daemon com autorização ativa e fica para a Fase 10.
- TRF-02/03 — exportação e importação lógica com manifesto.

## Fora de escopo

Administração de clusters distribuídos, provisionamento de nuvem, automação de SO, backup operacional completo e sincronização entre servidores. Também ficam no backlog: métricas/logs que exijam acesso externo ao host, gestão via API Atlas/identidade externa, parâmetros somente de startup/configuração persistente/reinício e execução de runbooks de SO ou manutenção offline. [Rastreabilidade dos recortes retirados](../../backlog/bkl-07-administracao-fora-da-connection-string.md).

Arquivos locais da transferência lógica e processamento no Desktop continuam incluídos. Não usar SO/SSH/API externa como fallback para falta de permissão ou comando não suportado na conexão selecionada.

## Antecipações técnicas presentes no código

O código desta fase **existe e está integrado**: `MongoWorkspaceService`, `ExplorerMetadataService` e a janela `WorkspaceToolsWindow` (abas Coleções, Documentos, Índices, Administração, Transferir, CRUD em lote, Análise).

A entrada **Ferramentas** foi reativada em Mais ações, habilitada somente com uma aba Mongo conectada; a janela usa o perfil e o banco explicitamente ativos. A Fase 6 reativa somente jornadas aceitas; análises gerais e itens do bkl-02 continuam fora do escopo e sem ponto de entrada.

## Critério de aceite

Criar, inspecionar, renomear e remover coleção; criar, alterar opção suportada e remover índice com releitura; script gerado corresponde ao alvo e não executa ao copiar; estatísticas distinguem estimativa de contagem exata. Confirmação obrigatória para `dropCollection`, `dropIndex`, `deleteMany` e operações de banco; respeitar somente leitura, permissões, proteção de `_id_`, auditoria e resultado incerto após cancelamento.

O aceite realizado e os limites por requisito estão detalhados em [F6-01..32](meta-de-implementacao.md#requisitos-pendentes-e-aceite). Marco A registra implementação/validação automatizável; Marco B registra homologação real/nativa na Fase 10. Nenhum exige concluir o bkl-07.

## Dependências

Base funcional das Fases 1 a 4; conexão MongoDB e contratos de escrita/capacidade por servidor/permissão. O aceite não depende de acesso administrativo ao SO, API Atlas, configuração de startup ou conclusão do bkl-07. Administração não exige modelo de IA local nem homologação de agentes.

## Documentos relacionados

- [Segurança e administração](../../07-dados-seguranca-e-administracao.md) · [Explorer](../../19-database-explorer.md) · [Transferência lógica](../../13-exportacao-logica.md) · [Fase 10 — homologação manual](../phase-10-v0.14.0/README.md)

## Validação manual transferida

RBAC, topologias reais, profiler além da leitura de configuração e operações destrutivas em servidor real são critérios da [Fase 10 / v0.14.0](../phase-10-v0.14.0/README.md).

**Atualização de implementação/homologação — 08/10/2026:** F6-27 implementa política Reject padrão e Upsert explícito com confirmação do destino, além de detecção de `_id` repetido antes de qualquer acesso ao Mongo. Build recompilado, testes focados 25/25 e renderização da aba Transferência (Light/Dark) passaram. Uma suíte integral reportou uma falha transitória de teardown LiteDB por arquivo temporário ocupado; o teste passou ao ser repetido isoladamente. A suite completa requer repetição para fechar o gate; Fase 6 permanece em andamento.

**Registro histórico L06 anterior à revalidação final — 08/10/2026:** array JSON foi homologado em Mongo local; restore opt-in restaurou capped, validator, índice único e view com readback/relatório; `buildInfo` registrou versão no manifesto. F6-28 revalidou e reiniciou import desde o documento zero em Mongo usando receipt sintético; persistência LiteDB e UI passaram 4 testes separados. `killOp` e `compact` passaram smokes Mongo em recursos sintéticos e descartáveis. F6-29 apresenta plano por definição, dependências e colisões conhecidas. Naquele gate, passaram restore locked, build (0 avisos/erros) e suíte serial (5.135 aprovados, 25 ignorados); os resultados posteriores e atuais estão no parágrafo **Revalidação local** acima. Falta falha/crash real e o ciclo Desktop + LiteDB real + Mongo; a prévia não compara todo efeito específico de versão/topologia. Ver [meta de implementação](meta-de-implementacao.md) e [homologação](meta-homologacao-finalizacao.md). A fase permanece em execução.

**Retomada F6-28 — 09/10/2026:** crash de processo real após BulkWrite Mongo e antes do update do checkpoint foi homologado 1/1 em `sample_mflix` com collection GUID. Receipt `Prepared` recuperado; preflight recusou reinício com destino não vazio; somente a fixture verificada foi removida e snapshots iniciais restaurados. [Evidência detalhada](meta-homologacao-finalizacao.md). Próximo gap automatizável: crash real durante exportação (F6-24). O gate global permanece não verde pelo teardown LiteDB intermitente.

**Crash real F6-24 — 09/10/2026:** smoke passou 1/1; processo filho morreu após produzir o arquivo de collection sintética e antes do manifesto final. Adapter novo removeu só o pacote incompleto com marker e preservou pacote committed/diretório sem marker. [Evidência](meta-homologacao-finalizacao.md). O cenário de F6-24 que faltava ficou coberto; crash físico permanece fora do escopo controlável.
