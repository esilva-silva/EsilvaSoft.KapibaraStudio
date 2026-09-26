# Catálogo funcional

Revisado em **22/09/2026** conforme o [roadmap oficial de dez fases](09-plano-de-implementacao.md). Os 68 IDs originais são preservados; somente a versão de consolidação mudou, conforme o novo roadmap. Linhas marcadas **Backlog sem versão** estão em [`backlog/`](backlog/README.md) e não têm fase atribuída — ver [bkl-03](backlog/bkl-03-script-engine-entre-conexoes.md) para EDT-06/TRF-05 e [bkl-04](backlog/bkl-04-modo-aggregation.md) para AGG-01/02/03/04. Administração, índices, coleções, views e transferência lógica passaram de v0.7.0 para [v0.10.0](phases/phase-06-v0.10.0/README.md). Status avalia a abrangência completa de cada linha; recortes implementados, evidência de código/testes e lacunas estão no [inventário](24-inventario-roadmap.md). ✅ Implementado não significa homologado em toda topologia; 🚧 Em desenvolvimento indica parcial; 📋 Planejado indica ausência de caminho integrado; 🧪 Experimental indica qualidade/ambiente ainda limitado. A versão indica consolidação do recorte prioritário: extensões de uma linha ampla não entram automaticamente no MVP. Critérios abaixo permanecem alvos de aceite, sujeitos ao [registro de capacidades](04-compatibilidade-e-capacidades.md).

## Conexões e workspace

| ID | Recurso e abrangência | Versão de consolidação | Status | Aceite observável |
| --- | --- | --- | --- | --- |
| CON-01 | CRUD de perfis em LiteDB; pastas, tags, cor, ambiente e favoritos | v0.5.0 | ✅ Implementado | Salvar, reiniciar, editar, duplicar e excluir sem misturar perfis em Windows/Linux. A implementação atual inclui CRUD, duplicação guiada, pastas, favoritos, ambiente, cor e tags persistidos. |
| CON-02 | URI `mongodb://` e `mongodb+srv://`, SRV/TXT, múltiplos hosts, replica set, conexão direta | v0.5.0 | 🚧 Em desenvolvimento | Diagnosticar URI inválida, DNS, seleção de servidor e topologia |
| CON-03 | SCRAM-SHA-256/SHA-1, authSource, TLS, CA e X.509 | v0.5.0 | 🚧 Em desenvolvimento | Conectar a ambiente de teste autenticado; rejeitar certificado inválido |
| CON-04 | AWS IAM, OIDC, LDAP/PLAIN e Kerberos, conforme implantação | Backlog sem versão | 📋 Planejado | Matriz de autenticação mostra provedor, dependências e testes executados |
| CON-05 | SSH: senha/chave/agente, host key, bastion, reconexão | Backlog sem versão | 📋 Planejado | Host key divergente bloqueada; topologia replica set acessível ou limitação explicada |
| CON-06 | Read preference, concerns, retry reads/writes, compressão, timeouts, pool, Stable API | v0.5.0 | 🚧 Em desenvolvimento | Configuração efetiva inspecionável e isolada por perfil |
| CON-07 | Importar/exportar perfis, cofres de SO e sessão sem persistência | v0.5.0 | 🚧 Em desenvolvimento | Arquivo compartilhado e logs não contêm senha, token ou chave privada |
| CON-08 | Conexões simultâneas, reconexão, desconexão, limite de clientes ativos | v0.5.0 | 🚧 Em desenvolvimento | Uma conexão indisponível não impede consultas em outra |
| CON-09 | Descoberta de versão, FCV, permissões, serviços e capacidades | v0.5.0 | 🚧 Em desenvolvimento | Falha de descoberta resulta em estado desconhecido, sem bloquear CRUD permitido |
| CON-10 | Workspace local de uma pasta: abrir/trocar/fechar raiz, árvore de arquivos, criar, renomear e enviar à lixeira | v0.8.0 | 📋 Planejado | Uma raiz por vez; caminhos permanecem dentro da raiz; atualização explícita e falhas recuperáveis |

## Dados, coleções e consultas

| ID | Recurso e abrangência | Versão de consolidação | Status | Aceite observável |
| --- | --- | --- | --- | --- |
| DAT-01 | Bancos, coleções, views, opções, estatísticas e namespaces de sistema | v0.5.0 | 🚧 Em desenvolvimento | Explorer incremental e filtrável; namespaces restritos identificados |
| DAT-02 | Criar/remover banco e coleção, renomear onde suportado, collation, capped e clustered | v0.10.0 | 🚧 Em desenvolvimento | Banco criado por coleção inicial explícita; opções relidas após criação. A implementação atual cria bancos de usuário por coleção inicial confirmada, cria coleções comuns, capped, clustered e views com collation, renomeia e remove coleções comuns, e remove bancos de usuário com confirmação textual. |
| DAT-03 | Find textual no editor: filtro, projeção, sort, limit, skip, hint, collation, maxTimeMS, batchSize e comment | v0.5.0 | 🚧 Em desenvolvimento | Uma única superfície de edição representa a consulta; autocomplete, diagnóstico e resultados tornam a execução reproduzível e cancelável |
| DAT-04 | Insert one/many, duplicar documento e geração opcional de `_id` | v0.5.0 | ✅ Implementado | Valores BSON mantidos e erros por documento relatados. A implementação atual prepara uma cópia da prévia sem o `_id` de nível superior e exige que o operador revise e escolha Inserir. |
| DAT-05 | Update/replace, upsert, find-and-modify, `$set`, `$unset`, arrays, arrayFilters e pipeline de update | v0.5.0 | 🚧 Em desenvolvimento | Diff tipado e restrições de `_id`; a implementação atual oferece update parcial por operadores ou pipeline, upsert explícito, `arrayFilters` JSON e `find-and-modify` atômico que retorna o documento posterior. |
| DAT-06 | Delete one/many, prévia de filtro e seleção de documentos | v0.5.0 | 🚧 Em desenvolvimento | Filtro vazio exige confirmação contextual no produto; resultado e falhas registrados |
| DAT-07 | Bulk ordered/unordered e, se suportado, múltiplos namespaces | Backlog sem versão | 📋 Planejado | Sucessos parciais preservados; não repetir cegamente operações aplicadas |
| DAT-08 | Concorrência na edição, detecção de conflito e recarregamento | v0.5.0 | 🚧 Em desenvolvimento | Edição concorrente não sobrescreve silenciosamente campos protegidos |
| DAT-09 | Distinct, contagem exata/estimada, paginação e projeção de grandes campos | v0.5.0 | 🚧 Em desenvolvimento | Estimativa distinguida de contagem; sem materializar coleção inteira. A implementação atual oferece contagem exata por filtro, estimativa de coleção e valores distintos limitados no servidor; a projeção de grandes campos permanece pendente. |
| DAT-10 | Validador `$jsonSchema`, validationLevel/action, inferência por amostra | v0.10.0 | 🚧 Em desenvolvimento | Mostrar amostra e incerteza; validar casos válidos e inválidos no servidor. A implementação atual aplica um validador BSON/Extended JSON pelo `collMod`, exige confirmação pelo nome da coleção, escolhe `off`/`strict`/`moderate` e `error`/`warn`; também gera schema inicial sem campos obrigatórios a partir de até 200 documentos, exclusivamente em memória. A homologação com servidor continua pendente. |
| DAT-11 | Views: criar, editar pipeline e dependências; materialização por tarefa | v0.10.0 | 🚧 Em desenvolvimento | View comum aparece somente leitura; refresh materializado explicita escrita. A implementação atual cria e edita views pelo explorer com coleção de origem, confirmação textual e pipeline JSON; materialização permanece pendente. |
| DAT-12 | Sessões, transações, concerns e retry transacional | Backlog sem versão | 📋 Planejado | Commit/abort e falha transitória testados em replica set; sessão serializada |

Base: [driver](https://www.mongodb.com/pt-br/docs/drivers/csharp/current/), [validação](https://www.mongodb.com/docs/manual/core/schema-validation/), [views](https://www.mongodb.com/docs/manual/core/views/), [transações](https://www.mongodb.com/docs/drivers/csharp/current/crud/transactions/).

## Editor e agregações

| ID | Recurso e abrangência | Versão de consolidação | Status | Aceite observável |
| --- | --- | --- | --- | --- |
| EDT-01 | Editor textual de consultas em Extended JSON/MQL, destaque, indentação, folding, seleção, busca e diagnóstico | v0.5.0 | 🚧 Em desenvolvimento | Documento inválido não executado; linha/coluna e correção localizáveis; não existe formulário paralelo para montar a consulta |
| EDT-02 | Autocomplete de operadores, campos, caminhos, coleções e tipos | v0.6.0 | 🚧 Em desenvolvimento (entrega determinística encerrada em 17/09/2026 com pendências aceitas em aberto) | Sugestões corretas por contexto, conexão e versão; funciona offline com catálogo. Gates de latência p95/p99 sem evidência |
| EDT-03 | BSON completo, UUID standard/legacy, ObjectId, datas e números exatos | v0.5.0 | 🚧 Em desenvolvimento | Ida e volta sem conversões implícitas, usando fixtures independentes |
| EDT-04 | Snippets, parâmetros tipados, favoritos, histórico, scripts e arquivos de texto | v0.8.0 | 🚧 Em desenvolvimento | Abrir, salvar e salvar como preservam conteúdo/codificação; parâmetros continuam nós BSON, nunca substituição textual vulnerável |
| EDT-05 | Console de comandos BSON com metadados e políticas de execução | Backlog sem versão | 📋 Planejado | Comando permitido sem formulário pode ser executado e resultado preservado |
| EDT-06 | Modo script JavaScript + queries JSON via `mongosh`; variáveis, funções, loops, múltiplas queries, console e resultados | Backlog sem versão | 🚧 Em desenvolvimento | Script combina lógica JS e filtros JSON/Extended JSON na mesma execução em Windows/Linux; preserva BSON, limita resultados e permite interrupção |
| EDT-07 | Exportar query/pipeline para C# e mongosh | Backlog sem versão | 📋 Planejado | Saída gerada cobre o subconjunto documentado e passa fixtures de equivalência |
| AGG-01 | Pipeline textual completo no editor, com explain | Backlog sem versão | 🚧 Em desenvolvimento | Aceita BSON válido e todos os estágios documentados sem depender de campos de formulário |
| AGG-02 | Apoio visual opcional para pipeline, sem substituir o editor textual | Backlog sem versão | 📋 Planejado | Prévia não executa `$out`/`$merge`; o texto continua sendo a fonte editável e salva |
| AGG-03 | Lookup, facet, union, group, window, geoespacial, expressões e variáveis | Backlog sem versão | 🚧 Em desenvolvimento | Catálogo contextual cobre famílias; estágio desconhecido permanece editável como texto |
| AGG-04 | Explain gráfico e bruto; planos vencedores/rejeitados e métricas | Backlog sem versão | 🚧 Em desenvolvimento | Suporta respostas clássicas e SBE sem depender de um único formato |

Referências de linguagem: [predicados](https://www.mongodb.com/docs/manual/reference/mql/query-predicates/), [updates](https://www.mongodb.com/docs/manual/reference/mql/update/), [expressões](https://www.mongodb.com/docs/manual/reference/mql/expressions/), [estágios](https://www.mongodb.com/docs/manual/reference/mql/aggregation-stages/).

## Índices

| ID | Recurso e abrangência | Versão de consolidação | Status | Aceite observável |
| --- | --- | --- | --- | --- |
| IDX-01 | Listar/criar/remover índices simples e compostos; ordem e nomes | v0.10.0 | 🚧 Em desenvolvimento | Chaves ordenadas preservadas; `_id` protegido; operação verificada no servidor |
| IDX-02 | Unique, sparse, partial, TTL, collation, hidden e wildcardProjection | v0.10.0 | 🚧 Em desenvolvimento | Opções incompatíveis diagnosticadas; alteração não suportada propõe recriação explícita. A implementação atual oferece filtro parcial BSON, collation, criação de índice oculto e `wildcardProjection` BSON para chaves que incluem `$**`; a projeção não permite mistura de inclusão e exclusão, exceto `_id`. |
| IDX-03 | Multikey, hashed, text, wildcard, 2d e 2dsphere | v0.10.0 | 🚧 Em desenvolvimento | Multikey apresentado como comportamento automático; assistentes não inventam tipos |
| IDX-04 | Uso/tamanho, índices redundantes, impacto de build e commit quorum | v0.10.0 | 🚧 Em desenvolvimento | Recomendação mostra evidência e limitações; sem aplicar automaticamente. A implementação atual apresenta as estatísticas de uso retornadas por `$indexStats`; tamanho, redundância, impacto de build e commit quorum permanecem pendentes. |
| IDX-05 | Search e Vector Search: criar, listar, atualizar, remover e acompanhar construção | Backlog sem versão | 📋 Planejado | Estado pronto confirmado antes de testar consulta; serviço detectado |
| IDX-06 | Query settings, plan cache e ações de otimização por versão | Backlog sem versão | 📋 Planejado | Prévia, execução e leitura posterior; comandos legados identificados |

Referências: [índices](https://www.mongodb.com/docs/manual/indexes/), [driver](https://www.mongodb.com/docs/drivers/csharp/current/indexes/), [TTL](https://www.mongodb.com/docs/manual/core/index-ttl/), [partial](https://www.mongodb.com/docs/manual/core/index-partial/).

## Transferência e recuperação

| ID | Recurso e abrangência | Versão de consolidação | Status | Aceite observável |
| --- | --- | --- | --- | --- |
| TRF-01 | Exportar resultado/coleção em Extended JSON e CSV | v0.5.0 | ✅ Implementado no recorte da página carregada | A implementação exporta a página carregada como JSON ou CSV para arquivo novo, por escrita incremental, com progresso e cancelamento; o arquivo final só aparece após sucesso. Exportação integral da coleção permanece fora do recorte MVP. |
| TRF-02 | Importar JSON/NDJSON/CSV com mapeamento de tipos, lotes e política de duplicados | v0.10.0 | 🚧 Em desenvolvimento | Linha inválida identificada, relatório e retomada segura quando suportada |
| TRF-03 | Exportar banco com manifesto de coleções, opções, índices e validadores | v0.10.0 | 🚧 Em desenvolvimento | Manifesto permite auditar o incluído, o omitido e a consistência |
| TRF-04 | Backup/restauração com mongodump/mongorestore, archive/gzip e namespace mapping | Backlog sem versão | 📋 Planejado | Restauração em destino de teste confirma BSON, metadados e contagens |
| TRF-05 | Copiar dados/coleções entre conexões e clonar estrutura | Backlog sem versão | 🚧 Em desenvolvimento | Sem atomicidade entre servidores prometida; conflitos e checkpoint explícitos |
| TRF-06 | Comparar e sincronizar dados, índices, validadores e opções | Backlog sem versão | 📋 Planejado | Diff determinístico por chave escolhida; usuário revisa plano antes de aplicar |
| TRF-07 | Agendar jobs locais, retenção e recuperação após reinício | Backlog sem versão | 📋 Planejado | Estado persistido; não executar novamente escrita incerta; app fechado claramente tratado |

## Administração e recursos especializados

| ID | Recurso e abrangência | Versão de consolidação | Status | Aceite observável |
| --- | --- | --- | --- | --- |
| ADM-01 | Métricas, serverStatus/dbStats, estatísticas de coleção e topologia | v0.10.0 | 🚧 Em desenvolvimento | Amostragem limitada e ausência de permissão sem falha global. A implementação atual consulta `serverStatus`, `dbStats`, `collStats` e `hello` para exibir topologia e papel do nó; métricas históricas permanecem pendentes. |
| ADM-02 | Operações ativas, sessões, locks, killOp e correlação por comment | v0.10.0 | 🚧 Em desenvolvimento | Alvo novamente conferido antes de interromper; ausência de privilégio explicada. A implementação atual consulta operações correntes e solicita `killOp` apenas para ID numérico confirmado exatamente, respeitando o modo somente leitura e registrando metadados na auditoria local; sessões, locks e correlação permanecem pendentes. |
| ADM-03 | Profiler: ler, configurar duração/nível/filtro e restaurar configuração | v0.10.0 | 🚧 Em desenvolvimento | Janela de coleta limitada e configuração anterior registrada. A implementação atual consulta somente a configuração atual por `profile: -1`; alteração, filtro e restauração permanecem pendentes. |
| ADM-04 | Usuários, papéis, privilégios, senha e grants/revokes | v0.10.0 | 🚧 Em desenvolvimento | Separar autorização de dados e controle Atlas; verificar acesso resultante. A implementação atual consulta usuários e papéis, cria/remove usuários e concede/revoga papéis com confirmação e JSON validado; senha permanece somente em memória, e a validação executável aguarda o executor. |
| ADM-05 | Replica set: saúde, lag, membros, configuração e oplog | Backlog sem versão | 🚧 Em desenvolvimento | Diagnóstico em failover real, sem polling excessivo |
| ADM-06 | Replica set: initiate/reconfig/stepdown/freeze/sync e resize de oplog | Backlog sem versão | 📋 Planejado | Runbook, topologia relida, impacto e privilégio; opções force em fluxo especializado |
| ADM-07 | Sharding: shards, chunks, zones, distribuição, balancer e shard key | Backlog sem versão | 📋 Planejado | Diagnóstico em cluster sharded real; namespace e roteamento corretos |
| ADM-08 | Shard/reshard/unshard/move, refine key, zones e balancer | Backlog sem versão | 📋 Planejado | Disponibilidade por versão; operação longa rastreável sem retry indiscriminado |
| ADM-09 | Validate, compact, collMod, parâmetros e manutenção documentada | v0.10.0 | 🚧 Em desenvolvimento | Plano por comando e topologia; comandos exclusivos de SO encaminhados a runbook. A implementação atual oferece `validate` completo e `compact` para coleções de usuário, ambos com confirmação textual, perfil gravável e auditoria local; parâmetros permanecem pendentes. |
| ADM-10 | Atlas: projetos, clusters, métricas, alertas, usuários, rede, backup e restore | Backlog sem versão | 📋 Planejado | Adaptador HTTP separado, credenciais próprias, paginação e rate limits |
| ADM-11 | Auditoria local de ações e diagnóstico exportável | v0.5.0 | 🚧 Em desenvolvimento | Segredos removidos e retenção limitada; não apresentar como auditoria inviolável. A implementação atual persiste metadados de alterações administrativas selecionadas no LiteDB, retém as 500 entradas mais recentes, permite consulta na interface e exporta até 500 entradas para JSON. |
| ADV-01 | GridFS: buckets, upload/download, metadata, renomear e excluir | Backlog sem versão | 📋 Planejado | Arquivo grande em streaming; cancelamento trata upload parcial |
| ADV-02 | Séries temporais: timeField/metaField, retenção, consultas e limites | Backlog sem versão | 📋 Planejado | Operações proibidas desabilitadas conforme versão; sem CRUD genérico irrestrito |
| ADV-03 | Change streams: watch, filtros, resume token, pre/post images e eventos DDL | Backlog sem versão | 📋 Planejado | Reconexão e token expirado testados; lacunas explicitadas |
| ADV-04 | CSFLE/Queryable Encryption, key vault, KMS, DEKs e rewrap | Backlog sem versão | 📋 Planejado | Dados cifrados não convertidos; dependências e permissões testadas por provedor |
| ADV-05 | Search textual, autocomplete de busca, facets, analyzers, synonyms e highlights | Backlog sem versão | 📋 Planejado | Playground mostra índice, query e resultado; distinguir do autocomplete do editor |
| ADV-06 | Vector Search: dimensões, similaridade, filtros, ANN/ENN e busca híbrida | Backlog sem versão | 📋 Planejado | Validar vetor e índice; recursos recentes entram por capacidade, sem envio externo implícito |
| ADV-07 | Federation, Online Archive, Stream Processing, Charts e conectores de ecossistema | Backlog sem versão | 📋 Planejado | Cada serviço recebe adaptador/runbook e status explícito de suporte |
| ADV-08 | SQL para MQL, geração de dados, análise de schema e migrações versionadas | Backlog sem versão | 🚧 Em desenvolvimento | Subconjunto SQL documentado; migração tem checksum, histórico e falhas parciais |
| ADV-09 | Assistente IA opcional e integrações Git | v0.9.0 (local) / v0.11.0 (MCP/agentes) / v0.12.0 (workflow) / backlog Git | **Removido** o painel local por aba (v0.9.0) em 25/09/2026 ([ADR-055](10-decisoes-arquiteturais.md#adr-055--remoção-do-assistente-ia-por-aba-25092026)); 🚧 Agente IA (v0.11.0) em desenvolvimento; 📋 Planejado (workflow/Git) | Substituído pelo painel **Agente IA**. Pendentes nele: proposta de código com diff e **Aplicar ao editor**/undo, contexto com o conteúdo completo do editor e resposta offline sem modelo; aceite de modelo real fica na Fase 9. **📋 Planejado em 26/09/2026 ([ADR-056](10-decisoes-arquiteturais.md#adr-056--agente-ia-integrado-conversas-persistidas-permissões-por-provider-modos-e-propostas-de-edição-26092026), polimento CLP):** painel global com conversas persistidas (opt-out), permissões persistentes por provider, chips de contexto (arquivo ativo do buffer), modos, tools MongoDB de leitura via MCP para o Claude (assinatura) e propostas de edição revisadas por hunk no editor — cobre as duas primeiras pendências quando entregue; resposta offline sem modelo continua pendente |
| UX-01 | Abas, temas, atalhos, localização, acessibilidade, DPI e virtualização | v0.5.0 | 🚧 Em desenvolvimento | Interface desktop disponível em pt-BR, en, es e zh-CN; pt-BR é o idioma inicial, `en` é o fallback determinístico; maturidade multiplataforma é validada na Fase 9 / v0.13.0 |
| UX-02 | Centro de tarefas, progresso, cancelamento, limites de concorrência | v0.5.0 | 🚧 Em desenvolvimento | Tarefa longa não bloqueia UI; estado incerto distinto de cancelado |
| UX-03 | Empacotamento Windows/Linux, atualização, diagnóstico, SBOM e licenças | v1.0.0 | 🚧 Em desenvolvimento | Implementação e documentação na v1.0.0; instalação limpa e verificação de integridade em ambiente real na Fase 9 / v0.13.0 |

Fontes especializadas: [GridFS](https://www.mongodb.com/docs/drivers/csharp/current/crud/gridfs/), [séries temporais](https://www.mongodb.com/docs/manual/core/timeseries/timeseries-limitations/), [change streams](https://www.mongodb.com/docs/drivers/csharp/current/logging-and-monitoring/change-streams/), [criptografia](https://www.mongodb.com/docs/drivers/csharp/current/security/in-use-encryption/), [Search próprio](https://www.mongodb.com/docs/search/self-managed/current/), [Atlas API](https://www.mongodb.com/docs/atlas/configure-api-access/).

## Aceite da revisão UI/UX — 10/09/2026

O [design system](17-design-system-ui-ux.md) detalha esta entrega dentro dos requisitos existentes, sem declarar toda a cobertura futura deles concluída.

| Requisitos | Incremento implementado | Evidência exigida |
| --- | --- | --- |
| CON-01/08, UX-01 | Modal de conexões, explorer incremental de bancos, busca dos nós carregados | Abertura não executa consulta; alteração do perfil invalida contexto aberto |
| EDT-01/02/04/06, UX-01/02 | Abas independentes com editor textual de consulta/script/agregação; contexto de banco/coleção; saídas inferiores separadas | Resposta fora de ordem e cancelamento não atravessam abas; seleção inválida não dispara texto completo; autocomplete funciona no editor |
| UX-01 | Sistema/claro/escuro, identidade azul/violeta, símbolo e ícones vetoriais, tipografia compacta, atalhos e splitters | Renderização nos dois temas, três tamanhos e escalas; teclado e foco |
| EDT-04, UX-01 | Recuperação de rascunhos e preferências, opt-out geral/por conexão | Reinício sem resultados/conexões; entrada só com opt-in; falha de gravação visível |
| UX-01 | Catálogo de localização, troca em execução e restauração por sessão | Quatro códigos canônicos; chave ausente resolve para `en` ou exibe `[[chave]]`; textos de produto e acessibilidade cobertos sem alterar `docs/**/*.md` |

Registro histórico de 10/09/2026: tabela/árvore, editor com destaque/folding, virtualização e homologação integral de acessibilidade não foram antecipados nesta revisão.


## CON-09 — Ambientes locais e credenciais opcionais

O armazenamento de ambientes **não é um cofre criptográfico**: os valores ficam em JSON puro no LiteDB local. O cofre é requisito de [backlog](backlog/bkl-01-key-vault-criptografico.md).

Implementado nesta revisão: credenciais literais na URI; `${ENV.get("chave")}` opcional; ambientes Development, Staging, Production e customizados; chaves/valores próprios; seleção ativa persistida; chamadas no script e nos valores de consulta/pipeline BSON do editor. Compatibilidade com `${NOME}` e fallback ao processo, sem migração de secrets. Cadastro e ativação pela modal **Ambientes**. Consulte o [guia](14-guia-de-uso.md) para escaping e limites de campos dinâmicos. Cofre nativo/criptografia e homologação MongoDB/mongosh continuam pendentes.

Aceite automatizado: duas URIs coexistem após reinício, ambientes distintos resolvem senhas distintas, caracteres reservados são preservados, operações capturadas sobrevivem à troca e dados ilegíveis não são sobrescritos.


## Incremento Database Explorer — CON-01/08, DAT, EDT, IDX e UX

Implementados: raízes de todos os perfis; bancos/coleções/índices sob demanda; refresh com preservação de nós; menus contextuais; painel de metadados; seleção de membro suportado; documentos em página JSON/estruturada; ações individuais de escrita com releitura/confirmação; geração de scripts CRUD/índices/administração. As ferramentas existentes continuam fornecendo criação de índices, ações de coleção/banco e operações em lote. O incremento não declara toda a matriz de MongoDB/Atlas homologada. Rastreabilidade dos 13 critérios: [guia do explorer](19-database-explorer.md) e [matriz](15-matriz-de-validacao.md).

## Incremento Console — EDT-01/02/04/06, CON, DAT e UX (11/09/2026)

Implementado Console JavaScript com db, getConnection, ConnectionPool, indexadores, ENV, CRUD, agregação, cursores limitados e resultados múltiplos. Conexão/banco selecionáveis por aba; autocomplete assíncrono; seleção/statement por Ctrl+Enter; histórico de execução; integração Explorer sem autoexecução. Escritas passam por confirmação, política de somente leitura e auditoria. [API e limites](20-console.md); [matriz de aceite](15-matriz-de-validacao.md).

O modo Script/mongosh continua separado. Não se anuncia compatibilidade integral com mongosh, explain no Console, transações ou isolamento de processo Jint.

## Incremento EDT — autocomplete local opcional (11/09/2026)

IA local Qwen2.5-Coder via ONNX GenAI adicionada ao autocomplete determinístico: configuração persistente, carregamento lazy, CPU mínimo, FIM e fallback. Prévia preemptiva por editor, Tab/Esc e menu Ctrl+Espaço. Modelos externos e status explícito. Os orçamentos de contexto e geração aceitam sugestões orientadas por VRAM/RAM e modelo, além de livre digitação validada; a seleção preserva valores exatos como `32` e os campos não usam separadores de milhar. Providers acelerados permanecem condicionados a distribuição/homologação específica. [Requisitos e evidência](21-autocomplete-local.md); geração real não é comprovada pelos testes com fakes.

## Incremento EDT-03 — UUID/GUID configurável (11/09/2026)

Implementado: construtores `UUID`, `CGUUID`, `JUUID` e `GUUID` no Console, Agregação, ferramentas BSON, importação e runner mongosh; preferência global e sobrescrita por conexão com prévia das quatro formas; saída humana configurada em Resultados, Documentos, área de transferência, editor de documento, “Abrir no editor” e **Gerar UUID**. Exportação/importação continuam em Extended JSON canônico e dados armazenados não são convertidos. Subtype 3 sem perfil legado é exibido como origem desconhecida. Aceite automatizado por fixtures de bytes independentes, concorrência entre conexões, migração e falha de persistência; homologação com MongoDB/mongosh reais permanece pendente. [Regras](06-editor-bson-e-uuid.md), [ADR-028](10-decisoes-arquiteturais.md) e [matriz](15-matriz-de-validacao.md).

Não fazem parte deste incremento: Python legacy, geração UUID v7, migração de representação e política por campo.

## Incremento EDT/DAT/UX — Resultados em JSON e árvore (11/09/2026)

Implementado: modelo estruturado de resultados (conjunto, posição, JSON original, origem, `_id`, truncamento e projeção); seletor **JSON | Árvore** por aba; JSON formatado sem conversão de tipos; árvore de conjuntos, documentos, objetos e arrays carregada sob demanda, com nome ou índice, tipo e valor exato; avisos textuais de vazio, limitado, projeção parcial, agregação e JSON inválido; menu por documento com botão direito, Shift+F10 e tecla Menu; visualização JSON somente leitura; edição a partir do resultado com releitura antes da escrita, conflito alterado/removido e bloqueio sem `_id`, com projeção ou agregação. A ordem de campos do driver é preservada no Console. Decisão em [ADR-029](10-decisoes-arquiteturais.md); aceite na [matriz](15-matriz-de-validacao.md).

Não fazem parte: tabela tabular, ações de edição por campo, virtualização de árvores muito grandes, persistência de resultados e representação Python legacy dedicada (o valor aparece como subtype 3 de origem desconhecida, com bytes preservados).

## Incremento EDT — autocomplete preditivo (11/09/2026)

Ghost text no cursor e aceitação incremental por Tab substituem a prévia abaixo do editor. Dicionário prioritário, Context Builder limitado com Input opcional, campos temporários dos resultados, histórico recente e nomes conhecidos da aba. Configurações independentes; fallback sem modelo; geração local automática após pausa. Usa `ConnectionPool` e a API real do Console. [Contrato e testes](21-autocomplete-local.md).

## Incremento EDT — chat IA revisável do Console (12/09/2026) — removido em 25/09/2026

> **Removido (P7-L06-CLEANUP, [ADR-055](10-decisoes-arquiteturais.md#adr-055--remoção-do-assistente-ia-por-aba-25092026)).** O painel foi substituído pelo **Agente IA** (botão na barra superior, Ctrl+Shift+A); o editor ocupa a largura liberada. Funcionalidade que **não** existe no Agente IA e fica pendente: (1) proposta de substituição do editor com diff, validação de sintaxe, **Aplicar ao editor** com undo e confirmação adicional para escrita/destrutiva; (2) envio do conteúdo completo do editor (o Agente IA compartilha só metadados ou a seleção); (3) resposta sem modelo — o baseline determinístico de filtro/limite foi removido e o provider local (`LocalAgentProvider`) exige modelo FIM válido e anuncia apenas `CodeProposals` (Chat/Streaming falsos); (4) os opt-ins "contexto da aba no modelo local", "Input JSON" e "contexto por conexão" saíram da interface (preferências e cadastro de conexão); os valores persistidos são preservados sem consumidor, sem migração, até uma decisão de produto. O texto abaixo é histórico.

Painel fixo à direita, por aba, com histórico, contexto estruturado, estados de carregamento/erro/cancelamento/ausência de contexto/resposta vazia e proposta com explicação e diff. O chat não substitui o autocomplete preditivo. A proposta só entra no editor após confirmação explícita; operações de escrita ou destrutivas exigem confirmação adicional. Respostas obsoletas são descartadas, o Explorer não é alterado e nenhuma consulta é executada automaticamente. O contrato `IAiChatService` mantém o provedor substituível e o baseline local cobre o exemplo de filtro por data/limite.


## Incremento implementado — syntax highlighting MongoDB

JSON, Extended JSON, scripts MongoDB/DSL, Query API, aggregation e Atlas Search compartilham highlighting no editor, Input, Resultados, documentos e ferramentas. Dark/Light dinâmicos, ghost text, delimitadores e cache por linha incluídos. Classificação visual não valida consultas. O registro original usava TextBox; o checkout atual usa MongoTextEditor/AvaloniaEdit, conforme o inventário. [Especificação e evidência](22-syntax-highlighting.md).

## Incremento EDT-03 — modos de identificador (12/09/2026)

Implementado: modos **Standard · ObjectId + UUID v4** (padrão), **ObjectId · MongoDB ObjectId** e **UUID v4 · BSON subtype 4** em Preferências, com explicação dinâmica e prévia adaptada (seção ObjectId com hex e UUID equivalente; seção UUID com as quatro formas). A representação UUID continua separada e por conexão. Serviço central de detecção, parsing, formatação e conversão ObjectId → UUID alternativa e reversível; `ObjectId("…")` na saída humana de Resultados, Documentos, visualização, editor e cópias, revertido com bytes idênticos em campos BSON, importação e `EJSON.parse`. **Gerar identificador**, **Interpretar**, `_id` de exemplo dos scripts CRUD e os itens **Copiar _id**, **Copiar consulta por _id** e **Copiar UUID equivalente do _id** seguem o modo. Migração sem perda. [Regras](06-editor-bson-e-uuid.md#modo-de-identificador--implementado-em-12092026), [ADR-032](10-decisoes-arquiteturais.md) e [matriz](15-matriz-de-validacao.md).

Não fazem parte: migração de `_id` entre ObjectId e UUID, UUID v7, modo por conexão e busca combinada por ObjectId e UUID equivalente.

## ONNX SlopCoder e chat — 13/09/2026

Revisão implementada: [contrato, uso, distribuições CPU/WinML/CUDA e limites](23-onnx-slopcoder.md). DeepSeek-Coder FIM com tokenizer .NET e manifesto validado; chat e autocomplete compartilham sessão, preservando cancelamento e revisão das propostas. A exportação CPU fornecida foi executada em CPU; a tentativa DirectML falhou na geração e o fallback CPU foi validado. O modelo FIM pode acrescentar alterações não solicitadas no chat; fidelidade conversacional não está homologada.


## Incremento EDT — datas BSON (13/09/2026)

Implementado: construtor Date em UTC na apresentação de resultados/documentos/árvore/cópias e interpretação nas entradas BSON e Console. Scripts recebem helper Date. Exportação canônica preservada; strings que parecem datas não são convertidas. Evidências e limites na matriz de validação.

## Requisito explícito de formatação do MVP

| ID | Recurso e abrangência | Versão de consolidação | Status | Aceite observável |
| --- | --- | --- | --- | --- |
| EDT-08 | Formatação de JSON, MongoDB Query e MongoDB Script | v0.5.0 | ✅ Implementado | Opções → Formatar JSON/query/script formata a seleção ou o editor sem executar; processamento em worker, cancelamento e undo. JSON conserva tokens; query/script usa limites da AST e mantém strings, comentários, regex e templates. Limite de 1 milhão de caracteres. Exemplos e gate no roadmap. |

## Interpretação dos incrementos datados

As seções de incrementos preservam evidência histórica. Seu texto não encerra requisitos amplos nem altera a versão alvo desta tabela. EDT-02 tem base determinística incluída na v0.5.0 e consolidação contextual na v0.6.0; IA opcional é v0.9.0. EDT-04 tem histórico/arquivos existentes, mas parâmetros e snippets completos permanecem pendentes. TRF-01 exige CSV no MVP, enquanto exportação de coleção inteira/streaming fica como extensão sem versão comprometida. Requisitos administrativos e Script/IA já implementados são antecipações mantidas, não novos bloqueadores do MVP.

## Recortes planejados da v0.11.0 — MCP e agentes externos

O [plano da Fase 7](phases/phase-07-v0.11.0/README.md) acrescenta os recortes abaixo sem transformar evidências de IA local ou MongoDB em evidência de MCP. Todos estão **📋 Planejados**. IDs existentes são preservados; a fase detalha contratos, ferramentas, testes e aceites específicos.

| IDs relacionados | Recorte novo | Aceite a implementar |
| --- | --- | --- |
| ADV-09, UX-01/02 | Agent Runtime, sessões, streaming, cancelamento e chat Avalonia por capabilities | UI independente do fornecedor, eventos normalizados, destino fixo e degradação sem serviços externos |
| ADV-09, DAT-01/03/09/10, IDX-01 | MCP e registro único de ferramentas | Cliente externo descobre/executa leituras limitadas; MCP e chat usam a mesma implementação e autorização |
| ADV-09, CON-07 | OpenAI/Codex e Claude por adaptadores; autenticação e cofre de SO | Contas próprias Codex/ChatGPT (7B) e Claude (8B, substituído pelo bloco CL: Claude pelo Claude Code, em código sem homologação desde 25/09/2026) são prioridade condicional planejada; API Key permanece alternativa. Apenas fluxos oficiais e gates por SO; nenhum token copiado ou segredo plaintext |
| CON-07, ADM-11, DAT-04/05/06, IDX-01 | Permissões, aprovações, auditoria e limite de dados externos | Nenhuma escrita sem autorização vinculada à ação; nenhum dado enviado só por conectar um provider; segredos não retornados |
| ADV-09, EDT-02 | Preservação do modo local | Autocomplete/ONNX funcionam offline e sem conta externa; capabilities locais não prometem ferramentas ou autonomia inexistentes |

O workflow de ADV-09 permanece **📋 Planejado na [Fase 8/v0.12.0](phases/phase-08-v0.12.0/README.md)**, com fluxo fechado e ações revisadas. A integração MCP não habilita automaticamente funcionalidades de backlog ou administrativas ocultas.
