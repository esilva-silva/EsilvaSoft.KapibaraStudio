# Transferência lógica de bancos MongoDB

## Finalidade

A transferência lógica exporta e importa um banco MongoDB em um pacote próprio de Extended JSON canônico. A janela `WorkspaceToolsWindow` é aberta por **Mais ações → Ferramentas do banco de dados** quando há uma aba MongoDB conectada. O pacote serve para intercâmbio de dados e não substitui `mongodump`/`mongorestore` em backup ou recuperação operacional. [Revisão da implementação — 08/10/2026](phases/phase-06-v0.10.0/revisao-2026-10-08.md).

## Arquivos gerados

Cada execução cria uma pasta exclusiva abaixo do diretório local de dados do aplicativo:

- Windows: `%LOCALAPPDATA%\EsilvaSoft\KapibaraStudio\exports`.
- Linux: `$XDG_DATA_HOME/EsilvaSoft/KapibaraStudio/exports`, ou `~/.local/share/EsilvaSoft/KapibaraStudio/exports` quando `XDG_DATA_HOME` não estiver definido.

A pasta contém:

- `manifest.json`, com versão do formato, banco, data UTC, limite, coleções, definições de views, metadados sanitizados e, no formato v3, checksum SHA-256 por arquivo de coleção e fotografia de definições;
- `collection-001.extended.json`, `collection-002.extended.json` e assim por diante, cada um contendo um array JSON de documentos Extended JSON de uma coleção.

O leitor aceita os formatos v1, v2 e v3. O v1 contém coleções; o v2 acrescenta definições de views (`viewOn`, pipeline e collation); o v3 preserva as views, acrescenta SHA-256 para cada arquivo de coleção e uma seção opcional `Definitions` com opções de coleção, validators e índices. O v3 também pode conter metadados sanitizados de produtor, origem genérica, namespaces omitidos, versão do servidor e classificação de topologia/FCV (`Available`/`Unavailable`, sem host, URI, usuário ou erro bruto). A captura é best effort; comandos ou permissões indisponíveis aparecem como `Unavailable`. Esses metadados são opcionais na leitura, e pacotes v1 sem metadados e v3 antigos continuam aceitos. `system.*` é omitido da exportação e recusado como namespace de usuário na importação.

Os arquivos de coleções são numerados para não usar nomes recebidos do servidor como caminhos locais. O nome real da coleção fica somente no manifesto.

## Fidelidade e limite

Documentos são serializados como Extended JSON canônico pelo driver MongoDB. O formato preserva os tipos BSON expressos no Extended JSON, incluindo `ObjectId`, datas, decimais e UUIDs. O importador de pacote recebe arrays de documentos Extended JSON por coleção. Há também importação avulsa de array JSON, NDJSON e CSV para uma única coleção nova, com prévia de até 20 linhas, mapeamento explícito de colunas CSV e política de duplicados. O array JSON e o NDJSON preservam os tipos definidos no Extended JSON.

O operador configura um limite entre 1 e 1.000.000 documentos por coleção. O serviço busca um documento adicional apenas para detectar truncamento; ele não grava esse documento adicional. A tela mostra progresso por coleção e atualiza a contagem de documentos em intervalos limitados; a conclusão aparece somente após publicar o manifesto. O manifesto e a tela exibem quando alguma coleção excedeu o limite. Para exportações sem corte e recuperação, use as Database Tools após a validação prevista na fase correspondente.

## Importação atual

O importador recebe a pasta que contém `manifest.json`; cada arquivo declarado deve permanecer dentro dela e ser um array de documentos Extended JSON. A primeira passagem lê os arquivos em streaming, verifica manifesto, caminhos, contagem, formato, tamanho BSON, presença de `_id` e, em v3, o SHA-256 declarado. Em v1/v2, também calcula um digest interno para comparar com a leitura de gravação. Essas validações de origem ocorrem antes do primeiro acesso ao MongoDB. Com a política padrão Reject, o destino precisa estar vazio de namespaces de usuário. Com Upsert explicitamente confirmado, podem ser reutilizadas somente coleções existentes também declaradas no pacote; views e namespaces extras bloqueiam o plano.

Durante a segunda passagem, o importador recalcula o digest e compara contagem e conteúdo com a pré-validação antes de considerar a operação concluída. As escritas usam lotes de até 500 documentos e 4 MiB de BSON; um documento pode formar sozinho um lote maior que 4 MiB, até o limite individual de 16 MiB BSON. Cada documento também tem limite de 32 MiB de Extended JSON. A política padrão rejeita `_id` duplicado no pacote e exige destino vazio. `Upsert` requer confirmação explícita e substitui por inteiro cada documento coincidente pelo `_id`, removendo campos que não existam na origem. Em destino populado, checkpoint/reinício e restauração de definições ficam indisponíveis; falhas podem deixar gravações parciais e não há rollback.

Se a importação falhar depois de criar estruturas, o serviço tenta remover somente as coleções e views criadas por essa execução; coleções preexistentes nunca entram na limpeza. Em Upsert sobre destino populado, uma falha pode deixar documentos substituídos/inseridos parcialmente e o serviço sinaliza esse estado sem remover a coleção. Falha na limpeza também informa que podem restar dados parciais. Cancelamento ou erro depois do envio de um comando não prova rollback; em caso de resultado incerto, confira o destino antes de repetir.

## Limites e próximos passos

Aplicar `Definitions` é opt-in e exige database de destino vazio e confirmação literal do nome. A interface apresenta um plano por definição com status, dependências e colisões conhecidas; esse plano é baseado no manifesto e em nomes de namespaces, não em todas as regras da versão/topologia. A importação cria coleções/opções/validators, grava documentos, cria índices e views dependentes na ordem validada; repete o preflight, relê os objetos e retorna relatório por item. Opções ainda não suportadas falham antes da escrita. Usuários, permissões, topologia/FCV, oplog e transações não são exportados/restaurados. Dados derivados/materialização não são exportados como coleção. A prévia avulsa mostra até 20 linhas e erros por linha/campo. Checkpoints persistem metadados mínimos no LiteDB local, sem caminho da fonte ou credenciais; após falha, a tela revalida fonte/plano/destino e oferece recomeçar do documento zero com confirmação literal, nunca pular lotes incertos. Smoke Mongo revalidou receipt sintético e importou do início; o ciclo Desktop + LiteDB real + Mongo e crash real ainda faltam. A [meta F6-HOM](phases/phase-06-v0.10.0/meta-homologacao-finalizacao.md) registra evidências locais e limites; RBAC/topologias adicionais e a conclusão da fase continuam pendentes.

Consulte [07-dados-seguranca-e-administracao.md](07-dados-seguranca-e-administracao.md), [09-plano-de-implementacao.md](09-plano-de-implementacao.md) e [12-acompanhamento-da-implementacao.md](12-acompanhamento-da-implementacao.md) para escopo e acompanhamento.

## Relação com o roadmap — atualizada em 21/09/2026

✅ A exportação de resultados da página carregada em Extended JSON e CSV existe (TRF-01). O contrato de serialização de objetos/arrays dentro das células está na [v0.5.0](09-plano-de-implementacao.md#fase-1--v050-mvp): não há flattening implícito nem promessa de round-trip BSON em CSV.

🚧 O pacote lógico de banco descrito neste documento possui exportação/importação limitada com manifesto v1/v2/v3 (TRF-02/03), como antecipação de manutenção v0.10.0. 📋 Backup/restauração com Database Tools (TRF-04) fica no backlog sem versão comprometida. Exportar uma página, importar um pacote lógico e produzir backup operacional são contratos diferentes. JSON/CSV na exportação de resultados não significa suporte a esses formatos na importação de banco.


## Exportação de resultados do MVP — JSON/CSV

Separada da transferência lógica de banco descrita acima, **Exportar página…** grava somente a página/conjunto carregado no clique. JSON e CSV são escritos incrementalmente em worker, com token e progresso real por documento. CSV segue o contrato do roadmap (campos de primeiro nível, valores complexos canônicos, UTF-8, vírgula e escaping); prefixos de fórmula em strings e cabeçalhos recebem apóstrofo. Não oferece round-trip BSON em CSV.

Arquivo temporário exclusivo no mesmo diretório, com sufixo `.partial`, só é movido para o destino no sucesso. Cancelamento/falha tenta remover o parcial; falha de limpeza é erro visível. Destino existente nunca é sobrescrito. Nenhuma consulta adicional é executada para exportar a página; exportação integral de coleção não foi acrescentada ao MVP. Testes cobrem escrita antes do término, cancelamento, JSON inválido, destino existente e limpeza.
