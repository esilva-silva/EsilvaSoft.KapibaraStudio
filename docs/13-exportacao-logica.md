# Exportação lógica atual

## Finalidade

A aba **Exportar** produz uma cópia de intercâmbio de um banco MongoDB em Extended JSON canônico. Ela atende inspeção, compartilhamento controlado de dados e a base para uma futura importação. Não substitui `mongodump` em uma rotina de backup ou recuperação operacional.

## Arquivos gerados

Cada execução cria uma pasta exclusiva abaixo do diretório local de dados do aplicativo:

- Windows: `%LOCALAPPDATA%\EsilvaSoft\KapibaraStudio\exports`.
- Linux: `$XDG_DATA_HOME/EsilvaSoft/KapibaraStudio/exports`, ou `~/.local/share/EsilvaSoft/KapibaraStudio/exports` quando `XDG_DATA_HOME` não estiver definido.

A pasta contém:

- `manifest.json`, com versão do formato, banco, data UTC, limite e metadados das coleções;
- `collection-001.extended.json`, `collection-002.extended.json` e assim por diante, um array JSON por coleção.

Os arquivos de coleções são numerados para não usar nomes recebidos do servidor como caminhos locais. O nome real da coleção fica somente no manifesto.

## Fidelidade e limite

Documentos são serializados como Extended JSON canônico pelo driver MongoDB. Isso preserva representações BSON, incluindo `ObjectId`, datas, decimais e UUIDs, no formato de intercâmbio do MongoDB.

O operador configura um limite entre 1 e 1.000.000 documentos por coleção. O serviço busca um documento adicional apenas para detectar truncamento; ele não grava esse documento adicional. O manifesto e a tela exibem quando alguma coleção excedeu o limite. Para exportações sem corte e recuperação, use as Database Tools após a validação prevista na fase correspondente.

## Importação atual

A mesma aba aceita a pasta que contém `manifest.json` e importa no banco selecionado. O caminho pode ser informado manualmente ou escolhido pelo seletor de pasta nativo. O manifesto precisa usar a versão 1 do formato e cada arquivo de coleção deve permanecer dentro da pasta de origem. Documentos são aplicados em lotes de 500 por `upsert` de `_id`: uma importação repetida atualiza o mesmo documento, sem apagar coleções, documentos ausentes ou o banco de destino.

O recurso recusa documentos sem `_id`, arquivos que não sejam arrays Extended JSON, caminhos declarados no manifesto que escapem da pasta de origem, nomes ou arquivos duplicados e divergência entre a contagem declarada e o conteúdo de cada arquivo. Essas verificações ocorrem antes de escrever a coleção correspondente. A importação é uma alteração no servidor de destino e deve ser executada somente contra o banco escolhido pelo operador.

## Limites e próximos passos

O recurso atual não exporta nem restaura índices, validadores, views, usuários, permissões, metadados de cluster, oplog ou transações. Ele não possui seleção gráfica de diretório, relatório de falha parcial, desfazer ou agendamento. Esses itens exigem um fluxo de revisão de destino e homologação de restauração real antes de serem anunciados.

Consulte [07-dados-seguranca-e-administracao.md](07-dados-seguranca-e-administracao.md), [09-plano-de-implementacao.md](09-plano-de-implementacao.md) e [12-acompanhamento-da-implementacao.md](12-acompanhamento-da-implementacao.md) para escopo e acompanhamento.

## Relação com o roadmap — atualizada em 21/09/2026

✅ A exportação de resultados da página carregada em Extended JSON e CSV existe (TRF-01). O contrato de serialização de objetos/arrays dentro das células está na [v0.5.0](09-plano-de-implementacao.md#fase-1--v050-mvp): não há flattening implícito nem promessa de round-trip BSON em CSV.

🚧 O pacote lógico de banco descrito neste documento já possui exportação/importação limitada com manifesto (TRF-02/03), como antecipação de manutenção v0.10.0. 📋 Backup/restauração com Database Tools (TRF-04) fica no backlog sem versão comprometida. Exportar uma página, exportar um pacote lógico e produzir backup operacional são contratos diferentes.


## Exportação de resultados do MVP — JSON/CSV

Separada da transferência lógica de banco descrita acima, **Exportar página…** grava somente a página/conjunto carregado no clique. JSON e CSV são escritos incrementalmente em worker, com token e progresso real por documento. CSV segue o contrato do roadmap (campos de primeiro nível, valores complexos canônicos, UTF-8, vírgula e escaping); prefixos de fórmula em strings e cabeçalhos recebem apóstrofo. Não oferece round-trip BSON em CSV.

Arquivo temporário exclusivo no mesmo diretório, com sufixo `.partial`, só é movido para o destino no sucesso. Cancelamento/falha tenta remover o parcial; falha de limpeza é erro visível. Destino existente nunca é sobrescrito. Nenhuma consulta adicional é executada para exportar a página; exportação integral de coleção não foi acrescentada ao MVP. Testes cobrem escrita antes do término, cancelamento, JSON inválido, destino existente e limpeza.
