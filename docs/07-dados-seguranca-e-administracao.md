# Transferência, segurança e administração

## Formatos e garantias

| Fluxo | Uso | Garantia e limite |
| --- | --- | --- |
| CSV | Análise tabular | Sem tipos BSON completos, arrays e documentos precisam de mapeamento |
| Extended JSON canônico / NDJSON | Troca tipada e revisão | Preserva tipos suportados; não inclui toda a configuração do deployment |
| Exportação de banco da IDE | Coleções, metadados e relatório | Manifesto próprio; consistência deve ser declarada |
| mongodump/mongorestore | Backup/restauração lógica BSON | Usar contratos oficiais e testar restauração |
| Snapshots/PITR gerenciados | Recuperação operacional | API Atlas/Ops Manager ou ferramenta externa; não reproduzir com CSV |

`mongoexport` é ferramenta de intercâmbio, não solução de backup. [mongoexport](https://www.mongodb.com/docs/database-tools/mongoexport/).

## Job de exportação/importação

Exportação exige escolher escopo: resultado completo da consulta, documentos carregados, coleção ou banco. Esses escopos não são intercambiáveis. Aplicar streaming, contrapressão, estimativa de espaço quando disponível, codificação UTF-8, escaping, política para newline/BOM e validação de nomes de arquivos. CSV oferece proteção opcional contra fórmulas ao abrir em planilhas e informa quando ela modifica o conteúdo exportado.

O manifesto versionado registra namespace, origem sem segredo, versões, início/fim, filtros, formato, contagens, checksums, opções, índices, validators, views, erros, itens omitidos e modo de consistência. O resultado só ganha marcador de conclusão depois da finalização e validação; arquivos incompletos mantêm extensão/estado parcial. Não declarar hash de arquivo como prova de consistência lógica.

Importação tem amostra, conversão de tipos, estratégia `insert`, `upsert`, `replace` ou rejeição de duplicados. Padrão: rejeitar duplicados e preservar `_id`. Lotes respeitam limites de bytes e operações. Relatório separa inserido, modificado, ignorado, falhou e resultado incerto. Retomada usa checkpoint e política idempotente; para fontes sem chave segura, disponibilizar recomeço revisado em vez de prometer retomada automática.

Na implementação inicial da IDE, o formato próprio usa explicitamente `upsert` por `_id` para tornar reimportações de fixture idempotentes; não há `--drop`, exclusão implícita ou restauração de metadados. A política de rejeição de duplicados continua sendo uma opção futura para fluxos de migração que exigirem esse comportamento.

## Consistência e restauração

Exportar coleções sequencialmente durante escritas pode produzir visões de instantes diferentes. O manifesto deve registrar esse fato. Um read concern isolado não transforma arbitrariamente um dump multicoleção em backup atômico.

`mongodump --oplog` é opção para dump completo de membro de replica set, com restrições; não combina com filtro por banco/coleção/query e não resolve backup sharded. A restauração correspondente usa oplog replay. Para cluster sharded, usar procedimento coordenado documentado ou serviço de backup. [mongodump](https://www.mongodb.com/docs/database-tools/mongodump/).

Restaurar envolve selecionar destino, conferir versões/FCV e ferramentas, mapear namespaces, mostrar colisões, opções/índices/validadores e efeito de `--drop`. O padrão será destino vazio de teste, sem drop implícito. Não presumir que cada dump inclua usuários, papéis, índices Search ou estado de cluster: inventariar o que o artefato realmente contém. [mongorestore](https://www.mongodb.com/docs/database-tools/mongorestore/).

Aceite: restauração real em ambiente isolado, contagens compatíveis com o modo de consistência, comparação tipada de amostras e conferência de índices e opções. Registrar duração e volume para estimar RTO; RPO depende da estratégia de backup, não da interface.

## Processos externos

Descobrir `mongodump`, `mongorestore`, `mongoexport`, `mongoimport` e `mongosh` por caminho configurado; mostrar versão e compatibilidade. Preferir instalação separada inicialmente, evitando presumir direito de redistribuição. Integrar por APIs de processo com lista de argumentos, sem concatenação em shell.

Senhas não entram em argumentos, URI de log ou relatórios. Usar mecanismos protegidos que cada executável suporta: prompt/entrada adequada ou configuração temporária com permissões restritas. Não assumir que todos aceitam o mesmo protocolo. Se o método seguro não existir naquela combinação, declarar a limitação e orientar configuração suportada, sem fallback silencioso para senha na linha de comando.

Executar sem janela extra quando não interativo, drenar stdout/stderr simultaneamente, impor timeout, registrar exit code e tratar encerramento da árvore de processos. Arquivos temporários são locais, com nomes imprevisíveis e remoção restrita ao diretório da tarefa. Cancelamento não certifica reversão de restauração parcialmente aplicada.

## Credenciais e privacidade

A estratégia vigente permite credenciais diretas na URI ou referências opcionais `${ENV.get("chave")}`. Perfis e valores por ambiente ficam no LiteDB local, sem criptografia por cofre nativo. Esse armazenamento é uma escolha explícita de configuração; não é fallback de um cofre nativo. Integração Windows Credential Manager/DPAPI e Linux Secret Service permanece planejada, assim como credenciais SSH, KMS e Atlas.

O módulo de ambientes usa coleção própria versionada, no mesmo proprietário LiteDB. **Ele não é um cofre criptográfico:** os valores são gravados em JSON puro e o arquivo LiteDB é aberto sem senha. O rótulo "Key Vault" foi retirado da interface, junto com o botão **Ambientes**; o cofre criptográfico é requisito de [backlog](backlog/bkl-01-key-vault-criptografico.md). O armazenamento local permanece em uso porque a resolução de `${ENV.get("chave")}` depende dele. Valores não são copiados para URIs, histórico, auditoria ou rascunhos. O marcador legado `${MONGODB_PASSWORD}` preserva seu contrato de variável do processo e o armazenamento de sessão já existente continua compatível. Nenhum segredo existente é migrado automaticamente. O runner aceita URI com senha, transportada pelo ambiente do processo filho e não por argumentos ou arquivo temporário. Ambientes de processo não oferecem a proteção de um cofre nativo contra processos privilegiados locais.

O [guia de uso](14-guia-de-uso.md) define precedência, escaping de URI, sintaxe JSON/script e limites dos campos dinâmicos.

O histórico de consultas guarda apenas o namespace e as opções Extended JSON, nunca a URI do perfil. A opção **Persistir localmente** pode ser desmarcada para manter filtros somente na sessão da IDE.

Consultas salvas são uma escolha explícita do operador e guardam o filtro e as demais opções no LiteDB, associadas ao perfil, sem URI nem credenciais. Não salve dados sensíveis em filtros quando o workspace não estiver protegido por cofre; a exclusão pela interface remove a entrada local.

O histórico de scripts guarda somente caminhos absolutos de arquivos `.js` e a data de acesso, nunca o conteúdo, URI ou credenciais. A opção **Persistir caminhos** desativa novos registros e esconde a lista na sessão atual.

TLS valida cadeia e hostname por padrão. Opções inseguras, se expostas para laboratório, ficam explícitas e não transitam silenciosamente para perfis duplicados. SSH valida host key; túnel de host único pode falhar quando o driver descobre outros membros. O plano de túnel precisa contemplar resolução/roteamento e certificados de todos os destinos ou declarar conexão direta com limitações.

O log padrão registra metadados de operação, duração e código de erro. Filtros, documentos e dumps de comandos podem conter dados pessoais; captura detalhada será opcional, com retenção e limpeza. `comment` conterá correlation ID, sem dados de negócio. Telemetria externa será opt-in.

O modo somente leitura bloqueia caminhos de escrita conhecidos, incluindo `$out`, `$merge`, imports, ações Atlas e comandos desconhecidos. Scripts JavaScript gerais não podem ser classificados com segurança apenas por busca textual: bloquear sua execução nesse modo ou exigir credencial de servidor efetivamente somente leitura. A proteção da UI não substitui RBAC.

O modo script JavaScript + JSON usa processo externo com permissões do usuário, sem garantia de sandbox. Exibir o contexto inicial e executar apenas a ação solicitada; abrir/importar `.js`, painel JSON ou workspace nunca executa conteúdo automaticamente. Resultados estruturados usam o canal `slop.results`; texto de console não é interpretado como instrução para a IDE. Jobs de script não acessam diretamente o arquivo LiteDB por uma API fornecida pelo app. Rascunhos e parâmetros persistidos seguem as mesmas regras de retenção e proteção do histórico.

## Administração por risco

| Classe | Exemplos | Fluxo de produto |
| --- | --- | --- |
| Leitura | Metadados, métricas, config e explain de planejamento | Executar com orçamento e registrar contexto |
| Leitura potencialmente pesada | executionStats, validate completo, amostragem ampla | Mostrar custo esperado e tarefa cancelável |
| Alteração de dados | Update/delete, import e `$merge` | Prévia concreta e resultado por lote |
| Alteração estrutural/acesso | Índices, validators, usuários e papéis | Comparação antes/depois e verificação |
| Impacto de infraestrutura | Stepdown, reconfig, reshard, compact, FCV, shutdown | Runbook, confirmação de alvo, pré-requisitos e plano de recuperação |

Contagem prévia é informativa e pode mudar antes da execução; apresentar essa limitação. Nenhuma tela usa “simulação” quando realmente executa uma operação com efeitos.

Para operações correntes, preferir `$currentOp` onde suportado e obter permissão apropriada. `killOp` requer rever identidade/servidor antes de agir, pois o contexto pode mudar. Profiling tem custo e pode capturar dados: registrar configuração anterior e oferecer restauração explícita, inclusive após interrupção do app. [Operações correntes](https://www.mongodb.com/docs/manual/reference/operator/aggregation/currentOp/), [performance](https://www.mongodb.com/docs/manual/administration/analyzing-mongodb-performance/).

## Atlas e criptografia

Atlas usa plano de controle HTTP separado das credenciais de banco. Implementar autenticação recomendada e suportada para contas de serviço, tokens, paginação, limites de requisição e operações assíncronas. Nunca distribuir client secret global dentro do desktop. Provisionar cluster, alterar tier, rede, retenção ou restaurar snapshot requer prévia de destino e impacto financeiro/operacional no futuro produto. [Acesso à API Atlas](https://www.mongodb.com/docs/atlas/configure-api-access/).

CSFLE e Queryable Encryption exigem matriz específica de servidor, driver, bibliotecas nativas, licença e KMS. O app distinguirá cifrado opaco, descriptografia autorizada e criação/rotação de chaves. Não oferecer impressão de DEKs em logs nem conversão de binário cifrado pelo editor UUID. Testar AWS/Azure/GCP/KMIP ou chaves locais apenas quando houver ambiente e escopo correspondentes; suporte não testado deve continuar explícito. [Criptografia em uso](https://www.mongodb.com/docs/drivers/csharp/current/security/in-use-encryption/).

## Privacidade dos rascunhos (10/09/2026)

A recuperação automática de texto foi explicitamente escolhida para esta revisão. Os rascunhos são locais e podem conter dados sensíveis digitados pelo usuário; não são um cofre. Desativação global/por conexão e opt-in separado da entrada JSON são aplicados antes de persistir. Snapshots não incluem credenciais do perfil nem resultados. Histórico de consulta, histórico de caminhos e rascunhos possuem preferências independentes.

Alterar URI ou política de um perfil invalida sua árvore aberta e exige reabertura. As ferramentas mantêm confirmações, auditoria e bloqueios existentes. Salvar ambientes também invalida o explorer e exige reabertura; operações já iniciadas conservam seus valores capturados. A integração com cofre nativo permanece planejada.

## Claude e operações externas

O fluxo Claude Code mantém a autenticação no processo oficial e o KapibaraStudio não lê nem armazena credenciais da conta. A aprovação de comandos, escrita e rede não fornece isolamento: o processo executa com os privilégios do usuário. Chamadas bloqueiam por padrão quando a auditoria ou a ponte de aprovação falha. Detalhes em [Claude no KapibaraStudio](phases/phase-07-v0.11.0/README.md).
